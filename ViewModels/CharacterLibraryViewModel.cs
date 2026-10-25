using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 角色库：左边「有谁」，右边「TA 是谁」。
///
/// 以前点一行直接换页，进来时右半页什么都没有——用户的原话是「最大的问题是空间不足，
/// 以及没有刚开始就显示角色预览」。现在一进页就选中第一个人，右边立刻有立绘、名字、性格、
/// 好感与存档数；点另一行只是换预览，真要改东西再按预览里那颗「进入 TA 的工作室」。
/// 这样这一页先回答「这里有哪些人」，而不是把人直接推进编辑表单。
///
/// 除主角外，AI 在对话里仍会按存档白名单调用库里的其他角色。
/// </summary>
public sealed partial class CharacterLibraryViewModel : ObservableObject
{
    private readonly GameEngine _engine;
    private readonly CharacterLibrary _library;
    private readonly SaveManager _save;
    private readonly StorageProvider _store;

    [ObservableProperty] private ObservableCollection<CharacterLibraryItem> _items = new();
    [ObservableProperty] private CharacterLibraryItem? _selected;
    [ObservableProperty] private string _countLabel = "";
    [ObservableProperty] private ImageSource? _previewSource;
    [ObservableProperty] private string _previewName = "";
    [ObservableProperty] private string _previewPersonality = "";
    [ObservableProperty] private string _previewDescription = "";
    [ObservableProperty] private string _previewSaves = "";
    [ObservableProperty] private string _previewAffection = "";
    [ObservableProperty] private string _previewCells = "";
    [ObservableProperty] private string _previewStatus = "";

    public bool HasSelection => Selected is not null;
    public bool HasPreviewImage => PreviewSource is not null;

    /// <summary>这一屏是暗档世界：缺图时垫的图记是黑色印刷件，暗底上要换反白那一版。</summary>
    public bool IsDarkWorld => WarmAsBefore.DesignSystem.Worlds.StyleWorldTokens.Dark;

    public CharacterLibraryViewModel(GameEngine engine, CharacterLibrary library,
        SaveManager save, StorageProvider store)
    {
        _engine = engine;
        _library = library;
        _save = save;
        _store = store;
    }

    [RelayCommand]
    private async Task Refresh()
    {
        var list = await _library.ListAsync();
        var allSaves = await _save.List();
        var mainId = _engine.ActiveCharacter?.Profile.Id ?? _engine.State.CharacterId;
        var keep = Selected?.Id;

        var items = list.Select(ch =>
        {
            var saves = allSaves.Where(s => s.Character == ch.Profile.Id).ToList();
            ImageSource? avatar = null;
            if (!string.IsNullOrEmpty(ch.Avatar))
            {
                var full = Path.Combine(_store.Root, ch.Avatar);
                if (File.Exists(full)) avatar = ImageSource.FromFile(full);
            }
            return new CharacterLibraryItem(ch, avatar, isMain: ch.Profile.Id == mainId, savesCount: saves.Count);
        }).ToList();

        Items = new ObservableCollection<CharacterLibraryItem>(items);
        CountLabel = items.Count == 0 ? "还没有人" : $"{items.Count} 个角色";
        // 一进页就有人站着：优先保持上次选中的那一个，其次才是第一个。
        Selected = items.FirstOrDefault(i => i.Id == keep) ?? items.FirstOrDefault();
    }

    partial void OnSelectedChanged(CharacterLibraryItem? value)
    {
        foreach (var item in Items) item.IsSelected = ReferenceEquals(item, value);
        OnPropertyChanged(nameof(HasSelection));

        if (value is null)
        {
            PreviewSource = null;
            PreviewName = "";
            PreviewPersonality = "";
            PreviewDescription = "";
            PreviewSaves = "";
            PreviewAffection = "";
            PreviewCells = "";
            PreviewStatus = "";
            OnPropertyChanged(nameof(HasPreviewImage));
            return;
        }

        var ch = value.Data;
        PreviewSource = FindPreview(ch);
        PreviewName = value.Name;
        PreviewPersonality = string.IsNullOrWhiteSpace(value.Personality) ? "还没写性格" : value.Personality;
        PreviewDescription = string.IsNullOrWhiteSpace(ch.Profile.Description)
            ? "还没有背景故事：进工作室补一段，AI 才会照着这个人回话。"
            : ch.Profile.Description;
        PreviewSaves = value.SaveCount.ToString();
        PreviewAffection = ch.State.Affection.ToString("F0");
        PreviewCells = value.CellCount.ToString();
        PreviewStatus = value.IsMain ? "当前主角" : "配角";
        OnPropertyChanged(nameof(HasPreviewImage));
    }

    /// <summary>
    /// 预览优先立绘：这张脸是「这一页的主角」，头像只是第二选择。
    /// 两样都没有才返回 null —— 界面据此露出缺图说明，而不是空着一块。
    /// </summary>
    private ImageSource? FindPreview(CharacterData ch)
    {
        foreach (var rel in ch.SpriteMap.Values)
        {
            if (string.IsNullOrWhiteSpace(rel)) continue;
            if (Modules.Affection.SpritePathResolver.ResolveFile(rel, _store.Root) is { } full)
                return ImageSource.FromFile(full);
        }
        if (string.IsNullOrWhiteSpace(ch.Avatar)) return null;
        if (Modules.Affection.SpritePathResolver.ResolveFile(ch.Avatar, _store.Root) is { } avatar)
            return ImageSource.FromFile(avatar);
        var raw = Path.Combine(_store.Root, ch.Avatar);
        return File.Exists(raw) ? ImageSource.FromFile(raw) : null;
    }

    /// <summary>
    /// 选一行只换预览，不换页。不用 CollectionView 自带的选中：它在 ItemsSource 整表替换时
    /// 会把 SelectedItem 写回 null，刚好把 Refresh 挑好的第一个人又清掉 —— 预览就白做了。
    /// </summary>
    [RelayCommand]
    private void Select(CharacterLibraryItem? item)
    {
        if (item is not null) Selected = item;
    }

    /// <summary>预览里那颗主按钮：id 是 GUID，转义只是不让路由替我们猜。</summary>
    [RelayCommand]
    private async Task Enter()
    {
        if (Selected is null) return;
        await Shell.Current.GoToAsync($"studio?id={Uri.EscapeDataString(Selected.Id)}");
    }

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");
}

/// <summary>角色库展示项。</summary>
public sealed partial class CharacterLibraryItem : ObservableObject
{
    public CharacterLibraryItem(CharacterData ch, ImageSource? avatar, bool isMain, int savesCount)
    {
        Data = ch;
        AvatarSource = avatar;
        IsMain = isMain;
        SaveCount = savesCount;
    }

    public CharacterData Data { get; init; }
    public string Id => Data.Profile.Id;
    public string Name => Data.Profile.Name;
    public string Personality => Data.Profile.Personality;
    public ImageSource? AvatarSource { get; init; }
    public bool HasAvatar => AvatarSource is not null;
    public bool IsMain { get; init; }
    public string IsMainLabel => IsMain ? "当前主角" : "";
    public int SaveCount { get; init; }

    /// <summary>有图的格子数：预览里那个 SPRITES 读数，不是「文件夹里有多少文件」。</summary>
    public int CellCount => Data.SpriteMap.Values.Count(v => !string.IsNullOrWhiteSpace(v));

    [ObservableProperty] private bool _isSelected;
}
