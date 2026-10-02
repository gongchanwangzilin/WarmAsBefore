using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 素材库页：背景库 + 音乐库（方框形矩阵网格）。
/// - 背景库：导入（JPG/PNG/MP4，MP4 自动首帧缩略图）、重命名、删除。
/// - 音乐库：导入、试听、重命名、删除、看时长/格式，
///   并把音乐分配为左键音 / 右键音 / 按键音 / 背景音轮播列表。
/// </summary>
public sealed partial class MaterialsViewModel : ObservableObject
{
    private readonly MaterialLibrary _materials;
    private readonly AudioController _audio;

    [ObservableProperty] private bool _isMusicTab;

    [ObservableProperty] private ObservableCollection<BackgroundGridItem> _backgrounds = new();
    [ObservableProperty] private ObservableCollection<MusicGridItem> _musics = new();

    public MaterialsViewModel(MaterialLibrary materials, AudioController audio)
    {
        _materials = materials;
        _audio = audio;
    }

    [RelayCommand]
    private Task ShowBackgrounds()
    {
        IsMusicTab = false;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ShowMusic()
    {
        IsMusicTab = true;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await _materials.EnsureLoadedAsync();
        var bg = new List<BackgroundGridItem>();
        foreach (var item in _materials.BackgroundItems)
        {
            ImageSource? thumb = null;
            try
            {
                var abs = await _materials.ThumbnailAbsAsync(item);
                if (!string.IsNullOrEmpty(abs) && File.Exists(abs))
                    thumb = ImageSource.FromFile(abs);
            }
            catch (Exception ex) { App.WriteLog("Materials.Thumb -> " + ex.Message); }
            bg.Add(new BackgroundGridItem(item, thumb));
        }
        Backgrounds = new ObservableCollection<BackgroundGridItem>(bg);
        Musics = new ObservableCollection<MusicGridItem>(
            _materials.MusicItems.Select(m => new MusicGridItem(m, _materials.DurationLabel(m.DurationMs))));
    }

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");

    /// <summary>从电脑导入背景（JPG/PNG/MP4 等）。</summary>
    [RelayCommand]
    private async Task ImportBackground()
    {
        var pick = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "导入背景素材",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif", ".mp4", ".mov", ".m4v", ".webm" } },
                { DevicePlatform.Android, new[] { "image/*", "video/*" } },
                { DevicePlatform.iOS, new[] { "public.image", "public.movie" } }
            })
        });
        if (pick is null) return;
        var (ok, msg, _) = await _materials.ImportBackgroundAsync(pick.FullPath);
        await Shell.Current.DisplayAlert(ok ? "导入成功" : "导入失败", msg, "好");
        if (ok) await Refresh();
    }

    /// <summary>从电脑导入音乐。</summary>
    [RelayCommand]
    private async Task ImportMusic()
    {
        var pick = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "导入音乐",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, new[] { ".mp3", ".wav", ".ogg", ".m4a", ".aac", ".flac", ".wma" } },
                { DevicePlatform.Android, new[] { "audio/*" } },
                { DevicePlatform.iOS, new[] { "public.audio" } }
            })
        });
        if (pick is null) return;
        var (ok, msg, _) = await _materials.ImportMusicAsync(pick.FullPath);
        await Shell.Current.DisplayAlert(ok ? "导入成功" : "导入失败", msg, "好");
        if (ok) await Refresh();
    }

    [RelayCommand]
    private async Task TapBackground(BackgroundGridItem item)
    {
        if (item is null) return;
        var act = await Shell.Current.DisplayActionSheet(
            $"「{item.Name}」", "取消", null, "重命名", "删除");
        switch (act)
        {
            case "重命名":
                var name = await Shell.Current.DisplayPromptAsync("重命名", "新的背景名：", initialValue: item.Name, maxLength: 40);
                if (string.IsNullOrWhiteSpace(name)) return;
                if (await _materials.RenameBackgroundAsync(item.Data.Id, name.Trim()))
                    await Refresh();
                break;
            case "删除":
                var okDel = await Shell.Current.DisplayAlert("删除背景", $"确定删除「{item.Name}」吗？", "删除", "取消");
                if (!okDel) return;
                if (await _materials.DeleteBackgroundAsync(item.Data.Id))
                    await Refresh();
                break;
        }
    }

    [RelayCommand]
    private async Task TapMusic(MusicGridItem item)
    {
        if (item is null) return;
        var a = _materials.Assign;
        string left = a.LeftClickId == item.Data.Id ? "取消设为左键音" : "设为左键音";
        string right = a.RightClickId == item.Data.Id ? "取消设为右键音" : "设为右键音";
        string key = a.KeyPressId == item.Data.Id ? "取消设为按键音" : "设为按键音";
        string bgm = a.BgmList.Contains(item.Data.Id) ? "从背景音列表移除" : "加入背景音列表";
        var act = await Shell.Current.DisplayActionSheet(
            $"「{item.Name}」", "取消", null, "试听", left, right, key, bgm, "重命名", "删除");
        switch (act)
        {
            case "试听":
                var abs = _materials.ResolveAbs(item.Data.RelPath);
                if (File.Exists(abs)) _audio.PlaySfxFile(abs);
                break;
            case "设为左键音": a.LeftClickId = item.Data.Id; await AfterAssign(); break;
            case "取消设为左键音": a.LeftClickId = ""; await AfterAssign(); break;
            case "设为右键音": a.RightClickId = item.Data.Id; await AfterAssign(); break;
            case "取消设为右键音": a.RightClickId = ""; await AfterAssign(); break;
            case "设为按键音": a.KeyPressId = item.Data.Id; await AfterAssign(); break;
            case "取消设为按键音": a.KeyPressId = ""; await AfterAssign(); break;
            case "加入背景音列表":
                if (!a.BgmList.Contains(item.Data.Id)) { a.BgmList.Add(item.Data.Id); await AfterAssign(true); }
                break;
            case "从背景音列表移除":
                a.BgmList.Remove(item.Data.Id); await AfterAssign(true);
                break;
            case "重命名":
                var name = await Shell.Current.DisplayPromptAsync("重命名", "新的音乐名：", initialValue: item.Name, maxLength: 40);
                if (string.IsNullOrWhiteSpace(name)) return;
                if (await _materials.RenameMusicAsync(item.Data.Id, name.Trim()))
                    await Refresh();
                break;
            case "删除":
                var okDel = await Shell.Current.DisplayAlert("删除音乐", $"确定删除「{item.Name}」吗？", "删除", "取消");
                if (!okDel) return;
                if (await _materials.DeleteMusicAsync(item.Data.Id))
                    await Refresh();
                break;
        }
    }

    private async Task AfterAssign(bool refreshBgm = false)
    {
        await _materials.PersistAssignAsync();
        if (refreshBgm) _ = _audio.StartBgmRotationAsync();
        await Refresh();
    }
}

/// <summary>背景库矩阵项（含缩略图）。</summary>
public sealed class BackgroundGridItem
{
    public BackgroundGridItem(BackgroundItem data, ImageSource? thumb)
    {
        Data = data;
        Thumb = thumb;
    }

    public BackgroundItem Data { get; init; }
    public string Id => Data.Id;
    public string Name => Data.Name;
    public string Meta => Data.Format;
    public ImageSource? Thumb { get; init; }
    public bool HasThumb => Thumb is not null;
}

/// <summary>音乐库矩阵项（含时长/格式）。</summary>
public sealed class MusicGridItem
{
    public MusicGridItem(MusicItem data, string durationLabel)
    {
        Data = data;
        DurationLabel = durationLabel;
    }

    public MusicItem Data { get; init; }
    public string Id => Data.Id;
    public string Name => Data.Name;
    public string DurationLabel { get; init; }
    public string Meta => $"{Data.Format} · {DurationLabel}";
}