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

    /// <summary>server.json 与 tools.json 同一套宽松读法：用户可以手改这个文件。</summary>
    private static readonly JsonSerializerOptions ServerJson = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    /// <summary>正在连接中的服务：DisconnectServerAsync 靠它把在途握手掐掉。</summary>
    private readonly Dictionary<string, CancellationTokenSource> _connecting = new(StringComparer.OrdinalIgnoreCase);

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
            // 递归删目录，server.json（接入配置与 stdio 启动命令）自然一起没了。
            // 在途握手先掐掉，否则它会在删完之后又把工具注册回内存、再把 tools.json 写回来。
            CancellationTokenSource? inFlight;
            lock (_connecting)
            {
                _connecting.TryGetValue(server, out inFlight);
                _connecting.Remove(server);
            }
            try { inFlight?.Cancel(); }
            catch (ObjectDisposedException) { App.WriteLog("[MCP] 删包时连接刚好已收尾（无需处理）"); }
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
                "mcp" => await RunMcpAsync(t, args),
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

    /// <summary>
    /// 真正的 MCP 远端调用：协议握手与传输都在 McpProtocol 里，这里只管取配置与同意闸。
    /// args 是模型传来的 JSON 字符串，空/坏 JSON 由协议层当 {} 处理。
    /// </summary>
    private async Task<string> RunMcpAsync(McpToolDef t, string args)
    {
        var cfg = LoadServerConfig(t.Server);
        if (cfg is null)
            return $"[MCP] 工具 {t.Name} 所属的服务「{t.Server}」没有 server.json（服务配置被删了？重新连一次）";
        // stdio 会在用户机器上起进程，调用这一路也要过同意闸，不能只在「点连接」那一次把关
        if (McpProtocol.UsesStdio(cfg) && !cfg.Allowed)
            return "[MCP] " + McpProtocol.NeedConsentMessage + $"（服务「{cfg.Name}」）";

        var tool = new McpRemoteTool(t.Name, t.Description, t.ParametersSchema);
        return await McpProtocol.CallToolAsync(cfg, tool, args ?? "", CancellationToken.None);
    }

    // ============ 服务配置（server.json：从哪里启动 / 连到哪里） ============

    /// <summary>把数据包名解析成 McpPacks 内的绝对路径；越界的一律 null（数据包名是外部输入）。</summary>
    private string? SafePackDir(string key)
    {
        var safe = Sanitize(key ?? "");
        if (string.IsNullOrWhiteSpace(safe)) return null;
        var dir = Path.GetFullPath(Path.Combine(McpPackDir, safe));
        var root = Path.GetFullPath(McpPackDir) + Path.DirectorySeparatorChar;
        if (!dir.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            App.WriteLog("[MCP] 拒绝越出 McpPacks 的目录：" + key);
            return null;
        }
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>读 McpPacks/&lt;key&gt;/server.json；没有这个文件（纯数据包）返回 null。</summary>
    public McpServerConfig? LoadServerConfig(string key)
    {
        try
        {
            var dir = SafePackDir(key);
            if (dir is null) return null;
            var path = Path.Combine(dir, "server.json");
            if (!File.Exists(path)) return null;
            var cfg = JsonSerializer.Deserialize<McpServerConfig>(File.ReadAllText(path), ServerJson);
            if (cfg is null) return null;
            // 落盘的 Key 可能被手改成别的值：以目录名为准，否则工具归属与删除会错位
            cfg.Key = Path.GetFileName(dir);
            return cfg;
        }
        catch (Exception ex)
        {
            App.WriteLog($"[MCP] server.json 读不出来（{key}）：{ex.Message}");
            return null;
        }
    }

    /// <summary>写 server.json（目录不存在就建）。先写临时文件再替换：半截 JSON 会让下次启动读不出配置。</summary>
    public bool SaveServerConfig(McpServerConfig cfg)
    {
        if (cfg is null) return false;
        try
        {
            var safe = Sanitize(cfg.Key ?? "");
            if (string.IsNullOrWhiteSpace(safe)) return false;
            var dir = Path.GetFullPath(Path.Combine(McpPackDir, safe));
            var root = Path.GetFullPath(McpPackDir) + Path.DirectorySeparatorChar;
            if (!dir.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
            Directory.CreateDirectory(dir);
            if (string.IsNullOrWhiteSpace(cfg.CreatedAt))
                cfg.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            cfg.Key = safe;
            var tmp = Path.Combine(dir, "server.json.tmp");
            File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, ServerJson));
            File.Move(tmp, Path.Combine(dir, "server.json"), overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog($"[MCP] server.json 落盘失败（{cfg.Key}）：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 登记一个外部 MCP 服务。commandLineOrUrl：stdio 传「整行命令」（自己拆成 Command + Args，
    /// 带空格的路径要加引号），http 传 URL。名字撞已有的数据包时加后缀而不是覆盖。
    /// 参数不合法返回 null，并且不会在磁盘上留下半个目录。
    /// </summary>
    public McpServerConfig? AddService(string name, string transport, string commandLineOrUrl, string description)
    {
        var t = (transport ?? "").Trim().ToLowerInvariant();
        if (t != "stdio" && t != "http")
        {
            App.WriteLog("[MCP] 添加服务被拒：传输方式只支持 stdio 与 http，收到「" + transport + "」");
            return null;
        }
        var input = (commandLineOrUrl ?? "").Trim();
        if (input.Length == 0)
        {
            App.WriteLog("[MCP] 添加服务被拒：命令行为空");
            return null;
        }

        var cfg = new McpServerConfig { Transport = t };
        if (t == "http")
        {
            if (!McpProtocol.IsHttpEndpoint(input, out var urlError))
            {
                App.WriteLog("[MCP] 添加服务被拒：" + urlError);
                return null;
            }
            cfg.Url = input;
        }
        else
        {
            var parts = McpProtocol.SplitCommandLine(input);
            if (parts.Count == 0 || string.IsNullOrWhiteSpace(parts[0]))
            {
                App.WriteLog("[MCP] 添加服务被拒：这一行里没有可执行文件名");
                return null;
            }
            cfg.Command = parts[0];
            cfg.Args = parts.GetRange(1, parts.Count - 1);
        }

        cfg.Key = UniqueKey(Sanitize(name));
        if (string.IsNullOrWhiteSpace(cfg.Key)) return null;
        cfg.Name = string.IsNullOrWhiteSpace(name) ? cfg.Key : name.Trim();
        cfg.Description = (description ?? "").Trim();

        if (!SaveServerConfig(cfg)) return null;

        // metadata.json 是数据包列表一直在读的那份：同步写一份，服务才在旧列表里看得见、也才删得掉。
        // stdio 没有地址，就把启动命令放进 Url 字段位上显示。
        var dir = Path.Combine(McpPackDir, cfg.Key);
        var meta = new McpServerMeta
        {
            Name = cfg.Name,
            Description = cfg.Description,
            Url = t == "http" ? cfg.Url : cfg.Command + (cfg.Args.Count > 0 ? " " + string.Join(" ", cfg.Args) : ""),
            CreatedAt = cfg.CreatedAt
        };
        try
        {
            File.WriteAllText(Path.Combine(dir, "metadata.json"), JsonSerializer.Serialize(meta, ServerJson));
        }
        catch (Exception ex)
        {
            App.WriteLog("[MCP] metadata.json 没写进去（服务配置已存，不影响连接）：" + ex.Message);
        }
        return cfg;
    }

    /// <summary>目录名撞车时加 _2 / _3 …，绝不覆盖已存在的数据包。</summary>
    private string UniqueKey(string baseKey)
    {
        var safe = Sanitize(baseKey);
        if (string.IsNullOrWhiteSpace(safe)) return "mcp_" + Guid.NewGuid().ToString("N")[..6];
        if (!Directory.Exists(Path.Combine(McpPackDir, safe))) return safe;
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Sanitize(baseKey) + "_" + i;
            if (!Directory.Exists(Path.Combine(McpPackDir, candidate))) return candidate;
        }
        return "mcp_" + Guid.NewGuid().ToString("N")[..6];
    }

    /// <summary>
    /// 用户同意让这个服务在本机启动进程（stdio 的同意闸，UI 上那一次「启动并连接」才调它）。
    /// 落盘之后重启也还在；要收回就在设置页把它关掉。
    /// </summary>
    public bool AllowServer(string key)
    {
        var cfg = LoadServerConfig(key);
        if (cfg is null) return false;
        if (cfg.Allowed) return true;
        cfg.Allowed = true;
        var ok = SaveServerConfig(cfg);
        App.WriteLog($"[MCP] 用户已放行 stdio 启动：「{cfg.Name}」→ {cfg.Command} {string.Join(" ", cfg.Args)}");
        return ok;
    }

    /// <summary>收回同意（下次连接与调用都会被拒）。</summary>
    public bool RevokeServer(string key)
    {
        var cfg = LoadServerConfig(key);
        if (cfg is null || !cfg.Allowed) return cfg is not null;
        cfg.Allowed = false;
        App.WriteLog($"[MCP] 已取消「{cfg.Name}」的启动许可");
        return SaveServerConfig(cfg);
    }

    /// <summary>
    /// 握手 + tools/list，把远端工具登记成 Kind="mcp" 并落盘（启停状态因此保留）。
    /// 只替换这个服务自己的 mcp 工具：数据包里原有的 js/http 工具是用户手写的，不能跟着一起掉。
    /// </summary>
    public async Task<(bool Ok, string Message, int ToolCount)> ConnectServerAsync(string key)
    {
        var cfg = LoadServerConfig(key);
        if (cfg is null) return (false, "没读到这个服务的 server.json（它可能是纯数据包，没有接入配置）", 0);
        CancellationTokenSource cts;
        lock (_connecting)
        {
            if (_connecting.ContainsKey(key)) return (false, "这个服务正在连接中", 0);
            cts = new CancellationTokenSource();
            _connecting[key] = cts;
        }

        try
        {
            var (ok, message, tools) = await McpProtocol.DiscoverAsync(cfg, cts.Token);
            if (!ok)
            {
                cfg.LastError = message;
                SaveServerConfig(cfg);
                return (false, message, 0);
            }

            foreach (var stale in _registry.Values
                         .Where(x => string.Equals(x.Server, cfg.Key, StringComparison.OrdinalIgnoreCase)
                                     && x.Kind == "mcp")
                         .Select(x => x.Name).ToList())
                _registry.Remove(stale);

            var registered = 0;
            foreach (var remote in tools)
            {
                var def = new McpToolDef
                {
                    Name = remote.Name,
                    Description = remote.Description,
                    Category = "mcp",
                    Kind = "mcp",
                    Server = cfg.Key,
                    ParametersSchema = remote.ParametersSchema
                };
                // 工具名在整个注册表里是唯一的键：撞了别的服务就跳过，别把别人的工具顶掉
                if (_registry.TryGetValue(def.Name, out var clash)
                    && !string.Equals(clash.Server, cfg.Key, StringComparison.OrdinalIgnoreCase))
                {
                    App.WriteLog($"[MCP] 工具 {def.Name} 已属于服务 {clash.Server}，本次跳过");
                    continue;
                }
                _registry[def.Name] = def;
                registered++;
            }

            cfg.LastError = "";
            cfg.LastConnectedUtc = DateTime.UtcNow;
            SaveServerConfig(cfg);
            await SaveToolsAsync(cfg.Key);
            return (true, message, registered);
        }
        catch (Exception ex)
        {
            App.WriteLog($"[MCP] 连接 {key} 失败：{ex}");
            cfg.LastError = ex.Message;
            SaveServerConfig(cfg);
            return (false, "连接失败：" + ex.Message, 0);
        }
        finally
        {
            lock (_connecting) _connecting.Remove(key);
            cts.Dispose();
        }
    }

    /// <summary>
    /// 断开：掐掉在途握手、注销这个服务的远端工具并落盘、清掉连接标记。
    /// stdio 没有常驻进程要收（每次调用起一个、用完就杀），所以这里只处理内存里的登记与在途请求。
    /// </summary>
    public async Task DisconnectServerAsync(string key)
    {
        CancellationTokenSource? cts;
        lock (_connecting)
        {
            _connecting.TryGetValue(key, out cts);
            _connecting.Remove(key);
        }
        try { cts?.Cancel(); }
        catch (ObjectDisposedException) { App.WriteLog("[MCP] 断开时连接刚好已收尾（无需处理）"); }

        foreach (var name in _registry.Values
                     .Where(x => string.Equals(x.Server, key, StringComparison.OrdinalIgnoreCase) && x.Kind == "mcp")
                     .Select(x => x.Name).ToList())
            _registry.Remove(name);

        var cfg = LoadServerConfig(key);
        if (cfg is not null)
        {
            cfg.LastConnectedUtc = default;
            SaveServerConfig(cfg);
        }
        await SaveToolsAsync(key);
        App.WriteLog($"[MCP] 已断开服务「{(cfg?.Name ?? key)}」登记的远端工具");
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
                    var service = LoadServerConfig(key);
                    items.Add(new McpServerItem
                    {
                        Key = key,
                        Name = meta?.Name ?? key,
                        Description = meta?.Description ?? "",
                        FolderPath = dir,
                        Url = service is null ? meta?.Url ?? "" : (string.IsNullOrWhiteSpace(service.Url) ? service.Command : service.Url),
                        FileCount = files.Length,
                        SizeLabel = FormatSize(size),
                        CreatedLabel = meta?.CreatedAt ?? "",
                        Tools = ToolsOf(key),
                        HasServiceConfig = service is not null,
                        Transport = service?.Transport ?? "",
                        Allowed = service?.Allowed ?? false,
                        CommandLine = service is null ? "" : BuildCommandLinePreview(service),
                        LastError = service?.LastError ?? "",
                        LastConnectedUtc = service?.LastConnectedUtc ?? default
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

    /// <summary>stdio 的「从哪里启动」那一行，给设置页原样显示。</summary>
    private static string BuildCommandLinePreview(McpServerConfig cfg)
        => McpProtocol.UsesStdio(cfg)
            ? (cfg.Command + " " + string.Join(" ", cfg.Args)).Trim()
            : cfg.Url;

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

    /// <summary>有没有 server.json（false 表示这是纯数据包，只靠 tools.json）。</summary>
    public bool HasServiceConfig { get; set; }
    /// <summary>"stdio" / "http"；纯数据包为空。</summary>
    public string Transport { get; set; } = "";
    /// <summary>stdio 是否已被用户放行；未放行时连接与调用都会被拒。</summary>
    public bool Allowed { get; set; }
    /// <summary>stdio 是整行启动命令，http 是地址。</summary>
    public string CommandLine { get; set; } = "";
    public string LastError { get; set; } = "";
    /// <summary>默认值表示从没握手成功过。</summary>
    public DateTime LastConnectedUtc { get; set; }

    public bool IsStdio => string.Equals(Transport, "stdio", StringComparison.OrdinalIgnoreCase);
    public string TransportLabel => IsStdio ? "本机进程" : string.Equals(Transport, "http", StringComparison.OrdinalIgnoreCase) ? "网络服务" : "数据包";
    public string ConnectLabel => !HasServiceConfig ? "无接入配置"
        : LastConnectedUtc == default ? "未连接"
        : "已连接 " + LastConnectedUtc.ToLocalTime().ToString("MM-dd HH:mm");
    public string SizeLine => $"{SizeLabel} · 文件 {FileCount} 个";

    /// <summary>握手成功过一次就算「在连」：stdio 不留常驻进程，这里表示的是配置可用且曾握手。</summary>
    public bool IsConnected => HasServiceConfig && LastConnectedUtc != default;

    /// <summary>列表里那颗连接按钮的文案：没握过手是「连接」，握过是「重新握手」，纯数据包没得连。</summary>
    public string ConnectButtonLabel => !HasServiceConfig ? "连接" : IsConnected ? "重新握手" : "连接";

    /// <summary>stdio 才有同意闸；http 不需要用户放行一个进程。</summary>
    public bool ShowConsent => IsStdio;
    public string ConsentLabel => Allowed ? "收回许可" : "允许启动";

    /// <summary>这一行给列表看「它从哪儿来」：接入的服务用启动预览，纯数据包只能给 metadata 里的地址。</summary>
    public string EndpointLine => string.IsNullOrWhiteSpace(CommandLine) ? Url : CommandLine;

    /// <summary>数据包（只靠 tools.json）没有远端工具，就不显示这一行，免得看着像坏了。</summary>
    public bool HasToolCount => HasServiceConfig;
    public string ToolCountLabel => Tools.Count == 0
        ? (IsConnected ? "这个服务没给出工具" : "还没握手，不知道有几个工具")
        : $"工具 {Tools.Count} 个";
    public string Detail =>
        $"路径: {FolderPath}\n类型: {TransportLabel}\n简介: {(string.IsNullOrWhiteSpace(Description) ? "（无）" : Description)}\n" +
        $"地址/启动命令: {(string.IsNullOrWhiteSpace(Url) ? "（无）" : Url)}\n" +
        $"同意启动: {(HasServiceConfig ? (Allowed ? "已允许" : "未允许") : "—")}\n连接状态: {ConnectLabel}\n" +
        $"最近错误: {(string.IsNullOrWhiteSpace(LastError) ? "（无）" : LastError)}\n" +
        $"文件数: {FileCount}\n大小: {SizeLabel}\n创建: {(string.IsNullOrWhiteSpace(CreatedLabel) ? "（无）" : CreatedLabel)}";
}

internal sealed class McpServerMeta
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Url { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}