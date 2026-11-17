using Microsoft.Maui.Graphics;
using WarmAsBefore.DesignSystem.Worlds;

namespace WarmAsBefore.Drawables;

/// <summary>流程图上的一个节点：VM 只给「哪一组、叫什么、什么状态、说明是什么」，位置由 drawable 自己排。</summary>
public sealed record MonitorFlowNode
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string Group { get; init; } = "";
    public string Status { get; init; } = "";
    public string Detail { get; init; } = "";
}

/// <summary>流程图上的一条联系：按 Id 连，drawable 只在两枚节点都排好位时画。</summary>
public sealed record MonitorFlowLink
{
    public string From { get; init; } = "";
    public string To { get; init; } = "";
}

/// <summary>
/// 超级监测页的流程图：日程 / 器官 / 记忆 / 数值 四组节点，组内竖排、组间连线。
/// 与 MemoryGraphDrawable 同一套路 —— 位置在世界坐标里算好后自己投到屏幕，不用 canvas 的 transform；
/// 节点矩形同时就是命中区，点一下就能把 id 交回 VM 展开详情。
///
/// 颜色只从 <see cref="GraphPalette"/>（已经按当前世界重钉过）和 <see cref="StyleWorldTokens"/> 取，
/// 这里不新增任何颜色令牌，也没有裸十六进制。
/// </summary>
public sealed class MonitorFlowDrawable : IDrawable
{
    private const float NodeW = 146f;
    private const float NodeH = 44f;
    private const float GapY = 10f;
    private const float GroupGap = 18f;
    private const float HeaderH = 24f;
    private const float TopPad = 6f;
    private const float LeftPad = 10f;

    private static readonly string[] Groups = { "日程", "器官", "记忆", "数值" };

    private IReadOnlyList<MonitorFlowNode>? _nodes;
    public IReadOnlyList<MonitorFlowNode>? Nodes
    {
        get => _nodes;
        set { _nodes = value; Layout(); }
    }

    private IReadOnlyList<MonitorFlowLink>? _links;
    public IReadOnlyList<MonitorFlowLink>? Links
    {
        get => _links;
        set { _links = value; }
    }

    public string? SelectedId { get; set; }

    // 排好的矩形：绘制和命中共用同一份，不会「看到的是那儿、点中的是这儿」。
    private readonly Dictionary<string, RectF> _rects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MonitorFlowNode> _byId = new(StringComparer.Ordinal);
    private float _viewW;
    private float _viewH;

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
        var nodes = _nodes;
        if (nodes is null || nodes.Count == 0) return;

        foreach (var n in nodes) _byId[n.Id] = n;

        var width = Math.Max(1f, _viewW);
        var totalW = Groups.Length * NodeW + (Groups.Length - 1) * GroupGap;
        // 窄屏整体等比收；再窄就不缩了，宁可让画布自己滚动，也不把字压成噪点。
        var scale = Math.Clamp((width - LeftPad * 2) / totalW, 0.62f, 1f);
        var nw = NodeW * scale;
        var nh = NodeH * Math.Max(0.86f, scale);
        var gap = GroupGap * scale;
        var stepY = nh + GapY;

        for (var g = 0; g < Groups.Length; g++)
        {
            var x = LeftPad + g * (nw + gap);
            var y = TopPad + HeaderH;
            foreach (var n in nodes)
            {
                if (!string.Equals(n.Group, Groups[g], StringComparison.Ordinal)) continue;
                if (y + nh > Math.Max(_viewH, nh + TopPad + HeaderH) && _viewH > 0) continue;
                _rects[n.Id] = new RectF(x, y, nw, nh);
                y += stepY;
            }
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

        // 底不另铺色：GraphicsView 自己透明，卡面由 XAML 的 CardSolid 负责，零新增合成层。
        var ink = StyleWorldTokens.Pick("WarmDark", GraphPalette.Unknown);
        var body = StyleWorldTokens.Pick("WarmBody", GraphPalette.Unknown);

        DrawLinks(canvas);
        DrawHeaders(canvas, ink);

        var nodes = _nodes;
        if (nodes is null || nodes.Count == 0)
        {
            canvas.FontSize = 12;
            canvas.FontColor = body.WithAlpha(0.7f);
            canvas.DrawString("监测数据还没到位。", 12, TopPad + HeaderH + 8,
                Math.Max(80f, dirtyRect.Width - 24), 28, HorizontalAlignment.Left, VerticalAlignment.Top);
            return;
        }

        foreach (var n in nodes)
        {
            if (_rects.TryGetValue(n.Id, out var r)) DrawNode(canvas, n, r, ink, body);
        }
    }

    private void DrawHeaders(ICanvas canvas, Color ink)
    {
        var width = _rects.Count == 0 ? 0f : _rects.Values.Max(r => r.Right);
        for (var g = 0; g < Groups.Length; g++)
        {
            var x = LeftPad + g * (NodeW * ScaleOf(width) + GroupGap * ScaleOf(width));
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.FontSize = 11;
            canvas.FontColor = ink.WithAlpha(0.78f);
            canvas.DrawString(Groups[g], x, TopPad, NodeW * ScaleOf(width), HeaderH,
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }
    }

    // 组距与节点宽用同一比例，表头才对得齐节点列。
    private static float ScaleOf(float drawnRight)
    {
        var total = Groups.Length * NodeW + (Groups.Length - 1) * GroupGap;
        if (drawnRight <= 0) return 1f;
        return Math.Clamp((drawnRight - LeftPad) / total, 0.62f, 1f);
    }

    private void DrawLinks(ICanvas canvas)
    {
        var links = _links;
        if (links is null || links.Count == 0) return;
        var tint = GraphPalette.Associated;
        foreach (var e in links)
        {
            if (!_rects.TryGetValue(e.From, out var a) || !_rects.TryGetValue(e.To, out var b)) continue;
            canvas.StrokeColor = tint.WithAlpha(0.4f);
            canvas.StrokeSize = 1.2f;
            canvas.DrawLine(a.Right, a.Center.Y, b.Left, b.Center.Y);
        }
    }

    private void DrawNode(ICanvas canvas, MonitorFlowNode n, RectF r, Color ink, Color body)
    {
        var fill = GroupColor(n.Group);
        var selected = n.Id == SelectedId;
        canvas.FillColor = fill.WithAlpha(selected ? 0.42f : 0.22f);
        canvas.FillRoundedRectangle(r.X, r.Y, r.Width, r.Height, 7);
        canvas.StrokeColor = selected ? ink.WithAlpha(0.92f) : ink.WithAlpha(0.35f);
        canvas.StrokeSize = selected ? 2f : 1f;
        canvas.DrawRoundedRectangle(r.X, r.Y, r.Width, r.Height, 7);

        // 状态灯：亮 = Active / Warn / Alert，暗 = 其余。只用绿/红/中性三档，不新增色。
        var lamp = LampColor(n.Status);
        var lampR = Math.Max(3.2f, r.Height * 0.11f);
        canvas.FillColor = lamp;
        canvas.FillCircle(r.X + 11, r.Center.Y, lampR);

        var textX = r.X + 11 + lampR + 6;
        canvas.Font = selected ? Microsoft.Maui.Graphics.Font.DefaultBold : Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = Math.Max(9.5f, r.Height * 0.31f);
        canvas.FontColor = ink;
        canvas.DrawString(Fit(n.Label, 11), textX, r.Y + 2, r.Width - (textX - r.X) - 6, r.Height - 4,
            HorizontalAlignment.Left, VerticalAlignment.Center);
    }

    private static Color GroupColor(string group) => group switch
    {
        "日程" => GraphPalette.Person,
        "器官" => GraphPalette.Thing,
        "记忆" => GraphPalette.MemoryCard,
        _ => GraphPalette.Tag
    };

    private static Color LampColor(string status)
    {
        if (string.IsNullOrWhiteSpace(status)) return GraphPalette.Unknown.WithAlpha(0.35f);
        return status switch
        {
            "Active" or "Normal" => GraphPalette.Tag,
            "Warn" => GraphPalette.Person,
            "Alert" => GraphPalette.Thing,
            _ => GraphPalette.Unknown.WithAlpha(0.35f)
        };
    }

    /// <summary>画布坐标（相对 GraphicsView 左上角）点到哪枚节点；点不中返回 null。</summary>
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
    public MonitorFlowNode? Find(string id) => _byId.TryGetValue(id, out var n) ? n : null;

    private static string Fit(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..(max - 1)] + "…");
}
