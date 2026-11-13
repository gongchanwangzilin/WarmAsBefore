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
    /// <summary>text / number / switch / select / multiselect / secret。</summary>
    public string Type { get; set; } = "text";
    public string Default { get; set; } = "";
    public string Help { get; set; } = "";
    public List<string> Options { get; set; } = new();
    /// <summary>可选：select / multiselect 选项的显示名。键仍是原选项值（后端标记不动），只换界面上给人看的文案；
    /// 没写的选项回退显示原值。游戏里的设置页也吃这份映射，两边中文文案一致。</summary>
    [JsonPropertyName("option_labels")]
    public Dictionary<string, string> OptionLabels { get; set; } = new();
}

public sealed class PluginActionSpec
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>可选：点了用系统浏览器打开的本地页面（相对插件目录）或 http(s)。</summary>
    public string Page { get; set; } = "";
}

/// <summary>
/// 插件在「角色档案」里追加的每角色内容：字段（开关/文本）与动作按钮。
/// 与 native 设置同构，但值按 charId 分开存，不挤在全局 settings.json 里。
/// </summary>
public sealed class PluginCharacterProfileSpec
{
    public List<PluginSettingField> Fields { get; set; } = new();
    public List<PluginActionSpec> Actions { get; set; } = new();
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
    /// <summary>可选：往「角色档案」里追加每角色字段与动作按钮（特质、口味等）。</summary>
    [JsonPropertyName("character_profile")]
    public PluginCharacterProfileSpec CharacterProfile { get; set; } = new();
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

    /// <summary>
    /// 插件的身份是 plugin.json 里的 id（UUID），不是文件夹名。这里只把路径不安全字符剔掉；
    /// 缺 id 时才回落到包名，且安装时会把这个值写回清单固定下来，
    /// 保证「同一个包重装」永远拿到同一个身份，不会每次随机生成一个新的。
    /// </summary>
    private static string NormalizeId(string? raw, string fallbackName)
    {
        var id = (raw ?? "").Trim();
        if (!IsSafeId(id))
            id = string.Concat(id.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'));
        if (!IsSafeId(id))
            id = string.Concat((fallbackName ?? "").Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'));
        return IsSafeId(id) ? id : "";
    }

    /// <summary>
    /// 扫描插件目录，得到「已安装 UUID → 目录」。只有带有效 plugin.json 的目录才算已安装；
    /// 删除后剩下的空壳（没清单）不进这张表 —— 列表看不见它，安装判重也不该被它挡住。
    /// 读 id 的规则与 EnsureLoadedAsync 完全一致，避免两处把同一个插件认成两个身份。
    /// </summary>
    private Dictionary<string, string> InstalledDirsById()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!Directory.Exists(PluginsDir)) return map;
            foreach (var dir in Directory.GetDirectories(PluginsDir))
            {
                var manifestPath = Path.Combine(dir, "plugin.json");
                if (!File.Exists(manifestPath)) continue;
                try
                {
                    var m = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), Json);
                    if (m is null || string.IsNullOrWhiteSpace(m.Name)) continue;
                    var folderId = Path.GetFileName(dir);
                    var id = string.IsNullOrWhiteSpace(m.Id) || !IsSafeId(m.Id) ? folderId : m.Id.Trim();
                    if (!string.IsNullOrWhiteSpace(id)) map[id] = dir;
                }
                catch { App.WriteLog("PluginHost.InstalledDirsById -> 读不出清单：" + manifestPath); }
            }
        }
        catch (Exception ex) { App.WriteLog("PluginHost.InstalledDirsById -> " + ex.Message); }
        return map;
    }

    /// <summary>
    /// 删掉一整个插件目录并核验。Windows 上设置页 / 回调 / 杀软常短暂占着文件，
    /// Directory.Delete(dir, true) 会先把能删的都删掉再抛 IOException，留下一个没清单的空壳 ——
    /// 正是「列表里没有插件、重装却说重复」的来源。这里清只读属性、等句柄释放后重试。
    /// </summary>
    private static async Task<bool> RemoveDirRobustAsync(string dir)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (!Directory.Exists(dir)) return true;
            try
            {
                Directory.Delete(dir, true);
                return true;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                }
            }
            catch { }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(80 * (attempt + 1));
        }
        var left = Directory.Exists(dir);
        if (left) App.WriteLog("PluginHost.RemoveDir -> 目录仍被占用：" + dir);
        return !left;
    }

    /// <summary>源目录是否正好是要被删掉的目录（或它里面）——从插件目录里选文件夹安装时会出现。</summary>
    private static bool IsSameOrInside(string path, string dir)
    {
        try
        {
            var p = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            var d = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
            return p.Equals(d, StringComparison.OrdinalIgnoreCase)
                   || p.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
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
    private readonly Dictionary<string, TcpListener> _callbackServers = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;
    private int _pluginCallDepth;

    /// <summary>
    /// 审计 P8：JS 宿主函数（callTool / callPlugin / fetch / setSetting…）都是
    /// GetAwaiter().GetResult() 式阻塞等待，每个在跑的调用都占死一个线程池线程。
    /// 这里给「阻塞等待」本身加并发闸：同时被阻塞的线程数不超过池的一半（至少 2），
    /// 插件链再深也不会把线程池抽干、拖慢 UI 定时器与流式渲染。
    /// </summary>
    private static readonly SemaphoreSlim BlockingWaitGate =
        new(Math.Max(2, Environment.ProcessorCount / 2));

    private static T WithBlockingWait<T>(Func<T> action)
    {
        BlockingWaitGate.Wait();
        try { return action(); }
        finally { BlockingWaitGate.Release(); }
    }

    /// <summary>无返回值的阻塞等待（GetResult() 是 void 的那批宿主函数用）。</summary>
    private static void WithBlockingWait(Action action)
    {
        BlockingWaitGate.Wait();
        try { action(); }
        finally { BlockingWaitGate.Release(); }
    }
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

    /// <summary>
    /// 插件页面动作口（由 RuntimeConfigurator 注入）：页面用带 token 的 POST /action 提交，
    /// 参数是 (pluginId, action, argsJson)，返回直接写给页面的 JSON 字符串。
    /// 宿主在这里只提供通道，不认识任何具体动作；动作语义归插件自己。
    /// </summary>
    public Func<string, string, string, Task<string>>? PageActionInvoker { get; set; }

    /// <summary>
    /// 调角色数值（由 RuntimeConfigurator 注入）：插件 JS 里用 adjustStat(charId, stat, delta)。
    /// 走与主流程同一套钳制与落盘，按角色逐个授权（stats 能力，对象是角色 id）。
    /// 返回错误文案，null 表示改好了。
    /// </summary>
    public Func<string, string, int, Task<string?>>? StatAdjuster { get; set; }

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
            // 设置缓存也必须一起丢：目录被换过之后仍拿着旧字典，读盘就被永久短路，
            // 用户看到的是「每次启动设置都回到默认值」。
            _settingsCache.Clear();
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
                if (!File.Exists(manifestPath))
                {
                    // 上次删除只清掉代码、留下空壳：这个目录本来就不会被当插件装载。
                    // 里面一个文件都没有时顺手收掉，别让空文件夹在插件目录里越攒越多；
                    // 还有残留文件的先留着（可能有人正在手工拷东西），安装时会按 UUID 清理。
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories).Any())
                            Directory.Delete(dir, false);
                    }
                    catch { }
                    continue;
                }
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
            var fallback = string.IsNullOrWhiteSpace(idHint) ? Path.GetFileName(sourceDir) : idHint;
            var id = NormalizeId(manifest.Id, fallback);
            if (id.Length == 0) id = "plugin_" + Guid.NewGuid().ToString("N")[..6];
            var dest = Path.Combine(PluginsDir, id);

            // 判重只认「已安装清单里的 UUID」。旧版拿 Directory.Exists(dest) 当依据，
            // 于是删插件时只要留下一个空壳目录，列表里看不见插件、重装却报「已经装过了」；
            // 反过来，同一个 UUID 装在另一个名字的目录里又会被漏判成两个插件。
            var installed = InstalledDirsById();
            installed.TryGetValue(id, out var existingDir);
            if (existingDir is not null && !overwrite)
                return (false, $"插件「{id}」已经装过了（同一 UUID）。");

            // 设置与角色级字段是用户数据，不属于插件清单。先捞出来、删完目录再放回去，
            // 否则「从 zip 更新一次插件」就把所有开关打回默认值。
            string? keptSettings = null;
            var keptCharFiles = new Dictionary<string, string>();
            var userDataDir = existingDir ?? (Directory.Exists(dest) ? dest : "");
            if (userDataDir.Length > 0 && Directory.Exists(userDataDir))
            {
                var settingsPath = Path.Combine(userDataDir, "settings.json");
                if (File.Exists(settingsPath)) keptSettings = File.ReadAllText(settingsPath);
                var charDir = Path.Combine(userDataDir, "char");
                if (Directory.Exists(charDir))
                    foreach (var file in Directory.GetFiles(charDir, "*", SearchOption.AllDirectories))
                        keptCharFiles[Path.GetRelativePath(charDir, file)] = File.ReadAllText(file);
            }

            // 要清的是「占着这个身份的所有目录」：旧插件本体，以及目标位置上没清单的残留空壳。
            var doomed = new List<string>();
            if (existingDir is not null && Directory.Exists(existingDir)) doomed.Add(existingDir);
            if (Directory.Exists(dest) && !doomed.Contains(dest, StringComparer.OrdinalIgnoreCase)) doomed.Add(dest);

            // 从插件目录里选文件夹安装时，源目录可能正是要删的那个：先搬到临时目录，别把自己删了。
            var source = sourceDir;
            string? tempSource = null;
            if (doomed.Any(d => IsSameOrInside(sourceDir, d)))
            {
                tempSource = Path.Combine(Path.GetTempPath(), "wab_plugin_src_" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(tempSource);
                CopyDir(sourceDir, tempSource);
                source = tempSource;
            }
            try
            {
                foreach (var d in doomed)
                    if (!await RemoveDirRobustAsync(d))
                        return (false, $"旧插件目录清不掉（文件被占用）：{d}。关掉这个插件的设置页再重试。");
                Directory.CreateDirectory(PluginsDir);
                CopyDir(source, dest);
            }
            finally
            {
                if (tempSource is not null)
                    try { Directory.Delete(tempSource, true); } catch { }
            }
            if (keptSettings is not null)
                File.WriteAllText(Path.Combine(dest, "settings.json"), keptSettings);
            foreach (var (rel, text) in keptCharFiles)
            {
                var target = Path.Combine(dest, "char", rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, text);
            }
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
            SafeZip.ExtractToDirectory(zipPath, tmp);
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

        // 目录可能比缓存更「真」：上次删除只清掉代码、留下空壳时，列表里已经没有这个插件，
        // 但目录还在。仍要能按 UUID 把它删干净，否则用户永远重装不上同名的插件。
        var doomed = new List<string>();
        if (entry is not null && Directory.Exists(entry.Dir)) doomed.Add(entry.Dir);
        if (IsSafeId(id))
        {
            var byId = Path.Combine(PluginsDir, id);
            if (Directory.Exists(byId) && !doomed.Contains(byId, StringComparer.OrdinalIgnoreCase)) doomed.Add(byId);
        }
        foreach (var pair in InstalledDirsById())
            if (string.Equals(pair.Key, id, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(pair.Value) && !doomed.Contains(pair.Value, StringComparer.OrdinalIgnoreCase))
                doomed.Add(pair.Value);
        if (doomed.Count == 0 && entry is null) return false;

        try
        {
            if (entry is not null)
            {
                await RunHookAsync(id, "onUnload", new { plugin = id });
                StopCallbackServer(id);
            }
            // 删不干净就明确失败，不能把「代码已删、目录还在」包装成删除成功。
            foreach (var d in doomed)
                if (!await RemoveDirRobustAsync(d))
                {
                    App.WriteLog("PluginHost.Delete -> 目录删不干净：" + d);
                    return false;
                }
            lock (_gate)
            {
                _plugins.Remove(id);
                _settingsCache.Remove(id);
                _disabled.Remove(id);
            }
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
            var charId = TryCharId(input);
            var charSettings = charId.Length > 0
                ? await GetCharacterSettingsAsync(pluginId, charId)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // JS 里的 callTool/callPlugin 会同步等待；钩子留在 UI 线程上等会把它锁死，统一放到线程池里跑。
            // 写设置也在这里同步等完：setSetting 是同步语义，异步飘走会让两条连续写互相盖掉，异常也进不了插件日志。
            var gate = new PermissionGate(this, entry);
            var result = await Task.Run(() => RunJs(code, input, settings, charSettings, charId,
                (key, value) => WithBlockingWait(() => SetSettingAsync(pluginId, key, value).GetAwaiter().GetResult()), gate));
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
    /// fetch / emit / notify / writeFile / readFile / listFiles / deleteFile / adjustStat。
    /// </summary>
    private string? RunJs(string code, string input, IReadOnlyDictionary<string, string> settings,
        IReadOnlyDictionary<string, string> charSettings, string charId,
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
        // 按角色的插件数据（角色档案里的特质/口味）。钩子里用 getCharSetting/setCharSetting 读写当前角色那一份。
        var writtenChar = new Dictionary<string, string>(StringComparer.Ordinal);
        engine.SetValue("getCharSetting", new Func<string, string>(k =>
        {
            if (!gate.Allowed(PluginCapabilities.Settings)) return "";
            var name = k ?? "";
            return writtenChar.TryGetValue(name, out var fresh) ? fresh
                 : charSettings.TryGetValue(name, out var v) ? v : "";
        }));
        engine.SetValue("setCharSetting", new Action<string, string>((k, v) =>
        {
            if (!gate.Allowed(PluginCapabilities.Settings)) return;
            if (charId.Length == 0) return;
            var name = k ?? "";
            WithBlockingWait(() => SaveCharacterSettingsAsync(gate.Entry.Manifest.Id, charId,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [name] = v ?? "" })
                .GetAwaiter().GetResult());
            writtenChar[name] = v ?? "";
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
                try { return WithBlockingWait(() => ToolInvoker(name ?? "", args ?? "{}").GetAwaiter().GetResult()); }
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
                return WithBlockingWait(() => Task.Run(() => RunHookAsync(id ?? "", hook ?? "", context)).GetAwaiter().GetResult()) ?? "";
            }
            catch (Exception ex)
            {
                return ErrorJson(ex.Message);
            }
            finally { Interlocked.Decrement(ref _pluginCallDepth); }
        }));
        if (StatAdjuster is not null)
        {
            // 数值结算的通用口：只按角色 id 授权（stats:charId），改哪个人由插件点名。
            engine.SetValue("adjustStat", new Func<string, string, int, string>((charId, stat, delta) =>
                AdjustStatJson(gate, charId, stat, delta)));
        }
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

    private static string TryCharId(string inputJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("charId", out var c)
                && c.ValueKind == JsonValueKind.String)
                return c.GetString() ?? "";
        }
        catch { }
        return "";
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
                // 这里不加阻塞闸：CheckAsync 可能弹「是否放行」等用户授权，时长不可控，
                // 占住闸位会把其他插件的调用全堵死。看门狗已通过 PausedMs 排除等待时间。
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

            using var resp = WithBlockingWait(() => Net.SendAsync(req).GetAwaiter().GetResult());
            var status = (int)resp.StatusCode;
            string text = "";
            bool truncated = false;
            if (method != "HEAD")
            {
                using var stream = WithBlockingWait(() => resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult());
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
            var delivered = WithBlockingWait(() => DispatchBroadcastAsync(gate.PluginId, t, payload ?? "").GetAwaiter().GetResult());
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
            WithBlockingWait(() => sender((title ?? "").Trim(), (text ?? "").Trim()).GetAwaiter().GetResult());
            return JsonSerializer.Serialize(new { ok = true, error = "" });
        }
        catch (Exception ex) { return ErrorJson(ex.Message); }
    }

    /// <summary>
    /// 调角色数值：先按「stats:角色 id」过授权闸，再走宿主注入的结算（同一套钳制与落盘）。
    /// 主程序没接上结算口时明确说清，不假装改成功。
    /// </summary>
    private string AdjustStatJson(PermissionGate gate, string charId, string stat, int delta)
    {
        var target = (charId ?? "").Trim();
        if (target.Length == 0) return ErrorJson("adjustStat 要带角色 id。");
        var d = gate.Decide(PluginCapabilities.Stats, target);
        if (!d.Allowed) return ErrorJson(d.Reason);
        var adjuster = StatAdjuster;
        if (adjuster is null) return ErrorJson("主程序没有接上数值结算口。");
        try
        {
            var err = WithBlockingWait(() => adjuster(target, (stat ?? "").Trim(), delta).GetAwaiter().GetResult());
            return err is null ? JsonSerializer.Serialize(new { ok = true, error = "" }) : ErrorJson(err);
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

    /// <summary>角色 id 只用作存储键的一段，别让 / 、.. 把它带出插件目录。</summary>
    private static string SafeCharId(string? charId)
    {
        var t = (charId ?? "").Trim();
        if (t.Length == 0) return "unknown";
        if (t.Length > 100) t = t[..100];
        var sb = new StringBuilder(t.Length);
        foreach (var ch in t)
            sb.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_');
        return sb.ToString();
    }

    private static string CharSettingsKey(string id, string charId)
        => "plugins/" + id + "/char/" + SafeCharId(charId);

    /// <summary>某插件在某角色上的档案字段值（原样，未补 default）。</summary>
    public async Task<Dictionary<string, string>> GetCharacterSettingsAsync(string id, string charId)
    {
        if (!IsSafeId(id) || string.IsNullOrWhiteSpace(charId))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            return await _store.Load<Dictionary<string, string>>(CharSettingsKey(id, charId))
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.CharSettings -> " + ex.Message);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task SaveCharacterSettingsAsync(string id, string charId, Dictionary<string, string> values)
    {
        if (!IsSafeId(id) || string.IsNullOrWhiteSpace(charId)) return;
        var merged = await GetCharacterSettingsAsync(id, charId);
        foreach (var (k, v) in values) merged[k ?? ""] = v ?? "";
        await _store.Save(CharSettingsKey(id, charId), merged);
    }

    /// <summary>角色档案里该插件字段的当前值（缺省用字段 default 补）。</summary>
    public async Task<Dictionary<string, string>> CharacterValuesAsync(PluginEntry entry, string charId)
    {
        var saved = await GetCharacterSettingsAsync(entry.Manifest.Id, charId);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in entry.Manifest.CharacterProfile.Fields)
        {
            if (string.IsNullOrWhiteSpace(f.Key)) continue;
            result[f.Key] = saved.TryGetValue(f.Key, out var v) ? v : (f.Default ?? "");
        }
        return result;
    }

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
            // 空结果别钉进缓存：启动那一瞬文件若还没就绪，缓存一个空字典会让整个进程
            // 都读不到盘上的设置，UI 全回落成默认值，下一次保存还会把默认值整表写回去。
            if (loaded.Count > 0)
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

    /// <summary>HTML 设置页的可打开地址；页面上会带上 ?callback=http://127.0.0.1:port/save&amp;plugin=id。</summary>
    public async Task<(bool Ok, string Message, string Url)> GetHtmlSettingsUrlAsync(string id)
    {
        var entry = await FindAsync(id);
        if (entry is null) return (false, "插件不存在。", "");
        var spec = entry.Manifest.Settings;
        if (spec.Kind != "html") return (false, "这个插件不是网页设置。", "");
        if (string.IsNullOrWhiteSpace(spec.Page)) return (false, "插件没有填设置页地址。", "");
        try
        {
            return await PrepareUrlAsync(id, entry, spec.Page.Trim(), spec.Callback);
        }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.GetHtmlSettingsUrl -> " + ex);
            return (false, "打开设置页失败：" + ex.Message, "");
        }
    }

    /// <summary>
    /// 插件自带页面（manifest.pages）的可打开地址。页面可以只是一个说明页，也可以带表单，
    /// 通过 callback 参数把值写回插件设置 —— 这是「插件改写页面」的受控入口。
    /// </summary>
    public async Task<(bool Ok, string Message, string Url)> GetPageUrlAsync(string id, int index)
    {
        var entry = await FindAsync(id);
        if (entry is null) return (false, "插件不存在。", "");
        if (index < 0 || index >= entry.Manifest.Pages.Count) return (false, "没有这个页面。", "");
        if (!await AllowAsync(entry, PluginCapabilities.Page))
            return (false, "「打开自带页面」这项权限没放行，插件的页面开不了。", "");
        var page = entry.Manifest.Pages[index];
        var target = string.IsNullOrWhiteSpace(page.File) ? page.Route : page.File;
        if (string.IsNullOrWhiteSpace(target)) return (false, "这个页面的 route/file 都是空的。", "");
        try
        {
            return await PrepareUrlAsync(id, entry, target, true);
        }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.GetPageUrl -> " + ex);
            return (false, "打开页面失败：" + ex.Message, "");
        }
    }

    private async Task<(bool Ok, string Message, string Url)> PrepareUrlAsync(string id, PluginEntry entry, string target, bool callback)
    {
        string url;
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = target;
        else
        {
            var file = Path.Combine(entry.Dir, target);
            if (!File.Exists(file)) return (false, "找不到页面文件：" + target, "");
            var (port, _) = StartCallbackServer(id);
            // 本地页面走本机回环 HTTP，不走 file://：file 地址后面的查询串在各家浏览器里行为不一，
            // 而且 file 源在 WebView 里读不到 callback，相对资源也容易断。
            var rel = Path.GetRelativePath(entry.Dir, file)
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString);
            url = $"http://127.0.0.1:{port}/page/" + string.Join('/', rel);
        }
        if (callback)
        {
            var (port, token) = StartCallbackServer(id);
            // token 放进 callback 这条 URL 里面，而不是只当页面 URL 的并列参数：
            // 已有页面都是「拿到 callback 就原样 POST」，让它自己记得附 token 等于把它们全弄坏。
            var sep = url.Contains('?') ? '&' : '?';
            url += $"{sep}callback={Uri.EscapeDataString($"http://127.0.0.1:{port}/save?token={token}")}"
                 + $"&plugin={Uri.EscapeDataString(id)}";
        }
        return (true, "", url);
    }

    /// <summary>用系统默认浏览器打开。返回是否真的递出去了——Process.Start 静默失败时页面不会自己报错。</summary>
    public async Task<(bool Ok, string Message)> OpenInBrowserAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return (false, "没有可打开的地址。");
        return await OpenExternalAsync(url)
            ? (true, "已在浏览器打开。")
            : (false, "交给系统浏览器失败，可以改用应用内打开。");
    }

    public async Task<(bool Ok, string Message)> OpenHtmlSettingsAsync(string id)
    {
        var (ok, message, url) = await GetHtmlSettingsUrlAsync(id);
        if (!ok) return (false, message);
        if (!await OpenExternalAsync(url)) return (false, "交给系统浏览器失败，可以改用应用内打开。");
        return (true, "已在浏览器打开设置页；保存后会写回应用。");
    }

    public async Task<(bool Ok, string Message)> OpenPageAsync(string id, int index)
    {
        var (ok, message, url) = await GetPageUrlAsync(id, index);
        if (!ok) return (false, message);
        if (!await OpenExternalAsync(url)) return (false, "交给系统浏览器失败，可以改用应用内打开。");
        var entry = await FindAsync(id);
        var title = entry is null || index >= entry.Manifest.Pages.Count ? "页面" : entry.Manifest.Pages[index].Title;
        return (true, $"已在浏览器打开「{title}」。");
    }

    /// <summary>
    /// 回调服务器：只监听 127.0.0.1，处理 POST /save、GET /settings、GET /runtime，
    /// 以及 GET /page/…（插件自己的静态页面与资源）。
    /// 写设置要一次性 token——回调端口上没有身份验证，谁都连得上；没有 token，
    /// 「任何本机进程/任何被打开的网页」都能往插件设置里写字。静态页面只读，不发 token。
    /// </summary>
    private (int Port, string Token) StartCallbackServer(string pluginId)
    {
        lock (_gate)
        {
            if (_callbackServers.TryGetValue(pluginId, out _)
                && _callbackPorts.TryGetValue(pluginId, out var rp)
                && _callbackTokens.TryGetValue(pluginId, out var rt))
                return (rp, rt);
        }
        // 不用 HttpListener：Windows 上它监听 127.0.0.1 需要 URL ACL，非管理员会被拒（报错像「被安全软件拦截」）。
        // TcpListener 不需要任何特权；HTTP 走极简手写解析，够这三个端点用。
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var token = Guid.NewGuid().ToString("N")[..20];
        lock (_gate)
        {
            _callbackServers[pluginId] = listener;
            _callbackPorts[pluginId] = port;
            _callbackTokens[pluginId] = token;
        }
        _ = Task.Run(async () =>
        {
            while (true)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); }
                catch { break; }
                _ = Task.Run(() => HandleCallbackAsync(pluginId, client));
            }
        });
        return (port, token);
    }

    private readonly Dictionary<string, int> _callbackPorts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _callbackTokens = new(StringComparer.Ordinal);

    private async Task HandleCallbackAsync(string pluginId, TcpClient client)
    {
        using var _c = client;
        try
        {
            using var stream = client.GetStream();
            var (header, bodyBytes) = await ReadHttpAsync(stream);
            if (header is null) return;
            var parts = (header.Split("\r\n").FirstOrDefault() ?? "").Split(' ');
            if (parts.Length < 2) { await WriteHttpAsync(stream, 400, "{\"ok\":false}"); return; }
            var method = parts[0].ToUpperInvariant();
            var target = parts[1];
            var q = target.IndexOf('?');
            var path = q >= 0 ? target[..q] : target;
            var query = q >= 0 ? target[(q + 1)..] : "";

            if (method == "OPTIONS") { await WriteHttpAsync(stream, 204, ""); return; }

            // 静态页面与资源不校验 token：浏览器随后会为 <link>/<script> 单独发请求，带不上查询串。
            // 只读、只出插件目录里的文件，写设置的那几个端点仍然要 token。
            if (method == "GET" && path.StartsWith("/page/", StringComparison.Ordinal))
            {
                await ServePageAsync(pluginId, path["/page/".Length..], stream);
                return;
            }

            var given = QueryValue(query, "token");
            string handed;
            lock (_gate) _callbackTokens.TryGetValue(pluginId, out handed!);
            if (string.IsNullOrEmpty(handed) || !string.Equals(handed, given, StringComparison.Ordinal))
            {
                await WriteHttpAsync(stream, 403, JsonSerializer.Serialize(new { ok = false, error = "回调凭据不对（token 缺失或已失效），请重新从应用里打开页面。" }));
                return;
            }

            if (method == "POST" && path == "/save")
            {
                var values = ParseSettingsBody(Encoding.UTF8.GetString(bodyBytes));
                var entry = await FindAsync(pluginId);
                if (entry is not null && !await AllowAsync(entry, PluginCapabilities.Settings))
                {
                    await WriteHttpAsync(stream, 403, JsonSerializer.Serialize(new { ok = false, error = "「自己的设置」这项权限没放行，写不回设置。" }));
                    return;
                }
                await SaveSettingsAsync(pluginId, values);
                await WriteHttpAsync(stream, 200, JsonSerializer.Serialize(new { ok = true, saved = values.Count }));
                return;
            }
            if (method == "GET" && path == "/settings")
            {
                await WriteHttpAsync(stream, 200, JsonSerializer.Serialize(await GetSettingsAsync(pluginId)));
                return;
            }
            if (method == "GET" && path == "/runtime")
            {
                await WriteHttpAsync(stream, 200, await BuildRuntimeJsonAsync(pluginId));
                return;
            }
            if (method == "POST" && path == "/action")
            {
                // 通用动作口：token 已在上面校验过。宿主不认识动作内容，整包交给插件自己解释。
                var invoker = PageActionInvoker;
                var answer = invoker is null
                    ? "{\"ok\":false,\"error\":\"这个插件没有接页面动作。\"}"
                    : await invoker(pluginId, QueryValue(query, "action") ?? "", Encoding.UTF8.GetString(bodyBytes));
                await WriteHttpAsync(stream, 200, answer);
                return;
            }
            await WriteHttpAsync(stream, 404, "{\"ok\":false}");
        }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.Callback -> " + ex.Message);
        }
    }

    /// <summary>把 /page/ 下的路径映射到插件目录里的文件，越界一律拒绝。</summary>
    private async Task ServePageAsync(string pluginId, string relPath, NetworkStream stream)
    {
        var entry = await FindAsync(pluginId);
        if (entry is null) { await WriteHttpAsync(stream, 404, "{\"ok\":false}"); return; }
        string full;
        try
        {
            var rel = Uri.UnescapeDataString(relPath).Replace('/', Path.DirectorySeparatorChar);
            var root = Path.GetFullPath(entry.Dir);
            full = Path.GetFullPath(Path.Combine(root, rel));
            var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            {
                await WriteHttpAsync(stream, 403, "{\"ok\":false}");
                return;
            }
        }
        catch
        {
            await WriteHttpAsync(stream, 400, "{\"ok\":false}");
            return;
        }
        if (!File.Exists(full)) { await WriteHttpAsync(stream, 404, "{\"ok\":false}"); return; }
        byte[] bytes;
        try { bytes = await File.ReadAllBytesAsync(full); }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.ServePage -> " + ex.Message);
            await WriteHttpAsync(stream, 500, "{\"ok\":false}");
            return;
        }
        await WriteHttpAsync(stream, 200, bytes, ContentTypeFor(full));
    }

    private static string ContentTypeFor(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".txt" or ".md" => "text/plain; charset=utf-8",
        ".wasm" => "application/wasm",
        _ => "application/octet-stream"
    };

    private static string QueryValue(string query, string key)
    {
        foreach (var pair in (query ?? "").Split('&'))
        {
            var i = pair.IndexOf('=');
            if (i <= 0) continue;
            if (!pair[..i].Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            return pair[(i + 1)..];
        }
        return "";
    }

    private static async Task WriteHttpAsync(NetworkStream stream, int status, string body)
        => await WriteHttpAsync(stream, status, Encoding.UTF8.GetBytes(body ?? ""), "application/json; charset=utf-8");

    private static async Task WriteHttpAsync(NetworkStream stream, int status, byte[] bytes, string contentType)
    {
        var reason = status switch
        {
            200 => "OK", 204 => "No Content", 400 => "Bad Request",
            403 => "Forbidden", 404 => "Not Found", 500 => "Internal Server Error", _ => "OK"
        };
        var head = "HTTP/1.1 " + status + " " + reason + "\r\n"
                   + "Access-Control-Allow-Origin: *\r\n"
                   + "Access-Control-Allow-Headers: Content-Type\r\n"
                   + "Access-Control-Allow-Methods: GET,POST,OPTIONS\r\n"
                   + "Cache-Control: no-store\r\n"
                   + "Content-Type: " + contentType + "\r\n"
                   + "Content-Length: " + bytes.Length + "\r\n"
                   + "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        if (bytes.Length > 0) await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private static async Task<(string? Header, byte[] Body)> ReadHttpAsync(NetworkStream stream)
    {
        var ms = new MemoryStream();
        var buf = new byte[4096];
        var end = -1;
        while (ms.Length < 65536)
        {
            var n = await stream.ReadAsync(buf, 0, buf.Length);
            if (n <= 0) break;
            ms.Write(buf, 0, n);
            end = FindCrlfCrlf(ms.GetBuffer(), (int)ms.Length);
            if (end >= 0) break;
        }
        if (end < 0) return (null, Array.Empty<byte>());
        var header = Encoding.ASCII.GetString(ms.GetBuffer(), 0, end);
        var contentLength = 0;
        foreach (var line in header.Split("\r\n"))
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line["Content-Length:".Length..].Trim(), out var cl))
                contentLength = cl;
        var all = ms.ToArray();
        var body = new List<byte>(Math.Max(0, contentLength));
        for (var i = end + 4; i < all.Length && body.Count < contentLength; i++) body.Add(all[i]);
        while (body.Count < contentLength)
        {
            var n = await stream.ReadAsync(buf, 0, Math.Min(buf.Length, contentLength - body.Count));
            if (n <= 0) break;
            for (var i = 0; i < n && body.Count < contentLength; i++) body.Add(buf[i]);
        }
        return (header, body.ToArray());
    }

    private static int FindCrlfCrlf(byte[] data, int len)
    {
        for (var i = 0; i + 3 < len; i++)
            if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10) return i;
        return -1;
    }

    /// <summary>
    /// GET /runtime：把这插件此刻的实时值给页面。只读、不缓存、不落盘。
    /// 状态由外部程序经 RuntimeProvider 供进来，页面无法自己证明它是谁给的，所以每条都带上 host。
    /// </summary>
    private async Task<string> BuildRuntimeJsonAsync(string pluginId)
    {
        var host = HostName();
        string? live;
        try { live = RuntimeProvider is null ? null : await RuntimeProvider(pluginId); }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.Runtime -> " + ex.Message);
            return JsonSerializer.Serialize(new { ok = false, host, error = "取实时状态时出了错：" + ex.Message });
        }
        if (string.IsNullOrEmpty(live))
            // 没接状态供给不等于页面坏了：要让它区分「这个插件没有实时值」和「取到了但都是空」，后者才是真问题。
            return JsonSerializer.Serialize(new { ok = false, host, error = host + "没给这个插件接实时状态，这一栏是「未知」不是「关」。" });
        JsonObject root;
        try
        {
            root = JsonObject.Parse(live) as JsonObject ?? throw new InvalidOperationException("不是一段 JSON 对象。");
        }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.Runtime.Parse -> " + ex.Message);
            return JsonSerializer.Serialize(new { ok = false, host, error = "实时状态不是合法的 JSON 对象：" + ex.Message });
        }
        root["host"] ??= host;
        return root.ToJsonString();
    }

    private static string HostName()
    {
        // AppInfo 在 UI 起来之前会抛；一条只读端点不该为它整个 500。
        try { return AppInfo.Current.Name; } catch { return "主程序"; }
    }

    /// <summary>
    /// 用系统默认浏览器打开链接。桌面端 Launcher.Default 有时静默失败（点了没反应），
    /// Windows 上直接用 ShellExecute 交给默认浏览器更可靠。
    /// 自检可把 <see cref="ExternalOpener"/> 换成只记地址的实现，免得跑测试时真的弹浏览器。
    /// </summary>
    public static Func<string, Task<bool>>? ExternalOpener { get; set; }

    private static async Task<bool> OpenExternalAsync(string url)
    {
        try
        {
            var opener = ExternalOpener;
            if (opener is not null) return await opener(url);
            if (OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                return true;
            }
            return await Launcher.Default.OpenAsync(url);
        }
        catch (Exception ex)
        {
            App.WriteLog("PluginHost.OpenExternal -> " + ex.Message);
            return false;
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
                try { listener.Stop(); } catch { }
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
