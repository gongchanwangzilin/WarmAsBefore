using System.Text.Json.Serialization;

namespace WarmAsBefore.Models;

/// <summary>
/// 地图定义：地点（建筑）→ 内部场景；场景之间以连线（MapEdge）构成通路，
/// AI 寻路时在场景图上求最短路径（多条等长路径随机选一条）。
/// 另支持：临时节点（Transit，仅作连线端点）、边的可视化路径（折点/曲直/长度/备注）。
/// </summary>
public sealed class MapDefinition
{
    public string Name { get; set; } = "温暖地图";
    /// <summary>初始所在场景（读档/启动时的出生点）。</summary>
    public string StartSceneId { get; set; } = "";

    public List<MapLocation> Locations { get; set; } = new();
    public List<MapEdge> Edges { get; set; } = new();
    /// <summary>临时节点：不归属任何地点，仅作为连线端点/路径中间站。</summary>
    public List<MapTransit> Transits { get; set; } = new();

    /// <summary>画布 1 单位 = 多少米。边长没手动量时按这个换算，导入真实地图后由比例尺定。</summary>
    public double MetersPerUnit { get; set; } = 0.5;
    /// <summary>这张图从哪来：manual 手工 / osm 开放街道图 / amap 高德 / ai 生成。决定能不能重新导入。</summary>
    public string Source { get; set; } = "manual";
    /// <summary>城市名（检索来的地图才有，用于标题与重新定位）。</summary>
    public string City { get; set; } = "";
    public double CenterLatitude { get; set; }
    public double CenterLongitude { get; set; }

    [JsonIgnore] public IEnumerable<MapScene> AllScenes => Locations.SelectMany(l => l.Scenes);
    [JsonIgnore] public IEnumerable<MapDoor> AllDoors => Locations.SelectMany(l => l.Doors);
    /// <summary>取景照片库：街道照绑「边 + 位置」，室内照绑「楼层 + 户型（+ 门）」；绑定校验见 <see cref="ValidatePhotoBinding"/>。</summary>
    public List<ScenePhoto> Photos { get; set; } = new();

    public MapScene? SceneById(string id) =>
        AllScenes.FirstOrDefault(s => s.Id == id);

    public MapTransit? TransitById(string id) =>
        Transits.FirstOrDefault(t => t.Id == id);

    /// <summary>任意顶点（场景或临时节点的名字，用于连线端点的名称解析）。</summary>
    public string? PointNameById(string id) =>
        SceneById(id)?.Name ?? TransitById(id)?.Name;

    [JsonIgnore]
    public IEnumerable<(string Id, string Name, bool IsScene)> AllPoints
    {
        get
        {
            foreach (var s in AllScenes) yield return (s.Id, s.Name, true);
            foreach (var t in Transits) yield return (t.Id, t.Name, false);
        }
    }

    /// <summary>按名称查找场景：支持完整名或包含匹配（AI 请求时的容错）。</summary>
    public MapScene? SceneByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();
        return AllScenes.FirstOrDefault(s => s.Name == name)
            ?? AllScenes.FirstOrDefault(s => s.Name.Contains(name, StringComparison.Ordinal))
            ?? AllScenes.FirstOrDefault(s => name.Contains(s.Name, StringComparison.Ordinal));
    }

    public string LocationNameOf(string sceneId) =>
        Locations.FirstOrDefault(l => l.Scenes.Any(s => s.Id == sceneId))?.Name ?? "";

    public bool SceneExists(string id) => AllScenes.Any(s => s.Id == id);

    /// <summary>相邻顶点（按边展开；顶点可以是场景或临时节点）。</summary>
    public IEnumerable<(string Id, string Name, bool IsScene)> NeighborsOfPoint(string pointId)
    {
        foreach (var e in Edges)
        {
            if (e.A == pointId && PointNameById(e.B) is { } nb) yield return ResolvePoint(e.B);
            else if (e.B == pointId && PointNameById(e.A) is { } na) yield return ResolvePoint(e.A);
        }
    }

    private (string Id, string Name, bool IsScene) ResolvePoint(string id) =>
        SceneById(id) is { } s ? (s.Id, s.Name, true) : (id, TransitById(id)?.Name ?? "", false);

    /// <summary>相邻场景（按边展开，忽略临时节点端点）。</summary>
    public IEnumerable<MapScene> Neighbors(MapScene scene) =>
        NeighborsOfPoint(scene.Id)
            .Where(n => n.IsScene && SceneById(n.Id) is { } sc)
            .Select(n => SceneById(n.Id)!);

    public MapLocation? LocationById(string id) =>
        Locations.FirstOrDefault(l => l.Id == id);

    public MapEdge? EdgeById(string id) => Edges.FirstOrDefault(e => e.Id == id);

    public MapDoor? DoorById(string id) => AllDoors.FirstOrDefault(d => d.Id == id);

    public ScenePhoto? PhotoById(string id) => Photos.FirstOrDefault(p => p.Id == id);

    /// <summary>连到某一户型的门（进门即到这个场景；不算只作标记的门）。</summary>
    public IEnumerable<MapDoor> DoorsOpeningTo(string sceneId) =>
        AllDoors.Where(d => d.OpensSceneId == sceneId);

    /// <summary>
    /// 取景绑定空间校验：street 必须挂在真实边上；indoor 必须有楼层，且楼层/户型必须与目标场景
    /// 一致——街道照落地面光线，室内照不许跨层错绑。返回给用户看的错误文案，null = 通过。
    /// </summary>
    public string? ValidatePhotoBinding(ScenePhoto p)
    {
        if (p is null) return "照片不存在。";
        if (string.IsNullOrWhiteSpace(p.RelPath)) return "照片没有文件。";
        if (!p.IsIndoor)
            return EdgeById(p.StreetId) is null ? "街道照要绑在这张图的一条街道（连线）上。" : null;

        if (p.Floor is null) return "室内照必须先选楼层。";
        var scene = SceneById(p.TargetSceneId);
        if (scene is null) return "室内照要绑在这张图里已有的户型（场景）上。";
        if (scene.Floor != p.Floor) return "不允许跨层错绑：所选楼层与目标场景所在层不一致。";
        if (!string.Equals(scene.Unit, p.Unit, StringComparison.Ordinal))
            return "不允许跨层/跨户错绑：户型与目标场景对不上。";
        return null;
    }

    /// <summary>地点 Id：从场景 Id 反查它挂在哪个地点下（找不到返回 ""）。</summary>
    public string LocationIdOfScene(string sceneId) =>
        Locations.FirstOrDefault(l => l.Scenes.Any(s => s.Id == sceneId))?.Id ?? "";

    /// <summary>这个地点有没有分层（地上或地下任意一侧超过一层，或场景带了楼层号）。</summary>
    public bool HasFloors(string locationId)
    {
        var loc = LocationById(locationId);
        if (loc is null) return false;
        return loc.Floors > 1 || loc.BasementFloors > 0 || loc.Scenes.Any(s => s.Floor is not null);
    }

    /// <summary>一个地点里实际存在的楼层（从最高层往下排，含地下）。没分层就返回单个 null 档。</summary>
    public List<int?> FloorsOf(string locationId)
    {
        var loc = LocationById(locationId);
        if (loc is null) return new List<int?> { null };
        var set = new SortedSet<int>(loc.Scenes.Where(s => s.Floor is not null).Select(s => s.Floor!.Value));
        if (set.Count == 0)
        {
            if (loc.Floors <= 1 && loc.BasementFloors == 0) return new List<int?> { null };
            for (var f = loc.Floors; f >= 1; f--) set.Add(f);
            for (var b = 1; b <= loc.BasementFloors; b++) set.Add(-b);
        }
        return set.OrderByDescending(f => f).Select(f => (int?)f).ToList();
    }

    /// <summary>某一层上的场景（户型）。floor = null 取所有没标楼层的，即不分层的那种画法。</summary>
    public IEnumerable<MapScene> ScenesOnFloor(string locationId, int? floor) =>
        LocationById(locationId)?.Scenes.Where(s => s.Floor == floor) ?? Enumerable.Empty<MapScene>();

    /// <summary>楼层显示名：1 → 1 层，0 → 地面层，-1 → 地下一层。</summary>
    public static string FloorLabel(int? floor) => floor switch
    {
        null => "不分层",
        0 => "地面层",
        > 0 => $"{floor} 层",
        _ => $"地下{-floor} 层",
    };
}

public sealed class MapLocation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    /// <summary>父地点 Id；"" 表示顶层地点。用于"一个地点套一堆子地点"的嵌套分组。</summary>
    public string ParentId { get; set; } = "";
    /// <summary>地点备注（地点组的备注卡）。</summary>
    public string Note { get; set; } = "";
    /// <summary>空地点锚点（无任何场景时画出最小框的位置）；-1 表示未摆放。</summary>
    public double X { get; set; } = -1;
    public double Y { get; set; } = -1;
    public List<MapScene> Scenes { get; set; } = new();

    /// <summary>这栋建筑的门：各门可连不同户型；门在街面上的位置见 <see cref="MapDoor.StreetId"/>。</summary>
    public List<MapDoor> Doors { get; set; } = new();
    /// <summary>楼层高度（米/层）：楼层与高度 H 的换算，3D 视图按它抬层。0 = 用全图默认。</summary>
    public double HeightPerFloor { get; set; } = 3.0;

    /// <summary>地上楼层数（0 = 不分层，按从前那样当一个整体看）。&gt;1 就能按层切换着看。</summary>
    public int Floors { get; set; }
    /// <summary>地下层数（0 = 没有）。</summary>
    public int BasementFloors { get; set; }
    /// <summary>门牌地址（真实城市导入后才有；手工地点留空）。</summary>
    public string Address { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    /// <summary>建筑外轮廓（画布世界坐标）；空 = 没有形状，按卡片框画。</summary>
    public List<MapWaypoint> Outline { get; set; } = new();
    /// <summary>外部数据源编号（OSM relation/way 之类），重新导入时拿来对齐，不展示给用户。</summary>
    public string SourceId { get; set; } = "";
    /// <summary>真实店名/机构名（导进来才有；重命名不覆盖它，方便对照原图）。</summary>
    public string RealName { get; set; } = "";

    /// <summary>标题后缀：这栋楼有几层（不分层就空着，别在名字后面挂一堆用不上的字）。</summary>
    [JsonIgnore]
    public string FloorBadge
    {
        get
        {
            if (Floors > 1 && BasementFloors > 0) return $" · {Floors} 层/地下 {BasementFloors} 层";
            if (Floors > 1) return $" · {Floors} 层";
            if (BasementFloors > 0) return $" · 地下 {BasementFloors} 层";
            var tagged = Scenes.Count(s => s.Floor is not null);
            return tagged > 1 ? $" · {tagged} 层" : "";
        }
    }
}

public sealed class MapScene
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    /// <summary>背景图：优先存绝对路径，缺失则退用 BackgroundColor。</summary>
    public string Background { get; set; } = "";
    public string BackgroundColor { get; set; } = "#2C1810";
    /// <summary>场景备注（显示在卡片下方的备注卡）。</summary>
    public string Note { get; set; } = "";
    /// <summary>画布坐标（地图编辑器里卡片的位置）；-1 表示尚未摆放，加载时自动网格布局。</summary>
    public double X { get; set; } = -1;
    public double Y { get; set; } = -1;

    /// <summary>
    /// 背景图的标定数据（落脚点 + 缩放依据）。为 null 或校验不通过 = 未标定，
    /// 此时不允许生成角色位置（切到该场景会强制进入标定流程）。
    /// 注意 X/Y 是编辑器画布坐标，与标定无关，不要混用。
    /// </summary>
    public ScenePlacement? Placement { get; set; }

    /// <summary>来源素材包名（空 = 手工创建）。用于素材包重复导入时判断「覆盖还是另存」。</summary>
    public string SourcePack { get; set; } = "";

    /// <summary>所在楼层（1 = 地面层，0 及以下 = 地下）；null = 这个地点不分层。</summary>
    public int? Floor { get; set; }
    /// <summary>户型名（同一层里的第几户 / 哪一间）；"" = 不分户。</summary>
    public string Unit { get; set; } = "";
    /// <summary>这个场景是「室外的路/街区面」还是「室内」——决定多层切换时它归哪一面。</summary>
    public bool Indoor { get; set; }
}

/// <summary>临时节点：不归属任何地点，作为连线端点/路径站。</summary>
public sealed class MapTransit
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "临时节点";
    public string Note { get; set; } = "";
    public double X { get; set; } = -1;
    public double Y { get; set; } = -1;
    /// <summary>路口性质，取值见 <see cref="JunctionKinds"/>；"" = 普通端点。</summary>
    public string Junction { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string SourceId { get; set; } = "";
}

/// <summary>连线的可视化路径点（折线用）或曲线控制点。</summary>
public sealed class MapWaypoint
{
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>
/// 道路类别：一条边到底是一段什么路。写进 <see cref="MapEdge.Road"/>。
/// 老存档没这个字段，反序列化后取默认值 street，观感与从前一致。
/// </summary>
public static class RoadKinds
{
    public const string Highway = "highway";     // 高速公路：有收费站，一般不让角色步行上
    public const string Trunk = "trunk";         // 快速路 / 城市主干道
    public const string Street = "street";       // 普通街道
    public const string Lane = "lane";           // 小巷 / 胡同
    public const string Walkway = "walkway";     // 人行道 · 步道
    public const string Steps = "steps";         // 楼梯：楼层之间的垂直通路
    public const string Bridge = "bridge";       // 桥
    public const string Tunnel = "tunnel";       // 隧道
    public const string Waterway = "waterway";   // 水路 / 渡轮
    public const string Interior = "interior";   // 户型内：客厅到卧室，不谈距离，直接到达

    /// <summary>角色靠腿能走的类别（高速与水路另说）。</summary>
    public static bool Walkable(string? kind) => kind switch
    {
        Highway or Waterway => false,
        _ => true,
    };

    /// <summary>按类别给一个默认限速（km/h）。户型内 = 0，表示不计时。</summary>
    public static double DefaultSpeedKmh(string? kind) => kind switch
    {
        Highway => 100,
        Trunk => 60,
        Street => 40,
        Lane => 30,
        Bridge or Tunnel => 40,
        Walkway => 5,
        Steps => 3,
        Waterway => 12,
        Interior => 0,
        _ => 40,
    };

    public static string Display(string? kind) => kind switch
    {
        Highway => "高速",
        Trunk => "快速路",
        Street => "街道",
        Lane => "小巷",
        Walkway => "人行道",
        Steps => "楼梯",
        Bridge => "桥",
        Tunnel => "隧道",
        Waterway => "水路",
        Interior => "户型内",
        _ => "街道",
    };
}

/// <summary>路口性质，写进 <see cref="MapTransit.Junction"/>。</summary>
public static class JunctionKinds
{
    public const string Crossing = "crossing";   // 十字路口（四岔以上，可挂红绿灯）
    public const string Junction = "junction";   // 普通路口
    public const string Roundabout = "roundabout"; // 环岛
    public const string Toll = "toll";           // 收费站
    public const string Stop = "stop";           // 站点 / 停靠点
    public const string Poi = "poi";             // 兴趣点（店门、信箱这种不算路的地方）

    public static string Display(string? kind) => kind switch
    {
        Crossing => "十字路口",
        Junction => "路口",
        Roundabout => "环岛",
        Toll => "收费站",
        Stop => "站点",
        Poi => "地点",
        _ => "端点",
    };
}

public sealed class MapEdge
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>端点 A/B（场景 Id 或临时节点 Id）。</summary>
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    /// <summary>线型："line" 折线（waypoint 直角直连）；"curve" 贝塞尔曲线（waypoint 为控制点）。</summary>
    public string Kind { get; set; } = "line";
    /// <summary>路径点（画布世界坐标；空 = 端点直连）。</summary>
    public List<MapWaypoint> Waypoints { get; set; } = new();
    /// <summary>-1 = 自动按几何长度计算；0 = 未丈量（画虚线）；&gt;0 = 手动指定长度（米）。</summary>
    public double Length { get; set; } = -1;
    /// <summary>连线备注（显示在连线中段的备注卡）。</summary>
    public string Note { get; set; } = "";
    /// <summary>true = 只显示整条总长一个标记（涂鸦一次性生成，不做逐段拆分）。</summary>
    public bool TotalLabel { get; set; }

    /// <summary>这段是什么路，取值见 <see cref="RoadKinds"/>。</summary>
    public string Road { get; set; } = RoadKinds.Street;
    /// <summary>道路名（长江路）。可以直接重命名，不必跟着端点走。</summary>
    public string RoadName { get; set; } = "";
    /// <summary>限速（km/h）；0 = 按类别默认。走路耗时按「长度 ÷ 限速」算，不再按段数。</summary>
    public double SpeedKmh { get; set; }
    /// <summary>单行：只允许 A→B，反向寻路不通。</summary>
    public bool OneWay { get; set; }
    /// <summary>路口有红绿灯：过马路多等一趟（寻路按时间加权时罚一段）。</summary>
    public bool TrafficLight { get; set; }
    /// <summary>这段要过收费站。</summary>
    public bool Toll { get; set; }
    public double TollFee { get; set; }
    /// <summary>外部数据源编号（OSM way id），重新导入时对齐用。</summary>
    public string SourceId { get; set; } = "";

    /// <summary>这条路的实际限速（没手填就按类别给）。</summary>
    public double EffectiveSpeedKmh => SpeedKmh > 0 ? SpeedKmh : RoadKinds.DefaultSpeedKmh(Road);
}

/// <summary>取景照片的两类绑定：街道照（河边贴边）与室内照（楼层+户型）。</summary>
public static class PhotoBindings
{
    public const string Street = "street";
    public const string Indoor = "indoor";
}

/// <summary>
/// 一张取景照片。「一个地点拍一张；街道不同位置可各拍一张」——街道照片落在
/// 某条边（<see cref="MapEdge"/>) 的弧长位置上，室内照片落在某楼层的某户型（场景）上，
/// 门/特殊地点另记一档。拍摄顺序随会话递增，电脑端导入时原样保留。
/// </summary>
public sealed class ScenePhoto
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>相对存储根的路径（maps/photos/{id}.jpg）；解析用 MapService.ResolvePhotoPath。</summary>
    public string RelPath { get; set; } = "";
    /// <summary>street / indoor，取值见 <see cref="PhotoBindings"/>。</summary>
    public string Binding { get; set; } = PhotoBindings.Street;
    /// <summary>街道照：所属边 Id 与边上弧长比例 0..1。同一街道的不同位置各算一张。</summary>
    public string StreetId { get; set; } = "";
    public double StreetT { get; set; }
    /// <summary>室内照：楼层与户型（必须与目标场景一致——不允许跨层错绑，校验在 MapDefinition）。</summary>
    public int? Floor { get; set; }
    public string Unit { get; set; } = "";
    /// <summary>室内照拍摄的那个户型场景（楼层+户型的具体落点）。</summary>
    public string TargetSceneId { get; set; } = "";
    /// <summary>门/特殊地点（MapDoor.Id）；室内照可空 = 拍的是户型本身。</summary>
    public string DoorRef { get; set; } = "";
    /// <summary>拍摄顺序（会话内递增；导入保留原序）。</summary>
    public int Order { get; set; }
    /// <summary>mobile = 手机端随拍；manual = 电脑端导入。</summary>
    public string Source { get; set; } = "mobile";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string Note { get; set; } = "";

    [JsonIgnore] public bool IsIndoor => string.Equals(Binding, PhotoBindings.Indoor, StringComparison.Ordinal);
}

/// <summary>
/// 建筑的门：一栋楼可以有多个门，各门可连不同户型。门在「街面」上的位置也是
/// 一条边 + 弧长比例（跟街道照同级，将来 3D/小地图都从这两个值起）。
/// </summary>
public sealed class MapDoor
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    /// <summary>门在街面上的位置：所属边 Id + 弧长比例 0..1。</summary>
    public string StreetId { get; set; } = "";
    public double StreetT { get; set; }
    /// <summary>进门后默认到达的户型场景；"" = 只是个标记门。</summary>
    public string OpensSceneId { get; set; } = "";
    public string Note { get; set; } = "";
}
