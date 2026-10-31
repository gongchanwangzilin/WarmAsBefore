using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Tools;

/// <summary>
/// 高德 AI 工具：给模型的「地图理解」外挂——路线规划（amap_route）与找店（amap_poi）。
/// 设计跟 MapImportService 同源：共用一条连接池、同一份 Key（设置页填的 map.amap.key，
/// 也认环境变量 WAB_AMAP_KEY）、失败要说人话。没配 Key 不炸，回一句「未配置」让模型转告。
/// 坐标一律 GCJ-02（高德的宇宙），两个工具之间可以无缝互喂。
/// </summary>
public sealed partial class ToolManager
{
    private const string AmapKeyPref = "map.amap.key";
    private const long MaxAmapBytes = 4L * 1024 * 1024;

    /// <summary>共用一条连接池（与 MapImportService 同一做法）：路线、POI、地理编码都走它。</summary>
    private static readonly HttpClient AmapNet = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(8),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        MaxConnectionsPerServer = 2,
    })
    { Timeout = TimeSpan.FromSeconds(20) };

    private void RegisterAmapTools()
    {
        RegisterBuiltin("amap_route",
            "路线规划：算从起点到终点怎么走、多远、几分钟。起点/终点给地名（可带城市）或经纬度「116.481,39.99」（经度在前）。"
            + "mode=walking|driving，默认 driving；坐标是 GCJ-02，可直接继续喂回本工具或 amap_poi。"
            + "用户问「怎么走 / 要多久 / 多远 / 帮我规划」时用它。",
            async args =>
            {
                var key = AmapKeyOrNull();
                if (key.Length == 0)
                    return JsonSerializer.Serialize(new { ok = false, message = "未配置高德 Key：设置里填好（或环境变量 WAB_AMAP_KEY）再叫我。" });

                var origin = GetString(args, "origin", "").Trim();
                var destination = GetString(args, "destination", "").Trim();
                var city = GetString(args, "city", "").Trim();
                var mode = GetString(args, "mode", "driving").Trim().ToLowerInvariant();
                if (mode != "walking") mode = "driving";
                var strategy = GetInt(args, "strategy", 0);

                if (origin.Length == 0 || destination.Length == 0)
                    return JsonSerializer.Serialize(new { ok = false, message = "起点和终点都得给一个（地名或经纬度）。" });

                try
                {
                    var start = await AmapResolvePointAsync(origin, city, key);
                    var goal = await AmapResolvePointAsync(destination, city, key);
                    if (start == goal)
                        return JsonSerializer.Serialize(new { ok = false, message = "起点和终点算出来重了。" });

                    var url = $"https://restapi.amap.com/v3/direction/{mode}?origin={Uri.EscapeDataString(start)}"
                        + $"&destination={Uri.EscapeDataString(goal)}&key={Uri.EscapeDataString(key)}&output=JSON";
                    if (mode == "driving")
                        url += $"&strategy={Math.Clamp(strategy, 0, 32)}";

                    var json = await AmapFetchAsync(url);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (JsonText(root, "status") != "1")
                    {
                        var info = JsonText(root, "info");
                        AmapLog("route 被高德拒：info=" + info);
                        return JsonSerializer.Serialize(new { ok = false, message = "高德没接单：" + info + "（检查 Key 权限与余额）" });
                    }
                    if (!root.TryGetProperty("route", out var route) || !route.TryGetProperty("paths", out var paths)
                        || paths.ValueKind != JsonValueKind.Array || paths.GetArrayLength() == 0)
                        return JsonSerializer.Serialize(new { ok = false, message = "这条路没算出方案：位置太偏或步行距离过长，地名称得更完整些试试。" });

                    var path = paths[0];
                    var meters = JsonNum(path, "distance");
                    var seconds = JsonNum(path, "duration");
                    var steps = new List<object>();
                    if (path.TryGetProperty("steps", out var stepArr) && stepArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var s in stepArr.EnumerateArray())
                        {
                            if (steps.Count >= 30) break;
                            var text = JsonText(s, "instruction");
                            if (text.Length == 0) text = JsonText(s, "instructions");
                            var road = JsonText(s, "road");
                            if (text.Length == 0 && road.Length == 0) continue;
                            steps.Add(new { text, road = road.Length == 0 ? null : road, m = JsonNum(s, "distance") });
                        }
                    }

                    var mins = seconds <= 0 ? 0 : (int)Math.Round(seconds / 60.0);
                    return JsonSerializer.Serialize(new
                    {
                        ok = true,
                        mode,
                        origin,
                        destination,
                        originCoord = start,
                        destCoord = goal,
                        meters = (int)Math.Round(meters),
                        minutes = mins,
                        summary = $"全程 {Fmt(meters)} 米、约 {mins} 分钟（{(mode == "walking" ? "步行" : "驾车")}）。",
                        steps,
                    });
                }
                catch (Exception ex)
                {
                    AmapLog("route -> " + ex);
                    return JsonSerializer.Serialize(new { ok = false, message = "路线没算出来：" + AmapBrief(ex) });
                }
            },
            """
            {"type":"object","properties":{"origin":{"type":"string","description":"起点：地名（可含城市，如「北京市朝阳区望京」）或经纬度「116.481,39.99」"},"destination":{"type":"string","description":"终点：同上"},"city":{"type":"string","description":"消歧用的城市名，可选"},"mode":{"type":"string","enum":["walking","driving"],"description":"出行方式，默认 driving"},"strategy":{"type":"integer","description":"驾车策略 0..32，可选"}},"required":["origin","destination"]}
            """);

        RegisterBuiltin("amap_poi",
            "周边与找店：mode=around 需给中心点 location（地名或经纬度）与半径 radius（默认 1000 米）；"
            + "mode=text 用 city + keywords 全城找。keywords 可空、types 是高德分类码（如 050500 餐饮）。"
            + "返回名称、地址、距离与坐标（GCJ-02）。用户问「附近有什么 / 推荐一家 / 哪里能买」时用它。",
            async args =>
            {
                var key = AmapKeyOrNull();
                if (key.Length == 0)
                    return JsonSerializer.Serialize(new { ok = false, message = "未配置高德 Key：设置里填好（或环境变量 WAB_AMAP_KEY）再叫我。" });

                var mode = GetString(args, "mode", "around").Trim().ToLowerInvariant();
                if (mode != "around" && mode != "text") mode = "around";
                var keywords = GetString(args, "keywords", "").Trim();
                var types = GetString(args, "types", "").Trim();
                var city = GetString(args, "city", "").Trim();
                var where = GetString(args, "location", "").Trim();
                var radius = Math.Clamp(GetInt(args, "radius", 1000), 20, 50000);

                try
                {
                    string? center = null;
                    if (mode == "around")
                    {
                        if (where.Length == 0)
                            return JsonSerializer.Serialize(new { ok = false, message = "around 要给中心点 location；想全城找就用 mode=text。" });
                        center = await AmapResolvePointAsync(where, city, key);
                    }
                    else if (keywords.Length == 0 && types.Length == 0)
                    {
                        return JsonSerializer.Serialize(new { ok = false, message = "text 检索 keywords 和 types 至少给一个。" });
                    }

                    var url = $"https://restapi.amap.com/v3/place/{mode}?key={Uri.EscapeDataString(key)}&output=JSON&offset=15&page=1";
                    if (mode == "around")
                    {
                        url += $"&location={Uri.EscapeDataString(center!)}&radius={radius}&sortrule=distance";
                        if (keywords.Length > 0) url += "&keywords=" + Uri.EscapeDataString(keywords);
                        if (types.Length > 0) url += "&types=" + Uri.EscapeDataString(types);
                    }
                    else
                    {
                        if (keywords.Length > 0) url += "&keywords=" + Uri.EscapeDataString(keywords);
                        if (types.Length > 0) url += "&types=" + Uri.EscapeDataString(types);
                        if (city.Length > 0) url += "&city=" + Uri.EscapeDataString(city) + "&citylimit=true";
                    }

                    var json = await AmapFetchAsync(url);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (JsonText(root, "status") != "1")
                    {
                        var info = JsonText(root, "info");
                        AmapLog("poi 被高德拒：info=" + info);
                        return JsonSerializer.Serialize(new { ok = false, message = "高德没接单：" + info + "（检查 Key 权限与余额）" });
                    }

                    var list = new List<object>();
                    if (root.TryGetProperty("pois", out var pois) && pois.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var p in pois.EnumerateArray())
                        {
                            if (list.Count >= 15) break;
                            var name = JsonText(p, "name");
                            if (name.Length == 0) continue;
                            var addr = JsonText(p, "address");
                            var cat = JsonText(p, "type");
                            var area = JsonText(p, "pname");
                            var town = JsonText(p, "cityname");
                            var dist = JsonNum(p, "distance");
                            list.Add(new
                            {
                                name,
                                address = addr.Length == 0 ? null : addr,
                                area = area.Length == 0 ? null : town.Length == 0 ? area : area + " " + town,
                                cat = cat.Length == 0 ? null : cat,
                                meters = dist > 0 ? (int)Math.Round(dist) : (int?)null,
                                coord = JsonText(p, "location"),
                            });
                        }
                    }

                    var total = (int)JsonNum(root, "count");
                    return JsonSerializer.Serialize(new
                    {
                        ok = true,
                        mode,
                        center,
                        count = list.Count,
                        total,
                        list,
                        note = list.Count == 0
                            ? "没找到：换个关键词，或把半径扩到 3000 米再试。"
                            : "坐标是 GCJ-02，可直接给 amap_route 续路线；要更多就换关键词或扩半径。",
                    });
                }
                catch (Exception ex)
                {
                    AmapLog("poi -> " + ex);
                    return JsonSerializer.Serialize(new { ok = false, message = "没找到：" + AmapBrief(ex) });
                }
            },
            """
            {"type":"object","properties":{"mode":{"type":"string","enum":["around","text"],"description":"around=按中心点照周边；text=城市内文字检索"},"keywords":{"type":"string","description":"检索词，如「火锅」「便利店」；text 模式必给"},"types":{"type":"string","description":"高德分类码（如 050500 餐饮），可选"},"location":{"type":"string","description":"around 的中心点：地名或经纬度"},"city":{"type":"string","description":"text 的城市；around 的地名消歧也可带"},"radius":{"type":"integer","description":"周边半径 20..50000 米，默认 1000"}}}
            """);
    }

    // ==================== 内件 ====================

    /// <summary>Key 谁的都行：服务里那份（设置页填的）优先，退回环境变量与 Preferences。</summary>
    private static string AmapKeyOrNull()
    {
        try
        {
            var services = ServicesHelper.GetServices();
            var imp = services?.GetService(typeof(MapImportService)) as MapImportService;
            var k = imp?.AmapKey;
            if (!string.IsNullOrWhiteSpace(k)) return k.Trim();
        }
        catch { /* 服务没起来就走兜底 */ }

        var env = Environment.GetEnvironmentVariable("WAB_AMAP_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        try { return Microsoft.Maui.Storage.Preferences.Default.Get(AmapKeyPref, "") ?? ""; }
        catch { return ""; }
    }

    /// <summary>地名/经纬度 → 高德坐标「lng,lat」。经纬度当场认不联网；地名走 v3 地理编码。</summary>
    private static async Task<string> AmapResolvePointAsync(string text, string city, string key)
    {
        if (TryParseLngLat(text, out var lngLat)) return lngLat;

        var url = $"https://restapi.amap.com/v3/geocode/geo?address={Uri.EscapeDataString(text)}"
            + $"&key={Uri.EscapeDataString(key)}&output=JSON";
        if (city.Length > 0) url += "&city=" + Uri.EscapeDataString(city);

        var json = await AmapFetchAsync(url);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (JsonText(root, "status") != "1")
            throw new InvalidOperationException("高德没接单：" + JsonText(root, "info"));
        if (!root.TryGetProperty("geocodes", out var codes) || codes.ValueKind != JsonValueKind.Array || codes.GetArrayLength() == 0)
            throw new InvalidOperationException($"没找到「{text}」：带上城市名，或直接给经纬度「lng,lat」。");
        var loc = JsonText(codes[0], "location");
        if (loc.Length == 0)
            throw new InvalidOperationException($"「{text}」没算出坐标：换个更完整的名字。");
        return loc;
    }

    /// <summary>「116.48,39.99」「116.48 39.99」「39.99，116.48」都认，统一成经度在前。</summary>
    private static bool TryParseLngLat(string text, out string lngLat)
    {
        lngLat = "";
        var parts = (text ?? "").Split(new[] { ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        if (!double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var a)
            || !double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var b)) return false;

        // 高德的宇宙：经度约 73..135、纬度约 18..54。顺序猜错就翻过来。
        double l = a, t = b;
        if (b > 90) { l = b; t = a; }                              // 反着给「39.99,116.48」
        else if (a >= 18 && a <= 54 && (b < 18 || b > 54)) { l = b; t = a; }

        if (l < -180 || l > 180 || t < -90 || t > 90) return false;
        lngLat = Fmt(l) + "," + Fmt(t);
        return true;
    }

    /// <summary>一条连接池 + 字节封顶的取回：超了直接报「不像正常响应」，不把内存吃穿。</summary>
    private static async Task<string> AmapFetchAsync(string url)
    {
        using var resp = await AmapNet.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        await using var s = await resp.Content.ReadAsStreamAsync();
        using var ms = new MemoryStream();
        var buf = new byte[8192];
        while (true)
        {
            var n = await s.ReadAsync(buf);
            if (n == 0) break;
            if (ms.Length + n > MaxAmapBytes) throw new InvalidOperationException("响应超了 4MB，不像正常的数据");
            ms.Write(buf, 0, n);
        }
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    // ---- 小工具：解析高德返回 / 对模型说话 ----

    /// <summary>元素取字符串；数值也转串（高德有的字段两个形态都出）。</summary>
    private static string JsonText(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            _ => "",
        };
    }

    private static double JsonNum(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.String => double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0,
            JsonValueKind.Number => v.GetDouble(),
            _ => 0,
        };
    }

    private static string Fmt(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    private static string AmapBrief(Exception ex)
    {
        var msg = ex.Message ?? ex.GetType().Name;
        return msg.Length <= 220 ? msg : msg[..220] + "…";
    }

    private static void AmapLog(string line) => App.WriteLog("ToolManager.amap -> " + line);
}
