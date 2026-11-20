using Microsoft.Maui.Graphics;
using WarmAsBefore.DesignSystem.Worlds;

namespace WarmAsBefore.Drawables;

/// <summary>活流程图上的一个节点。位置由 drawable 按 Layer 排，VM 只给身份、文案与所属层。</summary>
public sealed record ArchNode(string Id, string Label, string Layer, string? Module = null, string? Detail = null);

/// <summary>活流程图上的一条联系：按 Id 连，两端都排好位才画。</summary>
public sealed record ArchLink(string From, string To, LinkKind Kind);

/// <summary>论文 Mermaid 的三种箭头：--&gt; 普通、==&gt; 双向强、-.&gt; 反馈虚线。</summary>
public enum LinkKind
{
    Normal,
    Bidirectional,
    Feedback
}

/// <summary>
/// 论文《面向类人情感模拟的神经符号心智架构》底部 5 层架构的活流程图。
///
/// 布局：层纵向堆叠（每层顶部一条层标题带），层内节点按网格排布；宽屏多列、窄屏自动缩列并整体等比收，
/// 字号下限 9.5，再窄就让 GraphicsView 自己滚动，不把字压成噪点。绘制与命中共用同一份矩形字典。
///
/// 亮灯：<see cref="GlowProvider"/> 由 VM 注入（对齐 FlowTracer.Glow），返回 0..1 的余辉强度。
/// 这是数据驱动的按需 Invalidate —— trace 变了 VM 调 Invalidate，drawable 自己不起定时器、不循环动画；
/// 也不写任何 Opacity 属性，明暗只用 GraphicsView 颜色的 alpha。
///
/// 颜色只从 <see cref="GraphPalette"/> 与 <see cref="StyleWorldTokens"/> 取：零裸 hex、零新 Token、零 Shadow。
///
/// 最小构造示例（Layer 取值为 "1".."5"，对应论文五层；节点/连线全部由 VM 注入，这里不硬编码）：
/// <code>
/// var nodes = new List&lt;ArchNode&gt;
/// {
///     new("in.text", "文字输入", "1", module: "input",  detail: "文本入口"),
///     new("mem.retrieval", "记忆检索器", "2", module: "memory", detail: "检索"),
///     new("mind.trauma", "创伤节点", "3", module: "mind",   detail: "核心信念"),
///     new("soma.coping", "应对机制", "4", module: "soma",   detail: "战/逃/僵"),
///     new("eng.router", "意图路由", "5", module: "engine", detail: "分发")
/// };
/// var links = new List&lt;ArchLink&gt;
/// {
///     new("in.text", "mem.retrieval", LinkKind.Normal),
///     new("mem.retrieval", "mind.trauma", LinkKind.Bidirectional),
///     new("eng.router", "in.text", LinkKind.Feedback)
/// };
/// var d = new ArchitectureFlowDrawable { Nodes = nodes, Links = links, GlowProvider = FlowTracer.Glow };
/// </code>
/// </summary>
public sealed class ArchitectureFlowDrawable : IDrawable
{
    // ---- 设计尺寸（窄屏按宽度等比收，字号另设下限） ----
    private const float NodeW = 136f;
    private const float NodeH = 36f;
    private const float GapX = 8f;
    private const float GapY = 6f;
    private const float LeftPad = 10f;
    private const float RightPad = 10f;
    private const float TopPad = 6f;
    private const float HeaderH = 20f;
    private const float LayerGap = 12f;
    private const float MinNodeW = 84f;
    private const int MaxCols = 8;

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
    /// 层色覆盖：key 是 Layer（"1".."5"），value 直接用 GraphPalette 现有成员重钉即可，例如
    /// new Dictionary&lt;string, Color&gt; { ["1"] = GraphPalette.Place, ["5"] = GraphPalette.Emotion }。
    /// 不传则用内置的五层配色。
    /// </summary>
    public IReadOnlyDictionary<string, Color>? LayerColors { get; set; }

    // 绘制和命中共用这一份矩形；层标题矩形单独存，供绘制层带。
    private readonly Dictionary<string, RectF> _rects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ArchNode> _byId = new(StringComparer.Ordinal);
    private readonly List<(string Layer, RectF Rect, Color Color)> _layerHeaders = new();
    private float _viewW;
    private float _viewH;
    private float _nodeFont = 12f;

    public void SetCanvasSize(double w, double h)
    {
        if (Math.Abs(_viewW - w) < 0.5 && Math.Abs(_viewH - h) < 0.5) return;
        _viewW = (float)w;
        _viewH = (float)h;
        Layout();
    }

    private void Layout()
    {
        _rects.Clear();
        _byId.Clear();
        _layerHeaders.Clear();

        var nodes = _nodes;
        if (nodes is null || nodes.Count == 0) return;
        foreach (var n in nodes) _byId[n.Id] = n;

        var availW = Math.Max(120f, _viewW - LeftPad - RightPad);
        // 列数先按「节点至少 MinNodeW 宽」定，再算单一等比 scale。
        var cols = Math.Clamp((int)Math.Floor((availW + GapX) / (MinNodeW + GapX)), 1, MaxCols);
        var nw = (availW - (cols - 1) * GapX) / cols;
        // 以前只往下缩（nw 封顶 NodeW）：画布一大，136 宽的小方块就缩在左上角，
        // 右半张与下半张空着，读起来像"没排完"。这里允许适度放大到 1.35 档，
        // 让五层拓扑在大画布上真正铺开；字号、圆角、状态灯都跟着同一个 scale 走。
        nw = Math.Clamp(nw, MinNodeW, NodeW * 1.35f);
        var scale = nw / NodeW;
        var nh = Math.Max(28f, NodeH * Math.Max(0.74f, scale));
        var gx = GapX * Math.Max(0.55f, scale);
        var gy = GapY * Math.Max(0.60f, scale);
        _nodeFont = Math.Max(9.5f, 12f * Math.Max(0.82f, scale));

        var layers = nodes.Select(n => n.Layer)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(l => int.TryParse(l, out var k) ? k : int.MaxValue)
            .ToList();

        // 第一遍只算高度：整块拓扑在画布里居中（上下留白对称），而不是永远贴顶。
        var placed = new List<(string Layer, List<ArchNode> Nodes)>(layers.Count);
        var totalH = TopPad;
        foreach (var layer in layers)
        {
            var list = nodes.Where(n => string.Equals(n.Layer, layer, StringComparison.Ordinal)).ToList();
            placed.Add((layer, list));
            var rows = (list.Count + cols - 1) / cols;
            totalH += HeaderH + rows * (nh + gy) + LayerGap;
        }
        var contentW = Math.Min(availW, cols * (nw + gx) - gx);
        var offX = LeftPad + Math.Max(0f, (availW - contentW) * 0.5f);
        var offY = Math.Max(0f, (_viewH - totalH + LayerGap) * 0.5f);

        var y = TopPad + offY;
        foreach (var (layer, list) in placed)
        {
            _layerHeaders.Add((layer, new RectF(offX, y, contentW, HeaderH), LayerColor(layer)));
            y += HeaderH;

            var rows = (list.Count + cols - 1) / cols;
            for (var i = 0; i < list.Count; i++)
            {
                var col = i % cols;
                var row = i / cols;
                var x = offX + col * (nw + gx);
                var ry = y + row * (nh + gy);
                _rects[list[i].Id] = new RectF(x, ry, nw, nh);
            }
            y += rows * (nh + gy) + LayerGap;
        }
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Math.Abs(_viewW - dirtyRect.Width) > 0.5 || Math.Abs(_viewH - dirtyRect.Height) > 0.5)
        {
            _viewW = dirtyRect.Width;
            _viewH = dirtyRect.Height;
            Layout();
        }

        var ink = StyleWorldTokens.Pick("WarmDark", GraphPalette.Unknown);
        var body = StyleWorldTokens.Pick("WarmBody", GraphPalette.Unknown);

        DrawLayerHeaders(canvas, ink);
        DrawLinks(canvas);

        var nodes = _nodes;
        if (nodes is null || nodes.Count == 0)
        {
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.FontSize = 12;
            canvas.FontColor = body.WithAlpha(0.7f);
            canvas.DrawString("流程数据还没到位。", LeftPad, TopPad + HeaderH + 8,
                Math.Max(80f, dirtyRect.Width - LeftPad - RightPad), 28,
                HorizontalAlignment.Left, VerticalAlignment.Top);
            return;
        }

        foreach (var n in nodes)
            if (_rects.TryGetValue(n.Id, out var r)) DrawNode(canvas, n, r, ink);
    }

    private void DrawLayerHeaders(ICanvas canvas, Color ink)
    {
        foreach (var (layer, r, color) in _layerHeaders)
        {
            canvas.FillColor = color.WithAlpha(0.10f);
            canvas.FillRoundedRectangle(r.X, r.Y, r.Width, r.Height, 5);
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.FontSize = Math.Max(10f, _nodeFont - 0.5f);
            canvas.FontColor = ink.WithAlpha(0.8f);
            canvas.DrawString(LayerTitle(layer), r.X + 6, r.Y, r.Width - 12, r.Height,
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }
    }

    private void DrawLinks(ICanvas canvas)
    {
        var links = _links;
        if (links is null || links.Count == 0) return;

        var normal = GraphPalette.Unknown.WithAlpha(0.42f);      // Normal：灰实线
        var bi = GraphPalette.Associated.WithAlpha(0.55f);       // 双向：实线 + 两端箭头
        var fb = GraphPalette.Causal.WithAlpha(0.52f);           // 反馈：虚线 + 箭头

        foreach (var e in links)
        {
            if (!_rects.TryGetValue(e.From, out var a) || !_rects.TryGetValue(e.To, out var b)) continue;
            var (x1, y1, x2, y2) = EdgePoints(a, b);
            var dx = x2 - x1;
            var dy = y2 - y1;
            var len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1f) continue;
            var ux = dx / len;
            var uy = dy / len;
            var arrow = Math.Clamp(Math.Min(a.Width, a.Height) * 0.26f, 4f, 7f);

            var color = e.Kind switch
            {
                LinkKind.Bidirectional => bi,
                LinkKind.Feedback => fb,
                _ => normal
            };
            canvas.StrokeDashPattern = e.Kind == LinkKind.Feedback ? new[] { 5f, 4f } : null;
            canvas.StrokeSize = e.Kind == LinkKind.Bidirectional ? 1.5f : 1.1f;
            canvas.StrokeColor = color;
            canvas.DrawLine(x1, y1, x2, y2);
            canvas.StrokeDashPattern = null;

            if (e.Kind == LinkKind.Bidirectional)
                DrawArrow(canvas, x1, y1, -ux, -uy, arrow, color);
            DrawArrow(canvas, x2, y2, ux, uy, arrow, color);
        }
    }

    /// <summary>按主方向取两端边缘中点：纵向连下/上边，横向连右/左边，线从边出发不穿节点。</summary>
    private static (float x1, float y1, float x2, float y2) EdgePoints(RectF a, RectF b)
    {
        var ac = a.Center;
        var bc = b.Center;
        var dx = bc.X - ac.X;
        var dy = bc.Y - ac.Y;

        if (Math.Abs(dy) >= Math.Abs(dx))
        {
            var y1 = dy >= 0 ? a.Bottom : a.Top;
            var y2 = dy >= 0 ? b.Top : b.Bottom;
            var x = MidClamped(ac.X, bc.X, Math.Max(a.Left, b.Left), Math.Min(a.Right, b.Right));
            return (x, y1, x, y2);
        }
        else
        {
            var x1 = dx >= 0 ? a.Right : a.Left;
            var x2 = dx >= 0 ? b.Left : b.Right;
            var y = MidClamped(ac.Y, bc.Y, Math.Max(a.Top, b.Top), Math.Min(a.Bottom, b.Bottom));
            return (x1, y, x2, y);
        }
    }

    private static float MidClamped(float c1, float c2, float lo, float hi)
    {
        var mid = (c1 + c2) * 0.5f;
        return lo <= hi ? Math.Clamp(mid, lo, hi) : mid;
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

        // 填充：余辉越强，层色越深。
        canvas.FillColor = LayerColor(n.Layer).WithAlpha(0.14f + 0.30f * glow);
        canvas.FillRoundedRectangle(r.X, r.Y, r.Width, r.Height, 6);

        // 描边：选中给基础加粗；余辉再加粗并向高亮色位移。
        canvas.StrokeColor = glow > 0f
            ? GraphPalette.Blend(ink, highlight, 0.35f + 0.5f * glow).WithAlpha(0.58f + 0.42f * glow)
            : ink.WithAlpha(selected ? 0.85f : 0.30f);
        canvas.StrokeSize = (selected ? 1.8f : 1f) + glow * 2.2f;
        canvas.DrawRoundedRectangle(r.X, r.Y, r.Width, r.Height, 6);

        // 状态灯：灭 = Unknown 暗点；亮 = Tag 高亮且随余辉放大。
        var baseR = Math.Max(2.8f, r.Height * 0.11f);
        var lampR = baseR * (1f + 0.8f * glow);
        canvas.FillColor = glow > 0f
            ? highlight.WithAlpha(0.55f + 0.45f * glow)
            : GraphPalette.Unknown.WithAlpha(0.35f);
        canvas.FillCircle(r.X + 8 + lampR, r.Center.Y, lampR);

        var textX = r.X + 8 + lampR * 2 + 4;
        canvas.Font = selected || glow > 0f ? Microsoft.Maui.Graphics.Font.DefaultBold : Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = _nodeFont;
        canvas.FontColor = glow > 0f ? highlight : ink.WithAlpha(0.92f);
        canvas.DrawString(Fit(n.Label, 14), textX, r.Y + 1,
            Math.Max(8f, r.Width - (textX - r.X) - 4), r.Height - 2,
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
