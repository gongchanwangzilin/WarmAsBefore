using Microsoft.Win32;

namespace WarmAsBefore.Services;

/// <summary>
/// 显卡偏好：给本程序在 Windows 的「图形设置」里登记「高性能（独显）」。
///
/// 说明几点实话：
/// · 这个开关对「界面构建慢」没有帮助——那部分耗时在 CPU 的 UI 线程，GPU 是闲着的；
///   它真正影响的是玻璃模糊、大图缩放这类合成工作，以及某些老驱动下的掉帧。
/// · Windows 的应用级偏好写在 HKCU\Software\Microsoft\DirectX\UserGpuPreferences，
///   值名是 exe 的完整路径，值形如 "GpuPreference=2;"（2 = 高性能，1 = 省电）。
/// · 注册表按路径记：换过安装/构建目录后，旧路径那一条会留在 Windows 的列表里，
///   于是「开关关着、Windows 却仍显示高性能」。这里把同名 exe 的所有登记都算作本程序，
///   关闭时一律清掉，只保留当前路径那一条（见 <see cref="Reconcile"/>）。
/// · 登记后需要重启程序才生效（Windows 在进程启动时决定用哪块卡）。
/// 仅 Windows 有效，其它平台是空实现。
/// </summary>
public static class GpuPreferenceService
{
    private const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private const string HighPerf = "GpuPreference=2;";
    private const string PowerSaving = "GpuPreference=1;";

    /// <summary>当前程序（含同名 exe 的旧路径）是否已登记为「高性能」。</summary>
    public static bool IsHighPerformancePreferred()
    {
#if WINDOWS
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key is null) return false;
            var exe = ExecutablePath();
            foreach (var name in key.GetValueNames())
            {
                if (!IsOurApp(name, exe)) continue;
                if (key.GetValue(name) is string value
                    && value.Contains("GpuPreference=2", StringComparison.Ordinal))
                    return true;
            }
            return false;
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

    /// <summary>给设置页显示的一行状态：与注册表里的真实登记一致，不跟开关的意图走。</summary>
    public static string DescribeState() => IsHighPerformancePreferred()
        ? "当前已登记为「高性能」（重启程序后生效）"
        : "当前由 Windows 自动选择显卡";

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

            RemoveOurs(key, exe, keepCurrent: enabled);
            if (enabled) key.SetValue(exe, HighPerf, RegistryValueKind.String);

            // 以注册表的实际结果回话：删不掉/写不进时，状态行不能替 Windows 说"已关"。
            if (IsHighPerformancePreferred() != enabled)
                return (false, "显卡偏好没有保存成功，Windows 仍按原设置选择显卡。");

            return (true, enabled
                ? "已登记为「高性能」。Windows 在进程启动时才决定用哪块显卡，需要重启程序生效。"
                : "已移除登记（含旧路径的残留），恢复由 Windows 自动选择。");
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

    /// <summary>
    /// 启动时按设置把注册表校正一次：开着就确保当前 exe 已登记并清掉旧路径，
    /// 关着就清掉本程序的全部登记。设置是主人的意图，注册表只是它的影子。
    /// </summary>
    public static void Reconcile(bool enabled)
    {
#if WINDOWS
        try
        {
            var exe = ExecutablePath();
            if (!enabled)
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
                if (key is null) return;
                if (RemoveOurs(key, exe, keepCurrent: false) > 0)
                    App.WriteLog("GpuPreference.Reconcile -> 开关为关，已清掉残留的高性能登记");
                return;
            }

            using var write = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            if (write is null) return;
            RemoveOurs(write, exe, keepCurrent: true);
            if (write.GetValue(exe) is not string cur
                || !cur.Contains("GpuPreference=2", StringComparison.Ordinal))
                write.SetValue(exe, HighPerf, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            App.WriteLog("GpuPreference.Reconcile -> " + ex.Message);
        }
#endif
    }

#if WINDOWS
    /// <summary>删掉本程序（同名 exe）的登记；<paramref name="keepCurrent"/> 时留下当前路径那一条。</summary>
    private static int RemoveOurs(RegistryKey key, string exe, bool keepCurrent)
    {
        var removed = 0;
        foreach (var name in key.GetValueNames())
        {
            if (!IsOurApp(name, exe)) continue;
            if (keepCurrent && name.Equals(exe, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                key.DeleteValue(name, throwOnMissingValue: false);
                removed++;
            }
            catch (Exception ex)
            {
                App.WriteLog($"GpuPreference.RemoveOurs({name}) -> {ex.Message}");
            }
        }
        return removed;
    }

    /// <summary>注册表里的这条是不是本程序：按 exe 文件名认，换过目录的旧条目也算。</summary>
    private static bool IsOurApp(string registryName, string exe)
    {
        try
        {
            var a = Path.GetFileName(registryName);
            var b = Path.GetFileName(exe);
            return a.Length > 0 && a.Equals(b, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
#endif

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
