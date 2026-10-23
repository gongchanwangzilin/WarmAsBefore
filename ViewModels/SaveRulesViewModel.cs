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
    private string _saveId = "";

    public ObservableCollection<PermissionRuleItem> Plugins { get; } = new();
    public ObservableCollection<PermissionRuleItem> Tools { get; } = new();
    public ObservableCollection<PermissionRuleItem> Characters { get; } = new();

    [ObservableProperty] private string _saveLabel = "";
    [ObservableProperty] private bool _allowAllPlugins = true;
    [ObservableProperty] private bool _allowAllTools = true;
    [ObservableProperty] private bool _allowAiInstallPlugins;
    [ObservableProperty] private bool _allowAllCharacters = true;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _noPlugins;
    [ObservableProperty] private bool _noCharacters;

    public SaveRulesViewModel(SaveManager save, PluginHost plugins, ToolManager tools,
        CharacterLibrary chars, GameEngine engine)
    {
        _save = save;
        _plugins = plugins;
        _tools = tools;
        _chars = chars;
        _engine = engine;
    }

    public async Task LoadAsync(string id)
    {
        _saveId = id;
        Busy = true;
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
            NoPlugins = Plugins.Count == 0;
            NoCharacters = Characters.Count == 0;
            Status = $"插件 {Plugins.Count} 个 · 工具 {Tools.Count} 个 · 角色 {Characters.Count} 个。";
        }
        catch (Exception ex)
        {
            Status = "读取失败：" + ex.Message;
            App.WriteLog("SaveRules.Load -> " + ex);
        }
        finally { Busy = false; }
    }

    partial void OnAllowAllPluginsChanged(bool value) => ApplyEnabled(Plugins, value);
    partial void OnAllowAllToolsChanged(bool value) => ApplyEnabled(Tools, value);
    partial void OnAllowAllCharactersChanged(bool value) => ApplyEnabled(Characters, value);

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
            if (ok && string.Equals(_engine.CurrentSaveId, _saveId, StringComparison.Ordinal))
            {
                // 这份存档正好开着：内存里的状态也一起换，否则要重进存档才生效
                _engine.State.Permissions = permissions;
                _engine.State.AllowedCharacters.Clear();
                foreach (var c in allowedChars) _engine.State.AllowedCharacters.Add(c);
            }
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
