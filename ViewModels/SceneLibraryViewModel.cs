using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.Scene;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 场景库页：管理库条目（场景 + 模式 + 变换规则）。
/// - 库条目：新建 / 命名 / 引用地图单场景（MapSceneRef）或直接给背景 / 备注。
/// - 模式：每个条目挂多个模式（背景/背景色/灯光/BGM/时间区间）。
/// - 变换规则（AiRules）：注入 AI 语境的「何时切换」提示文本。
/// - 时间触发 / AI 指令切换由 SceneDirector 执行；本页只做数据管理。
/// </summary>
public sealed partial class SceneLibraryViewModel : ObservableObject
{
    private readonly SceneDirector _director;
    private readonly MapService _map;

    public SceneLibraryViewModel(SceneDirector director, MapService map)
    {
        _director = director;
        _map = map;
    }

    [ObservableProperty] private ObservableCollection<SceneLibraryEntry> _entries = new();

    [ObservableProperty] private SceneLibraryEntry? _selectedEntry;
    partial void OnSelectedEntryChanged(SceneLibraryEntry? value)
    {
        OnPropertyChanged(nameof(SelectedModes));
        OnPropertyChanged(nameof(CanEdit));
    }

    [ObservableProperty] private ObservableCollection<Models.SceneMode> _selectedModes = new();

    public bool CanEdit => SelectedEntry is not null;

    public List<MapService.MapNodeRef> MapScenes =>
        _map.IsLoaded
            ? _map.Map.AllScenes.Select(s => new MapService.MapNodeRef(s.Id, s.Name, true)).ToList()
            : new List<MapService.MapNodeRef>();

    /// <summary>加载库到 UI。</summary>
    [RelayCommand]
    public void Refresh()
    {
        Entries = new ObservableCollection<SceneLibraryEntry>(_director.Library);
        SelectedEntry = Entries.FirstOrDefault();
        SyncModes();
    }

    private void SyncModes()
    {
        SelectedModes = new ObservableCollection<Models.SceneMode>(SelectedEntry?.Modes ?? new List<Models.SceneMode>());
    }

    [RelayCommand]
    private async Task AddEntry()
    {
        var name = await Shell.Current.DisplayPromptAsync("新场景库条目", "命名", "未命名场景");
        if (string.IsNullOrWhiteSpace(name)) return;
        _director.AddEntry(name.Trim());
        await _director.SaveAsync();
        Refresh();
    }

    [RelayCommand]
    private async Task RenameEntry()
    {
        if (SelectedEntry is not { } e) return;
        var name = await Shell.Current.DisplayPromptAsync("重命名", "名称", e.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        e.Name = name.Trim();
        await _director.SaveAsync();
        Refresh();
    }

    [RelayCommand]
    private async Task RemoveEntry()
    {
        if (SelectedEntry is not { } e) return;
        _director.RemoveEntry(e.Id);
        await _director.SaveAsync();
        Refresh();
    }

    [RelayCommand]
    private async Task AddMode()
    {
        if (SelectedEntry is not { } e) return;
        var name = await Shell.Current.DisplayPromptAsync("新模式", "模式名（如 夜灯 / 雨天）", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        _director.AddMode(e, name.Trim());
        await _director.SaveAsync();
        Refresh();
    }

    /// <summary>引用地图单场景（MapSceneRef）：库条目直接复用地图里的场景。</summary>
    [RelayCommand]
    private async Task LinkMapScene()
    {
        if (SelectedEntry is not { } e) return;
        var scenes = MapScenes;
        if (scenes.Count == 0)
        {
            await Shell.Current.DisplayAlert("", "地图尚未加载", "好");
            return;
        }
        var options = scenes.Select(s => s.Name).Append("(取消)").ToArray();
        var pick = await Shell.Current.DisplayActionSheet("选择要引用的地图场景", "取消", null,
            options[..^1]);
        if (pick is null) return;
        var target = scenes.FirstOrDefault(s => s.Name == pick);
        if (!string.IsNullOrEmpty(target.Id))
        {
            e.MapSceneRef = target.Id;
            await _director.SaveAsync();
            Refresh();
        }
    }

    /// <summary>编辑变换规则（AiRules）：注入 AI 语境的「何时切换」提示。</summary>
    [RelayCommand]
    private async Task EditRules()
    {
        if (SelectedEntry is not { } e) return;
        var cur = e.AiRules ?? "";
        var text = await Shell.Current.DisplayPromptAsync(
            "变换规则", "例：下雨时切到室内；深夜自动关灯。", cur);
        if (text is null) return;
        e.AiRules = text.Trim();
        await _director.SaveAsync();
        OnPropertyChanged(nameof(Entries));
    }

    [RelayCommand]
    private void Back() => Shell.Current.GoToAsync("..");
}
