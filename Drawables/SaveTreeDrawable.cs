using Microsoft.Maui.Graphics;
using WarmAsBefore.DesignSystem.Worlds;

namespace WarmAsBefore.Drawables;

/// <summary>
/// 分支树上的一个存档节点：身份、文案、血缘（ParentId/BranchName）与时间由 VM 注入。
/// 详情面板要读的字段（场景/世界/时长/保存时间/父档）也由 VM 先格式化成字符串 ——
/// 绘制层只负责摆位，不认业务字段，两处就不会各写一套格式化。
/// </summary>
/// <param name="Id">存档 Id。</param>
/// <param name="Label">存档名，卡片主文案。</param>
/// <param name="BranchName">分支名；非空表示该节点是一条分支的树头/延续，用于聚合泳道。</param>
/// <param name="SavedAt">保存时间（UTC），列序与时间轴的依据。</param>
/// <param name="ParentId">父存档 Id；空/指向不存在的档视为根。</param>
/// <param name="CharacterName">角色名。</param>
/// <param name="SceneText">场景（详情面板一行）。</param>
/// <param name="WorldText">绑定的世界树（详情面板一行）。</param>
/// <param name="DurationText">游玩时长（详情面板一行）。</param>
/// <param name="SavedAtText">保存时间的本地写法（详情面板一行）。</param>
/// <param name="ParentText">父档名 / 「无（起始存档）」（详情面板一行）。</param>
public sealed record SaveTreeNode(
    string Id,
    string Label,
    string? BranchName,
    DateTime SavedAt,
    string? ParentId,
    string CharacterName,
    string SceneText = "",
    string WorldText = "",
    string DurationText = "",
    string SavedAtText = "",
    string ParentText = "");

/// <summary>
/// VS Code 式 git graph：时间（X）× 分支泳道（Y），**双向可滚**。
///
/// 布局（全部固定像素，不按视口归一化 —— 归一化会让泳道一多就把卡片挤成头发丝）：
/// · 每份存档占一列：<c>x = LeftPad + 列序 × ColumnW</c>，列序 = 按保存时间排序后的下标；
/// · 每条泳道一行：<c>y = TopPad + AxisH + 泳道 × LaneH</c>；
/// · 画布内容尺寸 = 列数 × 列距 + 一列卡宽 + 两侧边距（宽）、轴高 + 泳道数 × 行距（高），
///   页面照这个尺寸撑 GraphicsView，外面套 Orientation=Both 的 ScrollView，
///   于是「右边的卡被裁掉且滚不过去」这件事从根上消失。
///
/// 详情面板画在画布上（不是叠一个 XAML 浮层）：面板位置 = 卡片旁的偏好位，再夹进**当前视口**
/// —— 视口在画布坐标系里就是 [scrollX, scrollX+视口宽] × [scrollY, scrollY+视口高]，
/// 所以横竖滚到哪儿，面板都跟手且不会越出可见区域。
///
/// 配色只走 <see cref="GraphPalette"/> 与 <see cref="StyleWorldTokens.Pick"/>：零裸 hex、零新 Token、零动画。
/// </summary>
public sealed class SaveTreeDrawable : IDrawable
{
    // ---- 设计尺寸（固定；窄屏靠滚动，不缩字） ----
    private const float CardW = 92f;
    private const float CardH = 40f;
    private const float LaneGap = 14f;
    private const float LaneH = CardH + LaneGap;      // 54
    private const float ColumnW = CardW + 12f;        // 104：每档一列，列的固定像素密度
    private const float LeftPad = 12f;
    private const float RightPad = 12f;
    private const float TopPad = 8f;
    private const float AxisH = 30f;
    private const float LabelFont = 11f;
    private const float BranchFont = 9f;

    // ---- 详情面板 ----
    private const float PanelW = 216f;
    private const float PanelPadX = 10f;
    private const float PanelPadY = 9f;
    private const float PanelTitleH = 16f;
    private const float PanelRowH = 13f;
    private const float PanelGap = 10f;      // 面板与卡片之间的缝
    private const float EdgePad = 6f;        // 面板离视口边缘至少留这条缝
    private const int PanelRows = 7;         // 角色/场景/世界/时长/保存/父档/分支
    private const float PanelH = PanelPadY * 2 + PanelTitleH + 4 + PanelRows * PanelRowH;   // 130

    private IReadOnlyList<SaveTreeNode>? _nodes;
    public IReadOnlyList<SaveTreeNode>? Nodes
    {
        get => _nodes;
        set
        {
            try { _nodes = value; Layout(); Invalidate(); }
            catch { /* 绘制层兜底：坏数据只让画布空一帧 */ }
        }
    }

    private string? _currentSlotId;
    /// <summary>当前档（VM 传入）描高亮边 + 状态灯。</summary>
    public string? CurrentSlotId
    {
        get => _currentSlotId;
        set { if (_currentSlotId == value) return; _currentSlotId = value; Invalidate(); }
    }

    private string? _selectedId;
    public string? SelectedId
    {
        get => _selectedId;
        set
        {
            if (_selectedId == value) return;
            _selectedId = value;
            ComputePanel();
            Invalidate();
        }
    }

    private string? _hoverId;
    /// <summary>鼠标停在哪一枚卡片上（null = 没停）。悬停面板优先于选中面板。</summary>
    public string? HoverId
    {
        get => _hoverId;
        set
        {
            if (_hoverId == value) return;
            _hoverId = value;
            ComputePanel();
            Invalidate();
        }
    }

    private Action? _invalidate;
    /// <summary>VM 挂 GraphicsView.Invalidate；数据/选中/滚动变化后按需重绘。</summary>
    public void AttachInvalidate(Action invalidate) => _invalidate = invalidate;
    private void Invalidate()
    {
        try { _invalidate?.Invoke(); } catch { /* 兜底自己绝不能成为新的异常源 */ }
    }

    // 绘制与命中共用的矩形 / 索引
    private readonly Dictionary<string, RectF> _rects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SaveTreeNode> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _laneOf = new(StringComparer.Ordinal);
    private readonly List<TreeEdge> _edges = new();
    private readonly List<(float X, string Label)> _ticks = new();

    private float _viewW = 320f;
    private float _viewH = 260f;
    private float _scrollX;
    private float _scrollY;

    /// <summary>画布内容宽 = 列数 × 列距（页面照它撑宽 GraphicsView，横滚由外层 ScrollView 承担）。</summary>
    public float ContentWidth { get; private set; } = 260f;
    /// <summary>画布内容高 = 轴 + 泳道数 × 行高（同上，纵向）。</summary>
    public float ContentHeight { get; private set; } = 260f;

    private string? _panelId;
    private RectF _panelRect;

    private readonly record struct TreeEdge(string From, string To, bool Fork);

    /// <summary>可视视口尺寸（= 外层 ScrollView 的尺寸，不是画布内容尺寸）。</summary>
    public void SetViewport(double w, double h)
    {
        if (Math.Abs(_viewW - w) < 0.5 && Math.Abs(_viewH - h) < 0.5) return;
        _viewW = (float)Math.Max(1, w);
        _viewH = (float)Math.Max(1, h);
        ComputePanel();
    }

    /// <summary>滚动位置：面板要按它夹进可见区域，所以每次 Scrolled 都要送进来。</summary>
    public void SetScrollOffset(double x, double y)
    {
        if (Math.Abs(_scrollX - (float)x) < 0.5 && Math.Abs(_scrollY - (float)y) < 0.5) return;
        _scrollX = (float)Math.Max(0, x);
        _scrollY = (float)Math.Max(0, y);
        ComputePanel();
        Invalidate();
    }

    // ==================== 布局 ====================

    private void Layout()
    {
        _rects.Clear();
        _byId.Clear();
        _laneOf.Clear();
        _edges.Clear();
        _ticks.Clear();

        ContentWidth = LeftPad + CardW + RightPad;
        ContentHeight = Math.Max(260f, TopPad + AxisH + LaneH + 8f);

        var nodes = _nodes;
        if (nodes is null || nodes.Count == 0) { ComputePanel(); return; }
        foreach (var n in nodes)
            if (!_byId.ContainsKey(n.Id)) _byId[n.Id] = n;

        // 1) 根：ParentId 为空或指向不存在的档。按 SavedAt 升序分主干 lane（早的根在上）。
        var roots = new List<SaveTreeNode>();
        foreach (var n in nodes)
            if (string.IsNullOrEmpty(n.ParentId) || !_byId.ContainsKey(n.ParentId!))
                roots.Add(n);
        roots.Sort(CompareByTimeThenId);

        var laneByBranch = new Dictionary<string, int>(StringComparer.Ordinal);
        var nextLane = 0;
        foreach (var r in roots)
        {
            _laneOf[r.Id] = nextLane++;
            if (!string.IsNullOrEmpty(r.BranchName))
                laneByBranch.TryAdd(r.BranchName!, _laneOf[r.Id]);
        }

        // 2) 子节点：按时间多遍扫描，父先落位子才能落位。
        //    带非空 BranchName 时优先聚合到同名分支的 lane；否则沿用父 lane。
        var remaining = new List<SaveTreeNode>();
        foreach (var n in nodes)
            if (!_laneOf.ContainsKey(n.Id)) remaining.Add(n);
        remaining.Sort(CompareByTimeThenId);

        while (remaining.Count > 0)
        {
            var deferred = new List<SaveTreeNode>();
            foreach (var n in remaining)
            {
                if (n.ParentId is not null && _laneOf.TryGetValue(n.ParentId, out var parentLane))
                {
                    int lane;
                    if (!string.IsNullOrEmpty(n.BranchName) && laneByBranch.TryGetValue(n.BranchName!, out var bl))
                        lane = bl;
                    else if (!string.IsNullOrEmpty(n.BranchName))
                    {
                        lane = nextLane++;
                        laneByBranch[n.BranchName!] = lane;
                    }
                    else lane = parentLane;

                    _laneOf[n.Id] = lane;
                }
                else deferred.Add(n);
            }

            if (deferred.Count == remaining.Count)
            {
                // 环 / 完全断链：兜底各开一条 lane，保证收敛、绝不死循环。
                foreach (var n in deferred)
                {
                    _laneOf[n.Id] = nextLane++;
                    if (!string.IsNullOrEmpty(n.BranchName))
                        laneByBranch.TryAdd(n.BranchName!, _laneOf[n.Id]);
                }
                break;
            }
            remaining = deferred;
        }

        // 3) 列序：按保存时间排，一份存档占一列 —— 列宽固定，横向密度不随视口/泳道数变化。
        var ordered = new List<SaveTreeNode>(nodes);
        ordered.Sort(CompareByTimeThenId);
        var colOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < ordered.Count; i++) colOf[ordered[i].Id] = i;

        // 4) 卡片矩形。
        var top = TopPad + AxisH;
        var lanes = Math.Max(1, nextLane);
        ContentWidth = LeftPad + (ordered.Count - 1) * ColumnW + CardW + RightPad;
        ContentHeight = Math.Max(260f, top + lanes * LaneH + 8f);
        foreach (var n in ordered)
            _rects[n.Id] = new RectF(LeftPad + colOf[n.Id] * ColumnW, top + _laneOf[n.Id] * LaneH, CardW, CardH);

        // 5) 血缘边：parent -> child；跨 lane 标记为分叉。
        foreach (var n in nodes)
        {
            if (!string.IsNullOrEmpty(n.ParentId) && _byId.ContainsKey(n.ParentId!) && n.ParentId != n.Id)
                _edges.Add(new TreeEdge(n.ParentId!, n.Id, _laneOf[n.ParentId!] != _laneOf[n.Id]));
        }

        // 6) 时间刻度：日期变化点各一枚（第一天必有一枚），免得一行几十个标签叠成一团。
        string? prevDay = null;
        foreach (var n in ordered)
        {
            var at = n.SavedAt.ToLocalTime();
            var day = at.ToString("MM/dd");
            if (day == prevDay) continue;
            prevDay = day;
            _ticks.Add((LeftPad + colOf[n.Id] * ColumnW + CardW * 0.5f, day));
        }

        ComputePanel();
    }

    /// <summary>
    /// 面板落点：优先放在卡片右侧，放不下改放左侧，再整体夹进当前视口。
    /// 夹取公式（画布坐标系，视口 = 滚动偏移 + 视口尺寸，并裁到内容边界内）：
    ///   x = clamp(preferredX, vx0+EdgePad, vx1-PanelW-EdgePad)
    ///   y = clamp(card.Top,  vy0+EdgePad, vy1-PanelH-EdgePad)
    /// 视口比面板还窄时上界会低于下界，所以再取一次 Max —— 此时面板贴左边，宁可裁右也不跑到视口外。
    /// </summary>
    private void ComputePanel()
    {
        _panelId = null;
        _panelRect = default;

        var id = _hoverId ?? _selectedId;
        if (string.IsNullOrEmpty(id) || !_rects.TryGetValue(id, out var card)) return;

        var vx0 = _scrollX;
        var vy0 = _scrollY;
        var vx1 = Math.Min(_scrollX + _viewW, ContentWidth);
        var vy1 = Math.Min(_scrollY + _viewH, ContentHeight);

        var preferRight = card.Right + PanelGap;
        var x = preferRight + PanelW <= vx1 - EdgePad ? preferRight : card.Left - PanelGap - PanelW;
        var maxX = Math.Max(vx0 + EdgePad, vx1 - PanelW - EdgePad);
        x = Math.Clamp(x, vx0 + EdgePad, maxX);

        var maxY = Math.Max(vy0 + EdgePad, vy1 - PanelH - EdgePad);
        var y = Math.Clamp(card.Top, vy0 + EdgePad, maxY);

        _panelId = id;
        _panelRect = new RectF(x, y, PanelW, PanelH);
    }

    private static int CompareByTimeThenId(SaveTreeNode a, SaveTreeNode b)
    {
        var c = a.SavedAt.CompareTo(b.SavedAt);
        return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
    }

    // ==================== 绘制 ====================

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0) return;
        try
        {
            var ink = StyleWorldTokens.Pick("WarmDark", GraphPalette.Unknown);
            var body = StyleWorldTokens.Pick("WarmBody", GraphPalette.Unknown);

            DrawTimeAxis(canvas, ink, body);

            var nodes = _nodes;
            if (nodes is null || nodes.Count == 0)
            {
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
                canvas.FontSize = 12;
                canvas.FontColor = body.WithAlpha(0.7f);
                canvas.DrawString("这份起始存档还没有分支", 0, TopPad + AxisH + 12, Math.Max(140f, _viewW), 30,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
                return;
            }

            DrawEdges(canvas);   // 先连线
            foreach (var n in nodes)                       // 后节点（盖住线端）
                if (_rects.TryGetValue(n.Id, out var r)) DrawNode(canvas, n, r, ink, body);

            DrawPanel(canvas, ink, body);
        }
        catch { /* 画布兜底由 SafeDrawable 再包一层；这里就地吞掉坏帧 */ }
    }

    private void DrawTimeAxis(ICanvas canvas, Color ink, Color body)
    {
        canvas.StrokeColor = ink.WithAlpha(0.22f);
        canvas.StrokeSize = 1f;
        canvas.DrawLine(LeftPad, TopPad + AxisH - 6f, Math.Max(LeftPad, ContentWidth - RightPad), TopPad + AxisH - 6f);

        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = 9f;
        canvas.FontColor = body.WithAlpha(0.75f);
        var y = TopPad + AxisH - 6f;
        foreach (var (x, label) in _ticks)
        {
            canvas.DrawLine(x, y - 3f, x, y + 3f);
            canvas.DrawString(label, x - 22f, y - 17f, 44f, 14f,
                HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }

    /// <summary>parent→child 二次贝塞尔：主干实线、分叉虚线且换色。</summary>
    private void DrawEdges(ICanvas canvas)
    {
        var trunk = GraphPalette.Associated.WithAlpha(0.55f);
        var fork = GraphPalette.Causal.WithAlpha(0.72f);

        foreach (var e in _edges)
        {
            if (!_rects.TryGetValue(e.From, out var a) || !_rects.TryGetValue(e.To, out var b)) continue;

            float x1, y1, x2, y2;
            if (b.Center.X >= a.Center.X)
            {
                x1 = a.Right; y1 = a.Center.Y;
                x2 = Math.Max(b.Left, x1 + 8f); y2 = b.Center.Y;
            }
            else // 时间倒挂的脏数据：从父左边出、进子右边，避免线往回穿卡
            {
                x1 = a.Left; y1 = a.Center.Y;
                x2 = b.Right; y2 = b.Center.Y;
            }

            var p0 = new PointF(x1, y1);
            var p2 = new PointF(x2, y2);
            var ctrl = new PointF(x1 + (x2 - x1) * 0.5f, y1); // 离父水平、弯向子

            canvas.StrokeColor = e.Fork ? fork : trunk;
            canvas.StrokeSize = e.Fork ? 1.6f : 1.2f;
            canvas.StrokeDashPattern = e.Fork ? new float[] { 5f, 4f } : null;

            const int segments = 12;
            for (var i = 0; i < segments; i++)
            {
                var ta = (float)i / segments;
                var tb = (float)(i + 1) / segments;
                var pa = Quad(p0, ctrl, p2, ta);
                var pb = Quad(p0, ctrl, p2, tb);
                canvas.DrawLine(pa.X, pa.Y, pb.X, pb.Y);
            }
            canvas.StrokeDashPattern = null;
        }
    }

    private static PointF Quad(PointF p0, PointF c, PointF p2, float t)
    {
        var u = 1f - t;
        return new PointF(
            u * u * p0.X + 2f * u * t * c.X + t * t * p2.X,
            u * u * p0.Y + 2f * u * t * c.Y + t * t * p2.Y);
    }

    private void DrawNode(ICanvas canvas, SaveTreeNode n, RectF r, Color ink, Color body)
    {
        var lane = _laneOf.TryGetValue(n.Id, out var l) ? l : 0;
        var tint = LaneColor(lane);
        var current = n.Id == CurrentSlotId;
        var selected = n.Id == SelectedId;
        var hovered = n.Id == HoverId;

        canvas.FillColor = tint.WithAlpha(current ? 0.26f : hovered ? 0.22f : 0.15f);
        canvas.FillRoundedRectangle(r.X, r.Y, r.Width, r.Height, 6f);

        canvas.StrokeColor = selected
            ? GraphPalette.Tag.WithAlpha(0.95f)
            : current ? GraphPalette.Person.WithAlpha(0.90f) : ink.WithAlpha(hovered ? 0.5f : 0.28f);
        canvas.StrokeSize = selected ? 2f : current ? 1.6f : 1f;
        canvas.DrawRoundedRectangle(r.X, r.Y, r.Width, r.Height, 6f);

        // 状态灯：当前档亮绿，其余暗点。
        const float lampR = 3f;
        canvas.FillColor = current ? GraphPalette.Tag.WithAlpha(0.95f) : GraphPalette.Unknown.WithAlpha(0.40f);
        canvas.FillCircle(r.X + 7f + lampR, r.Center.Y, lampR);

        var tx = r.X + 7f + lampR * 2f + 4f;
        var tw = Math.Max(8f, r.Right - tx - 4f);

        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontSize = Math.Max(9f, LabelFont);
        canvas.FontColor = current ? tint : ink.WithAlpha(0.92f);
        canvas.DrawString(Fit(n.Label, 8), tx, r.Y + 2f, tw, r.Height * 0.5f,
            HorizontalAlignment.Left, VerticalAlignment.Center);

        if (!string.IsNullOrEmpty(n.BranchName))
        {
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.FontSize = Math.Max(8f, BranchFont);
            canvas.FontColor = body.WithAlpha(0.72f);
            canvas.DrawString("· " + Fit(n.BranchName!, 12), tx, r.Y + r.Height * 0.5f, tw, r.Height * 0.45f,
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }
    }

    /// <summary>
    /// 浮在卡片旁的详情面板：存档名 + 角色/场景/世界/时长/保存时间/父档/分支。
    /// 位置已经在 <see cref="ComputePanel"/> 里夹进视口，这里只管画。
    /// </summary>
    private void DrawPanel(ICanvas canvas, Color ink, Color body)
    {
        if (_panelId is null || !_byId.TryGetValue(_panelId, out var n)) return;

        var r = _panelRect;
        var surface = StyleWorldTokens.Pick("SurfaceBg", GraphPalette.Unknown);
        var edge = StyleWorldTokens.Pick("EdgeStrong", ink.WithAlpha(0.35f));

        // 面板是画在内容里的浮动层：先抹一层底色，底下的连线不会从字里透出来。
        canvas.FillColor = surface.WithAlpha(0.98f);
        canvas.FillRoundedRectangle(r.X, r.Y, r.Width, r.Height, 8f);
        canvas.StrokeColor = edge.WithAlpha(0.55f);
        canvas.StrokeSize = 1f;
        canvas.DrawRoundedRectangle(r.X, r.Y, r.Width, r.Height, 8f);

        var x = r.X + PanelPadX;
        var w = r.Width - PanelPadX * 2;

        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontSize = 11f;
        canvas.FontColor = ink.WithAlpha(0.95f);
        canvas.DrawString(Fit(n.Label, 18), x, r.Y + PanelPadY, w, PanelTitleH,
            HorizontalAlignment.Left, VerticalAlignment.Center);

        var rowY = r.Y + PanelPadY + PanelTitleH + 4;
        var rows = new (string Key, string Value)[]
        {
            ("角色", n.CharacterName),
            ("场景", n.SceneText),
            ("世界", n.WorldText),
            ("时长", n.DurationText),
            ("保存", n.SavedAtText),
            ("父档", n.ParentText),
            ("分支", string.IsNullOrEmpty(n.BranchName) ? "无分支" : n.BranchName!),
        };

        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = 10f;
        for (var i = 0; i < rows.Length; i++)
        {
            var y = rowY + i * PanelRowH;
            canvas.FontColor = body.WithAlpha(0.70f);
            canvas.DrawString(rows[i].Key, x, y, 30f, PanelRowH, HorizontalAlignment.Left, VerticalAlignment.Center);
            canvas.FontColor = ink.WithAlpha(0.88f);
            canvas.DrawString(Fit(string.IsNullOrWhiteSpace(rows[i].Value) ? "—" : rows[i].Value, 22),
                x + 32f, y, w - 32f, PanelRowH, HorizontalAlignment.Left, VerticalAlignment.Center);
        }
    }

    private static Color LaneColor(int lane) => (Math.Abs(lane) % 5) switch
    {
        0 => GraphPalette.Person,
        1 => GraphPalette.Place,
        2 => GraphPalette.Tag,
        3 => GraphPalette.Emotion,
        _ => GraphPalette.Thing
    };

    private static string Fit(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..(max - 1)] + "…";

    // ==================== 命中 / 查询 ====================

    /// <summary>画布坐标（= 内容坐标，已扣掉滚动）命中哪枚节点；点不中返回 null。</summary>
    public string? HitTest(double x, double y)
    {
        try
        {
            foreach (var kv in _rects)
            {
                var r = kv.Value;
                if (x >= r.X && x <= r.Right && y >= r.Y && y <= r.Bottom) return kv.Key;
            }
        }
        catch { /* 故意忽略：命中测试失败返回 null，等于没点中 */ }
        return null;
    }

    /// <summary>按 id 找节点，VM 展开详情时用。</summary>
    public SaveTreeNode? Find(string id)
    {
        try { return _byId.TryGetValue(id, out var n) ? n : null; }
        catch { return null; }
    }
}
