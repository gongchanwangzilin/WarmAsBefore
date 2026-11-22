using System.Diagnostics;
using Microsoft.Maui.Graphics;
using WarmAsBefore.DesignSystem.Worlds;
using WarmAsBefore.Helpers;
using WarmAsBefore.Models;
using WarmAsBefore.Services;
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
    /// <summary>地块（绿地/水面/航道/广场/分区）：画在所有路底下的地表。</summary>
    public IReadOnlyList<MapRegion>? Regions { get; set; }

    /// <summary>
    /// 底图瓦片层：铺在地表最底下，叠加层照旧压在上面。
    /// 为 null 或来源是「手工绘制」时这一层完全不画 —— 现状那套画布一个像素都不变。
    /// </summary>
    public TileLayerDrawable? Tiles { get; set; }

    /// <summary>
    /// 实时路况判出来的灯色（连线 Id → 灯色）。只有开了「红绿灯看高德路况」才有这一份；
    /// 没开着、或这条路还没问到（路没名、断了）就留空，那颗灯照每盏灯自己的本地周期画。
    /// </summary>
    public IReadOnlyDictionary<string, string>? LiveLights { get; set; }

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
    /// <summary>编辑器那一层（地点框、备注卡、空地点提示）：浏览态收起，地图才像地图。</summary>
    public bool MapChrome { get; set; } = true;
    /// <summary>区域划分：正在编辑的那块地的顶点手柄要画出来。</summary>
    public bool RegionEditMode { get; set; }
    public string? RegionEditId { get; set; }
    public string? SelectedEdgeId { get; set; }
    /// <summary>编辑器里选中的地块（画布高亮、右键菜单认它）。</summary>
    public string? SelectedRegionId { get; set; }
    /// <summary>「圈一块地」进行中的角点（世界坐标）：橡皮筋跟着光标，先让人看清这块地长什么样。</summary>
    public List<Point> RegionDraft { get; } = new();
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

    // ==================== 取色：只走世界令牌 ====================
    // 地图的每一档颜色都由「当前地面 + 这个世界最浅的一份 + 墨色」混出来：
    // 深色世界里同一套相对关系仍然成立（纸面沉、路面浮、墨色亮），不必维护两套色表。
    // 下面的十六进制只剩两类：调色种子（交给 Reanchor 重钉到当前地面）与红绿灯三色
    // （红停绿走是这盏灯本身的意思，不随世界换）。界面里不该再出现裸色值。
    private static readonly Color[] GroupSeeds =
    {
        Color.FromArgb("#E8B4B8"), Color.FromArgb("#A8C3E0"), Color.FromArgb("#C3B1E1"),
        Color.FromArgb("#A9CBB7"), Color.FromArgb("#F0D9A8"), Color.FromArgb("#F2B8C6"),
        Color.FromArgb("#B5D8C7"), Color.FromArgb("#D9C8A9")
    };

    // 初值只是「还没跟世界对齐」时的占位；第一次 SyncPalette 就会全部改写。
    private static Color _palGround = Color.FromArgb("#F5F0E8");
    private static Color _palLift = Color.FromArgb("#FFFFFF");
    private static Color _palInk = Color.FromArgb("#4A3D2C");
    private static Color _palMuted = Color.FromArgb("#6B5D4B");
    private static Color _palSurface = Color.FromArgb("#FDF8F0");
    private static Color _palWell = Color.FromArgb("#EADCC0");
    private static Color _palLine = Color.FromArgb("#F5DFC0");
    private static Color _palPaper = Color.FromArgb("#FDF6EE");
    private static Color _palAccent = Color.FromArgb("#E8B87A");
    private static Color[] _palGroups = System.Array.Empty<Color>();
    private static bool _palReady;

    private static Color Mix(Color a, Color b, double t) => GraphPalette.Blend(a, b, t);

    private static bool SameColor(Color a, Color b) =>
        a.Red == b.Red && a.Green == b.Green && a.Blue == b.Blue && a.Alpha == b.Alpha;

    /// <summary>地面换了才重算（切世界 / 系统翻深浅）。每帧都要取十几次，不能每帧做 HSL 换算。</summary>
    private static void SyncPalette()
    {
        var ground = StyleWorldTokens.Pick("PageBg", Color.FromArgb("#F5F0E8"));
        if (_palReady && SameColor(_palGround, ground)) return;
        _palReady = true;
        _palGround = ground;
        _palLift = StyleWorldTokens.Pick("ShadowLight", Color.FromArgb("#FFFFFF"));
        _palInk = StyleWorldTokens.Pick("SurfaceFg", Color.FromArgb("#4A3D2C"));
        _palMuted = StyleWorldTokens.Pick("MutedFg", Color.FromArgb("#6B5D4B"));
        _palSurface = StyleWorldTokens.Pick("SurfaceBg", Color.FromArgb("#FDF8F0"));
        _palWell = StyleWorldTokens.Pick("WellBg", Color.FromArgb("#EADCC0"));
        _palPaper = Mix(ground, _palLift, StyleWorldTokens.Dark ? 0.13 : 0.52);
        _palLine = Mix(ground, _palInk, 0.14);
        _palAccent = GraphPalette.Person;
        _palGroups = new Color[GroupSeeds.Length];
        for (var i = 0; i < GroupSeeds.Length; i++)
            _palGroups[i] = StyleWorldTokens.Reanchor(GroupSeeds[i], ground);
    }

    // 下面这些名字是绘制里已有的「角色」，值全部来自当前世界的一份
    private static Color InkStrong => _palInk;
    private static Color InkSoft => _palInk;
    private static Color CardOutline => _palLine;
    private static Color Gold => _palAccent;
    private static Color GridPaper => _palPaper;
    private static Color GridLine => _palLine;
    private static Color NamePlate => Mix(_palInk, _palPaper, 0.1).WithAlpha(0.88f);
    private static Color FrameFillParent => _palSurface.WithAlpha(0.14f);
    private static Color FrameFillChild => _palSurface.WithAlpha(0.24f);
    private static Color FrameFillMarquee => _palAccent.WithAlpha(0.22f);
    private static Color FrameTitleInk => _palMuted;
    private static Color FarMassFont => _palInk;
    private static Color TransitFill => GraphPalette.Place.WithAlpha(0.16f);
    private static Color TransitStroke => GraphPalette.Place;

    /// <summary>针的颜色：浅色世界里往墨色压一档，暗色世界里往亮处提一档——都是为了让它在纸面上站住。</summary>
    private static Color PinTone(Color group) =>
        StyleWorldTokens.Dark ? Mix(group, _palLift, 0.10) : Mix(group, _palInk, 0.30);

    public Color GroupColor(string groupKey)
    {
        SyncPalette();
        var h = 0;
        foreach (var ch in groupKey) h = (h * 31 + ch) & 0x7fffffff;
        return _palGroups[h % _palGroups.Length];
    }

    // ==================== 慢帧诊断 ====================
    // 卡了要知道卡在画布还是页面：Draw 总耗时超过 33ms（一帧的预算）就记一条。
    // 同标签最多 2 秒一条——慢帧本身已经在掉帧，再让日志每帧写一条，诊断就成了新的卡顿源。
    private static long _lastSlowFrameLog;

    private void NoteSlowFrame(long startedAt)
    {
        var elapsed = (Stopwatch.GetTimestamp() - startedAt) * 1000.0 / Stopwatch.Frequency;
        if (elapsed <= 33) return;
        var now = Stopwatch.GetTimestamp();
        if (now - _lastSlowFrameLog < Stopwatch.Frequency * 2) return;
        _lastSlowFrameLog = now;
        PerfLog.Note("画布 · 慢帧",
            $"{elapsed:0.0} ms · 节点 {Nodes?.Count} · 边 {Edges?.Count} · zoom {Scale:0.##}");
    }

    // ==================== Draw ====================

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var frameStart = Stopwatch.GetTimestamp();
        _drawTick++;
        _dirty = dirtyRect;
        _viewWv = dirtyRect.Width;
        _viewHv = dirtyRect.Height;
        SyncPalette();
        DrawGround(canvas, dirtyRect);

        // 底图（在线 / 离线瓦片）压在地表上、地块与路网下：缺图时露出的就是上面那层纸面。
        if (Tiles is { } tiles)
        {
            tiles.MetersPerUnit = MetersPerUnit;
            tiles.AttributionColor = _palMuted;
            tiles.Draw(canvas, dirtyRect, Scale, OffsetX, OffsetY);
        }

        // 地表在最底下：先铺地块（一片公园、一条河、一块校区），再往上长路、楼与字。
        if (Regions is not null) foreach (var r in Regions) DrawRegion(canvas, r, dirtyRect);

        // 远景只画大框：卡片与字在 0.3 倍下糊成一团，画了反而看不清城市结构。
        var lod = Detail;

        if (lod != Lod.Far && MapChrome) DrawLocationFrames(canvas, dirtyRect);

        if (Edges is not null) foreach (var e in Edges) DrawEdge(canvas, e, dirtyRect);
        if (LinkStartNode is not null || LinkStartTransit is not null) DrawLinkPreview(canvas);
        if (RegionDraft.Count > 0) DrawRegionDraft(canvas);

        if (lod != Lod.Far)
        {
            if (Transits is not null) foreach (var t in Transits) { if (InView(new Point(t.X, t.Y), dirtyRect)) DrawTransit(canvas, t); }
            if (Nodes is not null) foreach (var n in Nodes) { if (InView(n.X, n.Y, CardW, CardH, dirtyRect)) DrawPoi(canvas, n); }
        }
        else if (Nodes is not null)
        {
            DrawCityMass(canvas, dirtyRect);
        }

        if (lod == Lod.Near && MapChrome)
        {
            DrawLocationNotes(canvas);
            if (Nodes is not null) foreach (var n in Nodes) DrawNodeNote(canvas, n);
            if (Transits is not null) foreach (var t in Transits) DrawTransitNote(canvas, t);
            DrawEdgeNotes(canvas);
        }
        DrawEdgeLengthLabels(canvas);

        // 顶点手柄压在一切之上：拖它的时候不该被路名或卡片挡掉
        if (RegionEditMode && RegionEditId is { } editId
            && Regions?.FirstOrDefault(r => r.Id == editId) is { } editing)
            DrawRegionHandles(canvas, editing);

        DrawMarquee(canvas);
        if (Menu is not null) DrawMenu(canvas);

        NoteSlowFrame(frameStart);
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
            canvas.FontColor = InkSoft;
            canvas.FontSize = (float)Math.Clamp(11 * Scale, 8, 12);
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.DrawString(loc.Name, sp.X + 3, sp.Y, sp.Width - 6, (float)Math.Max(11, 15 * Scale),
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }
    }

    /// <summary>地图上颜色的「族」：人物/地点/绿地/地块各自一族，世界换档时整族跟着走。</summary>
    private enum Tone { Amber, Green, Blue, Sand, Rose }

    private static Color ToneColor(Tone t) => t switch
    {
        Tone.Green => GraphPalette.Tag,
        Tone.Blue => GraphPalette.Place,
        Tone.Sand => GraphPalette.ImageFrame,
        Tone.Rose => GraphPalette.Emotion,
        _ => GraphPalette.Person,
    };

    /// <summary>一块地该怎么铺：面状类别填一层淡色再描圈边；航道是细长一条，按「路基→路面」两条带子画。</summary>
    private static (double Width, double MinScale, bool Dash, double Skin, Tone Tone) RegionStyle(string? kind) => kind switch
    {
        RegionKinds.Water => (1.4, 0.24, false, 0.55, Tone.Blue),
        RegionKinds.Plaza => (1.2, 0.30, false, 0.46, Tone.Sand),
        RegionKinds.District => (1.8, 0.18, true, 0.14, Tone.Rose),
        RegionKinds.Route => (3.8, 0.26, true, 0.95, Tone.Blue),
        _ => (1.4, 0.24, false, 0.52, Tone.Green),
    };

    private void DrawRegion(ICanvas canvas, MapRegion r, RectF dirty)
    {
        if (!r.HasShape) return;
        var style = RegionStyle(r.Kind);
        var selected = SelectedRegionId == r.Id || RegionEditId == r.Id;
        if (Scale < style.MinScale && !selected) return;

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in r.Points)
        {
            minX = Math.Min(minX, (float)p.X);
            minY = Math.Min(minY, (float)p.Y);
            maxX = Math.Max(maxX, (float)p.X);
            maxY = Math.Max(maxY, (float)p.Y);
        }
        if (!InView(minX, minY, maxX - minX, maxY - minY, dirty, 120)) return;

        var linear = RegionKinds.IsLinear(r.Kind);
        var path = new PathF();
        path.MoveTo(ScreenPt(new Point(r.Points[0].X, r.Points[0].Y)));
        for (var i = 1; i < r.Points.Count; i++) path.LineTo(ScreenPt(new Point(r.Points[i].X, r.Points[i].Y)));
        if (!linear) path.Close();

        var family = ToneColor(style.Tone);
        var ink = selected ? InkStrong : Mix(family, InkStrong, 0.55);
        var w = Math.Max(0.8, style.Width * Scale);
        if (style.Dash) canvas.StrokeDashPattern = new[] { 6f, 4f };

        if (linear)
        {
            canvas.StrokeColor = ink.WithAlpha(0.75f);
            canvas.StrokeSize = (float)(w + Math.Max(1.0, w * 0.4));
            canvas.DrawPath(path);
            canvas.StrokeColor = Mix(family, _palPaper, 0.35).WithAlpha(selected ? 1f : 0.9f);
            canvas.StrokeSize = (float)w;
            canvas.DrawPath(path);
        }
        else
        {
            // 分区只是「这片叫这个名字」，不占地表颜色；绿化与水面才真的铺一层地皮。
            canvas.FillColor = family.WithAlpha((float)style.Skin);
            canvas.FillPath(path);
            canvas.StrokeColor = ink.WithAlpha(0.85f);
            canvas.StrokeSize = selected ? 2.4f : (float)w;
            canvas.DrawPath(path);
        }
        canvas.StrokeDashPattern = null;

        if (r.Name.Length > 0 && Detail != Lod.Far)
            DrawRoadName(canvas, ScreenPt(Centroid(r.Points)), r.Name);
    }

    /// <summary>多边形顶点的平均位置：地块的名字压在这儿。</summary>
    private static Point Centroid(IReadOnlyList<MapWaypoint> pts)
    {
        double x = 0, y = 0;
        foreach (var p in pts) { x += p.X; y += p.Y; }
        return new Point(x / pts.Count, y / pts.Count);
    }

    /// <summary>地面：一张铺满的图底。网格只在近处淡淡露一手，远景是一整片纸，像真地图的底。</summary>
    private void DrawGround(ICanvas canvas, RectF dirty)
    {
        canvas.FillColor = GridPaper;
        canvas.FillRectangle(dirty);
        var step = 56 * Scale;
        if (step < 18 || Scale < 0.4) return;
        canvas.StrokeSize = 1;
        canvas.StrokeColor = GridLine.WithAlpha(0.7f);
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
    private long _frameHash = -1;
    private int _drawTick;

    /// <summary>
    /// 框的位置只随卡片动，父子关系不常变——所以每帧扫一遍卡片分桶，而不是每个框各自递归扫全表。
    /// 原来那写法在几百个地点的城区图上，一次重绘要算几十万次比较。
    /// </summary>
    private void EnsureFrames()
    {
        if (Locations is null) { _frames = null; return; }
        // 框只随「地点/卡片的布局」变化：按布局算一个哈希，没变就整套复用，
        // 不再每帧重建一堆字典（审计 P6）。
        var hash = ComputeLayoutHash();
        if (_frames is not null && hash == _frameHash) return;
        _frameHash = hash;

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

    /// <summary>布局指纹：地点/卡片的位置一变才需要重建框表，纯数值比较，零分配。</summary>
    private long ComputeLayoutHash()
    {
        unchecked
        {
            long h = 17;
            h = h * 31 + (Locations?.Count ?? 0);
            if (Locations is not null)
                foreach (var l in Locations) h = h * 31 + l.X.GetHashCode() + l.Y.GetHashCode() * 3;
            h = h * 31 + (Nodes?.Count ?? 0);
            if (Nodes is not null)
                foreach (var n in Nodes) h = h * 31 + n.X.GetHashCode() + n.Y.GetHashCode() * 3;
            return h;
        }
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
            canvas.FillColor = isMarquee ? FrameFillMarquee
                : isChild ? FrameFillChild : FrameFillParent;
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
                canvas.FontColor = FrameTitleInk;
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
                canvas.FontColor = FarMassFont;
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
        canvas.FillColor = TransitFill;
        canvas.FillRectangle(sp);
        canvas.StrokeSize = (float)(1.5 * Scale);
        canvas.StrokeColor = TransitStroke;
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
    /// 一条路该多宽、涂哪一族色：MinScale 之下这一级路就不画了（远景只见高速与主街，拉近才长出小巷）。
    /// 画两遍——先一条「路基」、再一条「路面」，路与路相交时才有真地图那种路网层次。
    /// </summary>
    private static (double Width, double Mix, double MinScale, bool Dash, Tone Tone) RoadStyle(string? kind) => kind switch
    {
        RoadKinds.Highway => (7.6, 0.74, 0.28, false, Tone.Amber),
        RoadKinds.Trunk => (6.0, 0.60, 0.30, false, Tone.Amber),
        RoadKinds.County => (5.0, 0.48, 0.32, false, Tone.Amber),
        RoadKinds.Rural => (3.4, 0.30, 0.45, false, Tone.Sand),
        RoadKinds.Street => (4.4, 0.40, 0.34, false, Tone.Sand),
        RoadKinds.Lane => (2.6, 0.28, 0.62, false, Tone.Sand),
        RoadKinds.Walkway => (2.0, 0.42, 0.60, true, Tone.Green),
        RoadKinds.Steps => (1.8, 0.40, 0.62, true, Tone.Rose),
        RoadKinds.Bridge => (4.8, 0.44, 0.42, false, Tone.Sand),
        RoadKinds.Tunnel => (4.2, 0.46, 0.42, true, Tone.Blue),
        RoadKinds.Waterway => (5.2, 0.80, 0.30, false, Tone.Blue),
        RoadKinds.Interior => (1.6, 0.42, 0.70, true, Tone.Amber),
        _ => (4.4, 0.40, 0.34, false, Tone.Sand),
    };

    /// <summary>灯色：红停绿走黄等一等，三颗色是钉死的——灯不随世界换色，换到暗世界上也一眼读得出。</summary>
    private static Color SignalFill(string? state) => state switch
    {
        SignalKinds.Green => Color.FromArgb("#4E9A5B"),
        SignalKinds.Yellow => Color.FromArgb("#E0A93B"),
        _ => Color.FromArgb("#D2544A"),
    };

    /// <summary>
    /// 这一颗灯现在该画什么色。绘制在 UI 线程每一帧都跑，所以这里绝不开口问网络：
    /// 实时那份是 VM 慢慢灌进 <see cref="LiveLights"/> 的，字典里没有就退回本地周期。
    /// </summary>
    private string LightKindOf(MapEdge e)
        => LiveLights is { } live && live.TryGetValue(e.Id, out var kind) && kind.Length > 0
            ? kind
            : SignalClock.Now(e, true).Kind;

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

        var surface = Mix(_palPaper, ToneColor(style.Tone), style.Mix);
        var casing = Mix(surface, _palInk, 0.72);

        // 路基（沉）→ 路面（浮）：两条叠出来才有「一条路」的分量
        canvas.StrokeColor = selected ? InkStrong : casing.WithAlpha(0.9f);
        canvas.StrokeSize = (float)(selected ? w + 3.4 : w + Math.Max(1.0, w * 0.34));
        canvas.DrawPath(path);
        canvas.StrokeColor = surface;
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
                DrawArrow(canvas, ScreenPt(pts[^1]), d, casing);
            }
            // 单行道再往中段补一支：一眼看出这路不许倒着开
            if (e.OneWay && pts.Count > 5)
            {
                var m = pts.Count / 2;
                var md = new Point(pts[m].X - pts[m - 1].X, pts[m].Y - pts[m - 1].Y);
                var ml = Math.Sqrt(md.X * md.X + md.Y * md.Y);
                if (ml > 0.001) DrawArrow(canvas, ScreenPt(pts[m]), new Point(md.X / ml, md.Y / ml), casing);
            }
        }

        // 红绿灯 / 收费站：路上釘个小记号，比在备注里写「有灯」直观
        if (Scale >= 0.5 && (e.TrafficLight || e.Toll))
        {
            var mid = pts[pts.Count / 2];
            var sp = ScreenPt(mid);
            if (e.TrafficLight)
            {
                // 只画灯色，不写倒计时：字一多这张图就没法看了，秒数留给详细信息与流程图。
                // 认的是「顺着箭头那一侧（A→B）」现在能不能走：红=停，绿=过。
                canvas.FillColor = SignalFill(LightKindOf(e));
                canvas.FillEllipse((float)(sp.X - 4), (float)(sp.Y - 4), 8, 8);
                canvas.StrokeColor = _palLift;
                canvas.StrokeSize = 1.4f;
                canvas.DrawEllipse((float)(sp.X - 4), (float)(sp.Y - 4), 8, 8);
            }
            if (e.Toll)
            {
                canvas.FillColor = Mix(_palInk, ToneColor(Tone.Sand), 0.35f);
                canvas.FillRoundedRectangle((float)(sp.X + (e.TrafficLight ? 10 : 2)), (float)(sp.Y - 7), 22, 14, 4);
                canvas.FontColor = _palLift;
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
            canvas.FillColor = _palAccent;
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
        canvas.FillColor = _palSurface.WithAlpha(0.86f);
        canvas.FillRoundedRectangle((float)(sp.X - tw / 2), (float)(sp.Y - th / 2), tw, th, 4);
        canvas.FontColor = InkSoft;
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
        canvas.StrokeColor = Gold;
        var path = new PathF();
        path.MoveTo(ScreenPt(pts[0]));
        foreach (var p in pts.Skip(1)) path.LineTo(ScreenPt(p));
        canvas.DrawPath(path);
        canvas.StrokeDashPattern = null;

        canvas.FillColor = Gold;
        foreach (var p in LinkPreview)
        {
            var sp = ScreenPt(p);
            canvas.FillEllipse((float)(sp.X - 4), (float)(sp.Y - 4), 8, 8);
        }
    }

    /// <summary>圈地进行中：虚线多边形 + 每个角一个小圆点 + 一条跟着光标的橡皮筋。</summary>
    private void DrawRegionDraft(ICanvas canvas)
    {
        var pts = new List<Point>(RegionDraft);
        var ink = Gold;
        if (RegionDraft.Count >= 3 && Mouse is { } m) pts.Add(ScreenToWorld(m));
        if (pts.Count >= 3) pts.Add(pts[0]);                       // 首尾接上才看得出这是个圈

        canvas.StrokeSize = 2;
        canvas.StrokeDashPattern = new[] { 6f, 4f };
        canvas.StrokeColor = ink;
        var path = new PathF();
        path.MoveTo(ScreenPt(pts[0]));
        foreach (var p in pts.Skip(1)) path.LineTo(ScreenPt(p));
        canvas.DrawPath(path);
        canvas.StrokeDashPattern = null;

        foreach (var p in RegionDraft)
        {
            var sp = ScreenPt(p);
            canvas.FillColor = ink;
            canvas.FillEllipse((float)(sp.X - 4), (float)(sp.Y - 4), 8, 8);
            canvas.StrokeColor = _palLift;
            canvas.StrokeSize = 1.4f;
            canvas.DrawEllipse((float)(sp.X - 4), (float)(sp.Y - 4), 8, 8);
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
            JunctionKinds.Roundabout => _palSurface,
            JunctionKinds.Toll => Mix(_palInk, ToneColor(Tone.Sand), 0.35f),
            JunctionKinds.Crossing => ToneColor(Tone.Rose),
            JunctionKinds.Stop => ToneColor(Tone.Blue),
            JunctionKinds.Junction => Mix(_palSurface, ToneColor(Tone.Sand), 0.3f),
            _ => _palSurface,
        };
        canvas.StrokeSize = selected ? (float)(3 * Scale) : (float)(2 * Scale);
        canvas.StrokeColor = selected ? InkStrong : Mix(_palInk, _palSurface, 0.35f);
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

        canvas.FontColor = _palInk;
        canvas.FontSize = (float)Math.Max(8, 11 * Scale);
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        var name = t.Name;
        if (name.Length > 10) name = name[..10];
        canvas.DrawString(name, (float)(sp.X - r), (float)(sp.Y + r + 2), (float)(r * 2), 16,
            HorizontalAlignment.Center, VerticalAlignment.Top);
        // 性质写在名字下面一行（路口/环岛/收费站…），一眼能认出这是什么节点
        if (kind.Length > 0 && Scale >= 0.55)
        {
            canvas.FontColor = _palMuted;
            canvas.FontSize = (float)Math.Max(7, 9 * Scale);
            canvas.DrawString(JunctionKinds.Display(kind), (float)(sp.X - r), (float)(sp.Y + r + 15), (float)(r * 2), 12,
                HorizontalAlignment.Center, VerticalAlignment.Top);
        }
    }

    // ==================== 场景标记（地图上的「点」） ====================

    /// <summary>
    /// 场景画成一枚地图点：水滴形针 + 一枚名字牌，而不是一张卡片。
    /// 位置仍以 CardW×CardH 的矩形为准（拖拽与命中都吃这个框），针尖落在矩形中心。
    /// </summary>
    private void DrawPoi(ICanvas canvas, MapCanvasNode n)
    {
        var cx = (float)((n.X + CardW / 2) * Scale + OffsetX);
        var cy = (float)((n.Y + CardH / 2) * Scale + OffsetY);
        var group = PinTone(GroupColor(n.GroupKey));
        var selected = n.IsSelected || n == SelectedNode;
        var radius = (float)Math.Clamp(7.5 * Scale, 6, 12);

        // 这一层之外的房间淡成影子：还看得见它在哪，但不抢当前层的注意力
        if (Ghosted(n))
        {
            canvas.FillColor = group.WithAlpha(0.25f);
            canvas.FillCircle(cx, cy, radius * 0.8f);
            return;
        }

        // 针身：圆头往下收成尖
        var top = cy - radius * 1.9f;
        var path = new PathF();
        path.MoveTo(cx, cy + radius * 0.9f);
        path.CurveTo(cx - radius * 0.55f, cy + radius * 0.1f, cx - radius, cy - radius * 0.55f, cx - radius, top + radius);
        path.CurveTo(cx - radius, top + radius * 0.15f, cx - radius * 0.55f, top, cx, top);
        path.CurveTo(cx + radius * 0.55f, top, cx + radius, top + radius * 0.15f, cx + radius, top + radius);
        path.CurveTo(cx + radius, cy - radius * 0.55f, cx + radius * 0.55f, cy + radius * 0.1f, cx, cy + radius * 0.9f);
        path.Close();

        canvas.FillColor = group;
        canvas.FillPath(path);
        canvas.StrokeSize = selected ? 2.6f : 1.4f;
        canvas.StrokeColor = selected ? _palAccent : _palSurface;
        canvas.DrawPath(path);

        // 针心一点「孔」：针上有内容，不是一块纯色
        canvas.FillColor = _palSurface;
        canvas.FillCircle(cx, top + radius, radius * 0.36f);

        // 她此刻站着的这一枚再套一圈光
        if (n.IsCurrent)
        {
            canvas.StrokeSize = 2f;
            canvas.StrokeColor = _palAccent.WithAlpha(0.9f);
            canvas.DrawCircle(cx, top + radius, radius + 4f);
        }

        // 名字牌：贴在针脚下方，面 + 一道缝，深色世界里自然变成深底浅字
        var label = n.SceneName;
        if (label.Length > 10) label = label[..10] + "…";
        var font = (float)Math.Clamp(12 * Scale, 10, 14);
        var chipW = Math.Min((float)(CardW * Scale), font * (label.Length + 1) * 1.05f);
        var chipH = font + 8;
        var chip = new RectF(cx - chipW / 2, cy + radius * 1.2f, chipW, chipH);
        canvas.FillColor = _palSurface.WithAlpha(0.94f);
        canvas.FillRoundedRectangle(chip, chipH / 2);
        if (selected)
        {
            canvas.StrokeSize = 1.4f;
            canvas.StrokeColor = _palAccent;
            canvas.DrawRoundedRectangle(chip, chipH / 2);
        }
        canvas.FontColor = _palInk;
        canvas.FontSize = font;
        canvas.Font = selected ? Microsoft.Maui.Graphics.Font.DefaultBold : Microsoft.Maui.Graphics.Font.Default;
        canvas.DrawString(label, chip.X, chip.Y, chip.Width, chip.Height,
            HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    // ==================== 区域划分：顶点手柄 ====================

    private void DrawRegionHandles(ICanvas canvas, MapRegion r)
    {
        var pts = r.Points;
        if (pts.Count == 0) return;
        for (var i = 0; i < pts.Count; i++)
        {
            var sp = WorldToScreen(new Point(pts[i].X, pts[i].Y));
            canvas.FillColor = _palSurface;
            canvas.FillCircle((float)sp.X, (float)sp.Y, 5.5f);
            canvas.StrokeSize = 2f;
            canvas.StrokeColor = _palAccent;
            canvas.DrawCircle((float)sp.X, (float)sp.Y, 5.5f);
        }
        if (pts.Count < 2) return;
        var segments = RegionKinds.IsLinear(r.Kind) ? pts.Count - 1 : pts.Count;
        for (var i = 0; i < segments; i++)
        {
            var j = (i + 1) % pts.Count;
            var a = WorldToScreen(new Point(pts[i].X, pts[i].Y));
            var b = WorldToScreen(new Point(pts[j].X, pts[j].Y));
            var mx = (float)((a.X + b.X) / 2);
            var my = (float)((a.Y + b.Y) / 2);
            canvas.FillColor = _palAccent.WithAlpha(0.85f);
            canvas.FillRoundedRectangle(mx - 3.5f, my - 3.5f, 7, 7, 1.5f);
        }
    }

    /// <summary>命中某个顶点（屏幕坐标，-1 = 没点中）。</summary>
    public int HitRegionHandle(MapRegion r, Point screen, double tol = 11)
    {
        for (var i = 0; i < r.Points.Count; i++)
        {
            var sp = WorldToScreen(new Point(r.Points[i].X, r.Points[i].Y));
            if (Math.Abs(sp.X - screen.X) <= tol && Math.Abs(sp.Y - screen.Y) <= tol) return i;
        }
        return -1;
    }

    /// <summary>命中某条边的中点手柄（屏幕坐标）：返回这条边的起点索引，-1 = 没中。</summary>
    public int HitRegionEdge(MapRegion r, Point screen, double tol = 9)
    {
        var pts = r.Points;
        if (pts.Count < 2) return -1;
        var segments = RegionKinds.IsLinear(r.Kind) ? pts.Count - 1 : pts.Count;
        for (var i = 0; i < segments; i++)
        {
            var j = (i + 1) % pts.Count;
            var a = WorldToScreen(new Point(pts[i].X, pts[i].Y));
            var b = WorldToScreen(new Point(pts[j].X, pts[j].Y));
            if (Math.Abs((a.X + b.X) / 2 - screen.X) <= tol && Math.Abs((a.Y + b.Y) / 2 - screen.Y) <= tol)
                return i;
        }
        return -1;
    }

    private Point ScreenPt(Point w) => WorldToScreen(w);

    // 颜色解析缓存：地图一帧要给几百张卡片/路段解析同一批色字符串，Color.FromArgb 每次都走
    // 一遍字符串解析（#RRGGBB → 四个分量），纯属重复劳动。按 hex 记住结果；有界 256 条，
    // 撑满就整表丢掉重来——一张图上的色值种类远少于这个数，实际走不到清空那一步。
    private static readonly Dictionary<string, Color> _hexColors = new(StringComparer.Ordinal);
    private static readonly Color FallbackInk = Color.FromArgb("#2C1810");

    // 审计 P6 补记：ParseHex 原本就有 256 条上限的缓存，此处保持不变；
    // 真正的每帧分配来自 EnsureFrames 重建与 FrameFill/标题色，已在上方钉成字段解决。
    private static Color ParseHex(string hex, string fallback)
    {
        hex ??= "";
        if (_hexColors.TryGetValue(hex, out var cached)) return cached;
        Color color;
        try { color = Color.FromArgb(hex); }
        catch
        {
            try { color = Color.FromArgb(fallback); }
            catch { color = FallbackInk; }
        }
        if (_hexColors.Count >= 256) _hexColors.Clear();
        _hexColors[hex] = color;
        return color;
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
            DrawNoteCard(canvas, new Point(minX, minY - 48), loc.Note, _palSurface, _palMuted);
        }
    }

    private void DrawNodeNote(ICanvas canvas, MapCanvasNode n)
    {
        if (string.IsNullOrWhiteSpace(n.Scene.Note)) return;
        DrawNoteCard(canvas, new Point(n.X + CardW, n.Y + CardH / 2), n.Scene.Note, _palSurface, _palInk);
    }

    private void DrawTransitNote(ICanvas canvas, MapTransitNode t)
    {
        if (string.IsNullOrWhiteSpace(t.Transit.Note)) return;
        DrawNoteCard(canvas, new Point(t.X + 16, t.Y + 16), t.Transit.Note, _palSurface, _palInk);
    }

    private void DrawEdgeNotes(ICanvas canvas)
    {
        if (Edges is null) return;
        foreach (var e in Edges)
        {
            if (string.IsNullOrWhiteSpace(e.Note)) continue;
            var pts = SampleEdge(e);
            var mid = pts[pts.Count / 2];
            DrawNoteCard(canvas, mid, e.Note, _palSurface, _palMuted);
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
        canvas.StrokeColor = _palLine;
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
        canvas.FillColor = _palSurface;
        canvas.FillRoundedRectangle(rect, 5);
        canvas.StrokeSize = 1;
        canvas.StrokeColor = Gold;
        canvas.DrawRoundedRectangle(rect, 5);
        canvas.FontColor = _palInk;
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

    // ---- 菜单布局（每帧算一次，命中测试与滚动沿用同一份） ----
    private RectF _menuPanel;
    private RectF _menuContent;
    private float _menuContentH;
    private float _menuScroll;
    private string _menuSig = "";
    private readonly List<(RectF Rect, int Index)> _menuHitRects = new();
    private bool _menuDragging;
    private float _menuDragY;
    private float _viewWv, _viewHv;

    public bool MenuDragging => _menuDragging;

    private static string MenuGroupOf(string cmd) => cmd switch
    {
        "bg" or "bg-lib" or "bg-clear" or "cal" or "stand" => "背景",
        "ai-note" or "ai-anchor" or "ai-ratio" => "AI",
        "floor" or "unit" or "indoor" or "floors" or "basement" or "height" => "楼层与户型",
        _ => "本项",
    };

    /// <summary>
    /// 右键菜单：高度不超过屏幕可用高度，超出时内容区可滚，标题条钉住不动；
    /// 整块面板夹在窗口里（四边至少 8px），点靠下就向上弹、靠右就向左展开。
    /// </summary>
    private void DrawMenu(ICanvas canvas)
    {
        _menuHitRects.Clear();
        if (Menu is null) return;

        SyncPalette();
        var vw = _viewWv > 40 ? _viewWv : 1200;
        var vh = _viewHv > 40 ? _viewHv : 720;
        var itemH = (float)Math.Clamp(30 * Scale, 26, 36);
        var titleH = (float)Math.Clamp(24 * Scale, 22, 30);
        var w = (float)Math.Clamp(184 * Scale, 176, 236);
        var pad = 5f;

        var sig = Menu.TargetId + "|" + Menu.Title + "|" + Menu.Items.Count + "|" + vw.ToString("0") + vh.ToString("0");
        if (sig != _menuSig) { _menuSig = sig; _menuScroll = 0; }

        var seps = 0;
        for (var i = 1; i < Menu.Items.Count; i++)
            if (MenuGroupOf(Menu.Items[i].Cmd) != MenuGroupOf(Menu.Items[i - 1].Cmd)) seps++;
        const float sepH = 9f;
        _menuContentH = pad * 2 + Menu.Items.Count * itemH + seps * sepH;

        var anchor = ScreenPt(Menu.Pos);
        var below = vh - anchor.Y - 8;
        var above = anchor.Y - 8;
        var total = titleH + _menuContentH;
        var wantBelow = below >= total || below >= above;
        var avail = Math.Max(titleH + itemH * 2, wantBelow ? below : above);
        var panelH = (float)Math.Min(total, avail);
        panelH = Math.Max(panelH, titleH + itemH);          // 至少露出两项，别缩成一条

        var x = (float)Math.Clamp(anchor.X, 8f, Math.Max(8f, vw - w - 8f));
        var y = wantBelow ? (float)anchor.Y : (float)(anchor.Y - panelH);
        y = (float)Math.Clamp(y, 8f, Math.Max(8f, vh - panelH - 8f));
        _menuPanel = new RectF(x, y, w, panelH);
        _menuContent = new RectF(x, y + titleH, w, Math.Max(itemH, panelH - titleH));

        var maxScroll = Math.Max(0, _menuContentH - _menuContent.Height);
        _menuScroll = Math.Clamp(_menuScroll, 0, maxScroll);

        // 面板
        canvas.FillColor = _palSurface;
        canvas.FillRoundedRectangle(_menuPanel, 8);
        canvas.StrokeSize = 1;
        canvas.StrokeColor = _palLine;
        canvas.DrawRoundedRectangle(_menuPanel, 8);

        // 标题条：钉在面板顶上，不跟着滚
        if (!string.IsNullOrEmpty(Menu.Title))
        {
            canvas.FillColor = Mix(_palInk, _palAccent, 0.35f);
            canvas.FillRoundedRectangle(new RectF(x, y, w, titleH), 6);
            canvas.FillRectangle(new RectF(x, y + titleH / 2, w, titleH / 2));
            canvas.FontColor = _palLift;
            canvas.FontSize = (float)Math.Clamp(11 * Scale, 10, 13);
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.DrawString(Menu.Title, x + 8, y, w - 16, titleH,
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }

        // 内容区：滚动 + 裁剪
        canvas.SaveState();
        canvas.ClipRectangle(_menuContent);
        var yy = _menuContent.Y + pad - _menuScroll;
        canvas.FontColor = _palInk;
        canvas.FontSize = (float)Math.Clamp(13 * Scale, 11, 15);
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        for (var i = 0; i < Menu.Items.Count; i++)
        {
            if (i > 0 && MenuGroupOf(Menu.Items[i].Cmd) != MenuGroupOf(Menu.Items[i - 1].Cmd))
            {
                canvas.FillColor = _palLine.WithAlpha(0.5f);
                canvas.FillRectangle(_menuContent.X + 8, yy + 3, _menuContent.Width - 16, 1);
                yy += sepH;
            }
            var r = new RectF(_menuContent.X + pad, yy, _menuContent.Width - pad * 2, itemH);
            if (r.Bottom > _menuContent.Top && r.Y < _menuContent.Bottom)
            {
                canvas.FillColor = _palWell;
                canvas.FillRoundedRectangle(r, 4);
                canvas.DrawString(Menu.Items[i].Label, r.X + 10, r.Y, r.Width - 20, r.Height,
                    HorizontalAlignment.Left, VerticalAlignment.Center);

                var top = Math.Max(r.Y, _menuContent.Top);
                var bottom = Math.Min(r.Bottom, _menuContent.Bottom);
                if (bottom - top > 8)
                    _menuHitRects.Add((new RectF(r.X, top, r.Width, bottom - top), i));
            }
            yy += itemH;
        }
        canvas.RestoreState();

        // 可滚时右侧一道细条，提示「还没完」
        if (maxScroll > 0)
        {
            var trackH = _menuContent.Height - 8;
            var thumbH = Math.Max(18f, trackH * _menuContent.Height / _menuContentH);
            var thumbY = _menuContent.Y + 4 + (trackH - thumbH) * (_menuScroll / maxScroll);
            canvas.FillColor = _palMuted.WithAlpha(0.35f);
            canvas.FillRoundedRectangle(_menuContent.Right - 5, thumbY, 2.5f, thumbH, 1.2f);
        }
    }

    /// <summary>滚一格（wheelDelta &gt; 0 = 向上）。光标不在面板里就交还给缩放。</summary>
    public bool ScrollMenu(double wheelDelta, Point screen)
    {
        if (Menu is null) return false;
        if (!_menuPanel.Contains((float)screen.X, (float)screen.Y)) return false;
        ScrollMenuBy((float)(-Math.Sign(wheelDelta) * 44));
        return true;
    }

    private void ScrollMenuBy(float dy)
    {
        if (Menu is null) return;
        var maxScroll = Math.Max(0, _menuContentH - _menuContent.Height);
        _menuScroll = Math.Clamp(_menuScroll + dy, 0, maxScroll);
        NeedRedraw();
    }

    public void BeginMenuDrag(Point screen)
    {
        _menuDragging = true;
        _menuDragY = (float)screen.Y;
    }

    public void DragMenu(Point screen)
    {
        if (!_menuDragging) return;
        var dy = (float)screen.Y - _menuDragY;
        _menuDragY = (float)screen.Y;
        ScrollMenuBy(-dy);
    }

    public void EndMenuDrag() => _menuDragging = false;

    /// <summary>命中菜单项索引（屏幕坐标；-1=面板外，-3=面板内空白处，可拖动滚动）。</summary>
    public int HitMenu(Point screen)
    {
        if (Menu is null) return -1;
        foreach (var (rect, index) in _menuHitRects)
            if (rect.Contains((float)screen.X, (float)screen.Y)) return index;
        if (_menuPanel.Contains((float)screen.X, (float)screen.Y)) return -3;
        return -1;
    }

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

    /// <summary>
    /// 这个点落在哪块地上：面按射线法数内外，航道按「离这条线多近」。
    /// 几块地块套在一起（校区里的小公园）取最小的那块——跟地点框同一套规矩，光标点哪儿就是哪儿。
    /// </summary>
    public MapRegion? HitRegion(Point world, double tol = 8)
    {
        if (Regions is null) return null;
        MapRegion? best = null;
        var bestArea = double.MaxValue;
        foreach (var r in Regions)
        {
            if (!r.HasShape) continue;
            var hit = RegionKinds.IsLinear(r.Kind)
                ? NearRegionLine(r, world, tol)
                : InsideRing(r, world);
            if (!hit) continue;
            var area = RegionArea(r);
            if (area < bestArea) { bestArea = area; best = r; }
        }
        return best;
    }

    /// <summary>射线法：从这个点往右引一条线，数它穿过几道边——奇数在里、偶数在外。</summary>
    private static bool InsideRing(MapRegion r, Point p)
    {
        var pts = r.Points;
        var inside = false;
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            if ((a.Y > p.Y) == (b.Y > p.Y)) continue;
            if (p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    private static bool NearRegionLine(MapRegion r, Point p, double tol)
    {
        var pts = r.Points;
        for (var i = 0; i + 1 < pts.Count; i++)
            if (DistSqToSegment(pts[i], pts[i + 1], p) <= tol * tol) return true;
        return DistSqToSegment(pts[^1], pts[0], p) <= tol * tol;   // 航道首尾没接上时，把最后一段也认上
    }

    private static double DistSqToSegment(MapWaypoint a, MapWaypoint b, Point p)
    {
        var abx = b.X - a.X;
        var aby = b.Y - a.Y;
        var l2 = abx * abx + aby * aby;
        var t = l2 < 1e-9 ? 0 : Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / l2, 0, 1);
        var dx = a.X + abx * t - p.X;
        var dy = a.Y + aby * t - p.Y;
        return dx * dx + dy * dy;
    }

    /// <summary>鞋带公式的面积（绝对值）：地块重叠时用来挑出最小的那一块。</summary>
    private static double RegionArea(MapRegion r)
    {
        var pts = r.Points;
        double sum = 0;
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(sum) / 2;
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
            try
            {
                var bg = _resolveBg(n);
                if (string.IsNullOrEmpty(bg)) return;
                bytes = File.ReadAllBytes(bg);
            }
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

    // 换行结果缓存：备注卡每帧都要把同一段文字按同一宽度切一遍，长备注一次就是几十个 Substring。
    // 按「宽度 + 文本」记住切好的行；有界 512 条，撑满就整表丢掉重来（同一时刻屏上的备注远少于这个数）。
    // 返回的是缓存里那一份：调用方只读，别往里加/删。
    private static readonly Dictionary<string, List<string>> _wrapCache = new(StringComparer.Ordinal);

    public static List<string> Wrap(string text, int maxChars)
    {
        var key = maxChars + "\u0001" + (text ?? "");
        if (_wrapCache.TryGetValue(key, out var cached)) return cached;

        var lines = new List<string>();
        if (string.IsNullOrEmpty(text) || maxChars < 1)
        {
            lines.Add(text ?? "");
        }
        else
        {
            // 直接按段切片：从前一个字符一个字符往上攒，长备注能白分配几百个中间字符串
            var count = (text.Length + maxChars - 1) / maxChars;
            for (var i = 0; i < count; i++)
            {
                var start = i * maxChars;
                lines.Add(text.Substring(start, Math.Min(maxChars, text.Length - start)));
            }
        }
        if (_wrapCache.Count >= 512) _wrapCache.Clear();
        _wrapCache[key] = lines;
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