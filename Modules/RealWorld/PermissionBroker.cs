using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Modules.RealWorld;


public sealed class PermissionBroker
{
    public async Task<bool> RequestLocation()
    {
        var s = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (s == PermissionStatus.Granted) return true;
        s = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        return s == PermissionStatus.Granted;
    }

    /// <summary>
    /// 带原因的位置权限请求：成功给 (true, "")，失败给一句人话原因，设置页原样显示。
    /// 定位失败时用户最需要分清「权限被拒」还是「系统定位关了」——裸 bool 说不清楚，
    /// 这也是 Windows 端以前只能静默降级 IP 的原因之一。
    /// </summary>
    public async Task<(bool granted, string reason)> RequestLocationDetailedAsync()
    {
        try
        {
            var s = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (s == PermissionStatus.Granted) return (true, "");
            s = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            return s switch
            {
                PermissionStatus.Granted => (true, ""),
                PermissionStatus.Denied => (false, "定位权限被拒绝：到 系统设置 → 应用 → 温暖如初 → 权限 里允许「位置」；然后重试。"),
                PermissionStatus.Disabled => (false, "系统定位服务已关闭：到 系统设置 打开「位置信息」；然后重试。"),
                PermissionStatus.Restricted => (false, "定位权限被系统限制（设备管理/家长控制策略），当前无法使用设备定位。"),
                _ => (false, $"系统没有返回定位权限状态（{s}）：请到 系统设置 手动允许「位置」权限后重试。"),
            };
        }
        catch (PermissionException ex)
        {
            return (false, "定位权限请求异常：" + ex.Message + "。请到 系统设置 手动允许「位置」权限后重试。");
        }
        catch (Exception ex)
        {
            // 权限框架本身出问题（例如 Android 版本不支持该权限组）也不能把定位按钮整个打挂。
            return (false, "定位权限检查失败：" + ex.Message);
        }
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
