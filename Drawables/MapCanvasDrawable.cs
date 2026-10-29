using Microsoft.Maui.Graphics;
using WarmAsBefore.DesignSystem.Worlds;
using WarmAsBefore.Helpers;
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

    /// <summary>画布 1 单位 = 多少米（与 MapDefinition.MetersPerUnit 同步，标长度用）。</summary>
    public double MetersPerUnit { get; set; } = 0.5;

    /// <summary>只看某地点的某一层（楼层切换）；null = 全都看。其余房间会淡成影子，不挡路。</summary>
    public string? FloorLocationId { get; set; }
    public int? FloorFilter { get; set; }

    /// <summary>
    /// 细节档位：拉远了就只留主干道与大框，拉近了才长小巷、户型、备注卡。
    /// 一是像真地图（从大地看到小地），二是城市级地图（几千条路）在远景不必逐条画字。
    /// </summary>
    public enum Lod { Far, Mid, Near }

    public Lod Detail => Scale < 0.5 ? Lod.Far : Scale < 0.95 ? Lod.Mid : Lod.Near;

    public Point WorldToScreen(Point w) => new(w.X * Scale + OffsetX, w.Y * Scale + OffsetY);
    public Point ScreenToWorld(Point s) => new((s.X - OffsetX) / Scale, (s.Y - OffsetY) / Scale);

    /// <summary>这个包围盒（世界坐标）在当前视口里看得见吗——看不见的直接不画、不算。</summary>
    private bool InView(double x, double y, double w, double h, RectF dirty, double margin = 240)
    {
        var sx = x * Scale + OffsetX;
        var sy = y * Scale + OffsetY;
        var sw = w * Scale;
        var sh = h * Scale;
        return sx + sw >= dirty.Left - margin && sx <= dirty.Right + margin
            && sy + sh >= dirty.Top - margin && sy <= dirty.Bottom + margin;
    }

    /// <summary>这个点在当前视口里看得见吗。</summary>
    private bool InView(Point p, RectF dirty, double margin = 200)
    {
        var sx = p.X * Scale + OffsetX;
        var sy = p.Y * Scale + OffsetY;
        return sx >= dirty.Left - margin && sx <= dirty.Right + margin
            && sy >= dirty.Top - margin && sy <= dirty.Bottom + margin;
    }

    /// <summary>这张卡片是否归当前楼层视图（没开楼层切换时永远为真）。</summary>
    private bool OnFloor(MapCanvasNode n)
    {
        if (FloorLocationId is null || FloorFilter is null) return true;
        if (n.Location.Id != FloorLocationId) return true;   // 别的地点不管，只切这栋楼的层
        return n.Scene.Floor == FloorFilter;
    }

    /// <summary>这个场景是否被楼层视图淡出（还在画，只是成影子）。</summary>
    private bool Ghosted(MapCanvasNode n) => FloorLocationId is not null && FloorFilter is not null && !OnFloor(n);

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

    // ---- 背景图缓存 ----
    private readonly Dictionary<string, Microsoft.Maui.Graphics.IImage?> _imgCache = new();
    // ---- 地点缩略图缓存 ----
    private readonly Dictionary<string, Microsoft.Maui.Graphics.IImage?> _locThumbCache = new();
    // 两张缓存各自的入队顺序，用来「最久没画到的先扔」
    private readonly List<string> _imgOrder = new();
    private readonly List<string> _thumbOrder = new();
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
        _drawTick++;
        _dirty = dirtyRect;
        DrawGrid(canvas, dirtyRect);

        // 远景只画大框：卡片与字在 0.3 倍下糊成一团，画了反而看不清城市结构。
        var lod = Detail;

        if (lod != Lod.Far) DrawLocationFrames(canvas, dirtyRect);

        if (Edges is not null) foreach (var e in Edges) DrawEdge(canvas, e, dirtyRect);
        if (LinkStartNode is not null || LinkStartTransit is not null) DrawLinkPreview(canvas);

        if (lod != Lod.Far)
        {
            if (Transits is not null) foreach (var t in Transits) { if (InView(new Point(t.X, t.Y), dirtyRect)) DrawTransit(canvas, t); }
            if (Nodes is not null) foreach (var n in Nodes) { if (InView(n.X, n.Y, CardW, CardH, dirtyRect)) DrawCard(canvas, n); }
        }
        else if (Nodes is not null)
        {
            DrawCityMass(canvas, dirtyRect);
        }

        if (lod == Lod.Near)
        {
            DrawLocationNotes(canvas);
            if (Nodes is not null) foreach (var n in Nodes) DrawNodeNote(canvas, n);
            if (Transits is not null) foreach (var t in Transits) DrawTransitNote(canvas, t);
            DrawEdgeNotes(canvas);
        }
        DrawEdgeLengthLabels(canvas);

        DrawMarquee(canvas);
        if (Menu is not null) DrawMenu(canvas);
    }

    /// <summary>远景：地点不再画卡片，只画一团建筑块 + 名字，像地图上的街区底色。</summary>
    private void DrawCityMass(ICanvas canvas, RectF dirty)
    {
        if (Locations is null) return;
        foreach (var loc in Locations)
        {
            if (loc.Scenes.Count == 0) continue;
            var frame = LocationFrame(loc);
            if (frame is null) continue;
            var f = frame.Value;
            if (!InView(f.X, f.Y, f.Width, f.Height, dirty, 60)) continue;
            var sp = new RectF((float)(f.X * Scale + OffsetX), (float)(f.Y * Scale + OffsetY),
                (float)(f.Width * Scale), (float)(f.Height * Scale));
            var c = GroupColor(loc.Id);
            canvas.FillColor = c.WithAlpha(0.55f);
            canvas.FillRoundedRectangle(sp, (float)Math.Max(2, 6 * Scale));
            if (sp.Width < 46) continue;
            canvas.FontColor = Color.FromArgb("#4A3226");
            canvas.FontSize = (float)Math.Clamp(11 * Scale, 8, 12);
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.DrawString(loc.Name, sp.X + 3, sp.Y, sp.Width - 6, (float)Math.Max(11, 15 * Scale),
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }
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

    /// <summary>地点框的世界坐标矩形（含留白），带一遍算好的缓存。</summary>
    public RectF? LocationFrame(MapLocation loc)
    {
        EnsureFrames();
        return _frames is not null && _frames.TryGetValue(loc.Id, out var f) ? f : null;
    }

    private Dictionary<string, RectF?>? _frames;
    private int _frameBuiltFor = -1;
    private int _drawTick;

    /// <summary>
    /// 框的位置只随卡片动，父子关系不常变——所以每帧扫一遍卡片分桶，而不是每个框各自递归扫全表。
    /// 原来那写法在几百个地点的城区图上，一次重绘要算几十万次比较。
    /// </summary>
    private void EnsureFrames()
    {
        if (Locations is null) { _frames = null; return; }
        if (_frames is not null && _frameBuiltFor == _drawTick) return;
        _frameBuiltFor = _drawTick;

        // 子地点表（只在地点增删改父时重建）
        var children = new Dictionary<string, List<string>>();
        var byId = new Dictionary<string, MapLocation>();
        foreach (var l in Locations)
        {
            byId[l.Id] = l;
            if (string.IsNullOrEmpty(l.ParentId)) continue;
            if (!children.TryGetValue(l.ParentId, out var list)) children[l.ParentId] = list = new List<string>();
            list.Add(l.Id);
        }
        // 环形展开时不该再去全表找自己：按 Id 直取
        MapLocation? Find(string id) => byId.TryGetValue(id, out var l) ? l : null;

        // 卡片包围盒分桶
        var bucket = new Dictionary<string, (double MinX, double MinY, double MaxX, double MaxY)>();
        if (Nodes is not null)
            foreach (var n in Nodes)
            {
                if (!bucket.TryGetValue(n.Location.Id, out var b))
                    bucket[n.Location.Id] = b = (double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
                bucket[n.Location.Id] = (Math.Min(b.MinX, n.X), Math.Min(b.MinY, n.Y),
                    Math.Max(b.MaxX, n.X + CardW), Math.Max(b.MaxY, n.Y + CardH));
            }

        var frames = new Dictionary<string, RectF?>();
        const double emptyW = 230, emptyH = 100, pad = 56, title = 44;

        foreach (var loc in Locations)
        {
            // 自身 + 全部后代
            var ids = new List<string> { loc.Id };
            for (var i = 0; i < ids.Count; i++)
                if (children.TryGetValue(ids[i], out var kids))
                    ids.AddRange(kids);

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            var found = false;
            foreach (var id in ids)
            {
                if (bucket.TryGetValue(id, out var b))
                {
                    found = true;
                    minX = Math.Min(minX, b.MinX);
                    minY = Math.Min(minY, b.MinY);
                    maxX = Math.Max(maxX, b.MaxX);
                    maxY = Math.Max(maxY, b.MaxY);
                }
                // 空子地点也要计入父框，父框才真能包住子框（一环套一环）
                var me = Find(id);
                if (me is null || bucket.ContainsKey(id)) continue;
                found = true;
                var a = new Point(me.X < 0 ? 0 : me.X, me.Y < 0 ? 0 : me.Y);
                minX = Math.Min(minX, a.X - emptyW / 2);
                minY = Math.Min(minY, a.Y - emptyH / 2);
                maxX = Math.Max(maxX, a.X + emptyW / 2);
                maxY = Math.Max(maxY, a.Y + emptyH / 2);
            }
            if (!found)
            {
                frames[loc.Id] = new RectF((float)((loc.X < 0 ? 0 : loc.X) - emptyW / 2), (float)((loc.Y < 0 ? 0 : loc.Y) - emptyH / 2),
                    (float)emptyW, (float)emptyH);
                continue;
            }
            var extraHead = ids.Count > 1 ? title + pad : 0;
            frames[loc.Id] = new RectF((float)(minX - pad), (float)(minY - pad - title - extraHead),
                (float)(maxX - minX + pad * 2), (float)(maxY - minY + pad * 2 + title + extraHead));
        }
        _frames = frames;
    }

    /// <summary>地点标记框：淡色底 + 色描边 + 地点名标题。框只做分组标记，不参与连线。</summary>
    private void DrawLocationFrames(ICanvas canvas, RectF dirty)
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
            if (!InView(f.X, f.Y, f.Width, f.Height, dirty)) continue;   // 屏外的大框不画
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

            // 建筑外轮廓（真实导入才有）：按轮廓画，比圆角框像一栋楼
            if (loc.Outline.Count >= 3 && Scale >= 0.35)
            {
                var ring = new PathF();
                var first = new Point(loc.Outline[0].X, loc.Outline[0].Y);
                ring.MoveTo(ScreenPt(first));
                for (var i = 1; i < loc.Outline.Count; i++)
                    ring.LineTo(ScreenPt(new Point(loc.Outline[i].X, loc.Outline[i].Y)));
                ring.Close();
                canvas.FillColor = GroupColor(loc.Id).WithAlpha(0.32f);
                canvas.FillPath(ring);
                canvas.StrokeSize = (float)Math.Max(0.6, 1.1 * Scale);
                canvas.StrokeColor = GroupColor(loc.Id).WithAlpha(0.9f);
                canvas.DrawPath(ring);
            }

            // 标题（左上角）
            var titleH = (float)(24 * Scale);
            canvas.FontColor = GroupColor(loc.Id);
            canvas.FontSize = (float)Math.Max(10, 14 * Scale);
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            var floors = loc.FloorBadge;
            canvas.DrawString(loc.Name + (isChild ? " ▸" : "") + floors, (float)(sp.X + 8 * Scale), (float)(sp.Y + 2 * Scale),
                (float)(sp.Width - 16 * Scale), titleH,
                HorizontalAlignment.Left, VerticalAlignment.Center);
            // 真实地址（拉近了才写字，远景写字只会糊）
            if (Scale >= 0.8 && !string.IsNullOrWhiteSpace(loc.Address))
            {
                canvas.FontColor = Color.FromArgb("#7A6252");
                canvas.FontSize = (float)Math.Max(8, 10 * Scale);
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
                canvas.DrawString(loc.Address, (float)(sp.X + 8 * Scale), (float)(sp.Y + 24 * Scale),
                    (float)(sp.Width - 16 * Scale), titleH, HorizontalAlignment.Left, VerticalAlignment.Center);
            }

            // 地点缩略图（名字下方的方框内；有场景背景才显示）
            var thumb = GetLocationThumbImg(loc);
            if (thumb is not null)
            {
                var availW = sp.Width - 14 * Scale;
                var availH = sp.Height - titleH - 8 * Scale;
                if (availW > 0 && availH > 0)
                {
                    var iw = thumb.Width > 0 ? thumb.Width : 300;
                    var ih = thumb.Height > 0 ? thumb.Height : 160;
                    var ratio = Math.Min((float)(availW / iw), (float)(availH / ih));
                    var w = Math.Max(10f, (float)(iw * ratio));
                    var h = Math.Max(10f, (float)(ih * ratio));
                    canvas.DrawImage(thumb, (float)(sp.X + 7 * Scale), (float)(sp.Y + titleH + 4 * Scale), w, h);
                }
            }

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

    /// <summary>
    /// 一条路该多宽、涂什么色：MinScale 之下这一级路就不画了（远景只见高速与主街，拉近才长出小巷）。
    /// 画两遍——先深一条路基、再亮一条路面，路与路相交时才有真地图那种「路网」层次。
    /// </summary>
    private static (double Width, string Surface, string Casing, double MinScale, bool Dash) RoadStyle(string? kind) => kind switch
    {
        RoadKinds.Highway => (7.2, "#E3B34C", "#6B4A18", 0.28, false),
        RoadKinds.Trunk => (5.6, "#E8C97A", "#7A5620", 0.30, false),
        RoadKinds.Street => (4.0, "#D8C3AA", "#6E5240", 0.34, false),
        RoadKinds.Lane => (2.4, "#E4D6C4", "#7A5F4A", 0.62, false),
        RoadKinds.Walkway => (1.8, "#BFD6BC", "#4E6B4A", 0.60, true),
        RoadKinds.Steps => (1.6, "#D6C8E2", "#5B4A6B", 0.62, true),
        RoadKinds.Bridge => (4.6, "#DCCDB8", "#5D4037", 0.42, false),
        RoadKinds.Tunnel => (4.0, "#BFCAD6", "#3E5163", 0.42, true),
        RoadKinds.Waterway => (5.0, "#A8CFE0", "#2F6A85", 0.30, false),
        RoadKinds.Interior => (1.4, "#E7D9A8", "#8A6D1A", 0.70, true),
        _ => (4.0, "#D8C3AA", "#6E5240", 0.34, false),
    };

    /// <summary>这条路的可见包围盒（端点 + 折点），用来把屏外的路整条跳过。</summary>
    private RectF EdgeBounds(MapEdge e)
    {
        var pts = EdgePoints(e);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in pts)
        {
            minX = Math.Min(minX, (float)p.X);
            minY = Math.Min(minY, (float)p.Y);
            maxX = Math.Max(maxX, (float)p.X);
            maxY = Math.Max(maxY, (float)p.Y);
        }
        if (pts.Count == 0) return new RectF(0, 0, 0, 0);
        return new RectF(minX, minY, maxX - minX, maxY - minY);
    }

    private void DrawEdge(ICanvas canvas, MapEdge e, RectF dirty)
    {
        var style = RoadStyle(e.Road);
        var selected = SelectedEdgeId == e.Id;
        if (Scale < style.MinScale && !selected) return;                      // 这一级路在这个缩放还不该露面
        var bounds = EdgeBounds(e);
        if (!InView(bounds.X, bounds.Y, bounds.Width, bounds.Height, dirty, 120)) return;

        var pts = SampleEdge(e);
        if (pts.Count < 2) return;

        var path = new PathF();
        path.MoveTo(ScreenPt(pts[0]));
        for (var i = 1; i < pts.Count; i++) path.LineTo(ScreenPt(pts[i]));

        var w = Math.Max(0.8, style.Width * Scale);
        var dashed = e.Length == 0 || style.Dash;
        if (dashed) canvas.StrokeDashPattern = new[] { 7f, 5f };

        // 路基（深）→ 路面（亮）：两条叠出来才有「一条路」的分量
        canvas.StrokeColor = selected ? Color.FromArgb("#5D4037") : ParseHex(style.Casing, "#5D4037").WithAlpha(0.85f);
        canvas.StrokeSize = (float)(selected ? w + 3.4 : w + Math.Max(1.0, w * 0.34));
        canvas.DrawPath(path);
        canvas.StrokeColor = ParseHex(style.Surface, "#B08968");
        canvas.StrokeSize = (float)w;
        canvas.DrawPath(path);
        canvas.StrokeDashPattern = null;

        // 箭头（终点方向）
        if (pts.Count >= 2)
        {
            var tip = pts[^1];
            var prev = pts[Math.Max(0, pts.Count - 3)];
            var d = new Point(tip.X - prev.X, tip.Y - prev.Y);
            var len = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            if (len > 0.001)
            {
                d = new Point(d.X / len, d.Y / len);
                DrawArrow(canvas, ScreenPt(pts[^1]), d, ParseHex(style.Casing, "#5D4037"));
            }
            // 单行道再往中段补一支：一眼看出这路不许倒着开
            if (e.OneWay && pts.Count > 5)
            {
                var m = pts.Count / 2;
                var md = new Point(pts[m].X - pts[m - 1].X, pts[m].Y - pts[m - 1].Y);
                var ml = Math.Sqrt(md.X * md.X + md.Y * md.Y);
                if (ml > 0.001) DrawArrow(canvas, ScreenPt(pts[m]), new Point(md.X / ml, md.Y / ml), ParseHex(style.Casing, "#5D4037"));
            }
        }

        // 红绿灯 / 收费站：路上釘个小记号，比在备注里写「有灯」直观
        if (Scale >= 0.5 && (e.TrafficLight || e.Toll))
        {
            var mid = pts[pts.Count / 2];
            var sp = ScreenPt(mid);
            if (e.TrafficLight)
            {
                canvas.FillColor = Color.FromArgb("#D2544A");
                canvas.FillEllipse((float)(sp.X - 4), (float)(sp.Y - 4), 8, 8);
                canvas.StrokeColor = Color.FromArgb("#FFFDF9");
                canvas.StrokeSize = 1.4f;
                canvas.DrawEllipse((float)(sp.X - 4), (float)(sp.Y - 4), 8, 8);
            }
            if (e.Toll)
            {
                canvas.FillColor = Color.FromArgb("#8A5A3B");
                canvas.FillRoundedRectangle((float)(sp.X + (e.TrafficLight ? 10 : 2)), (float)(sp.Y - 7), 22, 14, 4);
                canvas.FontColor = Colors.White;
                canvas.FontSize = 9;
                canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
                canvas.DrawString(e.TollFee > 0 ? $"¥{e.TollFee:0.#}" : "收费",
                    (float)(sp.X + (e.TrafficLight ? 10 : 2)), (float)(sp.Y - 7), 22, 14,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }

        // 路名（长江路）：贴近中段，写字要有白底，不然压在路面色上看不清
        if (Scale >= 0.5 && !string.IsNullOrWhiteSpace(e.RoadName))
        {
            var mid = pts[pts.Count / 2];
            DrawRoadName(canvas, ScreenPt(mid), e.RoadName);
        }

        // 折点手柄（选中时显示）
        if (selected && e.Waypoints.Count > 0)
        {
            canvas.FillColor = Color.FromArgb("#8D6E63");
            foreach (var w2 in e.Waypoints)
            {
                var sp = ScreenPt(new Point(w2.X, w2.Y));
                canvas.FillEllipse((float)(sp.X - 5), (float)(sp.Y - 5), 10, 10);
            }
        }
    }

    private void DrawRoadName(ICanvas canvas, Point sp, string name)
    {
        var text = name.Length > 12 ? name[..12] : name;
        var tw = (float)(text.Length * 11 + 8);
        var th = 15f;
        canvas.FillColor = Color.FromArgb("#C8FFFDF9");
        canvas.FillRoundedRectangle((float)(sp.X - tw / 2), (float)(sp.Y - th / 2), tw, th, 4);
        canvas.FontColor = Color.FromArgb("#4A3226");
        canvas.FontSize = 11;
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.DrawString(text, (float)(sp.X - tw / 2), (float)(sp.Y - th / 2), tw, th,
            HorizontalAlignment.Center, VerticalAlignment.Center);
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
        var kind = t.Transit.Junction;

        // 路口有自己的长相：环岛画圈、收费站画方、红绿灯画点、楼梯画个小梯子
        canvas.FillColor = kind switch
        {
            JunctionKinds.Roundabout => Color.FromArgb("#FFFDF9"),
            JunctionKinds.Toll => Color.FromArgb("#8A5A3B"),
            JunctionKinds.Crossing => Color.FromArgb("#D2544A"),
            JunctionKinds.Stop => Color.FromArgb("#5B8DEF"),
            JunctionKinds.Junction => Color.FromArgb("#F0E0C8"),
            _ => Color.FromArgb("#FFFDF9"),
        };
        canvas.StrokeSize = selected ? (float)(3 * Scale) : (float)(2 * Scale);
        canvas.StrokeColor = selected ? Color.FromArgb("#5D4037") : Color.FromArgb("#8D6E63");
        if (kind == JunctionKinds.Toll || kind == JunctionKinds.Stop)
        {
            canvas.FillRoundedRectangle((float)(sp.X - r), (float)(sp.Y - r), (float)(r * 2), (float)(r * 2), (float)(r * 0.4));
            canvas.DrawRoundedRectangle((float)(sp.X - r), (float)(sp.Y - r), (float)(r * 2), (float)(r * 2), (float)(r * 0.4));
        }
        else
        {
            canvas.FillEllipse((float)(sp.X - r), (float)(sp.Y - r), (float)(r * 2), (float)(r * 2));
            canvas.DrawEllipse((float)(sp.X - r), (float)(sp.Y - r), (float)(r * 2), (float)(r * 2));
            if (kind == JunctionKinds.Roundabout)
            {
                canvas.StrokeSize = (float)Math.Max(1, 1.4 * Scale);
                canvas.DrawEllipse((float)(sp.X - r * 0.45), (float)(sp.Y - r * 0.45), (float)(r * 0.9), (float)(r * 0.9));
            }
        }

        canvas.FontColor = Color.FromArgb("#3E2A1C");
        canvas.FontSize = (float)Math.Max(8, 11 * Scale);
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        var name = t.Name;
        if (name.Length > 10) name = name[..10];
        canvas.DrawString(name, (float)(sp.X - r), (float)(sp.Y + r + 2), (float)(r * 2), 16,
            HorizontalAlignment.Center, VerticalAlignment.Top);
        // 性质写在名字下面一行（路口/环岛/收费站…），一眼能认出这是什么节点
        if (kind.Length > 0 && Scale >= 0.55)
        {
            canvas.FontColor = Color.FromArgb("#8A7259");
            canvas.FontSize = (float)Math.Max(7, 9 * Scale);
            canvas.DrawString(JunctionKinds.Display(kind), (float)(sp.X - r), (float)(sp.Y + r + 15), (float)(r * 2), 12,
                HorizontalAlignment.Center, VerticalAlignment.Top);
        }
    }

    // ==================== 场景卡片 ====================

    private void DrawCard(ICanvas canvas, MapCanvasNode n)
    {
        var rect = new RectF((float)(n.X * Scale + OffsetX), (float)(n.Y * Scale + OffsetY),
            (float)(CardW * Scale), (float)(CardH * Scale));
        var group = GroupColor(n.GroupKey);

        // 这一层之外的房间淡成影子：还看得见它在哪，但不抢当前层的注意力
        if (Ghosted(n))
        {
            canvas.FillColor = group.WithAlpha(0.22f);
            canvas.FillRoundedRectangle(rect, 10);
            canvas.StrokeSize = 1;
            canvas.StrokeColor = group.WithAlpha(0.35f);
            canvas.DrawRoundedRectangle(rect, 10);
            return;
        }

        // 主体：背景图或底色
        var img = Detail == Lod.Near && !string.IsNullOrWhiteSpace(n.Scene.Background) ? GetImage(n) : null;
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
        if (!InView(worldPos, _dirty)) return;
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
        // 远景字比路多就成噪声了：只给选中的那条标尺，其余拉近了再说。
        if (Detail == Lod.Far && SelectedEdgeId is null) return;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.FontSize = (float)Math.Max(9, 12 * Scale);
        foreach (var e in Edges)
        {
            if (e.Length == 0) continue;   // 未丈量：虚线，无长度
            if (Detail == Lod.Far && e.Id != SelectedEdgeId) continue;
            var bounds = EdgeBounds(e);
            if (!InView(bounds.X, bounds.Y, bounds.Width, bounds.Height, _dirty)) continue;
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
                        // 自动：按几何像素 × 比例尺估算每段
                        text = $"~{(int)Math.Round(segPxArr[i] * MetersPerUnit)}m";
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
                ? $"~{(int)Math.Round(EdgeLengthPixelsLocal(e) * MetersPerUnit)}m"
                : $"{e.Length:0}m";
            DrawLengthChip(canvas, mid, text2);
        }
    }

    /// <summary>本次绘制的可视区（剔除用）；Draw 开头赋值。</summary>
    private RectF _dirty;

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
            if (Ghosted(n)) continue;   // 淡出的那层不接鼠标：点它等于点到隔壁楼层
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
            // 先按包围盒甩掉八成不挨着的边：城市级地图上每次移动都全表采样折线，光标会明显发涩。
            var bb = EdgeBounds(e);
            if (world.X < bb.X - tol || world.X > bb.X + bb.Width + tol
                || world.Y < bb.Y - tol || world.Y > bb.Y + bb.Height + tol) continue;
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
        if (_imgCache.TryGetValue(n.Id, out var img))
        {
            // 这张这次真画到屏上了：顶到上限时才记账，免得把正在看的图当成最久没露面的踢掉
            if (_imgOrder.Count >= ImageCache.Cap) ImageCache.Touch(_imgOrder, n.Id);
            return img;
        }
        ImageCache.Put(_imgCache, _imgOrder, n.Id, null); // 占位，防重复加载
        LoadImageAsync(n);
        return null;
    }
private Func<MapCanvasNode, string?>? _bgResolver;

    /// <summary>背景图相对路径 → 绝对路径的解析器（由 MapViewModel 注入，内部走 MapService.ResolveBackground）。</summary>
    public void AttachBgResolver(Func<MapCanvasNode, string?> resolver) => _bgResolver = resolver;

    private string? _resolveBg(MapCanvasNode n) => _bgResolver?.Invoke(n);

    // ---- 地点缩略图 ----
    private Func<MapLocation, string?>? _locThumbResolver;

    /// <summary>地点缩略图绝对路径解析器（返回视频首帧PNG/图片路径；由 MapViewModel 注入）。</summary>
    public void AttachLocationThumbResolver(Func<MapLocation, string?> resolver) => _locThumbResolver = resolver;

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
                    ImageCache.Put(_imgCache, _imgOrder, n.Id, Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(ms));
                }
                catch { ImageCache.Put(_imgCache, _imgOrder, n.Id, null); }
                NeedRedraw();
            });
        });
    }

    // ==================== 地点缩略图加载 ====================

    public Microsoft.Maui.Graphics.IImage? GetLocationThumbImg(MapLocation loc)
    {
        if (_locThumbResolver is null) return null;
        if (_locThumbCache.TryGetValue(loc.Id, out var img))
        {
            if (_thumbOrder.Count >= ImageCache.Cap) ImageCache.Touch(_thumbOrder, loc.Id);
            return img;
        }
        ImageCache.Put(_locThumbCache, _thumbOrder, loc.Id, null); // 占位，防重复加载
        LoadLocationThumbAsync(loc);
        return null;
    }

    private void LoadLocationThumbAsync(MapLocation loc)
    {
        var path = _locThumbResolver(loc);
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
                    ImageCache.Put(_locThumbCache, _thumbOrder, loc.Id, Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(ms));
                }
                catch { ImageCache.Put(_locThumbCache, _thumbOrder, loc.Id, null); }
                NeedRedraw();
            });
        });
    }

    // ==================== 文本换行 ====================

    public static List<string> Wrap(string text, int maxChars)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text) || maxChars < 1)
        {
            lines.Add(text ?? "");
            return lines;
        }
        // 直接按段切片：从前一个字符一个字符往上攒，长备注能白分配几百个中间字符串
        var count = (text.Length + maxChars - 1) / maxChars;
        for (var i = 0; i < count; i++)
        {
            var start = i * maxChars;
            lines.Add(text.Substring(start, Math.Min(maxChars, text.Length - start)));
        }
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