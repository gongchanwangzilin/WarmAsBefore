using Microsoft.Maui.Graphics;
using WarmAsBefore.Models;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Drawables;

/// <summary>
/// 地图编辑器画布 v2（工作流式）：
/// - 无限画布：Offset 平移 + Scale 缩放（世界坐标 = 存储坐标，屏幕 = 世界*Scale + Offset）
/// - 场景卡片：地点色条 + 背景图缩略（无图用底色）+ 名称条；当前场景金色描边
/// - 临时节点：圆形端点，可引出/接收连线
/// - 连线：直线/曲线（waypoint 折点），长度=0 画虚线，长度文案黑色绘制在最上层
/// - 备注卡：地点/场景/临时节点/连线均可挂备注小卡
/// - 右键菜单浮层
/// </summary>
public sealed class MapCanvasDrawable : IDrawable
{
    public const double CardW = 170;
    public const double CardH = 64;
    public const double TransitSize = 26;
    public const double NoteW = 150;

    // ---- 数据（VM 填充）----
    public IReadOnlyList<MapCanvasNode>? Nodes { get; set; }
    public IReadOnlyList<MapTransitNode>? Transits { get; set; }
    public IReadOnlyList<MapEdge>? Edges { get; set; }
    public IReadOnlyList<MapLocation>? Locations { get; set; }

    // ---- 视口 ----
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double Scale { get; set; } = 1.0;

    public Point WorldToScreen(Point w) => new(w.X * Scale + OffsetX, w.Y * Scale + OffsetY);
    public Point ScreenToWorld(Point s) => new((s.X - OffsetX) / Scale, (s.Y - OffsetY) / Scale);

    // ---- 交互状态 ----
    public MapCanvasNode? SelectedNode { get; set; }
    public string? SelectedEdgeId { get; set; }
    /// <summary>长按左键框选矩形（世界坐标；null=未框选）。</summary>
    public RectF? MarqueeRect { get; set; }
    /// <summary>框选命中的地点 Id 集合（右键新建场景时优先归属这里的唯一地点）。</summary>
    public HashSet<string> MarqueeSelected { get; } = new();
    public MapCanvasNode? LinkStartNode { get; set; }     // 连线模式起点（场景）
    public MapTransitNode? LinkStartTransit { get; set; } // 连线模式起点（临时节点）
    public bool LinkCurve { get; set; }
    public List<Point> LinkPreview { get; } = new();      // 连线预览折点
    public Point? Mouse { get; set; }

    // ---- 右键菜单 ----
    public ContextMenuState? Menu { get; set; }

    // ---- 图片缓存 ----
    private readonly Dictionary<string, Microsoft.Maui.Graphics.IImage?> _imgCache = new();
    private Action? _onInvalidate;
    public void AttachInvalidate(Action a) => _onInvalidate = a;
    private void NeedRedraw() => _onInvalidate?.Invoke();

    private static readonly Color[] Palette =
    {
        Color.FromArgb("#E8B4B8"), Color.FromArgb("#A8C3E0"), Color.FromArgb("#C3B1E1"),
        Color.FromArgb("#A9CBB7"), Color.FromArgb("#F0D9A8"), Color.FromArgb("#F2B8C6"),
        Color.FromArgb("#B5D8C7"), Color.FromArgb("#D9C8A9")
    };

    public Color GroupColor(string groupKey)
    {
        var h = 0;
        foreach (var ch in groupKey) h = (h * 31 + ch) & 0x7fffffff;
        return Palette[h % Palette.Length];
    }

    // ==================== Draw ====================

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        DrawGrid(canvas, dirtyRect);

        DrawLocationFrames(canvas);

        if (Edges is not null) foreach (var e in Edges) DrawEdge(canvas, e);
        if (LinkStartNode is not null || LinkStartTransit is not null) DrawLinkPreview(canvas);

        if (Transits is not null) foreach (var t in Transits) DrawTransit(canvas, t);
        if (Nodes is not null) foreach (var n in Nodes) DrawCard(canvas, n);

        DrawLocationNotes(canvas);
        if (Nodes is not null) foreach (var n in Nodes) DrawNodeNote(canvas, n);
        if (Transits is not null) foreach (var t in Transits) DrawTransitNote(canvas, t);
        DrawEdgeNotes(canvas);
        DrawEdgeLengthLabels(canvas);

        DrawMarquee(canvas);
        if (Menu is not null) DrawMenu(canvas);
    }

    private void DrawGrid(ICanvas canvas, RectF dirty)
    {
        canvas.FillColor = Color.FromArgb("#FDF6EE");
        canvas.FillRectangle(dirty);
        var step = 24 * Scale;
        if (step < 10) step = 10;
        if (Scale < 0.05) return;
        canvas.StrokeSize = 1;
        canvas.StrokeColor = Color.FromArgb("#F0E4D6");
        var x0 = Math.Floor((dirty.Left - OffsetX) / step) * step + OffsetX;
        for (var x = x0; x <= dirty.Right; x += step)
            canvas.DrawLine((float)x, dirty.Top, (float)x, dirty.Bottom);
        var y0 = Math.Floor((dirty.Top - OffsetY) / step) * step + OffsetY;
        for (var y = y0; y <= dirty.Bottom; y += step)
            canvas.DrawLine(dirty.Left, (float)y, dirty.Right, (float)y);
    }

    // ==================== 地点大框（标记容器，不参与连线） ====================

    /// <summary>地点框的世界坐标矩形（含留白）。父框 = 自身场景 + 所有后代地点（含空子框锚点框）的并集包围盒；
    /// 无任何内容的地点退化为以锚点(X,Y)为中心的最小空框（新建地点立刻可见）。</summary>
    public RectF? LocationFrame(MapLocation loc)
    {
        if (Locations is null) return null;
        // 收集该地点及其所有后代地点（递归）
        var ids = new List<string> { loc.Id };
        bool added;
        do
        {
            added = false;
            foreach (var l in Locations)
                if (!string.IsNullOrEmpty(l.ParentId) && !ids.Contains(l.Id) && ids.Contains(l.ParentId))
                {
                    ids.Add(l.Id);
                    added = true;
                }
        } while (added);

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var found = false;
        if (Nodes is not null)
            foreach (var n in Nodes)
            {
                if (!ids.Contains(n.Location.Id)) continue;
                minX = Math.Min(minX, n.X);
                minY = Math.Min(minY, n.Y);
                maxX = Math.Max(maxX, n.X + CardW);
                maxY = Math.Max(maxY, n.Y + CardH);
                found = true;
            }
        // 关键：空后代（无卡片的子地点）也要计入父框包围盒，父框才能真正包住子框（一环套一环）
        const double emptyW = 230, emptyH = 100;
        foreach (var l in Locations)
        {
            if (!ids.Contains(l.Id)) continue;
            var hasCard = Nodes is not null && Nodes.Any(n => n.Location.Id == l.Id);
            if (hasCard) continue; // 有卡片的已由上面卡片包围盒覆盖
            var a = new Point(l.X < 0 ? 0 : l.X, l.Y < 0 ? 0 : l.Y);
            minX = Math.Min(minX, a.X - emptyW / 2);
            minY = Math.Min(minY, a.Y - emptyH / 2);
            maxX = Math.Max(maxX, a.X + emptyW / 2);
            maxY = Math.Max(maxY, a.Y + emptyH / 2);
            found = true;
        }
        if (!found)
        {
            return new RectF((float)((loc.X < 0 ? 0 : loc.X) - emptyW / 2), (float)((loc.Y < 0 ? 0 : loc.Y) - emptyH / 2),
                (float)emptyW, (float)emptyH);
        }
        // 包裹框留白：为标题与卡片边缘留出空隙，嵌套后字不至于贴框/看不清
        const double pad = 56, title = 44;
        // 父框（有后代）顶部再多留一段标题带：防止“最高的子框顶边”与“父框顶边/父标题”重合
        var extraHead = ids.Count > 1 ? title + pad : 0;
        return new RectF((float)(minX - pad), (float)(minY - pad - title - extraHead),
            (float)(maxX - minX + pad * 2), (float)(maxY - minY + pad * 2 + title + extraHead));
    }

    /// <summary>地点标记框：淡色底 + 色描边 + 地点名标题。框只做分组标记，不参与连线。</summary>
    private void DrawLocationFrames(ICanvas canvas)
    {
        if (Nodes is null || Locations is null) return;
        // 父框先画（在底层），子框后画（压在父框内层上），逐层嵌套
        var depth = new Dictionary<string, int>();
        foreach (var l in Locations)
        {
            var d = string.IsNullOrEmpty(l.ParentId) ? 0 : (depth.TryGetValue(l.ParentId, out var p) ? p + 1 : 999);
            depth[l.Id] = d;
        }
        var ordered = Locations.OrderBy(l => depth.GetValueOrDefault(l.Id, 999)).ToList();
        foreach (var loc in ordered)
        {
            var frame = LocationFrame(loc);
            if (frame is null) continue;
            var f = frame.Value;
            var sp = new RectF((float)(f.X * Scale + OffsetX), (float)(f.Y * Scale + OffsetY),
                (float)(f.Width * Scale), (float)(f.Height * Scale));

            var isChild = !string.IsNullOrEmpty(loc.ParentId);
            var isMarquee = MarqueeSelected.Contains(loc.Id);
            // 父框底色更淡一层（嵌套视觉层次）；被框选的地点高亮
            canvas.FillColor = isMarquee ? Color.FromArgb("#30FFFFFF")
                : isChild ? Color.FromArgb("#22FFFFFF") : Color.FromArgb("#14FFFFFF");
            canvas.FillRoundedRectangle(sp, (float)(10 * Scale));

            // 描边（虚线柔和）；框选命中 = 实线加粗
            canvas.StrokeDashPattern = isMarquee ? null : new[] { 8f, 6f };
            canvas.StrokeSize = (float)((isMarquee ? 3.0 : 1.6) * Scale);
            canvas.StrokeColor = GroupColor(loc.Id).WithAlpha(isMarquee ? 1f : 0.75f);
            canvas.DrawRoundedRectangle(sp, (float)(10 * Scale));
            canvas.StrokeDashPattern = null;

            // 标题（左上角）
            var titleH = (float)(24 * Scale);
            canvas.FontColor = GroupColor(loc.Id);
            canvas.FontSize = (float)Math.Max(10, 14 * Scale);
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.DrawString(loc.Name + (isChild ? " ▸" : ""), (float)(sp.X + 8 * Scale), (float)(sp.Y + 2 * Scale),
                (float)(sp.Width - 16 * Scale), titleH,
                HorizontalAlignment.Left, VerticalAlignment.Center);

            // 空地点提示
            var hasCard = Nodes.Any(n => n.Location.Id == loc.Id);
            if (!hasCard)
            {
                canvas.FontColor = Color.FromArgb("#80FFFFFF");
                canvas.FontSize = (float)Math.Max(9, 11 * Scale);
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
                canvas.DrawString("空地点：右键→新建场景/子地点", (float)(sp.X + 8 * Scale),
                    (float)(sp.Y + sp.Height - 20 * Scale), (float)(sp.Width - 16 * Scale), titleH,
                    HorizontalAlignment.Left, VerticalAlignment.Center);
            }
        }
    }

    /// <summary>框选矩形（半透明蓝）——长按左键拖动时显示。</summary>
    private void DrawMarquee(ICanvas canvas)
    {
        if (MarqueeRect is not { } r) return;
        var sp = new RectF((float)(r.X * Scale + OffsetX), (float)(r.Y * Scale + OffsetY),
            (float)(r.Width * Scale), (float)(r.Height * Scale));
        canvas.FillColor = Color.FromArgb("#203A8CFF");
        canvas.FillRectangle(sp);
        canvas.StrokeSize = (float)(1.5 * Scale);
        canvas.StrokeColor = Color.FromArgb("#7FA8FF");
        canvas.DrawRectangle(sp);
    }

    // ==================== 连线 ====================

    /// <summary>把边展开成路径点序列（世界坐标，端点 + 折点）。</summary>
    public List<Point> EdgePoints(MapEdge e)
    {
        var pts = new List<Point> { PointOf(e.A) };
        foreach (var w in e.Waypoints) pts.Add(new Point(w.X, w.Y));
        pts.Add(PointOf(e.B));
        return pts;
    }

    public Point PointOf(string id)
    {
        if (Nodes is not null)
            foreach (var n in Nodes)
                if (n.Id == id) return new Point(n.X + CardW / 2, n.Y + CardH / 2);
        if (Transits is not null)
            foreach (var t in Transits)
                if (t.Id == id) return new Point(t.X, t.Y);
        return default;
    }

    /// <summary>采样路径点（曲线按 Catmull-Rom 平滑过所有点，细分 16 段/对）。</summary>
    public List<Point> SampleEdge(MapEdge e)
    {
        var pts = EdgePoints(e);
        if (e.Kind != "curve" || pts.Count < 3)
        {
            var flat = new List<Point>();
            for (var i = 0; i + 1 < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                const int n = 8;
                for (var k = 0; k < n; k++)
                    flat.Add(new Point(a.X + (b.X - a.X) * k / (double)n, a.Y + (b.Y - a.Y) * k / (double)n));
            }
            flat.Add(pts[^1]);
            return flat;
        }

        var out_ = new List<Point>();
        for (var i = 0; i < pts.Count - 1; i++)
        {
            var p0 = pts[Math.Max(0, i - 1)];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = pts[Math.Min(pts.Count - 1, i + 2)];
            const int n = 16;
            for (var k = 0; k < n; k++)
            {
                var t = k / (double)n;
                var t2 = t * t;
                var t3 = t2 * t;
                var x = 0.5 * (2 * p1.X + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);
                var y = 0.5 * (2 * p1.Y + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);
                out_.Add(new Point(x, y));
            }
        }
        out_.Add(pts[^1]);
        return out_;
    }

    private void DrawEdge(ICanvas canvas, MapEdge e)
    {
        var pts = SampleEdge(e);
        if (pts.Count < 2) return;

        var dashed = e.Length == 0;
        var selected = SelectedEdgeId == e.Id;
        canvas.StrokeSize = (float)(selected ? 3.2 : 2.2);
        canvas.StrokeColor = selected ? Color.FromArgb("#5D4037") : Color.FromArgb("#B08968");
        if (dashed) canvas.StrokeDashPattern = new[] { 7f, 5f };

        var path = new PathF();
        path.MoveTo(ScreenPt(pts[0]));
        for (var i = 1; i < pts.Count; i++) path.LineTo(ScreenPt(pts[i]));
        canvas.DrawPath(path);
        canvas.StrokeDashPattern = null;

        // 箭头（终点方向）
        if (pts.Count >= 2)
        {
            var tip = pts[^1];
            var prev = pts[^2];
            var d = new Point(tip.X - prev.X, tip.Y - prev.Y);
            var len = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            if (len > 0.001)
            {
                d = new Point(d.X / len, d.Y / len);
                DrawArrow(canvas, ScreenPt(tip), d, Color.FromArgb("#8A5A3B"));
            }
        }

        // 折点手柄（选中时显示）
        if (selected && e.Waypoints.Count > 0)
        {
            canvas.FillColor = Color.FromArgb("#8D6E63");
            foreach (var w in e.Waypoints)
            {
                var sp = ScreenPt(new Point(w.X, w.Y));
                canvas.FillEllipse((float)(sp.X - 5), (float)(sp.Y - 5), 10, 10);
            }
        }
    }

    private static void DrawArrow(ICanvas canvas, Point tip, Point dir, Color color)
    {
        const double size = 9;
        var baseP = new Point(tip.X - dir.X * size, tip.Y - dir.Y * size);
        var nx = -dir.Y;
        var ny = dir.X;
        var path = new PathF();
        path.MoveTo((float)tip.X, (float)tip.Y);
        path.LineTo((float)(baseP.X + nx * size * 0.5), (float)(baseP.Y + ny * size * 0.5));
        path.LineTo((float)(baseP.X - nx * size * 0.5), (float)(baseP.Y - ny * size * 0.5));
        path.Close();
        canvas.FillColor = color;
        canvas.FillPath(path);
    }

    private void DrawLinkPreview(ICanvas canvas)
    {
        var start = LinkStartNode is not null ? new Point(LinkStartNode.X, LinkStartNode.Y) : new Point(LinkStartTransit!.X, LinkStartTransit.Y);
        var pts = new List<Point> { start };
        pts.AddRange(LinkPreview);
        if (Mouse is { } m) pts.Add(ScreenToWorld(m));

        canvas.StrokeSize = 2;
        canvas.StrokeDashPattern = new[] { 6f, 4f };
        canvas.StrokeColor = Color.FromArgb("#C9A227");
        var path = new PathF();
        path.MoveTo(ScreenPt(pts[0]));
        foreach (var p in pts.Skip(1)) path.LineTo(ScreenPt(p));
        canvas.DrawPath(path);
        canvas.StrokeDashPattern = null;

        canvas.FillColor = Color.FromArgb("#C9A227");
        foreach (var p in LinkPreview)
        {
            var sp = ScreenPt(p);
            canvas.FillEllipse((float)(sp.X - 4), (float)(sp.Y - 4), 8, 8);
        }
    }

    // ==================== 临时节点 ====================

    private void DrawTransit(ICanvas canvas, MapTransitNode t)
    {
        var sp = ScreenPt(new Point(t.X, t.Y));
        var r = TransitSize / 2 * Scale;
        var selected = SelectedNode is not null && SelectedNode.Id == t.Id;

        canvas.FillColor = Color.FromArgb("#FFFDF9");
        canvas.StrokeSize = selected ? (float)(3 * Scale) : (float)(2 * Scale);
        canvas.StrokeColor = selected ? Color.FromArgb("#5D4037") : Color.FromArgb("#8D6E63");
        canvas.FillEllipse((float)(sp.X - r), (float)(sp.Y - r), (float)(r * 2), (float)(r * 2));
        canvas.DrawEllipse((float)(sp.X - r), (float)(sp.Y - r), (float)(r * 2), (float)(r * 2));

        canvas.FontColor = Color.FromArgb("#3E2A1C");
        canvas.FontSize = (float)Math.Max(8, 11 * Scale);
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        var name = t.Name;
        if (name.Length > 10) name = name[..10];
        canvas.DrawString(name, (float)(sp.X - r), (float)(sp.Y + r + 2), (float)(r * 2), 16,
            HorizontalAlignment.Center, VerticalAlignment.Top);
    }

    // ==================== 场景卡片 ====================

    private void DrawCard(ICanvas canvas, MapCanvasNode n)
    {
        var rect = new RectF((float)(n.X * Scale + OffsetX), (float)(n.Y * Scale + OffsetY),
            (float)(CardW * Scale), (float)(CardH * Scale));
        var group = GroupColor(n.GroupKey);

        // 主体：背景图或底色
        var img = !string.IsNullOrWhiteSpace(n.Scene.Background) ? GetImage(n) : null;
        if (img is not null)
        {
            canvas.DrawImage(img, rect.X, rect.Y, rect.Width, rect.Height);
        }
        else
        {
            canvas.FillColor = ParseHex(n.Scene.BackgroundColor, "#2C1810");
            canvas.FillRoundedRectangle(rect, 10);
        }

        // 顶部地点色条
        canvas.FillColor = group;
        var barH = (float)(18 * Scale);
        canvas.FillRoundedRectangle(new RectF(rect.X, rect.Y, rect.Width, barH), 4);
        canvas.FillRectangle(new RectF(rect.X, rect.Y + barH / 2, rect.Width, barH / 2));

        // 底部名称条
        var nameH = (float)(22 * Scale);
        canvas.FillColor = Color.FromArgb("#AA000000");
        canvas.FillRoundedRectangle(new RectF(rect.X, rect.Bottom - nameH, rect.Width, nameH), 4);
        canvas.FillRectangle(new RectF(rect.X, rect.Bottom - nameH * 2, rect.Width, nameH));

        canvas.FontColor = Colors.White;
        canvas.FontSize = (float)Math.Max(9, 13 * Scale);
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        var name = n.SceneName;
        if (name.Length > 12) name = name[..12];
        canvas.DrawString(name, rect.X + 4, (float)(rect.Bottom - nameH), rect.Width - 8, nameH,
            HorizontalAlignment.Center, VerticalAlignment.Center);

        // 顶部色条：地点名 + 当前标记
        canvas.FontColor = Color.FromArgb("#4A3226");
        canvas.FontSize = (float)Math.Max(7, 10 * Scale);
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        var locName = n.LocationName;
        if (locName.Length > 8) locName = locName[..8];
        canvas.DrawString(locName, rect.X + 5, rect.Y + 2, rect.Width - 12, barH - 3,
            HorizontalAlignment.Left, VerticalAlignment.Center);
        if (n.IsCurrent)
        {
            canvas.FontColor = Color.FromArgb("#8A6D1A");
            canvas.DrawString("📍", rect.X + 5, rect.Y + 2, rect.Width - 10, barH - 3,
                HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        // 边框
        canvas.StrokeSize = (float)(n.IsCurrent ? 3.5 * Scale : n.IsSelected || n == SelectedNode ? 2.8 * Scale : 1.2 * Scale);
        canvas.StrokeColor = n.IsCurrent ? Color.FromArgb("#C9A227")
            : n.IsSelected || n == SelectedNode ? Color.FromArgb("#5D4037")
            : Color.FromArgb("#E3D5C4");
        canvas.DrawRoundedRectangle(rect, 10);
    }

    private Point ScreenPt(Point w) => WorldToScreen(w);

    private static Color ParseHex(string hex, string fallback)
    {
        try { return Color.FromArgb(hex); }
        catch { return Color.FromArgb(fallback); }
    }

    // ==================== 备注卡 ====================

    private void DrawLocationNotes(ICanvas canvas)
    {
        if (Nodes is null || Locations is null) return;
        foreach (var loc in Locations)
        {
            if (string.IsNullOrWhiteSpace(loc.Note)) continue;
            var group = Nodes.Where(n => n.GroupKey == loc.Id).ToList();
            if (group.Count == 0) continue;
            var minX = group.Min(n => n.X);
            var minY = group.Min(n => n.Y);
            DrawNoteCard(canvas, new Point(minX, minY - 48), loc.Note, Color.FromArgb("#EFE3D3"), Color.FromArgb("#6B5340"));
        }
    }

    private void DrawNodeNote(ICanvas canvas, MapCanvasNode n)
    {
        if (string.IsNullOrWhiteSpace(n.Scene.Note)) return;
        DrawNoteCard(canvas, new Point(n.X + CardW, n.Y + CardH / 2), n.Scene.Note, Color.FromArgb("#EFE9DE"), Color.FromArgb("#5B4A3A"));
    }

    private void DrawTransitNote(ICanvas canvas, MapTransitNode t)
    {
        if (string.IsNullOrWhiteSpace(t.Transit.Note)) return;
        DrawNoteCard(canvas, new Point(t.X + 16, t.Y + 16), t.Transit.Note, Color.FromArgb("#EFE9DE"), Color.FromArgb("#5B4A3A"));
    }

    private void DrawEdgeNotes(ICanvas canvas)
    {
        if (Edges is null) return;
        foreach (var e in Edges)
        {
            if (string.IsNullOrWhiteSpace(e.Note)) continue;
            var pts = SampleEdge(e);
            var mid = pts[pts.Count / 2];
            DrawNoteCard(canvas, mid, e.Note, Color.FromArgb("#F3E5CF"), Color.FromArgb("#7A5C3E"));
        }
    }

    private void DrawNoteCard(ICanvas canvas, Point worldPos, string text, Color bg, Color fg)
    {
        var lines = Wrap(text, 16);
        if (lines.Count > 4) lines = lines.Take(4).ToList();
        var w = NoteW * Scale;
        var h = (lines.Count * 13 + 10) * Scale;
        var pos = ScreenPt(worldPos);
        var rect = new RectF((float)(pos.X + 8), (float)(pos.Y + 8), (float)w, (float)h);
        canvas.FillColor = bg;
        canvas.FillRoundedRectangle(rect, 6);
        canvas.StrokeSize = 1;
        canvas.StrokeColor = Color.FromArgb("#D8C4A8");
        canvas.DrawRoundedRectangle(rect, 6);
        canvas.FontColor = fg;
        canvas.FontSize = (float)Math.Max(8, 11 * Scale);
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.DrawString(string.Join("\n", lines), rect.X + 6, rect.Y + 4, (float)(w - 12), (float)(h - 8),
            HorizontalAlignment.Left, VerticalAlignment.Top);
    }

    /// <summary>长度文案：黑色绘制在最上层。直线多折点：每两个相邻端点各显示一段；曲线/单段：整条中点显示总长。</summary>
    private void DrawEdgeLengthLabels(ICanvas canvas)
    {
        if (Edges is null) return;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontSize = (float)Math.Max(9, 12 * Scale);
        foreach (var e in Edges)
        {
            if (e.Length == 0) continue;   // 未丈量：虚线，无长度
            var vertexPts = EdgePoints(e); // 端点 + 折点（逐端点分段测量）
            if (e.Kind == "line" && !e.TotalLabel && vertexPts.Count >= 2)
            {
                // 直线/折线：每一对相邻端点测一次长度
                var segPxArr = new double[vertexPts.Count - 1];
                double totalPx = 0;
                for (var i = 0; i + 1 < vertexPts.Count; i++)
                {
                    segPxArr[i] = Math.Sqrt(Math.Pow(vertexPts[i + 1].X - vertexPts[i].X, 2)
                                          + Math.Pow(vertexPts[i + 1].Y - vertexPts[i].Y, 2));
                    totalPx += segPxArr[i];
                }
                for (var i = 0; i + 1 < vertexPts.Count; i++)
                {
                    var a = vertexPts[i];
                    var b = vertexPts[i + 1];
                    string text;
                    if (e.Length > 0)
                    {
                        // 手动总长：按像素比例分摊到每一段
                        var share = totalPx > 1e-9
                            ? e.Length * segPxArr[i] / totalPx
                            : e.Length / segPxArr.Length;
                        text = $"{(int)Math.Round(share)}m";
                    }
                    else
                    {
                        // 自动：按几何像素 ×0.5 估算每段
                        text = $"~{(int)Math.Round(segPxArr[i] * 0.5)}m";
                    }
                    DrawLengthChip(canvas, new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2), text);
                }
                continue;
            }

            // 曲线/单段直线：总长显示在中点
            var pts = SampleEdge(e);
            if (pts.Count == 0) continue;
            var mid = pts[pts.Count / 2];
            var auto = e.Length < 0;
            var text2 = auto
                ? $"~{(int)Math.Round(EdgeLengthPixelsLocal(e) * 0.5)}m"
                : $"{e.Length:0}m";
            DrawLengthChip(canvas, mid, text2);
        }
    }

    private void DrawLengthChip(ICanvas canvas, Point worldMid, string text)
    {
        var sp = ScreenPt(worldMid);
        var tw = (float)(text.Length * 9 * Scale + 10);
        var th = (float)(16 * Scale);
        var rect = new RectF((float)(sp.X - tw / 2), (float)(sp.Y - th / 2), tw, th);
        canvas.FillColor = Color.FromArgb("#FFFFFF");
        canvas.FillRoundedRectangle(rect, 5);
        canvas.StrokeSize = 1;
        canvas.StrokeColor = Color.FromArgb("#C9A227");
        canvas.DrawRoundedRectangle(rect, 5);
        canvas.FontColor = Colors.Black;
        canvas.DrawString(text, rect.X, rect.Y, tw, th, HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    private double EdgeLengthPixelsLocal(MapEdge e)
    {
        var pts = SampleEdge(e);
        double px = 0;
        for (var i = 0; i + 1 < pts.Count; i++)
            px += Math.Sqrt(Math.Pow(pts[i + 1].X - pts[i].X, 2) + Math.Pow(pts[i + 1].Y - pts[i].Y, 2));
        return px;
    }

    // ==================== 右键菜单 ====================

    private void DrawMenu(ICanvas canvas)
    {
        if (Menu is null) return;
        var itemH = (float)(30 * Scale);
        var w = (float)(150 * Scale);
        var pos = ScreenPt(Menu.Pos);
        var rect = new RectF((float)pos.X, (float)pos.Y, w, (float)(Menu.Items.Count * itemH + 8 * Scale));

        // 标题条
        var th = (float)(24 * Scale);
        if (!string.IsNullOrEmpty(Menu.Title))
        {
            rect.Height += th;
            canvas.FillColor = Color.FromArgb("#8D6E63");
            canvas.FillRoundedRectangle(new RectF(rect.X, rect.Y, rect.Width, th), 6);
            canvas.FillRectangle(new RectF(rect.X, rect.Y + th / 2, rect.Width, th / 2));
            canvas.FontColor = Colors.White;
            canvas.FontSize = (float)(11 * Scale);
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.DrawString(Menu.Title, rect.X + 8, rect.Y, w - 16, th,
                HorizontalAlignment.Left, VerticalAlignment.Center);
            rect.Y += th;
            rect.Height -= th; // 面板本身高度（标题除外）
        }

        canvas.FillColor = Color.FromArgb("#FFFDF9");
        canvas.FillRoundedRectangle(rect, 8);
        canvas.StrokeSize = 1;
        canvas.StrokeColor = Color.FromArgb("#C8B4A0");
        canvas.DrawRoundedRectangle(rect, 8);

        canvas.FontColor = Color.FromArgb("#3E2A1C");
        canvas.FontSize = (float)(13 * Scale);
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        var y = rect.Y + 4 * Scale;
        for (var i = 0; i < Menu.Items.Count; i++)
        {
            var itemRect = new RectF(rect.X, (float)y, rect.Width, itemH);
            canvas.FillColor = Color.FromArgb("#F5E9DA");
            canvas.FillRoundedRectangle(itemRect, 4);
            canvas.DrawString(Menu.Items[i].Label, itemRect.X + 12, itemRect.Y, itemRect.Width - 24, itemRect.Height,
                HorizontalAlignment.Left, VerticalAlignment.Center);
            y += itemH;
        }
    }

    // ==================== 命中测试 ====================

    public MapCanvasNode? HitCard(Point world)
    {
        if (Nodes is null) return null;
        foreach (var n in Nodes)
        {
            var rect = new RectF((float)n.X, (float)n.Y, (float)CardW, (float)CardH);
            if (rect.Contains((float)world.X, (float)world.Y)) return n;
        }
        return null;
    }

    public MapTransitNode? HitTransit(Point world)
    {
        if (Transits is null) return null;
        foreach (var t in Transits)
        {
            var dx = world.X - t.X;
            var dy = world.Y - t.Y;
            if (dx * dx + dy * dy <= TransitSize * TransitSize) return t;
        }
        return null;
    }

    public MapEdge? HitEdge(Point world, double tol = 8)
    {
        if (Edges is null) return null;
        var tolSq = tol * tol;
        foreach (var e in Edges)
        {
            var pts = SampleEdge(e);
            for (var i = 0; i + 1 < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                var abx = b.X - a.X;
                var aby = b.Y - a.Y;
                var l2 = abx * abx + aby * aby;
                double t = l2 < 1e-9 ? 0 : ((world.X - a.X) * abx + (world.Y - a.Y) * aby) / l2;
                t = Math.Clamp(t, 0, 1);
                var px = a.X + abx * t - world.X;
                var py = a.Y + aby * t - world.Y;
                if (px * px + py * py <= tolSq) return e;
            }
        }
        return null;
    }

    /// <summary>命中边上折点索引（世界坐标）。</summary>
    public int HitWaypoint(MapEdge e, Point world, double tol = 8)
    {
        for (var i = 0; i < e.Waypoints.Count; i++)
        {
            var dx = world.X - e.Waypoints[i].X;
            var dy = world.Y - e.Waypoints[i].Y;
            if (dx * dx + dy * dy <= tol * tol) return i;
        }
        return -1;
    }

    /// <summary>命中菜单项索引（屏幕坐标；-1=未命中面板，-2=标题区）。与 DrawMenu 同参照系（屏幕）。</summary>
    public int HitMenu(Point screen)
    {
        if (Menu is null) return -1;
        var itemH = 30 * Scale;
        var w = 150 * Scale;
        var pos = ScreenPt(Menu.Pos);   // 世界→屏幕，与绘制一致
        var y = pos.Y;
        if (!string.IsNullOrEmpty(Menu.Title)) y += 24 * Scale;
        var h = Menu.Items.Count * itemH + 8 * Scale;
        // 面板含 4*Scale 上内边距，items 从 y+4*Scale 开始
        var panel = new RectF((float)pos.X, (float)y, (float)w, (float)h);
        if (!panel.Contains((float)screen.X, (float)screen.Y)) return -1;
        var itemStart = y + 4 * Scale;
        return (int)((screen.Y - itemStart) / itemH);
    }

    /// <summary>命中地点框：返回包含该点的最深（最内层）地点，供右键时"新建场景/子地点归属此地点"。</summary>
    public MapLocation? HitLocationFrame(Point world)
    {
        if (Locations is null) return null;
        MapLocation? best = null;
        var bestArea = double.MaxValue;
        foreach (var loc in Locations)
        {
            var f = LocationFrame(loc);
            if (f is null) continue;
            var fv = f.Value;
            if (world.X >= fv.X && world.X <= fv.X + fv.Width
                && world.Y >= fv.Y && world.Y <= fv.Y + fv.Height)
            {
                var area = fv.Width * fv.Height;
                if (area < bestArea) { bestArea = area; best = loc; }
            }
        }
        return best;
    }

    // ==================== 背景图加载 ====================

    public Microsoft.Maui.Graphics.IImage? GetImage(MapCanvasNode n)
    {
        if (_imgCache.TryGetValue(n.Id, out var img)) return img;
        _imgCache[n.Id] = null; // 防重复加载
        LoadImageAsync(n);
        return null;
    }
private Func<MapCanvasNode, string?>? _bgResolver;

    /// <summary>背景图相对路径 → 绝对路径的解析器（由 MapViewModel 注入，内部走 MapService.ResolveBackground）。</summary>
    public void AttachBgResolver(Func<MapCanvasNode, string?> resolver) => _bgResolver = resolver;

    private string? _resolveBg(MapCanvasNode n) => _bgResolver?.Invoke(n);

    private void LoadImageAsync(MapCanvasNode n)
    {
        var path = n.Scene.Background;   // 相对路径，由 ResolveBackground 解析
        if (string.IsNullOrEmpty(path)) return;
        _ = Task.Run(() =>
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(_resolveBg(n)); }
            catch { return; }
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    using var ms = new MemoryStream(bytes);
                    _imgCache[n.Id] = Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(ms);
                }
                catch { _imgCache[n.Id] = null; }
                NeedRedraw();
            });
        });
    }

    // ==================== 文本换行 ====================

    public static List<string> Wrap(string text, int maxChars)
    {
        var lines = new List<string>();
        var cur = "";
        foreach (var ch in text)
        {
            cur += ch;
            if (cur.Length >= maxChars)
            {
                lines.Add(cur);
                cur = "";
            }
        }
        if (cur.Length > 0) lines.Add(cur);
        if (lines.Count == 0) lines.Add("");
        return lines;
    }
}

/// <summary>右键菜单状态（世界坐标定位）。</summary>
public sealed class ContextMenuState
{
    public required Point Pos { get; init; }
    public string Title { get; init; } = "";
    public string TargetKind { get; init; } = "";   // scene/transit/edge/blank
    public string TargetId { get; init; } = "";
    public List<(string Label, string Cmd)> Items { get; init; } = new();
}