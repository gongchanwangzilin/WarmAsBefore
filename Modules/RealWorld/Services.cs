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

    public string City => _city;

    /// <summary>
    /// 下发数据源配置。密钥只在请求自定义源那一条地址上带（不进了 <see cref="HttpClient"/> 的默认头），
    /// 否则同一条 Key 会被连带发给地理编码与 wttr 这些第三方主机。
    /// </summary>
    public void Configure(string? source, string? baseUrl, string? apiKey)
    {
        _source = WeatherSources.Normalize(source);
        _baseUrl = (baseUrl ?? "").Trim();
        _apiKey = (apiKey ?? "").Trim();
        _geoDone = false;   // 换源之后按新源重新解析一次城市名
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

    public async Task<WeatherReading?> Fetch(string? city = null)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(city)) SetCity(city);
            await EnsureGeoAsync();
            var url = WeatherFormat.ForecastUrl(_source, _baseUrl, _lat, _lon);
            var json = await GetAsync(url, _source == WeatherSources.Custom);
            var reading = WeatherFormat.Parse(_source, json, _city);
            if (reading is not null) return reading;
        }
        catch (Exception ex)
        {
            App.WriteLog("WeatherProvider.Fetch -> " + ex.Message);
        }
        return new WeatherReading { Condition = "clear", Celsius = 22, City = string.IsNullOrEmpty(_city) ? "未知" : _city };
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

    private async Task EnsureGeoAsync()
    {
        if (_hasCoord || _geoDone) return;
        if (string.IsNullOrWhiteSpace(_city)) { _geoDone = true; return; }
        foreach (var url in WeatherFormat.GeoSearchUrls(_source, _baseUrl, _city, 1))
        {
            try
            {
                var hits = WeatherFormat.ParseGeo(await _http.GetStringAsync(url));
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
}

public sealed class TimeProvider
{
    public TimeOfDayInfo Now() => new() { Now = DateTime.Now, Holiday = GetHoliday() };

    private static string? GetHoliday() => DateTime.Now switch
    {
        { Month: 1, Day: 1 } => "元旦",
        { Month: 2, Day: 14 } => "情人节",
        { Month: 3, Day: 8 } => "妇女节",
        { Month: 5, Day: 1 } => "劳动节",
        { Month: 10, Day: 1 } => "国庆节",
        { Month: 12, Day: 25 } => "圣诞节",
        _ => null
    };
}

public sealed class PermissionBroker
{
    public async Task<bool> RequestLocation()
    {
        var s = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (s == PermissionStatus.Granted) return true;
        s = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        return s == PermissionStatus.Granted;
    }

    public async Task<bool> RequestCamera()
    {
        var s = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (s == PermissionStatus.Granted) return true;
        s = await Permissions.RequestAsync<Permissions.Camera>();
        return s == PermissionStatus.Granted;
    }

    public async Task<bool> RequestMic()
    {
        var s = await Permissions.CheckStatusAsync<Permissions.Microphone>();
        if (s == PermissionStatus.Granted) return true;
        s = await Permissions.RequestAsync<Permissions.Microphone>();
        return s == PermissionStatus.Granted;
    }
}

public sealed class PhysiologicalTracker
{
    private int _day;
    private bool _tracked = true;
    private int _cycleLen = 28;
    private int _periodLen = 5;

    /// <summary>按当前角色的性别下发规则：没有周期的性别直接把 Tracked 关掉，
    /// 下面三个读数（Phase / InPeriod / Label）全部退回「无」，谁拿它去拼界面或提示词都不会冒出经期。</summary>
    public void SetCycle(CycleRule rule)
    {
        _tracked = rule.Enabled;
        _cycleLen = Math.Clamp(rule.CycleLength, 7, 60);
        _periodLen = Math.Clamp(rule.PeriodLength, 1, _cycleLen);
    }

    public bool Tracked => _tracked;
    public int Day { get => _day; set => _day = value % _cycleLen; }
    public string Phase => !_tracked ? "normal" : _day switch
    {
        _ when _day < _periodLen => "period",
        _ when _day < 14 => "follicular",
        _ when _day < 16 => "ovulation",
        _ => "luteal"
    };
    public bool InPeriod => _tracked && _day < _periodLen;
    public string Label => !_tracked ? "" : Phase switch
    {
        "period" => "经期",
        "follicular" => "卵泡期",
        "ovulation" => "排卵期",
        "luteal" => "黄体期",
        _ => "正常"
    };
}