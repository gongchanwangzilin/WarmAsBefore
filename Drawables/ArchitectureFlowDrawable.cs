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
    private const float NodeH = 50f;
    private const float ModuleH = 54f;
    private const float ModeW = 158f;
    private const float ModeH = 50f;
    private const float GapX = 34f;
    private const float GapY = 26f;
    private const float BandPad = 20f;
    private const float HeaderH = 28f;
    private const float BandGap = 30f;
    private const float ModeGap = 26f;
    private const float OuterPad = 30f;
    private const float MinBandW = 1120f;
    private const float MaxBandW = 1480f;

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

    /// <summary>
    /// 层色覆盖：key 是 Layer（"1".."5"），value 直接用 GraphPalette 现有成员重钉即可。
    /// 不传则用内置的五层配色。
    /// </summary>
    public IReadOnlyDictionary<string, Color>? LayerColors { get; set; }

    /// <summary>自然内容尺寸变了：VM 据此改 GraphicsView 的请求宽高，让外面能滚。</summary>
    public event Action<float, float>? ContentSizeChanged;

    public float ContentWidth { get; private set; }
    public float ContentHeight { get; private set; }

    private readonly Dictionary<string, RectF> _rects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ArchNode> _byId = new(StringComparer.Ordinal);
    private readonly List<(string Layer, RectF Rect, Color Color)> _layerHeaders = new();
    private readonly List<(string Layer, RectF Rect, Color Color)> _bands = new();
    private readonly List<(string Layer, RectF Rect)> _modeRails = new();
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
        _layerHeaders.Clear();
        _bands.Clear();
        _modeRails.Clear();
        _groupFrames.Clear();

        var nodes = _nodes;
        if (nodes is null || nodes.Count == 0)
        {
            SetContent(0, 0);
            return;
        }
        foreach (var n in nodes) _byId[n.Id] = n;

        var bandW = Math.Clamp(Math.Max(_viewW - OuterPad * 2f, MinBandW), MinBandW, MaxBandW);
        var cols = (int)Math.Floor((bandW - 2 * BandPad - (ModeW + ModeGap) + GapX) / (NodeW + GapX));
        cols = Math.Clamp(cols, 2, 5);

        var layers = nodes.Select(n => n.Layer)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(l => int.TryParse(l, out var k) ? k : int.MaxValue)
            .ToList();

        var bands = new List<(string Layer, List<ArchNode> Modes, List<ArchNode> Steps, int Rows, float Height)>();
        foreach (var layer in layers)
        {
            var inBand = nodes.Where(n => string.Equals(n.Layer, layer, StringComparison.Ordinal)).ToList();
            var modes = inBand.Where(n => n.Kind == ArchNodeKind.Mode).ToList();
            var steps = OrderByFlow(inBand.Where(n => n.Kind != ArchNodeKind.Mode).ToList());
            var rows = Math.Max(1, (steps.Count + cols - 1) / cols);
            var modeStackH = modes.Count == 0 ? 0 : modes.Count * ModeH + (modes.Count - 1) * 12f;
            var stepStackH = steps.Count == 0 ? 0 : rows * NodeH + (rows - 1) * GapY;
            var bodyH = Math.Max(Math.Max(modeStackH, stepStackH), NodeH);
            bands.Add((layer, modes, steps, rows, HeaderH + BandPad * 2 + bodyH));
        }

        var totalH = OuterPad * 2 + bands.Sum(b => b.Height) + BandGap * (bands.Count - 1);
        var bandX = OuterPad;
        var y = OuterPad;

        foreach (var (layer, modes, steps, rows, height) in bands)
        {
            _layerHeaders.Add((layer, new RectF(bandX, y, bandW, HeaderH), LayerColor(layer)));
            _bands.Add((layer, new RectF(bandX, y, bandW, height), LayerColor(layer)));

            var bodyTop = y + HeaderH + BandPad;
            var bodyCenter = bodyTop + (height - HeaderH - BandPad * 2) / 2f;

            // 调控栏：整体模式竖排在带首，和流程节点分开，视觉上就是「管着这一层」而不是「在这一层里跑」。
            if (modes.Count > 0)
            {
                _modeRails.Add((layer, new RectF(bandX + BandPad, y + HeaderH * 0.4f, ModeW + 14f,
                    height - BandPad * 0.6f)));
                var stackH = modes.Count * ModeH + (modes.Count - 1) * 12f;
                var my = bodyCenter - stackH / 2f;
                foreach (var m in modes)
                {
                    _rects[m.Id] = new RectF(bandX + BandPad + 7f, my, ModeW, ModeH);
                    my += ModeH + 12f;
                }
            }

            var gridLeft = bandX + BandPad + (modes.Count > 0 ? ModeW + ModeGap : 0f);
            var gridW = bandW - (gridLeft - bandX) - BandPad;
            var gridCols = Math.Max(1, Math.Min(cols, (int)Math.Floor((gridW + GapX) / (NodeW + GapX))));
            var gridTop = bodyCenter - (rows * NodeH + (rows - 1) * GapY) / 2f;
            for (var i = 0; i < steps.Count; i++)
            {
                var row = i / gridCols;
                var col = i % gridCols;
                var inRow = Math.Min(gridCols, steps.Count - row * gridCols);
                var rowW = inRow * NodeW + (inRow - 1) * GapX;
                var rowLeft = gridLeft + Math.Max(0f, (gridW - rowW) / 2f);
                var x = rowLeft + col * (NodeW + GapX);
                var h = steps[i].Kind == ArchNodeKind.Module ? ModuleH : NodeH;
                var ry = gridTop + row * (NodeH + GapY) + (NodeH - h) / 2f;
                _rects[steps[i].Id] = new RectF(x, ry, NodeW, h);
            }

            y += height + BandGap;
        }

        BuildGroupFrames();
        SetContent(bandW + OuterPad * 2f, totalH);
    }

    /// <summary>
    /// 子系统框：把同 Group 的成员围起来（外扩留出标题位），让「五层记忆」「内驱动」这些
    /// 在图上是一块，而不是散在格里靠人猜。
    /// </summary>
    private void BuildGroupFrames()
    {
        var nodes = _nodes;
        if (nodes is null) return;
        foreach (var g in nodes.Where(n => !string.IsNullOrEmpty(n.Group)).GroupBy(n => n.Group!, StringComparer.Ordinal))
        {
            float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
            var any = false;
            foreach (var n in g)
            {
                if (!_rects.TryGetValue(n.Id, out var r)) continue;
                any = true;
                left = Math.Min(left, r.X);
                top = Math.Min(top, r.Y);
                right = Math.Max(right, r.Right);
                bottom = Math.Max(bottom, r.Bottom);
            }
            if (!any) continue;
            _groupFrames[g.Key] = new RectF(left - 12f, top - 20f, right - left + 24f, bottom - top + 30f);
        }
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
            canvas.DrawString("流程数据还没到位。", OuterPad, OuterPad + HeaderH,
                Math.Max(80f, dirtyRect.Width - OuterPad * 2), 28,
                HorizontalAlignment.Left, VerticalAlignment.Top);
            return;
        }

        // 顺序：层带 → 子系统框 → 连线 → 节点。线压在框上、节点压在线头上，层次才读得出来。
        DrawBands(canvas, ink);
        DrawGroups(canvas, ink);
        DrawLinks(canvas);
        foreach (var n in _nodes ?? Array.Empty<ArchNode>())
            if (_rects.TryGetValue(n.Id, out var r)) DrawNode(canvas, n, r, ink);
    }

    private void DrawBands(ICanvas canvas, Color ink)
    {
        foreach (var (layer, r, color) in _bands)
        {
            canvas.FillColor = color.WithAlpha(0.045f);
            canvas.FillRoundedRectangle(r.X, r.Y, r.Width, r.Height, 14);
            canvas.FillColor = color.WithAlpha(0.55f);
            canvas.FillRoundedRectangle(r.X, r.Y, 3f, r.Height, 2);
        }

        foreach (var (layer, r, color) in _layerHeaders)
        {
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.FontSize = Math.Max(10.5f, _nodeFont - 1.5f);
            canvas.FontColor = ink.WithAlpha(0.75f);
            canvas.DrawString(LayerTitle(layer), r.X + 14, r.Y, r.Width - 28, r.Height,
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }

        foreach (var (_, r) in _modeRails)
        {
            canvas.StrokeColor = GraphPalette.Tag.WithAlpha(0.20f);
            canvas.StrokeSize = 1f;
            canvas.StrokeDashPattern = new[] { 4f, 5f };
            canvas.DrawRoundedRectangle(r.X, r.Y, r.Width, r.Height, 12);
            canvas.StrokeDashPattern = null;
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.FontSize = _smallFont;
            canvas.FontColor = GraphPalette.Tag.WithAlpha(0.72f);
            canvas.DrawString("整体模式", r.X + 9, r.Y + 2, r.Width - 18, 14,
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }
    }

    private void DrawGroups(ICanvas canvas, Color ink)
    {
        foreach (var (id, r) in _groupFrames)
        {
            canvas.FillColor = GraphPalette.MemoryCard.WithAlpha(0.05f);
            canvas.FillRoundedRectangle(r.X, r.Y, r.Width, r.Height, 16);
            canvas.StrokeColor = GraphPalette.MemoryCard.WithAlpha(0.30f);
            canvas.StrokeSize = 1.1f;
            canvas.StrokeDashPattern = new[] { 6f, 5f };
            canvas.DrawRoundedRectangle(r.X, r.Y, r.Width, r.Height, 16);
            canvas.StrokeDashPattern = null;

            var title = GroupTitles is not null && GroupTitles.TryGetValue(id, out var t) ? t : id;
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.FontSize = _smallFont + 0.5f;
            canvas.FontColor = ink.WithAlpha(0.62f);
            canvas.DrawString(title, r.X + 10, r.Y + 2, r.Width - 20, 14,
                HorizontalAlignment.Left, VerticalAlignment.Center);
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

        canvas.StrokeColor = color;
        canvas.StrokeSize = severed ? 1.6f : e.Kind is LinkKind.Bidirectional or LinkKind.Control ? 1.5f : 1.2f;
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

    private static string LayerTitle(string layer) => layer switch
    {
        "1" => "1 · 感知与输入抽象层",
        "2" => "2 · 记忆与情绪双向耦合",
        "3" => "3 · 核心心智与病理",
        "4" => "4 · 低级中枢熔断与躯体化",
        "5" => "5 · .NET 工程落地",
        _ => "层 " + layer
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
