#if ANDROID
using Microsoft.Maui.Storage;

namespace WarmAsBefore.Modules.Tools.Sandbox;

/// <summary>
/// Android 平台层：把打进 APK assets 的 proot 二进制复制到可执行目录。
/// APK 内 asset 解压到 /data 后没有执行位（SELinux 策略），
/// 必须复制到 App 私有目录再 chmod +x。
/// </summary>
public static class SandboxNativeHelper
{
    /// <summary>沙箱原生文件根目录（App 私有）：{AppData}/WarmAsBefore/sandbox-native/。</summary>
    public static string NativeDir =>
        Path.Combine(FileSystem.AppDataDirectory, "WarmAsBefore", "sandbox-native");

    /// <summary>
    /// 从 assets 把 proot / loader / loader32 / libtalloc.so 复制到 NativeDir，
    /// 赋 +x 权限。幂等：文件已存在且大小一致则跳过。
    /// 返回 (成功, 说明)。
    /// </summary>
    public static async Task<(bool Ok, string Note)> EnsureCopiedAsync()
    {
        try
        {
            var ctx = Microsoft.Maui.Platform.AppContext ?? global::Android.App.Application.Context;
            if (ctx is null)
                return (false, "无法获取 Android Context");

            Directory.CreateDirectory(NativeDir);

            // proot（NDK 重编版）动态依赖（全部放 sandbox-native/，proot 通过 LD_LIBRARY_PATH 找到）：
            //   libtalloc.so.2       ← proot GNUmakefile 的 -ltalloc
            //   libandroid-shmem.so  ← /dev/ashmem 匿名共享内存池（仅 sysvipc_shm 扩展用到）
            //   libtermux-exec.so    ← Termux 的 execve 替换库（proot 里 execve 拦截走它）
            // loader/loader32 已内嵌进 proot 二进制（PROOT_UNBUNDLE_LOADER 未定义时的默认行为），
            // 运行时 proot 自己从临时目录解压，无需单独复制。
            var files = new[]
            {
                "proot-sandbox/proot",
                "proot-sandbox/libtalloc.so.2",
                "proot-sandbox/libtermux-exec.so",
                "proot-sandbox/libandroid-shmem.so",
            };
            foreach (var assetName in files)
            {
                var fileName = assetName[(assetName.LastIndexOf('/') + 1)..];
                var dest = Path.Combine(NativeDir, fileName);
                long expectedSize;
                using (var check = ctx.GetAssets().Open(assetName))
                {
                    expectedSize = check.Length;
                }
                if (File.Exists(dest) && new FileInfo(dest).Length == expectedSize)
                    continue;

                await using var input = await ctx.GetAssets().OpenAsync(assetName);
                await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write,
                    FileShare.None, 81920, useAsync: true);
                await input.CopyToAsync(fs);
            }

            // APK asset 没有 +x 位，需要显式 chmod。
            // Android 上 .NET 的 Process.Start 能调系统 /system/bin/chmod。
            // 只给宿主直接 exec 的 proot 加 +x；loader/loader32/.so 由 proot 内部调用/加载，不需要 +x
            foreach (var name in new[] { "proot" })
            {
                await ChmodExecAsync(name);
            }

            return (true, "原生沙箱文件已就绪 → " + NativeDir);
        }
        catch (Exception ex)
        {
            App.WriteLog("SandboxNativeHelper.EnsureCopiedAsync -> " + ex);
            return (false, ex.Message);
        }
    }

    private static async Task ChmodExecAsync(string name)
    {
        var path = Path.Combine(NativeDir, name);
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/system/bin/chmod",
                Arguments = "+x \"" + path + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null)
            {
                App.WriteLog("chmod " + name + " -> Process.Start returned null");
                return;
            }
            var err = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            if (p.ExitCode != 0)
                App.WriteLog("chmod " + name + " exit=" + p.ExitCode + " " + err);
        }
        catch (Exception ex)
        {
            App.WriteLog("SandboxNativeHelper.Chmod(" + name + ") -> " + ex);
        }
    }
}
#else
// 非 Android 平台：无原生沙箱，所有方法直接返回空实现
public static class SandboxNativeHelper
{
    public static string NativeDir => "";
    public static Task<(bool Ok, string Note)> EnsureCopiedAsync()
        => Task.FromResult((false, "仅 Android 支持"));
}
#endif
