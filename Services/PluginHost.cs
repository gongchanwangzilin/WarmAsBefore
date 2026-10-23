using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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
    private readonly object _gate = new();
    private readonly Dictionary<string, PluginEntry> _plugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _disabled = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _settingsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HttpListener> _callbackServers = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;
    private int _pluginCallDepth;

    /// <summary>调用工具（由 RuntimeConfigurator 注入）：插件 JS 里用 callTool(name, argsJson)。</summary>
    public Func<string, string, Task<string>>? ToolInvoker { get; set; }

    /// <summary>当前存档的插件白名单；null = 不限制。</summary>
    public Func<Models.SavePermissions?>? PermissionsProvider { get; set; }

    public PluginHost(StorageProvider store, SettingsManager settings)
    {
        _store = store;
        _settings = settings;
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
            var result = await Task.Run(() =>
                RunJs(code, input, settings, (key, value) => SetSettingAsync(pluginId, key, value)));
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

    /// <summary>JS 沙箱：可用 input（JSON）、getSetting/setSetting/log/callTool/now，结果写进 result。</summary>
    private string? RunJs(string code, string input, IReadOnlyDictionary<string, string> settings,
        Action<string, string> setSetting)
    {
        var engine = new Engine(options =>
        {
            options.TimeoutInterval(TimeSpan.FromMilliseconds(300));
            options.MaxStatements(20_000);
            options.LimitMemory(16_000_000);
        });
        engine.SetValue("input", input);
        engine.SetValue("result", "");
        engine.SetValue("getSetting", new Func<string, string>(k =>
            settings.TryGetValue(k ?? "", out var v) ? v : ""));
        engine.SetValue("setSetting", new Action<string, string>((k, v) => setSetting?.Invoke(k ?? "", v ?? "")));
        engine.SetValue("log", new Action<object?>(m => App.WriteLog("[plugin] " + m)));
        engine.SetValue("now", new Func<string>(() => DateTime.Now.ToString("yyyy-MM-dd HH:mm")));
        if (ToolInvoker is not null)
        {
            engine.SetValue("callTool", new Func<string, string, string>((name, args) =>
            {
                try { return ToolInvoker(name ?? "", args ?? "{}").GetAwaiter().GetResult(); }
                catch (Exception ex) { return JsonSerializer.Serialize(new { error = ex.Message }); }
            }));
        }
        // 插件之间互相调用：callPlugin("other.id", "hook", '{"...":1}')。
        // 嵌套上限是硬的——A 调 B、B 又调 A 时不能把宿主拖进无限递归。
        engine.SetValue("callPlugin", new Func<string, string, string, string>((id, hook, args) =>
        {
            if (Interlocked.Increment(ref _pluginCallDepth) > 4)
            {
                Interlocked.Decrement(ref _pluginCallDepth);
                return JsonSerializer.Serialize(new { error = "插件互相调用的嵌套太深（上限 4 层）。" });
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
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
            finally { Interlocked.Decrement(ref _pluginCallDepth); }
        }));
        engine.Execute(code);
        var value = engine.GetValue("result");
        return value.IsUndefined() || value.IsNull() ? null : value.ToString();
    }

    /// <summary>人设扩展：按启用顺序执行 persona 钩子；声明 overridePersona 的非空结果会替换后续内容。</summary>
    public async Task<string> PersonaTextAsync(string charId)
    {
        if (!_settings.Current.PluginHooksEnabled) return "";
        var parts = new List<string>();
        foreach (var p in (await AllAsync()).Where(p => p.Enabled))
        {
            var text = await RunHookAsync(p.Manifest.Id, "persona", new { charId, character = charId });
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (p.Manifest.Overrides.Contains("persona", StringComparer.OrdinalIgnoreCase))
                    parts.Clear();
                parts.Add(text.Trim());
            }
        }
        return string.Join("\n", parts);
    }

    public async Task<string> TransformAsync(string hook, string charId, string text, string extraKey = "", string extraValue = "")
    {
        if (!_settings.Current.PluginHooksEnabled) return text;
        var current = text;
        foreach (var p in (await AllAsync()).Where(p => p.Enabled))
        {
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
            var port = StartCallbackServer(id);
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
                var sep = url.Contains('?') ? '&' : '?';
                url += $"{sep}callback={Uri.EscapeDataString($"http://127.0.0.1:{port}/save")}&plugin={Uri.EscapeDataString(id)}";
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
            var port = StartCallbackServer(id);
            var sep = url.Contains('?') ? '&' : '?';
            url += $"{sep}callback={Uri.EscapeDataString($"http://127.0.0.1:{port}/save")}&plugin={Uri.EscapeDataString(id)}";
            await Launcher.Default.OpenAsync(url);
            return (true, $"已在浏览器打开「{page.Title}」。");
        }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.OpenPage -> " + ex);
            return (false, "打开页面失败：" + ex.Message);
        }
    }

    /// <summary>回调服务器：只监听 127.0.0.1，处理 POST /save 与 GET /settings。</summary>
    private int StartCallbackServer(string pluginId)
    {
        lock (_gate)
        {
            if (_callbackServers.TryGetValue(pluginId, out var existing) && existing.IsListening
                && _callbackPorts.TryGetValue(pluginId, out var runningPort))
                return runningPort;
        }
        var listener = new HttpListener();
        var port = FreePort();
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
        return port;
    }

    private readonly Dictionary<string, int> _callbackPorts = new(StringComparer.OrdinalIgnoreCase);

    private async Task HandleCallbackAsync(string pluginId, HttpListenerContext ctx)
    {
        var res = ctx.Response;
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
            if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url?.AbsolutePath == "/save")
            {
                using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                var values = ParseSettingsBody(body);
                await SaveSettingsAsync(pluginId, values);
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { ok = true, saved = values.Count }));
                res.ContentType = "application/json; charset=utf-8";
                res.ContentLength64 = bytes.Length;
                await res.OutputStream.WriteAsync(bytes);
                res.Close();
                return;
            }
            if (ctx.Request.HttpMethod == "GET" && ctx.Request.Url?.AbsolutePath == "/settings")
            {
                var all = await GetSettingsAsync(pluginId);
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(all));
                res.ContentType = "application/json; charset=utf-8";
                res.ContentLength64 = bytes.Length;
                await res.OutputStream.WriteAsync(bytes);
                res.Close();
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
