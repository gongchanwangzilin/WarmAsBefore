using System.Diagnostics;
using System.Text.Json;

namespace WarmAsBefore.Modules.RealWorld;

/// <summary>一次定位结果：城市（含真实坐标）、「它是怎么来的」，以及它是不是只是估算。</summary>
/// <param name="AccuracyMeters">设备定位给出的误差半径（米）；IP 估算没有这个值，为 null。</param>
/// <param name="IsEstimate">true 表示坐标不是设备实测，而是城市级估算——界面必须明确提示，不能当成精确位置用。</param>
public sealed record LocationFix(CityHit City, string Origin, double? AccuracyMeters = null, bool IsEstimate = false);

/// <summary>
/// 城市定位：设备定位（Android GPS / Windows 定位服务）优先，IP 反查只作「估算」单独提供。
/// 为什么不自动降级：IP 反查只到城市级，经常落到邻近大城市（人在漯河、IP 却被判到郑州），
/// 以前静默采用就等于骗用户；现在设备定位失败时把原因写进 <see cref="LastDeviceError"/>，
/// 由界面问用户「要不要接受城市级估算」，用户自己决定点不点。
/// </summary>
public sealed class LocationService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private readonly PermissionBroker _permissions;

    public LocationService(PermissionBroker permissions) => _permissions = permissions;

    /// <summary>最近一次设备定位失败的人话原因（权限被拒 / 系统定位关闭 / 超时 / 无硬件）；成功后清空。</summary>
    public string LastDeviceError { get; private set; } = "";

    /// <summary>
    /// 这台机器有没有可能拿到设备定位。Windows 桌面恒为 true：没有 GPS 也能靠 Wi-Fi/网络定位，
    /// 真正不可用时由 <see cref="LastDeviceError"/> 告诉用户去系统设置里打开。
    /// </summary>
    public bool DeviceLocationLikelySupported
    {
        get
        {
#if WINDOWS
            return true;
#elif ANDROID
            try
            {
                // 有 GPS 或网络定位任一，就有机会拿到设备坐标；两个都没有才提示「无硬件」。
                var pm = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.PackageManager;
                return pm?.HasSystemFeature(Android.Content.PM.PackageManager.FeatureLocationGps) == true
                    || pm?.HasSystemFeature(Android.Content.PM.PackageManager.FeatureLocationNetwork) == true;
            }
            catch { return true; }
#else
            return true;
#endif
        }
    }

    /// <summary>
    /// 只走设备定位：Android 用 GPS/网络定位，Windows 用系统定位服务。
    /// 失败返回 null 并把原因写进 <see cref="LastDeviceError"/>，绝不降级 IP。
    /// </summary>
    public async Task<LocationFix?> LocateDeviceAsync()
    {
        LastDeviceError = "";
        var sw = Stopwatch.StartNew();
        try
        {
#if WINDOWS
            // Windows 走原生 Geolocator：能拿到明确的 access 状态，才能告诉用户「去设置里打开定位」。
            var (windowsFix, windowsError) = await Platforms.Windows.WindowsDeviceLocation.TryGetAsync();
            if (windowsFix is null)
            {
                LastDeviceError = string.IsNullOrWhiteSpace(windowsError)
                    ? Platforms.Windows.WindowsDeviceLocation.UnavailableMessage
                    : windowsError;
                App.WriteLog($"LocationService.Device(Windows) 失败：{LastDeviceError} 耗时 {sw.ElapsedMilliseconds}ms");
                return null;
            }
            App.WriteLog($"LocationService.Device(Windows) 成功：{windowsFix.Latitude:0.0000},{windowsFix.Longitude:0.0000}" +
                $" 精度 {(windowsFix.AccuracyMeters is double wa ? wa.ToString("0") + "m" : "未知")}" +
                $" 来源 {windowsFix.Source} 耗时 {sw.ElapsedMilliseconds}ms");
            return FromDeviceCoordinates(windowsFix.Latitude, windowsFix.Longitude, windowsFix.AccuracyMeters);
#else
            // Android：先权限（带原因），再考虑「够新的最后已知位置」，最后才主动请求一次高精度定位。
            var (granted, reason) = await _permissions.RequestLocationDetailedAsync();
            if (!granted)
            {
                LastDeviceError = reason;
                App.WriteLog("LocationService.Device 权限未通过：" + reason);
                return null;
            }

            // 最后已知位置只有 2 分钟内的才复用：够新，又不必为了一个城市名白等 12 秒。
            var last = await Geolocation.GetLastKnownLocationAsync();
            if (last is not null && DateTimeOffset.Now - last.Timestamp <= TimeSpan.FromMinutes(2))
            {
                App.WriteLog($"LocationService.Device 复用最后已知位置：{last.Latitude:0.0000},{last.Longitude:0.0000}" +
                    $" 精度 {FormatAccuracy(last.Accuracy)} 时间 {last.Timestamp:HH:mm:ss} 耗时 {sw.ElapsedMilliseconds}ms");
                return FromDeviceCoordinates(last.Latitude, last.Longitude, last.Accuracy);
            }

            var now = await Geolocation.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(12)));
            if (now is null)
            {
                LastDeviceError = "定位超时（12 秒内没有拿到坐标）：请到开阔处，或确认系统定位已开启，然后重试。";
                App.WriteLog($"LocationService.Device 12 秒内没有坐标，耗时 {sw.ElapsedMilliseconds}ms");
                return null;
            }
            App.WriteLog($"LocationService.Device(GPS) 成功：{now.Latitude:0.0000},{now.Longitude:0.0000}" +
                $" 精度 {FormatAccuracy(now.Accuracy)} 耗时 {sw.ElapsedMilliseconds}ms");
            return FromDeviceCoordinates(now.Latitude, now.Longitude, now.Accuracy);
#endif
        }
        catch (FeatureNotSupportedException ex)
        {
            LastDeviceError = "这台设备没有可用的定位硬件（GPS/网络定位都不可用），只能手动选城市。";
            App.WriteLog("LocationService.Device 无硬件 -> " + ex.Message);
            return null;
        }
        catch (PermissionException ex)
        {
            LastDeviceError = "系统拒绝了定位权限：到 系统设置 → 应用 → 温暖如初 → 权限 里允许「位置」；然后重试。";
            App.WriteLog("LocationService.Device 权限异常 -> " + ex.Message);
            return null;
        }
        catch (OperationCanceledException ex)
        {
            // MAUI 的部分实现在超时/取消时抛这个，而不是返回 null。
            LastDeviceError = "定位超时或已取消：请到开阔处，或确认系统定位已开启，然后重试。";
            App.WriteLog("LocationService.Device 超时/取消 -> " + ex.Message);
            return null;
        }
        catch (TimeoutException ex)
        {
            LastDeviceError = "定位超时：系统在限定时间内没有返回坐标，请稍后重试。";
            App.WriteLog("LocationService.Device 超时 -> " + ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            LastDeviceError = "设备定位失败：" + ex.Message + "。可到系统设置检查定位权限后重试。";
            App.WriteLog("LocationService.Device -> " + ex);
            return null;
        }
    }

    /// <summary>
    /// 按公网 IP 估一个城市：只到城市级，误差几十公里很常见（人在漯河被判到郑州就是这样），
    /// 所以返回 IsEstimate=true，Origin 里写清楚这是估算。失败返回 null。
    /// </summary>
    public async Task<LocationFix?> LocateByIpAsync()
    {
        var city = await FromIpAsync();
        if (city is null) return null;
        return new LocationFix(city, IpEstimateOrigin, null, true);
    }

    private const string IpEstimateOrigin = "IP 估算（城市级，可能偏差几十公里）";

    /// <summary>旧入口（保留兼容）：现在等价于设备定位，失败返回 null，不会再偷偷降级到 IP。</summary>
    /// <remarks>
    /// 设置页请改用 <see cref="LocateDeviceAsync"/> + <see cref="LocateByIpAsync"/>：
    /// 设备失败时把 <see cref="LastDeviceError"/> 告诉用户，再由用户决定要不要接受 IP 估算。
    /// </remarks>
    public Task<LocationFix?> LocateAsync() => LocateDeviceAsync();

    /// <summary>设备定位只给坐标：城市名用内置城市表回推，坐标一律用实测值覆盖（天气按坐标取，名字只给人确认）。</summary>
    private static LocationFix FromDeviceCoordinates(double lat, double lon, double? accuracy)
    {
        var near = CityCatalog.Nearest(lat, lon);
        return new LocationFix(near with { Latitude = lat, Longitude = lon }, "设备定位", accuracy, false);
    }

    private static string FormatAccuracy(double? meters) =>
        meters is double m && m > 0 ? m.ToString("0") + "m" : "未知";

    /// <summary>IP 反查：先 ipwho.is，失败再 ipapi.co。两家都只到城市级。</summary>
    private async Task<CityHit?> FromIpAsync()
    {
        var city = await TryIpWhoIsAsync();
        if (city is not null) return city;
        return await TryIpApiCoAsync();
    }

    /// <summary>ipwho.is（免密钥，?lang=zh-CN 让城市名/省名回中文）。</summary>
    private async Task<CityHit?> TryIpWhoIsAsync()
    {
        try
        {
            var json = await _http.GetStringAsync("https://ipwho.is/?lang=zh-CN");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False) return null;
            if (!Num(root, "latitude", out var lat) || !Num(root, "longitude", out var lon)) return null;
            return BuildCity(Str(root, "city"), Str(root, "region"), lat, lon);
        }
        catch (Exception ex)
        {
            App.WriteLog("LocationService.Ip(ipwho.is) -> " + ex.Message);
            return null;
        }
    }

    /// <summary>ipapi.co（第二候选；限流或失败时静默放弃，由调用方决定怎么提示用户）。</summary>
    private async Task<CityHit?> TryIpApiCoAsync()
    {
        try
        {
            var json = await _http.GetStringAsync("https://ipapi.co/json/");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var err) && err.ValueKind is JsonValueKind.True) return null;
            if (!Num(root, "latitude", out var lat) || !Num(root, "longitude", out var lon)) return null;
            return BuildCity(Str(root, "city"), Str(root, "region"), lat, lon);
        }
        catch (Exception ex)
        {
            App.WriteLog("LocationService.Ip(ipapi.co) -> " + ex.Message);
            return null;
        }
    }

    /// <summary>名字优先用接口给的 city（更贴近当地叫法）；接口没给就用内置城市表按坐标回推。</summary>
    private static CityHit BuildCity(string name, string admin, double lat, double lon)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            var near = CityCatalog.Nearest(lat, lon);
            return near with { Latitude = lat, Longitude = lon };
        }
        return new CityHit
        {
            Name = name,
            Admin = admin,
            Latitude = lat,
            Longitude = lon,
        };
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
