using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Modules.Tools;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 存档高级设置：哪些插件、工具、角色可以碰这份存档。
/// 规则写进存档自己的 state.permissions，换档就换一套；不进入游戏也能改。
/// </summary>
public sealed partial class SaveRulesViewModel : ObservableObject
{
    private readonly SaveManager _save;
    private readonly PluginHost _plugins;
    private readonly ToolManager _tools;
    private readonly CharacterLibrary _chars;
    private readonly GameEngine _engine;
    private readonly SettingsManager _settings;
    private string _saveId = "";
    private bool _loading;

    public ObservableCollection<PermissionRuleItem> Plugins { get; } = new();
    public ObservableCollection<PermissionRuleItem> Tools { get; } = new();
    public ObservableCollection<PermissionRuleItem> Characters { get; } = new();
    public ObservableCollection<LibidoRuleItem> Libido { get; } = new();
    public ObservableCollection<WorldviewRuleItem> Worldviews { get; } = new();

    [ObservableProperty] private string _saveLabel = "";
    [ObservableProperty] private bool _allowAllPlugins = true;
    [ObservableProperty] private bool _allowAllTools = true;
    [ObservableProperty] private bool _allowAiInstallPlugins;
    [ObservableProperty] private bool _allowAllCharacters = true;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _noPlugins;
    [ObservableProperty] private bool _noCharacters;
    /// <summary>全局总开关关着时，这一节的勾选不生效（在这里也能看出来）。</summary>
    [ObservableProperty] private bool _libidoSystemOff;

    // ── 角色世界观（需求 16）────────────────────────────────────────────
    [ObservableProperty] private bool _hasWorldviews;
    [ObservableProperty] private string _worldviewHint = "";
    [ObservableProperty] private bool _worldviewCrossSave = true;
    [ObservableProperty] private string _worldviewCrossSaveHint = "";
    private string _worldviewCharacterId = "";

    public SaveRulesViewModel(SaveManager save, PluginHost plugins, ToolManager tools,
        CharacterLibrary chars, GameEngine engine, SettingsManager settings)
    {
        _save = save;
        _plugins = plugins;
        _tools = tools;
        _chars = chars;
        _engine = engine;
        _settings = settings;
    }

    public async Task LoadAsync(string id)
    {
        _saveId = id;
        Busy = true;
        _loading = true;
        try
        {
            var slot = (await _save.List()).FirstOrDefault(s => s.Id == id);
            SaveLabel = slot is null ? id : $"{slot.Label} · {slot.Scene}";
            var rules = await _save.LoadRulesAsync(id);
            var perm = rules?.Permissions ?? new SavePermissions();
            var allowedChars = rules?.AllowedCharacters ?? new List<string>();

            AllowAllPlugins = perm.AllowAllPlugins;
            AllowAllTools = perm.AllowAllTools;
            AllowAiInstallPlugins = perm.AllowAiInstallPlugins;
            AllowAllCharacters = allowedChars.Count == 0;

            Plugins.Clear();
            foreach (var p in await _plugins.AllAsync())
            {
                Plugins.Add(new PermissionRuleItem
                {
                    Id = p.Manifest.Id,
                    Name = p.Manifest.Name,
                    Hint = $"v{p.Manifest.Version} · {(p.Enabled ? "已启用" : "已停用")}",
                    Allowed = perm.AllowAllPlugins || perm.AllowedPlugins.Contains(p.Manifest.Id, StringComparer.OrdinalIgnoreCase)
                });
            }
            Tools.Clear();
            foreach (var t in _tools.AllTools())
            {
                Tools.Add(new PermissionRuleItem
                {
                    Id = t.Name,
                    Name = t.Name,
                    Hint = t.Description,
                    Allowed = perm.AllowAllTools || perm.AllowedTools.Contains(t.Name, StringComparer.OrdinalIgnoreCase)
                });
            }
            Characters.Clear();
            foreach (var c in await _chars.ListAsync())
            {
                Characters.Add(new PermissionRuleItem
                {
                    Id = c.Profile.Id,
                    Name = c.Profile.Name,
                    Hint = c.Profile.Description,
                    Allowed = allowedChars.Count == 0 || allowedChars.Contains(c.Profile.Id, StringComparer.OrdinalIgnoreCase)
                });
            }
            ApplyEnabled(Plugins, AllowAllPlugins);
            ApplyEnabled(Tools, AllowAllTools);
            ApplyEnabled(Characters, AllowAllCharacters);

            // 性欲系统：只在这份存档里点名的人才参与；没点名就是没人参与，不是默认全员参与
            var libidoIds = await _save.LoadLibidoCharactersAsync(id) ?? new List<string>();
            var libidoOn = _settings.Current.LibidoSystemEnabled;
            LibidoSystemOff = !libidoOn;
            Libido.Clear();
            foreach (var c in await _chars.ListAsync())
            {
                Libido.Add(new LibidoRuleItem
                {
                    Id = c.Profile.Id,
                    Name = c.Profile.Name,
                    Hint = c.StateShared ? "共享角色 · 数值跨存档" : "非共享角色 · 数值按存档各存一份",
                    Participates = libidoIds.Contains(c.Profile.Id, StringComparer.Ordinal),
                    Enabled = libidoOn
                });
            }

            NoPlugins = Plugins.Count == 0;
            NoCharacters = Characters.Count == 0;

            // 角色世界观：这套设定属于角色，但「这份存档用哪一套」可以按档覆盖。
            var allChars = await _chars.ListAsync();
            var wv = await _save.LoadWorldviewAsync(id);
            _worldviewCharacterId = wv?.CharacterId
                ?? allChars.FirstOrDefault(c => c.Profile.Id == _engine.State.CharacterId)?.Profile.Id ?? "";
            var main = allChars.FirstOrDefault(c => c.Profile.Id == _worldviewCharacterId);
            var active = (wv?.WorldviewId ?? "").Trim();
            if (active.Length == 0) active = (main?.ActiveWorldviewId ?? "").Trim();

            Worldviews.Clear();
            foreach (var w in main?.Worldviews ?? new List<CharacterWorldview>())
            {
                Worldviews.Add(new WorldviewRuleItem
                {
                    Id = w.Id,
                    Name = string.IsNullOrWhiteSpace(w.Name) ? "未命名世界观" : w.Name.Trim(),
                    Hint = BuildWorldviewHint(w),
                    IsOn = string.Equals(w.Id, active, StringComparison.Ordinal)
                });
            }
            HasWorldviews = Worldviews.Count > 0;
            WorldviewCrossSave = main?.WorldviewCrossSave ?? true;
            RefreshWorldviewCrossSaveHint();
            WorldviewHint = main is null
                ? "这份存档的主角还没进角色库。"
                : HasWorldviews
                    ? $"「{main.Profile.Name}」有 {Worldviews.Count} 套世界观。这里换的是这一档用哪一套。"
                    : $"「{main.Profile.Name}」还没有角色世界观：先去工作室给它建一套。";

            Status = $"插件 {Plugins.Count} 个 · 工具 {Tools.Count} 个 · 角色 {Characters.Count} 个。";
        }
        catch (Exception ex)
        {
            Status = "读取失败：" + ex.Message;
            App.WriteLog("SaveRules.Load -> " + ex);
        }
        finally { Busy = false; _loading = false; }
    }

    partial void OnAllowAllPluginsChanged(bool value) => ApplyEnabled(Plugins, value);
    partial void OnAllowAllToolsChanged(bool value) => ApplyEnabled(Tools, value);
    partial void OnAllowAllCharactersChanged(bool value) => ApplyEnabled(Characters, value);

    partial void OnWorldviewCrossSaveChanged(bool value)
    {
        if (_loading) return;
        RefreshWorldviewCrossSaveHint();
        _ = PersistWorldviewCrossSaveAsync(value);
    }

    private void RefreshWorldviewCrossSaveHint()
    {
        WorldviewCrossSaveHint = WorldviewCrossSave
            ? "开：在这里换世界观会写回角色，其他存档一起换。"
            : "关：只在当前这一档换，角色与其他存档不受影响。";
    }

    private async Task PersistWorldviewCrossSaveAsync(bool value)
    {
        if (_worldviewCharacterId.Length == 0) return;
        try
        {
            var ch = (await _chars.ListAsync()).FirstOrDefault(c => c.Profile.Id == _worldviewCharacterId);
            if (ch is null) return;
            var picked = Worldviews.FirstOrDefault(w => w.IsOn)?.Id ?? ch.ActiveWorldviewId;
            var updated = ch with
            {
                WorldviewCrossSave = value,
                // 打开跨存档的那一刻，把「这一档正在用的那套」定成角色的默认：
                // 否则开关是开了，别的存档仍指着旧的那套，读起来像没生效。
                ActiveWorldviewId = value ? picked : ch.ActiveWorldviewId
            };
            if (await _chars.UpdateAsync(updated))
                Status = value ? "世界观改动改为跨存档生效。" : "世界观改动只在这一档生效。";
            else
                Status = "切换跨存档失败：角色没写进盘。";
        }
        catch (Exception ex)
        {
            Status = "切换跨存档失败：" + ex.Message;
            App.WriteLog("SaveRules.WorldviewCrossSave -> " + ex);
        }
    }

    [RelayCommand]
    private async Task PickWorldview(WorldviewRuleItem? item)
    {
        if (item is null || _saveId.Length == 0) return;
        foreach (var w in Worldviews) w.IsOn = ReferenceEquals(w, item);

        var scoped = await _save.LoadWorldviewAsync(_saveId);
        var books = scoped?.ScopedBooks ?? new List<string>();
        var ok = await _save.SaveWorldviewAsync(_saveId, item.Id, WorldviewCrossSave, books);
        if (!ok)
        {
            Status = "没能写进存档：" + item.Name;
            return;
        }

        // 跨存档：写回角色的默认世界观；不跨存档：只认存档里的 CharacterWorldviewId。
        if (WorldviewCrossSave && _worldviewCharacterId.Length > 0)
        {
            var ch = (await _chars.ListAsync()).FirstOrDefault(c => c.Profile.Id == _worldviewCharacterId);
            if (ch is not null && !await _chars.UpdateAsync(ch with { ActiveWorldviewId = item.Id }))
            {
                Status = "世界观已记进这一档，但没能写回角色。";
                return;
            }
        }

        if (string.Equals(_engine.CurrentSaveId, _saveId, StringComparison.Ordinal))
        {
            _engine.State.CharacterWorldviewId = item.Id;
            _engine.State.WorldviewScopedToSave = !WorldviewCrossSave;
        }
        Status = WorldviewCrossSave
            ? $"已换成「{item.Name}」，并写回角色：其他存档也跟着换。"
            : $"已换成「{item.Name}」，只在当前这一档生效。";
    }

    private static string BuildWorldviewHint(CharacterWorldview w)
    {
        var books = (w.WorldbookIds ?? new List<string>()).Count(id => !string.IsNullOrWhiteSpace(id));
        var desc = (w.Description ?? "").Trim();
        var summary = desc.Length == 0
            ? "没有描述"
            : desc.Length > 42 ? desc[..42] + "…" : desc;
        return $"{books} 本世界书 · {summary}";
    }


    private static void ApplyEnabled(IEnumerable<PermissionRuleItem> items, bool allowAll)
    {
        foreach (var item in items) item.Enabled = !allowAll;
    }

    [RelayCommand]
    private async Task Save()
    {
        if (string.IsNullOrEmpty(_saveId)) return;
        Busy = true;
        try
        {
            var allowedChars = AllowAllCharacters
                ? new List<string>()
                : Characters.Where(c => c.Allowed).Select(c => c.Id).ToList();
            var permissions = new SavePermissions
            {
                AllowAllPlugins = AllowAllPlugins,
                AllowAllTools = AllowAllTools,
                AllowAiInstallPlugins = AllowAiInstallPlugins,
                AllowedPlugins = Plugins.Where(p => p.Allowed).Select(p => p.Id).ToList(),
                AllowedTools = Tools.Where(t => t.Allowed).Select(t => t.Id).ToList(),
                AllowedCharacters = allowedChars
            };
            var ok = await _save.SaveRulesAsync(_saveId, permissions, allowedChars);
            var libidoIds = Libido.Where(l => l.Participates).Select(l => l.Id).ToList();
            var libidoOk = await _save.SaveLibidoCharactersAsync(_saveId, libidoIds);
            if (ok && string.Equals(_engine.CurrentSaveId, _saveId, StringComparison.Ordinal))
            {
                // 这份存档正好开着：内存里的状态也一起换，否则要重进存档才生效
                _engine.State.Permissions = permissions;
                _engine.State.AllowedCharacters.Clear();
                foreach (var c in allowedChars) _engine.State.AllowedCharacters.Add(c);
                _engine.State.LibidoCharacters.Clear();
                foreach (var c in libidoIds) _engine.State.LibidoCharacters.Add(c);
            }
            if (ok && !libidoOk) App.WriteLog("SaveRules.Save -> 性欲名单没能写进存档");
            Status = ok ? "已保存。下次进入这个存档时生效；AI 安装插件的开关同样跟着存档走。" : "保存失败：存档文件读不出来。";
        }
        catch (Exception ex)
        {
            Status = "保存失败：" + ex.Message;
            App.WriteLog("SaveRules.Save -> " + ex);
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");
}

/// <summary>白名单里的一行；允许全部时整列变灰，单项选择保留。</summary>
public sealed partial class PermissionRuleItem : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Hint { get; init; } = "";
    public bool HasHint => !string.IsNullOrWhiteSpace(Hint);
    [ObservableProperty] private bool _allowed = true;
    [ObservableProperty] private bool _enabled = true;
}

/// <summary>性欲系统在这份存档里的一位参与者。总开关关着时整列变灰，但勾选保留。</summary>
public sealed partial class LibidoRuleItem : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Hint { get; init; } = "";
    public bool HasHint => !string.IsNullOrWhiteSpace(Hint);
    [ObservableProperty] private bool _participates;
    [ObservableProperty] private bool _enabled = true;
}

/// <summary>这份存档可选的某一套角色世界观；单选。</summary>
public sealed partial class WorldviewRuleItem : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Hint { get; init; } = "";
    public bool HasHint => !string.IsNullOrWhiteSpace(Hint);
    [ObservableProperty] private bool _isOn;
}
