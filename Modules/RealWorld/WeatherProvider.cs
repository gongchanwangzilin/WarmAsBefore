using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.RealWorld;


public sealed class WeatherProvider
{
    // 真实天气 5 分钟内基本不变，而 SceneDirector / 主界面 / 生理服务各有一分钟级定时器来问一次。
    // 不缓存的话它们会各自 HTTP + JSON 解析一遍：网络慢的时候，每次就是一次周期性小卡。
    private const int CacheTtlSeconds = 300;
    // 失败/回退的默认 reading 只缓存 30 秒：断网时别让每次定时器都重试一条 8 秒超时的请求，
    // 但网络一恢复必须能很快自愈，所以这个 TTL 要短。
    private const int FallbackTtlSeconds = 30;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    // —— 内存 TTL 缓存 + 并发去重。
    // 缓存键包含「用户位置 / 地图覆盖」两套位置状态、源、自定义地址与 Key 摘要：
    // 任何一项变了旧结果就不再可信，必须重新联网（见 InvalidateCache 的调用点）。
    private readonly object _cacheLock = new();
    private string _cacheKey = "";
    private WeatherReading? _cacheValue;
    private DateTime _cacheAtUtc;
    private int _cacheTtlSeconds;
    // 同一时刻同一查询只发一条 HTTP：后来者 await 这个 Task 共享结果，而不是各拉各的。
    private Task<WeatherReading?>? _inflight;
    private string _inflightKey = "";

    private string _city = "";
    private double _lat = 39.9042;
    private double _lon = 116.4074;
    private bool _hasCoord;      // 坐标来自「定位 / 选择城市」，直接拿去查天气
    private bool _geoDone;       // 只填过城市名的旧数据：解析尝试过一次就作罢，别每次拉天气都联网试
    private string _source = WeatherSources.OpenMeteo;
    private string _baseUrl = "";
    private string _apiKey = "";

    // —— 地图天气覆盖：与用户位置是两套独立的位置状态。
    // 分开存是必须的：主界面按「这张图的设置」查天气时，用户自己在设置里选的城市
    // 不能被覆盖掉，否则退出这张图之后就再也回不到用户天气了。
    private string _mapCity = "";
    private double _mapLat;
    private double _mapLon;
    private bool _mapHasCoord;   // 覆盖位置已有坐标（抓取时的中心点 / 调用方给的）
    private bool _mapGeoDone;    // 只给了城市名：地理编码尝试过一次就作罢

    public string City => _city;

    /// <summary>当前是否在用「地图当地 / 自定义地点」的天气（主界面按地图设置写入）。</summary>
    public bool HasMapWeatherOverride { get; private set; }

    /// <summary>
    /// 下发数据源配置。密钥只在请求自定义源那一条地址上带（不进了 <see cref="HttpClient"/> 的默认头），
    /// 否则同一条 Key 会被连带发给地理编码与 wttr 这些第三方主机。
    /// </summary>
    public void Configure(string? source, string? baseUrl, string? apiKey)
    {
        _source = WeatherSources.Normalize(source);
        _baseUrl = (baseUrl ?? "").Trim();
        _apiKey = (apiKey ?? "").Trim();
        // 换源之后两套位置都要重新解析一次：新源的地理编码地址不一样。
        _geoDone = false;
        _mapGeoDone = false;
        // 换源/换地址/换 Key：缓存键里的这几项变了，旧结果不再可信，直接作废。
        InvalidateCache();
    }

    /// <summary>设置城市名（没有坐标的旧数据走这条路，首次拉取天气时自动地理编码）。</summary>
    public void SetCity(string? city)
    {
        var c = (city ?? "").Trim();
        if (c == _city) return;
        _city = c;
        _hasCoord = false;
        _geoDone = false;
        InvalidateCache();
    }

    /// <summary>设置城市名 + 坐标（「定位」或「选择城市」拿到的结果）。</summary>
    public void SetLocation(string? city, double? lat, double? lon)
    {
        if (lat is not double la || lon is not double lo) { SetCity(city); return; }
        var c = (city ?? "").Trim();
        if (c == _city && la == _lat && lo == _lon) return;
        _city = c;
        _lat = la;
        _lon = lo;
        _hasCoord = true;
        _geoDone = true;
        InvalidateCache();
    }

    // ==================== 地图天气覆盖 ====================

    /// <summary>
    /// 主界面按地图的天气设置调用：user / 空 = 撤回覆盖、回到用户位置；
    /// map / custom = 用给定城市与坐标（没有坐标就只给城市，首次查询时地理编码）。
    /// 覆盖状态与用户位置彻底分开存，退出这张图后 Clear 一下就能回到用户天气。
    /// </summary>
    public void SetMapWeather(string? mode, string? city, double? lat, double? lon)
    {
        var m = MapWeatherModes.Normalize(mode);
        if (m == MapWeatherModes.User) { ClearMapWeather(); return; }

        // 先记下旧覆盖：进同一张图/同一地点时这个函数会被反复调用，
        // 只有位置真的变了才该让缓存作废，否则每次进图都白丢一次有效天气。
        var changed = !HasMapWeatherOverride;
        HasMapWeatherOverride = true;
        var c = (city ?? "").Trim();
        if (lat is double la && lon is double lo && !(Math.Abs(la) < 1e-6 && Math.Abs(lo) < 1e-6))
        {
            // 有坐标就直接用，城市名只是显示用：抓来的地图中心点比地名准得多。
            changed |= c != _mapCity || la != _mapLat || lo != _mapLon || !_mapHasCoord;
            _mapLat = la;
            _mapLon = lo;
            _mapCity = c;
            _mapHasCoord = true;
            _mapGeoDone = true;
        }
        else
        {
            changed |= c != _mapCity || _mapHasCoord;
            _mapCity = c;
            _mapHasCoord = false;
            _mapGeoDone = false;   // 只有城市名：首次查询时再解析一次
        }
        // 地图覆盖换地方了：上一张图的天气不能继续端给这张图。
        if (changed) InvalidateCache();
    }

    /// <summary>撤回地图天气覆盖（回到用户位置）。</summary>
    public void ClearMapWeather()
    {
        // 本来就没有覆盖时（主界面每次进图都会调一次）别碰缓存：否则每次进图都要重新联网。
        var hadOverride = HasMapWeatherOverride;
        HasMapWeatherOverride = false;
        _mapCity = "";
        _mapLat = 0;
        _mapLon = 0;
        _mapHasCoord = false;
        _mapGeoDone = false;
        // 覆盖真的撤掉了：缓存里的「上张图天气」必须作废，下一问要回到用户位置。
        if (hadOverride) InvalidateCache();
    }

    // ==================== 查询 ====================

    /// <summary>
    /// 旧调用点的入口：有地图覆盖时按覆盖位置查，否则按用户位置。
    /// 传了 city（旧签名）仍按「临时改成这个城市」处理 —— 那是老行为，不跟地图覆盖抢。
    /// </summary>
    public async Task<WeatherReading?> Fetch(string? city = null, bool force = false)
    {
        if (!string.IsNullOrWhiteSpace(city))
        {
            SetCity(city);
            return await FetchForUserAsync(force).ConfigureAwait(false);
        }
        return HasMapWeatherOverride
            ? await FetchAtAsync(useMap: true, force).ConfigureAwait(false)
            : await FetchForUserAsync(force).ConfigureAwait(false);
    }

    /// <summary>
    /// 设置页「测试天气」用：永远按用户自己在设置里选的城市 / 坐标查，忽略地图覆盖。
    /// force = true 时跳过缓存（测试要的是「此刻这个源通不通」，不是 5 分钟前的答案）。
    /// </summary>
    public async Task<WeatherReading?> FetchForUserAsync(bool force = false) =>
        await FetchAtAsync(useMap: false, force).ConfigureAwait(false);

    private async Task<WeatherReading?> FetchAtAsync(bool useMap, bool force = false)
    {
        if (useMap)
        {
            // 覆盖位置地理编码不出来（只填了城市名又查不到）：退回用户位置，别拿 0,0 去问天气。
            if (!await EnsureMapGeoAsync().ConfigureAwait(false))
                return await FetchAtAsync(useMap: false, force).ConfigureAwait(false);
        }
        else
        {
            await EnsureUserGeoAsync().ConfigureAwait(false);
        }

        // 位置快照必须在地理编码之后取：那一步可能刚把城市名/坐标改成了解析结果。
        var city = useMap ? _mapCity : _city;
        var lat = useMap ? _mapLat : _lat;
        var lon = useMap ? _mapLon : _lon;
        var key = CacheKey(useMap, city, lat, lon);

        // 命中就直接回内存里的 reading：省掉一次 HTTP、一次 JSON 解析和一次对象构造。
        // 这是「天气每分钟被三处重复拉」真正被消掉的地方，所以命中也不打日志（否则日志比请求还吵）。
        if (!force && TryGetCache(key, out var cached)) return cached;

        Task<WeatherReading?> request;
        var started = false;
        lock (_cacheLock)
        {
            // 去重：同一时刻同一位置的多处调用（主界面/场景/生理）只发一条请求，其余等这条的结果。
            if (_inflight is null || _inflightKey != key)
            {
                _inflightKey = key;
                _inflight = FetchCoreAsync(city, lat, lon, key);
                request = _inflight;
                started = true;
            }
            else
            {
                request = _inflight;
            }
        }
        // 只在真正发请求时记一行：断网时 30 秒一次、正常时 5 分钟一次，不会刷屏。
        if (started)
            App.WriteLog($"WeatherProvider.{(force ? "强制刷新" : "缓存未命中")} -> {WeatherSources.Normalize(_source)} {city} {lat:0.####},{lon:0.####}");
        return await request.ConfigureAwait(false);
    }

    /// <summary>真正的那一次请求：成功按 5 分钟缓存，失败回退的默认值只按 30 秒缓存。</summary>
    private async Task<WeatherReading?> FetchCoreAsync(string city, double lat, double lon, string key)
    {
        try
        {
            WeatherReading? reading = null;
            var ok = false;
            using (PerfLog.Measure("天气 · 联网拉取"))
            {
                try
                {
                    var url = WeatherFormat.ForecastUrl(_source, _baseUrl, lat, lon);
                    var json = await GetAsync(url, _source == WeatherSources.Custom).ConfigureAwait(false);
                    reading = WeatherFormat.Parse(_source, json, city);
                    ok = reading is not null;
                }
                catch (Exception ex)
                {
                    App.WriteLog("WeatherProvider.Fetch -> " + ex.Message);
                }
            }
            // 拿不到数据也给一份「晴朗 22°」的默认值，界面照常显示（这是原有行为），
            // 但它只缓存 30 秒：断网恢复后能很快换回真实天气。
            reading ??= new WeatherReading { Condition = "clear", Celsius = 22, City = string.IsNullOrEmpty(city) ? "未知" : city };

            lock (_cacheLock)
            {
                _cacheKey = key;
                _cacheValue = reading;
                _cacheAtUtc = DateTime.UtcNow;
                _cacheTtlSeconds = ok ? CacheTtlSeconds : FallbackTtlSeconds;
                // 只有「还是自己」才清 in-flight：中途被别的 key 顶替时别把新请求也一起清掉。
                if (_inflightKey == key) { _inflight = null; _inflightKey = ""; }
            }
            return reading;
        }
        catch (Exception ex)
        {
            // 兜底：任何意外都不能把 in-flight 留成一个坏 Task —— 那会让同 key 的后续调用永远失败。
            App.WriteLog("WeatherProvider.FetchCore -> " + ex.Message);
            lock (_cacheLock) { if (_inflightKey == key) { _inflight = null; _inflightKey = ""; } }
            return new WeatherReading { Condition = "clear", Celsius = 22, City = string.IsNullOrEmpty(city) ? "未知" : city };
        }
    }

    // ==================== 缓存键 / 失效 ====================

    /// <summary>
    /// 影响「这次天气请求的结果」的所有输入：两套位置、源、自定义地址、Key 摘要。
    /// Key 摘要用短哈希而不是明文：缓存键不进日志，也不把密钥留在内存字符串里。
    /// </summary>
    private string CacheKey(bool useMap, string city, double lat, double lon) =>
        string.Join('|',
            useMap ? "map" : "user",
            city,
            lat.ToString("F5", CultureInfo.InvariantCulture),
            lon.ToString("F5", CultureInfo.InvariantCulture),
            WeatherSources.Normalize(_source),
            _baseUrl,
            KeyDigest(_apiKey));

    private static string KeyDigest(string key)
    {
        if (key.Length == 0) return "0";
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(key), hash);
        return Convert.ToHexString(hash[..8]);
    }

    private bool TryGetCache(string key, out WeatherReading? reading)
    {
        lock (_cacheLock)
        {
            if (_cacheValue is not null && _cacheKey == key
                && (DateTime.UtcNow - _cacheAtUtc).TotalSeconds < _cacheTtlSeconds)
            {
                reading = _cacheValue;
                return true;
            }
            reading = null;
            return false;
        }
    }

    /// <summary>位置或数据源变了：旧结果立刻作废，下一次查询必须重新联网。</summary>
    private void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cacheKey = "";
            _cacheValue = null;
            _cacheAtUtc = default;
            _cacheTtlSeconds = 0;
        }
    }

    /// <summary>
    /// 给界面「看一眼某地此刻天气」用：不改任何状态，按给定城市/坐标查一次就丢。
    /// 地图创建页拿它显示「当地此刻 xx°C」，答不上来（没网、地名查不到）就返回 null，界面自己收声。
    /// </summary>
    public async Task<WeatherReading?> PeekAsync(string? city, double? lat, double? lon)
    {
        try
        {
            var la = lat ?? 0;
            var lo = lon ?? 0;
            var hasCoord = lat is double && lon is double && !(Math.Abs(la) < 1e-6 && Math.Abs(lo) < 1e-6);
            var name = (city ?? "").Trim();
            if (!hasCoord)
            {
                if (string.IsNullOrWhiteSpace(name)) return null;
                var hit = false;
                foreach (var url in WeatherFormat.GeoSearchUrls(_source, _baseUrl, name, 1))
                {
                    try
                    {
                        var hits = WeatherFormat.ParseGeo(await _http.GetStringAsync(url).ConfigureAwait(false));
                        if (hits.Count == 0) continue;
                        la = hits[0].Latitude;
                        lo = hits[0].Longitude;
                        name = hits[0].Name;
                        hit = true;
                        break;
                    }
                    catch { /* 换下一个候选地址；全部失败就当查不到 */ }
                }
                if (!hit) return null;
            }
            var json = await GetAsync(WeatherFormat.ForecastUrl(_source, _baseUrl, la, lo),
                _source == WeatherSources.Custom).ConfigureAwait(false);
            return WeatherFormat.Parse(_source, json, name);
        }
        catch (Exception ex)
        {
            App.WriteLog("WeatherProvider.Peek -> " + ex.Message);
            return null;
        }
    }

    private async Task<string> GetAsync(string url, bool withKey)
    {
        if (!withKey || _apiKey.Length == 0) return await _http.GetStringAsync(url);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");
        using var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }

    // ==================== 地理编码（两套位置各做各的） ====================

    /// <summary>用户位置：只填过城市名的旧数据，首次查询时解析一次。</summary>
    private async Task EnsureUserGeoAsync()
    {
        if (_hasCoord || _geoDone) return;
        if (string.IsNullOrWhiteSpace(_city)) { _geoDone = true; return; }
        foreach (var url in WeatherFormat.GeoSearchUrls(_source, _baseUrl, _city, 1))
        {
            try
            {
                var hits = WeatherFormat.ParseGeo(await _http.GetStringAsync(url).ConfigureAwait(false));
                if (hits.Count == 0) continue;
                _lat = hits[0].Latitude;
                _lon = hits[0].Longitude;
                _city = hits[0].Name;
                _hasCoord = true;
                _geoDone = true;
                return;
            }
            catch { /* 换下一个候选地址，全部失败则保持默认坐标 */ }
        }
        _geoDone = true;
    }

    /// <summary>地图覆盖位置：只有城市名（地图没抓坐标 / 自定义地点）时解析一次。返回有没有拿到坐标。</summary>
    private async Task<bool> EnsureMapGeoAsync()
    {
        if (_mapHasCoord || _mapGeoDone) return _mapHasCoord;
        if (string.IsNullOrWhiteSpace(_mapCity)) { _mapGeoDone = true; return false; }
        foreach (var url in WeatherFormat.GeoSearchUrls(_source, _baseUrl, _mapCity, 1))
        {
            try
            {
                var hits = WeatherFormat.ParseGeo(await _http.GetStringAsync(url).ConfigureAwait(false));
                if (hits.Count == 0) continue;
                _mapLat = hits[0].Latitude;
                _mapLon = hits[0].Longitude;
                _mapCity = hits[0].Name;
                _mapHasCoord = true;
                _mapGeoDone = true;
                return true;
            }
            catch { /* 换下一个候选地址；全部失败就当没有这个覆盖位置 */ }
        }
        _mapGeoDone = true;
        return false;
    }
}
