using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using WarmAsBefore.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 新建角色向导：基本 → 人设 → 形象 → 世界观，一次只问一件事。
///
/// 起手先用 <see cref="CharacterLibrary.CreateDefault"/> 拿一个带 id 的草稿：
/// 头像裁剪要写进 <c>assets/characters/{id}/avatar.png</c>，没有 id 就没地方落。
/// 草稿在最后一按之前不进角色库；中途放弃时把这次写进草稿目录的文件一起收掉，
/// 不让半个没人认领的角色文件夹留在盘上。
///
/// 世界观那一步可以整步跳过：不选世界书、语音与模型都跟随全局，与工作室里的默认值一致。
/// </summary>
public sealed partial class NewCharacterViewModel : ObservableObject
{
    private const int LastStep = 3;

    private readonly CharacterLibrary _library;
    private readonly GenderLibrary _genderLib;
    private readonly WorldTreeService _worlds;
    private readonly StorageProvider _store;
    private readonly AvatarCropService _crop;
    private readonly CharacterData _draft;

    private bool _loaded;
    private bool _created;
    private bool _leaving;

    public ObservableCollection<GenderChoice> Genders { get; } = new();
    public ObservableCollection<WorldbookChoice> Worlds { get; } = new();

    // ── 步骤 ────────────────────────────────────────────────────────────────
    [ObservableProperty] private int _step;

    // ── ① 基本 ──────────────────────────────────────────────────────────────
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _nameError = "";
    [ObservableProperty] private string _gender = "";
    [ObservableProperty] private string _genderError = "";
    [ObservableProperty] private string _userAddress = "主人";
    [ObservableProperty] private bool _isRomance = true;

    // ── ② 人设 ──────────────────────────────────────────────────────────────
    [ObservableProperty] private string _personality = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _greeting = "";

    // ── ③ 形象 ──────────────────────────────────────────────────────────────
    [ObservableProperty] private string _spriteRel = "";
    [ObservableProperty] private string _spriteName = "";
    [ObservableProperty] private string _avatarRel = "";
    [ObservableProperty] private string _avatarName = "";
    [ObservableProperty] private ImageSource? _previewSource;

    // ── ④ 世界观 ────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _hasNoWorlds;

    // ── 状态 ────────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isBusy;

    public NewCharacterViewModel(CharacterLibrary library, GenderLibrary genderLib,
        WorldTreeService worlds, StorageProvider store, AvatarCropService crop)
    {
        _library = library;
        _genderLib = genderLib;
        _worlds = worlds;
        _store = store;
        _crop = crop;
        // 只借一个 id 与副设定的起点；名字/性别在创建那一刻才真正写进去。
        _draft = library.CreateDefault("", "", "");
    }

    public string DraftId => _draft.Profile.Id;

    // ── 派生显示 ────────────────────────────────────────────────────────────
    public bool AtStep1 => Step == 0;
    public bool AtStep2 => Step == 1;
    public bool AtStep3 => Step == 2;
    public bool AtStep4 => Step >= LastStep;
    public bool CanGoBack => Step > 0;
    public bool IsLastStep => Step >= LastStep;
    public bool ShowNext => !IsLastStep;
    public string StepEyebrow => $"{Math.Min(Step, LastStep) + 1} / {LastStep + 1}";
    public string StepLabel => Step switch
    {
        0 => "基本",
        1 => "人设",
        2 => "形象",
        _ => "世界观"
    };

    public bool Reached1 => Step >= 0;
    public bool Reached2 => Step >= 1;
    public bool Reached3 => Step >= 2;
    public bool Reached4 => Step >= 3;

    public bool HasNameError => !string.IsNullOrWhiteSpace(NameError);
    public bool HasGenderError => !string.IsNullOrWhiteSpace(GenderError);
    public bool HasPreview => PreviewSource is not null;
    public bool HasSprite => !string.IsNullOrWhiteSpace(SpriteRel);
    public bool HasAvatar => !string.IsNullOrWhiteSpace(AvatarRel);

    /// <summary>暗档世界里缺图用的是反白那版图记（与角色库、工作室同一判定）。</summary>
    public bool IsDarkWorld => WarmAsBefore.DesignSystem.Worlds.StyleWorldTokens.Dark;

    public string PreviewName => string.IsNullOrWhiteSpace(Name) ? "还没起名" : Name.Trim();
    public string PersonalityPreview => string.IsNullOrWhiteSpace(Personality) ? "性格还没写" : Personality.Trim();

    public string PreviewMeta
    {
        get
        {
            var gender = string.IsNullOrWhiteSpace(Gender) ? "性别未选" : Gender.Trim();
            var address = string.IsNullOrWhiteSpace(UserAddress) ? "主人" : UserAddress.Trim();
            return $"{gender} · {(IsRomance ? "恋爱对象" : "伙伴")} · 称呼你「{address}」";
        }
    }

    public string SpriteLabel => HasSprite ? $"default/normal · {SpriteName}" : "还没有选立绘";
    public string AvatarLabel => HasAvatar ? AvatarName : "还没有头像（创建时会先用立绘）";

    public string SelectedWorldLabel
    {
        get
        {
            var picked = Worlds.Where(w => w.IsSelected).Select(w => w.Title).ToList();
            return picked.Count == 0 ? "不绑定世界书（可以跳过）" : "已选：" + string.Join("、", picked);
        }
    }

    public string SelectedWorldCountLabel
    {
        get
        {
            var count = Worlds.Count(w => w.IsSelected);
            return count == 0 ? "未选择" : $"{count} / {Worlds.Count}";
        }
    }

    // ── 读盘 ────────────────────────────────────────────────────────────────
    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            await Task.WhenAll(LoadGendersAsync(), LoadWorldsAsync());
        }
        catch (Exception ex)
        {
            App.WriteLog("NewCharacter.Load -> " + ex);
        }
    }

    private async Task LoadGendersAsync()
    {
        if (Genders.Count > 0) return;
        var list = await _genderLib.AllAsync();
        Genders.Clear();
        foreach (var g in list)
            if (!string.IsNullOrWhiteSpace(g.Name))
                Genders.Add(new GenderChoice(g.Name));
        // 只有一个候选时替用户选上；有多个就留空，让「必填」真的是一道选择。
        if (string.IsNullOrWhiteSpace(Gender) && Genders.Count == 1)
            PickGender(Genders[0].Name);
    }

    private async Task LoadWorldsAsync()
    {
        if (Worlds.Count > 0) return;
        var list = await _worlds.ListAsync();
        Worlds.Clear();
        foreach (var w in list)
            Worlds.Add(new WorldbookChoice(w));
        HasNoWorlds = Worlds.Count == 0;
        OnPropertyChanged(nameof(SelectedWorldLabel));
        OnPropertyChanged(nameof(SelectedWorldCountLabel));
    }

    // ── 步骤操作 ────────────────────────────────────────────────────────────
    [RelayCommand]
    private void Next()
    {
        if (IsBusy) return;
        if (Step == 0 && !ValidateBasics()) return;
        if (Step < LastStep) Step++;
    }

    [RelayCommand]
    private void Prev()
    {
        if (IsBusy || Step <= 0) return;
        Step--;
    }

    /// <summary>名字 / 性别是必填：就地亮一行提示，不弹窗、也不悄悄替用户猜一个。</summary>
    private bool ValidateBasics()
    {
        NameError = string.IsNullOrWhiteSpace(Name) ? "名字不能空着：先给 TA 起个名字。" : "";
        GenderError = string.IsNullOrWhiteSpace(Gender) ? "选一个性别：称呼与周期规则都从这里来。" : "";
        return !HasNameError && !HasGenderError;
    }

    [RelayCommand]
    private void PickGender(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        Gender = name.Trim();
        foreach (var g in Genders)
            g.IsSelected = string.Equals(g.Name, Gender, StringComparison.OrdinalIgnoreCase);
        GenderError = "";
    }

    [RelayCommand]
    private void ToggleWorld(WorldbookChoice? choice)
    {
        if (choice is null) return;
        choice.IsSelected = !choice.IsSelected;
        OnPropertyChanged(nameof(SelectedWorldLabel));
        OnPropertyChanged(nameof(SelectedWorldCountLabel));
    }

    // ── 形象 ────────────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task PickSprite()
    {
        if (IsBusy) return;
        try
        {
            var file = await PickImageAsync("挑一张立绘");
            if (file is null) return;

            IsBusy = true;
            var rel = await CopyIntoDraftAsync(file.FullPath, "default");
            if (rel is null)
            {
                Toast.Error("这张图没能读进来，换一张试试。");
                return;
            }
            SpriteRel = rel;
            SpriteName = Path.GetFileName(file.FileName);
            Toast.Info("立绘已放进 default/normal。");
        }
        catch (Exception ex)
        {
            App.WriteLog("NewCharacter.PickSprite -> " + ex);
            Toast.Error("选立绘失败：" + ex.Message);
        }
        finally { IsBusy = false; }
    }

    /// <summary>选头像 → 进裁剪页（服务会等这一页回传结果）→ 拿回相对存储根的路径。</summary>
    [RelayCommand]
    private async Task PickAvatarCrop()
    {
        if (IsBusy) return;
        try
        {
            var file = await PickImageAsync("挑一张头像");
            if (file is null) return;

            IsBusy = true;
            var rel = await _crop.OpenAsync(_draft.Profile.Id, file.FullPath);
            if (string.IsNullOrWhiteSpace(rel))
            {
                Toast.Info("没有裁剪：头像保持原样。");
                return;
            }
            AvatarRel = rel;
            AvatarName = "已裁剪的头像";
            Toast.Info("头像已裁剪好，创建后会跟着入库。");
        }
        catch (Exception ex)
        {
            App.WriteLog("NewCharacter.PickAvatarCrop -> " + ex);
            Toast.Error("裁剪头像失败：" + ex.Message);
        }
        finally { IsBusy = false; }
    }

    /// <summary>不想裁就直接用原图：拷进这个草稿的目录，路径仍按相对存储根记。</summary>
    [RelayCommand]
    private async Task PickAvatarDirect()
    {
        if (IsBusy) return;
        try
        {
            var file = await PickImageAsync("挑一张头像");
            if (file is null) return;

            IsBusy = true;
            var rel = await CopyIntoDraftAsync(file.FullPath, "");
            if (rel is null)
            {
                Toast.Error("这张图没能读进来，换一张试试。");
                return;
            }
            AvatarRel = rel;
            AvatarName = Path.GetFileName(file.FileName);
            Toast.Info("头像已选好，创建后会跟着入库。");
        }
        catch (Exception ex)
        {
            App.WriteLog("NewCharacter.PickAvatarDirect -> " + ex);
            Toast.Error("选头像失败：" + ex.Message);
        }
        finally { IsBusy = false; }
    }

    private static Task<FileResult?> PickImageAsync(string title) =>
        FilePicker.PickAsync(new PickOptions
        {
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" } },
                { DevicePlatform.Android, new[] { "image/*" } },
                { DevicePlatform.iOS, new[] { "public.image" } }
            }),
            PickerTitle = title
        });

    /// <summary>把挑中的图拷进草稿目录。<paramref name="subDir"/> 为空时落在角色根目录。</summary>
    private async Task<string?> CopyIntoDraftAsync(string sourcePath, string subDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return null;
            var ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
            var stem = SafeStem(Path.GetFileNameWithoutExtension(sourcePath));

            var relDir = string.IsNullOrWhiteSpace(subDir)
                ? $"assets/characters/{_draft.Profile.Id}"
                : $"assets/characters/{_draft.Profile.Id}/{subDir}";
            var dir = Path.Combine(_store.Root, relDir.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, stem + ext);
            await Task.Run(() => File.Copy(sourcePath, dest, overwrite: true));
            return $"{relDir}/{stem}{ext}";
        }
        catch (Exception ex)
        {
            App.WriteLog("NewCharacter.CopyIntoDraft -> " + ex);
            return null;
        }
    }

    private static string SafeStem(string raw)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string((raw ?? "").Where(c => Array.IndexOf(bad, c) < 0 && c != '/').ToArray()).Trim();
        if (clean.Length == 0) clean = "image";
        return clean.Length > 48 ? clean[..48] : clean;
    }

    // ── 创建 ────────────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task Create()
    {
        if (IsBusy) return;
        if (!ValidateBasics())
        {
            Step = 0;
            return;
        }

        IsBusy = true;
        try
        {
            var profile = _draft.Profile with
            {
                Name = Name.Trim(),
                Gender = Gender.Trim(),
                Personality = string.IsNullOrWhiteSpace(Personality) ? _draft.Profile.Personality : Personality.Trim(),
                Greeting = Greeting.Trim(),
                UserAddress = string.IsNullOrWhiteSpace(UserAddress) ? "主人" : UserAddress.Trim(),
                Description = string.IsNullOrWhiteSpace(Description) ? _draft.Profile.Description : Description.Trim(),
                IsRomanceTarget = IsRomance
            };

            var spriteMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(SpriteRel))
                spriteMap["default/normal"] = SpriteRel;

            var data = _draft with
            {
                Profile = profile,
                SpriteMap = spriteMap,
                Avatar = AvatarRel,
                WorldbookIds = Worlds.Where(w => w.IsSelected).Select(w => w.Id).ToList(),
                // 副设定要按最终的主设定重新播种：草稿那会儿的名字还是「小雨」。
                SubProfile = CharacterSubProfileService.Seed(profile),
                // 语音与模型都留默认：空字段在工作室里读作「跟随全局」。
                Voice = new CharacterVoice(),
                Model = new CharacterModelBinding()
            };

            if (!await _library.AddAsync(data))
            {
                Toast.Error("没能保存这个角色，稍后再试。");
                return;
            }

            _created = true;
            Toast.Success($"「{profile.Name}」已进入角色库");
            if (Shell.Current is not null) await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            App.WriteLog("NewCharacter.Create -> " + ex);
            Toast.Error("创建失败：" + ex.Message);
        }
        finally { IsBusy = false; }
    }

    // ── 离开 ────────────────────────────────────────────────────────────────
    /// <summary>放弃这次向导：还没创建就把草稿目录里刚写进去的图一起删掉。</summary>
    public async Task LeaveAsync()
    {
        if (_leaving) return;
        _leaving = true;
        await CleanupDraftAsync();
        if (Shell.Current is not null) await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private Task Back() => LeaveAsync();

    private async Task CleanupDraftAsync()
    {
        if (_created) return;
        try
        {
            var dir = Path.Combine(_store.Root, "assets", "characters", _draft.Profile.Id);
            if (Directory.Exists(dir))
                await Task.Run(() => Directory.Delete(dir, recursive: true));
        }
        catch (Exception ex)
        {
            // 删不掉（被占用等）只记一笔：这是清理，不是这次创建的一部分。
            App.WriteLog("NewCharacter.Cleanup -> " + ex);
        }
    }

    // ── 派生属性刷新 ────────────────────────────────────────────────────────
    partial void OnStepChanged(int value)
    {
        OnPropertyChanged(nameof(AtStep1));
        OnPropertyChanged(nameof(AtStep2));
        OnPropertyChanged(nameof(AtStep3));
        OnPropertyChanged(nameof(AtStep4));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(ShowNext));
        OnPropertyChanged(nameof(StepEyebrow));
        OnPropertyChanged(nameof(StepLabel));
        OnPropertyChanged(nameof(Reached1));
        OnPropertyChanged(nameof(Reached2));
        OnPropertyChanged(nameof(Reached3));
        OnPropertyChanged(nameof(Reached4));
    }

    partial void OnNameChanged(string value)
    {
        if (HasNameError && !string.IsNullOrWhiteSpace(value)) NameError = "";
        OnPropertyChanged(nameof(PreviewName));
    }

    partial void OnNameErrorChanged(string value) => OnPropertyChanged(nameof(HasNameError));
    partial void OnGenderErrorChanged(string value) => OnPropertyChanged(nameof(HasGenderError));

    partial void OnGenderChanged(string value)
    {
        if (HasGenderError && !string.IsNullOrWhiteSpace(value)) GenderError = "";
        OnPropertyChanged(nameof(PreviewMeta));
    }

    partial void OnUserAddressChanged(string value) => OnPropertyChanged(nameof(PreviewMeta));
    partial void OnIsRomanceChanged(bool value) => OnPropertyChanged(nameof(PreviewMeta));
    partial void OnPersonalityChanged(string value) => OnPropertyChanged(nameof(PersonalityPreview));

    partial void OnSpriteRelChanged(string value)
    {
        OnPropertyChanged(nameof(HasSprite));
        OnPropertyChanged(nameof(SpriteLabel));
        UpdatePreview();
    }

    partial void OnAvatarRelChanged(string value)
    {
        OnPropertyChanged(nameof(HasAvatar));
        OnPropertyChanged(nameof(AvatarLabel));
        UpdatePreview();
    }

    partial void OnPreviewSourceChanged(ImageSource? value) => OnPropertyChanged(nameof(HasPreview));

    private void UpdatePreview()
    {
        var rel = !string.IsNullOrWhiteSpace(SpriteRel) ? SpriteRel : AvatarRel;
        ImageSource? source = null;
        if (!string.IsNullOrWhiteSpace(rel))
        {
            var full = Path.Combine(_store.Root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) source = ImageSource.FromFile(full);
        }
        PreviewSource = source;
    }
}

/// <summary>性别候选：选择状态只用来点芯片的选中档，真正的值写回 VM 的 Gender。</summary>
public sealed partial class GenderChoice : ObservableObject
{
    public GenderChoice(string name) => Name = name;
    public string Name { get; }
    [ObservableProperty] private bool _isSelected;
}

/// <summary>世界书候选：多选，标题与 id 都来自 <see cref="WorldTreeService"/>。</summary>
public sealed partial class WorldbookChoice : ObservableObject
{
    public WorldbookChoice(WorldbookEntry entry)
    {
        Id = entry.Id;
        Title = string.IsNullOrWhiteSpace(entry.Title) ? "未命名世界" : entry.Title.Trim();
        Summary = WorldTreeService.SummaryOf(entry);
    }

    public string Id { get; }
    public string Title { get; }
    public string Summary { get; }
    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

    [ObservableProperty] private bool _isSelected;
}
