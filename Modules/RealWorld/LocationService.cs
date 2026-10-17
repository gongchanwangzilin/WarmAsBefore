using System.Text.Json;

namespace WarmAsBefore.Modules.RealWorld;

/// <summary>一次定位结果：城市（含坐标）与「它是怎么来的」，界面据此写提示。</summary>
public sealed record LocationFix(CityHit City, string Origin);

/// <summary>
/// 城市定位与搜索。定位走设备 GPS，拿不到时降级用公网 IP 估算——
/// Windows 端是无 MSIX 打包的 EXE，系统定位经常不可用，只靠 GPS 这个按钮就基本是摆设。
/// </summary>
public sealed class LocationService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private readonly PermissionBroker _permissions;

    public LocationService(PermissionBroker permissions) => _permissions = permissions;

    public async Task<LocationFix?> LocateAsync()
    {
        var gps = await FromDeviceAsync();
        if (gps is not null)
        {
            // GPS 只给经纬度，名字回推表内最近城市：天气按坐标查，名字只是给人确认用的。
            var near = CityCatalog.Nearest(gps.Lat, gps.Lon);
            return new LocationFix(near with { Latitude = gps.Lat, Longitude = gps.Lon }, "设备定位");
        }
        var ip = await FromIpAsync();
        return ip is null ? null : new LocationFix(ip, "IP 估算");
    }

    private sealed record GeoPoint(double Lat, double Lon);

    /// <summary>设备坐标；权限被拒、无 GPS 硬件或服务未开启时返回 null（不抛异常，交给 IP 降级）。</summary>
    private async Task<GeoPoint?> FromDeviceAsync()
    {
        try
        {
            if (!await _permissions.RequestLocation()) return null;
            var last = await Geolocation.GetLastKnownLocationAsync();
            if (last is not null) return new GeoPoint(last.Latitude, last.Longitude);
            var now = await Geolocation.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(6)));
            return now is null ? null : new GeoPoint(now.Latitude, now.Longitude);
        }
        catch (Exception ex)
        {
            App.WriteLog("LocationService.Gps -> " + ex.Message);
            return null;
        }
    }

    /// <summary>公网 IP 反查城市（ipwho.is，免密钥）。名字缺失时同样回推内置城市表。</summary>
    private async Task<CityHit?> FromIpAsync()
    {
        try
        {
            var json = await _http.GetStringAsync("https://ipwho.is/?lang=zh-CN");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False) return null;
            if (!Num(root, "latitude", out var lat) || !Num(root, "longitude", out var lon)) return null;
            var name = Str(root, "city");
            if (string.IsNullOrWhiteSpace(name))
            {
                var near = CityCatalog.Nearest(lat, lon);
                return near with { Latitude = lat, Longitude = lon };
            }
            return new CityHit
            {
                Name = name,
                Admin = Str(root, "region"),
                Latitude = lat,
                Longitude = lon,
            };
        }
        catch (Exception ex)
        {
            App.WriteLog("LocationService.Ip -> " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 城市搜索联想：内置表先给一批（离线也能点），再补在线地理编码的结果。
    /// source/customBase 传进来是为了让自定义源优先走自己的镜像，公共接口只作回落。
    /// </summary>
    public async Task<List<CityHit>> SearchAsync(string keyword, string? source = null, string? customBase = null)
    {
        var merged = CityCatalog.Match(keyword);
        var k = (keyword ?? "").Trim();
        if (k.Length == 0) return merged;
        foreach (var url in WeatherFormat.GeoSearchUrls(source, customBase, k, 8))
        {
            List<CityHit> remote;
            try { remote = WeatherFormat.ParseGeo(await _http.GetStringAsync(url)); }
            catch { continue; }
            foreach (var c in remote)
                if (!merged.Any(m => m.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase))) merged.Add(c);
            break;
        }
        return merged.Take(12).ToList();
    }

    private static string Str(JsonElement node, string prop) =>
        node.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool Num(JsonElement node, string prop, out double value)
    {
        value = 0;
        if (!node.TryGetProperty(prop, out var v)) return false;
        if (v.ValueKind == JsonValueKind.Number) { value = v.GetDouble(); return true; }
        return v.ValueKind == JsonValueKind.String
            && double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}
