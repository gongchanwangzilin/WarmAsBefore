#if WINDOWS
using Windows.Devices.Geolocation;

namespace WarmAsBefore.Platforms.Windows;

/// <summary>
/// Windows 桌面定位辅助：直接用 Windows.Devices.Geolocation，而不是 MAUI 的 Geolocation 封装。
/// 原因：无 MSIX 的 EXE 里 MAUI 封装对「系统定位关闭 / 没允许桌面应用访问位置」往往只返回 null，
/// 用户看不到任何指引；这里能拿到明确的 access 状态和异常，才能把「去哪里打开」写清楚。
/// </summary>
internal static class WindowsDeviceLocation
{
    /// <summary>系统定位关闭或桌面应用没有位置权限时的统一指引（设置页会原样展示）。</summary>
    public const string UnavailableMessage =
        "Windows 定位不可用：到 设置 → 隐私和安全性 → 位置 打开定位服务，并允许桌面应用访问位置；然后重试。";

    /// <summary>授权状态不明确（无 MSIX 的桌面应用常见）导致最终取不到坐标时的提示。</summary>
    private const string UnspecifiedMessage =
        "Windows 定位不可用：系统没有返回定位授权状态（桌面应用常见）。到 设置 → 隐私和安全性 → 位置 打开定位服务，并允许桌面应用访问位置；然后重试。";

    private const string TimeoutMessage =
        "Windows 定位超时（12 秒内没有拿到坐标）：到 设置 → 隐私和安全性 → 位置 确认定位服务已开启，稍等几秒再重试。";

    /// <summary>一次 Windows 设备定位结果：坐标、误差（米）与来源，仅用于写日志。</summary>
    internal sealed record Fix(double Latitude, double Longitude, double? AccuracyMeters, string Source);

    /// <summary>
    /// 取一次系统定位。任何失败都返回 (null, 人话原因)，不抛异常——调用方只负责把原因展示给用户。
    /// 追求 High 精度、1 秒上报间隔，最多接受 3 分钟内的缓存坐标，12 秒拿不到就算超时。
    /// </summary>
    internal static async Task<(Fix? Fix, string Error)> TryGetAsync()
    {
        // 非 null 表示「授权状态不明确 / 查询授权失败」：先继续尝试实际取坐标，
        // 真取不到时用它作为 LastDeviceError，避免把可用的情况误判成不可用。
        string? accessNote = null;

        try
        {
            // RequestAccessAsync 要求从 UI 线程发起；从 UI 命令进来时 InvokeOnMainThreadAsync 会直接执行。
            var access = await Microsoft.Maui.ApplicationModel.MainThread
                .InvokeOnMainThreadAsync<GeolocationAccessStatus>(async () => await Geolocator.RequestAccessAsync());

            switch (access)
            {
                case GeolocationAccessStatus.Allowed:
                    break;
                case GeolocationAccessStatus.Denied:
                    // 明确的拒绝：直接按系统设置指引返回，不浪费用户 12 秒。
                    return (null, UnavailableMessage);
                default:
                    // Unspecified：无 MSIX 的桌面应用拿不到明确授权状态，但系统级「允许桌面应用访问位置」
                    // 打开时定位实际仍然可能可用，所以记下原因后继续尝试，而不是在这里就宣告失败。
                    accessNote = UnspecifiedMessage;
                    break;
            }
        }
        catch (Exception ex)
        {
            // 部分 Windows 版本上，无 MSIX 的 EXE 调 RequestAccessAsync 会直接抛异常
            // （如 0x80070490 Element not found / E_ACCESSDENIED）。这不等于定位一定不可用：
            // 只要系统允许桌面应用访问位置，直接取坐标仍能成功，所以继续尝试。
            WarmAsBefore.App.WriteLog("WindowsDeviceLocation.RequestAccessAsync 异常 -> " + ex.Message);
            accessNote = UnavailableMessage;
        }

        try
        {
            var geolocator = new Geolocator
            {
                DesiredAccuracy = PositionAccuracy.High,
                ReportInterval = 1000,
            };

            var position = await geolocator.GetGeopositionAsync(
                TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(12));
            var point = position?.Coordinate?.Point?.Position;
            if (point is not { } p)
                return (null, accessNote ?? "Windows 定位失败：系统返回了空坐标，请稍后重试。");

            var accuracy = position!.Coordinate.Accuracy;
            return (new Fix(p.Latitude, p.Longitude, accuracy > 0 ? accuracy : null, "Windows 定位服务"), "");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || (uint)ex.HResult == 0x80070005)
        {
            // 系统定位服务关闭 / 桌面应用被禁止访问位置时，会以「拒绝访问」(E_ACCESSDENIED) 的形式冒出来。
            WarmAsBefore.App.WriteLog("WindowsDeviceLocation 拒绝访问 -> " + ex.Message);
            return (null, accessNote ?? UnavailableMessage);
        }
        catch (Exception ex) when (ex is OperationCanceledException || ex is TimeoutException)
        {
            WarmAsBefore.App.WriteLog("WindowsDeviceLocation 超时 -> " + ex.Message);
            return (null, accessNote ?? TimeoutMessage);
        }
        catch (Exception ex)
        {
            WarmAsBefore.App.WriteLog("WindowsDeviceLocation.TryGetAsync -> " + ex);
            return (null, accessNote
                ?? "Windows 定位失败：" + ex.Message + "。如果定位服务已关闭，到 设置 → 隐私和安全性 → 位置 打开定位服务，并允许桌面应用访问位置；然后重试。");
        }
    }
}
#endif
