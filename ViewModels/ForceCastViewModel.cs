using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 「点名」页 ViewModel：人类主动点名一个或多个角色，指定各自的初始位置（场景），
/// 确定后被点名的角色会被：
///   1) 把 <see cref="CharacterState.Location"/> 设为指定场景并落盘（初始位置）；
///   2) 强制加微信 → 变成常驻 NPC（出现在微信联系人里）；
///   3) 第一个被点名的角色强制进入对话剧情（Boot + SetCharacter + 导航到游戏页）。
/// </summary>
public sealed partial class ForceCastViewModel : ObservableObject
{
    private readonly CharacterLibrary _library;
    private readonly MapService _map;
    private readonly WeChatService _wechat;
    private readonly StorageProvider _store;

    public ForceCastViewModel(CharacterLibrary library, MapService map,
        WeChatService wechat, StorageProvider store)
    {
        _library = library;
        _map = map;
        _wechat = wechat;
        _store = store;
    }

    /// <summary>角色条目（每个角色一个：勾选 + 选场景）。</summary>
    public ObservableCollection<ForceCastEntry> Entries { get; } = new();

    /// <summary>可选场景（地图里的所有场景）。</summary>
    public ObservableCollection<MapScene> Scenes { get; } = new();

    public bool HasCharacters => Entries.Count > 0;
    public bool HasNoCharacters => Entries.Count == 0;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "勾选要点名的角色，并为 TA 指定初始位置";

    /// <summary>进入页面：加载角色 + 场景。</summary>
    public async Task LoadAsync()
    {
        await _map.InitializeAsync();
        var scenes = _map.Map.AllScenes.ToList();

        var chars = await _library.ListAsync();
        var items = new List<ForceCastEntry>();
        foreach (var ch in chars)
        {
            // 头像：转成绝对路径（MAUI Image 需要能直接读取的路径）
            var avatarPath = "";
            if (!string.IsNullOrEmpty(ch.Avatar))
            {
                var full = Path.Combine(_store.Root, ch.Avatar);
                if (File.Exists(full)) avatarPath = full;
            }
            items.Add(new ForceCastEntry
            {
                CharacterId = ch.Profile.Id,
                CharacterName = ch.Profile.Name,
                Avatar = avatarPath,
                // 默认场景 = 角色当前所在场景（若有效），否则地图第一个场景
                SceneId = _map.Map.SceneExists(ch.State.Location)
                    ? ch.State.Location
                    : (scenes.FirstOrDefault()?.Id ?? "")
            });
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Scenes.Clear();
            foreach (var s in scenes) Scenes.Add(s);

            Entries.Clear();
            foreach (var it in items)
            {
                // 注入共享的场景列表 + 回填场景对象（Picker 使用）
                it.SceneOptions = Scenes;
                it.SelectedScene = scenes.FirstOrDefault(s => s.Id == it.SceneId);
                if (it.SceneName.Length == 0)
                    it.SceneName = it.SelectedScene?.Name ?? "";
                Entries.Add(it);
            }
            OnPropertyChanged(nameof(HasCharacters));
            OnPropertyChanged(nameof(HasNoCharacters));
        });
    }


    /// <summary>选中的角色数（供确定按钮文案）。</summary>
    public int SelectedCount => Entries.Count(e => e.Selected);

    /// <summary>
    /// 确定：对每个勾选的角色执行点名流程。
    /// 返回是否真的进入了对话剧情。
    /// </summary>
    [RelayCommand]
    private async Task Confirm()
    {
        if (IsBusy) return;
        var picked = Entries.Where(e => e.Selected).ToList();
        if (picked.Count == 0)
        {
            Toast.Warn("还没有勾选任何角色。");
            return;
        }
        IsBusy = true;
        try
        {
            var chars = await _library.ListAsync();
            string? firstId = null;

            foreach (var entry in picked)
            {
                var ch = chars.FirstOrDefault(c => c.Profile.Id == entry.CharacterId);
                if (ch is null) continue;

                // 1) 设置初始位置（场景）并落盘
                var sceneId = string.IsNullOrEmpty(entry.SceneId)
                    ? (_map.Map.AllScenes.FirstOrDefault()?.Id ?? "")
                    : entry.SceneId;
                ch.State.Location = sceneId;
                await _library.UpdateAsync(ch);

                // 2) 强制加微信 → 常驻 NPC
                await _wechat.AddContactAsync(ch.Profile.Id);

                if (firstId is null) firstId = ch.Profile.Id;
            }

            // 3) 第一个被点名的角色进入剧情：开局页负责绑定世界树与人设
            if (firstId is not null)
            {
                await Shell.Current.GoToAsync($"newrun?character={Uri.EscapeDataString(firstId)}");
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("ForceCastViewModel.Confirm -> " + ex);
            Toast.Error("点名失败：" + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Back()
    {
        try { await Shell.Current.GoToAsync(".."); }
        catch (Exception ex) { App.WriteLog("ForceCastViewModel.Back -> " + ex.Message); }
    }
}