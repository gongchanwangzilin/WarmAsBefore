using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 一次地名 / 经纬度检索的一条命中：名字、行政区划、经纬度、来源，外加可选的边界框（城市 / 区县这种成片的结果才有）。
/// </summary>
public sealed class MapSearchHit
{
    public string Name { get; init; } = "";
    /// <summary>行政区划全名（浙江省杭州市上城区…）。</summary>
    public string AdminPath { get; init; } = "";
    /// <summary>这条是什么：城市 / 区县 / 街道 / 门址 / 点。</summary>
    public string Kind { get; init; } = "";
    public string PlaceId { get; init; } = "";
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    /// <summary>amap 高德 / osm 开放街道图 / coord 用户直接输入的经纬度。</summary>
    public string Source { get; init; } = "osm";
    public double South { get; init; }
    public double West { get; init; }
    public double North { get; init; }
    public double East { get; init; }

    public bool HasBoundingBox => North > South && East > West && North - South > 1e-7 && East - West > 1e-7;

    public string Title => !string.IsNullOrWhiteSpace(Name) ? Name
        : !string.IsNullOrWhiteSpace(AdminPath) ? AdminPath : "定位点";

    public string SourceLabel => Source switch
    {
        "amap" => "高德",
        "coord" => "经纬度",
        _ => "开放街道图",
    };

    public double BoxWidthMeters => HasBoundingBox ? (East - West) * MapImportService.MetersPerDegreeLon(Latitude) : 0;
    public double BoxHeightMeters => HasBoundingBox ? (North - South) * MapImportService.MetersPerDegreeLat : 0;

    public string Subtitle
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Kind)) parts.Add(Kind);
            if (!string.IsNullOrWhiteSpace(AdminPath) && AdminPath != Title) parts.Add(AdminPath);
            parts.Add("经纬度 " + Latitude.ToString("0.0000", CultureInfo.InvariantCulture)
                + ", " + Longitude.ToString("0.0000", CultureInfo.InvariantCulture));
            if (HasBoundingBox)
                parts.Add($"边界框约 {BoxWidthMeters / 1000:0.#}×{BoxHeightMeters / 1000:0.#} 公里");
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>一次导入的范围与硬上限。</summary>
public sealed class MapImportOptions
{
    /// <summary>以命中点为中心的半边长（米）。</summary>
    public double RadiusMeters { get; set; } = 500;
    /// <summary>命中自带边界框时按它取（城市级边界框会被范围上限自动收小）。</summary>
    public bool UseHitBoundingBox { get; set; }
    public int MaxRoads { get; set; } = 1200;
    public int MaxBuildings { get; set; } = 600;
    public int MaxPois { get; set; } = 300;

    /// <summary>预设档说的是整幅图的边长（半径取一半），这样才念得对得上「一公里」「五公里」。</summary>
    public static readonly (string Label, double Radius)[] Presets =
    {
        ("街区", 200),
        ("一公里", 500),
        ("五公里", 2500),
    };

    /// <summary>任一边最长 6 公里：再大一次请求要吐几十兆，安卓端先被内存绊倒，画布也走不动。</summary>
    public const double MaxSpanMeters = 6000;
    public const double MaxRadiusMeters = 3000;
}

/// <summary>一次「拉真实城市并拼成地图」的产物：地图本体 + 这次到底拿到了多少东西。</summary>
public sealed class MapBuildResult
{
    public MapDefinition Map { get; set; } = new();
    public int Roads { get; set; }
    public int Buildings { get; set; }
    public int NamedPlaces { get; set; }
    public int Junctions { get; set; }
    public double MetersPerUnit { get; set; } = 1;
    /// <summary>这次实际取到的范围（人话）。</summary>
    public string Extent { get; set; } = "";
    /// <summary>因为上限被砍掉过一部分。</summary>
    public bool Truncated { get; set; }
    public string Warning { get; set; } = "";

    public string ScaleText => $"比例尺：1 单位 ≈ {MetersPerUnit:0.##} 米";
}

/// <summary>
/// 真实城市导入：地名 / 经纬度 → 检索（高德优先，没 key 自动退回开放街道图）→ Overpass 按范围拉
/// 道路 + 建筑 + 兴趣点 → 拼成 <see cref="MapDefinition"/>，交给 <see cref="MapService.AdoptAsync"/> 落地。
///
/// 三条约束是这里全部设计的来源：
/// · 安卓端：全程共用一条连接池（不像每次 new HttpClient 那样把 socket 全砸在 TIME_WAIT 上），
///   响应按字节封顶，JSON 一律在后台线程解析，20 秒就撒手。
/// · 范围与要素数必须有硬上限：城市级 bbox 一张图能拉到上万条路，画布与寻路会当场趴下。
/// · 失败要说人话：没 key、被限流、超出范围是三种完全不同的下一步，不能都写成「网络错误」。
/// </summary>
public sealed class MapImportService
{
    private const string AmapEndpoint = "https://restapi.amap.com/v3/geocode/geo";
    private const string NominatimEndpoint = "https://nominatim.openstreetmap.org/search";
    private const string OverpassEndpoint = "https://overpass-api.de/api/interpreter";

    /// <summary>Nominatim 的使用政策要求每条请求带可识别的 User-Agent，缺了会被 403。</summary>
    private const string UserAgent = "WarmAsBefore/1.0 (个人应用 · AI 地图创建)";

    public const double MetersPerDegreeLat = 111320;

    /// <summary>单次响应的字节上限：超了就是范围太大，与其把手机拖死，不如直接叫用户缩小范围。</summary>
    private const long MaxResponseBytes = 12L * 1024 * 1024;

    private const int MaxTransitPoints = 2400;
    /// <summary>轮廓抽稀到的点数：真实建筑动辄上百个点，全写进存档等于每次加载都重算一遍。</summary>
    private const int MaxOutlinePoints = 24;

    private const string AmapKeyPref = "map.amap.key";

    /// <summary>共用一条连接池（与 PluginHost 同一做法）：地名检索与 Overpass 都走它。</summary>
    private static readonly HttpClient Net = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(8),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        MaxConnectionsPerServer = 2,
    })
    { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>上一次检索有没有「高德不行、退回开放街道图」这类话要说给界面听（服务层不直接弹 Toast）。</summary>
    public string LastSearchNote { get; private set; } = "";

    /// <summary>
    /// 高德 Web 服务的 key。设置页还没有这一栏，先落在 Preferences（页面顶部那条可以直接填），
    /// 也认环境变量 WAB_AMAP_KEY，方便开发与桌面端手工试。
    /// </summary>
    public string AmapKey
    {
        get => ReadAmapKey();
        set
        {
            try { Microsoft.Maui.Storage.Preferences.Default.Set(AmapKeyPref, (value ?? "").Trim()); }
            catch (Exception ex) { App.WriteLog("MapImportService: 高德 key 没存下 —— " + ex.Message); }
        }
    }

    public bool HasAmapKey => !string.IsNullOrWhiteSpace(AmapKey);

    private static string ReadAmapKey()
    {
        var env = Environment.GetEnvironmentVariable("WAB_AMAP_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        try { return Microsoft.Maui.Storage.Preferences.Default.Get(AmapKeyPref, "") ?? ""; }
        catch (Exception ex)
        {
            App.WriteLog("MapImportService: 高德 key 读不到 —— " + ex.Message);
            return "";
        }
    }

    /// <summary>来源提示（页面顶部那条灰字用）。</summary>
    public string SourceNote => HasAmapKey
        ? "检索走高德（国内的中文地名与区划更准），道路与建筑详情仍取自开放街道图。"
        : "还没填高德 Key，检索退回开放街道图 —— 国内的中文店名与小路会少一些。填上 Key 自动改用高德。";

    // ==================== 检索 ====================

    /// <summary>
    /// 输入城市 / 县区 / 街道名，或直接输入「120.15,30.27」这种经纬度。
    /// 经纬度当场解析成一条结果，不联网；两边都失败时抛出一句说清原因的话。
    /// </summary>
    public async Task<List<MapSearchHit>> SearchAsync(string query, CancellationToken ct)
    {
        var text = (query ?? "").Trim();
        LastSearchNote = "";
        if (text.Length == 0) return new List<MapSearchHit>();

        if (TryParseCoordinate(text, out var lon, out var lat))
        {
            return new List<MapSearchHit>
            {
                new()
                {
                    Name = "按经纬度定位",
                    AdminPath = lat.ToString("0.000000", CultureInfo.InvariantCulture) + "°, "
                        + lon.ToString("0.000000", CultureInfo.InvariantCulture) + "°",
                    Kind = "点",
                    Latitude = lat,
                    Longitude = lon,
                    Source = "coord",
                }
            };
        }

        string? amapFailure = null;
        if (HasAmapKey)
        {
            try
            {
                var url = $"{AmapEndpoint}?address={Uri.EscapeDataString(text)}"
                    + $"&key={Uri.EscapeDataString(AmapKey.Trim())}&output=JSON";
                var json = await GetJsonAsync(url, ct).ConfigureAwait(false);
                var hits = await Task.Run(() => ParseAmap(json), ct).ConfigureAwait(false);
                if (hits.Count > 0) return hits;
                amapFailure = "高德没返回结果（地址太笼统，或 key 的权限/额度不够）";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                amapFailure = "高德检索失败：" + Brief(ex.Message);
                App.WriteLog("MapImportService.SearchAsync(amap) -> " + ex);
            }
        }

        List<MapSearchHit> osmHits;
        try
        {
            var url = $"{NominatimEndpoint}?q={Uri.EscapeDataString(text)}"
                + "&format=jsonv2&addressdetails=1&limit=8&accept-language=zh-CN";
            var json = await GetJsonAsync(url, ct).ConfigureAwait(false);
            osmHits = await Task.Run(() => ParseNominatim(json), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            App.WriteLog("MapImportService.SearchAsync(osm) -> " + ex);
            var second = "开放街道图检索失败：" + Brief(ex.Message);
            throw new InvalidOperationException((amapFailure is null ? "" : amapFailure + "；")
                + second + " 看看网络，再试一次。");
        }

        if (amapFailure is not null && osmHits.Count > 0)
            LastSearchNote = amapFailure + "，已改用开放街道图的结果。";
        else if (osmHits.Count == 0 && amapFailure is not null)
            throw new InvalidOperationException(amapFailure + "；开放街道图也没找到「" + text + "」。换个更完整的名词试试。");
        return osmHits;
    }

    /// <summary>
    /// 「120.15,30.27」「120.15 30.27」「30.27，120.15」都认。
    /// 高德的习惯是经度在前，所以默认按「经度,纬度」读；而纬度必然落在 ±90 以内，
    /// 于是两个数只有一个越界时对调一次，省得用户来回倒顺序。
    /// </summary>
    public static bool TryParseCoordinate(string text, out double lon, out double lat)
    {
        lon = lat = 0;
        var parts = text.Split(new[] { ',', '，', ' ', ';', '；', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        if (ParseDouble(parts[0]) is not double a || ParseDouble(parts[1]) is not double b) return false;
        if (!(InRange(a, -180, 180) && InRange(b, -90, 90)))
        {
            if (InRange(b, -180, 180) && InRange(a, -90, 90)) (a, b) = (b, a);
            else return false;
        }
        if (a == 0 && b == 0) return false;   // 「0,0」在大西洋上，多半是误触
        lon = a;
        lat = b;
        return true;
    }

    private static bool InRange(double v, double lo, double hi) => v >= lo && v <= hi;

    private static double? ParseDouble(string s) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    // ==================== 取要素并拼图 ====================

    /// <summary>按命中取一份真实城市拼成地图。预览与导入都走 <see cref="FetchAsync"/>，这里只取本体。</summary>
    public async Task<MapDefinition> BuildAsync(MapSearchHit hit, MapImportOptions options, CancellationToken ct)
    {
        var built = await FetchAsync(hit, options, ct).ConfigureAwait(false);
        return built.Map;
    }

    public async Task<MapBuildResult> FetchAsync(MapSearchHit hit, MapImportOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(hit);
        var o = options ?? new MapImportOptions();

        var (south, west, north, east) = ResolveBoundingBox(hit, o);
        var centerLat = (south + north) / 2;
        var centerLon = (west + east) / 2;
        var mPerLon = Math.Max(1, MetersPerDegreeLon(centerLat));
        var eastMeters = (east - west) * mPerLon;
        var northMeters = (north - south) * MetersPerDegreeLat;
        if (eastMeters > MapImportOptions.MaxSpanMeters + 1 || northMeters > MapImportOptions.MaxSpanMeters + 1)
            throw new InvalidOperationException(
                $"范围太大：一次最多取 {MapImportOptions.MaxSpanMeters / 1000:0}×{MapImportOptions.MaxSpanMeters / 1000:0} 公里。"
                + "把半径调小一档，或者分几次拼。");

        var query = BuildOverpassQuery(south, west, north, east);
        using var msg = new HttpRequestMessage(HttpMethod.Post, OverpassEndpoint)
        {
            Content = new StringContent("data=" + Uri.EscapeDataString(query), Encoding.UTF8,
                "application/x-www-form-urlencoded")
        };
        msg.Headers.UserAgent.ParseAdd(UserAgent);
        using var resp = await Net.SendAsync(msg, ct).ConfigureAwait(false);
        var json = await ReadCappedAsync(resp, ct).ConfigureAwait(false);

        // 城市级 JSON 动辄几兆，解析放在后台线程，UI 那一根线不碰。
        return await Task.Run(() => BuildFromOverpass(json, hit, o, centerLat, centerLon, mPerLon,
            eastMeters, northMeters), ct).ConfigureAwait(false);
    }

    private static (double South, double West, double North, double East) ResolveBoundingBox(MapSearchHit hit, MapImportOptions o)
    {
        if (o.UseHitBoundingBox && hit.HasBoundingBox)
        {
            var midLat = (hit.South + hit.North) / 2;
            var midLon = (hit.West + hit.East) / 2;
            var perLon = Math.Max(1, MetersPerDegreeLon(midLat));
            // 城市级边界框照样会超上限：越界就围绕中心收成一个取得动的最大框，别直接把错抛回给用户。
            var halfNS = Math.Min((hit.North - hit.South) / 2 * MetersPerDegreeLat,
                MapImportOptions.MaxSpanMeters / 2) / MetersPerDegreeLat;
            var halfEW = Math.Max(80, Math.Min((hit.East - hit.West) / 2 * perLon,
                MapImportOptions.MaxSpanMeters / 2)) / perLon;
            return (midLat - halfNS, midLon - halfEW, midLat + halfNS, midLon + halfEW);
        }
        var r = Math.Clamp(o.RadiusMeters, 80, MapImportOptions.MaxRadiusMeters);
        var dLat = r / MetersPerDegreeLat;
        var dLon = r / Math.Max(1, MetersPerDegreeLon(hit.Latitude));
        return (hit.Latitude - dLat, hit.Longitude - dLon, hit.Latitude + dLat, hit.Longitude + dLon);
    }

    public static double MetersPerDegreeLon(double latitude) => MetersPerDegreeLat * Math.Cos(latitude * Math.PI / 180);

    private static string BuildOverpassQuery(double south, double west, double north, double east)
    {
        string Fmt(double v) => v.ToString("0.000000", CultureInfo.InvariantCulture);
        var box = Fmt(south) + "," + Fmt(west) + "," + Fmt(north) + "," + Fmt(east);
        var q = new StringBuilder(512);
        // maxsize 是服务端那侧的闸：超了它 Overpass 直接报错，比让它把几十兆慢慢吐到我们手机上好。
        q.Append("[out:json][timeout:25][maxsize:20000000];\n(\n");
        q.Append($"way[\"highway\"]({box});\n");
        q.Append($"way[\"waterway\"]({box});\n");
        q.Append($"way[\"building\"]({box});\n");
        q.Append($"node[\"shop\"][\"name\"]({box});\n");
        q.Append($"node[\"amenity\"][\"name\"]({box});\n");
        q.Append($"way[\"shop\"][\"name\"]({box});\n");
        q.Append($"way[\"amenity\"][\"name\"]({box});\n");
        q.Append($"node[\"highway\"=\"traffic_signals\"]({box});\n");
        q.Append($"node[\"barrier\"=\"toll_booth\"]({box});\n");
        // out body 拿带 tag 的要素，> 把它们引用的节点捞回来，out skel 只回坐标。
        q.Append(");\nout body;\n>;\nout skel qt;\n");
        return q.ToString();
    }

    // ==================== Overpass → MapDefinition ====================

    private sealed class RoadNode
    {
        public double Lat;
        public double Lon;
        public int Degree;
        public bool IsPoint;
        public bool Signal;
        public bool Toll;
        public string Name = "";
    }

    private sealed class RawWay
    {
        public long Id;
        public long[] Nodes = Array.Empty<long>();
        public string Highway = "";
        public string Waterway = "";
        public string Building = "";
        public string Name = "";
        public string Address = "";
        public string Oneway = "";
        public string Junction = "";
        public bool Bridge;
        public bool Tunnel;
        public bool Toll;
        public double TollFee;
        public bool Signal;
        public int Levels;
        public int Basement;
        public int Layer;
        public bool IsRoad => Highway.Length > 0 || Waterway.Length > 0;
    }

    private static MapBuildResult BuildFromOverpass(string json, MapSearchHit hit, MapImportOptions o,
        double centerLat, double centerLon, double metersPerDegreeLon, double eastMeters, double northMeters)
    {
        var nodes = new Dictionary<long, RoadNode>();
        var ways = new List<RawWay>(512);
        ReadElements(json, nodes, ways);

        var mpu = PickMetersPerUnit(Math.Max(eastMeters, northMeters));
        var map = new MapDefinition
        {
            Name = MapNameOf(hit),
            // 命中来自高德就记 amap：这一张的「地名出处」是高德，要素本体仍是 OSM 的，重新导入照走 Overpass。
            Source = hit.Source == "amap" ? "amap" : "osm",
            City = FirstNonEmpty(hit.AdminPath, hit.Name),
            CenterLatitude = centerLat,
            CenterLongitude = centerLon,
            MetersPerUnit = mpu,
        };
        var g = new Graph(map, nodes, centerLat, centerLon, metersPerDegreeLon, mpu);

        // —— 1. 路网顶点：被两条路共用、或是一条路的头尾，才留一个能落脚的点；
        //    纯中间点只当折线顶点，否则一条直路能生出几十个莫须有的「路口」。
        foreach (var w in ways)
        {
            if (!w.IsRoad) continue;
            for (var i = 0; i < w.Nodes.Length; i++)
            {
                if (!nodes.TryGetValue(w.Nodes[i], out var n)) continue;
                n.Degree++;
                if (i == 0 || i == w.Nodes.Length - 1) n.IsPoint = true;
            }
        }
        foreach (var n in nodes.Values)
            if (n.Degree >= 2 || n.Signal || n.Toll) n.IsPoint = true;

        // —— 2. 道路：按「先主干道、后小路」排，超上限就砍后面的小路，别砍名字有用的大街。
        var roads = ways.Where(x => x.IsRoad).OrderBy(RoadPriority).ToList();
        foreach (var w in roads)
        {
            if (g.RoadCount >= o.MaxRoads) { g.Truncated = true; break; }
            var (road, reverse) = MapRoadKind(w);
            if (road.Length == 0) continue;
            var speed = RoadKinds.DefaultSpeedKmh(RoadClassOf(w));
            if (IsClosedRing(w)) g.AddRing(w, road, speed, reverse, o.MaxRoads);
            else g.AddPolylineRoad(w, road, speed, reverse, o.MaxRoads);
        }

        // —— 3. 建筑与带名字的店：有名字的优先留住，光秃秃的轮廓排在后面。
        var buildings = 0;
        var pois = 0;
        foreach (var w in ways.Where(IsPlaceWay).OrderByDescending(x => x.Name.Length > 0).ThenBy(RoadPriority).ToList())
        {
            var isNamed = w.Name.Length > 0;
            if (isNamed ? pois >= o.MaxPois : buildings >= o.MaxBuildings) { g.Truncated = true; continue; }
            var outline = CollectOutline(w, nodes, g);
            if (outline.Count < 3) continue;
            if (isNamed) pois++; else buildings++;
            g.AddPlace(FirstNonEmpty(w.Name, BuildingLabel(w), "建筑"), w, outline);
        }
        foreach (var (id, n) in nodes)
        {
            if (n.Name.Length == 0 || n.Degree > 0 || g.HasTransit(id)) continue;
            if (pois >= o.MaxPois) { g.Truncated = true; break; }
            pois++;
            g.AddPlaceNode(id, n);
        }

        // —— 4. 每栋楼 / 每家店接最近的一个路网顶点：不接就是一张张浮岛，角色走过去要飞。
        g.LinkPlacesToRoads();

        map.StartSceneId = map.Locations.FirstOrDefault(l => l.RealName.Length > 0)?.Scenes.FirstOrDefault()?.Id
            ?? map.Locations.FirstOrDefault()?.Scenes.FirstOrDefault()?.Id ?? "";

        if (map.Locations.Count == 0)
            throw new InvalidOperationException($"这个范围里一栋建筑都没有（{map.Name}）。往市中心挪一挪，或者把半径调大一档。");

        var named = map.Locations.Count(l => l.RealName.Length > 0);
        return new MapBuildResult
        {
            Map = map,
            Roads = g.RoadCount,
            Buildings = buildings,
            NamedPlaces = named,
            Junctions = map.Transits.Count,
            MetersPerUnit = mpu,
            Extent = $"{eastMeters / 1000:0.##} × {northMeters / 1000:0.##} 公里",
            Truncated = g.Truncated,
            Warning = g.Truncated
                ? $"已经按上限截过一轮（道路 {o.MaxRoads} 条 / 建筑 {o.MaxBuildings} 栋 / 店名 {o.MaxPois} 家）。"
                  + "把范围缩小一点能拿全：一张图塞太满，安卓端画布与寻路都会先撑不住。"
                : "",
        };
    }

    private static void ReadElements(string json, Dictionary<long, RoadNode> nodes, List<RawWay> ways)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("elements", out var elements)
            || elements.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("开放街道图这次没有返回要素（这个范围里可能真的什么都没有）。");

        foreach (var el in elements.EnumerateArray())
        {
            var type = GetStr(el, "type");
            if (type == "node")
            {
                if (GetNum(el, "lat") is not double lat || GetNum(el, "lon") is not double lon) continue;
                var id = (long)(GetNum(el, "id", 0) ?? 0);
                var n = nodes.TryGetValue(id, out var exist) ? exist : new RoadNode();
                n.Lat = lat;
                n.Lon = lon;
                if (el.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
                {
                    foreach (var t in tags.EnumerateObject())
                    {
                        var v = t.Value.ValueKind == JsonValueKind.String ? t.Value.GetString() ?? "" : t.Value.ToString();
                        if (t.Name == "name") n.Name = v;
                        else if (t.Name == "highway" && v == "traffic_signals") n.Signal = true;
                        else if (t.Name == "barrier" && v == "toll_booth") n.Toll = true;
                    }
                }
                nodes[id] = n;
            }
            else if (type == "way")
            {
                var w = new RawWay { Id = (long)(GetNum(el, "id", 0) ?? 0) };
                if (el.TryGetProperty("nodes", out var ns) && ns.ValueKind == JsonValueKind.Array)
                {
                    var arr = new List<long>(ns.GetArrayLength());
                    foreach (var v in ns.EnumerateArray())
                        if (v.TryGetInt64(out var lv)) arr.Add(lv);
                    w.Nodes = arr.ToArray();
                }
                if (w.Nodes.Length < 2) continue;
                if (el.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
                    ReadWayTags(tags, w);
                if (!w.IsRoad && w.Building.Length == 0 && w.Name.Length == 0) continue;
                ways.Add(w);
            }
        }
    }

    /// <summary>一条 way 的 tag 只取我们真用得到的那十来项，其余丢掉（否则一张城市图要白白揣着几万串）。</summary>
    private static void ReadWayTags(JsonElement tags, RawWay w)
    {
        var houseNumber = "";
        var street = "";
        var full = "";
        foreach (var t in tags.EnumerateObject())
        {
            var v = t.Value.ValueKind == JsonValueKind.String ? t.Value.GetString() ?? ""
                : t.Value.ValueKind == JsonValueKind.Number ? t.Value.ToString() : "";
            switch (t.Name)
            {
                case "highway": w.Highway = v; break;
                case "waterway": w.Waterway = v; break;
                case "building": w.Building = v; break;
                case "name": w.Name = v; break;
                case "oneway": w.Oneway = v; break;
                case "junction": w.Junction = v; break;
                case "bridge": w.Bridge = v is "yes" or "true" or "1"; break;
                case "tunnel": w.Tunnel = v is "yes" or "true" or "1"; break;
                case "toll": w.Toll = v is "yes" or "true" or "1"; break;
                case "toll:fee": if (TryMoney(v, out var fee)) { w.TollFee = fee; w.Toll = true; } break;
                case "traffic_signals": w.Signal = v is "intersection" or "yes" or "true"; break;
                case "building:levels": w.Levels = ParseInt(v); break;
                case "building:levels:underground": w.Basement = ParseInt(v); break;
                case "layer": w.Layer = ParseInt(v); break;
                case "addr:full": full = v; break;
                case "addr:street": street = v; break;
                case "addr:housenumber": houseNumber = v; break;
            }
        }
        if (full.Length > 0) w.Address = full;
        else if (street.Length > 0) w.Address = houseNumber.Length > 0 ? street + " " + houseNumber : street;
    }

    /// <summary>「¥15」「15 CNY」「15.5」都读得出来：只取第一个数。</summary>
    private static bool TryMoney(string v, out double fee)
    {
        fee = 0;
        var run = new StringBuilder(12);
        foreach (var ch in v)
        {
            if (char.IsDigit(ch) || ch == '.') run.Append(ch);
            else if (run.Length > 0) break;
        }
        return run.Length > 0 && double.TryParse(run.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out fee);
    }

    private static int ParseInt(string v)
    {
        var digits = new string(v.Where(ch => char.IsDigit(ch) || ch == '-').ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0;
    }

    private static bool IsClosedRing(RawWay w) => w.Nodes.Length > 4 && w.Nodes[0] == w.Nodes[^1];

    private static bool IsPlaceWay(RawWay w) =>
        // building:part 是同栋楼切片，收进来就是一摞重叠的框
        (w.Building.Length > 0 && w.Building != "part")
        || (w.Name.Length > 0 && !w.IsRoad);

    private static List<MapWaypoint> CollectOutline(RawWay w, Dictionary<long, RoadNode> nodes, Graph g)
    {
        var pts = new List<MapWaypoint>(w.Nodes.Length);
        foreach (var id in w.Nodes)
            if (nodes.TryGetValue(id, out var n)) pts.Add(new MapWaypoint { X = g.ToX(n.Lon), Y = g.ToY(n.Lat) });
        if (pts.Count <= MaxOutlinePoints) return pts;
        // 抽稀：等间隔取样、点数封顶。上百个点写进存档，每次加载都要重算一遍整条轮廓。
        var step = pts.Count / (double)MaxOutlinePoints;
        var thinned = new List<MapWaypoint>(MaxOutlinePoints);
        for (var i = 0; i < MaxOutlinePoints; i++) thinned.Add(pts[(int)(i * step)]);
        return thinned;
    }

    private static (string Road, bool Reverse) MapRoadKind(RawWay w)
    {
        if (w.Waterway.Length > 0) return (RoadKinds.Waterway, false);
        var cls = RawClass(w.Highway);
        if (cls.Length == 0) return ("", false);
        if (w.Tunnel) return (RoadKinds.Tunnel, false);
        if (w.Bridge) return (RoadKinds.Bridge, false);
        return (cls, w.Oneway == "-1");   // -1 = 通行方向与画出来的节点顺序相反，把端点对调过来
    }

    private static string RoadClassOf(RawWay w) => w.Waterway.Length > 0 ? RoadKinds.Waterway : RawClass(w.Highway);

    /// <summary>OSM 的 highway=* 到本项目道路类别的对照；空串表示这条不要进图。</summary>
    private static string RawClass(string highway) => highway switch
    {
        "motorway" or "motorway_link" or "freeway" or "express_way" => RoadKinds.Highway,
        "trunk" or "trunk_link" or "primary" or "primary_link" or "secondary" or "secondary_link" => RoadKinds.Trunk,
        "tertiary" or "tertiary_link" or "residential" or "unclassified" or "living_street" or "road" => RoadKinds.Street,
        "service" or "alley" or "drive_through" or "private" or "track" => RoadKinds.Lane,
        "pedestrian" or "footway" or "path" or "cycleway" or "bridleway" or "pier" or "corridor" => RoadKinds.Walkway,
        "steps" => RoadKinds.Steps,
        // 施工 / 规划 / 废弃 / 纯虚拟的路口连线：画出来只会让角色往一条不存在的路里走。
        _ => "",
    };

    /// <summary>被截断时的保留次序：高速与主干道先留，无名小路最后丢。</summary>
    private static int RoadPriority(RawWay w)
    {
        if (w.IsRoad)
        {
            var c = w.Waterway.Length > 0 ? RoadKinds.Waterway : RawClass(w.Highway);
            if (w.Name.Length > 0 && c is RoadKinds.Street or RoadKinds.Trunk or RoadKinds.Highway) return 1;
            return c switch
            {
                RoadKinds.Highway or RoadKinds.Trunk => 0,
                RoadKinds.Street or RoadKinds.Bridge or RoadKinds.Tunnel => 2,
                RoadKinds.Lane => 3,
                RoadKinds.Walkway or RoadKinds.Steps => 4,
                RoadKinds.Waterway => 5,
                _ => 6,
            };
        }
        if (w.Name.Length > 0) return 1;
        return w.Building.Length > 0 ? 4 : 5;
    }

    private static string BuildingLabel(RawWay w) => w.Building switch
    {
        "residential" or "house" or "apartments" or "detached" or "farm" => "住宅楼",
        "commercial" or "office" or "retail" => "写字楼",
        "school" or "university" or "college" or "kindergarten" => "学校",
        "hospital" or "clinic" => "医院",
        "hotel" or "guest_house" => "旅馆",
        "supermarket" or "mall" or "shop" => "商场",
        "church" or "mosque" or "temple" or "shrine" => "庙堂",
        "industrial" or "warehouse" or "factory" => "厂房",
        "garage" or "garages" or "shed" => "车库",
        "construction" or "ruins" or "" => "",
        _ => "建筑",
    };

    private static string UniqueName(string baseName, HashSet<string> used)
    {
        var name = baseName.Length == 0 ? "无名地点" : baseName;
        if (used.Add(name)) return name;
        var i = 2;
        while (!used.Add(name + " " + i)) i++;
        return name + " " + i;
    }

    private static string MapNameOf(MapSearchHit hit)
    {
        var n = FirstNonEmpty(hit.Name, hit.AdminPath);
        if (n.Length > 24) n = n[..24];
        return n.Length == 0 ? "真实城市" : n;
    }

    /// <summary>
    /// 让整幅图落在千把个画布单位上下：再小卡片会叠成一坨，再大编辑器平移一步就出界。
    /// 取不小于算值的「整齐档」，比例尺念出来是 1 单位 ≈ 2 米这种整话；
    /// 边长仍按 几何长度 × MetersPerUnit 反推真实米数，所以画布与比例尺始终自洽。
    /// </summary>
    private static double PickMetersPerUnit(double spanMeters)
    {
        var raw = spanMeters / 1200.0;
        double[] ladder = { 0.05, 0.1, 0.2, 0.25, 0.5, 1, 2, 2.5, 5, 10, 20, 25, 50, 100 };
        foreach (var v in ladder) if (raw <= v) return v;
        return 100;
    }

    // ==================== 建图上下文 ====================

    /// <summary>
    /// 一次建图的全部中间状态：投影、顶点登记、去重、上限记账。
    /// 单独拿出来是因为这些量在道路 / 环岛 / 建筑三段里都要用，摊在方法签名上传来传去会糊成一团。
    /// </summary>
    private sealed class Graph
    {
        private readonly MapDefinition _map;
        private readonly Dictionary<long, RoadNode> _nodes;
        private readonly Dictionary<long, string> _transitIds = new();
        private readonly HashSet<(string, string)> _seenEdges = new();
        private readonly HashSet<string> _usedNames = new();
        private readonly double _centerLat;
        private readonly double _centerLon;
        private readonly double _mPerLon;
        private readonly double _mpu;

        public bool Truncated;
        public int RoadCount { get; private set; }

        public Graph(MapDefinition map, Dictionary<long, RoadNode> nodes,
            double centerLat, double centerLon, double mPerLon, double mpu)
        {
            _map = map;
            _nodes = nodes;
            _centerLat = centerLat;
            _centerLon = centerLon;
            _mPerLon = mPerLon;
            _mpu = mpu;
        }

        public double ToX(double lon) => (lon - _centerLon) * _mPerLon / _mpu;
        public double ToY(double lat) => -(lat - _centerLat) * MetersPerDegreeLat / _mpu;
        public bool HasTransit(long nodeId) => _transitIds.ContainsKey(nodeId);

        /// <summary>OSM 节点 → 图上的顶点（临时节点）。超出顶点上限就不再造，这一段会被丢掉。</summary>
        private string EnsureNode(long nodeId, string fallbackName, string junction)
        {
            if (_transitIds.TryGetValue(nodeId, out var known)) return known;
            if (!_nodes.TryGetValue(nodeId, out var n)) { Truncated = true; return ""; }
            if (_map.Transits.Count >= MaxTransitPoints) { Truncated = true; return ""; }
            var t = new MapTransit
            {
                Name = FirstNonEmpty(n.Name, fallbackName, "路口"),
                X = ToX(n.Lon),
                Y = ToY(n.Lat),
                Latitude = n.Lat,
                Longitude = n.Lon,
                // 红绿灯与收费站归节点自己管：同一个路口的每条边都因此带上红绿灯，不用逐条猜。
                Junction = n.Signal ? JunctionKinds.Crossing
                    : n.Toll ? JunctionKinds.Toll
                    : junction,
                SourceId = "n" + nodeId.ToString(CultureInfo.InvariantCulture),
            };
            _map.Transits.Add(t);
            _transitIds[nodeId] = t.Id;
            return t.Id;
        }

        /// <summary>一条不闭合的路：在岔口处切成几段，段里的普通节点降为折线顶点，形状因此还在。</summary>
        public void AddPolylineRoad(RawWay w, string road, double speed, bool reverse, int budget)
        {
            if (RoadCount >= budget) { Truncated = true; return; }
            var name = FirstNonEmpty(w.Name, "路");
            var prev = EnsureNode(w.Nodes[0], name, "");
            var interior = new List<MapWaypoint>();
            for (var i = 1; i < w.Nodes.Length; i++)
            {
                var id = w.Nodes[i];
                var isLast = i == w.Nodes.Length - 1;
                if (!_nodes.TryGetValue(id, out var n)) { Truncated = true; continue; }
                if (!n.IsPoint && !isLast)
                {
                    interior.Add(new MapWaypoint { X = ToX(n.Lon), Y = ToY(n.Lat) });
                    continue;
                }
                if (RoadCount >= budget) { Truncated = true; return; }
                var next = EnsureNode(id, name, isLast && n.Degree <= 1 ? "" : JunctionKinds.Junction);
                AddEdge(prev, next, w, road, speed, reverse, interior);
                prev = next;
                interior.Clear();
            }
        }

        /// <summary>
        /// 闭合环路 / 环岛。闭合 way 的首尾是同一个节点，直连会生成一条自己接自己的边，
        /// 所以按岔口拆成几段弧线；环岛再在环心放一颗 <see cref="JunctionKinds.Roundabout"/> 的顶点。
        /// </summary>
        public void AddRing(RawWay w, string road, double speed, bool reverse, int budget)
        {
            var ring = w.Nodes[..^1];
            var stops = new List<int>();
            for (var i = 0; i < ring.Length; i++)
                if (_nodes.TryGetValue(ring[i], out var n) && n.Degree >= 3) stops.Add(i);
            if (stops.Count == 0)
            {
                var take = Math.Min(4, ring.Length);
                for (var k = 0; k < take; k++) stops.Add(ring.Length * k / Math.Max(1, take));
            }
            if (stops.Count == 0) return;

            for (var s = 0; s < stops.Count && RoadCount < budget; s++)
            {
                var fromIdx = stops[s];
                var toIdx = stops[(s + 1) % stops.Count];
                var interior = new List<MapWaypoint>();
                for (var i = (fromIdx + 1) % ring.Length; i != toIdx; i = (i + 1) % ring.Length)
                    if (_nodes.TryGetValue(ring[i], out var n)) interior.Add(new MapWaypoint { X = ToX(n.Lon), Y = ToY(n.Lat) });
                var a = EnsureNode(ring[fromIdx], FirstNonEmpty(w.Name, "路口"), "");
                var b = EnsureNode(ring[toIdx], FirstNonEmpty(w.Name, "路口"), "");
                AddEdge(a, b, w, road, speed, reverse, interior);
            }

            if (w.Junction != "roundabout" || RoadCount >= budget || _map.Transits.Count >= MaxTransitPoints) return;
            double cx = 0, cy = 0;
            var counted = 0;
            foreach (var id in ring)
                if (_nodes.TryGetValue(id, out var n)) { cx += ToX(n.Lon); cy += ToY(n.Lat); counted++; }
            if (counted == 0) return;
            var center = new MapTransit
            {
                Name = FirstNonEmpty(w.Name, "环岛"),
                X = cx / counted,
                Y = cy / counted,
                Junction = JunctionKinds.Roundabout,
                Note = "开放街道图上的环岛",
                SourceId = "w" + w.Id.ToString(CultureInfo.InvariantCulture),
            };
            _map.Transits.Add(center);
            foreach (var idx in stops)
            {
                if (RoadCount >= budget) break;
                if (!_transitIds.TryGetValue(ring[idx], out var stop)) continue;
                AddEdge(stop, center.Id, w, road, speed, false, new List<MapWaypoint>());
            }
        }

        private void AddEdge(string from, string to, RawWay w, string road, double speed, bool reverse,
            List<MapWaypoint> interior)
        {
            if (from.Length == 0 || to.Length == 0 || from == to) return;
            var a = reverse ? to : from;
            var b = reverse ? from : to;
            if (!_seenEdges.Add((a, b))) return;
            var points = interior;
            if (reverse && points.Count > 1)
            {
                points = new List<MapWaypoint>(interior);
                points.Reverse();
            }
            _map.Edges.Add(new MapEdge
            {
                A = a,
                B = b,
                Kind = "line",
                Road = road,
                RoadName = w.Name,
                // 桥/隧道会把 Road 换掉，限速得按原来的路级钉住：一条高架高速被算成 40 码，走路耗时就读错了。
                SpeedKmh = road is RoadKinds.Bridge or RoadKinds.Tunnel ? speed : 0,
                OneWay = w.Oneway is "yes" or "true" or "1" || w.Junction == "roundabout",
                TrafficLight = w.Signal,
                Toll = w.Toll,
                TollFee = w.TollFee,
                SourceId = "w" + w.Id.ToString(CultureInfo.InvariantCulture),
                Waypoints = points,
            });
            RoadCount++;
        }

        /// <summary>一栋楼 / 一家店：轮廓、真实店名、门牌、楼层，加一个还没背景的场景。</summary>
        public void AddPlace(string baseName, RawWay w, List<MapWaypoint> outline)
        {
            double sx = 0, sy = 0;
            foreach (var p in outline) { sx += p.X; sy += p.Y; }
            var cx = sx / outline.Count;
            var cy = sy / outline.Count;
            (double lat, double lon) = NodeAverage(w);
            var name = UniqueName(baseName, _usedNames);
            var loc = new MapLocation
            {
                Name = name,
                RealName = w.Name,
                Address = w.Address,
                X = cx,
                Y = cy,
                Latitude = lat,
                Longitude = lon,
                Floors = Math.Max(0, w.Levels),
                BasementFloors = Math.Max(0, w.Basement > 0 ? w.Basement : w.Layer < 0 ? -w.Layer : 0),
                Outline = outline,
                SourceId = "w" + w.Id.ToString(CultureInfo.InvariantCulture),
            };
            // 背景一律留空：导入只带来形状与名字，实景要么用户自己拍照，要么让 AI 补一张。
            loc.Scenes.Add(new MapScene { Name = name, X = cx, Y = cy });
            _map.Locations.Add(loc);
        }

        /// <summary>一个点状的店（没轮廓）：位置与真实店名照记，轮廓留空由卡片框表示。</summary>
        public void AddPlaceNode(long id, RoadNode n)
        {
            var name = UniqueName(n.Name, _usedNames);
            var loc = new MapLocation
            {
                Name = name,
                RealName = n.Name,
                X = ToX(n.Lon),
                Y = ToY(n.Lat),
                Latitude = n.Lat,
                Longitude = n.Lon,
                SourceId = "n" + id.ToString(CultureInfo.InvariantCulture),
            };
            loc.Scenes.Add(new MapScene { Name = name, X = loc.X, Y = loc.Y });
            _map.Locations.Add(loc);
        }

        public void LinkPlacesToRoads()
        {
            if (_map.Transits.Count == 0) return;
            foreach (var loc in _map.Locations)
            {
                var scene = loc.Scenes.FirstOrDefault();
                if (scene is null) continue;
                MapTransit? best = null;
                var bestD = double.MaxValue;
                foreach (var t in _map.Transits)
                {
                    var dx = t.X - scene.X;
                    var dy = t.Y - scene.Y;
                    var d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = t; }
                }
                if (best is null) continue;
                _map.Edges.Add(new MapEdge
                {
                    A = best.Id,
                    B = scene.Id,
                    Kind = "line",
                    Road = RoadKinds.Walkway,
                    RoadName = loc.RealName.Length > 0 ? loc.RealName + " 门口" : "",
                    // 门口是自动接的：离路太远时提醒一句，省得用户在画布上找这根线找半天。
                    Note = bestD > 400 ? "门口位置是自动接的，接得有点远" : "",
                    SourceId = loc.SourceId,
                });
            }
        }

        private (double Lat, double Lon) NodeAverage(RawWay w)
        {
            double sl = 0, sn = 0;
            var c = 0;
            foreach (var id in w.Nodes)
                if (_nodes.TryGetValue(id, out var n)) { sl += n.Lat; sn += n.Lon; c++; }
            return c == 0 ? (0, 0) : (sl / c, sn / c);
        }
    }

    // ==================== 检索结果解析（都在后台线程跑） ====================

    private static List<MapSearchHit> ParseAmap(string json)
    {
        var hits = new List<MapSearchHit>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (GetStr(root, "status") != "1")
        {
            var described = GetStr(root, "info");
            throw new InvalidOperationException(described.Length > 0
                ? $"高德返回：{described}（infocode {GetStr(root, "infocode")}）"
                : "高德的返回读不懂（infocode " + GetStr(root, "infocode") + "）。");
        }
        if (!root.TryGetProperty("geocodes", out var arr) || arr.ValueKind != JsonValueKind.Array) return hits;
        foreach (var g in arr.EnumerateArray())
        {
            var parts = GetStr(g, "location").Split(',');
            if (parts.Length != 2) continue;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) continue;
            var admin = new StringBuilder();
            foreach (var key in new[] { "province", "city", "district" })
            {
                var v = CleanAmapField(GetStr(g, key));
                if (v.Length > 0 && !admin.ToString().Contains(v, StringComparison.Ordinal)) admin.Append(v);
            }
            hits.Add(new MapSearchHit
            {
                Name = FirstNonEmpty(CleanAmapField(GetStr(g, "street")), GetStr(g, "level"), "定位点"),
                AdminPath = FirstNonEmpty(GetStr(g, "formatted_address"), admin.ToString()),
                Kind = GetStr(g, "level"),
                PlaceId = GetStr(g, "adcode"),
                Latitude = lat,
                Longitude = lon,
                Source = "amap",
            });
        }
        return hits;
    }

    /// <summary>高德把空字段写成 "[]"，原样拼进界面就是一行方括号。</summary>
    private static string CleanAmapField(string v) => v.Trim('[', ']', ' ');

    private static List<MapSearchHit> ParseNominatim(string json)
    {
        var hits = new List<MapSearchHit>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return hits;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (GetNum(el, "lat") is not double lat || GetNum(el, "lon") is not double lon) continue;
            double s = 0, n = 0, w = 0, e = 0;
            var got = 0;
            if (el.TryGetProperty("boundingbox", out var bb) && bb.ValueKind == JsonValueKind.Array)
            {
                // OSM 这边 boundingbox 的顺序是 [南, 北, 西, 东]，不是直觉上的「南西北东」。
                foreach (var v in bb.EnumerateArray())
                {
                    if (got >= 4) break;
                    if (!double.TryParse(v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) break;
                    switch (got++)
                    {
                        case 0: s = d; break;
                        case 1: n = d; break;
                        case 2: w = d; break;
                        default: e = d; break;
                    }
                }
            }
            hits.Add(new MapSearchHit
            {
                Name = GetStr(el, "name"),
                AdminPath = GetStr(el, "display_name"),
                Kind = FirstNonEmpty(GetStr(el, "addresstype"), GetStr(el, "type")),
                PlaceId = ((long)(GetNum(el, "place_id", 0) ?? 0)).ToString(CultureInfo.InvariantCulture),
                Latitude = lat,
                Longitude = lon,
                Source = "osm",
                South = got == 4 ? s : 0,
                North = got == 4 ? n : 0,
                West = got == 4 ? w : 0,
                East = got == 4 ? e : 0,
            });
        }
        return hits;
    }

    // ==================== 网络小工具 ====================

    private static async Task<string> GetJsonAsync(string url, CancellationToken ct)
    {
        using var msg = new HttpRequestMessage(HttpMethod.Get, url);
        // Nominatim 的政策是可识别的 UA 必带，缺了会被封；高德与 Overpass 一并带上，省得分叉。
        msg.Headers.UserAgent.ParseAdd(UserAgent);
        using var resp = await Net.SendAsync(msg, ct).ConfigureAwait(false);
        return await ReadCappedAsync(resp, ct).ConfigureAwait(false);
    }

    private static async Task<string> ReadCappedAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var body = await ReadBodyAsync(resp, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            // 限流与查不到是两件不同的事，得分开说：429 该等一等，404 该换个词。
            var why = (int)resp.StatusCode switch
            {
                429 => "请求太频繁，对方让我们歇一会儿（Overpass 与 Nominatim 的免费额度就这样，稍等再试一次）",
                403 => "对方拒绝了这个请求（Nominatim 要可识别的 User-Agent；高德的 key 也可能不对）",
                404 => "地址或参数不对：" + Brief(body),
                >= 500 => "对方此刻腾不出手（HTTP " + (int)resp.StatusCode + "），过一会儿再试",
                _ => "HTTP " + (int)resp.StatusCode + "：" + Brief(body),
            };
            throw new InvalidOperationException(why);
        }
        return body;
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buf = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaxResponseBytes)
                throw new InvalidOperationException(
                    $"返回的数据太大（超过 {MaxResponseBytes / 1024 / 1024}MB 上限）。把半径调到一公里以内再试。");
            ms.Write(buf, 0, read);
        }
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    private static string Brief(string s)
    {
        s = (s ?? "").Trim();
        if (s.Length > 160) s = s[..160] + "…";
        return s.Length == 0 ? "对方没给原因" : s;
    }

    private static string FirstNonEmpty(params string?[] vs)
    {
        foreach (var v in vs) if (!string.IsNullOrWhiteSpace(v)) return v!.Trim();
        return "";
    }

    private static string GetStr(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

    private static double? GetNum(JsonElement el, string key, double? fallback = null)
    {
        if (!el.TryGetProperty(key, out var v)) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String => double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback,
            _ => fallback,
        };
    }
}
