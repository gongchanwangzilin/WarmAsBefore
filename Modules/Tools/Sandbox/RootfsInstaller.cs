#if ANDROID
using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Maui.Storage;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Tools.Sandbox;

/// <summary>
/// Linux 沙箱 rootfs 安装器（Android）：
/// 下载 Python（python-build-standalone，glibc aarch64）与 Temurin JRE（aarch64 Linux）
/// 解压到 guest rootfs 目录，建好 /usr/bin/python3、/usr/bin/java 入口。
/// 进度经 IProgress&lt;string&gt; 上报，供设置页安装向导实时显示。
/// </summary>
public sealed class RootfsInstaller
{
    private readonly LinuxSandboxRuntime _sandbox;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(20) };

    // glibc 版 aarch64 静态 Python（indygreg 构建，免编译）
    private const string PythonTarUrl =
        "https://github.com/astral-sh/python-build-standalone/releases/download/20240810/cpython-3.12.7+20240814-aarch64-unknown-linux-gnu-lto-install_only.tar.gz";
    // Temurin JRE 21 aarch64 Linux
    private const string JreTarUrl =
        "https://api.adoptium.net/v3/binary/latest/21/ga/linux/aarch64/jre/hotspot/normal/eclipse";

    public RootfsInstaller(LinuxSandboxRuntime sandbox) => _sandbox = sandbox;

    public string RootfsDir => _sandbox.RootfsDir;
    public bool IsInstalled => _sandbox.RootfsReady;

    /// <summary>安装 rootfs + 解释器。Python 与 Java 相互独立，单侧失败不影响另一侧。</summary>
    public async Task<(bool Ok, string Note)> InstallAsync(IProgress<string>? progress = null)
    {
        var dest = RootfsDir;
        Directory.CreateDirectory(Path.Combine(dest, "usr", "bin"));
        Directory.CreateDirectory(Path.Combine(dest, "usr", "local"));
        Directory.CreateDirectory(Path.Combine(dest, "root"));
        Directory.CreateDirectory(Path.Combine(dest, "tmp"));
        Directory.CreateDirectory(Path.Combine(dest, "etc"));

        var notes = new List<string>();

        progress?.Report("下载 Python 3.12（aarch64 glibc）…");
        var pyOk = await Task.Run(() => InstallPythonSafeAsync(dest, progress, notes));

        progress?.Report("下载 Java 21 JRE（aarch64）…");
        var jvOk = await Task.Run(() => InstallJavaSafeAsync(dest, progress, notes));

        // 标记 rootfs 就绪（.sandbox-ready 哨兵）
        File.WriteAllText(Path.Combine(dest, ".sandbox-ready"),
            $"python={pyOk} java={jvOk} at={DateTime.UtcNow:o}");

        var ok = pyOk && jvOk;
        if (pyOk) notes.Insert(0, "Python 3.12 已安装");
        if (jvOk) notes.Insert(0, "Java 21 JRE 已安装");
        progress?.Report(ok ? "安装完成" : "部分失败");
        return (ok, string.Join("；", notes));
    }

    private Task<bool> InstallPythonSafeAsync(string dest, IProgress<string>? progress, List<string> notes)
        => Task.Run(async () =>
        {
            try { await InstallPythonAsync(dest, progress); return true; }
            catch (Exception ex)
            {
                App.WriteLog("RootfsInstaller.InstallPython -> " + ex);
                notes.Add("Python 安装失败：" + ex.Message);
                return false;
            }
        });

    private Task<bool> InstallJavaSafeAsync(string dest, IProgress<string>? progress, List<string> notes)
        => Task.Run(async () =>
        {
            try { await InstallJavaAsync(dest, progress); return true; }
            catch (Exception ex)
            {
                App.WriteLog("RootfsInstaller.InstallJava -> " + ex);
                notes.Add("Java 安装失败：" + ex.Message);
                return false;
            }
        });

    /// <summary>下载并解压 Python（install_only 布局直接就是 bin/lib/include 树）。</summary>
    private async Task InstallPythonAsync(string rootfsDir, IProgress<string>? progress)
    {
        var pyDir = Path.Combine(rootfsDir, "usr", "local", "python");
        Directory.CreateDirectory(pyDir);

        var tmp = Path.Combine(Path.GetTempPath(), "wab-sandbox-py.tar.gz");
        await DownloadToFileAsync(PythonTarUrl, tmp, progress);

        await ExtractTarGzAsync(tmp, pyDir);
        File.Delete(tmp);

        // guest 里 /usr/bin/python3 → /usr/local/python/bin/python3
        LinkIntoGuestUsrBin(rootfsDir, Path.Combine(pyDir, "bin"), "python3");
    }

    /// <summary>下载并解压 Temurin JRE（zip 根目录带版本号子目录，铺平后 bin/java 直接可用）。</summary>
    private async Task InstallJavaAsync(string rootfsDir, IProgress<string>? progress)
    {
        var javaDir = Path.Combine(rootfsDir, "usr", "lib", "jvm", "temurin21");
        Directory.CreateDirectory(javaDir);

        var tmp = Path.Combine(Path.GetTempPath(), "wab-sandbox-jre.bin");
        await DownloadToFileAsync(JreTarUrl, tmp, progress);

        // Temurin linux 包是 zip 格式（不是 tar.gz）
        Directory.CreateDirectory(javaDir);
        await using var zip = ZipFile.OpenRead(tmp);
        foreach (var entry in zip.Entries)
        {
            var entryPath = SanitizeTarget(javaDir, entry.FullName);
            if (entryPath is null) continue;
            if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(entryPath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(entryPath)!);
                await using var es = entry.Open();
                await using var dst = File.Create(entryPath);
                await es.CopyToAsync(dst);
            }
        }
        File.Delete(tmp);

        // zip 里有一层 "jre-21.x.x" 子目录，铺平
        var nested = Directory.GetDirectories(javaDir)
            .FirstOrDefault(d => File.Exists(Path.Combine(d, "bin", "java")));
        if (nested is not null && nested != javaDir)
        {
            foreach (var item in Directory.GetFileSystemEntries(nested))
            {
                var target = Path.Combine(javaDir, Path.GetFileName(item));
                DeleteRecurse(target);
                Directory.Move(item, target);
            }
            DeleteRecurse(nested);
        }

        if (!File.Exists(Path.Combine(javaDir, "bin", "java")))
            throw new Exception("JRE 解压后未找到 bin/java");

        LinkIntoGuestUsrBin(rootfsDir, Path.Combine(javaDir, "bin"), "java");
        File.WriteAllText(Path.Combine(rootfsDir, "etc", "java-home"), javaDir);
    }

    /// <summary>在 guest 的 /usr/bin 下放解释器入口（优先软链接，失败退化为复制）。</summary>
    private static void LinkIntoGuestUsrBin(string rootfsDir, string binDir, string name)
    {
        var src = Path.Combine(binDir, name);
        if (!File.Exists(src)) return;
        var link = Path.Combine(rootfsDir, "usr", "bin", name);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try
        {
            var relative = Path.GetRelativePath(Path.GetDirectoryName(link)!, src);
            try
            {
                // .NET 的 File.CreateSymbolicLink(path, pathToTarget)：path 是要创建的链接，第二个是目标
                File.CreateSymbolicLink(link, relative);
            }
            catch (PlatformNotSupportedException)
            {
                // 运行时不支持符号链接时退化为直接复制
                App.WriteLog("CreateSymbolicLink 不可用，退化为复制");
                File.Copy(src, link, overwrite: true);
            }
            if (!File.Exists(link) && !IsSymlink(link))
                File.Copy(src, link, overwrite: true);
        }
        catch (Exception ex)
        {
            App.WriteLog("LinkIntoGuestUsrBin(" + name + ") -> " + ex);
            File.Copy(src, link, overwrite: true);
        }
    }

    /// <summary>tar.gz 解压（Android 内置 tar 依赖 API 差异，直接用 .NET 实现，只支持常规条目）。</summary>
    private static async Task ExtractTarGzAsync(string gzPath, string destDir)
    {
        await using var gz = File.OpenRead(gzPath);
        await using var tar = new GZipStream(gz, CompressionMode.Decompress, leaveOpen: true);
        await using var reader = new TarReader(tar, leaveOpen: true);
        while (await reader.GetNextEntryAsync() is { } entry)
        {
            var target = SanitizeTarget(destDir, entry.Name);
            if (target is null) continue;
            if (entry.EntryType is TarEntryType.Directory)
            {
                Directory.CreateDirectory(target);
            }
            else if (entry.DataStream is { } data)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var dst = File.Create(target);
                await data.CopyToAsync(dst);
            }
            // 符号链接等无 DataStream 的条目跳过（只还原常规文件与目录）
        }
    }

    /// <summary>防 tar slip：拒绝绝对路径与 .. 逃逸，返回安全目标路径。</summary>
    private static string? SanitizeTarget(string destDir, string entryName)
    {
        if (string.IsNullOrEmpty(entryName)) return null;
        var target = Path.GetFullPath(Path.Combine(destDir, entryName.Replace('\\', '/')));
        if (!target.StartsWith(Path.GetFullPath(destDir) + Path.DirectorySeparatorChar,
               StringComparison.OrdinalIgnoreCase) && target != Path.GetFullPath(destDir))
            return null;
        return target;
    }

    private static async Task DownloadToFileAsync(string url, string dest, IProgress<string>? progress)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength;
        // 流必须开在循环外：放循环里每轮都会重新发起请求，等于从头下载
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var fs = File.Create(dest);
        var buffer = new byte[81920];
        var read = 0L;
        while (true)
        {
            var n = await src.ReadAsync(buffer.AsMemory());
            if (n <= 0) break;
            read += n;
            await fs.WriteAsync(buffer.AsMemory(0, n));
            progress?.Report($"下载中… {read / 1024 / 1024} MB" +
                (total > 0 ? $" / {total / 1024 / 1024} MB" : ""));
        }
    }

    private static bool IsSymlink(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return (info.Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch { return false; }
    }

    private static void DeleteRecurse(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) { App.WriteLog("DeleteRecurse(" + path + ") -> " + ex); }
    }

    /// <summary>删除整个沙箱（rootfs + native）。</summary>
    public void Remove()
    {
        DeleteRecurse(RootfsDir);
        DeleteRecurse(_sandbox.NativeDir);
    }

    /// <summary>查询已安装状态（python3 / java 是否就位）。</summary>
    public string GetStatus()
    {
        if (!IsInstalled) return "未安装";
        var py = File.Exists(Path.Combine(RootfsDir, "usr", "bin", "python3")) ? "✅" : "❌";
        var jv = File.Exists(Path.Combine(RootfsDir, "usr", "bin", "java")) ? "✅" : "❌";
        return $"rootfs 已安装：python3 {py} · java {jv}";
    }
}
#else
// Windows / 非 Android 平台：无 rootfs 安装器，空 stub 让跨平台引用编译通过。
public sealed class RootfsInstaller
{
    public RootfsInstaller(LinuxSandboxRuntime sandbox) { }
    public string RootfsDir => "";
    public bool IsInstalled => false;
    public Task<(bool Ok, string Note)> InstallAsync(System.IProgress<string>? progress = null)
        => Task.FromResult((false, "仅 Android 支持"));
    public void Remove() { }
    public string GetStatus() => "未安装";
}
#endif
