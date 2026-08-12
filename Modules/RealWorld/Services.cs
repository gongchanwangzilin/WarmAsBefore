using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Modules.RealWorld;

public sealed class WeatherProvider
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private string _city = "";
    private double _lat = 39.9042;
    private double _lon = 116.4074;
    private bool _geoOk;

    public string City => _city;

    /// <summary>设置城市名（首次拉取天气时自动地理编码）。</summary>
    public void SetCity(string? city)
    {
        var c = (city ?? "").Trim();
        if (c == _city) return;
        _city = c;
        _geoOk = false;
    }

    public async Task<WeatherReading?> Fetch(string? city = null)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(city)) SetCity(city);
            await EnsureGeoAsync();
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={_lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}&longitude={_lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}&current_weather=true&timezone=auto";
            var resp = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(resp);
            var cw = doc.RootElement.GetProperty("current_weather");
            var code = cw.GetProperty("weathercode").GetInt32();
            return new WeatherReading
            {
                Condition = CodeToCondition(code),
                Description = CodeToDesc(code),
                Celsius = cw.GetProperty("temperature").GetDouble(),
                City = string.IsNullOrEmpty(_city) ? "北京" : _city
            };
        }
        catch
        {
            return new WeatherReading { Condition = "clear", Celsius = 22, City = string.IsNullOrEmpty(_city) ? "未知" : _city };
        }
    }

    private async Task EnsureGeoAsync()
    {
        if (_geoOk) return;
        if (string.IsNullOrWhiteSpace(_city)) { _geoOk = true; return; }
        try
        {
            var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(_city)}&count=1&language=zh&format=json";
            var resp = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(resp);
            if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
            {
                var r = results[0];
                _lat = r.GetProperty("latitude").GetDouble();
                _lon = r.GetProperty("longitude").GetDouble();
                if (r.TryGetProperty("name", out var n)) _city = n.GetString() ?? _city;
            }
        }
        catch { /* 保持默认坐标 */ }
        _geoOk = true;
    }

    private static string CodeToCondition(int c) => c switch
    {
        0 => "clear", 1 => "mostly-clear", 2 => "partly-cloudy", 3 => "overcast",
        >= 45 and <= 48 => "foggy", >= 51 and <= 57 => "drizzle",
        >= 61 and <= 67 => "rain", >= 71 and <= 77 => "snow",
        >= 80 and <= 82 => "showers", >= 95 => "storm", _ => "cloudy"
    };

    private static string CodeToDesc(int c) => c switch
    {
        0 => "晴朗", 1 => "大部晴朗", 2 => "多云", 3 => "阴天",
        >= 45 and <= 48 => "雾", >= 51 and <= 57 => "毛毛雨",
        >= 61 and <= 67 => "雨", >= 71 and <= 77 => "雪",
        >= 80 and <= 82 => "阵雨", >= 95 => "雷暴", _ => "多云"
    };
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
    private int _cycleLen = 28;
    private int _periodLen = 5;

    public void SetCycle(int cycleLen, int periodLen)
    {
        _cycleLen = Math.Clamp(cycleLen, 7, 60);
        _periodLen = Math.Clamp(periodLen, 1, _cycleLen);
    }

    public int Day { get => _day; set => _day = value % _cycleLen; }
    public string Phase => _day switch
    {
        _ when _day < _periodLen => "period",
        _ when _day < 14 => "follicular",
        _ when _day < 16 => "ovulation",
        _ => "luteal"
    };
    public bool InPeriod => _day < _periodLen;
    public string Label => Phase switch
    {
        "period" => "经期",
        "follicular" => "卵泡期",
        "ovulation" => "排卵期",
        "luteal" => "黄体期",
        _ => "正常"
    };
}