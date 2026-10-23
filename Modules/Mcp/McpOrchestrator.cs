using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Jint;
using Jint.Runtime;
using WarmAsBefore.Models;

namespace WarmAsBefore.Modules.Mcp;

public sealed class McpOrchestrator
{
    private readonly Dictionary<string, McpToolDef> _registry = new();
    private readonly HttpClient _http = new();

    /// <summary>MCP 工具调用是否自动确认。false 时 Run 会拒绝未确认的调用（在设置页可切换）。</summary>
    public bool AutoApprove { get; set; } = true;

    /// <summary>MCP 总开关：关掉后一切 MCP 工具（含聊天里的 mcp_call）都不可用。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>网络 MCP 的默认接入地址（来自设置页）。</summary>
    public string NetworkUrl { get; set; } = "";

    /// <summary>MCP 数据包统一存放目录（导入/新建的服务器都在这里）。</summary>
    public string McpPackDir => System.IO.Path.Combine(WarmAsBefore.App.RootDirectory, "McpPacks");

    /// <summary>tools.json 是手写/模型产出的：小写字段、注释、尾逗号都认。</summary>
    private static readonly JsonSerializerOptions PackJson = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private bool _toolsLoaded;

    public McpOrchestrator()
    {
        Register("powershell", "Execute PS commands", "shell");
        Register("ps", "List processes", "monitor");
        Register("screencap", "Capture screen", "capture");
        LoadPersistedTools();
    }

    public void Register(string name, string desc, string cat) =>
        _registry[name] = new McpToolDef
        {
            Name = name,
            Description = desc,
            Category = cat,
            Server = "system",
            Kind = "builtin"
        };

    public void Register(McpToolDef def)
    {
        if (string.IsNullOrWhiteSpace(def.Name)) return;
        _registry[def.Name] = def;
    }

    public void Toggle(string name, bool on)
    {
        if (_registry.TryGetValue(name, out var t)) t.Active = on;
    }

    /// <summary>全部启用中的工具（含内置与自定义）。</summary>
    public List<McpToolDef> List() =>
        _registry.Values.Where(t => t.Active).OrderBy(t => t.Server, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>全部工具（含停用项），供工坊与设置页管理。</summary>
    public List<McpToolDef> ListAll() =>
        _registry.Values.OrderBy(t => t.Server, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>某个数据包里的工具。</summary>
    public List<McpToolDef> ToolsOf(string server) =>
        _registry.Values.Where(t => string.Equals(t.Server, server, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();

    // ============ 持久化 ============

    /// <summary>扫描 McpPacks/&lt;包&gt;/tools.json，把工具重新注册进来。导入/新建/删除后调用。</summary>
    public void ReloadPersistedTools()
    {
        foreach (var name in _registry.Where(kv => kv.Value.Server != "system").Select(kv => kv.Key).ToList())
            _registry.Remove(name);
        _toolsLoaded = false;
        LoadPersistedTools();
    }

    private void LoadPersistedTools()
    {
        if (_toolsLoaded) return;
        _toolsLoaded = true;
        try
        {
            if (!Directory.Exists(McpPackDir)) return;
            foreach (var dir in Directory.GetDirectories(McpPackDir))
            {
                var path = Path.Combine(dir, "tools.json");
                if (!File.Exists(path)) continue;
                try
                {
                    var tools = JsonSerializer.Deserialize<List<McpToolDef>>(File.ReadAllText(path), PackJson);
                    var server = Path.GetFileName(dir);
                    foreach (var t in tools ?? new List<McpToolDef>())
                    {
                        if (string.IsNullOrWhiteSpace(t.Name)) continue;
                        _registry[t.Name] = t with { Server = server };
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[MCP] tools.json {dir}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MCP] LoadPersistedTools: {ex.Message}");
        }
    }

    /// <summary>把某数据包当前注册的工具写回 tools.json（启停状态也一起落盘）。</summary>
    public async Task SaveToolsAsync(string server)
    {
        try
        {
            var dir = Path.Combine(McpPackDir, server);
            Directory.CreateDirectory(dir);
            var tools = ToolsOf(server);
            await File.WriteAllTextAsync(Path.Combine(dir, "tools.json"),
                JsonSerializer.Serialize(tools, PackJson));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MCP] SaveTools: {ex.Message}");
        }
    }

    public async Task SetToolActiveAsync(string server, string toolName, bool on)
    {
        Toggle(toolName, on);
        await SaveToolsAsync(server);
    }

    /// <summary>新建 MCP 数据包并登记它的工具（工坊与手工创建共用）。</summary>
    public async Task<(bool Ok, string Message, string Server)> CreateServerWithToolsAsync(
        string name, string description, string url, IEnumerable<McpToolDef> tools)
    {
        try
        {
            var server = Sanitize(string.IsNullOrWhiteSpace(name) ? "mcp_" + Guid.NewGuid().ToString("N")[..6] : name);
            if (string.IsNullOrWhiteSpace(server)) server = "mcp_" + Guid.NewGuid().ToString("N")[..6];
            var dir = Path.Combine(McpPackDir, server);
            Directory.CreateDirectory(dir);

            var meta = new McpServerMeta
            {
                Name = (name ?? "").Trim(),
                Description = (description ?? "").Trim(),
                Url = (url ?? "").Trim(),
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
            };
            if (string.IsNullOrWhiteSpace(meta.Name)) meta.Name = server;
            await File.WriteAllTextAsync(Path.Combine(dir, "metadata.json"),
                JsonSerializer.Serialize(meta, PackJson));

            // 重新生成同一个数据包时，先摘掉它上一版登记的工具（否则旧工具会以幽灵状态留在内存里）
            foreach (var old in ToolsOf(server)) _registry.Remove(old.Name);

            var registered = new List<McpToolDef>();
            foreach (var t in tools)
            {
                if (string.IsNullOrWhiteSpace(t.Name)) continue;
                var def = t with { Server = server, Name = t.Name.Trim() };
                // 内置工具不让 MCP 数据包顶掉（powershell / ps / screencap）
                if (_registry.TryGetValue(def.Name, out var existing)
                    && string.Equals(existing.Server, "system", StringComparison.OrdinalIgnoreCase))
                {
                    App.WriteLog($"[MCP] 工具 {def.Name} 与内置工具重名，已跳过");
                    continue;
                }
                _registry[def.Name] = def;
                registered.Add(def);
            }
            await File.WriteAllTextAsync(Path.Combine(dir, "tools.json"),
                JsonSerializer.Serialize(registered, PackJson));
            return (true, $"已创建 MCP「{meta.Name}」，登记 {registered.Count} 个工具。", server);
        }
        catch (Exception ex)
        {
            return (false, "创建 MCP 失败：" + ex.Message, "");
        }
    }

    /// <summary>删除整个 MCP 数据包（目录 + 注册的工具）。</summary>
    public bool RemoveServer(string server)
    {
        try
        {
            var dir = Path.Combine(McpPackDir, server);
            // 递归删除前确认它真的在 McpPacks 里面：数据包名是外部输入，
            // 一个 ".." 就会把整个数据目录（存档、立绘、设置）连锅端了。
            if (!Path.GetFullPath(dir).StartsWith(
                    Path.GetFullPath(McpPackDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                App.WriteLog("[MCP] 拒绝删除越出 McpPacks 的目录：" + server);
                return false;
            }
            foreach (var t in ToolsOf(server)) _registry.Remove(t.Name);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MCP] RemoveServer: {ex.Message}");
            return false;
        }
    }

    /// <summary>执行一个 MCP 工具；manual=true 时跳过自动确认（用户在界面上点「试跑」）。</summary>
    public async Task<string> Run(string name, string args) => await RunCoreAsync(name, args, manual: false);

    public async Task<string> RunManualAsync(string name, string args) => await RunCoreAsync(name, args, manual: true);

    private async Task<string> RunCoreAsync(string name, string args, bool manual)
    {
        if (!Enabled) return "[MCP] MCP 已关闭（可在设置里打开）";
        if (!_registry.TryGetValue(name, out var t) || !t.Active)
            return $"[MCP] {name} 已停用或不存在";
        if (!manual && !AutoApprove)
            return $"[MCP] 工具 {name} 未获人工确认，已跳过（可在设置中开启自动确认）";

        try
        {
            return t.Kind switch
            {
                "js" => await Task.Run(() => RunJs(t, args)),
                "http" => await RunHttpAsync(t, args),
                _ => await RunBuiltinAsync(t, name, args)
            };
        }
        catch (JavaScriptException ex)
        {
            return $"[MCP] JS 运行出错：{ex.Message}";
        }
        catch (StatementsCountOverflowException)
        {
            return "[MCP] JS 语句数超限，已中止";
        }
        catch (TimeoutException)
        {
            return "[MCP] JS 超时（300ms），已中止";
        }
        catch (Exception ex)
        {
            return $"[MCP] {name} 执行失败：{ex.Message}";
        }
    }

    private async Task<string> RunBuiltinAsync(McpToolDef t, string name, string args) => name switch
    {
        "powershell" => await ExecPs(args),
        "ps" => await Task.Run(() => ListProcesses(args)),
        "screencap" => "[MCP] 截图需要桌面端画面管线（暂未接入此入口）",
        _ => $"[MCP] {name} 没有实现"
    };

    /// <summary>MCP 的 JS 沙箱：input / args 都是 JSON 字符串，结果写进 result。</summary>
    private static string RunJs(McpToolDef tool, string args)
    {
        var engine = new Engine(options =>
        {
            options.TimeoutInterval(TimeSpan.FromMilliseconds(300));
            options.MaxStatements(20_000);
            options.LimitMemory(16_000_000);
        });
        engine.SetValue("input", string.IsNullOrWhiteSpace(args) ? "{}" : args);
        engine.SetValue("args", string.IsNullOrWhiteSpace(args) ? "{}" : args);
        engine.SetValue("result", "");
        engine.SetValue("log", new Action<object?>(m => App.WriteLog("[mcp] " + m)));
        engine.SetValue("now", new Func<string>(() => DateTime.Now.ToString("yyyy-MM-dd HH:mm")));
        engine.Execute(tool.Code ?? "");
        var value = engine.GetValue("result");
        return value.IsUndefined() || value.IsNull() ? "" : value.ToString();
    }

    /// <summary>远程 MCP：POST {name, arguments} 到工具声明的地址，返回原始响应。</summary>
    private async Task<string> RunHttpAsync(McpToolDef tool, string args)
    {
        if (string.IsNullOrWhiteSpace(tool.Url))
            return "[MCP] 这个工具没有填远程地址";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var req = new HttpRequestMessage(
            string.Equals(tool.Method, "GET", StringComparison.OrdinalIgnoreCase) ? HttpMethod.Get : HttpMethod.Post,
            tool.Url);
        foreach (var (k, v) in tool.Headers) req.Headers.TryAddWithoutValidation(k, v);
        if (req.Method != HttpMethod.Get)
        {
            var payload = JsonSerializer.Serialize(new { name = tool.Name, arguments = ParseArgs(args) });
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        }
        var resp = await _http.SendAsync(req, cts.Token);
        var body = await resp.Content.ReadAsStringAsync(cts.Token);
        return resp.IsSuccessStatusCode ? body : $"[MCP] HTTP {(int)resp.StatusCode}: {body}";
    }

    private static object ParseArgs(string args)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(args) ? "{}" : args);
            return JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText()) ?? new { };
        }
        catch { return args ?? ""; }
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
                    var key = Path.GetFileName(dir);
                    items.Add(new McpServerItem
                    {
                        Key = key,
                        Name = meta?.Name ?? key,
                        Description = meta?.Description ?? "",
                        FolderPath = dir,
                        Url = meta?.Url ?? "",
                        FileCount = files.Length,
                        SizeLabel = FormatSize(size),
                        CreatedLabel = meta?.CreatedAt ?? "",
                        Tools = ToolsOf(key)
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
                Key = safe,
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

    /// <summary>metadata.json 是手写数据包的一部分：小写字段、注释、尾逗号都认。</summary>
    private static readonly JsonSerializerOptions MetaJson = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static McpServerMeta? LoadMeta(string dir)
    {
        try
        {
            var p = Path.Combine(dir, "metadata.json");
            return File.Exists(p) ? JsonSerializer.Deserialize<McpServerMeta>(File.ReadAllText(p), MetaJson) : null;
        }
        catch { return null; }
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string((name ?? "").Trim().Where(c => !invalid.Contains(c)).ToArray());
        // "." 与 ".." 是合法文件名字符串，但拼进路径就变成数据根目录本身：
        // 之后 RemoveServer 的递归删除会连存档、立绘、设置一起删掉，所以这两种一律换成随机名。
        if (clean == "." || clean == "..") clean = "";
        return clean;
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
        // 用 -EncodedCommand（Base64 UTF-16LE）传命令，彻底避免 -Command "..." 的 shell 解析/引号注入。
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(cmd ?? ""));
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -EncodedCommand {encoded}",
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
            if (proc.ExitCode == 0) ReloadPersistedTools();
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
                ReloadPersistedTools();
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
                ReloadPersistedTools();
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
    /// <summary>McpPacks 下的目录名（工具按它归属）。</summary>
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public string Url { get; set; } = "";
    public int FileCount { get; set; }
    public string SizeLabel { get; set; } = "";
    public string CreatedLabel { get; set; } = "";
    public List<McpToolDef> Tools { get; set; } = new();
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