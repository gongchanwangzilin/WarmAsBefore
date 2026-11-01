using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Maui.Storage;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 一盏灯此刻的状态：<see cref="Kind"/> 是 <see cref="SignalKinds"/> 里的红/黄/绿，
/// <see cref="SecondsLeft"/> 是这一色还剩几秒，<see cref="Source"/> 说它是谁算出来的
/// （本地倒计时 / 高德路况），<see cref="FromLive"/> 用于「实时源断了其实退回模拟」这种场合还能被看出来。
/// </summary>
public readonly record struct SignalState(string Kind, int SecondsLeft, string Source, bool FromLive)
{
    public bool IsGreen => Kind == SignalKinds.Green;
    public bool IsRed => Kind == SignalKinds.Red;

    /// <summary>给人看的一行：「绿灯 12 秒」。</summary>
    public string Display => SignalKinds.Display(Kind) + (SecondsLeft > 0 ? $" {SecondsLeft} 秒" : "");
}

/// <summary>
/// 本地模拟的灯：按「现在几点 + 这条边自己定的周期」算，不联网、不读存档、每帧都算得起。
///
/// 一条边的循环是 [绿灯 → 黄灯 → 红灯]；绿灯亮的是 <see cref="MapEdge.SignalPhase"/>
/// 那一侧，红灯段则轮到对侧绿。没有「全红」这一档（除了双向同绿的边在红灯段真的全红）。
/// 每条边按自己的 Id 错开一个相位——不然一城的灯会在同一秒一起变脸，看着像坏了。
/// </summary>
public static class SignalClock
{
    public const int DefaultCycle = 60;
    public const int DefaultGreen = 26;
    public const int DefaultYellow = 4;

    public static SignalState Now(MapEdge e, bool forward) => At(e, forward, DateTime.Now);

    public static SignalState At(MapEdge e, bool forward, DateTime at)
    {
        var cycle = Math.Max(6, e.SignalCycle > 0 ? e.SignalCycle : DefaultCycle);
        var green = Math.Clamp(e.SignalGreen > 0 ? e.SignalGreen : DefaultGreen, 1, cycle - 2);
        var yellow = Math.Clamp(e.SignalYellow > 0 ? e.SignalYellow : DefaultYellow, 1, Math.Max(1, cycle - green - 1));
        var pos = (int)((at.TimeOfDay.TotalSeconds + OffsetOf(e.Id, cycle)) % cycle);

        // 「问的这一侧」是不是本轮绿灯的主角：双向同绿的边，两侧都是主角。
        var isPhaseSide = e.SignalPhase switch
        {
            SignalPhases.Both => true,
            SignalPhases.Reverse => !forward,
            _ => forward,
        };

        if (pos < green)
            return Side(isPhaseSide, SignalKinds.Green, SignalKinds.Red, green - pos, cycle - pos, e);
        if (pos < green + yellow)
            return Side(isPhaseSide, SignalKinds.Yellow, SignalKinds.Red, green + yellow - pos, cycle - pos, e);
        // 红灯段：主角等着，对侧这时候正好是绿的，两者都在循环末尾换班。
        return Side(isPhaseSide, SignalKinds.Red, SignalKinds.Green, cycle - pos, cycle - pos, e);
    }

    private static SignalState Side(bool askedIsPhase, string phaseState, string otherState,
        int phaseLeft, int otherLeft, MapEdge e)
        => new(askedIsPhase ? phaseState : otherState, askedIsPhase ? phaseLeft : otherLeft, "本地倒计时", false);

    /// <summary>这条边的相位偏移（秒）：由 Id 里稳定地摇出来，同一个存档每次都一样。</summary>
    private static int OffsetOf(string id, int cycle)
    {
        var h = 0;
        foreach (var ch in id) h = (h * 31 + ch) & 0x7fffffff;
        return h % cycle;
    }
}

/// <summary>
/// 红绿灯的数据源。默认只有本地模拟（<see cref="SignalClock"/>），因为高德公开接口给的是
/// 路况——畅通 / 缓行 / 拥堵——不是灯色，开着「按高德路况判灯」时把它翻译成绿 / 黄 / 红。
///
/// 这一档默认关；没配 Key、这条路没名字、断网、返回里没有路况字段，一律静默退回本地倒计时：
/// 画面不许因为一个外部接口而空转，流程图也不许因为判不出就卡住。
/// </summary>
public sealed class TrafficSignalService
{
    /// <summary>设置项：用高德路况判灯（默认关，§9 的口径）。</summary>
    public const string UseAmapPref = "map.signal.amap";

    /// <summary>同一条路的问答至少隔这么多秒：一屏十几盏灯，每帧问一次会被自己打死。</summary>
    private const int CacheSeconds = 60;

    private static readonly HttpClient Net = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(6),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        MaxConnectionsPerServer = 2,
    })
    { Timeout = TimeSpan.FromSeconds(10) };

    private readonly MapService _maps;
    private readonly MapImportService _import;
    private readonly ConcurrentDictionary<string, (DateTime At, string Kind)> _cache = new();

    public TrafficSignalService(MapService maps, MapImportService import)
    {
        _maps = maps;
        _import = import;
    }

    public static bool UseAmap => Preferences.Default.Get(UseAmapPref, false);
    public static void SetUseAmap(bool on) => Preferences.Default.Set(UseAmapPref, on);

    /// <summary>本地模拟那一档：同步、不要网络，画布与流程图判分都走它。</summary>
    public SignalState Local(MapEdge edge, bool forward) => SignalClock.Now(edge, forward);

    /// <summary>
    /// 现在这盏灯什么色。开了高德且问得着就问，问不着（没 Key / 路没名 / 断了）就退回本地，
    /// 退回是静默的：调用方拿到的永远是一个能用的状态，区别只写在 <see cref="SignalState.Source"/> 里。
    /// </summary>
    public async Task<SignalState> CurrentAsync(MapEdge edge, bool forward)
    {
        if (!UseAmap) return Local(edge, forward);
        var live = await LiveStateAsync(edge);
        return live is { } s
            ? new SignalState(s, Remaining(edge, forward), "高德路况", true)
            : Local(edge, forward);
    }

    /// <summary>流程图那侧要的只有一句话：这一侧现在能不能过。<see cref="SignalClock"/> 同步算，绝不为判断去联网。</summary>
    public bool IsGreenNow(MapEdge edge, bool forward) => SignalClock.Now(edge, forward).IsGreen;

    private int Remaining(MapEdge edge, bool forward) => SignalClock.Now(edge, forward).SecondsLeft;

    /// <summary>这条路的路况（绿/黄/红），同一条路 60 秒内只问一次；问不出来回 null。</summary>
    private async Task<string?> LiveStateAsync(MapEdge edge)
    {
        var key = _import.AmapKey;
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(edge.RoadName)) return null;
        var city = FirstCity();
        if (city.Length == 0) return null;

        var cacheKey = city + "|" + edge.RoadName;
        if (_cache.TryGetValue(cacheKey, out var hit) && (DateTime.Now - hit.At).TotalSeconds < CacheSeconds)
            return hit.Kind;

        var kind = await AskAmapAsync(city, edge.RoadName, key);
        // 问不着也记一笔（记成空串）：一盏灯每帧都撞墙比灯色不准更糟。
        _cache[cacheKey] = (DateTime.Now, kind ?? "");
        return string.IsNullOrEmpty(kind) ? null : kind;
    }

    /// <summary>城市名：导入时记下的那张图所属城市；手工地图没有，就不走高德。</summary>
    private string FirstCity()
    {
        var map = _maps.Map;
        return string.IsNullOrWhiteSpace(map.City) ? "" : map.City.Trim();
    }

    private static async Task<string?> AskAmapAsync(string city, string road, string key)
    {
        try
        {
            var url = "https://restapi.amap.com/v3/traffic/status/road"
                + "?name=" + Uri.EscapeDataString(road)
                + "&city=" + Uri.EscapeDataString(city)
                + "&key=" + Uri.EscapeDataString(key)
                + "&output=JSON";
            using var resp = await Net.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("status", out var st)
                && st.ValueKind == JsonValueKind.String && st.GetString() != "1") return null;
            if (!doc.RootElement.TryGetProperty("info", out var info)
                || info.ValueKind != JsonValueKind.String) return null;
            // 高德把路况塞在 info / description 里：畅通、缓行、拥堵、严重拥堵。
            return Translate(info.GetString());
        }
        catch
        {
            return null;      // 判不出灯色不是错误：下面自动退回本地倒计时，一张图不该为一根网线停住
        }
    }

    /// <summary>畅通=绿、缓行=黄、拥堵与严重拥堵=红；别的说法（「未知」）不算数。</summary>
    private static string? Translate(string? description) => description switch
    {
        "畅通" => SignalKinds.Green,
        "缓行" => SignalKinds.Yellow,
        "拥堵" or "严重拥堵" => SignalKinds.Red,
        _ => null,
    };

    /// <summary>把一条边的周期凑齐给人看：「60 秒一轮：绿 26 / 黄 4 / 红 30」。</summary>
    public static string CycleText(MapEdge edge)
    {
        var cycle = Math.Max(6, edge.SignalCycle > 0 ? edge.SignalCycle : SignalClock.DefaultCycle);
        var green = Math.Clamp(edge.SignalGreen > 0 ? edge.SignalGreen : SignalClock.DefaultGreen, 1, cycle - 2);
        var yellow = Math.Clamp(edge.SignalYellow > 0 ? edge.SignalYellow : SignalClock.DefaultYellow, 1, Math.Max(1, cycle - green - 1));
        return $"{cycle.ToString(CultureInfo.InvariantCulture)} 秒一轮：绿 {green} / 黄 {yellow} / 红 {cycle - green - yellow}";
    }
}
