using Microsoft.Maui.Graphics;
using WarmAsBefore.DesignSystem.Worlds;

namespace WarmAsBefore.Drawables;

/// <summary>节点在流程里的身份：决定大小、形状与画法，而不是只决定颜色。</summary>
public enum ArchNodeKind
{
    /// <summary>最小节点：一次具体处理，小方框。</summary>
    Step = 0,
    /// <summary>模块：一组处理构成的子系统，中等方框。</summary>
    Module = 1,
    /// <summary>整体模式 / 调控者：跨层生效、可以整体停用，画成左侧调控栏里的胶囊，用虚线控制别人。</summary>
    Mode = 2,
    /// <summary>输出：流程的落点，画成右端强调的方框。</summary>
    Output = 3
}

/// <summary>活流程图上的一个节点。位置由 drawable 按 Layer 与流序排，VM 只给身份、文案、所属层与身份类别。</summary>
public sealed record ArchNode(
    string Id,
    string Label,
    string Layer,
    string? Module = null,
    string? Detail = null,
    ArchNodeKind Kind = ArchNodeKind.Step);

/// <summary>活流程图上的一条联系：按 Id 连，两端都排好位才画。</summary>
public sealed record ArchLink(string From, string To, LinkKind Kind);

/// <summary>论文 Mermaid 的四种箭头：--&gt; 数据流、==&gt; 双向强、-.&gt; 反馈虚线、--o 调控（模式对模块的控制）。</summary>
public enum LinkKind
{
    Normal,
    Bidirectional,
    Feedback,
    Control
}

/// <summary>
/// 论文《面向类人情感模拟的神经符号心智架构》的活流程图。
///
/// 布局：五层各自一行横带，带内先算流序（层内边做拓扑排序），所以从左往右读就是数据走过的顺序；
/// 整体模式（专注 / 神游 / 内驱动这类可以整体停用、跨层生效的东西）不混在流程里，
/// 单独排在带首的调控栏，用虚线控制它们调控的模块——这样「总开关」和「一个零件」长得不一样。
///
/// 尺寸：按内容算自然宽高（不再压进一个固定框里等比缩小），VM 把结果绑到 GraphicsView 的
/// Width/HeightRequest 上，外面套 ScrollView 滚动。挤成一团是因为以前硬把 65 个节点塞进视口。
///
/// 连线：正交折线（横 → 竖 → 横），带箭头；同类节点之间走带内通道，跨带走层间通道，
/// 反馈线从右侧绕回，调控线是虚线并带一个源点。绘制顺序是层带 → 连线 → 节点，线不会被方块吃掉。
///
/// 亮灯：<see cref="GlowProvider"/> 由 VM 注入（对齐 FlowTracer.Glow），返回 0..1 的余辉强度；
/// 数据驱动按需 Invalidate，drawable 自己不起定时器。颜色只从 <see cref="GraphPalette"/> 与
/// <see cref="StyleWorldTokens"/> 取：零裸 hex、零新 Token、零 Shadow。
/// </summary>
public sealed class ArchitectureFlowDrawable : IDrawable
{
    // ---- 设计尺寸：自然布局，不随视口等比缩 ----
    private const float NodeW = 172f;
    private const float NodeH = 46f;
    private const float ModuleH = 50f;
    private const float ModeW = 156f;
    private const float ModeH = 48f;
    private const float GapX = 30f;
    private const float GapY = 18f;
    private const float BandPad = 16f;
    private const float HeaderH = 26f;
    private const float BandGap = 20f;
    private const float ModeGap = 22f;
    private const float OuterPad = 26f;
    private const float MinBandW = 1080f;
    private const float MaxBandW = 1440f;

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
        set { _links = value; }
    }

    public string? SelectedId { get; set; }

    /// <summary>VM 注入的余辉强度：id -> 0..1。空则所有节点按暗态画。</summary>
    public Func<string, float>? GlowProvider { get; set; }

    /// <summary>
    /// 层色覆盖：key 是 Layer（"1".."5"），value 直接用 GraphPalette 现有成员重钉即可。
    /// 不传则用内置的五层配色。
    /// </summary>
    public IReadOnlyDictionary<string, Color>? LayerColors { get; set; }

    /// <summary>自然内容尺寸变了：VM 据此改 GraphicsView 的请求宽高，让外面能滚。</summary>
    public event Action<float, float>? ContentSizeChanged;

    /// <summary>当前内容的自然宽高。</summary>
    public float ContentWidth { get; private set; }
    public float ContentHeight { get; private set; }

    // 绘制和命中共用这一份矩形；层标题矩形单独存，供绘制层带。
    private readonly Dictionary<string, RectF> _rects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ArchNode> _byId = new(StringComparer.Ordinal);
    private readonly List<(string Layer, RectF Rect, Color Color)> _layerHeaders = new();
    private readonly List<(string Layer, RectF Rect, Color Color)> _bands = new();
    private readonly List<(string Layer, RectF Rect)> _modeRails = new();
    private float _viewW;
    private float _viewH;
    private float _nodeFont = 12f;
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

        var nodes = _nodes;
        if (nodes is null || nodes.Count == 0)
        {
            SetContent(0, 0);
            return;
        }
        foreach (var n in nodes) _byId[n.Id] = n;

        // 带宽：视口够宽就铺满，太窄就给一个下限，靠 ScrollView 横向滚，不把字压小。
        var bandW = Math.Clamp(Math.Max(_viewW - OuterPad * 2f, MinBandW), MinBandW, MaxBandW);
        var cols = (int)Math.Floor((bandW - 2 * BandPad - (ModeW + ModeGap) + GapX) / (NodeW + GapX));
        cols = Math.Clamp(cols, 2, 5);

        var layers = nodes.Select(n => n.Layer)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(l => int.TryParse(l, out var k) ? k : int.MaxValue)
            .ToList();

        // 每层的成员与流序
        var bands = new List<(string Layer, List<ArchNode> Modes, List<ArchNode> Steps, int Rows, float Height)>();
        foreach (var layer in layers)
        {
            var inBand = nodes.Where(n => string.Equals(n.Layer, layer, StringComparison.Ordinal)).ToList();
            var modes = inBand.Where(n => n.Kind == ArchNodeKind.Mode).ToList();
            var steps = OrderByFlow(inBand.Where(n => n.Kind != ArchNodeKind.Mode).ToList());
            var rows = Math.Max(1, (steps.Count + cols - 1) / cols);
            var modeStackH = modes.Count == 0 ? 0 : modes.Count * ModeH + (modes.Count - 1) * 10f;
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

            // 调控栏：整体模式竖着排在带首，与流程节点分开，视觉上就是「管着这一层」而不是「在这一层里跑」。
            if (modes.Count > 0)
            {
                var railH = height - BandPad * 0.5f;
                _modeRails.Add((layer, new RectF(bandX + BandPad, y + HeaderH * 0.35f, ModeW + 12f, railH)));
                var stackH = modes.Count * ModeH + (modes.Count - 1) * 10f;
                var my = bodyCenter - stackH / 2f;
                foreach (var m in modes)
                {
                    _rects[m.Id] = new RectF(bandX + BandPad + 6f, my, ModeW, ModeH);
                    my += ModeH + 10f;
                }
            }

            // 流程节点：按流序铺行，行内居中，读起来是「左边先、右边后」。
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

        SetContent(bandW + OuterPad * 2f, totalH);
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
        var queue = new List<string>(list.Where(n => indegree[n.Id] == 0).Select(n => n.Id));
        var ordered = new List<string>(list.Count);
        while (queue.Count > 0)
        {
            // 入度为零的里优先按 VM 原序取，避免同层平级节点被随意打乱。
            var next = list.Where(n => queue.Contains(n.Id)).Select(n => n.Id).First();
            queue.Remove(next);
            ordered.Add(next);
            foreach (var to in outMap[next])
            {
                if (--indegree[to] <= 0) queue.Add(to);
            }
        }
        if (ordered.Count != list.Count) return list; // 有环：退回原序
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

        // 顺序：层带 → 调控栏 → 连线 → 节点。线在节点下面，但清清爽爽压在不透明层带之上。
        DrawBands(canvas, ink);
        DrawLinks(canvas);
        foreach (var n in _nodes ?? Array.Empty<ArchNode>())
            if (_rects.TryGetValue(n.Id, out var r)) DrawNode(canvas, n, r, ink);
    }

    private void DrawBands(ICanvas canvas, Color ink)
    {
        foreach (var (layer, r, color) in _bands)
        {
            // 整条横带：极淡底 + 层色左缘，读起来是一段流程区，不像一排独立的卡。
            canvas.FillColor = color.WithAlpha(0.05f);
            canvas.FillRoundedRectangle(r.X, r.Y, r.Width, r.Height, 12);
            canvas.FillColor = color.WithAlpha(0.55f);
            canvas.FillRoundedRectangle(r.X, r.Y, 3f, r.Height, 2);
        }

        foreach (var (layer, r, color) in _layerHeaders)
        {
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.FontSize = Math.Max(10.5f, _nodeFont - 1f);
            canvas.FontColor = ink.WithAlpha(0.78f);
            canvas.DrawString(LayerTitle(layer), r.X + 12, r.Y, r.Width - 24, r.Height,
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }

        foreach (var (_, r) in _modeRails)
        {
            canvas.FillColor = GraphPalette.Tag.WithAlpha(0.05f);
            canvas.FillRoundedRectangle(r.X, r.Y, r.Width, r.Height, 9);
            canvas.StrokeColor = GraphPalette.Tag.WithAlpha(0.22f);
            canvas.StrokeSize = 1f;
            canvas.StrokeDashPattern = new[] { 4f, 4f };
            canvas.DrawRoundedRectangle(r.X, r.Y, r.Width, r.Height, 9);
            canvas.StrokeDashPattern = null;
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.FontSize = _smallFont;
            canvas.FontColor = GraphPalette.Tag.WithAlpha(0.75f);
            canvas.DrawString("整体模式", r.X + 8, r.Y + 1, r.Width - 16, 14,
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }
    }

    private void DrawLinks(ICanvas canvas)
    {
        var links = _links;
        if (links is null || links.Count == 0) return;

        // 调控线先画（最淡最底），数据流其次，反馈最后——重要的线压在上面。
        foreach (var e in links.Where(e => e.Kind == LinkKind.Control)) DrawLink(canvas, e);
        foreach (var e in links.Where(e => e.Kind == LinkKind.Normal)) DrawLink(canvas, e);
        foreach (var e in links.Where(e => e.Kind == LinkKind.Bidirectional)) DrawLink(canvas, e);
        foreach (var e in links.Where(e => e.Kind == LinkKind.Feedback)) DrawLink(canvas, e);
    }

    private void DrawLink(ICanvas canvas, ArchLink e)
    {
        if (!_rects.TryGetValue(e.From, out var a) || !_rects.TryGetValue(e.To, out var b)) return;

        var normal = GraphPalette.Unknown.WithAlpha(0.40f);
        var bi = GraphPalette.Associated.WithAlpha(0.62f);
        var fb = GraphPalette.Causal.WithAlpha(0.58f);
        var ctrl = GraphPalette.Tag.WithAlpha(0.55f);
        var color = e.Kind switch
        {
            LinkKind.Bidirectional => bi,
            LinkKind.Feedback => fb,
            LinkKind.Control => ctrl,
            _ => normal
        };

        var pts = Route(a, b, e.Kind);
        if (pts.Count < 2) return;

        canvas.StrokeColor = color;
        canvas.StrokeSize = e.Kind is LinkKind.Bidirectional or LinkKind.Control ? 1.5f : 1.2f;
        canvas.StrokeDashPattern = e.Kind is LinkKind.Feedback or LinkKind.Control ? new[] { 5f, 4f } : null;
        canvas.StrokeLineJoin = LineJoin.Round;

        var path = new PathF();
        path.MoveTo(pts[0].X, pts[0].Y);
        for (var i = 1; i < pts.Count; i++) path.LineTo(pts[i].X, pts[i].Y);
        canvas.DrawPath(path);
        canvas.StrokeDashPattern = null;

        var last = pts[^1];
        var prev = pts[^2];
        var dx = last.X - prev.X;
        var dy = last.Y - prev.Y;
        var len = (float)Math.Sqrt(dx * dx + dy * dy);
        if (len > 0.5f) DrawArrow(canvas, last.X, last.Y, dx / len, dy / len, 7f, color);

        if (e.Kind == LinkKind.Bidirectional)
        {
            var first = pts[0];
            var second = pts[1];
            var ex = first.X - second.X;
            var ey = first.Y - second.Y;
            var l2 = (float)Math.Sqrt(ex * ex + ey * ey);
            if (l2 > 0.5f) DrawArrow(canvas, first.X, first.Y, ex / l2, ey / l2, 7f, color);
        }
        else if (e.Kind == LinkKind.Control)
        {
            // 调控线的源头画一个小空心点：表达「从这里管过去」而不是「数据流过去」。
            canvas.FillColor = color;
            canvas.FillCircle(pts[0].X, pts[0].Y, 3f);
        }
    }

    /// <summary>
    /// 正交路由：同带内横着走一道通道，跨带竖向走层间通道，反馈从右侧绕回。
    /// 折线比直线更容易看出「谁指向谁」，也不会整段藏在方块底下。
    /// </summary>
    private static List<PointF> Route(RectF a, RectF b, LinkKind kind)
    {
        var pts = new List<PointF>(6);
        var sameBand = Math.Abs((a.Bottom + a.Top) / 2 - (b.Bottom + b.Top) / 2) < a.Height + b.Height;

        if (kind == LinkKind.Control)
        {
            // 调控线走左侧总线：从模式块左边出去，沿最左的通道上下，再横进目标左侧。
            // 这样「谁管着谁」一眼能顺下来，也不会横穿中间的节点。
            var busX = Math.Max(6f, a.Left - 12f);
            var sx = a.Left;
            var sy = a.Center.Y;
            var ty = b.Center.Y;
            var tx = b.Left - 6f;
            pts.Add(new PointF(sx, sy));
            pts.Add(new PointF(busX, sy));
            if (Math.Abs(sy - ty) > 0.5f) pts.Add(new PointF(busX, ty));
            pts.Add(new PointF(tx, ty));
            return pts;
        }

        if (sameBand && Math.Abs(a.Center.Y - b.Center.Y) < 6f && Math.Abs(a.Center.X - b.Center.X) > a.Width)
        {
            // 同一行左右相邻：横穿一道。
            var right = b.Center.X > a.Center.X;
            var x1 = right ? a.Right : a.Left;
            var x2 = right ? b.Left : b.Right;
            var y = a.Center.Y;
            pts.Add(new PointF(x1, y));
            pts.Add(new PointF(x2, y));
            return pts;
        }

        if (b.Top >= a.Bottom - 1f)
        {
            // 往下走：下边出、上边进，中间一道层间通道。
            var sx = a.Center.X;
            var sy = a.Bottom;
            var ex = b.Center.X;
            var ey = b.Top;
            var midY = sy + Math.Max(10f, (ey - sy) * 0.5f);
            pts.Add(new PointF(sx, sy));
            pts.Add(new PointF(sx, midY));
            pts.Add(new PointF(ex, midY));
            pts.Add(new PointF(ex, ey));
            return pts;
        }

        if (b.Bottom <= a.Top + 1f)
        {
            // 往上走（反馈）：从右侧绕出去再回到目标底边，不压在中间那些节点上。
            var sx = a.Right;
            var sy = a.Center.Y;
            var ex = b.Center.X;
            var ey = b.Bottom;
            // 右侧只有 OuterPad 的空隙，绕行通道贴着它走，不许画出画布外面。
            var channelX = Math.Max(a.Right, b.Right) + 10f;
            pts.Add(new PointF(sx, sy));
            pts.Add(new PointF(channelX, sy));
            pts.Add(new PointF(channelX, ey + 10f));
            pts.Add(new PointF(ex, ey + 10f));
            pts.Add(new PointF(ex, ey));
            return pts;
        }

        // 同一带但不同行：走左侧短通道。
        var sxx = a.Center.X;
        var syy = a.Center.Y;
        var exx = b.Center.X;
        var eyy = b.Center.Y;
        var laneX = Math.Min(a.Left, b.Left) - 10f;
        pts.Add(new PointF(sxx, syy));
        pts.Add(new PointF(laneX, syy));
        pts.Add(new PointF(laneX, eyy));
        pts.Add(new PointF(exx, eyy));
        return pts;
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
        var selected = n.Id == SelectedId;
        var highlight = GraphPalette.Tag;
        var isMode = n.Kind == ArchNodeKind.Mode;
        var radius = isMode ? r.Height / 2f : 7f;

        // 填充：模式块用层色淡底 + 标签色描边，和流程节点区分开。
        canvas.FillColor = isMode
            ? LayerColor(n.Layer).WithAlpha(0.22f + 0.30f * glow)
            : LayerColor(n.Layer).WithAlpha(0.16f + 0.34f * glow);
        canvas.FillRoundedRectangle(r.X, r.Y, r.Width, r.Height, radius);

        canvas.StrokeColor = glow > 0f
            ? GraphPalette.Blend(ink, highlight, 0.35f + 0.5f * glow).WithAlpha(0.60f + 0.40f * glow)
            : isMode
                ? highlight.WithAlpha(selected ? 0.95f : 0.55f)
                : ink.WithAlpha(selected ? 0.85f : 0.32f);
        canvas.StrokeSize = (selected ? 1.8f : 1f) + glow * 2.2f;
        canvas.DrawRoundedRectangle(r.X, r.Y, r.Width, r.Height, radius);

        // 状态灯：灭 = 暗点；亮 = 高亮且随余辉放大。
        var baseR = Math.Max(2.8f, r.Height * 0.10f);
        var lampR = baseR * (1f + 0.8f * glow);
        canvas.FillColor = glow > 0f
            ? highlight.WithAlpha(0.55f + 0.45f * glow)
            : GraphPalette.Unknown.WithAlpha(0.35f);
        canvas.FillCircle(r.X + 10 + lampR, r.Center.Y, lampR);

        var textX = r.X + 10 + lampR * 2 + 5;
        canvas.Font = selected || glow > 0f || isMode || n.Kind == ArchNodeKind.Output
            ? Microsoft.Maui.Graphics.Font.DefaultBold
            : Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = _nodeFont;
        canvas.FontColor = glow > 0f ? highlight : ink.WithAlpha(0.94f);
        canvas.DrawString(Fit(n.Label, 13), textX, r.Y + 1,
            Math.Max(8f, r.Width - (textX - r.X) - 6), r.Height - 2,
            HorizontalAlignment.Left, VerticalAlignment.Center);
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

    /// <summary>按 id 找节点，VM 展开详情时用。</summary>
    public ArchNode? Find(string id) => _byId.TryGetValue(id, out var n) ? n : null;

    private static string Fit(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..(max - 1)] + "…");
}
