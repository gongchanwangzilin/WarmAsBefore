using System.Globalization;
using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Modules.RealWorld;

/// <summary>天气数据源标识。预设两家免密钥服务，外加一个 Open-Meteo 兼容的自定义端点。</summary>
public static class WeatherSources
{
    public const string OpenMeteo = "open-meteo";
    public const string Wttr = "wttr";
    public const string Custom = "custom";

    /// <summary>无法识别的值一律按默认源处理：设置文件手改坏、旧版本没这个字段都不能让天气消失。</summary>
    public static string Normalize(string? value) => value switch
    {
        Wttr => Wttr,
        Custom => Custom,
        _ => OpenMeteo,
    };

    public static bool IsKnown(string? value) =>
        !string.IsNullOrWhiteSpace(value) && (value == OpenMeteo || value == Wttr || value == Custom);
}

/// <summary>数据源下拉项：Code 落盘，Display 只给界面看。</summary>
public sealed record WeatherSourceChoice(string Code, string Display);

/// <summary>
/// 天气源的纯逻辑部分：拼请求地址、解响应、把各家天气码归一到同一套 condition/description。
/// 拆出来是为了让 <see cref="WeatherProvider"/> 只管取数，也方便离线单测。
/// </summary>
public static class WeatherFormat
{
    private const string OpenMeteoApi = "https://api.open-meteo.com";
    private const string OpenMeteoGeo = "https://geocoding-api.open-meteo.com";

    private static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// 预报请求地址。自定义源允许填基址（自动补 /v1/forecast），也允许直接填完整端点。
    /// 基址为空或不像地址时退回官方端点，免得拼出相对 URL 让 HttpClient 抛异常。
    /// </summary>
    public static string ForecastUrl(string? source, string? customBase, double lat, double lon)
    {
        var s = WeatherSources.Normalize(source);
        if (s == WeatherSources.Wttr)
            return $"https://wttr.in/{N(lat)},{N(lon)}?format=j1&lang=zh";
        return OpenMeteoLike(customBase, $"latitude={N(lat)}&longitude={N(lon)}&current_weather=true&timezone=auto");
    }

    /// <summary>
    /// 城市搜索的请求候选（按顺序试）。自定义源大概率是官方接口的镜像，先试它自己的
    /// /v1/search；再回落到公共地理编码——两条都失败时界面上仍有内置热门城市可点。
    /// </summary>
    public static string[] GeoSearchUrls(string? source, string? customBase, string keyword, int count)
    {
        var q = $"name={Uri.EscapeDataString(keyword)}&count={count}&language=zh&format=json";
        var custom = WeatherSources.Normalize(source) == WeatherSources.Custom;
        if (!custom || string.IsNullOrWhiteSpace(customBase)) return [$"{OpenMeteoGeo}/v1/search?{q}"];
        var baseRoot = RootOf(customBase!);
        return baseRoot.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? [$"{baseRoot}/v1/search?{q}", $"{OpenMeteoGeo}/v1/search?{q}"]
            : [$"{OpenMeteoGeo}/v1/search?{q}"];
    }

    /// <summary>从自定义基址取出「协议+主机+路径前缀」，去掉用户可能已经写上的 /v1/forecast 尾巴。</summary>
    public static string RootOf(string baseUrl)
    {
        var b = (baseUrl ?? "").Trim().TrimEnd('/');
        if (b.Length == 0 || !b.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return OpenMeteoApi;
        var cut = b.IndexOf("/v1", StringComparison.OrdinalIgnoreCase);
        if (cut >= 0) b = b[..cut].TrimEnd('/');
        if (b.Length == 0) b = OpenMeteoApi;
        return b;
    }

    private static string OpenMeteoLike(string? customBase, string query)
    {
        var root = RootOf(customBase ?? "");
        return $"{root}/v1/forecast?{query}";
    }

    /// <summary>解析响应；拿不到必要字段返回 null，由调用方决定兜底。</summary>
    public static WeatherReading? Parse(string? source, string json, string city)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return WeatherSources.Normalize(source) == WeatherSources.Wttr
                ? FromWttr(doc.RootElement, city)
                : FromOpenMeteo(doc.RootElement, city);
        }
        catch { return null; }   // 响应结构不认识 = 当作取不到，交给调用方的兜底逻辑
    }

    /// <summary>解析地理编码响应（Open-Meteo 协议：results[] 里取 name/admin1/latitude/longitude）。</summary>
    public static List<CityHit> ParseGeo(string json)
    {
        var list = new List<CityHit>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                return list;
            foreach (var r in results.EnumerateArray())
            {
                var name = Str(r, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!Num(r, "latitude", out var lat) || !Num(r, "longitude", out var lon)) continue;
                var admin = Str(r, "admin1");
                var country = Str(r, "country");
                var detail = string.Join(" ", new[] { admin, country }.Where(x => !string.IsNullOrWhiteSpace(x)));
                list.Add(new CityHit { Name = name.Trim(), Admin = detail.Trim(), Latitude = lat, Longitude = lon });
            }
        }
        catch { /* 解析不出来的响应按「没有结果」处理，界面上仍有内置城市可点 */ }
        return list;
    }

    private static WeatherReading? FromOpenMeteo(JsonElement root, string city)
    {
        // current_weather 是老字段、current 是新字段：镜像/代理实现不一定都支持，按能读到的那个走。
        var node = root.TryGetProperty("current_weather", out var cw) ? cw
            : root.TryGetProperty("current", out var c) ? c : default;
        if (node.ValueKind != JsonValueKind.Object) return null;
        var hasCode = Num(node, "weathercode", out var code) || Num(node, "weather_code", out code);
        var hasTemp = Num(node, "temperature", out var temp) || Num(node, "temperature_2m", out temp);
        if (!hasCode || !hasTemp) return null;
        return new WeatherReading
        {
            Condition = ConditionOf((int)code),
            Description = DescriptionOf((int)code),
            Celsius = temp,
            City = string.IsNullOrWhiteSpace(city) ? "未知" : city,
        };
    }

    private static WeatherReading? FromWttr(JsonElement root, string city)
    {
        if (!root.TryGetProperty("current_condition", out var arr) || arr.ValueKind != JsonValueKind.Array
            || arr.GetArrayLength() == 0) return null;
        var cur = arr[0];
        // wttr 的天气码是它自己的一套（116/122/296…），不按码表映射，直接用它给的文字更稳。
        var desc = (FirstOfArr(cur, "lang_zh") ?? FirstOfArr(cur, "weatherDesc") ?? "").Trim();
        if (desc.Length == 0 || !Num(cur, "temp_C", out var temp)) return null;
        var condition = ConditionFromText(desc);
        return new WeatherReading
        {
            Condition = condition,
            // 实测加了 lang=zh，wttr 的 lang_zh 经常还是原文，所以没中文字就用 condition 反查中文
            Description = desc.Any(IsCjk) ? desc : ChineseOf(condition),
            Celsius = temp,
            City = string.IsNullOrWhiteSpace(city) ? "未知" : city,
        };
    }

    private static bool IsCjk(char c) => c is >= '一' and <= '鿿';

    /// <summary>condition → 中文短描述（主界面天气角标显示）。</summary>
    public static string ChineseOf(string condition) => condition switch
    {
        "clear" => "晴朗", "mostly-clear" => "大部晴朗", "partly-cloudy" => "多云", "overcast" => "阴天",
        "foggy" => "雾", "drizzle" => "毛毛雨", "rain" => "雨", "showers" => "阵雨",
        "snow" => "雪", "storm" => "雷暴", _ => "多云",
    };

    private static string? FirstOfArr(JsonElement node, string prop) =>
        node.TryGetProperty(prop, out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() > 0
            ? a[0].TryGetProperty("value", out var v) ? v.GetString() : null
            : null;

    private static string Str(JsonElement node, string prop) =>
        node.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool Num(JsonElement node, string prop, out double value)
    {
        value = 0;
        if (!node.TryGetProperty(prop, out var v)) return false;
        if (v.ValueKind == JsonValueKind.Number) { value = v.GetDouble(); return true; }
        // wttr 把数值型字段也写成字符串（"296"、"12.5"），两种都得接。
        return v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(),
            NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>WMO 天气码 → condition（场景天气门控用的英文标签）。</summary>
    public static string ConditionOf(int c) => c switch
    {
        0 => "clear", 1 => "mostly-clear", 2 => "partly-cloudy", 3 => "overcast",
        >= 45 and <= 48 => "foggy", >= 51 and <= 57 => "drizzle",
        >= 61 and <= 67 => "rain", >= 71 and <= 77 => "snow",
        >= 80 and <= 82 => "showers", >= 85 and <= 86 => "snow",
        >= 95 => "storm", _ => "cloudy"
    };

    /// <summary>WMO 天气码 → 中文描述（主界面天气角标显示的就是它）。</summary>
    public static string DescriptionOf(int c) => c switch
    {
        0 => "晴朗", 1 => "大部晴朗", 2 => "多云", 3 => "阴天",
        >= 45 and <= 48 => "雾", >= 51 and <= 57 => "毛毛雨",
        >= 61 and <= 67 => "雨", >= 71 and <= 77 => "雪",
        >= 80 and <= 82 => "阵雨", >= 85 and <= 86 => "阵雪",
        >= 95 => "雷暴", _ => "多云"
    };

    /// <summary>
    /// 任意来源的天气文字 → condition：按关键词归类，判断顺序即优先级
    /// （「雷阵雨」既是雨也是雷，门控按雷暴放行更符合直觉，所以雷先判）。
    /// </summary>
    public static string ConditionFromText(string? text)
    {
        var t = (text ?? "").ToLowerInvariant();
        return t switch
        {
            _ when Has(t, "雷", "暴", "thunder", "storm") => "storm",
            _ when Has(t, "雪", "冰雹", "sleet", "hail", "snow", "blizzard") => "snow",
            _ when Has(t, "毛毛", "细雨", "drizzle") => "drizzle",
            _ when Has(t, "雨", "rain", "shower") => "rain",
            _ when Has(t, "雾", "霾", "烟", "沙尘", "fog", "mist", "haze", "dust", "sand") => "foggy",
            _ when Has(t, "阴", "overcast") => "overcast",
            _ when Has(t, "多云", "partly", "cloud") => "partly-cloudy",
            _ when Has(t, "晴", "clear", "sunny") => "clear",
            _ => "cloudy"
        };
    }

    private static bool Has(string t, params string[] words)
    {
        foreach (var w in words) if (t.Contains(w, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
