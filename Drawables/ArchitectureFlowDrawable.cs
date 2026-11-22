using Microsoft.Maui.Graphics;
using WarmAsBefore.DesignSystem.Worlds;

namespace WarmAsBefore.Drawables;

/// <summary>节点在流程里的身份：决定大小、形状与画法，而不是只决定颜色。</summary>
public enum ArchNodeKind
{
    /// <summary>最小节点：一次具体处理，小圆角框。</summary>
    Step = 0,
    /// <summary>模块：一组处理构成的子系统，带左缘色条。</summary>
    Module = 1,
    /// <summary>整体模式 / 调控者：跨层生效、可以整体停用，画成胶囊并用虚线控制别人。</summary>
    Mode = 2,
    /// <summary>输出：流程的落点，画成右端带箭嘴的框。</summary>
    Output = 3
}

/// <summary>
/// 活流程图上的一个节点。位置由 drawable 按 Layer 与流序排；VM 只给身份、文案、所属层、身份类别、
/// 所属子系统（Group，用来在图上围出「记忆」「内驱」这类独立系统）与一行实时读数（Badge）。
/// </summary>
public sealed record ArchNode(
    string Id,
    string Label,
    string Layer,
    string? Module = null,
    string? Detail = null,
    ArchNodeKind Kind = ArchNodeKind.Step,
    string? Group = null,
    string? Badge = null);

/// <summary>活流程图上的一条联系：按 Id 连，两端都排好位才画。</summary>
public sealed record ArchLink(string From, string To, LinkKind Kind);

/// <summary>四种箭头语义：--&gt; 数据流、==&gt; 双向强、-.&gt; 反馈虚线、--o 调控（模式对模块的控制）。</summary>
public enum LinkKind
{
    Normal,
    Bidirectional,
    Feedback,
    Control
}

/// <summary>节点特效：病理态（抑郁 / PTSD / 惊恐这类）画成呼吸的红色环，越重越急。</summary>
public sealed record NodeFx(double Severity, bool Pathological)
{
    public static readonly NodeFx None = new(0, false);
}

/// <summary>
/// 论文《面向类人情感模拟的神经符号心智架构》的活流程图。
///
/// 读法：五层各一条横带，带内按层内流序排，从左往右就是数据走过的顺序；
/// 整体模式（专注 / 神游 / 内驱动这类能整体停用、跨层生效的东西）单独排带首的调控栏，
/// 用虚线控制它们管着的模块，和「一个零件」长得完全不一样。
/// 同一件事的子系统（五层记忆、内驱动）在图上被围成一块，看得见边界，而不散成几个孤点。
///
/// 观感：连线是带轻微随机弧度的三次贝塞尔（不是死板的正交折线），活跃链路上有流动光点；
/// 颜色按模块分（输入 / 记忆 / 心智 / 躯体 / 工程 / 调控 / 病理），形状按身份分；
/// 被切断的链路（熔断、越权、反刍死锁）会在中间断开并打一个叉。
///
/// 尺寸按内容算自然宽高交给 VM（不再压进固定框等比缩小），外面的 ScrollView 负责滚动。
/// 亮灯与特效都由 VM 注入的 provider 决定，drawable 只按图片画、不碰业务。
/// </summary>
public sealed class ArchitectureFlowDrawable : IDrawable
{
    // ---- 设计尺寸：自然布局 ----
    private const float NodeW = 176f;
    private const float NodeH = 48f;
    private const float ModuleH = 52f;
    private const float ModeH = 52f;
    private const float GapX = 16f;
    private const float GapY = 14f;
    private const float CellW = 470f;
    private const float CellH = 404f;
    private const float CellGap = 24f;
    private const float CellPad = 14f;
    private const float CellTitleH = 26f;
    private const float OuterPad = 30f;
    private const int GridCols = 4;
    private const int GridRows = 3;

    /// <summary>没归进任何系统的节点统一收进这一块。</summary>
    private const string LooseGroupId = "__loose";

    private IReadOnlyList<ArchNode>? _nodes;
    public IReadOnlyList<ArchNode>? Nodes
    {
        get => _nodes;
        set { _nodes = value; Layout(); }
    }

    private IReadOnlyList<ArchLink>? _links;
    public IReadOnlyList<ArchLink>? Links
    {
        get => _links;
        set => _links = value;
    }

    public string? SelectedId { get; set; }

    /// <summary>VM 注入的余辉强度：id -> 0..1。空则所有节点按暗态画。</summary>
    public Func<string, float>? GlowProvider { get; set; }

    /// <summary>VM 注入的病理特效：返回非 None 的节点会画呼吸的警示环。</summary>
    public Func<string, NodeFx>? EffectProvider { get; set; }

    /// <summary>VM 注入的切断判定：返回 true 的连线画成中间断开、打叉的断链。</summary>
    public Func<ArchLink, bool>? SeveredProvider { get; set; }

    /// <summary>子系统在图上围出来的框的标题：Group id -> 人话名字。</summary>
    public IReadOnlyDictionary<string, string>? GroupTitles { get; set; }

    /// <summary>每个子系统摆在哪一格（列, 行）：布局由 VM 决定谁挨着谁，drawable 只负责摆。</summary>
    public IReadOnlyDictionary<string, (int Col, int Row)>? GroupCells { get; set; }

    /// <summary>激素这块是媒介而不是一层：画得比别的系统更宽更淡，并标注它向全系统广播。</summary>
    public string? HubGroup { get; set; }

    /// <summary>
    /// 层色覆盖：key 是 Layer（"1".."5"），value 直接用 GraphPalette 现有成员重钉即可。
    /// 不传则用内置的五层配色。
    /// </summary>
    public IReadOnlyDictionary<string, Color>? LayerColors { get; set; }

    /// <summary>
    /// 整页氛围：0 神游、1 专注、2 专注即将失效。
    /// 这三件事是整个心智的模式，不是某一格的属性，所以画在整页上而不是画在节点上。
    /// </summary>
    public int AmbientMode { get; set; }

    /// <summary>0..1 循环相位，由 VM 的钟推进；呼吸与脉冲的节奏都取它。</summary>
    public double AmbientPhase { get; set; }

    /// <summary>0..1 病理浓度：越高压暗底色、边缘泛红越重。</summary>
    public double AmbientSeverity { get; set; }

    /// <summary>
    /// 整张图的亮度：神游时压暗（低精度默认态，系统大半在后台怠速），专注时全亮。
    /// 图跟着状态变，而不是永远同一张。
    /// </summary>
    public double AmbientOpacity { get; set; } = 1;

    /// <summary>自然内容尺寸变了：VM 据此改 GraphicsView 的请求宽高，让外面能滚。</summary>
    public event Action<float, float>? ContentSizeChanged;

    public float ContentWidth { get; private set; }
    public float ContentHeight { get; private set; }

    private readonly Dictionary<string, RectF> _rects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ArchNode> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RectF> _groupFrames = new(StringComparer.Ordinal);
    private float _viewW;
    private float _viewH;
    private float _nodeFont = 12.5f;
    private float _smallFont = 9.5f;

    public void SetCanvasSize(double w, double h)
    {
        if (Math.Abs(_viewW - w) < 0.5 && Math.Abs(_viewH - h) < 0.5) return;
        _viewW = (float)w;
        _viewH = (float)h;
        Layout();
    }

    // ==================== 布局 ====================

    private void Layout()
    {
        _rects.Clear();
        _byId.Clear();
        _groupFrames.Clear();

        var nodes = _nodes;
        if (nodes is null || nodes.Count == 0)
        {
            SetContent(0, 0);
            return;
        }
        foreach (var n in nodes) _byId[n.Id] = n;

        // 系统即格子：每个子系统一块区域，激素那块居中当媒介。没有层，没有上下级——
        // 它们同时存在，只是在图上各占一块、互相连线。
        var groups = nodes
            .Where(n => !string.IsNullOrEmpty(n.Group))
            .GroupBy(n => n.Group!, StringComparer.Ordinal)
            .Select(g => g.Key)
            .ToList();
        var loose = nodes.Where(n => string.IsNullOrEmpty(n.Group)).ToList();

        var cells = new Dictionary<string, (int Col, int Row)>(StringComparer.Ordinal);
        if (GroupCells is not null)
            foreach (var kv in GroupCells) cells[kv.Key] = kv.Value;
        // 没给格子的子系统：按顺序填进还没被占的格子。
        var used = new HashSet<(int, int)>(cells.Values);
        var nextCell = 0;
        foreach (var g in groups.Where(g => !cells.ContainsKey(g)))
        {
            while (used.Contains((nextCell % GridCols, nextCell / GridCols))) nextCell++;
            cells[g] = (nextCell % GridCols, nextCell / GridCols);
            used.Add(cells[g]);
            nextCell++;
        }
        if (loose.Count > 0 && !cells.ContainsKey(LooseGroupId))
        {
            while (used.Contains((nextCell % GridCols, nextCell / GridCols))) nextCell++;
            cells[LooseGroupId] = (nextCell % GridCols, nextCell / GridCols);
            used.Add(cells[LooseGroupId]);
        }

        var cols = Math.Max(GridCols, cells.Values.Select(c => c.Col + 1).DefaultIfEmpty(1).Max());
        var rows = Math.Max(GridRows, cells.Values.Select(c => c.Row + 1).DefaultIfEmpty(1).Max());
        var contentW = OuterPad * 2 + cols * CellW + (cols - 1) * CellGap;
        var contentH = OuterPad * 2 + rows * CellH + (rows - 1) * CellGap;

        foreach (var (group, cell) in cells)
        {
            var frame = new RectF(OuterPad + cell.Col * (CellW + CellGap),
                OuterPad + cell.Row * (CellH + CellGap), CellW, CellH);
            _groupFrames[group] = frame;

            var members = group == LooseGroupId
                ? loose
                : OrderByFlow(nodes.Where(n => string.Equals(n.Group, group, StringComparison.Ordinal)).ToList());
            if (members.Count == 0) continue;

            // 系统内排布：两列、紧凑居中，读的时候是一簇一簇的。
            var innerCols = Math.Max(1, Math.Min(2, (int)Math.Floor((frame.Width - 2 * CellPad + GapX) / (NodeW + GapX))));
            var innerRows = (members.Count + innerCols - 1) / innerCols;
            var stackH = innerRows * NodeH + (innerRows - 1) * GapY;
            var top = frame.Y + CellTitleH + Math.Max(CellPad, (frame.Height - CellTitleH - stackH) / 2f);
            for (var i = 0; i < members.Count; i++)
            {
                var row = i / innerCols;
                var col = i % innerCols;
                var inRow = Math.Min(innerCols, members.Count - row * innerCols);
                var rowW = inRow * NodeW + (inRow - 1) * GapX;
                var left = frame.X + (frame.Width - rowW) / 2f;
                var h = members[i].Kind == ArchNodeKind.Mode ? ModeH - 4f
                    : members[i].Kind == ArchNodeKind.Module ? ModuleH - 4f
                    : NodeH;
                _rects[members[i].Id] = new RectF(left + col * (NodeW + GapX),
                    top + row * (NodeH + GapY) + (NodeH - h) / 2f, NodeW, h);
            }
        }

        SetContent(contentW, contentH);
    }

    /// <summary>
    /// 层内流序：只拿同层边做一次拓扑排序，让左边的节点先被喂到；
    /// 有环或没有边时保持 VM 给的原始顺序（论文顺序本身就是拓扑序）。
    /// </summary>
    private List<ArchNode> OrderByFlow(List<ArchNode> list)
    {
        if (list.Count <= 2 || _links is null) return list;
        var ids = list.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var indegree = list.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        var outMap = list.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var e in _links)
        {
            if (e.Kind is LinkKind.Feedback or LinkKind.Control) continue;
            if (!ids.Contains(e.From) || !ids.Contains(e.To) || e.From == e.To) continue;
            outMap[e.From].Add(e.To);
            indegree[e.To]++;
        }
        var ready = list.Where(n => indegree[n.Id] == 0).Select(n => n.Id).ToList();
        var ordered = new List<string>(list.Count);
        while (ready.Count > 0)
        {
            var next = ready[0];
            ready.RemoveAt(0);
            ordered.Add(next);
            foreach (var to in outMap[next])
            {
                if (--indegree[to] <= 0) ready.Add(to);
            }
        }
        if (ordered.Count != list.Count) return list;
        var byId = list.ToDictionary(n => n.Id, StringComparer.Ordinal);
        return ordered.Select(id => byId[id]).ToList();
    }

    private void SetContent(float w, float h)
    {
        if (Math.Abs(ContentWidth - w) < 0.5f && Math.Abs(ContentHeight - h) < 0.5f) return;
        ContentWidth = w;
        ContentHeight = h;
        ContentSizeChanged?.Invoke(w, h);
    }

    // ==================== 绘制 ====================

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        _viewW = dirtyRect.Width;
        _viewH = dirtyRect.Height;

        var ink = StyleWorldTokens.Pick("WarmDark", GraphPalette.Unknown);
        var body = StyleWorldTokens.Pick("WarmBody", GraphPalette.Unknown);

        if (_rects.Count == 0 && (_nodes is null || _nodes.Count == 0))
        {
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.FontSize = 12;
            canvas.FontColor = body.WithAlpha(0.7f);
            canvas.DrawString("流程数据还没到位。", OuterPad, OuterPad + CellTitleH,
                Math.Max(80f, dirtyRect.Width - OuterPad * 2), 28,
                HorizontalAlignment.Left, VerticalAlignment.Top);
            return;
        }

        // 顺序：整页氛围 → 系统块 → 连线 → 节点 → 边缘光。底色垫在下面，光压在最外面。
        DrawAmbient(canvas, dirtyRect);
        // 神游时整张图压暗：低精度默认态里大部分系统在怠速，图也该是暗的。
        canvas.SaveState();
        canvas.Alpha = (float)Math.Clamp(AmbientOpacity, 0.3, 1.0);
        DrawGroups(canvas, ink);
        DrawLinks(canvas);
        foreach (var n in _nodes ?? Array.Empty<ArchNode>())
            if (_rects.TryGetValue(n.Id, out var r)) DrawNode(canvas, n, r, ink);
        canvas.RestoreState();
        DrawAura(canvas, dirtyRect);
    }

    // 氛围用的四种底色：神游冷、专注暖、失效红、病理紫红。写在本地而不是 GraphPalette，
    // 因为 palette 是名词色（人、地、物、情绪），这里要的是「整页此刻的气质」。
    private static readonly Color ToneWander = Color.FromArgb("#4FA3C7");
    private static readonly Color ToneFocus = Color.FromArgb("#FFB454");
    private static readonly Color ToneLapse = Color.FromArgb("#FF5A5A");
    private static readonly Color TonePatho = Color.FromArgb("#B25DD6");

    private double Wave => 0.5 + 0.5 * Math.Sin(AmbientPhase * Math.PI * 2);

    /// <summary>
    /// 整页底色：神游时是缓慢的冷色呼吸，专注时暖起来，专注快撑不住时泛红并加快脉冲，
    /// 病理浓度越高整页越压暗。这样一眼看过去就知道「她现在是神游还是在硬撑」。
    /// </summary>
    private void DrawAmbient(ICanvas canvas, RectF r)
    {
        var wave = Wave;
        Color tone;
        double alpha;
        switch (AmbientMode)
        {
            case 1:
                tone = ToneFocus;
                alpha = 0.045 + 0.055 * wave;
                break;
            case 2:
                tone = ToneLapse;
                alpha = 0.06 + 0.15 * wave;
                break;
            default:
                tone = ToneWander;
                alpha = 0.03 + 0.04 * wave;
                break;
        }
        if (AmbientSeverity > 0.01)
        {
            tone = GraphPalette.Blend(tone, TonePatho, Math.Min(1, AmbientSeverity));
            alpha += 0.045 * AmbientSeverity;
        }

        canvas.FillColor = tone.WithAlpha((float)Math.Min(0.26, alpha));
        canvas.FillRectangle(r);
    }

    /// <summary>
    /// 边缘光：由外到内三道低透明度描边，读起来是「边缘在发光」而不是画了个框。
    /// 神游时几乎不闪、专注时缓慢呼吸、即将失效时红得明显 —— 失效前的提示就靠它。
    /// </summary>
    private void DrawAura(ICanvas canvas, RectF r)
    {
        var wave = Wave;
        var tone = AmbientMode == 2 ? ToneLapse : AmbientMode == 1 ? ToneFocus : ToneWander;
        var strength = AmbientMode == 2 ? 0.62 : AmbientMode == 1 ? 0.34 : 0.18;
        var beat = AmbientMode == 2 ? 0.35 + 0.65 * wave : 0.55 + 0.45 * wave;

        for (var i = 0; i < 3; i++)
        {
            var inset = 3f + i * 6f;
            if (r.Width <= inset * 2 || r.Height <= inset * 2) break;
            canvas.StrokeColor = tone.WithAlpha((float)Math.Max(0, (strength - i * 0.1) * beat));
            canvas.StrokeSize = 1.6f;
            canvas.DrawRoundedRectangle(inset, inset, r.Width - inset * 2, r.Height - inset * 2, 14);
        }
    }

    /// <summary>
    /// 系统块：每个子系统占一格，标题写在块顶。激素那块当媒介画——更淡、更宽、
    /// 带一句「向全系统广播」，因为它不是又一层，而是所有系统共享的通信介质。
    /// </summary>
    private void DrawGroups(ICanvas canvas, Color ink)
    {
        foreach (var (id, r) in _groupFrames)
        {
            var isHub = !string.IsNullOrEmpty(HubGroup) && string.Equals(id, HubGroup, StringComparison.Ordinal);
            var tint = isHub ? GraphPalette.Tag : GraphPalette.MemoryCard;

            canvas.FillColor = tint.WithAlpha(isHub ? 0.075f : 0.045f);
            canvas.FillRoundedRectangle(r.X, r.Y, r.Width, r.Height, 18);
            canvas.StrokeColor = tint.WithAlpha(isHub ? 0.42f : 0.22f);
            canvas.StrokeSize = isHub ? 1.6f : 1f;
            canvas.StrokeDashPattern = isHub ? new[] { 8f, 5f } : new[] { 6f, 5f };
            canvas.DrawRoundedRectangle(r.X, r.Y, r.Width, r.Height, 18);
            canvas.StrokeDashPattern = null;

            var title = GroupTitles is not null && GroupTitles.TryGetValue(id, out var t)
                ? t
                : id == LooseGroupId ? "其他" : id;
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.FontSize = _smallFont + 1f;
            canvas.FontColor = (isHub ? GraphPalette.Tag : ink).WithAlpha(isHub ? 0.85f : 0.62f);
            canvas.DrawString(title, r.X + 14, r.Y + 4, r.Width - 28, 18,
                HorizontalAlignment.Left, VerticalAlignment.Center);

            if (isHub)
            {
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
                canvas.FontSize = _smallFont;
                canvas.FontColor = GraphPalette.Tag.WithAlpha(0.62f);
                canvas.DrawString("全系统广播 · 慢性调制", r.X + 14, r.Y + 20, r.Width - 28, 14,
                    HorizontalAlignment.Left, VerticalAlignment.Center);
            }
        }
    }

    private void DrawLinks(ICanvas canvas)
    {
        var links = _links;
        if (links is null || links.Count == 0) return;

        // 调控线最底，数据流其次，双向与反馈压在最上面；被切断的最后画，一眼能看见断口。
        foreach (var e in links.Where(e => e.Kind == LinkKind.Control)) DrawLink(canvas, e);
        foreach (var e in links.Where(e => e.Kind == LinkKind.Normal)) DrawLink(canvas, e);
        foreach (var e in links.Where(e => e.Kind is LinkKind.Bidirectional or LinkKind.Feedback)) DrawLink(canvas, e);
        foreach (var e in links.Where(e => SeveredProvider?.Invoke(e) == true)) DrawLink(canvas, e);
    }

    private void DrawLink(ICanvas canvas, ArchLink e)
    {
        if (!_rects.TryGetValue(e.From, out var a) || !_rects.TryGetValue(e.To, out var b)) return;

        var severed = SeveredProvider?.Invoke(e) == true;
        var color = severed
            ? GraphPalette.Causal.WithAlpha(0.75f)
            : e.Kind switch
            {
                LinkKind.Bidirectional => GraphPalette.Associated.WithAlpha(0.62f),
                LinkKind.Feedback => GraphPalette.Causal.WithAlpha(0.55f),
                LinkKind.Control => GraphPalette.Tag.WithAlpha(0.50f),
                _ => GraphPalette.Unknown.WithAlpha(0.34f)
            };

        var (p0, c1, c2, p3) = CurveOf(a, b, e);

        // 这一轮计算真的流过这条链路时，线本身也亮起来：只亮节点看不出「从哪儿流到哪儿」。
        var linkGlow = Math.Max(GlowProvider?.Invoke(e.From) ?? 0f, GlowProvider?.Invoke(e.To) ?? 0f);
        if (!severed && linkGlow > 0.05f)
            color = color.WithAlpha(Math.Min(1f, color.Alpha + 0.45f * linkGlow));

        canvas.StrokeColor = color;
        canvas.StrokeSize = (severed ? 1.6f : e.Kind is LinkKind.Bidirectional or LinkKind.Control ? 1.5f : 1.2f)
            + (severed ? 0f : 0.7f * linkGlow);
        canvas.StrokeDashPattern = severed || e.Kind is LinkKind.Feedback or LinkKind.Control
            ? new[] { 5f, 4f }
            : null;

        var path = new PathF();
        path.MoveTo(p0.X, p0.Y);
        path.CurveTo(c1.X, c1.Y, c2.X, c2.Y, p3.X, p3.Y);
        canvas.DrawPath(path);
        canvas.StrokeDashPattern = null;

        var (tx, ty) = BezierPoint(p0, c1, c2, p3, 0.97f);
        var (px, py) = BezierPoint(p0, c1, c2, p3, 0.90f);
        var dx = tx - px;
        var dy = ty - py;
        var len = (float)Math.Sqrt(dx * dx + dy * dy);
        if (len > 0.5f) DrawArrow(canvas, tx, ty, dx / len, dy / len, 7f, color);
        if (e.Kind == LinkKind.Bidirectional)
        {
            var (sx, sy) = BezierPoint(p0, c1, c2, p3, 0.03f);
            var (qx, qy) = BezierPoint(p0, c1, c2, p3, 0.10f);
            var ex = sx - qx;
            var ey = sy - qy;
            var l2 = (float)Math.Sqrt(ex * ex + ey * ey);
            if (l2 > 0.5f) DrawArrow(canvas, sx, sy, ex / l2, ey / l2, 7f, color);
        }
        if (e.Kind == LinkKind.Control && !severed)
        {
            canvas.FillColor = color;
            canvas.FillCircle(p0.X, p0.Y, 3f);
        }
        if (severed)
        {
            // 断口：中点留一段空白 + 一个小叉，表示这条路径被强制切断了。
            var (mx, my) = BezierPoint(p0, c1, c2, p3, 0.5f);
            canvas.FillColor = GraphPalette.TextCard.WithAlpha(0.9f);
            canvas.FillRoundedRectangle(mx - 9, my - 9, 18, 18, 4);
            canvas.StrokeColor = GraphPalette.Causal.WithAlpha(0.95f);
            canvas.StrokeSize = 2f;
            canvas.DrawLine(mx - 5, my - 5, mx + 5, my + 5);
            canvas.DrawLine(mx + 5, my - 5, mx - 5, my + 5);
        }
        else
        {
            DrawFlowDot(canvas, p0, c1, c2, p3, e, color);
        }
    }

    /// <summary>活跃链路上的一颗流动光点：位置由时间相位 + 链路散列决定，VM 的时钟负责重绘。</summary>
    private void DrawFlowDot(ICanvas canvas, PointF p0, PointF c1, PointF c2, PointF p3, ArchLink e, Color color)
    {
        var glow = GlowProvider?.Invoke(e.To) ?? 0f;
        if (glow <= 0.05f) return;
        var phase = (float)((DateTime.UtcNow.Ticks / 1_000_0000.0) * 0.55);
        var seed = (Math.Abs(e.From.GetHashCode() ^ e.To.GetHashCode()) % 1000) / 1000f;
        var t = (phase + seed) % 1f;
        var (x, y) = BezierPoint(p0, c1, c2, p3, t);
        canvas.FillColor = GraphPalette.Tag.WithAlpha(0.35f + 0.5f * glow);
        canvas.FillCircle(x, y, 2.6f);
    }

    /// <summary>
    /// 贝塞尔的控制点：方向按两端相对位置取「下→上 / 左→右」的主轴，再用法向随机偏移，
    /// 让每条线有一点自己的弧度——太规整就没有呼吸感。
    /// </summary>
    private static (PointF P0, PointF C1, PointF C2, PointF P3) CurveOf(RectF a, RectF b, ArchLink e)
    {
        var sameBand = Math.Abs((a.Bottom + a.Top) / 2 - (b.Bottom + b.Top) / 2) < a.Height + b.Height;
        var seed = Math.Abs((e.From.GetHashCode() * 31 ^ e.To.GetHashCode()) % 1000) / 1000f;
        var wobble = (seed - 0.5f) * 2f; // -1..1

        if (e.Kind == LinkKind.Control)
        {
            // 调控线从模式块左侧出来，沿最左总线上下，再拐进目标左侧。
            var sx = a.Left;
            var sy = a.Center.Y;
            var tx = b.Left - 4f;
            var ty = b.Center.Y;
            var busX = Math.Max(8f, Math.Min(a.Left, b.Left) - 14f);
            return (new PointF(sx, sy), new PointF(busX, sy + wobble * 4f),
                    new PointF(busX, ty + wobble * 4f), new PointF(tx, ty));
        }

        if (sameBand && Math.Abs(a.Center.X - b.Center.X) > a.Width)
        {
            var right = b.Center.X > a.Center.X;
            var p0 = new PointF(right ? a.Right : a.Left, a.Center.Y);
            var p3 = new PointF(right ? b.Left : b.Right, b.Center.Y);
            var bend = Math.Abs(p3.X - p0.X) * (0.42f + 0.16f * wobble);
            var dy = (p3.Y - p0.Y) * 0.35f + wobble * 18f;
            var c1 = new PointF(p0.X + (right ? bend : -bend), p0.Y + dy);
            var c2 = new PointF(p3.X - (right ? bend : -bend), p3.Y + dy);
            return (p0, c1, c2, p3);
        }

        if (b.Top >= a.Bottom - 1f)
        {
            var p0 = new PointF(a.Center.X, a.Bottom);
            var p3 = new PointF(b.Center.X, b.Top);
            var dy = Math.Max(18f, (p3.Y - p0.Y) * 0.55f);
            var c1 = new PointF(p0.X + wobble * 26f, p0.Y + dy);
            var c2 = new PointF(p3.X + wobble * 26f, p3.Y - dy * 0.6f);
            return (p0, c1, c2, p3);
        }

        // 往上（反馈）或同带不同行：从右侧绕出去，弧度更大，不和中间的线缠在一起。
        var s = new PointF(a.Right, a.Center.Y);
        var t2 = new PointF(b.Center.X, b.Bottom);
        var channel = Math.Max(a.Right, b.Right) + 22f + Math.Abs(wobble) * 10f;
        return (s, new PointF(channel, s.Y - 10f), new PointF(channel, t2.Y + 16f), t2);
    }

    private static (float X, float Y) BezierPoint(PointF p0, PointF c1, PointF c2, PointF p3, float t)
    {
        var u = 1f - t;
        var x = u * u * u * p0.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * p3.X;
        var y = u * u * u * p0.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * p3.Y;
        return (x, y);
    }

    private static void DrawArrow(ICanvas canvas, float tipX, float tipY, float ux, float uy, float size, Color color)
    {
        var bx = tipX - ux * size;
        var by = tipY - uy * size;
        var nx = -uy;
        var ny = ux;
        var path = new PathF();
        path.MoveTo(tipX, tipY);
        path.LineTo(bx + nx * size * 0.5f, by + ny * size * 0.5f);
        path.LineTo(bx - nx * size * 0.5f, by - ny * size * 0.5f);
        path.Close();
        canvas.FillColor = color;
        canvas.FillPath(path);
    }

    private void DrawNode(ICanvas canvas, ArchNode n, RectF r, Color ink)
    {
        var glow = GlowProvider is null ? 0f : Math.Clamp(GlowProvider(n.Id), 0f, 1f);
        var fx = EffectProvider?.Invoke(n.Id) ?? NodeFx.None;
        var selected = n.Id == SelectedId;
        var highlight = GraphPalette.Tag;
        var accent = AccentOf(n);
        var isMode = n.Kind == ArchNodeKind.Mode;

        // 病理特效：呼吸的警示环，越重越急（相位随真实时间走，VM 的时钟负责重绘）。
        if (fx.Severity > 0.01f)
        {
            var phase = (float)(DateTime.UtcNow.Ticks / 1_000_0000.0 * (1.2 + fx.Severity));
            var beat = 0.5f + 0.5f * (float)Math.Sin(phase);
            var pad = 4f + 5f * beat * (float)fx.Severity;
            canvas.FillColor = GraphPalette.Causal.WithAlpha(0.05f + 0.10f * beat * (float)fx.Severity);
            canvas.FillRoundedRectangle(r.X - pad, r.Y - pad, r.Width + pad * 2, r.Height + pad * 2,
                isMode ? (r.Height + pad * 2) / 2f : 12f);
            canvas.StrokeColor = GraphPalette.Causal.WithAlpha(0.25f + 0.45f * beat * (float)fx.Severity);
            canvas.StrokeSize = 1.2f + 1.4f * beat * (float)fx.Severity;
            canvas.DrawRoundedRectangle(r.X - pad, r.Y - pad, r.Width + pad * 2, r.Height + pad * 2,
                isMode ? (r.Height + pad * 2) / 2f : 12f);
        }

        var radius = isMode ? r.Height / 2f : n.Kind == ArchNodeKind.Output ? 4f : 11f;
        canvas.FillColor = accent.WithAlpha(0.13f + 0.32f * glow);
        canvas.FillRoundedRectangle(r.X, r.Y, r.Width, r.Height, radius);

        // 模块左侧一条色条：一眼分得出这是哪一类东西。
        if (n.Kind == ArchNodeKind.Module)
        {
            canvas.FillColor = accent.WithAlpha(0.55f + 0.25f * glow);
            canvas.FillRoundedRectangle(r.X + 1.5f, r.Y + 6f, 3f, r.Height - 12f, 2);
        }
        if (n.Kind == ArchNodeKind.Output)
        {
            canvas.FillColor = accent.WithAlpha(0.40f);
            canvas.FillRoundedRectangle(r.Right - 16f, r.Y + 4f, 12f, r.Height - 8f, 3);
        }

        canvas.StrokeColor = glow > 0f
            ? GraphPalette.Blend(accent, highlight, 0.30f + 0.5f * glow).WithAlpha(0.62f + 0.38f * glow)
            : accent.WithAlpha(selected ? 0.95f : isMode ? 0.52f : 0.30f);
        canvas.StrokeSize = (selected ? 1.8f : 1f) + glow * 2.2f;
        if (isMode)
        {
            canvas.StrokeDashPattern = new[] { 5f, 4f };
            canvas.DrawRoundedRectangle(r.X, r.Y, r.Width, r.Height, radius);
            canvas.StrokeDashPattern = null;
        }
        else
        {
            canvas.DrawRoundedRectangle(r.X, r.Y, r.Width, r.Height, radius);
        }

        // 状态灯
        var baseR = Math.Max(2.8f, r.Height * 0.09f);
        var lampR = baseR * (1f + 0.8f * glow);
        canvas.FillColor = glow > 0f
            ? highlight.WithAlpha(0.55f + 0.45f * glow)
            : accent.WithAlpha(0.30f);
        canvas.FillCircle(r.X + 11 + lampR, r.Center.Y, lampR);

        var textX = r.X + 11 + lampR * 2 + 6;
        var textW = r.Width - (textX - r.X) - 10;
        var hasBadge = !string.IsNullOrWhiteSpace(n.Badge);
        canvas.Font = selected || glow > 0f || isMode || n.Kind == ArchNodeKind.Output
            ? Microsoft.Maui.Graphics.Font.DefaultBold
            : Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = _nodeFont;
        canvas.FontColor = glow > 0f ? highlight : ink.WithAlpha(0.94f);
        canvas.DrawString(Fit(n.Label, 13), textX, r.Y + (hasBadge ? -1 : 1),
            Math.Max(8f, textW), hasBadge ? r.Height * 0.55f : r.Height - 2,
            HorizontalAlignment.Left, VerticalAlignment.Center);

        // 实时读数：中文小字压在标签下面，比只写「某某模块」有信息量。
        if (hasBadge)
        {
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.FontSize = _smallFont;
            canvas.FontColor = accent.WithAlpha(0.80f);
            canvas.DrawString(Fit(n.Badge!, 20), textX, r.Y + r.Height * 0.42f,
                Math.Max(8f, textW), r.Height * 0.55f - 2,
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }
    }

    /// <summary>按模块分色：同一类东西在图上同一个颜色，比按层涂色好认。</summary>
    private Color AccentOf(ArchNode n)
    {
        var m = n.Module ?? "";
        if (m.StartsWith("InnerDrive", StringComparison.Ordinal) || m.StartsWith("DriveRules", StringComparison.Ordinal))
            return GraphPalette.Tag;
        if (m.StartsWith("CognitionMode", StringComparison.Ordinal))
            return GraphPalette.Emotion;
        if (m.StartsWith("HumanMemory", StringComparison.Ordinal) || m.StartsWith("EmotionMix", StringComparison.Ordinal))
            return GraphPalette.MemoryCard;
        if (m.StartsWith("ChatEngine", StringComparison.Ordinal))
            return GraphPalette.Person;
        if (m.StartsWith("OrganSimulation", StringComparison.Ordinal))
            return GraphPalette.Thing;
        if (m.StartsWith("ApiGateway", StringComparison.Ordinal))
            return GraphPalette.Associated;
        return LayerColor(n.Layer);
    }

    private Color LayerColor(string layer) =>
        LayerColors is not null && LayerColors.TryGetValue(layer, out var c)
            ? c
            : layer switch
            {
                "1" => GraphPalette.Place,
                "2" => GraphPalette.MemoryCard,
                "3" => GraphPalette.Person,
                "4" => GraphPalette.Thing,
                "5" => GraphPalette.Emotion,
                _ => GraphPalette.Unknown
            };

    /// <summary>画布坐标命中哪枚节点；点不中返回 null。</summary>
    public string? HitTest(double x, double y)
    {
        foreach (var kv in _rects)
        {
            var r = kv.Value;
            if (x >= r.X && x <= r.Right && y >= r.Y && y <= r.Bottom) return kv.Key;
        }
        return null;
    }

    public ArchNode? Find(string id) => _byId.TryGetValue(id, out var n) ? n : null;

    private static string Fit(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..(max - 1)] + "…");
}
