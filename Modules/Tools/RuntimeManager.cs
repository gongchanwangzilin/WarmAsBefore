using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Tools;

/// <summary>一次运行时检测结果（供设置页展示）。</summary>
public sealed record RuntimeStatus
{
    public bool PythonOk { get; set; }
    public bool JavaOk { get; set; }
    public string PythonVersion { get; set; } = "";
    public string JavaVersion { get; set; } = "";
    public string PythonDetail { get; set; } = "";
    public string JavaDetail { get; set; } = "";
    public bool PythonAutoDownloaded { get; set; }
    public bool JavaAutoDownloaded { get; set; }
    public string WindowsOnlyNote { get; set; } = "";
}

/// <summary>
/// 运行时管理器：管理 Java / Python 两大工具运行时。
/// 策略：先检测电脑已装运行时（PATH / 用户手动配置路径）→ 引导用户 → 必要时把官方便携运行时自动下载到应用数据目录。
/// Java 与 Python 共用同一套 JSON-RPC 工具协议（ToolManager），运行时只负责「用什么解释器跑」。
/// </summary>
public sealed class RuntimeManager
{
    private readonly SettingsManager _settings;
    private static readonly HttpClient Http = new();

    public RuntimeManager(SettingsManager settings) => _settings = settings;

    /// <summary>便携运行时统一存放目录：{root}/runtimes。</summary>
    public static string RuntimesDir => Path.Combine(App.RootDirectory, "runtimes");

    public bool IsDesktop => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    // ============ 用户手动配置的路径（持久化到 UserSettings） ============

    public string ManualPythonPath => _settings.Current.PythonPath ?? "";
    public string ManualJavaPath => _settings.Current.JavaPath ?? "";

    public void SetManualPythonPath(string? value) => UpdateSetting(s => s with { PythonPath = NormalizeDir(value) });
    public void SetManualJavaPath(string? value) => UpdateSetting(s => s with { JavaPath = NormalizeDir(value) });

    private static string NormalizeDir(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        try { return Path.GetFullPath(value.Trim().Trim('"').Trim()); }
        catch { return ""; }
    }

    private void UpdateSetting(Func<Models.UserSettings, Models.UserSettings> update)
    {
        var s = update(_settings.Current);
        _settings.Apply(s);
        _ = _settings.Persist();
    }

    // ============ 检测 ============

    /// <summary>检测本机 Java / Python 运行时，并报告便携运行时是否已下载。</summary>
    public async Task<RuntimeStatus> DetectAsync()
    {
        var st = new RuntimeStatus();
        if (!IsDesktop)
        {
            st.WindowsOnlyNote = "外部 Java/Python 工具仅桌面端可用";
            return st;
        }

        var py = await ResolvePythonAsync();
        st.PythonOk = py is not null;
        st.PythonVersion = py?.Version ?? "";
        st.PythonDetail = py?.Detail ?? "在本机未找到 Python，可在下方自动下载便携版";

        var jv = await ResolveJavaAsync();
        st.JavaOk = jv is not null;
        st.JavaVersion = jv?.Version ?? "";
        st.JavaDetail = jv?.Detail ?? "在本机未找到 Java，可在下方自动下载便携版";

        if (Directory.Exists(Path.Combine(RuntimesDir, "python"))) st.PythonAutoDownloaded = true;
        if (Directory.Exists(Path.Combine(RuntimesDir, "java"))) st.JavaAutoDownloaded = true;
        return st;
    }

    private sealed record RuntimeResolve(string Exe, string Args, string Version, string Detail);

    private async Task<RuntimeResolve?> ResolvePythonAsync()
    {
        try
        {
            // 1) 用户手动配置的路径（目录或 python.exe）
            var manual = ManualPythonPath;
            if (!string.IsNullOrEmpty(manual))
            {
                var exe = DirOrFile(manual, "python.exe");
                if (exe is not null)
                {
                    var v = await VersionAsync(exe, "-V");
                    if (v is not null) return new RuntimeResolve(exe, "", v, $"已配置路径 {exe}");
                }
            }
            // 2) 便携运行时
            var portable = Path.Combine(RuntimesDir, "python", "python.exe");
            if (File.Exists(portable))
            {
                var v = await VersionAsync(portable, "-V");
                if (v is not null) return new RuntimeResolve(portable, "", v, $"便携运行时 {portable}");
            }
            // 3) PATH：py 启动器优先
            var py = await VersionAsync("py", "-3 -V");
            if (py is not null) return new RuntimeResolve("py", "-3", py, "本机已安装 Python（py 启动器）");
            var pyPath = await VersionAsync("python", "-V");
            if (pyPath is not null) return new RuntimeResolve("python", "", pyPath, "本机已安装 Python（PATH）");
            return null;
        }
        catch (Exception ex)
        {
            App.WriteLog("RuntimeManager.ResolvePython -> " + ex);
            return null;
        }
    }

    private async Task<RuntimeResolve?> ResolveJavaAsync()
    {
        try
        {
            var manual = ManualJavaPath;
            if (!string.IsNullOrEmpty(manual))
            {
                var exe = DirOrFile(manual, "java.exe");
                if (exe is not null)
                {
                    var v = await VersionAsync(exe, "-version");
                    if (v is not null) return new RuntimeResolve(exe, "", v, $"已配置路径 {exe}");
                }
            }
            var portable = Path.Combine(RuntimesDir, "java", "bin", "java.exe");
            if (File.Exists(portable))
            {
                var v = await VersionAsync(portable, "-version");
                if (v is not null) return new RuntimeResolve(portable, "", v, $"便携运行时 {portable}");
            }
            var jh = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrWhiteSpace(jh))
            {
                var exe = Path.Combine(jh, "bin", "java.exe");
                if (File.Exists(exe))
                {
                    var v = await VersionAsync(exe, "-version");
                    if (v is not null) return new RuntimeResolve(exe, "", v, $"JAVA_HOME {exe}");
                }
            }
            var javaPath = await VersionAsync("java", "-version");
            if (javaPath is not null) return new RuntimeResolve("java", "", javaPath, "本机已安装 Java（PATH）");
            return null;
        }
        catch (Exception ex)
        {
            App.WriteLog("RuntimeManager.ResolveJava -> " + ex);
            return null;
        }
    }

    /// <summary>目录→解释器文件，或直接是解释器文件。</summary>
    private static string? DirOrFile(string value, string exeName)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try
        {
            var full = Path.GetFullPath(value);
            if (Directory.Exists(full)) return Path.Combine(full, exeName);
            if (File.Exists(full)) return full;
        }
        catch { }
        return null;
    }

    /// <summary>运行 x -V / java -version 获取版本。</summary>
    private static async Task<string?> VersionAsync(string fileName, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return null;
            }
            var text = (await outTask) + (await errTask);
            return FirstVersionLine(text);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>从运行输出提取第一行能看出版本信息的文本。</summary>
    private static string FirstVersionLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "已安装";
        var lines = text.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        return lines.Count == 0 ? "已安装" : lines[0];
    }

    // ============ 自动下载便携运行时 ============

    public async Task<string> DownloadPythonAsync(IProgress<string>? progress = null)
    {
        return await Task.Run(async () =>
        {
            try
            {
                const string version = "3.11.9";
                var zipUrl = $"https://www.python.org/ftp/python/{version}/python-{version}-embed-amd64.zip";
                var dest = Path.Combine(RuntimesDir, "python");
                var zipPath = Path.Combine(Path.GetTempPath(), $"wab-python-{version}.zip");
                progress?.Report($"下载 Python {version}（embed 便携版）…");
                await DownloadAsync(zipUrl, zipPath);
                progress?.Report("解压引擎…");
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                Directory.CreateDirectory(dest);
                ZipFile.ExtractToDirectory(zipPath, dest);
                File.Delete(zipPath);
                if (!File.Exists(Path.Combine(dest, "python.exe")))
                    return "自动下载失败：压缩包内容异常";
                SetManualPythonPath(Path.Combine(dest, "python.exe"));
                progress?.Report("完成");
                return $"已自动下载 Python {version} 便携版 → {dest}";
            }
            catch (Exception ex)
            {
                App.WriteLog("RuntimeManager.DownloadPython -> " + ex);
                return "下载失败：" + ex.Message;
            }
        });
    }

    public async Task<string> DownloadJavaAsync(IProgress<string>? progress = null)
    {
        return await Task.Run(async () =>
        {
            try
            {
                const string major = "21";
                var zipUrl = $"https://api.adoptium.net/v3/binary/latest/{major}/ga/windows/x64/jre/hotspot/normal/eclipse";
                var dest = Path.Combine(RuntimesDir, "java");
                var zipPath = Path.Combine(Path.GetTempPath(), $"wab-jre-{major}.zip");
                progress?.Report($"下载 Java {major}（Temurin JRE 便携版）…");
                await DownloadAsync(zipUrl, zipPath);
                progress?.Report("解压引擎…");
                var root = "jre-" + major;
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                Directory.CreateDirectory(dest);
                ZipFile.ExtractToDirectory(zipPath, dest);
                File.Delete(zipPath);
                // Adoptium zip 根目录带有版本号子目录，把内容直接铺到 dest 下
                var nested = Directory.GetDirectories(dest).FirstOrDefault(d => File.Exists(Path.Combine(d, "bin", "java.exe")));
                if (nested is not null && nested != dest)
                {
                    foreach (var entry in Directory.GetFileSystemEntries(nested))
                    {
                        var target = Path.Combine(dest, Path.GetFileName(entry));
                        if (Directory.Exists(target)) Directory.Delete(target, true);
                        Directory.Move(entry, target);
                    }
                }
                if (!File.Exists(Path.Combine(dest, "bin", "java.exe")))
                    return "自动下载失败：压缩包内容异常";
                SetManualJavaPath(dest);
                progress?.Report("完成");
                return $"已自动下载 Java {major} JRE 便携版 → {dest}";
            }
            catch (Exception ex)
            {
                App.WriteLog("RuntimeManager.DownloadJava -> " + ex);
                return "下载失败：" + ex.Message;
            }
        });
    }

    private static async Task DownloadAsync(string url, string destPath)
    {
        var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        await using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await resp.Content.CopyToAsync(fs);
    }

    // ============ 供 ToolManager 使用的运行时解析 ============

    /// <summary>返回 (解释器, 启动参数)。找不到返回 null。</summary>
    public async Task<(string Exe, string Args)?> ResolveRuntimeAsync(ToolLanguage language)
    {
        if (!IsDesktop) return null;
        return language switch
        {
            ToolLanguage.Python => await ResolvePythonAsync() switch
            {
                null => null,
                { } r when r.Exe == "py" => ("py", "-3"),
                { } r => (r.Exe, "")
            },
            ToolLanguage.Java => await ResolveJavaAsync() switch
            {
                { } r => (r.Exe, ""),
                null => null
            },
            _ => null
        };
    }
}