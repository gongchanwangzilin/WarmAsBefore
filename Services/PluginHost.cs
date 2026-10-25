using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Jint;
using Jint.Runtime;

namespace WarmAsBefore.Services;

/// <summary>插件的设置页声明。三选一是硬性要求：没选就不能算装好。</summary>
public sealed class PluginSettingsSpec
{
    /// <summary>html（网页设置 + 回调）/ native（应用内标准表单）/ none（明确不需要）。</summary>
    public string Kind { get; set; } = "";
    /// <summary>html 模式：本地文件（相对插件目录）或 http(s) 地址。</summary>
    public string Page { get; set; } = "";
    /// <summary>native 模式的字段定义。</summary>
    public List<PluginSettingField> Fields { get; set; } = new();
    /// <summary>html 模式是否注入 callback 参数（默认是）。</summary>
    public bool Callback { get; set; } = true;

    public bool IsValid => Kind is "html" or "native" or "none";
}

public sealed class PluginSettingField
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>text / number / switch / select / secret。</summary>
    public string Type { get; set; } = "text";
    public string Default { get; set; } = "";
    public string Help { get; set; } = "";
    public List<string> Options { get; set; } = new();
}

public sealed class PluginDependency
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public bool Optional { get; set; }
}

public sealed class PluginPageSpec
{
    public string Route { get; set; } = "";
    public string Title { get; set; } = "";
    public string File { get; set; } = "";
}

public sealed class PluginManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    /// <summary>可选：插件声明需要哪些能力（只做展示与授权提示，不是安全边界）。</summary>
    public List<string> Permissions { get; set; } = new();
    public List<PluginDependency> Dependencies { get; set; } = new();
    /// <summary>钩子名 → JS 文件（相对插件目录）。</summary>
    public Dictionary<string, string> Hooks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public PluginSettingsSpec Settings { get; set; } = new();
    /// <summary>插件自带页面（用系统浏览器打开；页面里可调 callback 写设置）。</summary>
    public List<PluginPageSpec> Pages { get; set; } = new();
    /// <summary>声明要接管的内部环节（persona / reply / beforeSend / afterReply / scene）。</summary>
    public List<string> Overrides { get; set; } = new();
    /// <summary>
    /// 声明需要的外部运行时（node / python / java / webview2 / browser / git / dotnet / uv / deno / bun）。
    /// 只用于「装之前先告诉用户缺什么、去哪装、本应用有什么替代」——不是安全边界，也不拦启用。
    /// 写 `"runtime": "node"` 这种单个字符串也算对（作者常忘了数组写法）。
    /// </summary>
    [JsonConverter(typeof(OneOrManyConverter))]
    public List<string> Runtime { get; set; } = new();
}

public sealed class PluginEntry
{
    public PluginManifest Manifest { get; set; } = new();
    public string Dir { get; set; } = "";
    public bool Enabled { get; set; }
    public string SettingsIssue { get; set; } = "";
    public List<PluginDependency> MissingDependencies { get; set; } = new();
    public string LogPath => Path.Combine(PluginHost.LogsDir, Manifest.Id + ".log");
}

/// <summary>
/// 插件宿主：扫描 plugins/&lt;id&gt;/plugin.json，执行 JS 钩子，管设置、依赖与崩溃日志。
///
/// 插件能做的事：
///  · beforeSend / afterReply 改写文本（等于接管回复管线的两个环节）；
///  · persona 追加人设，overridePersona / overrideReply 在钩子返回非空时替换原值；
///  · afterTurn / onSceneChange / onToolResult 做副作用；
///  · native 字段或 HTML 网页（带回调）做设置；
///  · pages 用系统浏览器打开自带页面；
///  · dependencies 声明依赖，缺了会在界面上给出安装 URL。
///
/// 安全边界：JS 走 Jint，有超时/语句数/内存上限；没有文件系统与反射入口，
/// 想读文件或调工具必须通过宿主明确暴露的 host 函数。插件崩溃只写日志，绝不带崩主程序。
/// </summary>
public sealed class PluginHost
{
    public static string PluginsDir => Path.Combine(App.RootDirectory, "plugins");
    public static string LogsDir => Path.Combine(App.RootDirectory, "plugin_logs");
    // 相对存储键（不是文件路径）：StorageProvider 自己拼数据根并补 .json，
    // 传绝对路径的结果是写出 state.json.json / settings.json.json 这类双扩展名文件。
    private const string StateKey = "plugins/state";

    /// <summary>插件 id 只允许安装时那套字符；plugin.json 里的 id 不可信。</summary>
    private static bool IsSafeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id == "." || id == "..") return false;
        return id.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');
    }
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private readonly StorageProvider _store;
    private readonly SettingsManager _settings;
    private readonly PluginPermissions _permissions;
    private readonly object _gate = new();
    private readonly Dictionary<string, PluginEntry> _plugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _settingsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HttpListener> _callbackServers = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;
    private int _pluginCallDepth;
    private int _emitDepth;

    /// <summary>能力授权表（声明 ∩ 授予）。界面与钩子判定共用同一个实例。</summary>
    public PluginPermissions Permissions => _permissions;

    /// <summary>
    /// 最近一次安装成功的插件 id（安装入口在它非空时立刻做一次体检）。
    /// 只记成功的那次：装失败时留着上一条 id，会把体检结论报到别的插件身上。
    /// </summary>
    public string? LastInstalledId { get; private set; }

    /// <summary>调用工具（由 RuntimeConfigurator 注入）：插件 JS 里用 callTool(name, argsJson)。</summary>
    public Func<string, string, Task<string>>? ToolInvoker { get; set; }

    /// <summary>发用户可见通知（由 RuntimeConfigurator 注入）：notify(title,text) 要走 notify 授权。</summary>
    public Func<string, string, Task>? Notifier { get; set; }

    /// <summary>
    /// 实时状态供给（由 RuntimeConfigurator 注入）：插件设置页 GET /runtime 取这一段 JSON。
    /// 只读、不落盘、不缓存 —— 运行中的值写进 settings.json 会在断线和重启后变成看着像真相的陈旧值。
    /// 返回 null 表示这个插件没有实时状态。
    /// </summary>
    public Func<string, Task<string?>>? RuntimeProvider { get; set; }

    /// <summary>当前存档的插件白名单；null = 不限制。</summary>
    public Func<Models.SavePermissions?>? PermissionsProvider { get; set; }

    public PluginHost(StorageProvider store, SettingsManager settings, PluginPermissions? permissions = null)
    {
        _store = store;
        _settings = settings;
        _permissions = permissions ?? new PluginPermissions(store);
    }

    /// <summary>插件目录变了 / 需要最新状态：丢缓存重扫。</summary>
    public void ReleaseCache()
    {
        lock (_gate)
        {
            _plugins.Clear();
            _disabled.Clear();
            _loaded = false;
        }
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        try
        {
            Directory.CreateDirectory(PluginsDir);
            Directory.CreateDirectory(LogsDir);
            MoveDoubledExtension(Path.Combine(PluginsDir, "state.json.json"), Path.Combine(PluginsDir, "state.json"));
            var state = await _store.Load<PluginState>(StateKey);
            lock (_gate)
            {
                _disabled.Clear();
                if (state is not null)
                    foreach (var id in state.Disabled) _disabled.Add(id);
            }
            foreach (var dir in Directory.GetDirectories(PluginsDir))
            {
                var manifestPath = Path.Combine(dir, "plugin.json");
                if (!File.Exists(manifestPath)) continue;
                try
                {
                    var manifest = JsonSerializer.Deserialize<PluginManifest>(await File.ReadAllTextAsync(manifestPath), Json);
                    if (manifest is null || string.IsNullOrWhiteSpace(manifest.Name)) continue;
                    // id 就是目录名：安装时已经消毒过。plugin.json 里写什么不能信——
                    // 一个 "../../x" 会把设置与崩溃日志写到数据目录外面。
                    var folderId = Path.GetFileName(dir);
                    if (string.IsNullOrWhiteSpace(manifest.Id) || !IsSafeId(manifest.Id))
                        manifest.Id = folderId;
                    manifest.Id = manifest.Id.Trim();
                    var entry = new PluginEntry
                    {
                        Manifest = manifest,
                        Dir = dir,
                        Enabled = !_disabled.Contains(manifest.Id)
                    };
                    if (!manifest.Settings.IsValid)
                        entry.SettingsIssue = "没有声明设置页（必须是 html / native / none 之一）";
                    entry.MissingDependencies = MissingDeps(manifest);
                    lock (_gate) _plugins[manifest.Id] = entry;
                }
                catch (Exception ex)
                {
                    App.WriteLog("PluginHost.Load(" + dir + ") -> " + ex.Message);
                }
            }
            lock (_gate) _loaded = true;
        }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.EnsureLoaded -> " + ex.Message);
            lock (_gate) _loaded = true;
        }
    }

    public async Task<List<PluginEntry>> AllAsync()
    {
        await EnsureLoadedAsync();
        lock (_gate) return _plugins.Values.OrderBy(p => p.Manifest.Name, StringComparer.CurrentCulture).ToList();
    }

    public async Task<PluginEntry?> FindAsync(string id)
    {
        await EnsureLoadedAsync();
        lock (_gate) return _plugins.TryGetValue(id ?? "", out var p) ? p : null;
    }

    /// <summary>某插件是否允许在这份存档里运行。</summary>
    public bool AllowedInSave(Models.SavePermissions? permissions, string id)
        => permissions is null || permissions.AllowsPlugin(id);

    private List<PluginDependency> MissingDeps(PluginManifest manifest)
    {
        var missing = new List<PluginDependency>();
        foreach (var d in manifest.Dependencies ?? new List<PluginDependency>())
        {
            if (string.IsNullOrWhiteSpace(d.Id)) continue;
            var found = Directory.Exists(Path.Combine(PluginsDir, d.Id))
                        || Directory.Exists(Path.Combine(Modules.Tools.ToolManager.ToolsDir, d.Id))
                        || Directory.Exists(Path.Combine(SkillLibrary.SkillsDir, d.Id))
                        || Directory.Exists(Path.Combine(App.RootDirectory, "McpPacks", d.Id));
            if (!found) missing.Add(d);
        }
        return missing;
    }

    public async Task SetEnabledAsync(string id, bool enabled)
    {
        await EnsureLoadedAsync();
        // 停用前先跑 onUnload：RunHookAsync 会拒绝已停用的插件，顺序反了就永远收不到清理信号。
        if (!enabled) await RunHookAsync(id, "onUnload", new { plugin = id });
        // 「仅这一次」的临时允许不该活过一轮停用：重新启用就当重新认识一次。
        if (!enabled) _permissions.ForgetSession(id);
        lock (_gate)
        {
            if (enabled) _disabled.Remove(id);
            else _disabled.Add(id);
            if (_plugins.TryGetValue(id, out var p)) p.Enabled = enabled;
        }
        await _store.Save(StateKey, new PluginState { Disabled = _disabled.ToList() });
        if (enabled) await RunHookAsync(id, "onLoad", new { plugin = id });
    }

    /// <summary>从文件夹安装：复制到 plugins/&lt;id&gt;，扫描并执行 onInstall。</summary>
    public async Task<(bool Ok, string Message)> InstallFromFolderAsync(string sourceDir, bool overwrite = false, string? idHint = null)
    {
        try
        {
            var manifestPath = Path.Combine(sourceDir, "plugin.json");
            if (!File.Exists(manifestPath)) return (false, "这个文件夹里没有 plugin.json。");
            var manifest = JsonSerializer.Deserialize<PluginManifest>(await File.ReadAllTextAsync(manifestPath), Json);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Name)) return (false, "plugin.json 缺少 name。");
            var id = string.IsNullOrWhiteSpace(manifest.Id)
                ? (string.IsNullOrWhiteSpace(idHint) ? Path.GetFileName(sourceDir) : idHint)
                : manifest.Id;
            id = string.Concat(id.Trim().Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
            if (id.Length == 0) id = "plugin_" + Guid.NewGuid().ToString("N")[..6];
            var dest = Path.Combine(PluginsDir, id);
            if (Directory.Exists(dest))
            {
                if (!overwrite) return (false, $"插件「{id}」已经装过了。");
                Directory.Delete(dest, true);
            }
            Directory.CreateDirectory(PluginsDir);
            CopyDir(sourceDir, dest);
            manifest.Id = id;
            File.WriteAllText(Path.Combine(dest, "plugin.json"), JsonSerializer.Serialize(manifest, Json));
            ReleaseCache();
            await EnsureLoadedAsync();
            await RunHookAsync(id, "onInstall", new { plugin = id });
            LastInstalledId = id;
            return (true, $"已安装插件「{manifest.Name}」。");
        }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.Install -> " + ex);
            return (false, "安装失败：" + ex.Message);
        }
    }

    public async Task<(bool Ok, string Message)> InstallFromZipAsync(string zipPath, bool overwrite = false)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "wab_plugin_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tmp);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, tmp, true);
            // 压缩包里可能多套一层目录
            var root = Directory.GetFiles(tmp, "plugin.json", SearchOption.AllDirectories).FirstOrDefault();
            if (root is null) return (false, "压缩包里没有 plugin.json。");
            var srcDir = Path.GetDirectoryName(root)!;
            // plugin.json 在压缩包根目录时，sourceDir 是随机临时目录名；用压缩包名当 ID 才稳定。
            var idHint = string.Equals(Path.GetFullPath(srcDir), Path.GetFullPath(tmp), StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(zipPath)
                : "";
            return await InstallFromFolderAsync(srcDir, overwrite, idHint);
        }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.InstallZip -> " + ex);
            return (false, "安装失败：" + ex.Message);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    private static void CopyDir(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src))
            CopyDir(d, Path.Combine(dest, Path.GetFileName(d)));
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await EnsureLoadedAsync();
        PluginEntry? entry;
        lock (_gate) _plugins.TryGetValue(id, out entry);
        if (entry is null) return false;
        try
        {
            await RunHookAsync(id, "onUnload", new { plugin = id });
            StopCallbackServer(id);
            Directory.Delete(entry.Dir, true);
            lock (_gate) _plugins.Remove(id);
            // 授权跟着插件一起删：留着的话，日后重装一个同名 id 的插件就直接继承了用户给过的允许。
            await _permissions.ResetAsync(id);
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.Delete -> " + ex.Message);
            return false;
        }
    }

    // ==================== 钩子执行 ====================

    /// <summary>执行一个钩子；没有钩子、插件停用或出错都返回 null（错误进崩溃日志）。</summary>
    public async Task<string?> RunHookAsync(string pluginId, string hook, object context)
    {
        await EnsureLoadedAsync();
        PluginEntry? entry;
        lock (_gate) _plugins.TryGetValue(pluginId, out entry);
        if (entry is null || !entry.Enabled) return null;
        // 设置页是硬性契约：三选一没声明就不让跑。否则会出现「装了插件、找不到任何设置」的死角。
        if (!string.IsNullOrEmpty(entry.SettingsIssue)) return null;
        try
        {
            if (!AllowedInSave(PermissionsProvider?.Invoke(), entry.Manifest.Id))
            {
                LogCrash(entry.Manifest.Id,
                    $"[权限] 这份存档没有放行插件「{entry.Manifest.Id}」，钩子 {hook} 已跳过。");
                return null;
            }
        }
        catch { }
        if (!entry.Manifest.Hooks.TryGetValue(hook, out var rel) || string.IsNullOrWhiteSpace(rel)) return null;
        var path = Path.Combine(entry.Dir, rel);
        if (!File.Exists(path))
        {
            LogCrash(pluginId, $"钩子 {hook} 找不到文件：{rel}");
            return null;
        }
        try
        {
            var code = await File.ReadAllTextAsync(path);
            var input = JsonSerializer.Serialize(context);
            var settings = await GetSettingsAsync(pluginId);
            // JS 里的 callTool/callPlugin 会同步等待；钩子留在 UI 线程上等会把它锁死，统一放到线程池里跑。
            // 写设置也在这里同步等完：setSetting 是同步语义，异步飘走会让两条连续写互相盖掉，异常也进不了插件日志。
            var gate = new PermissionGate(this, entry);
            var result = await Task.Run(() => RunJs(code, input, settings,
                (key, value) => SetSettingAsync(pluginId, key, value).GetAwaiter().GetResult(), gate));
            return result;
        }
        catch (Exception ex)
        {
            LogCrash(pluginId, $"钩子 {hook}：{ex.GetType().Name} - {ex.Message}");
            return null;
        }
    }

    /// <summary>把同一个钩子广播给所有启用的插件（场景切换、工具返回这类副作用）。</summary>
    public async Task RunAllHookAsync(string hook, object context)
    {
        if (!_settings.Current.PluginHooksEnabled) return;
        foreach (var p in (await AllAsync()).Where(p => p.Enabled))
        {
            try { await RunHookAsync(p.Manifest.Id, hook, context); }
            catch (Exception ex) { LogCrash(p.Manifest.Id, $"广播钩子 {hook}：{ex.Message}"); }
        }
    }

    /// <summary>
    /// JS 沙箱。每个宿主函数都先过「声明 ∩ 授予」：被拦时函数返回空值或错误 JSON，
    /// 钩子不炸、主程序不炸，原因同时进插件崩溃日志与权限留痕。
    /// 可用：input / result / getSetting / setSetting / log / now / callTool / callPlugin /
    /// fetch / emit / notify / writeFile / readFile / listFiles / deleteFile。
    /// </summary>
    private string? RunJs(string code, string input, IReadOnlyDictionary<string, string> settings,
        Action<string, string> setSetting, PermissionGate gate)
    {
        var entry = gate.Entry;
        var budget = BudgetMs(entry);
        using var cts = new CancellationTokenSource();
        var engine = new Engine(options =>
        {
            // 时间预算自己数，不用 Jint 的 TimeoutInterval：那条从引擎启动算墙上时间，
            // 一次「问用户要不要放行」的等待就能把它吃光，钩子会在答完后的下一条语句上被误杀。
            options.CancellationToken(cts.Token);
            options.MaxStatements(20_000);
            options.LimitMemory(16_000_000);
        });
        // 看门狗只算 JS 真正在跑的时间：gate.PausedMs 把等用户授权的那段单独记着，从总耗时里扣掉。
        var elapsed = Stopwatch.StartNew();
        using var watchdog = new Timer(
            _ => { if (elapsed.ElapsedMilliseconds - gate.PausedMs > budget) cts.Cancel(); },
            null, 25, 25);
        engine.SetValue("input", input);
        engine.SetValue("result", "");
        // settings 是进钩子时的一次性快照，写盘刷不到它。不留这层本地副本，
        // setSetting('k','v') 紧接着 getSetting('k') 会读回旧值——插件作者只会当成「没写进去」。
        var written = new Dictionary<string, string>(StringComparer.Ordinal);
        engine.SetValue("getSetting", new Func<string, string>(k =>
        {
            if (!gate.Allowed(PluginCapabilities.Settings)) return "";
            var name = k ?? "";
            return written.TryGetValue(name, out var fresh) ? fresh
                 : settings.TryGetValue(name, out var v) ? v : "";
        }));
        engine.SetValue("setSetting", new Action<string, string>((k, v) =>
        {
            if (!gate.Allowed(PluginCapabilities.Settings)) return;
            var name = k ?? "";
            // 先落盘再记账：写失败要沿着钩子的异常路径进插件日志，不能留下「JS 以为写好了」的假账。
            setSetting?.Invoke(name, v ?? "");
            written[name] = v ?? "";
        }));
        engine.SetValue("log", new Action<object?>(m =>
        {
            if (!gate.Allowed(PluginCapabilities.Log)) return;
            App.WriteLog("[plugin:" + entry.Manifest.Id + "] " + m);
        }));
        engine.SetValue("now", new Func<string>(() => DateTime.Now.ToString("yyyy-MM-dd HH:mm")));
        if (ToolInvoker is not null)
        {
            engine.SetValue("callTool", new Func<string, string, string>((name, args) =>
            {
                var d = gate.Decide(PluginCapabilities.Tools, name);
                if (!d.Allowed) return ErrorJson(d.Reason);
                try { return ToolInvoker(name ?? "", args ?? "{}").GetAwaiter().GetResult(); }
                catch (Exception ex) { return ErrorJson(ex.Message); }
            }));
        }
        // 插件之间互相调用：callPlugin("other.id", "hook", '{"...":1}')。
        // 嵌套上限是硬的——A 调 B、B 又调 A 时不能把宿主拖进无限递归。
        engine.SetValue("callPlugin", new Func<string, string, string, string>((id, hook, args) =>
        {
            var d = gate.Decide(PluginCapabilities.Plugins, id);
            if (!d.Allowed) return ErrorJson(d.Reason);
            if (Interlocked.Increment(ref _pluginCallDepth) > 4)
            {
                Interlocked.Decrement(ref _pluginCallDepth);
                return ErrorJson("插件互相调用的嵌套太深（上限 4 层）。");
            }
            try
            {
                object context = new { };
                if (!string.IsNullOrWhiteSpace(args))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(args);
                        context = doc.RootElement.Clone();
                    }
                    catch { context = args; }
                }
                // 钩子里的 await 会回到当前同步上下文；这里若在 UI 线程上直接等，文件读完后回不去，整窗死住。
                return Task.Run(() => RunHookAsync(id ?? "", hook ?? "", context)).GetAwaiter().GetResult() ?? "";
            }
            catch (Exception ex)
            {
                return ErrorJson(ex.Message);
            }
            finally { Interlocked.Decrement(ref _pluginCallDepth); }
        }));
        engine.SetValue("fetch", new Func<string, string, string>((url, options) => FetchJson(gate, url, options)));
        engine.SetValue("emit", new Func<string, string, string>((topic, payload) => EmitJson(gate, topic, payload)));
        engine.SetValue("notify", new Func<string, string, string>((title, text) => NotifyJson(gate, title, text)));
        engine.SetValue("writeFile", new Func<string, string, string>((name, text) => WriteFileJson(gate, name, text)));
        engine.SetValue("readFile", new Func<string, string>((name) => ReadFileJson(gate, name)));
        engine.SetValue("listFiles", new Func<string>(() => ListFilesJson(gate)));
        engine.SetValue("deleteFile", new Func<string, string>((name) => DeleteFileJson(gate, name)));
        try
        {
            engine.Execute(code);
        }
        catch (Exception ex) when (cts.IsCancellationRequested)
        {
            LogCrash(entry.Manifest.Id,
                $"JS 超过 {budget}ms 时间预算（等用户授权的时间不算在内），钩子已中断：{ex.GetType().Name}");
            return null;
        }
        var value = engine.GetValue("result");
        return value.IsUndefined() || value.IsNull() ? null : value.ToString();
    }

    private static string ErrorJson(string? reason)
        => JsonSerializer.Serialize(new { error = reason ?? "没有权限" });

    /// <summary>
    /// 一次钩子运行内的授权判定缓存：同一条「能力:对象」只判一次、只记一次，
    /// 免得一个每轮都调的 getSetting 把留痕刷满、把弹窗问烦。
    /// </summary>
    private sealed class PermissionGate
    {
        private readonly PluginHost _host;
        private readonly Dictionary<string, PluginDecision> _memo = new(StringComparer.OrdinalIgnoreCase);
        private readonly Stopwatch _paused = new();
        public PluginEntry Entry { get; }

        public PermissionGate(PluginHost host, PluginEntry entry)
        {
            _host = host;
            Entry = entry;
        }

        public string PluginId => Entry.Manifest.Id;

        /// <summary>等用户授权累计掉的毫秒数——这段时间不能算进钩子的时间预算。</summary>
        public long PausedMs => _paused.ElapsedMilliseconds;

        public PluginDecision Decide(string capability, string? arg = null)
        {
            var key = capability + "|" + (arg ?? "");
            lock (_memo) if (_memo.TryGetValue(key, out var cached)) return cached;
            PluginDecision d;
            // 只有会走「问一次」这条路的能力才真的阻塞；Stopwatch 在等待期间是活着的，
            // 所以看门狗看到的「已用预算」在人没答话时几乎不增长。
            _paused.Start();
            try
            {
                d = _host._permissions
                    .CheckAsync(Entry.Manifest.Id, Entry.Manifest.Name, Entry.Manifest, capability, arg)
                    .GetAwaiter().GetResult();
            }
            finally { _paused.Stop(); }
            lock (_memo) _memo[key] = d;
            if (!d.Allowed)
                _host.LogCrash(Entry.Manifest.Id, "[权限] " + capability
                    + (string.IsNullOrWhiteSpace(arg) ? "" : ":" + arg) + " 被拦：" + d.Reason);
            return d;
        }

        public bool Allowed(string capability, string? arg = null) => Decide(capability, arg).Allowed;
    }

    /// <summary>
    /// 钩子的时间预算。出网与调工具是同步等的，300ms 的 JS 看门狗会在响应回来后的下一条语句上
    /// 把整个钩子掐掉，所以按「声明了会等待的能力」留出预算；语句数与内存上限不跟着放宽。
    /// </summary>
    private static int BudgetMs(PluginEntry entry)
    {
        bool Has(string cap) => PluginPermissions.DeclaredFor(entry.Manifest,
            PluginCapabilities.ByName[cap]);
        if (Has(PluginCapabilities.Network) || Has(PluginCapabilities.LocalNet)) return 12_000;
        if (Has(PluginCapabilities.Tools) || Has(PluginCapabilities.Plugins) || Has(PluginCapabilities.Broadcast)) return 5_000;
        return 300;
    }

    // ==================== 能力实现：网络 / 广播 / 通知 / 文件 ====================

    private const int MaxFetchBytes = 512 * 1024;
    private const int MaxRequestBodyBytes = 64 * 1024;
    private const int MaxPayloadBytes = 64 * 1024;
    private const int MaxStorageFileBytes = 1024 * 1024;
    private const long MaxStorageTotalBytes = 8 * 1024 * 1024;

    private static readonly HttpClient Net = new(new SocketsHttpHandler
    {
        // 不自动跟跳转：302 把请求转到 127.0.0.1 就等于插件用一条 network:api.x.com 摸到了本机。
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(6),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    { Timeout = TimeSpan.FromSeconds(10) };

    private static readonly HashSet<string> BlockedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Transfer-Encoding", "Connection", "Upgrade", "Keep-Alive", "TE", "Trailer"
    };

    private string FetchJson(PermissionGate gate, string rawUrl, string optionsJson)
    {
        try
        {
            var url = (rawUrl ?? "").Trim();
            if (url.Length == 0 || url.Length > 2000) return ErrorJson("地址为空或太长。");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return ErrorJson("不是完整 URL（要带 http:// 或 https://）。");
            if (uri.Scheme is not ("http" or "https")) return ErrorJson("只允许 http / https。");

            var host = uri.IdnHost;
            var d = LooksLocal(host)
                ? gate.Decide(PluginCapabilities.LocalNet, null)
                : gate.Decide(PluginCapabilities.Network, host);
            if (!d.Allowed) return ErrorJson(d.Reason);

            string method = "GET", body = "";
            var headers = new List<(string Name, string Value)>();
            if (!string.IsNullOrWhiteSpace(optionsJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(optionsJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String)
                        method = (m.GetString() ?? "GET").Trim().ToUpperInvariant();
                    if (root.TryGetProperty("body", out var b))
                        body = b.ValueKind == JsonValueKind.String ? (b.GetString() ?? "") : b.GetRawText();
                    if (root.TryGetProperty("headers", out var hs) && hs.ValueKind == JsonValueKind.Object)
                        foreach (var p in hs.EnumerateObject())
                            headers.Add((p.Name, p.Value.ValueKind == JsonValueKind.String
                                ? p.Value.GetString() ?? "" : p.Value.GetRawText()));
                }
                catch (Exception ex) { return ErrorJson("options 不是合法 JSON：" + ex.Message); }
            }
            if (method is not ("GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD"))
                return ErrorJson("不支持的方法：" + method);
            if (System.Text.Encoding.UTF8.GetByteCount(body) > MaxRequestBodyBytes)
                return ErrorJson("请求体超过 64KB。");

            using var req = new HttpRequestMessage(new HttpMethod(method), uri);
            if (body.Length > 0 && method is not ("GET" or "HEAD"))
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            foreach (var (name, value) in headers)
            {
                if (BlockedHeaders.Contains(name)) continue;
                if (name.StartsWith("access-control-", StringComparison.OrdinalIgnoreCase)) continue;
                req.Headers.TryAddWithoutValidation(name, value);
            }

            using var resp = Net.SendAsync(req).GetAwaiter().GetResult();
            var status = (int)resp.StatusCode;
            string text = "";
            bool truncated = false;
            if (method != "HEAD")
            {
                using var stream = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                using var ms = new MemoryStream();
                var buffer = new byte[8192];
                int read;
                while (ms.Length < MaxFetchBytes && (read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    ms.Write(buffer, 0, read);
                if (ms.Length >= MaxFetchBytes)
                {
                    var extra = new byte[1];
                    truncated = stream.Read(extra, 0, 1) > 0;
                }
                text = Encoding.UTF8.GetString(ms.ToArray());
            }
            var location = resp.Headers.Location?.ToString() ?? "";
            return JsonSerializer.Serialize(new
            {
                ok = resp.IsSuccessStatusCode,
                status,
                body = text,
                truncated,
                location,
                error = ""
            });
        }
        catch (TaskCanceledException)
        {
            return ErrorJson("请求超时（10 秒）。");
        }
        catch (Exception ex)
        {
            return ErrorJson("请求失败：" + ex.Message);
        }
    }

    private static bool LooksLocal(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;
        var h = host.Trim('[', ']').TrimEnd('.');
        if (h.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (h.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || h.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)) return true;
        if (IPAddress.TryParse(h, out var literal)) return IsLocalIp(literal);
        try
        {
            // 域名解析后是内网/回环，也算本机访问：否则 network:me 一条就能把请求送到 127.0.0.1 的服务上
            return System.Net.Dns.GetHostAddresses(h).Any(IsLocalIp);
        }
        catch { return false; }   // 解析不了就让 network 那条判定去处理（连接自然也会失败）
    }

    private static bool IsLocalIp(IPAddress a)
    {
        if (IPAddress.IsLoopback(a)) return true;
        if (a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || a.IsIPv6Multicast) return true;
        var b = a.GetAddressBytes();
        if (b.Length == 4)
            return b[0] == 0 || b[0] == 10 || b[0] == 127
                   || (b[0] == 169 && b[1] == 254)
                   || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                   || (b[0] == 192 && b[1] == 168)
                   || b[0] >= 224;
        if (b.Length == 16)
            return b[0] == 0xfc || b[0] == 0xfd || b[0] == 0xfe;
        return false;
    }

    /// <summary>广播：把 payload 送给所有声明了 on:&lt;topic&gt; 且被授权收听这个 topic 的插件。</summary>
    private string EmitJson(PermissionGate gate, string topic, string payload)
    {
        var t = (topic ?? "").Trim();
        if (t.Length == 0 || t.Length > 64) return ErrorJson("话题名要 1~64 个字符。");
        var d = gate.Decide(PluginCapabilities.Broadcast, t);
        if (!d.Allowed) return ErrorJson(d.Reason);
        if (System.Text.Encoding.UTF8.GetByteCount(payload ?? "") > MaxPayloadBytes)
            return ErrorJson("载荷超过 64KB。");
        try
        {
            var delivered = DispatchBroadcastAsync(gate.PluginId, t, payload ?? "").GetAwaiter().GetResult();
            return JsonSerializer.Serialize(new { ok = true, delivered, error = "" });
        }
        catch (Exception ex) { return ErrorJson(ex.Message); }
    }

    private async Task<int> DispatchBroadcastAsync(string fromId, string topic, string payload)
    {
        // A 发 T、B 的监听里再发 T…——两层就够把整个应用绕晕，硬性停在第二层。
        if (Interlocked.Increment(ref _emitDepth) > 2)
        {
            Interlocked.Decrement(ref _emitDepth);
            App.WriteLog("[插件广播] topic=" + topic + " 嵌套超过 2 层，本条已丢弃。");
            return 0;
        }
        try
        {
            var hookName = "on:" + topic;
            var targets = (await AllAsync()).Where(p => p.Enabled
                && !string.Equals(p.Manifest.Id, fromId, StringComparison.OrdinalIgnoreCase)
                && p.Manifest.Hooks.ContainsKey(hookName)).ToList();
            var delivered = 0;
            foreach (var t in targets)
            {
                // 收听方自己也得声明并拿到这个 topic：有人愿意发不等于谁都可以收。
                var d = await _permissions.CheckAsync(t.Manifest.Id, t.Manifest.Name,
                    t.Manifest, PluginCapabilities.Broadcast, topic);
                if (!d.Allowed) continue;
                try
                {
                    await RunHookAsync(t.Manifest.Id, hookName,
                        new { from = fromId, topic, payload, data = payload });
                    delivered++;
                }
                catch (Exception ex) { LogCrash(t.Manifest.Id, $"广播 {topic} 投递失败：{ex.Message}"); }
            }
            return delivered;
        }
        finally { Interlocked.Decrement(ref _emitDepth); }
    }

    private string NotifyJson(PermissionGate gate, string title, string text)
    {
        var d = gate.Decide(PluginCapabilities.Notify, null);
        if (!d.Allowed) return ErrorJson(d.Reason);
        var sender = Notifier;
        if (sender is null) return ErrorJson("主程序没有接上通知通道。");
        try
        {
            sender((title ?? "").Trim(), (text ?? "").Trim()).GetAwaiter().GetResult();
            return JsonSerializer.Serialize(new { ok = true, error = "" });
        }
        catch (Exception ex) { return ErrorJson(ex.Message); }
    }

    private static string StorageDir(PluginEntry entry) => Path.Combine(entry.Dir, "files");

    /// <summary>文件区里的名字：只要一个纯文件名，路径分隔符、盘符、.. 全不要。</summary>
    private static string? SafeFileName(string? raw)
    {
        var name = (raw ?? "").Trim().Replace('\\', '/');
        if (name.Length == 0 || name.Length > 120) return null;
        if (name.Contains('/')) name = name[(name.LastIndexOf('/') + 1)..];
        if (name is "." or ".." || name.Contains(':')) return null;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        return name.Length == 0 ? null : name;
    }

    private string WriteFileJson(PermissionGate gate, string name, string text)
    {
        var d = gate.Decide(PluginCapabilities.Storage, null);
        if (!d.Allowed) return ErrorJson(d.Reason);
        var safe = SafeFileName(name);
        if (safe is null) return ErrorJson("文件名不合法。");
        try
        {
            var dir = StorageDir(gate.Entry);
            var bytes = Encoding.UTF8.GetByteCount(text ?? "");
            if (bytes > MaxStorageFileBytes) return ErrorJson("单文件不能超过 1MB。");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, safe);
            var root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return ErrorJson("文件路径越出了插件自己的文件区。");
            var used = new DirectoryInfo(dir).EnumerateFiles().Sum(f => (long)f.Length);
            if (used + bytes - (File.Exists(path) ? new FileInfo(path).Length : 0) > MaxStorageTotalBytes)
                return ErrorJson("插件文件区已满（上限 8MB）。");
            File.WriteAllText(path, text ?? "");
            return JsonSerializer.Serialize(new { ok = true, bytes, error = "" });
        }
        catch (Exception ex) { return ErrorJson(ex.Message); }
    }

    private string ReadFileJson(PermissionGate gate, string name)
    {
        var d = gate.Decide(PluginCapabilities.Storage, null);
        if (!d.Allowed) return ErrorJson(d.Reason);
        var safe = SafeFileName(name);
        if (safe is null) return ErrorJson("文件名不合法。");
        try
        {
            var root = Path.GetFullPath(StorageDir(gate.Entry)) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(root, safe));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                return JsonSerializer.Serialize(new { ok = false, content = "", error = "" });
            return JsonSerializer.Serialize(new { ok = true, content = File.ReadAllText(path), error = "" });
        }
        catch (Exception ex) { return ErrorJson(ex.Message); }
    }

    private string ListFilesJson(PermissionGate gate)
    {
        var d = gate.Decide(PluginCapabilities.Storage, null);
        if (!d.Allowed) return ErrorJson(d.Reason);
        try
        {
            var dir = StorageDir(gate.Entry);
            if (!Directory.Exists(dir)) return "[]";
            var list = new DirectoryInfo(dir).EnumerateFiles()
                .Select(f => new { name = f.Name, bytes = f.Length, at = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm") })
                .ToList();
            return JsonSerializer.Serialize(list);
        }
        catch (Exception ex) { return ErrorJson(ex.Message); }
    }

    private string DeleteFileJson(PermissionGate gate, string name)
    {
        var d = gate.Decide(PluginCapabilities.Storage, null);
        if (!d.Allowed) return ErrorJson(d.Reason);
        var safe = SafeFileName(name);
        if (safe is null) return ErrorJson("文件名不合法。");
        try
        {
            var root = Path.GetFullPath(StorageDir(gate.Entry)) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(root, safe));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return ErrorJson("路径越出了插件自己的文件区。");
            if (File.Exists(path)) File.Delete(path);
            return JsonSerializer.Serialize(new { ok = true, error = "" });
        }
        catch (Exception ex) { return ErrorJson(ex.Message); }
    }

    /// <summary>人设扩展：按启用顺序执行 persona 钩子；声明 overridePersona 的非空结果会替换后续内容。</summary>
    public async Task<string> PersonaTextAsync(string charId)
    {
        if (!_settings.Current.PluginHooksEnabled) return "";
        var parts = new List<string>();
        foreach (var p in (await AllAsync()).Where(p => p.Enabled))
        {
            // 被 deny 就别跑：persona 钩子唯一的作用是往提示词里加字，跑一遍只留一条崩溃日志没用。
            if (!await AllowAsync(p, PluginCapabilities.PromptWrite)) continue;
            var text = await RunHookAsync(p.Manifest.Id, "persona", new { charId, character = charId });
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (p.Manifest.Overrides.Contains("persona", StringComparer.OrdinalIgnoreCase)
                && await AllowAsync(p, PluginCapabilities.PipelineOverride))
                parts.Clear();
            parts.Add(text.Trim());
        }
        return string.Join("\n", parts);
    }

    public async Task<string> TransformAsync(string hook, string charId, string text, string extraKey = "", string extraValue = "")
    {
        if (!_settings.Current.PluginHooksEnabled) return text;
        var capability = CapabilityOfHook(hook);
        var current = text;
        foreach (var p in (await AllAsync()).Where(p => p.Enabled))
        {
            if (capability.Length > 0 && !await AllowAsync(p, capability)) continue;
            var input = new Dictionary<string, string>
            {
                ["charId"] = charId,
                ["text"] = current,
                ["reply"] = current
            };
            if (extraKey.Length > 0) input[extraKey] = extraValue;
            var result = await RunHookAsync(p.Manifest.Id, hook, input);
            if (!string.IsNullOrWhiteSpace(result)) current = result;
        }
        return current;
    }

    /// <summary>
    /// 钩子名 → 它其实要动用哪一项能力。副作用型钩子（afterTurn、on:topic）不改文本，不额外要能力。
    /// </summary>
    private static string CapabilityOfHook(string hook) => (hook ?? "").ToLowerInvariant() switch
    {
        "persona" or "context" => PluginCapabilities.PromptWrite,
        "beforesend" or "afterreply" => PluginCapabilities.PipelineTransform,
        _ => ""
    };

    /// <summary>宿主侧（不在 JS 沙箱里）的一轮授权判定：拦下时留痕已经写好，这里只要布尔。</summary>
    public async Task<bool> AllowAsync(PluginEntry entry, string capability, string? arg = null)
    {
        var d = await _permissions.CheckAsync(entry.Manifest.Id, entry.Manifest.Name, entry.Manifest, capability, arg);
        if (d.Allowed) return true;
        LogCrash(entry.Manifest.Id, "[权限] " + capability
            + (string.IsNullOrWhiteSpace(arg) ? "" : ":" + arg) + " 被拦：" + d.Reason);
        return false;
    }

    // ==================== 设置 ====================

    // 存储键要相对：StorageProvider 自己会拼数据根并补 .json，
    // 这里传绝对路径的结果是落盘成 settings.json.json——存档打包那句「排除 settings.json」就永远匹配不上，插件密钥跟着存档外流。
    private static string SettingsKey(string id) => "plugins/" + id + "/settings";

    /// <summary>
    /// 旧版本把绝对路径当存储键用，结果落盘成了双扩展名（plugins\state.json.json、某插件的 settings.json.json）。
    /// 键改对之后先搬一次家，否则停用记录和插件设置都会被当成「从没存过」。
    /// </summary>
    private static void MoveDoubledExtension(string legacyPath, string nowPath)
    {
        try
        {
            if (!File.Exists(legacyPath) || File.Exists(nowPath)) return;
            File.Move(legacyPath, nowPath);
            App.WriteLog("PluginHost: 插件文件搬回正确名字 " + Path.GetFileName(legacyPath)
                         + " → " + Path.GetFileName(nowPath) + "（" + Path.GetDirectoryName(legacyPath) + "）");
        }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.MoveDoubledExtension(" + legacyPath + ") -> " + ex.Message);
        }
    }

    public async Task<Dictionary<string, string>> GetSettingsAsync(string id)
    {
        if (!IsSafeId(id)) return new Dictionary<string, string>();
        lock (_gate)
            if (_settingsCache.TryGetValue(id, out var cached)) return new Dictionary<string, string>(cached);
        try
        {
            MoveDoubledExtension(Path.Combine(PluginsDir, id, "settings.json.json"),
                                 Path.Combine(PluginsDir, id, "settings.json"));
            var loaded = await _store.Load<Dictionary<string, string>>(SettingsKey(id))
                         ?? new Dictionary<string, string>();
            lock (_gate) _settingsCache[id] = new Dictionary<string, string>(loaded);
            return loaded;
        }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.Settings -> " + ex.Message);
            return new Dictionary<string, string>();
        }
    }

    public async Task SetSettingAsync(string id, string key, string value)
    {
        if (!IsSafeId(id))
        {
            App.WriteLog("PluginHost.SetSetting -> 拒绝非法插件 id：" + id);
            return;
        }
        var all = await GetSettingsAsync(id);
        all[key ?? ""] = value ?? "";
        lock (_gate) _settingsCache[id] = new Dictionary<string, string>(all);
        await _store.Save(SettingsKey(id), all);
    }

    public async Task SaveSettingsAsync(string id, Dictionary<string, string> values)
    {
        if (!IsSafeId(id))
        {
            App.WriteLog("PluginHost.SaveSettings -> 拒绝非法插件 id：" + id);
            return;
        }
        var merged = await GetSettingsAsync(id);
        foreach (var (k, v) in values) merged[k ?? ""] = v ?? "";
        lock (_gate) _settingsCache[id] = new Dictionary<string, string>(merged);
        await _store.Save(SettingsKey(id), merged);
    }

    /// <summary>打开 HTML 设置：页面上会带上 ?callback=http://127.0.0.1:port/save&amp;plugin=id。</summary>
    public async Task<(bool Ok, string Message)> OpenHtmlSettingsAsync(string id)
    {
        var entry = await FindAsync(id);
        if (entry is null) return (false, "插件不存在。");
        var spec = entry.Manifest.Settings;
        if (spec.Kind != "html") return (false, "这个插件不是网页设置。");
        if (string.IsNullOrWhiteSpace(spec.Page)) return (false, "插件没有填设置页地址。");
        try
        {
            var (port, token) = StartCallbackServer(id);
            var page = spec.Page.Trim();
            string url;
            if (page.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || page.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = page;
            else
            {
                var file = Path.Combine(entry.Dir, page);
                if (!File.Exists(file)) return (false, "找不到设置页文件：" + page);
                url = new Uri(file).AbsoluteUri;
            }
            if (spec.Callback)
            {
                // token 放进 callback 这条 URL 里面，而不是只当页面 URL 的并列参数：
                // 已有页面都是「拿到 callback 就原样 POST」，让它自己记得附 token 等于把它们全弄坏。
                var sep = url.Contains('?') ? '&' : '?';
                url += $"{sep}callback={Uri.EscapeDataString($"http://127.0.0.1:{port}/save?token={token}")}"
                     + $"&plugin={Uri.EscapeDataString(id)}";
            }
            await Launcher.Default.OpenAsync(url);
            return (true, "已在浏览器打开设置页；保存后会写回应用。");
        }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.OpenHtmlSettings -> " + ex);
            return (false, "打开设置页失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 打开插件自带页面（manifest.pages）。页面可以只是一个说明页，也可以带表单，
    /// 通过 callback 参数把值写回插件设置 —— 这是「插件改写页面」的受控入口：
    /// 页面归插件，窗口归系统浏览器，主程序不被替换。
    /// </summary>
    public async Task<(bool Ok, string Message)> OpenPageAsync(string id, int index)
    {
        var entry = await FindAsync(id);
        if (entry is null) return (false, "插件不存在。");
        if (index < 0 || index >= entry.Manifest.Pages.Count) return (false, "没有这个页面。");
        if (!await AllowAsync(entry, PluginCapabilities.Page))
            return (false, "「打开自带页面」这项权限没放行，插件的页面开不了。");
        var page = entry.Manifest.Pages[index];
        var target = string.IsNullOrWhiteSpace(page.File) ? page.Route : page.File;
        if (string.IsNullOrWhiteSpace(target)) return (false, "这个页面的 route/file 都是空的。");
        try
        {
            string url;
            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = target;
            else
            {
                var file = Path.Combine(entry.Dir, target);
                if (!File.Exists(file)) return (false, "找不到页面文件：" + target);
                url = new Uri(file).AbsoluteUri;
            }
            var (port, token) = StartCallbackServer(id);
            var sep = url.Contains('?') ? '&' : '?';
            url += $"{sep}callback={Uri.EscapeDataString($"http://127.0.0.1:{port}/save?token={token}")}"
                 + $"&plugin={Uri.EscapeDataString(id)}";
            await Launcher.Default.OpenAsync(url);
            return (true, $"已在浏览器打开「{page.Title}」。");
        }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.OpenPage -> " + ex);
            return (false, "打开页面失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 回调服务器：只监听 127.0.0.1，处理 POST /save、GET /settings 与 GET /runtime。
    /// 返回一次性 token——回调端口上没有身份验证，谁都连得上；没有 token，
    /// 「任何本机进程/任何被打开的网页」都能往插件设置里写字。
    /// </summary>
    private (int Port, string Token) StartCallbackServer(string pluginId)
    {
        lock (_gate)
        {
            if (_callbackServers.TryGetValue(pluginId, out var existing) && existing.IsListening
                && _callbackPorts.TryGetValue(pluginId, out var runningPort)
                && _callbackTokens.TryGetValue(pluginId, out var runningToken))
                return (runningPort, runningToken);
        }
        var listener = new HttpListener();
        var port = FreePort();
        var token = Guid.NewGuid().ToString("N")[..20];
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        try { listener.Start(); }
        catch
        {
            // HttpListener 在某些机器上需要 URL ACL：退回 TcpListener 太重，这里明确失败
            throw new InvalidOperationException("本机回调端口无法监听（可能被安全软件拦截）。");
        }
        lock (_gate)
        {
            _callbackServers[pluginId] = listener;
            _callbackPorts[pluginId] = port;
            _callbackTokens[pluginId] = token;
        }
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); }
                catch { break; }
                _ = Task.Run(() => HandleCallbackAsync(pluginId, ctx));
            }
        });
        return (port, token);
    }

    private readonly Dictionary<string, int> _callbackPorts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _callbackTokens = new(StringComparer.Ordinal);

    private async Task HandleCallbackAsync(string pluginId, HttpListenerContext ctx)
    {
        var res = ctx.Response;
        // Origin 仍是 *（本地 file:// 页面没有可用的 Origin），身份全靠 URL 里那条一次性 token。
        res.Headers["Access-Control-Allow-Origin"] = "*";
        res.Headers["Access-Control-Allow-Headers"] = "Content-Type";
        res.Headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
        try
        {
            if (ctx.Request.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 204;
                res.Close();
                return;
            }
            var handed = "";
            lock (_gate) _callbackTokens.TryGetValue(pluginId, out handed);
            var given = ctx.Request.QueryString["token"] ?? "";
            if (string.IsNullOrEmpty(handed) || !string.Equals(handed, given, StringComparison.Ordinal))
            {
                await WriteJsonAsync(res, 403, new { ok = false, error = "回调凭据不对（token 缺失或已失效），请重新从应用里打开页面。" });
                return;
            }
            if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url?.AbsolutePath == "/save")
            {
                using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                var values = ParseSettingsBody(body);
                var entry = await FindAsync(pluginId);
                if (entry is not null && !await AllowAsync(entry, PluginCapabilities.Settings))
                {
                    await WriteJsonAsync(res, 403, new { ok = false, error = "「自己的设置」这项权限没放行，写不回设置。" });
                    return;
                }
                await SaveSettingsAsync(pluginId, values);
                await WriteJsonAsync(res, 200, new { ok = true, saved = values.Count });
                return;
            }
            if (ctx.Request.HttpMethod == "GET" && ctx.Request.Url?.AbsolutePath == "/settings")
            {
                var all = await GetSettingsAsync(pluginId);
                await WriteJsonAsync(res, 200, all);
                return;
            }
            if (ctx.Request.HttpMethod == "GET" && ctx.Request.Url?.AbsolutePath == "/runtime")
            {
                await HandleRuntimeAsync(pluginId, res);
                return;
            }
            res.StatusCode = 404;
            res.Close();
        }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.Callback -> " + ex.Message);
            try { res.StatusCode = 500; res.Close(); } catch { }
        }
    }

    /// <summary>
    /// GET /runtime：把这插件此刻的实时值给页面。只读、不缓存、不落盘。
    /// 状态由外部程序经 RuntimeProvider 供进来，页面无法自己证明它是谁给的，所以每条都带上 host。
    /// </summary>
    private async Task HandleRuntimeAsync(string pluginId, HttpListenerResponse res)
    {
        var host = HostName();
        string? live;
        try { live = RuntimeProvider is null ? null : await RuntimeProvider(pluginId); }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.Runtime -> " + ex.Message);
            await WriteJsonAsync(res, 200, new { ok = false, host, error = "取实时状态时出了错：" + ex.Message });
            return;
        }
        if (string.IsNullOrEmpty(live))
        {
            // 没接状态供给不等于页面坏了：要让它区分「这个插件没有实时值」和「取到了但都是空」，后者才是真问题。
            await WriteJsonAsync(res, 200, new { ok = false, host, error = host + "没给这个插件接实时状态，这一栏是「未知」不是「关」。" });
            return;
        }
        JsonObject root;
        try
        {
            root = JsonObject.Parse(live) as JsonObject ?? throw new InvalidOperationException("不是一段 JSON 对象。");
        }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.Runtime.Parse -> " + ex.Message);
            await WriteJsonAsync(res, 200, new { ok = false, host, error = "实时状态不是合法的 JSON 对象：" + ex.Message });
            return;
        }
        root["host"] ??= host;
        var raw = Encoding.UTF8.GetBytes(root.ToJsonString());
        res.StatusCode = 200;
        res.ContentType = "application/json; charset=utf-8";
        res.ContentLength64 = raw.Length;
        await res.OutputStream.WriteAsync(raw);
        res.Close();
    }

    private static string HostName()
    {
        // AppInfo 在 UI 起来之前会抛；一条只读端点不该为它整个 500。
        try { return AppInfo.Current.Name; } catch { return "主程序"; }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse res, int status, object payload)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        res.StatusCode = status;
        res.ContentType = "application/json; charset=utf-8";
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    private static Dictionary<string, string> ParseSettingsBody(string body)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("settings", out var inner)
                && inner.ValueKind == JsonValueKind.Object)
                root = inner;
            if (root.ValueKind != JsonValueKind.Object) return result;
            foreach (var prop in root.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? "",
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => "",
                    _ => prop.Value.ToString()
                };
            }
        }
        catch (Exception ex) { App.WriteLog("PluginHost.ParseSettings -> " + ex.Message); }
        return result;
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public void StopCallbackServer(string id)
    {
        lock (_gate)
        {
            if (_callbackServers.TryGetValue(id, out var listener))
            {
                try { listener.Stop(); listener.Close(); } catch { }
                _callbackServers.Remove(id);
                _callbackPorts.Remove(id);
                // token 一起丢：端口会被下一个插件的回调复用，留着旧凭据等于旧页面还能写新插件的设置。
                _callbackTokens.Remove(id);
            }
        }
    }

    // ==================== 崩溃日志 ====================

    public void LogCrash(string pluginId, string message)
    {
        if (!_settings.Current.PluginCrashLogs) return;
        try
        {
            Directory.CreateDirectory(LogsDir);
            var path = Path.Combine(LogsDir, (pluginId ?? "unknown") + ".log");
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            File.AppendAllText(path, line);
            if (new FileInfo(path).Length > 256 * 1024)
            {
                var text = File.ReadAllText(path);
                File.WriteAllText(path, text[^Math.Min(text.Length, 128 * 1024)..]);
            }
        }
        catch (Exception ex) { App.WriteLog("PluginHost.CrashLog -> " + ex.Message); }
    }

    public async Task<string> CrashLogAsync(string id)
    {
        try
        {
            var path = Path.Combine(LogsDir, id + ".log");
            if (!File.Exists(path)) return "（没有崩溃记录）";
            return await File.ReadAllTextAsync(path);
        }
        catch (Exception ex) { return "读日志失败：" + ex.Message; }
    }

    public void ClearCrashLog(string id)
    {
        try
        {
            var path = Path.Combine(LogsDir, id + ".log");
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    /// <summary>插件设置字段的当前值（native 表单初始化用）。</summary>
    public async Task<Dictionary<string, string>> FieldValuesAsync(PluginEntry entry)
    {
        var saved = await GetSettingsAsync(entry.Manifest.Id);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in entry.Manifest.Settings.Fields)
        {
            if (string.IsNullOrWhiteSpace(f.Key)) continue;
            result[f.Key] = saved.TryGetValue(f.Key, out var v) ? v : (f.Default ?? "");
        }
        return result;
    }

    private sealed class PluginState
    {
        public List<string> Disabled { get; set; } = new();
    }
}
