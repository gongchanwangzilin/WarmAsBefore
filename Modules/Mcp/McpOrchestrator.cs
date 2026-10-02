using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Modules.Mcp;

public sealed class McpOrchestrator
{
    private readonly Dictionary<string, McpToolDef> _registry = new();
    private readonly HttpClient _http = new();

    /// <summary>MCP 工具调用是否自动确认。false 时 Run 会拒绝未确认的调用（在设置页可切换）。</summary>
    public bool AutoApprove { get; set; } = true;

    /// <summary>网络 MCP 的默认接入地址（来自设置页）。</summary>
    public string NetworkUrl { get; set; } = "";

    /// <summary>MCP 数据包统一存放目录（导入/新建的服务器都在这里）。</summary>
    public string McpPackDir => System.IO.Path.Combine(WarmAsBefore.App.RootDirectory, "McpPacks");

    public McpOrchestrator()
    {
        Register("powershell", "Execute PS commands", "shell");
        Register("ps", "List processes", "monitor");
        Register("screencap", "Capture screen", "capture");
    }

    public void Register(string name, string desc, string cat) =>
        _registry[name] = new McpToolDef { Name = name, Description = desc, Category = cat };

    public void Toggle(string name, bool on)
    {
        if (_registry.TryGetValue(name, out var t)) t.Active = on;
    }

    public List<McpToolDef> List() => _registry.Values.Where(t => t.Active).ToList();

    public async Task<string> Run(string name, string args)
    {
        if (!_registry.TryGetValue(name, out var t) || !t.Active)
            return $"[MCP] {name} disabled";
        if (!AutoApprove)
            return $"[MCP] 工具 {name} 未获人工确认，已跳过（可在设置中开启自动确认）";

        return (t.Category, name) switch
        {
            ("shell", "powershell") => await ExecPs(args),
            ("monitor", "ps") => await Task.Run(() => ListProcesses(args)),
            ("network", "mcp") => await ExecNetworkMcp(args),
            _ => $"[MCP] {name} stub"
        };
    }

    // ============ 服务器管理（列表 / 新建 / 详情） ============

    /// <summary>列出 McpPacks 下所有已导入/新建的 MCP 数据包。</summary>
    public List<McpServerItem> ListServers()
    {
        var items = new List<McpServerItem>();
        try
        {
            if (!Directory.Exists(McpPackDir)) return items;
            foreach (var dir in Directory.GetDirectories(McpPackDir))
            {
                try
                {
                    var meta = LoadMeta(dir);
                    var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                    long size = 0;
                    foreach (var f in files)
                    {
                        try { size += new FileInfo(f).Length; } catch { }
                    }
                    items.Add(new McpServerItem
                    {
                        Name = meta?.Name ?? Path.GetFileName(dir),
                        Description = meta?.Description ?? "",
                        FolderPath = dir,
                        Url = meta?.Url ?? "",
                        FileCount = files.Length,
                        SizeLabel = FormatSize(size),
                        CreatedLabel = meta?.CreatedAt ?? ""
                    });
                }
                catch { }
            }
        }
        catch { }
        return items;
    }

    /// <summary>新建一个 MCP 服务器条目（在 McpPacks 下建目录 + metadata.json）。</summary>
    public McpServerItem? CreateServer(string name, string description, string url)
    {
        try
        {
            var safe = Sanitize(name);
            if (string.IsNullOrWhiteSpace(safe)) return null;
            var dir = Path.Combine(McpPackDir, safe);
            Directory.CreateDirectory(dir);
            var meta = new McpServerMeta
            {
                Name = name.Trim(),
                Description = (description ?? "").Trim(),
                Url = (url ?? "").Trim(),
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            };
            File.WriteAllText(Path.Combine(dir, "metadata.json"),
                JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
            return new McpServerItem
            {
                Name = meta.Name,
                Description = meta.Description,
                FolderPath = dir,
                Url = meta.Url,
                FileCount = 0,
                SizeLabel = "0 B",
                CreatedLabel = meta.CreatedAt
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MCP] CreateServer: {ex.Message}");
            return null;
        }
    }

    private static McpServerMeta? LoadMeta(string dir)
    {
        try
        {
            var p = Path.Combine(dir, "metadata.json");
            return File.Exists(p) ? JsonSerializer.Deserialize<McpServerMeta>(File.ReadAllText(p)) : null;
        }
        catch { return null; }
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string((name ?? "").Trim().Where(c => !invalid.Contains(c)).ToArray());
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / 1024.0 / 1024.0:0.#} MB";
    }

    /// <summary>执行网络 MCP 请求（通过 URL 接入外部 MCP 服务）。</summary>
    private async Task<string> ExecNetworkMcp(string args)
    {
        try
        {
            var cfg = JsonSerializer.Deserialize<McpNetworkConfig>(args);
            if (cfg is null) return "[MCP] 无效的网络 MCP 配置";
            var resp = await _http.PostAsync(cfg.Url, new StringContent(JsonSerializer.Serialize(cfg.Payload), Encoding.UTF8, "application/json"));
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(body) ? "[MCP] 空响应" : body;
        }
        catch (Exception ex)
        {
            return $"[MCP] 网络请求失败: {ex.Message}";
        }
    }

    private static async Task<string> ExecPs(string cmd)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -Command \"{cmd.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        p.Start();
        var o = await p.StandardOutput.ReadToEndAsync();
        var e = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return string.IsNullOrEmpty(e) ? o : $"{o}\nERR: {e}";
    }

    private static string ListProcesses(string filter)
    {
        var ps = Process.GetProcesses()
            .Where(p =>
            {
                try { return string.IsNullOrEmpty(filter) || p.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            })
            .Select(p =>
            {
                try { return $"{p.ProcessName} ({p.Id})"; }
                catch { return "?"; }
            });
        return string.Join("\n", ps.Take(50));
    }

    /// <summary>从 GitHub 仓库导入 MCP 配置/数据包。progress 用于实时回报阶段与 git clone 进度。</summary>
    public async Task<string> ImportFromGitHub(string repoUrl, string targetDir, IProgress<string>? progress = null)
    {
        try
        {
            progress?.Report("查询仓库信息…");
            var apiUrl = repoUrl.Replace("https://github.com/", "https://api.github.com/repos/");

            // 网络不可达/受限时不能无限等待：API 查询 20 秒超时
            using var apiCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var resp = await _http.GetAsync(apiUrl, apiCts.Token);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(apiCts.Token);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var repoName = root.GetProperty("name").GetString() ?? "unknown";
            var cloneUrl = root.GetProperty("clone_url").GetString() ?? "";

            var targetPath = Path.Combine(targetDir, repoName);
            Directory.CreateDirectory(targetPath);

            // 使用 git clone 下载
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"clone {cloneUrl} \"{targetPath}\"",
                RedirectStandardOutput = false,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc is null) return "[MCP] 无法启动 git";

            progress?.Report("正在 git clone…");
            // 实时转发 git 进度输出（stderr），避免长时间无反馈；120 秒超时后强制终止
            using var gitCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            try
            {
                while (!proc.StandardError.EndOfStream)
                {
                    var line = await proc.StandardError.ReadLineAsync(gitCts.Token);
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.Contains("Receiving objects", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("Resolving deltas", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("Counting objects", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("Cloning into", StringComparison.OrdinalIgnoreCase))
                    {
                        progress?.Report(line);
                    }
                }
                await proc.WaitForExitAsync();
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return "[MCP] git clone 超时（120 秒），已终止。请检查网络或更换仓库地址";
            }
            progress?.Report("完成");
            return proc.ExitCode == 0 ? $"[MCP] 已导入 GitHub 仓库: {repoName} → {targetPath}" : "[MCP] git clone 失败";
        }
        catch (OperationCanceledException)
        {
            return "[MCP] 查询 GitHub 仓库信息超时（20 秒），请检查网络";
        }
        catch (Exception ex)
        {
            return $"[MCP] GitHub 导入失败: {ex.Message}";
        }
    }

    /// <summary>通过 ZIP 文件导入数据包。progress 回报解压进度 (完成数, 总数)。</summary>
    public async Task<string> ImportZip(string zipPath, string targetDir, IProgress<(int done, int total)>? progress = null)
    {
        try
        {
            if (!File.Exists(zipPath)) return "[MCP] ZIP 文件不存在";
            return await Task.Run(() =>
            {
                using var zip = ZipFile.OpenRead(zipPath);
                var name = Path.GetFileNameWithoutExtension(zipPath);
                var dest = Path.Combine(targetDir, name);
                Directory.CreateDirectory(dest);

                var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
                int done = 0;
                foreach (var entry in entries)
                {
                    // 防路径穿越：确保解压目标始终落在 dest 内
                    var target = Path.GetFullPath(Path.Combine(dest, entry.FullName));
                    if (!target.StartsWith(dest, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                    done++;
                    progress?.Report((done, entries.Count));
                }
                return $"[MCP] ZIP 导入成功: {name} → {dest}";
            });
        }
        catch (Exception ex)
        {
            return $"[MCP] ZIP 导入失败: {ex.Message}";
        }
    }

    /// <summary>通过文件夹导入数据包。progress 回报复制进度 (完成数, 总数)。</summary>
    public async Task<string> ImportFolder(string folderPath, string targetDir, IProgress<(int done, int total)>? progress = null)
    {
        try
        {
            if (!Directory.Exists(folderPath)) return "[MCP] 文件夹不存在";
            return await Task.Run(() =>
            {
                var name = Path.GetFileName(folderPath);
                var dest = Path.Combine(targetDir, name);
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                Directory.CreateDirectory(dest);

                var files = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories);
                int done = 0;
                foreach (var f in files)
                {
                    var rel = Path.GetRelativePath(folderPath, f);
                    var target = Path.Combine(dest, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(f, target, overwrite: true);
                    done++;
                    progress?.Report((done, files.Length));
                }
                return $"[MCP] 文件夹导入成功: {name} → {dest}";
            });
        }
        catch (Exception ex)
        {
            return $"[MCP] 文件夹导入失败: {ex.Message}";
        }
    }
}

public sealed class McpNetworkConfig
{
    public string Url { get; set; } = "";
    public Dictionary<string, object> Payload { get; set; } = new();
}

/// <summary>设置页 MCP 列表中的一条服务器/数据包。</summary>
public sealed class McpServerItem
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public string Url { get; set; } = "";
    public int FileCount { get; set; }
    public string SizeLabel { get; set; } = "";
    public string CreatedLabel { get; set; } = "";
    public string SizeLine => $"{SizeLabel} · 文件 {FileCount} 个";
    public string Detail =>
        $"路径: {FolderPath}\n简介: {(string.IsNullOrWhiteSpace(Description) ? "（无）" : Description)}\n地址: {(string.IsNullOrWhiteSpace(Url) ? "（无）" : Url)}\n文件数: {FileCount}\n大小: {SizeLabel}\n创建: {(string.IsNullOrWhiteSpace(CreatedLabel) ? "（无）" : CreatedLabel)}";
}

internal sealed class McpServerMeta
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Url { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}