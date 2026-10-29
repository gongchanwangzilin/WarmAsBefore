using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>展示案例表页：管理展示案例列表，新建/编辑/播放/删除。</summary>
public sealed partial class ShowcaseListViewModel : ObservableObject
{
    private readonly Modules.Showcase.ShowcaseStore _store;

    public ObservableCollection<ShowcaseScript> Scripts { get; } = new();

    public ShowcaseListViewModel(Modules.Showcase.ShowcaseStore store)
    {
        _store = store;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var all = await _store.LoadAsync();
        Scripts.Clear();
        foreach (var s in all.OrderByDescending(s => s.UpdatedAt))
            Scripts.Add(s);
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        var name = await Shell.Current.DisplayPromptAsync("新建展示案", "展示案名称：", "创建", "取消", "我的第一段演出");
        if (string.IsNullOrWhiteSpace(name)) return;
        var script = new ShowcaseScript
        {
            Name = name.Trim(),
            Description = "",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        if (!await _store.UpsertAsync(script))
        {
            Toast.Error("写入失败，重试");
            return;
        }
        await LoadAsync();
        ShowcaseEditViewModel.PendingScriptId = script.Id;
        await Shell.Current.GoToAsync("showcase-edit");
    }

    [RelayCommand]
    private async Task EditAsync(ShowcaseScript? script)
    {
        if (script is null) return;
        ShowcaseEditViewModel.PendingScriptId = script.Id;
        await Shell.Current.GoToAsync("showcase-edit");
    }

    [RelayCommand]
    private async Task PlayAsync(ShowcaseScript? script)
    {
        if (script is null) return;
        if (script.Steps.Count == 0)
        {
            Toast.Info("该展示案还没有任何步骤，无法播放。");
            return;
        }
        ShowcasePlayViewModel.PendingScriptId = script.Id;
        await Shell.Current.GoToAsync("showcase-play");
    }

    [RelayCommand]
    private async Task DeleteAsync(ShowcaseScript? script)
    {
        if (script is null) return;
        var confirm = await Shell.Current.DisplayActionSheet($"确定删除「{script.Name}」？", "取消", null, "删除");
        if (confirm != "删除") return;
        await _store.DeleteAsync(script.Id);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task BackAsync() => await Shell.Current.GoToAsync("..");
}

/// <summary>展示案例步骤：编辑器中用于回显/上移/下移/删除的包装。</summary>
public sealed partial class ShowcaseStepDisplay : ObservableObject
{
    public ShowcaseStep Step { get; init; } = new();
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _sub = "";

    /// <summary>重新计算显示文本。</summary>
    public void RefreshLabel(string characterName)
    {
        Label = Step.Type switch
        {
            "dialogue" => Step.Speaker == "user" ? $"💬 你：{Truncate(Step.Text,30)}" : $"💬 {characterName}：{Truncate(Step.Text,30)}",
            "cg" => $"🖼️ CG · {Path.GetFileName(Step.CgPath)}",
            "sprites" => $"✨ 立绘 ×{Step.Sprites.Count}",
            _ => Step.Type
        };
        Sub = Step.Type switch
        {
            "dialogue" => "",
            "cg" => Step.CgPath,
            "sprites" => string.Join(" + ", Step.Sprites.Select(s => $"{PosCn(s.Position)}×1")),
            _ => ""
        };
    }

    private static string PosCn(string p) => p switch { "left" => "左", "right" => "右", _ => "中" };
    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
