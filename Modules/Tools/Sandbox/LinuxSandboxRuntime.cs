#if ANDROID
using Microsoft.Maui.Storage;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Tools.Sandbox;

/// <summary>
/// Linux 用户态沙箱运行时（Android）：
/// 宿主用 proot（ptrace 拦截 syscall，无需 root）把 guest rootfs 投射到 App 私有目录，
/// 在沙箱内启动 guest 的 python / java 解释器，宿主 Process 的 stdin/stdout 走 JSON-RPC。
///
/// 目录结构（全部在 App 私有 /data/data/{pkg}/files 下）：
///   {AppData}/WarmAsBefore/sandbox-native/   ← 宿主 exec 的 proot/loader/libtalloc（已 chmod +x）
///   {AppData}/WarmAsBefore/sandbox-rootfs/   ← Debian/guest 根文件系统（含 python3 / java）
///   {AppData}/WarmAsBefore/tools/           ← 插件工具目录（宿主 tools/ 在 guest 里 bind 到 /tools）
/// </summary>
public sealed class LinuxSandboxRuntime
{
    private readonly SettingsManager _settings;

    public LinuxSandboxRuntime(SettingsManager settings)
    {
        _settings = settings;
    }

    public string NativeDir => SandboxNativeHelper.NativeDir;
    /// <summary>guest rootfs 根目录。</summary>
    public string RootfsDir => Path.Combine(FileSystem.AppDataDirectory, "WarmAsBefore", "sandbox-rootfs");
    /// <summary>宿主工具目录（guest 里 bind 到 /tools）。</summary>
    public string HostToolsDir => Path.Combine(FileSystem.AppDataDirectory, "WarmAsBefore", "tools");

    /// <summary>rootfs 是否已装好。</summary>
    public bool RootfsReady =>
        Directory.Exists(RootfsDir)
        && File.Exists(Path.Combine(RootfsDir, ".sandbox-ready"));

    /// <summary>把 proot/loader/libtalloc.so 从 assets 复制到可执行目录。返回 (成功, 说明)。</summary>
    public Task<(bool Ok, string Note)> EnsureNativeAsync()
        => SandboxNativeHelper.EnsureCopiedAsync();

    /// <summary>
    /// 构造「proot 起 guest 解释器」的宿主 Process 启动参数 (exe, args)。
    /// 找不到 rootfs 时返回 null（先跑 RootfsInstaller 装好）。
    /// </summary>
    public async Task<(string Exe, string Args)?> BuildInterpreterAsync(string language)
    {
        if (!RootfsReady)
            return null;
        if (!await NativeDirReady())
            return null;

        Directory.CreateDirectory(HostToolsDir);
        var native = NativeDir;
        var rootfs = RootfsDir;
        var interp = language switch
        {
            "python" => "/usr/bin/python3",
            "java" => "/usr/bin/java",
            _ => throw new ArgumentException("unknown language: " + language)
        };
        // proot -r <rootfs> -b <host>:/tools -0 -e LD_LIBRARY_PATH=<native> -- <interp 绝对路径在 guest 内>
        // 注意：interp 是 guest 内路径（proot -r 之后视角）；
        // 工具脚本在 /tools/{name}/{entry}（宿主 tools/ 目录 bind 到 /tools）
        var args =
            $"-r \"{rootfs}\""
            + $" -b \"{HostToolsDir}\":/tools"
            + " -0"
            + $" -e LD_LIBRARY_PATH=\"{native}\""
            + " -e HOME=/root"
            + $" -- \"{interp}\"";
        return (Path.Combine(native, "proot"), args);
    }

    /// <summary>构造 proot 诊断命令（如 "python3 --version"），用于验证沙箱可用。</summary>
    public async Task<(string Exe, string Args)?> BuildDiagnosticAsync(string guestShellCmd)
    {
        if (!RootfsReady)
            return null;
        if (!await NativeDirReady())
            return null;
        Directory.CreateDirectory(HostToolsDir);
        var native = NativeDir;
        var args =
            $"-r \"{RootfsDir}\""
            + $" -b \"{HostToolsDir}\"" + ":/tools"
            + " -0"
            + $" -e LD_LIBRARY_PATH=\"{native}\""
            + $" -- /bin/sh -c \"{guestShellCmd}\"";
        return (Path.Combine(native, "proot"), args);
    }

    private async Task<bool> NativeDirReady()
    {
        if (Directory.Exists(NativeDir) && File.Exists(Path.Combine(NativeDir, "proot")))
            return true;
        var (ok, _) = await SandboxNativeHelper.EnsureCopiedAsync();
        return ok && File.Exists(Path.Combine(NativeDir, "proot"));
    }

    /// <summary>
    /// 在宿主上跑一次 guest 命令（不通过 ToolSession，直接等退出）。
    /// 用于安装向导里"验证沙箱"。返回 (退出码, 标准输出)。
    /// </summary>
    public async Task<(int Code, string Out)> RunGuestCommandAsync(string exe, string args, int timeoutSec = 30)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return (-1, "无法启动进程");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { p.Kill(true); } catch { }
                return (-1, "超时（" + timeoutSec + "s）");
            }
            var outText = await outTask;
            var errText = await errTask;
            var text = (outText + "\n" + errText).Trim();
            // proot 在缺 lib 或 seccomp 限制时会非 0 退出；把 stderr 也带上方便诊断
            return (p.ExitCode, text.Length <= 4000 ? text : text[..4000]);
        }
        catch (Exception ex)
        {
            App.WriteLog("LinuxSandboxRuntime.RunGuestCommandAsync -> " + ex);
            return (-1, ex.Message);
        }
    }
}
#else
// Windows / 非 Android 平台：无 Linux 沙箱，提供空 stub 让跨平台引用编译通过。
public sealed class LinuxSandboxRuntime
{
    public LinuxSandboxRuntime(WarmAsBefore.Services.SettingsManager settings) { }
    public string NativeDir => "";
    public string RootfsDir => "";
    public string HostToolsDir => "";
    public bool RootfsReady => false;
    public Task<(bool Ok, string Note)> EnsureNativeAsync()
        => Task.FromResult((false, "仅 Android 支持"));
    public Task<(string Exe, string Args)?> BuildInterpreterAsync(string language)
        => Task.FromResult<(string Exe, string Args)?>(null);
    public Task<(string Exe, string Args)?> BuildDiagnosticAsync(string cmd)
        => Task.FromResult<(string Exe, string Args)?>(null);
    public Task<(int Code, string Out)> RunGuestCommandAsync(string exe, string args, int timeoutSec = 30)
        => Task.FromResult((0, ""));
}
#endif
