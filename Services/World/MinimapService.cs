using Microsoft.Maui.Graphics;
using WarmAsBefore.Drawables;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>小地图这一帧该画出什么：<see cref="Indoor"/> 本楼本层户型、<see cref="Outdoor"/> 周边路面局部、<see cref="List"/> 只有一份 POI 列表。</summary>
public enum MinimapMode { Indoor, Outdoor, List }

/// <summary>
/// 城/乡显示分档：不是给路换个名字，而是决定「这一带该给人看什么」——
/// 城里楼密，就把楼基涂实、名字收进当前位置；乡野路稀，就把巷与水路放开、把字留给远处。
/// </summary>
public enum MinimapTier { City, Country }

/// <summary>小地图上的一个可前往点：距离走路网（与 AI 语境同一口径），坐标只用来画点。</summary>
public sealed record MinimapPoi(
    string SceneId,
    string Name,
    string DistanceText,
    double SortMeters,
    bool HasPoint,
    double X,
    double Y,
    bool IsNearby);

/// <summary>室内档的一间房：画布卡片框就是它在楼里的占位（本图没有真正的房间轮廓）。</summary>
public sealed record MinimapRoom(string Name, RectF BoxMeters, bool IsCurrent);

/// <summary>室外档的一块地（绿地/水面/航道/广场/分区）：顶点已换成米，画在路网底下当底色。</summary>
public sealed record MinimapField(string Kind, List<Point> Points);

/// <summary>一次场景切换算一份，交给 <see cref="MinimapDrawable"/> 画、交给面板列成条目。</summary>
public sealed class MinimapPlan
{
    public MinimapMode Mode { get; set; }
    public MinimapTier Tier { get; set; } = MinimapTier.City;

    /// <summary>视点中心与可见半径（世界米）：屏幕短边的一半对应 RangeM。</summary>
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double RangeM { get; set; } = 300;

    /// <summary>整张图的几何快照（米）：路与楼都在里面，画的时候按半径裁。</summary>
    public Minimal3DScene? Geo { get; set; }

    /// <summary>这一圈内的地块底色（米）：公园、湖面、航道，铺在路底下。</summary>
    public List<MinimapField> Fields { get; } = new();

    /// <summary>室内档：本楼外轮廓（米，可空）与本层各房间。</summary>
    public List<Point>? Outline { get; set; }
    public List<MinimapRoom> Rooms { get; } = new();

    public List<MinimapPoi> Pois { get; } = new();

    public string Headline { get; set; } = "";
    public string Subline { get; set; } = "";

    /// <summary>没铺背景图也没标定：列表之外什么也画不出来，面板就只出列表。</summary>
    public bool HasCanvas => Mode != MinimapMode.List;

    /// <summary>能画的 POI（在半径内的）；列表条目不受半径限制。</summary>
    public IEnumerable<MinimapPoi> Dots => Pois.Where(p => p.IsNearby && p.HasPoint);
}

/// <summary>
/// 小地图取材：把「我此刻站在图上的哪一格」翻译成一份能画、能列的快照。
///
/// 为什么不重造几何：3D 灰度视图已经把 MapDefinition 压成了一份「米」制快照
/// （采样、抽稀、楼基三种口径都在 <see cref="Minimal3DBuilder"/> 里定过一次）。
/// 小地图要的正是同一份东西，只是换个镜头看——再抄一遍折点采样，两边迟早会漂。
/// 那份快照按整张图算、只在图变了时重算（<see cref="MapService.GeometryInvalidated"/>），
/// 换场景只重做「裁哪一圈 + 谁在我附近」这两件便宜事。
/// </summary>
public sealed class MinimapService
{
    private readonly MapService _maps;
    private Minimal3DScene? _geo;
    private MapDefinition? _geoFrom;

    /// <summary>室外可见半径的档位（米）：从小到大试，够看清一圈路就停。</summary>
    private static readonly double[] RangeTiers = { 300, 700, 1600, 3200 };

    public MinimapService(MapService maps)
    {
        _maps = maps;
        _maps.GeometryInvalidated += Invalidate;
    }

    /// <summary>图变了（编辑器落盘、导入、换档）：整图几何快照作废，下次取材重建。</summary>
    public void Invalidate()
    {
        _geo = null;
        _geoFrom = null;
    }

    /// <summary>整张图的几何快照（3D 页也用它同一口径）；换了一张 MapDefinition 就自动重来。</summary>
    public Minimal3DScene Geometry()
    {
        var map = _maps.Map;
        if (_geo is { } cached && ReferenceEquals(_geoFrom, map)) return cached;
        var built = Minimal3DBuilder.Build(map);
        _geo = built;
        _geoFrom = map;
        return built;
    }

    /// <summary>当前场景的小地图取材；场景 Id 无效返回 null（面板什么都不画）。</summary>
    public MinimapPlan? BuildFor(string sceneId)
    {
        var map = _maps.Map;
        var scene = map.SceneById(sceneId);
        if (scene is null) return null;

        var mpu = map.MetersPerUnit > 0 ? map.MetersPerUnit : 0.5;
        var loc = map.LocationById(map.LocationIdOfScene(scene.Id));
        var plan = new MinimapPlan
        {
            Headline = loc is null || string.IsNullOrEmpty(loc.Name)
                ? scene.Name
                : $"{loc.Name} · {scene.Name}",
        };

        // 卡片中心当落脚点：与 Minimal3DBuilder 的端点约定同一格，别一处按左上、一处按中心。
        var hasPoint = scene.X >= 0 && scene.Y >= 0;
        plan.CenterX = hasPoint ? (scene.X + MapCanvasDrawable.CardW / 2) * mpu : 0;
        plan.CenterY = hasPoint ? (scene.Y + MapCanvasDrawable.CardH / 2) * mpu : 0;

        FillPois(plan, map, scene);

        if (scene.Indoor && loc is not null && TryFillIndoor(plan, map, loc, scene, mpu))
        {
            plan.Mode = MinimapMode.Indoor;
            plan.Subline = $"{MapDefinition.FloorLabel(scene.Floor)} · 本层 {plan.Rooms.Count} 间";
            return plan;
        }

        var geo = Geometry();
        plan.Geo = geo;
        plan.RangeM = PickRange(geo, plan);
        MarkNearby(plan, plan.Pois);
        plan.Tier = ClassifyTier(geo, plan, plan.RangeM);
        plan.Subline = $"周边 {RangeText(plan.RangeM)} · 可去 {plan.Pois.Count} 处";

        // 一条路都裁不出来 = 这张图还只是几张卡片（手工新建的存档就是这样）：
        // 那就别画一张空纸，退成「名字 + 距离」的列表档。
        // 地表先裁一圈：站在公园边上时，哪怕四周还没楼，这块绿也够说明「我在哪儿」。
        FillFields(plan, map, mpu);
        var drawable = plan.Fields.Count > 0
            || geo.Roads.Any(r => InRange(r.Box, plan.CenterX, plan.CenterY, plan.RangeM))
            || geo.Buildings.Any(b => InRange(b.Box, plan.CenterX, plan.CenterY, plan.RangeM));
        plan.Mode = drawable && hasPoint ? MinimapMode.Outdoor : MinimapMode.List;
        if (plan.Mode == MinimapMode.List) plan.Subline = $"可去 {plan.Pois.Count} 处";
        return plan;
    }

    /// <summary>
    /// 这一圈里的地块：顶点从「图单位」换成「米」（跟 3D 快照同一单位），包围盒不在可见半径里的不收——
    /// 方片只有 60 像素，背着一整座城的公园画图没意义。
    /// </summary>
    private static void FillFields(MinimapPlan plan, MapDefinition map, double mpu)
    {
        foreach (var r in map.Regions)
        {
            if (!r.HasShape) continue;
            var pts = new List<Point>(r.Points.Count);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in r.Points)
            {
                var x = p.X * mpu;
                var y = p.Y * mpu;
                pts.Add(new Point(x, y));
                if (x < minX) minX = (float)x;
                if (y < minY) minY = (float)y;
                if (x > maxX) maxX = (float)x;
                if (y > maxY) maxY = (float)y;
            }
            if (!InRange(new RectF(minX, minY, maxX - minX, maxY - minY), plan.CenterX, plan.CenterY, plan.RangeM)) continue;
            plan.Fields.Add(new MinimapField(r.Kind, pts));
        }
    }

    // ==================== 室内 ====================

    /// <summary>本层户型简图：外轮廓（没有就按房间框撑出一个楼壳）+ 同层各房间 + 我在哪间。</summary>
    private static bool TryFillIndoor(MinimapPlan plan, MapDefinition map, MapLocation loc, MapScene scene, double mpu)
    {
        var rooms = map.ScenesOnFloor(loc.Id, scene.Floor)
            .Where(s => s.X >= 0 && s.Y >= 0)
            .Select(s => new MinimapRoom(
                s.Name,
                new RectF(
                    (float)(s.X * mpu),
                    (float)(s.Y * mpu),
                    (float)(MapCanvasDrawable.CardW * mpu),
                    (float)(MapCanvasDrawable.CardH * mpu)),
                s.Id == scene.Id))
            .ToList();
        if (rooms.Count == 0) return false;

        foreach (var r in rooms) plan.Rooms.Add(r);

        if (loc.Outline.Count >= 3)
            plan.Outline = loc.Outline.Select(o => new Point(o.X * mpu, o.Y * mpu)).ToList();

        // 取景框由房间包围盒撑开（外轮廓更大时按轮廓），留一成边距，别让墙贴着面板糊掉。
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        void Add(double x, double y)
        {
            minX = Math.Min(minX, x); minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
        }
        foreach (var r in rooms)
        {
            Add(r.BoxMeters.Left, r.BoxMeters.Top);
            Add(r.BoxMeters.Right, r.BoxMeters.Bottom);
        }
        if (plan.Outline is { } outline)
            foreach (var p in outline) Add(p.X, p.Y);

        var padX = Math.Max(4, (maxX - minX) * 0.14);
        var padY = Math.Max(4, (maxY - minY) * 0.14);
        plan.CenterX = (minX + maxX) / 2;
        plan.CenterY = (minY + maxY) / 2;
        // 半径取「长边的一半」：画的时候按屏幕短边换算，横长竖短的楼就顺着短边留出上下空白。
        plan.RangeM = Math.Max((maxX - minX) / 2 + padX, (maxY - minY) / 2 + padY);
        plan.RangeM = Math.Max(plan.RangeM, 10);
        plan.Tier = MinimapTier.City;
        return true;
    }

    // ==================== 附近有什么 ====================

    /// <summary>
    /// POI 用路网里程排序（和喂给 AI 的那句「距离」同一个口径），
    /// 图上坐标只用来决定这个点画不画得下——推门就到的小路没有里程，就按「同楼」排在最前。
    /// </summary>
    private void FillPois(MinimapPlan plan, MapDefinition map, MapScene scene)
    {
        var reach = _maps.ReachFrom(scene.Id);
        var mpu = map.MetersPerUnit > 0 ? map.MetersPerUnit : 0.5;
        var entries = new List<MinimapPoi>();
        foreach (var other in map.AllScenes)
        {
            if (other.Id == scene.Id || !reach.CanReach(other.Id)) continue;
            var meters = reach.MetersTo(other.Id);
            var locName = map.LocationNameOf(other.Id);
            var name = string.IsNullOrEmpty(locName) ? other.Name : $"{locName}·{other.Name}";
            var text = meters is double m
                ? (m >= 1000 ? $"{m / 1000:0.#} 公里" : $"{(int)Math.Round(m)} 米")
                : "推门就到";
            var hasPoint = other.X >= 0 && other.Y >= 0;
            double x = 0, y = 0;
            if (hasPoint)
            {
                x = (other.X + MapCanvasDrawable.CardW / 2) * mpu;
                y = (other.Y + MapCanvasDrawable.CardH / 2) * mpu;
            }
            // 排序键：有里程按里程；推门就到的那档排到最前（同楼的距离不是「零」，是「隔壁」）。
            entries.Add(new MinimapPoi(other.Id, name, text, meters ?? -1, hasPoint, x, y, false));
        }
        // 「推门就到」的（户型内小路没有里程）先排：那不是零距离，是隔壁；有里程的按里程由近及远。
        var ordered = entries
            .OrderBy(e => e.SortMeters < 0 ? 0 : 1)
            .ThenBy(e => e.SortMeters < 0 ? 0 : e.SortMeters)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
        foreach (var e in ordered) plan.Pois.Add(e);
    }

    /// <summary>档位定了之后回填「画不画得下」：POI 取材早于半径选择，所以只能事后钉一次。</summary>
    private static void MarkNearby(MinimapPlan plan, List<MinimapPoi> pois)
    {
        for (var i = 0; i < pois.Count; i++)
        {
            var p = pois[i];
            var nearby = p.HasPoint && Within(p.X, p.Y, plan.CenterX, plan.CenterY, plan.RangeM);
            pois[i] = p with { IsNearby = nearby };
        }
    }

    // ==================== 半径与城乡 ====================

    private static double PickRange(Minimal3DScene geo, MinimapPlan plan)
    {
        foreach (var r in RangeTiers)
        {
            var roads = geo.Roads.Count(x => InRange(x.Box, plan.CenterX, plan.CenterY, r));
            var pois = plan.Pois.Count(p => p.HasPoint && Within(p.X, p.Y, plan.CenterX, plan.CenterY, r));
            // 一圈里既有路可看、又有几处可去，就够「像张地图」了；再拉远只会把字挤没。
            if (roads >= 10 || pois >= 4) return r;
        }
        return RangeTiers[^1];
    }

    /// <summary>按「这一圈路上的城市成分」定档：宽路多算城，巷与水路多算乡。</summary>
    private static MinimapTier ClassifyTier(Minimal3DScene geo, MinimapPlan plan, double range)
    {
        double urban = 0, rural = 0;
        foreach (var r in geo.Roads)
        {
            if (!InRange(r.Box, plan.CenterX, plan.CenterY, range)) continue;
            var w = r.WidthM;
            switch (r.Kind)
            {
                case RoadKinds.Highway:
                case RoadKinds.Trunk:
                case RoadKinds.Street:
                case RoadKinds.Bridge:
                case RoadKinds.Tunnel:
                    urban += w;
                    break;
                default:
                    rural += w;
                    break;
            }
        }
        var total = urban + rural;
        if (total <= 0) return MinimapTier.City;
        return urban / total >= 0.5 ? MinimapTier.City : MinimapTier.Country;
    }

    private static bool InRange(RectF box, double cx, double cy, double range) =>
        Within(box.Left, box.Top, cx, cy, range) || Within(box.Right, box.Bottom, cx, cy, range)
        || Within(box.Left, box.Bottom, cx, cy, range) || Within(box.Right, box.Top, cx, cy, range)
        || Within((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2, cx, cy, range);

    private static bool Within(double x, double y, double cx, double cy, double range)
    {
        var dx = x - cx;
        var dy = y - cy;
        return dx * dx + dy * dy <= range * range;
    }

    private static string RangeText(double meters) =>
        meters >= 1000 ? $"{meters / 1000:0.#} 公里" : $"{(int)meters} 米";
}
