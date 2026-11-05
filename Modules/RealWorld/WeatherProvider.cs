using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Modules.RealWorld;


public sealed class WeatherProvider
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
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
    }

    /// <summary>设置城市名（没有坐标的旧数据走这条路，首次拉取天气时自动地理编码）。</summary>
    public void SetCity(string? city)
    {
        var c = (city ?? "").Trim();
        if (c == _city) return;
        _city = c;
        _hasCoord = false;
        _geoDone = false;
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

        HasMapWeatherOverride = true;
        var c = (city ?? "").Trim();
        if (lat is double la && lon is double lo && !(Math.Abs(la) < 1e-6 && Math.Abs(lo) < 1e-6))
        {
            // 有坐标就直接用，城市名只是显示用：抓来的地图中心点比地名准得多。
            _mapLat = la;
            _mapLon = lo;
            _mapCity = c;
            _mapHasCoord = true;
            _mapGeoDone = true;
        }
        else
        {
            _mapCity = c;
            _mapHasCoord = false;
            _mapGeoDone = false;   // 只有城市名：首次查询时再解析一次
        }
    }

    /// <summary>撤回地图天气覆盖（回到用户位置）。</summary>
    public void ClearMapWeather()
    {
        HasMapWeatherOverride = false;
        _mapCity = "";
        _mapLat = 0;
        _mapLon = 0;
        _mapHasCoord = false;
        _mapGeoDone = false;
    }

    // ==================== 查询 ====================

    /// <summary>
    /// 旧调用点的入口：有地图覆盖时按覆盖位置查，否则按用户位置。
    /// 传了 city（旧签名）仍按「临时改成这个城市」处理 —— 那是老行为，不跟地图覆盖抢。
    /// </summary>
    public async Task<WeatherReading?> Fetch(string? city = null)
    {
        if (!string.IsNullOrWhiteSpace(city))
        {
            SetCity(city);
            return await FetchForUserAsync().ConfigureAwait(false);
        }
        return HasMapWeatherOverride
            ? await FetchAtAsync(useMap: true).ConfigureAwait(false)
            : await FetchForUserAsync().ConfigureAwait(false);
    }

    /// <summary>设置页「测试天气」用：永远按用户自己在设置里选的城市 / 坐标查，忽略地图覆盖。</summary>
    public async Task<WeatherReading?> FetchForUserAsync() =>
        await FetchAtAsync(useMap: false).ConfigureAwait(false);

    private async Task<WeatherReading?> FetchAtAsync(bool useMap)
    {
        if (useMap)
        {
            // 覆盖位置地理编码不出来（只填了城市名又查不到）：退回用户位置，别拿 0,0 去问天气。
            if (!await EnsureMapGeoAsync().ConfigureAwait(false))
                return await FetchAtAsync(useMap: false).ConfigureAwait(false);
        }
        else
        {
            await EnsureUserGeoAsync().ConfigureAwait(false);
        }

        var city = useMap ? _mapCity : _city;
        var lat = useMap ? _mapLat : _lat;
        var lon = useMap ? _mapLon : _lon;
        try
        {
            var url = WeatherFormat.ForecastUrl(_source, _baseUrl, lat, lon);
            var json = await GetAsync(url, _source == WeatherSources.Custom).ConfigureAwait(false);
            var reading = WeatherFormat.Parse(_source, json, city);
            if (reading is not null) return reading;
        }
        catch (Exception ex)
        {
            App.WriteLog("WeatherProvider.Fetch" + (useMap ? "(map)" : "") + " -> " + ex.Message);
        }
        return new WeatherReading { Condition = "clear", Celsius = 22, City = string.IsNullOrEmpty(city) ? "未知" : city };
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
