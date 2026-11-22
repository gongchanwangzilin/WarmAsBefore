using Microsoft.Maui.Graphics;
using WarmAsBefore.DesignSystem.Worlds;

namespace WarmAsBefore.Drawables;

/// <summary>分支树上的一个存档节点：身份、文案、血缘（ParentId/BranchName）与时间由 VM 注入。</summary>
/// <param name="Id">存档 Id。</param>
/// <param name="Label">存档名，卡片主文案。</param>
/// <param name="BranchName">分支名；非空表示该节点是一条分支的树头/延续，用于聚合泳道。</param>
/// <param name="SavedAt">保存时间（UTC），X 轴归一化依据。</param>
/// <param name="ParentId">父存档 Id；空/指向不存在的档视为根。</param>
/// <param name="CharacterName">角色名，供页面详情条使用（卡片内不画，留给详情条）。</param>
public sealed record SaveTreeNode(
    string Id,
    string Label,
    string? BranchName,
    DateTime SavedAt,
    string? ParentId,
    string CharacterName);

/// <summary>
/// VS Code 式 git graph：时间（X）× 分支泳道（Y）。
///
/// 布局：每个 root 拉一条主干 lane；子节点沿用父 lane，若带非空 BranchName 则优先按分支名聚合到同一条 lane，
/// 否则开新 lane。X 按 SavedAt 归一化到画布宽（左旧右新）并做同 lane 最小间距，窄屏不压字、只把内容向右延伸。
/// 绘制与命中共用同一份 _rects。
///
/// 配色只走 <see cref="GraphPalette"/> 与 <see cref="StyleWorldTokens.Pick"/>：零裸 hex、零新 Token、零 Shadow、零动画。
/// 显隐/高亮只改 IsVisible 之外的绘制参数（这里没有合成层与动画）。
/// </summary>
public sealed class SaveTreeDrawable : IDrawable
{
    // ---- 设计尺寸（固定卡片；窄屏靠最小间距向右延伸，不缩字） ----
    private const float CardW = 92f;
    private const float CardH = 40f;
    private const float LaneGap = 14f;
    private const float LeftPad = 12f;
    private const float RightPad = 12f;
    private const float TopPad = 8f;
    private const float AxisH = 30f;
    private const float MinDx = CardW + 10f;   // 同一泳道相邻卡片的最小水平间距
    private const float LabelFont = 11f;
    private const float BranchFont = 9f;

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
        set { if (_selectedId == value) return; _selectedId = value; Invalidate(); }
    }

    private Action? _invalidate;
    /// <summary>VM 挂 GraphicsView.Invalidate；数据/选中变化后按需重绘。</summary>
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
    private readonly List<(float X, DateTime At)> _ticks = new();

    private float _viewW;
    private float _viewH;

    /// <summary>
    /// 画布需要的总高度 = 轴 + 泳道数 × 行高。泳道只由数据决定、与视口高度无关，
    /// 所以页面照它撑高画布不会和布局互相追着改 —— 存档一多，节点会排到视口外，
    /// 页面必须能滚动到它们（否则「有 40 条分支」等于「只看得到 5 条」）。
    /// </summary>
    public float ContentHeight { get; private set; } = 260f;
    private long _tMin;
    private long _tMax;
    private float _plotX0;
    private float _avail;
    private float _axisY;

    private readonly record struct TreeEdge(string From, string To, bool Fork);

    public void SetCanvasSize(double w, double h)
    {
        if (Math.Abs(_viewW - w) < 0.5 && Math.Abs(_viewH - h) < 0.5) return;
        _viewW = (float)w;
        _viewH = (float)h;
        try { Layout(); } catch { }
    }

    // ==================== 布局 ====================

    private void Layout()
    {
        _rects.Clear();
        _byId.Clear();
        _laneOf.Clear();
        _edges.Clear();
        _ticks.Clear();

        ContentHeight = 260f;
        var nodes = _nodes;
        if (nodes is null || nodes.Count == 0) return;
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

        // 3) 时间范围 + X 轴参数。
        _tMin = nodes[0].SavedAt.Ticks;
        _tMax = _tMin;
        foreach (var n in nodes)
        {
            if (n.SavedAt.Ticks < _tMin) _tMin = n.SavedAt.Ticks;
            if (n.SavedAt.Ticks > _tMax) _tMax = n.SavedAt.Ticks;
        }
        _axisY = TopPad + AxisH - 6f;
        _plotX0 = LeftPad;
        var plotRight = Math.Max(_plotX0 + CardW, _viewW - RightPad);
        _avail = plotRight - _plotX0 - CardW;   // 留出一张卡宽，最右卡不越界

        var xOf = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var n in nodes) xOf[n.Id] = TimeX(n.SavedAt.Ticks);

        // 4) 同一泳道内按 X 推挤，保证最小间距不重叠（只往右推，不缩小间距）。
        var byLane = new Dictionary<int, List<SaveTreeNode>>();
        foreach (var n in nodes)
        {
            var lane = _laneOf[n.Id];
            if (!byLane.TryGetValue(lane, out var list)) byLane[lane] = list = new List<SaveTreeNode>();
            list.Add(n);
        }
        foreach (var kv in byLane)
        {
            var list = kv.Value;
            list.Sort((a, b) =>
            {
                var c = xOf[a.Id].CompareTo(xOf[b.Id]);
                return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
            });
            for (var i = 1; i < list.Count; i++)
            {
                var min = xOf[list[i - 1].Id] + MinDx;
                if (xOf[list[i].Id] < min) xOf[list[i].Id] = min;
            }
        }

        // 5) 卡片矩形。
        var laneH = CardH + LaneGap;
        var top = TopPad + AxisH;
        ContentHeight = Math.Max(260f, top + Math.Max(1, nextLane) * laneH + 8f);
        foreach (var n in nodes)
            _rects[n.Id] = new RectF(xOf[n.Id], top + _laneOf[n.Id] * laneH, CardW, CardH);

        // 6) 血缘边：parent -> child；跨 lane 标记为分叉。
        foreach (var n in nodes)
        {
            if (!string.IsNullOrEmpty(n.ParentId) && _byId.ContainsKey(n.ParentId!))
                _edges.Add(new TreeEdge(n.ParentId!, n.Id, _laneOf[n.ParentId!] != _laneOf[n.Id]));
        }

        // 7) 时间刻度：全区间 5 档，按标签去重。
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 5; i++)
        {
            var t = _tMax > _tMin ? _tMin + (long)Math.Round((double)(_tMax - _tMin) * i / 4.0) : _tMin;
            var at = new DateTime(t, DateTimeKind.Utc).ToLocalTime();
            var label = at.ToString("MM/dd");
            if (!seen.Add(label)) continue;
            _ticks.Add((TimeX(t) + CardW * 0.5f, at));
        }
    }

    private float TimeX(long ticks) =>
        _plotX0 + (_tMax > _tMin ? (float)((double)(ticks - _tMin) / (_tMax - _tMin)) : 0f) * _avail;

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
            if (Math.Abs(_viewW - dirtyRect.Width) > 0.5 || Math.Abs(_viewH - dirtyRect.Height) > 0.5)
            {
                _viewW = dirtyRect.Width;
                _viewH = dirtyRect.Height;
                Layout();
            }

            var ink = StyleWorldTokens.Pick("WarmDark", GraphPalette.Unknown);
            var body = StyleWorldTokens.Pick("WarmBody", GraphPalette.Unknown);

            DrawTimeAxis(canvas, ink, body);

            var nodes = _nodes;
            if (nodes is null || nodes.Count == 0)
            {
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
                canvas.FontSize = 12;
                canvas.FontColor = body.WithAlpha(0.7f);
                canvas.DrawString("还没有存档分支", 0, _axisY + 24, Math.Max(80f, _viewW), 30,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
                return;
            }

            DrawEdges(canvas);   // 先连线
            foreach (var n in nodes)                       // 后节点（盖住线端）
                if (_rects.TryGetValue(n.Id, out var r)) DrawNode(canvas, n, r, ink, body);
        }
        catch { /* 画布兜底由 SafeDrawable 再包一层；这里就地吞掉坏帧 */ }
    }

    private void DrawTimeAxis(ICanvas canvas, Color ink, Color body)
    {
        canvas.StrokeColor = ink.WithAlpha(0.22f);
        canvas.StrokeSize = 1f;
        canvas.DrawLine(_plotX0, _axisY, _plotX0 + _avail + CardW, _axisY);

        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = 9f;
        canvas.FontColor = body.WithAlpha(0.75f);
        foreach (var (x, at) in _ticks)
        {
            canvas.DrawLine(x, _axisY - 3f, x, _axisY + 3f);
            canvas.DrawString(at.ToString("MM/dd HH:mm"), x - 28f, _axisY - 17f, 56f, 14f,
                HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }

    /// <summary>parent→child 二次贝塞尔：主干实线、分叉虚线且换色，照 DrawBranch 的 Quad 分段画法。</summary>
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

        canvas.FillColor = tint.WithAlpha(current ? 0.26f : 0.15f);
        canvas.FillRoundedRectangle(r.X, r.Y, r.Width, r.Height, 6f);

        canvas.StrokeColor = selected
            ? GraphPalette.Tag.WithAlpha(0.95f)
            : current ? GraphPalette.Person.WithAlpha(0.90f) : ink.WithAlpha(0.28f);
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

    /// <summary>画布坐标命中哪枚节点；点不中返回 null。</summary>
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
        catch { }
        return null;
    }

    /// <summary>按 id 找节点，VM 展开详情条时用。</summary>
    public SaveTreeNode? Find(string id)
    {
        try { return _byId.TryGetValue(id, out var n) ? n : null; }
        catch { return null; }
    }
}
