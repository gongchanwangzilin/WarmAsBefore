using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.ApiManager;
using WarmAsBefore.Modules.Mcp;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 插件中心：装、停、删、依赖、设置、崩溃日志，以及「说一句话让 AI 写个插件」的工坊。
///
/// 工坊不是直接往 plugins 目录里丢文件：先生成到临时目录，走 <see cref="PluginHost.InstallFromFolderAsync"/>
/// 的同一条安装路径（校验 plugin.json、清 id、执行 onInstall），这样工坊产物和手动安装的插件没有两样。
/// </summary>
public sealed partial class PluginCenterViewModel : ObservableObject
{
    private readonly PluginHost _plugins;
    private readonly ApiGateway _api;
    private readonly McpOrchestrator _mcp;

    public ObservableCollection<PluginRow> Rows { get; } = new();
    public ObservableCollection<McpServerRow> McpServers { get; } = new();

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _workshopPrompt = "";
    [ObservableProperty] private string _workshopResult = "";
    [ObservableProperty] private string _mcpPrompt = "";
    [ObservableProperty] private string _mcpResult = "";
    [ObservableProperty] private bool _mcpBusy;
    public bool HasMcpServers => McpServers.Count > 0;

    public PluginCenterViewModel(PluginHost plugins, ApiGateway api, McpOrchestrator mcp)
    {
        _plugins = plugins;
        _api = api;
        _mcp = mcp;
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            var list = await _plugins.AllAsync();
            Rows.Clear();
            foreach (var p in list)
            {
                var settingsKind = p.Manifest.Settings?.Kind ?? "";
                // 一行摘要就够：用户要的是「这个插件能用几件事、我改过没有」，细节在权限台里。
                var caps = await _plugins.Permissions.RowsAsync(p.Manifest.Id, p.Manifest);
                var declaredCount = caps.Count(c => c.Declared);
                var usableCount = caps.Count(c => c.Declared && c.Policy != PluginGrant.Deny);
                var customCount = caps.Count(c => c.Dirty);
                Rows.Add(new PluginRow
                {
                    PermissionLabel = $"权限：声明 {declaredCount} 项 · 可用 {usableCount} 项"
                        + (customCount == 0 ? " · 全按默认" : $" · 你改过 {customCount} 项"),
                    Id = p.Manifest.Id,
                    Name = p.Manifest.Name,
                    Version = p.Manifest.Version,
                    Author = p.Manifest.Author,
                    Description = p.Manifest.Description,
                    Enabled = p.Enabled,
                    SettingsKind = settingsKind,
                    SettingsKindLabel = settingsKind switch
                    {
                        "native" => "应用内设置",
                        "html" => "网页设置",
                        "none" => "无需设置",
                        _ => "缺少设置声明"
                    },
                    HookLabel = p.Manifest.Hooks.Count == 0 ? "无钩子" : string.Join(" · ", p.Manifest.Hooks.Keys),
                    FirstHook = p.Manifest.Hooks.Keys.FirstOrDefault() ?? "",
                    DependenciesLabel = p.MissingDependencies.Count == 0
                        ? (p.Manifest.Dependencies.Count == 0 ? "" : "依赖已满足")
                        : "缺少依赖：" + string.Join("、", p.MissingDependencies.Select(d => d.Id)),
                    MissingUrls = p.MissingDependencies.Where(d => !string.IsNullOrWhiteSpace(d.Url))
                        .Select(d => d.Url).ToList(),
                    Issue = p.SettingsIssue,
                    CanRun = p.Enabled && string.IsNullOrEmpty(p.SettingsIssue),
                    HasPages = p.Manifest.Pages.Count > 0,
                    PagesLabel = p.Manifest.Pages.Count == 0
                        ? ""
                        : string.Join("、", p.Manifest.Pages.Select(pg => string.IsNullOrWhiteSpace(pg.Title) ? pg.Route : pg.Title)),
                    OverridesLabel = p.Manifest.Overrides.Count == 0
                        ? ""
                        : "接管：" + string.Join("、", p.Manifest.Overrides)
                });
            }
            Status = Rows.Count == 0
                ? "还没有插件。可以用下面的工坊生成一个，或从 zip / 文件夹安装。"
                : $"共 {Rows.Count} 个插件，{Rows.Count(r => r.CanRun)} 个正在生效。";
            RefreshMcp();
        }
        catch (Exception ex)
        {
            Status = "读取失败：" + ex.Message;
            App.WriteLog("PluginCenter.Refresh -> " + ex);
        }
    }

    [RelayCommand]
    private async Task InstallZip()
    {
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "选择插件 zip" });
            if (pick is null) return;
            Busy = true;
            var (ok, message) = await _plugins.InstallFromZipAsync(pick.FullPath, overwrite: true);
            Status = message;
            if (!ok) await Shell.Current.DisplayAlert("安装插件", message, "好");
            if (ok) await OfferMissingDependenciesAsync();
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Status = "安装失败：" + ex.Message;
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task Toggle(PluginRow? row)
    {
        if (row is null) return;
        await _plugins.SetEnabledAsync(row.Id, !row.Enabled);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task Delete(PluginRow? row)
    {
        if (row is null) return;
        if (!await Shell.Current.DisplayAlert("删除插件", $"删除「{row.Name}」的代码与设置？这个操作不可恢复。", "删除", "取消")) return;
        await _plugins.DeleteAsync(row.Id);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task OpenSettings(PluginRow? row)
    {
        if (row is null) return;
        if (row.SettingsKind == "html")
        {
            var (ok, message) = await _plugins.OpenHtmlSettingsAsync(row.Id);
            Status = message;
            if (!ok) await Shell.Current.DisplayAlert("插件设置", message, "好");
            return;
        }
        if (row.SettingsKind == "native")
        {
            await Shell.Current.GoToAsync($"pluginSettings?id={Uri.EscapeDataString(row.Id)}");
            return;
        }
        await Shell.Current.DisplayAlert("插件设置", "这个插件声明了「无需设置」，没有可改的项。", "好");
    }

    [RelayCommand]
    private async Task OpenPerms(PluginRow? row)
    {
        if (row is null) return;
        await Shell.Current.GoToAsync($"pluginPerms?id={Uri.EscapeDataString(row.Id)}");
    }

    [RelayCommand]
    private async Task ToggleLog(PluginRow? row)
    {
        if (row is null) return;
        if (row.LogVisible) { row.LogVisible = false; return; }
        row.LogText = await _plugins.CrashLogAsync(row.Id);
        row.LogVisible = true;
    }

    [RelayCommand]
    private async Task ClearLog(PluginRow? row)
    {
        if (row is null) return;
        _plugins.ClearCrashLog(row.Id);
        row.LogText = "（已清空）";
        row.LogVisible = true;
    }

    [RelayCommand]
    private async Task RunTest(PluginRow? row)
    {
        if (row is null) return;
        var hook = row.FirstHook;
        if (hook.Length == 0)
        {
            Status = "这个插件没有声明钩子，装上也不会有行为。";
            return;
        }
        var result = await _plugins.RunHookAsync(row.Id, hook, new
        {
            charId = "test",
            character = "test",
            text = "这是一次测试。",
            reply = "这是一次测试。"
        });
        Status = string.IsNullOrWhiteSpace(result)
            ? $"已执行 {hook}，没有返回文本（可能只做了副作用）。"
            : $"{hook} 返回：{result}";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task OpenMissingUrl(PluginRow? row)
    {
        var url = row?.MissingUrls.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(url)) return;
        try { await Launcher.Default.OpenAsync(url); }
        catch (Exception ex) { Status = "打不开安装地址：" + ex.Message; }
    }

    /// <summary>打开插件自带的页面（manifest.pages）。多个页面时先让用户挑。</summary>
    [RelayCommand]
    private async Task OpenPage(PluginRow? row)
    {
        if (row is null) return;
        var entry = await _plugins.FindAsync(row.Id);
        if (entry is null || entry.Manifest.Pages.Count == 0) return;
        var index = 0;
        if (entry.Manifest.Pages.Count > 1)
        {
            var titles = entry.Manifest.Pages
                .Select(p => string.IsNullOrWhiteSpace(p.Title) ? p.Route : p.Title).ToArray();
            var pick = await Shell.Current.DisplayActionSheet("打开插件的页面", "取消", null, titles);
            index = Array.IndexOf(titles, pick);
            if (index < 0) return;
        }
        var (ok, message) = await _plugins.OpenPageAsync(row.Id, index);
        Status = message;
        if (!ok) await Shell.Current.DisplayAlert("插件页面", message, "好");
    }

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");

    /// <summary>刚装好的插件缺依赖时，直接把安装地址摆出来问一句（不是装完了才发现它不能跑）。</summary>
    private readonly HashSet<string> _offeredMissing = new(StringComparer.OrdinalIgnoreCase);

    private async Task OfferMissingDependenciesAsync()
    {
        try
        {
            var list = await _plugins.AllAsync();
            var entry = list.FirstOrDefault(p =>
                !_offeredMissing.Contains(p.Manifest.Id)
                && p.MissingDependencies.Any(d => !string.IsNullOrWhiteSpace(d.Url)));
            if (entry is null) return;
            _offeredMissing.Add(entry.Manifest.Id);
            var missing = entry.MissingDependencies
                .Where(d => !string.IsNullOrWhiteSpace(d.Url)).ToList();
            var detail = string.Join("\n", missing.Select(d => $"{d.Id}：{d.Url}"));
            var go = await Shell.Current.DisplayAlert("缺少依赖",
                $"「{entry.Manifest.Name}」需要先装好以下依赖：\n\n{detail}\n\n现在打开安装地址吗？",
                "打开地址", "稍后再说");
            if (!go) return;
            foreach (var d in missing)
            {
                try { await Launcher.Default.OpenAsync(d.Url); }
                catch (Exception ex) { Status = $"打不开 {d.Id} 的安装地址：{ex.Message}"; }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("PluginCenter.OfferMissing -> " + ex);
        }
    }

    // ==================== MCP 工坊 ====================

    private void RefreshMcp()
    {
        try
        {
            McpServers.Clear();
            foreach (var item in _mcp.ListServers())
            {
                var row = new McpServerRow
                {
                    Key = item.Key,
                    Name = item.Name,
                    Description = item.Description,
                    Url = item.Url
                };
                foreach (var t in item.Tools)
                {
                    row.Tools.Add(new McpToolRow
                    {
                        Name = t.Name,
                        Description = t.Description,
                        Category = t.Category,
                        Server = item.Key,
                        Active = t.Active,
                        KindLabel = t.Kind switch
                        {
                            "js" => "本地 JS",
                            "http" => "远程 HTTP",
                            "builtin" => "内置",
                            _ => t.Kind
                        }
                    });
                }
                McpServers.Add(row);
            }
            OnPropertyChanged(nameof(HasMcpServers));
        }
        catch (Exception ex)
        {
            McpResult = "读取 MCP 列表失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task TestMcpTool(McpToolRow? row)
    {
        if (row is null) return;
        var args = await Shell.Current.DisplayPromptAsync(
            $"试跑 {row.Name}", "传给工具的 JSON 参数（默认 {}）：", "运行", "取消", "{}");
        if (args is null) return;
        row.LastResult = "运行中…";
        row.ResultVisible = true;
        row.LastResult = await _mcp.RunManualAsync(row.Name, string.IsNullOrWhiteSpace(args) ? "{}" : args);
    }

    [RelayCommand]
    private async Task ToggleMcpTool(McpToolRow? row)
    {
        if (row is null) return;
        await _mcp.SetToolActiveAsync(row.Server, row.Name, !row.Active);
        Status = $"MCP 工具「{row.Name}」已{(row.Active ? "停用" : "启用")}。";
        RefreshMcp();
    }

    [RelayCommand]
    private async Task DeleteMcpServer(McpServerRow? row)
    {
        if (row is null) return;
        if (!await Shell.Current.DisplayAlert("删除 MCP 数据包",
                $"删除「{row.Name}」的全部文件与工具注册？这个操作不可恢复。", "删除", "取消")) return;
        _mcp.RemoveServer(row.Key);
        Status = $"已删除 MCP「{row.Name}」。";
        RefreshMcp();
    }

    /// <summary>
    /// MCP 工坊：一句话 → AI 给出一个数据包（服务器 + 若干工具），直接建档、注册，
    /// 本地 JS 工具立刻用空参数试跑一次，把真实结果摆在用户面前。
    /// </summary>
    [RelayCommand]
    private async Task GenerateMcp()
    {
        var prompt = (McpPrompt ?? "").Trim();
        if (prompt.Length < 4)
        {
            McpResult = "先用一句话说清这个 MCP 要提供什么能力。";
            return;
        }
        McpBusy = true;
        McpResult = "正在让 AI 写 MCP 工具…";
        try
        {
            var reply = await _api.Chat(new List<ChatMessage>
            {
                new() { Role = "system", Content = McpWorkshopSystemPrompt },
                new() { Role = "user", Content = prompt }
            });
            if (string.IsNullOrWhiteSpace(reply) || reply.StartsWith("[", StringComparison.Ordinal))
            {
                McpResult = string.IsNullOrWhiteSpace(reply) ? "AI 没有回应。检查 AI 配置后再试。" : reply;
                return;
            }
            GeneratedMcpPack? draft;
            try
            {
                draft = JsonSerializer.Deserialize<GeneratedMcpPack>(ExtractJson(reply), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
            }
            catch (Exception ex)
            {
                McpResult = "AI 返回的不是可解析的 JSON：" + ex.Message + "\n\n原文：\n" + Truncate(reply, 1200);
                return;
            }
            var drafts = (draft?.Tools ?? new List<GeneratedMcpTool>())
                .Where(t => !string.IsNullOrWhiteSpace(t.Name) && !string.IsNullOrWhiteSpace(t.Description))
                .ToList();
            if (draft?.Server is null || drafts.Count == 0)
            {
                McpResult = "生成结果缺少 server 或 tools，没有创建。\n\n" + Truncate(reply, 1200);
                return;
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tools = new List<McpToolDef>();
            foreach (var t in drafts)
            {
                var name = SafeToolName(t.Name);
                if (!used.Add(name)) name += "_" + used.Count;
                var kind = (t.Kind ?? "js").Trim().ToLowerInvariant();
                var code = t.Code ?? "";
                var url = (t.Url ?? "").Trim();
                if (kind == "http" && url.Length == 0) kind = code.Length > 0 ? "js" : "skip";
                if (kind is not ("js" or "http")) kind = code.Length > 0 ? "js" : "skip";
                if (kind == "skip") continue;
                tools.Add(new McpToolDef
                {
                    Name = name,
                    Description = t.Description!.Trim(),
                    Category = string.IsNullOrWhiteSpace(t.Category) ? "自定义" : t.Category.Trim(),
                    Kind = kind,
                    Code = code,
                    Url = url,
                    Method = string.IsNullOrWhiteSpace(t.Method) ? "POST" : t.Method.Trim().ToUpperInvariant(),
                    ParametersSchema = t.Parameters?.GetRawText() ?? "",
                    Active = true
                });
            }
            if (tools.Count == 0)
            {
                McpResult = "生成的工具要么缺 code 要么缺 url，没有可执行的条目。";
                return;
            }

            var (ok, message, server) = await _mcp.CreateServerWithToolsAsync(
                draft.Server.Name ?? "", draft.Server.Description ?? "", draft.Server.Url ?? "", tools);
            if (!ok)
            {
                McpResult = message;
                return;
            }

            // 直接试跑：本地 JS 是沙箱，空参数跑一次就知道能不能用。
            var tests = new List<string>();
            foreach (var t in tools.Where(t => t.Kind == "js").Take(3))
            {
                var output = await _mcp.RunManualAsync(t.Name, "{}");
                tests.Add($"· {t.Name} → {Truncate(string.IsNullOrWhiteSpace(output) ? "（无输出）" : output, 220)}");
            }
            McpResult = message + (tests.Count == 0
                ? "\n远程工具没有自动试跑，可点每条工具的「试跑」手动验证。"
                : "\n\n自动试跑（空参数）：\n" + string.Join("\n", tests));
            Status = $"MCP 数据包「{server}」已就绪，AI 可通过 mcp_list / mcp_call 使用。";
            RefreshMcp();
        }
        catch (Exception ex)
        {
            McpResult = "MCP 工坊出错：" + ex.Message;
            App.WriteLog("PluginCenter.McpWorkshop -> " + ex);
        }
        finally { McpBusy = false; }
    }

    private static string SafeToolName(string raw)
    {
        var s = new string((raw ?? "").Trim().ToLowerInvariant()
            .Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-').ToArray());
        if (s.Length == 0) s = "mcp_tool";
        if (!char.IsAsciiLetter(s[0])) s = "mcp_" + s;
        return s.Length > 48 ? s[..48] : s;
    }

    private const string McpWorkshopSystemPrompt = """
        你在为「温暖如初」写一个 MCP 数据包。只输出一个 JSON 对象，不要 Markdown、不要解释。

        格式：
        {
          "server": { "name": "数据包名（可中文）", "description": "一句话说明", "url": "" },
          "tools": [
            {
              "name": "mcp_example",
              "description": "这个工具做什么、什么时候该调用",
              "category": "分类",
              "kind": "js",
              "code": "一整段 JS 源码",
              "parameters": { "type": "object", "properties": { "text": { "type": "string", "description": "..." } }, "required": ["text"] }
            }
          ]
        }

        硬性规则：
        - 生成 1~4 个工具，每个都必须真的能执行。
        - kind="js"：code 是完整 JS。输入放在 input 与 args（都是 JSON 字符串），用 JSON.parse(args) 取参数；最终把要返回的文本赋给 result。可用 log(x)、now()、Math、JSON；没有文件系统、没有网络、不能读环境变量。
        - kind="http"：只有当用户明确给了远程地址时才用；此时必须填 url，method 默认 POST，请求体会是 {"name":"工具名","arguments":{...}}。
        - name 用小写英文/数字/下划线，建议 mcp_ 前缀，不要和 powershell / ps / screencap 重名。
        - 不要写删除文件、扫描网络、发送消息、绕过权限之类危险行为；只做用户要求的无害能力。
        - description 要写清「什么时候调用」，这是模型选工具的唯一依据。
        """;

    // ==================== 插件工坊 ====================

    /// <summary>
    /// 让 AI 直接产出一个可安装的插件。模型只回一个 JSON：
    /// 清单字段 + settings（三选一，工坊只生成 native/none）+ hooks 列表 + code（一段 JS）。
    /// 目录、转义、id 清洗都由这里做，模型不用拼 plugin.json。
    /// </summary>
    [RelayCommand]
    private async Task GeneratePlugin()
    {
        var prompt = (WorkshopPrompt ?? "").Trim();
        if (prompt.Length < 4)
        {
            WorkshopResult = "先用一句话说清你要的插件做什么。";
            return;
        }
        Busy = true;
        WorkshopResult = "正在让 AI 写插件…";
        try
        {
            var reply = await _api.Chat(new List<ChatMessage>
            {
                new() { Role = "system", Content = WorkshopSystemPrompt },
                new() { Role = "user", Content = prompt }
            });
            if (string.IsNullOrWhiteSpace(reply))
            {
                WorkshopResult = "AI 没有回应。检查 AI 配置后再试。";
                return;
            }
            var json = ExtractJson(reply);
            GeneratedPlugin? draft;
            try
            {
                draft = JsonSerializer.Deserialize<GeneratedPlugin>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
            }
            catch (Exception ex)
            {
                WorkshopResult = "AI 返回的不是可解析的 JSON：" + ex.Message + "\n\n原文：\n" + Truncate(reply, 1200);
                return;
            }
            if (draft is null || string.IsNullOrWhiteSpace(draft.Name) || string.IsNullOrWhiteSpace(draft.Code))
            {
                WorkshopResult = "生成的插件缺 name 或 code，没有安装。\n\n" + Truncate(reply, 1200);
                return;
            }
            var kind = (draft.Settings?.Kind ?? "none").Trim().ToLowerInvariant();
            if (kind is not ("native" or "none"))
            {
                // html 需要一整个页面文件，工坊第一版不代写页面：退回 none，并说明
                kind = "none";
                draft.Settings = new PluginSettingsSpec { Kind = "none" };
            }

            var temp = Path.Combine(Path.GetTempPath(), "wab_workshop_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(temp);
            try
            {
                var hooks = (draft.Hooks ?? new List<string>())
                    .Where(h => !string.IsNullOrWhiteSpace(h))
                    .Select(h => h.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (hooks.Count == 0) hooks.Add("persona");
                // 权限名只认能力目录里有的：拼错的那几条丢在这里说清楚，
                // 不带进 plugin.json——一条谁也不认识的声明，在权限台上永远不会亮。
                var declared = new List<string>();
                var unknownPerm = new List<string>();
                foreach (var raw in draft.Permissions ?? new List<string>())
                {
                    var perm = (raw ?? "").Trim();
                    if (perm.Length == 0) continue;
                    PluginCapabilities.SplitDeclaration(perm, out var cap, out var arg);
                    var found = PluginCapabilities.Find(cap);
                    if (found is null) { unknownPerm.Add(perm); continue; }
                    declared.Add(found.ArgScoped && arg.Length > 0 ? found.Name + ":" + arg : found.Name);
                }
                var manifest = new PluginManifest
                {
                    Id = draft.Id ?? "",
                    Name = draft.Name.Trim(),
                    Version = string.IsNullOrWhiteSpace(draft.Version) ? "1.0.0" : draft.Version.Trim(),
                    Description = (draft.Description ?? "").Trim(),
                    Author = string.IsNullOrWhiteSpace(draft.Author) ? "AI 工坊" : draft.Author.Trim(),
                    Permissions = declared.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    Settings = new PluginSettingsSpec
                    {
                        Kind = kind,
                        Fields = kind == "native" ? (draft.Settings?.Fields ?? new List<PluginSettingField>()) : new(),
                        Callback = true
                    },
                    Hooks = hooks.ToDictionary(h => h, _ => "hooks.js", StringComparer.OrdinalIgnoreCase)
                };
                await File.WriteAllTextAsync(Path.Combine(temp, "plugin.json"),
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
                await File.WriteAllTextAsync(Path.Combine(temp, "hooks.js"), draft.Code);
                var (ok, message) = await _plugins.InstallFromFolderAsync(temp, overwrite: true);
                WorkshopResult = ok
                    ? message + $"\n钩子：{string.Join("、", hooks)}；设置：{manifest.Settings.Kind}；"
                      + $"声明的能力：{(manifest.Permissions.Count == 0 ? "无（只用钩子本身隐含的那几项）" : string.Join("、", manifest.Permissions))}"
                      + (unknownPerm.Count == 0 ? "" : $"。这几条权限名程序不认识，已丢弃：{string.Join("、", unknownPerm)}")
                      + "。装好后到「插件中心 → 权限」逐条看，网络与工具那类默认还要问你一次。已经可以直接试跑。"
                    : message;
                if (ok) await OfferMissingDependenciesAsync();
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            WorkshopResult = "工坊出错：" + ex.Message;
            App.WriteLog("PluginCenter.Workshop -> " + ex);
        }
        finally { Busy = false; }
    }

    private static string ExtractJson(string text)
    {
        var s = text.Trim();
        if (s.StartsWith("```"))
        {
            var first = s.IndexOf('\n');
            if (first >= 0) s = s[(first + 1)..];
            var end = s.LastIndexOf("```", StringComparison.Ordinal);
            if (end >= 0) s = s[..end];
        }
        var a = s.IndexOf('{');
        var b = s.LastIndexOf('}');
        return a >= 0 && b > a ? s[a..(b + 1)] : s;
    }

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";

    private const string WorkshopSystemPrompt = """
        你在为「温暖如初」写一个插件。只输出一个 JSON 对象，不要 Markdown、不要解释。

        格式：
        {
          "id": "小写字母/数字/下划线，如 mood_echo",
          "name": "显示名",
          "version": "1.0.0",
          "description": "一句话说明",
          "author": "AI 工坊",
          "settings": { "kind": "native", "fields": [
            { "key": "tone", "label": "语气", "type": "text", "default": "温柔", "help": "..." }
          ]},
          "hooks": ["persona"],
          "permissions": ["tools:send_to_user"],
          "code": "JS 源码"
        }

        硬性规则：
        - settings.kind 只能是 "native" 或 "none"。需要用户调节就 native 并给出 fields；否则 none。field.type 用 text / number / switch / select / secret；select 要带 options。
        - hooks 只能从 persona、context、beforeSend、afterReply、afterTurn、onSceneChange、onToolResult 中选。
        - code 是一整段 JS：运行时可用 input（JSON 字符串）、result（最终文本）、getSetting(key)、setSetting(key,value)、log(x)、now()。
        - persona/context：input 形如 {"charId":"..."}，把要加进人设/上下文的文字赋给 result。
        - beforeSend/afterReply：input 形如 {"charId":"...","text":"...","reply":"..."}，把改写后的文本赋给 result。
        - 越出自己家门的四个入口，每一个都要在 permissions 里先声明，否则宿主直接把它拦死（函数返回错误 JSON，不会报错，但什么也不会发生）：
          · callTool(name,argsJson) → "tools:工具名"；
          · callPlugin(id,hook,argsJson) → "plugins:插件id"；
          · emit(topic,payloadJson) → "broadcast:话题名"；
          · fetch(url,optionsJson) → "network:主机名"（只写主机，不要写 http:// 和路径）。
          还可以用 notify(title,text)（声明 "notify"）发一条用户可见通知。writeFile/readFile/listFiles/deleteFile 只在插件自己的文件区里工作，不用声明。
        - permissions 只认这些名字：settings、storage、log、prompt.write、pipeline.transform、pipeline.override、tools、plugins、broadcast、notify、page、network、localnet。不认识的名字会被丢掉。
        - 默认策略按风险走：自己家里的直接放行；动提示词与文本的放行；工具、别的插件、广播、通知、联网要问用户；localnet 与不在 permissions 里的联网主机直接禁止。所以别默认用户会同意联网——能不联网就别联网。
        - 不写自伤/伤人的具体做法；不要为了绕过限制去猜路径、拼接主机名或伪装成本地地址。
        - 不要输出插件设置页 HTML（工坊不代写网页），需要界面就用 native。
        """;

    private sealed class GeneratedPlugin
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Description { get; set; }
        public string? Author { get; set; }
        public PluginSettingsSpec? Settings { get; set; }
        public List<string>? Hooks { get; set; }
        public List<string>? Permissions { get; set; }
        public string? Code { get; set; }
    }
}

public sealed partial class PluginRow : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Enabled { get; init; }
    public bool CanRun { get; init; }
    public string SettingsKind { get; init; } = "";
    public string SettingsKindLabel { get; init; } = "";
    public string HookLabel { get; init; } = "";
    public string FirstHook { get; init; } = "";
    public string DependenciesLabel { get; init; } = "";
    public List<string> MissingUrls { get; init; } = new();
    public string Issue { get; init; } = "";

    public string ToggleLabel => Enabled ? "停用" : "启用";
    public string StateLabel => Issue.Length > 0 ? "不能运行" : Enabled ? "生效中" : "已停用";
    public string PermissionLabel { get; init; } = "";
    public bool HasIssue => Issue.Length > 0;
    public bool HasMissing => DependenciesLabel.StartsWith("缺少", StringComparison.Ordinal);
    public bool CanOpenMissing => MissingUrls.Count > 0;

    public bool HasPages { get; init; }
    public string PagesLabel { get; init; } = "";
    public string OverridesLabel { get; init; } = "";
    public bool HasOverrides => OverridesLabel.Length > 0;

    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private bool _logVisible;
}

/// <summary>插件中心里的一张 MCP 数据包卡（含它登记的工具）。</summary>
public sealed class McpServerRow
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Url { get; init; } = "";
    public ObservableCollection<McpToolRow> Tools { get; } = new();
    public string ToolsLabel => Tools.Count == 0 ? "没有登记工具" : $"{Tools.Count} 个工具";
    public bool HasTools => Tools.Count > 0;
    public bool HasDescription => Description.Length > 0;
}

/// <summary>MCP 工具的一行：可启停、可试跑并就地看结果。</summary>
public sealed partial class McpToolRow : ObservableObject
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    public string Server { get; init; } = "";
    public bool Active { get; init; }
    public string KindLabel { get; init; } = "";

    public string StateLabel => Active ? "启用" : "停用";
    public string ToggleLabel => Active ? "停用" : "启用";

    [ObservableProperty] private string _lastResult = "";
    [ObservableProperty] private bool _resultVisible;
}

/// <summary>MCP 工坊的模型返回结构。</summary>
public sealed class GeneratedMcpPack
{
    public GeneratedMcpServer? Server { get; set; }
    public List<GeneratedMcpTool>? Tools { get; set; }
}

public sealed class GeneratedMcpServer
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
}

public sealed class GeneratedMcpTool
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? Kind { get; set; }
    public string? Code { get; set; }
    public string? Url { get; set; }
    public string? Method { get; set; }
    public JsonElement? Parameters { get; set; }
}
