using Microsoft.Win32;

namespace WarmAsBefore.Services;

/// <summary>
/// 显卡偏好：给本程序在 Windows 的「图形设置」里登记「高性能（独显）」。
///
/// 说明几点实话：
/// · 这个开关对「界面构建慢」没有帮助——那部分耗时在 CPU 的 UI 线程，GPU 是闲着的；
///   它真正影响的是玻璃模糊、大图缩放这类合成工作，以及某些老驱动下的掉帧。
/// · Windows 的应用级偏好写在 HKCU\Software\Microsoft\DirectX\UserGpuPreferences，
///   值形如 "GpuPreference=2;"（2 = 高性能，1 = 省电）。
/// · 登记后需要重启程序才生效（Windows 在进程启动时决定用哪块卡）。
/// 仅 Windows 有效，其它平台是空实现。
/// </summary>
public static class GpuPreferenceService
{
    private const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string HighPerf = "GpuPreference=2;";
    private const string PowerSaving = "GpuPreference=1;";

    /// <summary>当前程序（exe）是否已登记为「高性能」。</summary>
    public static bool IsHighPerformancePreferred()
    {
#if WINDOWS
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            var exe = ExecutablePath();
            var value = key?.GetValue(exe) as string;
            return value is not null && value.Contains("GpuPreference=2", StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            App.WriteLog("GpuPreference.IsHigh -> " + ex.Message);
            return false;
        }
#else
        return false;
#endif
    }

    /// <summary>
    /// 写入/移除偏好。返回给用户看的说明（含是否需要重启）。
    /// 写入失败不抛异常——注册表权限受限时只提示，不影响其它设置。
    /// </summary>
    public static (bool Ok, string Message) SetHighPerformance(bool enabled)
    {
#if WINDOWS
        try
        {
            var exe = ExecutablePath();
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            if (key is null) return (false, "无法写入显卡偏好（注册表不可写）。");

            if (enabled)
                key.SetValue(exe, HighPerf, RegistryValueKind.String);
            else if (key.GetValue(exe) is string cur && cur.Contains("GpuPreference=", StringComparison.Ordinal))
                key.DeleteValue(exe, throwOnMissingValue: false);

            return (true, enabled
                ? "已登记为「高性能」。Windows 在进程启动时才决定用哪块显卡，需要重启程序生效。"
                : "已移除登记，恢复由 Windows 自动选择。");
        }
        catch (Exception ex)
        {
            App.WriteLog("GpuPreference.Set -> " + ex);
            return (false, "写入失败：" + ex.Message);
        }
#else
        return (false, "只有 Windows 有这个设置。");
#endif
    }

    /// <summary>当前程序的可执行文件路径（注册表里的键就是它）。</summary>
    private static string ExecutablePath()
    {
#if WINDOWS
        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path)) return path;
#endif
        return System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "WarmAsBefore.exe";
    }
}
