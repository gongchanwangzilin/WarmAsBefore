using Microsoft.Maui.Graphics;
using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.DesignSystem.Worlds;
using WarmAsBefore.Models;

namespace WarmAsBefore.Drawables;

/// <summary>画布上的一个节点（VM 摆好位置后塞进来，drawable 只管画）。</summary>
public sealed record GraphNodeView
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public NodeKind Kind { get; init; } = NodeKind.Word;
    public double X { get; init; }
    public double Y { get; init; }
    public int Depth { get; init; }
    public string? ParentId { get; init; }

    /// <summary>0.05~1：决定这个疙瘩长多大。记忆重要性、被想起次数、共现强度汇成。</summary>
    public double Salience { get; init; } = 0.35;

    /// <summary>今天还剩几分力气（参数模型算出来的 0~1）：淡的枝条就是快要想不起来的。</summary>
    public double Live { get; init; } = 1.0;

    /// <summary>这根枝从何时开始长、长多久（毫秒，相对生长时钟）。</summary>
    public double GrowAtMs { get; init; }

    /// <summary>指向的原物：记忆条目 Id / 图片相对路径。</summary>
    public string Ref { get; init; } = "";

    public string Source { get; init; } = "auto";

    /// <summary>它属于哪一片（布局算出来的）：「只看这一片」和片子的底色都靠它。</summary>
    public string NetId { get; init; } = "";

    /// <summary>这件事的情绪配比（只有记忆节点有）：画成卡片边上一只小饼，说得清「几分几分」。</summary>
    public IReadOnlyList<EmotionPart>? Emotion { get; init; }
}

/// <summary>画布上的一条联系。</summary>
public sealed record GraphEdgeView
{
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public RelationKind Kind { get; init; } = RelationKind.Associated;
    public double Weight { get; init; } = 1;

    /// <summary>这条边今天的强度 0~1（饱和 + 遗忘一起算完之后的）。</summary>
    public double Live { get; init; } = 1.0;

    public string Source { get; init; } = "auto";

    /// <summary>是不是布局树上的枝；false = 跨枝缠绕的「藤」，画得含蓄一点。</summary>
    public bool IsTree { get; init; }
}

/// <summary>
/// 关系网的枝条渲染：一张会自己长出来的网。
///
/// 观感怎么来的（都是这几件事）：
/// · <b>光</b>：底子上先压一层极淡的暗，再从几片网的重心往外叠出一团暖光 ——
///   网浮在有明暗的纸上，而不是贴在一张同色的平面上；
/// · <b>生长</b>：每根枝按它离焦点的层数错开起跑时间，二次贝塞尔被 de Casteljau 截到 t，
///   于是看得见的不是「淡入」，而是<b>梢头往前伸</b>；伸到位时芽苞弹一下（overshoot 后回落），
///   枝条从粗到细一路收下去，生长中的枝从根部到梢头一路变亮（不是整根一起亮起来），像真长出来的小枝；
/// · <b>活着</b>：静止后每个节点仍在按 Id 的固定相位轻轻摆，越深的梢摆得越多 —— 网不停，
///   但幅度小到不会干扰阅读；几片地的底子按 <see cref="MotionTokens.StaggerMs"/> 次第亮起；
/// · <b>景深</b>：选中一个点时，它自己发一圈暖光、直接牵着的枝仍保持全浓，其余整片降浓并往纸色里退 ——
///   远处的东西是被<b>推远</b>的，不是被藏起来的（藏起来会让人以为网断了）；
/// · <b>遗忘</b>：枝条的粗细与明暗跟着参数模型的 Live 与强度走，久不想起的联系会真的褪成浅色，
///   点上去还能把它重新接粗。
///
/// 颜色不在这里：一律读 <see cref="GraphPalette"/>，页面图例读的也是它，两边不可能再长歪。
///
/// 和 <see cref="MapCanvasDrawable"/> 一样：**不用 canvas 的平移缩放**，世界坐标自己投影成屏幕坐标，
/// 免得各平台的 clip/transform 行为不一致；坐标变换只有一份（<see cref="WorldToScreen"/>）。
/// </summary>
public sealed class MemoryGraphDrawable : IDrawable
{
    public const double GrowMs = 620;

    private IReadOnlyList<GraphNodeView>? _nodes;

    public IReadOnlyList<GraphNodeView>? Nodes
    {
        get => _nodes;
        set { _nodes = value; RebuildNodes(); }
    }

    private IReadOnlyList<GraphEdgeView>? _edges;

    public IReadOnlyList<GraphEdgeView>? Edges
    {
        get => _edges;
        set { _edges = value; RebuildEdges(); }
    }

    // Find/EdgeTo 每帧每条边都要查一次：线性扫的话，四百个点乘一千五百条边就是每帧六十万次比较。
    // 索引在赋值时重建一次，画面才跑得动。
    private readonly Dictionary<string, GraphNodeView> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GraphEdgeView> _edgeOf = new(StringComparer.Ordinal);

    private void RebuildNodes()
    {
        _byId.Clear();
        if (_nodes is null) return;
        foreach (var n in _nodes) _byId[n.Id] = n;
    }

    private void RebuildEdges()
    {
        _edgeOf.Clear();
        if (_edges is null) return;
        foreach (var e in _edges)
        {
            if (!e.IsTree) continue;
            _edgeOf[e.To] = e;
            if (!_edgeOf.ContainsKey(e.From)) _edgeOf[e.From] = e;
        }
    }

    /// <summary>几片网各自的地盘（底子和片名画在这上面）。没有 = 还没摊开。</summary>
    public IReadOnlyList<NetPlace>? Nets { get; set; }

    /// <summary>「就是它」的标记点：跳到它／从它开始看的那个。只多画一圈，不改变网的结构。</summary>
    public string? MarkId { get; set; }
    public string? SelectedId { get; set; }

    /// <summary>只看某一片：其余片退成背景（极低透明度），点错了也不丢上下文。</summary>
    public string? OnlyNetId { get; set; }

    /// <summary>「连着谁」模式的起点：从它到鼠标拉一条虚线，点第二个节点就建联系。</summary>
    public string? LinkFromId { get; set; }

    public PointF? Mouse { get; set; }

    /// <summary>生长时钟（毫秒）。0 = 刚刚重新开始长。</summary>
    public double ElapsedMs { get; set; }

    /// <summary>是否已经全部长完（true 后摆动照旧，但不再算生长）。</summary>
    public bool Grown { get; set; }

    // ---- 配色：每帧向世界要一次 ----
    // 这四个数以前由 VM 在 OnShown 塞进来一次，塞的是那一刻的世界：页面开着的时候换风格世界
    // （或系统翻了深浅），图例早就跟着 DynamicResource 走了，画布却留在上一个世界上 ——
    // 同一张网的两种底色，看着就是 bug。自绘画布本来就每帧重画，那就每帧现取。
    // 下面这四个可赋值的属性退成「引擎还没下发时的兜底」，VM 仍然会写它（首帧之前给个像样的值）。
    public Color CanvasBg { get; set; } = Color.FromArgb("#FBF3E8");
    public Color Ink { get; set; } = Color.FromArgb("#5D4A3A");
    public Color Body { get; set; } = Color.FromArgb("#4A3D2C");
    public Color Glow { get; set; } = Color.FromArgb("#F5D8B8");

    // 这一帧真正用的：底 / 硬墨 / 正文墨 / 暖光。初值与上面那份兜底一致（Draw 之前也可能被读到）。
    private Color _bg = Color.FromArgb("#FBF3E8"), _ink = Color.FromArgb("#5D4A3A"),
        _body = Color.FromArgb("#4A3D2C"), _glow = Color.FromArgb("#F5D8B8");

    private void SyncWorld()
    {
        _bg = StyleWorldTokens.Pick("PageBg", CanvasBg);
        _ink = StyleWorldTokens.Pick("WarmDark", Ink);
        _body = StyleWorldTokens.Pick("WarmBody", Body);
        _glow = StyleWorldTokens.Pick("AccentWarm", Glow);
    }

    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double Scale { get; set; } = 1.0;

    /// <summary>疙瘩画多大：跟着缩放走，但缩到 0.55 就停 —— 再小就成了噪点。</summary>
    private float Glyph => (float)Math.Clamp(Scale, 0.55, 1.35);

    public Point WorldToScreen(Point w) => new(w.X * Scale + OffsetX, w.Y * Scale + OffsetY);
    public Point ScreenToWorld(Point s) => new((s.X - OffsetX) / Scale, (s.Y - OffsetY) / Scale);

    private Action? _onInvalidate;
    public void AttachInvalidate(Action a) => _onInvalidate = a;
    public void NeedRedraw() => _onInvalidate?.Invoke();

    private Func<GraphNodeView, string?>? _thumbResolver;
    public void AttachThumbResolver(Func<GraphNodeView, string?> resolver) => _thumbResolver = resolver;

    private readonly Dictionary<string, Microsoft.Maui.Graphics.IImage?> _thumbCache = new(StringComparer.Ordinal);

    // ==================== 一帧 ====================

    // 这一帧看得见的范围（世界坐标）。屏幕外的点既不画也不算 —— 一张几百点的网，
    // 塞进窗口后大半都在外面，每帧都算它就是白烧电。
    // 初值是「整片天地」：第一帧之前命中测试也要能用。
    private double _vl = double.NegativeInfinity, _vt = double.NegativeInfinity,
        _vr = double.PositiveInfinity, _vb = double.PositiveInfinity;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        SyncWorld();
        canvas.FillColor = _bg;
        canvas.FillRectangle(dirtyRect);
        DrawAmbientLight(canvas, dirtyRect);

        var nodes = Nodes;
        if (nodes is null || nodes.Count == 0)
        {
            canvas.FontSize = 14;
            canvas.FontColor = _ink.WithAlpha(0.55f);
            canvas.DrawString("这张网还是空的 —— 聊几句，TA 脑子里的枝就会自己长出来。",
                dirtyRect.Width * 0.1f, dirtyRect.Height * 0.42f, dirtyRect.Width * 0.8f, 40,
                HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

        SetVisible(dirtyRect);
        EnsureFocus();
        DrawNetBeds(canvas);

        var edges = Edges;
        if (edges is not null)
        {
            foreach (var e in edges)
            {
                if (e.IsTree) continue;
                var a = Find(e.From);
                var b = Find(e.To);
                if (a is null || b is null) continue;
                if (!Show(a) || !Show(b)) continue;
                if (!GrownOrGrowing(a) || !GrownOrGrowing(b)) continue;
                if (!BoxInWorld(a, b)) continue;
                DrawVine(canvas, e, a, b);
            }
            foreach (var n in nodes)
            {
                if (n.Depth <= 0 || n.ParentId is null) continue;
                if (!Show(n)) continue;
                if (!InView(n.X, n.Y)) continue;
                DrawBranch(canvas, n);
            }
        }

        foreach (var n in nodes)
        {
            if (!Show(n)) continue;
            if (!InView(n.X, n.Y)) continue;
            DrawNode(canvas, n);
        }

        if (LinkFromId is not null && Mouse is { } m && Find(LinkFromId) is { } src)
            DrawLinkRubber(canvas, src, m);
    }

    /// <summary>这一点这一帧到底画不画：屏外的不算，被「只看这一片」筛掉的也不算。</summary>
    private bool Show(GraphNodeView n)
        => OnlyNetId is null || n.NetId == OnlyNetId || n.Id == SelectedId;

    // ==================== 光 ====================

    /// <summary>
    /// 底子上的环境光：先整体压一层极淡的暗，再从「几片网的重心」往外叠一团暖光。
    /// 以前画布就是一张平色 —— 网再软也是贴在一板上，看着就死。叠层而不是渐变，
    /// 因为 ICanvas 各家后端的径向渐变行为不一致（这片地盘的画片也是这么叠出来的）。
    /// 光心跟着片子走、还跟着选中的点偏一点：整张图因此是「活的」，不是每帧同一张底。
    /// </summary>
    private void DrawAmbientLight(ICanvas canvas, RectF r)
    {
        var w = r.Width;
        var h = r.Height;
        if (w < 8 || h < 8) return;

        // 底子上先压一层极淡的暗，才有「一张纸」而不是「一块发光的板」。
        // 深档这个数必须小得多：地上已经是沉的、墨是提亮的，压重了整张画布就比页面亮一档，
        // 而 GraphPalette 锚的是页面底 —— 所有点都会跟着错位。
        canvas.FillColor = _ink.WithAlpha(StyleWorldTokens.Dark ? 0.018f : 0.045f);
        canvas.FillRectangle(r);

        var cx = w * 0.5f;
        var cy = h * 0.46f;
        var nets = Nets;
        if (nets is { Count: > 0 })
        {
            double sx = 0, sy = 0, sw = 0;
            foreach (var net in nets)
            {
                var p = WorldToScreen(new Point(net.X, net.Y));
                var k = Math.Max(1, net.Count);
                sx += p.X * k; sy += p.Y * k; sw += k;
            }
            if (sw > 0)
            {
                cx = (float)(sx / sw);
                cy = (float)(sy / sw);
            }
        }
        if (SelectedId is { Length: > 0 } sel && Find(sel) is { } sn)
        {
            var p = WorldToScreen(new Point(sn.X, sn.Y));
            // 只挪四分之一：光跟着你看的东西稍微偏一点就够了，全跟过去就成了聚光灯剧场
            cx += (float)(p.X - cx) * 0.25f;
            cy += (float)(p.Y - cy) * 0.25f;
        }

        var t = ElapsedMs / 1000.0;
        var breathe = 1f + (float)Math.Sin(t * 0.18) * 0.03f;
        var spread = Math.Max(w, h) * 0.62f * breathe;
        for (var i = 5; i >= 1; i--)
        {
            var k = spread * i / 5f;
            canvas.FillColor = _glow.WithAlpha(0.018f + 0.020f * (5 - i));
            canvas.FillEllipse(cx - k, cy - k * 0.88f, k * 2, k * 1.76f);
        }
    }

    // ==================== 景深 ====================

    private readonly HashSet<string> _inFocus = new(StringComparer.Ordinal);
    private string? _focusFor;
    private string? _focusNet;
    private IReadOnlyList<GraphEdgeView>? _focusEdges;

    /// <summary>有没有人在「看这一点」：只有选中才算，「就是它」那圈标记不当焦点 —— 这张网本来就没有中心。</summary>
    private bool HasFocus => SelectedId is { Length: > 0 } && _inFocus.Count > 0;

    /// <summary>
    /// 重算一次「谁留在景深里」：选中点自己 + 直接牵着它的邻居（连片名、连它那片底子一起算）。
    /// 只在选中变了或边换了那份列表时算一次 —— 每帧扫一遍一千五百条边是白烧。
    /// </summary>
    private void EnsureFocus()
    {
        var sel = SelectedId;
        if (sel is null || sel.Length == 0)
        {
            if (_focusFor is null) return;
            _focusFor = null;
            _focusNet = null;
            _inFocus.Clear();
            return;
        }
        if (ReferenceEquals(_focusEdges, Edges) && _focusFor == sel) return;
        _focusFor = sel;
        _focusEdges = Edges;
        _inFocus.Clear();
        _inFocus.Add(sel);
        _focusNet = Find(sel)?.NetId;
        if (Edges is null) return;
        foreach (var e in Edges)
        {
            if (e.From == sel) _inFocus.Add(e.To);
            else if (e.To == sel) _inFocus.Add(e.From);
        }
    }

    /// <summary>这个点该留多实：在景深里 = 1，外面 = 退到 <see cref="GraphPalette.OutOfFocusAlpha"/>。</summary>
    private float Dof(string id) => HasFocus && !_inFocus.Contains(id) ? GraphPalette.OutOfFocusAlpha : 1f;

    /// <summary>退到景深外的颜色顺手往地面里拉一点：只降不透明度会显得「脏」，降饱和才是「远」。</summary>
    private Color DofColor(Color c, float dof)
        => dof >= 1f ? c : GraphPalette.Blend(c, _bg, GraphPalette.OutOfFocusBlend * (1f - dof)).WithAlpha(c.Alpha);

    private void SetVisible(RectF dirtyRect)
    {
        var inv = 1.0 / Math.Max(0.05, Scale);
        const double pad = 130;    // 屏幕外再画 130 像素：枝尖刚探出边时不该看见它断在半路
        var l = dirtyRect.Width > 40 ? dirtyRect.Left : 0;
        var t = dirtyRect.Height > 40 ? dirtyRect.Top : 0;
        var r = Math.Max(dirtyRect.Right, _viewW);
        var b = Math.Max(dirtyRect.Bottom, _viewH);
        _vl = (l - pad - OffsetX) * inv;
        _vt = (t - pad - OffsetY) * inv;
        _vr = (r + pad - OffsetX) * inv;
        _vb = (b + pad - OffsetY) * inv;
    }

    private double _viewW, _viewH;

    /// <summary>VM 把画布尺寸告诉 drawable：dirtyRect 各平台给得不一样，光靠它没法判断「在不在屏内」。</summary>
    public void SetCanvasSize(double w, double h)
    {
        _viewW = w;
        _viewH = h;
    }

    private bool InView(double x, double y) => x >= _vl && x <= _vr && y >= _vt && y <= _vb;

    /// <summary>藤的包围盒（两端点 + 弯出去的那段）够不够得着视野：够不着就整条不画。</summary>
    private bool BoxInWorld(GraphNodeView a, GraphNodeView b)
    {
        var ctrl = ControlWorld(a, b);
        var l = Math.Min(Math.Min(a.X, b.X), ctrl.X) - 24;
        var r = Math.Max(Math.Max(a.X, b.X), ctrl.X) + 24;
        var t = Math.Min(Math.Min(a.Y, b.Y), ctrl.Y) - 24;
        var btm = Math.Max(Math.Max(a.Y, b.Y), ctrl.Y) + 24;
        return r >= _vl && l <= _vr && btm >= _vt && t <= _vb;
    }

    private Point ControlWorld(GraphNodeView a, GraphNodeView b)
    {
        var mx = (a.X + b.X) / 2;
        var my = (a.Y + b.Y) / 2;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var hash = RelationLayout.StableHash(a.Id + b.Id);
        var bend = RelationLayout.Normalized(hash, 3) * 0.34 - 0.06;
        var sign = (hash & 1) == 0 ? 1.0 : -1.0;
        return new Point(mx - dy * bend * sign, my + dx * bend * sign);
    }

    // ==================== 几片地盘 ====================

    /// <summary>
    /// 每片底下垫一层晕开的色、外沿一圈极淡的环，片名贴在片子上沿。
    /// 没有这层，多张网就是一团没有边界的线头；有了它，「海边那晚」和「考试周」才看得出是两件事。
    /// 几片地不是同时亮起的：按 <see cref="MotionTokens.StaggerMs"/> 次第浮出来，
    /// 「网长开了」因此是一-wave 一-wave 的事，而不是一帧之内啪一下全铺好。
    /// </summary>
    private void DrawNetBeds(ICanvas canvas)
    {
        var nets = Nets;
        if (nets is null || nets.Count == 0) return;
        var t = ElapsedMs / 1000.0;

        for (var i = 0; i < nets.Count; i++)
        {
            var net = nets[i];
            if (!InView(net.X - net.Radius, net.Y - net.Radius) &&
                !InView(net.X + net.Radius, net.Y + NetPlaceHeight(net)) &&
                !InView(net.X - net.Radius, net.Y + net.Radius) &&
                !InView(net.X + net.Radius, net.Y - net.Radius) &&
                !InView(net.X, net.Y)) continue;

            // 次第亮起：每片比上一片晚 StaggerMs 起跑（长完之后 appear 恒为 1，不占任何计算）
            var appear = Grown ? 1f
                : (float)MotionTokens.OutCubic(Math.Clamp((ElapsedMs - i * (double)MotionTokens.StaggerMs) / (GrowMs * 1.4), 0, 1));
            if (appear < 0.02f) continue;

            var c = WorldToScreen(new Point(net.X, net.Y));
            var solo = OnlyNetId == net.Id;
            var faded = OnlyNetId is not null && !solo;
            // 选中一个点时，不是它那片的地子再往后退半步（底子跟着景深走，不然前景是虚的、背景还是实的）
            var outOfFocus = HasFocus && _focusNet is not null && net.Id != _focusNet;
            var breathe = 1 + Math.Sin(t * 0.5 + RelationLayout.StableHash(net.Id) % 100) * 0.012;
            var rr = net.Radius * Scale * breathe;
            if (rr < 6) continue;
            var lift = appear * (outOfFocus ? 0.42f : 1f);

            // 三层同心晕：ICanvas 没有径向渐变，一层层叠出来更省也更稳
            for (var k3 = 3; k3 >= 1; k3--)
            {
                var k = rr * (0.55f + 0.45f * k3 / 3f);
                canvas.FillColor = _glow.WithAlpha((float)((faded ? 0.04 : solo ? 0.20 : 0.13) * k3 / 3f + 0.03f) * lift);
                canvas.FillEllipse((float)(c.X - k), (float)(c.Y - k * 0.94f), (float)(k * 2), (float)(k * 1.88f));
            }

            canvas.StrokeColor = _ink.WithAlpha((faded ? 0.05f : solo ? 0.20f : 0.11f) * lift);
            canvas.StrokeSize = 1;
            canvas.DrawEllipse((float)(c.X - rr), (float)(c.Y - rr * 0.94f), (float)(rr * 2), (float)(rr * 1.88f));

            if (Scale < 0.34 || rr < 40 || outOfFocus) continue;
            DrawNetName(canvas, net, c, rr, faded, solo, appear);
        }
    }

    private static double NetPlaceHeight(NetPlace n) => n.Radius * 0.94;

    private void DrawNetName(ICanvas canvas, NetPlace net, Point c, double rr, bool faded, bool solo, float appear)
    {
        var fs = Math.Clamp((float)(10.5 + 2.5 * Math.Min(1.4, rr / 260)) * (float)Math.Clamp(Scale, 0.8, 1.25), 9f, 17f);
        var label = Fit(net.IsLoose ? net.Name : net.Name + " 这一片", 12);
        var w = Math.Max(fs * 3.2f, label.Length * fs * 1.1f);
        var h = fs * 1.6f;
        var y = c.Y - rr * 0.94 - h - 3;
        if (y < 2) y = c.Y - rr * 0.94 + 3;

        canvas.FillColor = _bg.WithAlpha((faded ? 0.45f : 0.82f) * appear);
        canvas.FillRoundedRectangle((float)(c.X - w / 2), (float)y, w, h, h * 0.42f);
        canvas.StrokeColor = _ink.WithAlpha((faded ? 0.06f : solo ? 0.28f : 0.14f) * appear);
        canvas.StrokeSize = 1;
        canvas.DrawRoundedRectangle((float)(c.X - w / 2), (float)y, w, h, h * 0.42f);

        canvas.Font = solo ? Microsoft.Maui.Graphics.Font.DefaultBold : Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = fs;
        canvas.FontColor = _ink.WithAlpha((faded ? 0.25f : solo ? 1f : 0.72f) * appear);
        canvas.DrawString(label, (float)(c.X - w / 2), (float)y, w, h, HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    // ==================== 枝条 ====================

    private void DrawBranch(ICanvas canvas, GraphNodeView child)
    {
        if (child.ParentId is null) return;
        var parent = Find(child.ParentId);
        if (parent is null) return;

        var t = (float)Math.Clamp(Progress(child), 0, 1);
        if (t <= 0.001f) return;

        var p0 = Screen(parent);
        var p2 = Screen(child, t);
        var ctrl = Control(p0, p2, child.Id);

        var edge = EdgeTo(child);
        var kind = edge?.Kind ?? RelationKind.Associated;
        var live = edge?.Live ?? child.Live;
        var dof = Math.Min(Dof(child.Id), Dof(child.ParentId));
        var tint = DofColor(GraphPalette.Edge(kind), dof);

        // 粗到细 + 随强度变粗：梢头永远比根部细，这是「枝」而不是「线」的关键；
        // 而「牵得紧不紧」（边权）也得看得出来，不然一片里几十根枝全是同一根头发丝。
        var grip = Math.Clamp((edge?.Weight ?? 4) / 8.0, 0.2, 1);
        var rootW = (float)((1.1 + 3.0 * child.Salience + 2.6 * grip * (0.45 + 0.55 * live)) * Math.Max(0.5, Scale));
        var tipW = Math.Max(0.6f, rootW * 0.26f);

        var segments = 10;
        var alpha = GraphPalette.BranchAlpha(live) * dof;
        if (!Grown) alpha *= (float) Math.Clamp(t * 1.6, 0.4, 1.0);
        for (var i = 0; i < segments; i++)
        {
            var a = (float)i / segments * t;
            var b = (float)(i + 1) / segments * t;
            var pa = Quad(p0, ctrl, p2, a);
            var pb = Quad(p0, ctrl, p2, b);
            canvas.StrokeSize = (float)(rootW + (tipW - rootW) * b);
            // 正在长的那几帧：根部淡、梢头实 —— 一眼看得出力气正往哪儿走，而不是整根一起亮
            var ramp = Grown ? 1f : (float)(0.72 + 0.28 * (t > 0.001 ? b / t : 1));
            canvas.StrokeColor = tint.WithAlpha((float)alpha * ramp);
            canvas.DrawLine(pa.X, pa.Y, pb.X, pb.Y);
        }

        if (kind == RelationKind.Causal && t >= 1) DrawArrowHead(canvas, ctrl, p2, tint);

        // 正在伸的梢头：一团亮点，让人看得见「还在长」
        if (t < 1)
        {
            var tip = Quad(p0, ctrl, p2, t);
            var r = (2.6f + 1.8f * (float)Math.Max(0.4, Scale)) * (1 - 0.35f * t);
            canvas.FillColor = tint.WithAlpha(0.28f);
            canvas.FillCircle(tip.X, tip.Y, r * 2.1f);
            canvas.FillColor = tint.WithAlpha(0.95f);
            canvas.FillCircle(tip.X, tip.Y, r);
            canvas.FillColor = GraphPalette.TipSpark.WithAlpha(0.85f);
            canvas.FillCircle(tip.X - r * 0.28f, tip.Y - r * 0.28f, r * 0.45f);
        }
    }

    /// <summary>
    /// 跨片的藤：不是枝的延长线，是两头都实体、中间垂下去的一条。
    /// 所以它不「从粗收到细」，而是中段最实、两头收细 —— 加上按强度变粗，
    /// 「等价」和「顺便想到一下」才不是一条线的一个粗细版本。
    /// </summary>
    private void DrawVine(ICanvas canvas, GraphEdgeView e, GraphNodeView a, GraphNodeView b)
    {
        var p0 = Screen(a);
        var p2 = Screen(b);
        var ctrl = Control(p0, p2, e.From + e.To);

        var dimNet = OnlyNetId is not null && a.NetId != OnlyNetId && b.NetId != OnlyNetId ? 0.22f : 1f;
        var dof = Math.Min(Dof(e.From), Dof(e.To));
        var grip = Math.Clamp(e.Weight / 8.0, 0.2, 1);
        var baseAlpha = (float) ((e.Kind == RelationKind.Equivalent ? GraphPalette.EquivalentVineAlpha : GraphPalette.VineAlpha)
                        * (0.45 + 0.55 * Math.Clamp(e.Live, 0, 1)) * dimNet * dof);
        var tint = DofColor(GraphPalette.Edge(e.Kind), dof);
        var baseW = (float)((e.Kind == RelationKind.Equivalent ? 2.1 : 1.1) + 1.6 * grip) * Math.Max(0.5, Scale);

        canvas.StrokeColor = tint.WithAlpha(baseAlpha);
        canvas.StrokeDashPattern = e.Kind is RelationKind.Contrasts or RelationKind.Temporal ? new float[] { 4, 4 } : null;

        var prev = p0;
        const int segments = 10;
        for (var i = 1; i <= segments; i++)
        {
            var u = (float)i / segments;
            var pt = Quad(p0, ctrl, p2, u);
            // 两头收细：sin 弧，正中间最实
            canvas.StrokeSize = (float) Math.Max(0.5, baseW * (0.5 + 0.5 * Math.Sin(Math.PI * u)));
            canvas.DrawLine(prev.X, prev.Y, pt.X, pt.Y);
            prev = pt;
        }
        canvas.StrokeDashPattern = null;
    }

    /// <summary>
    /// 情绪配比画在卡片下沿：一段一段，长度就是占比。
    /// 画成小饼要赌各平台 Arc 的角度方向（MAUI 三家实现不完全一致），条子不会 ——
    /// 而且「安心占一半、依赖占三成」本来就是一个能量长度的东西。
    /// </summary>
    private void DrawEmotionPie(ICanvas canvas, GraphNodeView node, PointF at, float cardW, float halfH, IReadOnlyList<EmotionPart> mix)
    {
        if (Scale < 0.45) return;
        var total = Math.Max(6f, cardW - 6f);
        var h = Math.Max(2f, 2.6f * (float)Math.Clamp(Scale, 0.7, 1.4));
        var x = at.X - total / 2;
        var y = at.Y + halfH - h - 1.5f;
        var dof = Dof(node.Id);
        foreach (var part in mix)
        {
            var w = total * (float)Math.Clamp(part.Ratio, 0, 1);
            if (w < 0.6f) continue;
            canvas.FillColor = DofColor(GraphPalette.EmotionTint(part.Name), dof).WithAlpha(0.92f * dof);
            canvas.FillRectangle(x, y, w, h);
            x += w;
        }
        canvas.StrokeColor = DofColor(_ink.WithAlpha(0.3f), dof);
        canvas.StrokeSize = 0.8f;
        canvas.DrawRectangle(at.X - total / 2, y, total, h);
    }

    private void DrawLinkRubber(ICanvas canvas, GraphNodeView from, PointF to)
    {
        var p0 = Screen(from);
        canvas.StrokeColor = _ink.WithAlpha(0.5f);
        canvas.StrokeSize = 1.6f;
        canvas.StrokeDashPattern = new float[] { 5, 4 };
        canvas.DrawLine(p0.X, p0.Y, to.X, to.Y);
        canvas.StrokeDashPattern = null;
    }

    // ==================== 节点 ====================

    private void DrawNode(ICanvas canvas, GraphNodeView n)
    {
        var t = n.Depth == 0 ? 1f : (float)Math.Clamp(Progress(n) / 0.85, 0, 1);
        if (t <= 0.01) return;

        var grown = n.Depth == 0 || Progress(n) >= 1;
        var pop = grown ? Pop(n) : 1f;                    // 长到位弹一下
        // 一路上长大：疙瘩不是「枝到头了啪一下出现」，而是顺着枝的进度从半大长到全大 ——
        // 连着看只有一件事在发生（它在长），不会看成「线长完了，换成一个点」。
        var swell = Grown || n.Depth == 0 ? 1f : (float)(0.45 + 0.55 * MotionTokens.OutCubic(Progress(n)));
        var p = Screen(n, n.Depth == 0 ? 1f : (float)Math.Clamp(Progress(n), 0, 1));
        // 疙瘩跟着缩放走（缩到 0.55 就停）：位置会缩、疙瘩不缩，网一密就全糊在一起
        var r = Radius(n) * pop * swell * Glyph;
        if (r < 0.4f) return;

        var selected = n.Id == SelectedId;
        var mark = n.Id == MarkId;
        var dof = Dof(n.Id);
        // 遗忘和景深一起收：淡的疙瘩本来就小半圈，被让到景深外再退一层
        var dim = (float)((0.4 + 0.6 * Math.Clamp(n.Live, 0, 1)) * dof);
        var ink = DofColor(_ink, dof);                     // 描边只用这一份：远了的点连墨都淡，不是还那么硬
        var halfH = r;                                   // 标签挂在节点下沿：圆就是半径，卡片是半高
        float cardW = 0;                                 // 记忆卡片的宽：小饼要挂在它的右上角

        // 光要垫在最底下，所以先画光再画面
        if (selected) DrawHalo(canvas, p, n.Kind is NodeKind.Memory or NodeKind.Image ? r * 2.1f : r);

        if (n.Kind == NodeKind.Image && TryThumb(n) is { } img)
        {
            var side = Math.Max(10f, r * 2.4f);
            var ratio = Math.Min(side / Math.Max(1, img.Width), side / Math.Max(1, img.Height));
            var w = Math.Max(8f, img.Width * ratio);
            var h = Math.Max(8f, img.Height * ratio);
            halfH = h / 2;
            cardW = w;                                   // 圈要跟着照片的方框走，不要套个圆
            canvas.FillColor = DofColor(_glow, dof).WithAlpha(0.9f * dof);
            canvas.FillRoundedRectangle(p.X - w / 2 - 2, p.Y - h / 2 - 2, w + 4, h + 4, 4);
            canvas.DrawImage(img, p.X - w / 2, p.Y - h / 2, w, h);
        }
        else if (n.Kind == NodeKind.Memory)
        {
            // 一张「记得的事」画成小卡片，不要画成圆：它是一段话，不是一个词
            var w = Math.Max(r * 3.6f, 40f * Glyph);
            var h = Math.Max(r * 1.9f, 20f * Glyph);
            halfH = h / 2;
            cardW = w;
            canvas.FillColor = DofColor(GraphPalette.MemoryCard, dof).WithAlpha(Math.Min(1f, dim + 0.4f));
            canvas.FillRoundedRectangle(p.X - w / 2, p.Y - h / 2, w, h, 5);
            // 卡片自己先描一圈：这圈就是卡片的边界，选中时的外圈是另一回事
            canvas.StrokeColor = ink.WithAlpha(0.72f * dof);
            canvas.StrokeSize = mark || selected ? 2f : 1.3f;
            canvas.DrawRoundedRectangle(p.X - w / 2, p.Y - h / 2, w, h, 5);
        }
        else if (n.Kind == NodeKind.Emotion)
        {
            // 情绪点染成它自己的颜色：一片里全是同色的点，一眼看不出哪种感受占得多
            var tint = DofColor(GraphPalette.EmotionTint(n.Label), dof);
            canvas.FillColor = tint.WithAlpha(0.28f * dof);
            canvas.FillCircle(p.X, p.Y, r * 1.9f);
            canvas.FillColor = tint.WithAlpha(dim);
            canvas.FillCircle(p.X, p.Y, r);
            StrokeBlob(canvas, p, r, ink, n.Source, dof);
        }
        else
        {
            canvas.FillColor = DofColor(GraphPalette.Node(n.Kind), dof).WithAlpha(dim);
            canvas.FillCircle(p.X, p.Y, r);
            StrokeBlob(canvas, p, r, ink, n.Source, dof);
        }

        if (n.Emotion is { Count: > 0 } mix) DrawEmotionPie(canvas, n, p, cardW > 0 ? cardW : r * 2, halfH, mix);

        if (selected || mark)
        {
            // 圈要跟着节点形状走：给记忆卡片套个圆圈，就成两个东西了
            var grow = mark ? 7f : 4.5f;
            canvas.StrokeColor = ink.WithAlpha(mark ? 0.74f : 0.42f);
            canvas.StrokeSize = mark ? 2.1f : 1.4f;
            if (cardW > 0)
                canvas.DrawRoundedRectangle(p.X - cardW / 2 - grow, p.Y - halfH - grow,
                    (cardW / 2 + grow) * 2, (halfH + grow) * 2, 9);
            else canvas.DrawCircle(p.X, p.Y, r + grow);
        }
        if (n.Source == "ai")
        {
            canvas.FillColor = DofColor(GraphPalette.AiMark, dof).WithAlpha(dof);
            canvas.FillCircle(p.X + r * 0.82f, p.Y - r * 0.82f, Math.Max(2f, r * 0.28f));
        }

        DrawLabel(canvas, n, p, r, halfH);
    }

    /// <summary>疙瘩外那圈描边：模型整理出来的（source=ai）描得实一点，好一眼看出哪些不是 TA 自己长出来的。</summary>
    private void StrokeBlob(ICanvas canvas, PointF p, float r, Color ink, string source, float dof)
    {
        canvas.StrokeColor = ink.WithAlpha(source == "auto" ? 0.42f * dof : 0.8f * dof);
        canvas.StrokeSize = source == "auto" ? 1f : 1.6f;
        canvas.DrawCircle(p.X, p.Y, r);
    }

    /// <summary>
    /// 选中那一点时它自己发的光：<see cref="GraphPalette.GlowLayers"/> 层圆从外往里叠，
    /// 里层浓外层淡，中心合起来才到「一团暖光」的量。
    ///
    /// 用光而不是用粗描边表达「选中」：描边只会让疙瘩更硬，跟这片画纸的气质相反；
    /// 而光和底子上那团环境光同色 —— 选中一个点，整张图像是把灯往这边挪了挪。
    /// 呼吸只有 ±4%（和片的底子同一路）：看得出这一团是活的，又不至于晃眼。
    /// </summary>
    private void DrawHalo(ICanvas canvas, PointF p, float baseR)
    {
        var layers = GraphPalette.GlowLayers;
        var breathe = 1f + (float)Math.Sin(ElapsedMs / 1000.0 * 0.9) * 0.04f;
        for (var i = layers; i >= 1; i--)
        {
            var k = baseR * (1f + 0.55f * i) * breathe;
            canvas.FillColor = _glow.WithAlpha(GraphPalette.GlowInnerAlpha * (layers + 1 - i) / layers);
            canvas.FillEllipse(p.X - k, p.Y - k, k * 2, k * 2);
        }
    }

    private void DrawLabel(ICanvas canvas, GraphNodeView n, PointF p, float r, float halfH)
    {
        // 满屏都是字就读不出结构了：深处的、小的、还没长完的要先让位
        var hot = n.Id == SelectedId || n.Id == MarkId;
        if (string.IsNullOrWhiteSpace(n.Label)) return;
        if (!hot && !(n.Depth <= 2 || n.Salience >= 0.55 || r > 11)) return;
        // 缩得越狠、只留越靠中心的字：远处的字挤成一团，不如干脆不给
        if (!hot)
        {
            if (n.Depth >= 3 && Scale < 0.8) return;
            if (n.Depth >= 2 && Scale < 0.6) return;
        }

        var fs = Math.Clamp((float)(9.5 + 6 * n.Salience) * (float)Math.Clamp(Scale, 0.75, 1.35), 8f, 19f);
        canvas.Font = hot ? Microsoft.Maui.Graphics.Font.DefaultBold : Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = fs;
        // 景深外的字跟着退：远处那一片的「名字」读不清正是应该的 —— 但字还在，网没断
        canvas.FontColor = DofColor(hot ? _ink : _body.WithAlpha(0.86f), Dof(n.Id));

        var text = Fit(n.Label, n.Kind == NodeKind.Memory ? 12 : 10);
        var w = Math.Max(fs * 3f, text.Length * fs * 1.06f);
        var h = fs * 1.35f;
        var x = p.X - w / 2;
        var y = p.Y + halfH + 2;
        canvas.DrawString(text, x, y, w, h, HorizontalAlignment.Center, VerticalAlignment.Top);
    }

    // ==================== 命中 ====================

    /// <summary>世界坐标点到最近的可点节点（半径外 6 也算中，触屏好点）。</summary>
    public GraphNodeView? HitNode(Point world)
    {
        if (Nodes is null) return null;
        GraphNodeView? best = null;
        var bestDist = double.MaxValue;
        foreach (var n in Nodes)
        {
            if (!Show(n)) continue;
            if (n.Depth > 0 && Progress(n) < 0.55) continue;
            var pos = SwayPos(n);
            // 命中圈要和画出来的疙瘩一样大（世界 = 屏幕 / 缩放），外扩 6 个屏幕像素给触屏留余量
            var rad = (Radius(n) * Glyph + 6.0) / Math.Max(0.4, Scale);
            var dx = world.X - pos.X;
            var dy = world.Y - pos.Y;
            var d2 = dx * dx + dy * dy;
            if (d2 <= rad * rad && d2 < bestDist) { bestDist = d2; best = n; }
        }
        return best;
    }

    /// <summary>点到枝身上（用来选中「一条联系」而不是一个概念）。</summary>
    public GraphEdgeView? HitEdge(Point world, double tol = 10)
    {
        if (Edges is null) return null;
        var threshold = tol / Math.Max(0.4, Scale);
        var w = new PointF((float)world.X, (float)world.Y);
        GraphEdgeView? best = null;
        var bestD = double.MaxValue;
        foreach (var e in Edges)
        {
            var a = Find(e.From);
            var b = Find(e.To);
            if (a is null || b is null) continue;
            if (!Show(a) || !Show(b)) continue;
            if (!BoxInWorld(a, b)) continue;          // 屏外的枝不参与命中计算
            if (e.IsTree && Progress(b) < 1) continue;
            var p0 = SwayPos(a);
            var p2 = SwayPos(b);
            var ctrl = Control(p0, p2, e.From + e.To);
            for (var i = 0; i < 8; i++)
            {
                var q1 = Quad(p0, ctrl, p2, (float)i / 8);
                var q2 = Quad(p0, ctrl, p2, (float)(i + 1) / 8);
                var d = DistanceToSegment(w, q1, q2);
                if (d < bestD) { bestD = d; best = e; }
            }
        }
        return bestD <= threshold ? best : null;
    }

    /// <summary>当前正在生长的那一圈里，最靠近某点的梢头（给「跟着长」的提示用）。</summary>
    public GraphNodeView? Find(string id) => _byId.TryGetValue(id, out var n) ? n : null;

    // ==================== 几何 ====================

    private double Progress(GraphNodeView n) =>
        Grown ? 1 : Math.Clamp((ElapsedMs - n.GrowAtMs) / GrowMs, 0, 1);

    private bool GrownOrGrowing(GraphNodeView n) => n.Depth == 0 || Progress(n) > 0.35;

    /// <summary>枝条伸到位之后，芽苞弹一下再落回原位（320ms）。</summary>
    private float Pop(GraphNodeView n)
    {
        var after = ElapsedMs - (n.GrowAtMs + GrowMs);
        if (Grown || after <= 0 || after > 320) return 1f;
        var k = after / 320.0;
        return (float)(1 + Math.Sin(k * Math.PI) * 0.28);
    }

    /// <summary>疙瘩多大：显著性定基调，「今天还剩几分力气」再收一圈。</summary>
    public static float Radius(GraphNodeView n)
    {
        var baseR = n.Kind switch
        {
            NodeKind.Memory => 9.0,
            NodeKind.Image => 9.5,
            NodeKind.Person => 9.0,
            NodeKind.Emotion => 8.0,
            _ => 6.5
        };
        var live = 0.5 + 0.5 * Math.Clamp(n.Live, 0, 1);
        return (float)((baseR + 15 * Math.Clamp(n.Salience, 0, 1)) * live);
    }

    private PointF SwayPos(GraphNodeView n)
    {
        if (n.Depth == 0) return new PointF((float)n.X, (float)n.Y);
        var phase = RelationLayout.Normalized(RelationLayout.StableHash(n.Id), 0) * Math.PI * 2;
        var amp = Math.Min(4.5, n.Depth * 1.7);
        var t = ElapsedMs / 1000.0;
        return new PointF(
            (float)(n.X + Math.Sin(t * 0.9 + phase) * amp),
            (float)(n.Y + Math.Cos(t * 0.7 + phase * 1.3) * amp * 0.72));
    }

    private PointF Screen(GraphNodeView n, float growT = 1f)
    {
        var pos = SwayPos(n);
        if (n.Depth > 0 && growT < 1f && n.ParentId is not null)
        {
            var parent = Find(n.ParentId);
            if (parent is not null)
            {
                var pp = SwayPos(parent);
                pos = new PointF(pp.X + (pos.X - pp.X) * growT, pp.Y + (pos.Y - pp.Y) * growT);
            }
        }
        var s = WorldToScreen(new Point(pos.X, pos.Y));
        return new PointF((float)s.X, (float)s.Y);
    }

    /// <summary>枝的弯法：中点朝垂直方向偏一点，方向与幅度由 Id 的稳定哈希决定（同一根枝每次一样弯）。</summary>
    private static PointF Control(PointF a, PointF b, string seed)
    {
        var hash = RelationLayout.StableHash(seed);
        var bend = (float)(RelationLayout.Normalized(hash, 3) * 0.34 - 0.06);
        var sign = (hash & 1) == 0 ? 1f : -1f;
        var mx = (a.X + b.X) / 2f;
        var my = (a.Y + b.Y) / 2f;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return new PointF(mx - dy * bend * sign, my + dx * bend * sign);
    }

    /// <summary>二次贝塞尔上 t 处的点。</summary>
    private static PointF Quad(PointF p0, PointF p1, PointF p2, float t)
    {
        var ax = p0.X + (p1.X - p0.X) * t;
        var ay = p0.Y + (p1.Y - p0.Y) * t;
        var bx = p1.X + (p2.X - p1.X) * t;
        var by = p1.Y + (p2.Y - p1.Y) * t;
        return new PointF(ax + (bx - ax) * t, ay + (by - ay) * t);
    }

    private static double DistanceToSegment(PointF p, PointF a, PointF b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var len2 = dx * dx + dy * dy;
        if (len2 <= 0.0001) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0f, 1f);
        var qx = a.X + t * dx;
        var qy = a.Y + t * dy;
        return Math.Sqrt((double)(p.X - qx) * (p.X - qx) + (double)(p.Y - qy) * (p.Y - qy));
    }

    private GraphEdgeView? EdgeTo(GraphNodeView child)
        => _edgeOf.TryGetValue(child.Id, out var e) ? e : null;

    private void DrawArrowHead(ICanvas canvas, PointF ctrl, PointF tip, Color tint)
    {
        var dx = tip.X - ctrl.X;
        var dy = tip.Y - ctrl.Y;
        var len = (float)Math.Max(0.001, Math.Sqrt(dx * dx + dy * dy));
        var ux = dx / len;
        var uy = dy / len;
        var s = 6f * (float)Math.Max(0.5, Scale);
        canvas.StrokeColor = tint.WithAlpha(0.8f);
        canvas.StrokeSize = 1.6f;
        canvas.DrawLine(tip.X, tip.Y, tip.X - ux * s - uy * s * 0.6f, tip.Y - uy * s + ux * s * 0.6f);
        canvas.DrawLine(tip.X, tip.Y, tip.X - ux * s + uy * s * 0.6f, tip.Y - uy * s - ux * s * 0.6f);
    }

    private static string Fit(string s, int max) =>
        s.Length <= max ? s : s[..Math.Max(1, max - 1)] + "…";

    // ==================== 缩略图 ====================

    private Microsoft.Maui.Graphics.IImage? TryThumb(GraphNodeView n)
    {
        if (_thumbResolver is null) return null;
        if (_thumbCache.TryGetValue(n.Id, out var cached)) return cached;
        _thumbCache[n.Id] = null;
        LoadThumb(n);
        return null;
    }

    private void LoadThumb(GraphNodeView n)
    {
        var path = _thumbResolver?.Invoke(n);
        if (string.IsNullOrEmpty(path)) return;
        _ = Task.Run(() =>
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch { return; }
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    using var ms = new MemoryStream(bytes);
                    _thumbCache[n.Id] = Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(ms);
                }
                catch { _thumbCache[n.Id] = null; }
                NeedRedraw();
            });
        });
    }
}
