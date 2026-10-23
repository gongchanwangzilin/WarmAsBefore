using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 一个插件的权限台：能力目录 × 这个插件的实际声明 × 用户给过的策略，再加它撞过哪些闸。
///
/// 这里刻意没有「一键全部允许」：那等于把整套机制退回一刀切，用户按下去的那一秒
/// 并不知道自己放行了什么。也没有「一键全部禁止」——真想关掉一切，停用插件就是那个开关。
/// 每一行都要人自己点一下，点的那一下才是授权。
/// </summary>
public sealed partial class PluginPermissionsViewModel : ObservableObject
{
    private readonly PluginHost _plugins;
    private readonly PluginPermissions _permissions;
    private string _pluginId = "";

    /// <summary>按风险档分四组摆：待在自己家里的、动管线的、动用别的东西的、出本机的。</summary>
    public ObservableCollection<PluginPermissionLine> GroupOwn { get; } = new();
    public ObservableCollection<PluginPermissionLine> GroupPipeline { get; } = new();
    public ObservableCollection<PluginPermissionLine> GroupInteract { get; } = new();
    public ObservableCollection<PluginPermissionLine> GroupExternal { get; } = new();
    public ObservableCollection<AuditLine> Audit { get; } = new();

    [ObservableProperty] private string _pluginName = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _declaredRaw = "";
    [ObservableProperty] private string _unknownRaw = "";
    [ObservableProperty] private bool _hasUnknown;
    [ObservableProperty] private bool _hasCustom;
    [ObservableProperty] private bool _auditVisible;
    [ObservableProperty] private string _status = "";

    public bool HasOwn => GroupOwn.Count > 0;
    public bool HasPipeline => GroupPipeline.Count > 0;
    public bool HasInteract => GroupInteract.Count > 0;
    public bool HasExternal => GroupExternal.Count > 0;
    public bool HasDeclared => DeclaredRaw.Length > 0;

    public PluginPermissionsViewModel(PluginHost plugins)
    {
        _plugins = plugins;
        _permissions = plugins.Permissions;
    }

    public async Task LoadAsync(string pluginId)
    {
        _pluginId = pluginId ?? "";
        try
        {
            var entry = await _plugins.FindAsync(_pluginId);
            if (entry is null)
            {
                PluginName = "插件不存在";
                Summary = "它已经被删掉了，授权也跟着清掉了。";
                return;
            }
            PluginName = $"{entry.Manifest.Name} v{entry.Manifest.Version}";
            DeclaredRaw = entry.Manifest.Permissions.Count == 0
                ? "这份清单里没有 permissions 数组：它写于权限目录之前，所以除「出本机」外的能力按默认策略对待"
                    + "（该问的照问、该禁的照禁）。只要它补上任何一条声明，就只认声明过的那几条。"
                : "清单里声明的：" + string.Join("、", entry.Manifest.Permissions);
            var unknown = PluginPermissions.UnknownDeclarations(entry.Manifest);
            HasUnknown = unknown.Count > 0;
            UnknownRaw = HasUnknown
                ? "这几条权限名程序不认识，等于没声明：" + string.Join("、", unknown)
                : "";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Summary = "读取失败：" + ex.Message;
            App.WriteLog("PluginPermissions.Load -> " + ex);
        }
    }

    private async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(_pluginId)) return;
        var entry = await _plugins.FindAsync(_pluginId);
        if (entry is null) return;
        var rows = await _permissions.RowsAsync(_pluginId, entry.Manifest);

        foreach (var group in new[] { GroupOwn, GroupPipeline, GroupInteract, GroupExternal }) group.Clear();
        foreach (var r in rows)
        {
            var line = new PluginPermissionLine(r, this);
            switch (r.Capability.Tier)
            {
                case PluginRiskTier.Own: GroupOwn.Add(line); break;
                case PluginRiskTier.Pipeline: GroupPipeline.Add(line); break;
                case PluginRiskTier.Interact: GroupInteract.Add(line); break;
                default: GroupExternal.Add(line); break;
            }
        }
        OnPropertyChanged(nameof(HasOwn));
        OnPropertyChanged(nameof(HasPipeline));
        OnPropertyChanged(nameof(HasInteract));
        OnPropertyChanged(nameof(HasExternal));

        var declared = rows.Count(r => r.Declared);
        var usable = rows.Count(r => r.Declared && r.Policy != PluginGrant.Deny);
        var custom = rows.Count(r => r.Dirty);
        Summary = $"{rows.Count} 项能力里，这个插件声明了 {declared} 项，现在能用 {usable} 项"
            + (custom == 0 ? "，你还没改过任何一条（全按默认）。" : $"，你改过 {custom} 条。");
        HasCustom = rows.Any(r => r.Dirty);
        OnPropertyChanged(nameof(HasDeclared));

        await _permissions.FlushAsync();
        Audit.Clear();
        foreach (var e in _permissions.AuditOf(_pluginId, 40))
            Audit.Add(new AuditLine(e));
        OnPropertyChanged(nameof(NoAudit));
    }

    [RelayCommand]
    private async Task ResetAll()
    {
        if (string.IsNullOrWhiteSpace(_pluginId)) return;
        if (!await Shell.Current.DisplayAlert("恢复默认",
                $"撤销你对「{PluginName}」做过的全部授权改动？问过之后的答案也一起清掉。", "恢复默认", "先不")) return;
        await _permissions.ResetAsync(_pluginId);
        Status = "已恢复默认策略。";
        await RefreshAsync();
    }

    [RelayCommand]
    private void ToggleAudit() => AuditVisible = !AuditVisible;

    public string AuditToggleLabel => AuditVisible ? "收起" : "展开";
    public bool NoAudit => Audit.Count == 0;

    partial void OnAuditVisibleChanged(bool value) => OnPropertyChanged(nameof(AuditToggleLabel));

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");

    /// <summary>改一条能力的策略。行上的按钮直接调它——命令挂在行上，模板里就不需要 x:Reference。</summary>
    internal async Task ApplyAsync(PluginPermissionLine line, PluginGrant grant)
    {
        if (string.IsNullOrWhiteSpace(_pluginId)) return;
        await _permissions.SetGrantAsync(_pluginId, line.Capability.Name, grant);
        Status = $"「{line.Title}」已设为{PluginCapabilities.GrantLabel(grant)}。"
            + (line.Declared ? "" : "（这个插件没声明它，声明了才用得上。）");
        await RefreshAsync();
    }

    /// <summary>把「上次问过」的答案清掉，这条能力下次撞上时重新问一遍。</summary>
    internal async Task AskAgainAsync(PluginPermissionLine line)
    {
        if (string.IsNullOrWhiteSpace(_pluginId)) return;
        await _permissions.AskAgainAsync(_pluginId, line.Capability.Name);
        Status = $"「{line.Title}」下次用到时会重新问。";
        await RefreshAsync();
    }
}

/// <summary>一行能力：说明书 + 当前策略 + 这个插件到底有没有声明它（三态按钮的激活态也从这里出）。</summary>
public sealed class PluginPermissionLine
{
    public PluginPermissionLine(PluginPermissionRow row, PluginPermissionsViewModel owner)
    {
        Row = row;
        Title = row.Capability.Title;
        Capability = row.Capability;
        Detail = row.Capability.Detail;
        PolicyLabel = PluginCapabilities.GrantLabel(row.Policy);
        IsDeny = row.Policy == PluginGrant.Deny;
        IsAsk = row.Policy == PluginGrant.Ask;
        IsAllow = row.Policy == PluginGrant.Allow;
        Declared = row.Declared;
        ArgsText = row.Args.Count == 0 ? "" : string.Join("、", row.Args);
        HasArgs = ArgsText.Length > 0;
        LastResult = row.LastResult;
        HasLast = LastResult.Length > 0;
        StateLabel = row.Declared ? PolicyLabel : "用不上";
        Note = row.Declared
            ? (row.Dirty ? "你改过这一条" : $"默认{PluginCapabilities.GrantLabel(row.Default)}")
            : "插件没声明这一项：允许了也不会给它——声明才是可审计的。";
        DenyCommand = new Command(() => _ = owner.ApplyAsync(this, PluginGrant.Deny));
        AskCommand = new Command(() => _ = owner.ApplyAsync(this, PluginGrant.Ask));
        AllowCommand = new Command(() => _ = owner.ApplyAsync(this, PluginGrant.Allow));
        AskAgainCommand = new Command(() => _ = owner.AskAgainAsync(this));
    }

    public ICommand DenyCommand { get; }
    public ICommand AskCommand { get; }
    public ICommand AllowCommand { get; }
    public ICommand AskAgainCommand { get; }

    public PluginPermissionRow Row { get; }
    public PluginCapability Capability { get; }
    public string Title { get; }
    public string Detail { get; }
    public string PolicyLabel { get; }
    public bool IsDeny { get; }
    public bool IsAsk { get; }
    public bool IsAllow { get; }
    public bool IsDefault { get; }
    public bool Declared { get; }
    public string ArgsText { get; }
    public bool HasArgs { get; }
    public string LastResult { get; }
    public bool HasLast { get; }
    public string StateLabel { get; }
    public string Note { get; }
    public bool HasArgsText => HasArgs;
}

/// <summary>留痕的一行：什么时候、哪条能力、撞了什么结果。</summary>
public sealed class AuditLine
{
    public AuditLine(PluginPermissionEvent e)
    {
        When = string.IsNullOrWhiteSpace(e.LastAt) ? e.At : e.LastAt;
        Text = e.Capability + (string.IsNullOrWhiteSpace(e.Arg) ? "" : ":" + e.Arg)
             + " → " + e.Result
             + (string.IsNullOrWhiteSpace(e.Note) ? "" : "（" + e.Note + "）");
        Hits = e.Hits > 1 ? $"共 {e.Hits} 次" : "";
        HasHits = Hits.Length > 0;
    }

    public string When { get; }
    public string Text { get; }
    public string Hits { get; }
    public bool HasHits { get; }
}
