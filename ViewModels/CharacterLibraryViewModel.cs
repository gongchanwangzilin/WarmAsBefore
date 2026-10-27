using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using WarmAsBefore.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 角色库：左边「有谁」，右边「TA 是谁」。
///
/// 一进页选中第一个人，右边显示立绘、名字、性格、好感与存档数；
/// 点另一行只换预览，要编辑再按预览里那颗「进入 TA 的工作室」。
/// 这样这一页先回答「这里有哪些人」，不直接把人推进编辑表单。
///
/// 除主角外，AI 在对话里仍会按存档白名单调用库里的其他角色。
/// </summary>
public sealed partial class CharacterLibraryViewModel : ObservableObject
{
    private readonly GameEngine _engine;
    private readonly CharacterLibrary _library;
    private readonly SaveManager _save;
    private readonly StorageProvider _store;
    private readonly CharacterPackService _packs;
    private readonly SettingsManager _settings;
    private bool _exporting;

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

    // ── 单角色背景（需求 5）────────────────────────────────────────────
    [ObservableProperty] private CharacterBackdropState? _previewBackdrop;
    [ObservableProperty] private bool _hasBackground;
    [ObservableProperty] private string _backgroundHint = "";
    public ObservableCollection<BackgroundFitItem> BackgroundFits { get; } = new();

    public bool HasSelection => Selected is not null;
    public bool HasPreviewImage => PreviewSource is not null;

    /// <summary>这一屏是暗档世界：缺图时垫的图记是黑色印刷件，暗底上要换反白那一版。</summary>
    public bool IsDarkWorld => WarmAsBefore.DesignSystem.Worlds.StyleWorldTokens.Dark;

    public CharacterLibraryViewModel(GameEngine engine, CharacterLibrary library,
        SaveManager save, StorageProvider store, CharacterPackService packs, SettingsManager settings)
    {
        _engine = engine;
        _library = library;
        _save = save;
        _store = store;
        _packs = packs;
        _settings = settings;
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
            return new CharacterLibraryItem(ch, avatar, isMain: ch.Profile.Id == mainId, savesCount: saves.Count,
                displayName: CharacterMask.DisplayName(ch, _settings.Current.MaskUnintroducedNames));
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
            PreviewBackdrop = null;
            HasBackground = false;
            BackgroundHint = "";
            foreach (var fit in BackgroundFits) fit.IsOn = false;
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
        ApplyBackground(ch);
        OnPropertyChanged(nameof(HasPreviewImage));
    }

    /// <summary>
    /// 选中谁就换谁的背景：路径与适配先解析成一条快照，视图层只认它。
    /// 背景不存在/没设时把快照清空，露出井面底色，而不是留着上一个人的图。
    /// </summary>
    private void ApplyBackground(CharacterData ch)
    {
        var path = CharacterBackgroundService.Resolve(ch, _store.Root);
        HasBackground = !string.IsNullOrWhiteSpace(path);
        BackgroundHint = HasBackground
            ? $"背景：{System.IO.Path.GetFileName(path)} · {CharacterBackgroundService.FitLabel(ch.BackgroundFit)}"
            : "还没有专属背景：点「换背景」选一张图 / GIF / 视频。";
        PreviewBackdrop = HasBackground
            ? new CharacterBackdropState(
                path!, CharacterBackgroundService.NormalizeFit(ch.BackgroundFit),
                ch.BackgroundCrop.X, ch.BackgroundCrop.Y, ch.BackgroundCrop.W, ch.BackgroundCrop.H,
                CharacterBackgroundService.IsVideo(path))
            : null;

        BackgroundFits.Clear();
        foreach (var fit in new[] { "fill", "tile", "stretch", "fit", "crop" })
        {
            BackgroundFits.Add(new BackgroundFitItem(fit, CharacterBackgroundService.FitLabel(fit))
            {
                IsOn = string.Equals(CharacterBackgroundService.NormalizeFit(ch.BackgroundFit), fit, StringComparison.Ordinal)
            });
        }
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

    /// <summary>
    /// 导出这一行的人：单个角色包，发给别人而不是搬整台机器。
    /// 只是分享一个文件，不碰角色库本身；分享面板由系统弹，桌面与 Android 同一条路。
    /// </summary>
    [RelayCommand]
    private async Task Export(CharacterLibraryItem? item)
    {
        if (item is null || _exporting) return;
        _exporting = true;
        item.IsExporting = true;
        try
        {
            var result = await _packs.ExportAsync(item.Data);
            if (!result.Ok)
            {
                Toast.Error(result.Message);
                return;
            }

            try
            {
                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = $"分享「{item.Name}」的角色包",
                    File = new ShareFile(result.ZipPath)
                });
                Toast.Info($"角色包已生成并分享：{Path.GetFileName(result.ZipPath)}（{DataPackageService.FormatSize(result.Size)}）");
            }
            catch (Exception ex)
            {
                // 包已经打好了，分享面板没打开：把位置说清楚，至少能自己去取。
                App.WriteLog("CharacterLibrary.Export share -> " + ex);
                Toast.Error($"角色包已生成，但分享面板没打开：{result.ZipPath}");
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("CharacterLibrary.Export -> " + ex);
            Toast.Error("导出失败：" + ex.Message);
        }
        finally
        {
            item.IsExporting = false;
            _exporting = false;
        }
    }

    /// <summary>顶部那颗「新建角色」：走四步向导，不在这里塞表单。</summary>
    [RelayCommand]
    private async Task NewCharacter() => await Shell.Current.GoToAsync("newcharacter");

    /// <summary>
    /// 给选中的人换专属背景。图片 / GIF / 视频都走同一条导入口，
    /// 旧背景文件会由服务清掉；失败时不改角色字段。
    /// </summary>
    [RelayCommand]
    private async Task PickBackground()
    {
        if (Selected is null) return;
        var ch = Selected.Data;
        try
        {
            var file = await FilePicker.PickAsync(new PickOptions
            {
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".mp4", ".mov", ".m4v", ".webm" } },
                    { DevicePlatform.Android, new[] { "image/*", "video/*" } },
                    { DevicePlatform.iOS, new[] { "public.image", "public.movie" } }
                }),
                PickerTitle = $"给「{ch.Profile.Name}」挑一张背景"
            });
            if (file is null) return;

            var rel = await CharacterBackgroundService.ImportAsync(ch.Profile.Id, file.FullPath, _store.Root);
            var updated = ch with { Background = rel, BackgroundInGame = true };
            if (await ApplyCharacterUpdate(updated))
                Toast.Success($"「{ch.Profile.Name}」的背景已换。");
        }
        catch (Exception ex)
        {
            App.WriteLog("CharacterLibrary.PickBackground -> " + ex);
            Toast.Error("换背景失败：" + ex.Message);
        }
    }

    /// <summary>五种适配：填充 / 平铺 / 拉伸 / 完整 / 裁剪。选了就落盘，选中即刻生效。</summary>
    [RelayCommand]
    private async Task SetBackgroundFit(string? fit)
    {
        if (Selected is null) return;
        var normalized = CharacterBackgroundService.NormalizeFit(fit);
        var ch = Selected.Data;
        var crop = ch.BackgroundCrop;
        // 第一次切到「裁剪」时给一个居中的 80% 框，否则 0,0,1,1 看起来和填充一模一样。
        if (normalized == "crop" && crop.W >= 0.999 && crop.H >= 0.999)
            crop = new BackgroundCrop { X = 0.1, Y = 0.1, W = 0.8, H = 0.8 };
        var updated = ch with { BackgroundFit = normalized, BackgroundCrop = crop };
        if (await ApplyCharacterUpdate(updated))
            Toast.Info($"背景适配：{CharacterBackgroundService.FitLabel(normalized)}。");
    }

    private async Task<bool> ApplyCharacterUpdate(CharacterData updated)
    {
        if (!await _library.UpdateAsync(updated))
        {
            Toast.Error("没能写进盘：详见日志。");
            return false;
        }
        var index = Selected is null ? -1 : Items.IndexOf(Selected);
        if (index >= 0)
        {
            var old = Items[index];
            var item = new CharacterLibraryItem(updated, old.AvatarSource, old.IsMain, old.SaveCount,
                CharacterMask.DisplayName(updated, _settings.Current.MaskUnintroducedNames))
            {
                IsSelected = true
            };
            Items[index] = item;
            Selected = item;
        }
        return true;
    }

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");
}

/// <summary>背景适配的一颗选项。</summary>
public sealed partial class BackgroundFitItem : ObservableObject
{
    public BackgroundFitItem(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }
    public string Label { get; }
    [ObservableProperty] private bool _isOn;
}

/// <summary>角色库展示项。</summary>
public sealed partial class CharacterLibraryItem : ObservableObject
{
    public CharacterLibraryItem(CharacterData ch, ImageSource? avatar, bool isMain, int savesCount,
        string? displayName = null)
    {
        Data = ch;
        AvatarSource = avatar;
        IsMain = isMain;
        SaveCount = savesCount;
        DisplayName = displayName ?? ch.Profile.Name;
    }

    public CharacterData Data { get; init; }
    public string Id => Data.Profile.Id;
    public string DisplayName { get; init; }
    public string Name => DisplayName;
    public string Personality => Data.Profile.Personality;
    public ImageSource? AvatarSource { get; init; }
    public bool HasAvatar => AvatarSource is not null;
    public bool IsMain { get; init; }
    public string IsMainLabel => IsMain ? "当前主角" : "";
    public int SaveCount { get; init; }

    /// <summary>有图的格子数：预览里那个 SPRITES 读数，不是「文件夹里有多少文件」。</summary>
    public int CellCount => Data.SpriteMap.Values.Count(v => !string.IsNullOrWhiteSpace(v));

    [ObservableProperty] private bool _isSelected;

    /// <summary>这一行的「导出」正忙：只禁这一颗，不把整张名册锁住。</summary>
    [ObservableProperty] private bool _isExporting;
}
