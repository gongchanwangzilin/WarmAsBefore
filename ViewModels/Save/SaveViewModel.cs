using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.Drawables;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Modules.Automation;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

public sealed partial class SaveViewModel : ObservableObject
{
    private readonly SaveManager _save;
    private readonly SettingsManager _settings;
    private readonly Modules.NovelImport.NovelAnalyzer _novel;
    private readonly Services.CharacterLibrary _library;
    [ObservableProperty] private List<SaveSlotItem> _slots = new();
    [ObservableProperty] private bool _novelTesting;

    public SaveViewModel(SaveManager save, SettingsManager settings, Modules.NovelImport.NovelAnalyzer novel,
        Services.CharacterLibrary library)
    {
        _save = save;
        _settings = settings;
        _novel = novel;
        _library = library;
        NovelTesting = settings.Current.NovelTestingEnabled;
    }

    [RelayCommand]
    private async Task Refresh()
    {
        var list = await _save.List();
        var roster = await _library.ListAsync();
        Slots = list.Select(s => new SaveSlotItem
        {
            Slot = s,
            CharacterName = roster.FirstOrDefault(c => c.Profile.Id == s.Character)?.Profile.Name ?? "未知角色"
        }).ToList();
        NovelTesting = _settings.Current.NovelTestingEnabled;
    }

    [RelayCommand]
    private async Task LoadSlot(string id)
    {
        await _library.ListAsync();   // 确保角色库已载入，读档时才能恢复角色状态
        await _save.Load(id);
        // 过期存档被自动修复过就告知用户（不是静默改动数据）
        if (!string.IsNullOrWhiteSpace(_save.LastLoadNote))
        {
            var note = _save.LastLoadNote;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try { Toast.Warn(note); }
                catch (Exception ex) { App.WriteLog("SaveVM.RepairNote -> " + ex.Message); }
            });
        }
        await Shell.Current.GoToAsync("main");
    }

    [RelayCommand]
    private async Task ExportSlot(string id)
    {
        try
        {
            var bundle = _settings.Current.SaveBundleWithPlugins;
            var data = await _save.ExportStream(id, bundle);
            if (data is null) { Toast.Error("存档不存在"); return; }
            using var ms = new MemoryStream(data.Value.bytes);
            var result = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync(data.Value.name, ms);
            if (result.IsSuccessful)
                Toast.Success(bundle ? "已导出存档包（含所用插件代码，不含插件设置里的密钥）。" : "已导出存档。");
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveViewModel.ExportSlot -> " + ex);
        }
    }

    /// <summary>存档高级设置：插件/工具/角色白名单，不进入游戏也能改。</summary>
    [RelayCommand]
    private async Task OpenRules(string id)
        => await Shell.Current.GoToAsync($"saveRules?id={Uri.EscapeDataString(id)}");

    [RelayCommand]
    private async Task DeleteSlot(string id)
    {
        var slot = Slots.FirstOrDefault(s => s.Id == id);
        var ok = await Shell.Current.DisplayAlert("删除存档",
            $"确定删除「{slot?.Label ?? id}」吗？删除后不可恢复", "删除", "取消");
        if (!ok) return;
        await _save.Delete(id);
        await Refresh();
    }

    [RelayCommand]
    private async Task RenameSlot(string id)
    {
        var slot = Slots.FirstOrDefault(s => s.Id == id);
        var input = await Shell.Current.DisplayPromptAsync("重命名存档",
            "新的存档名称？", "保存", "取消", slot?.Label ?? "");
        if (string.IsNullOrWhiteSpace(input)) return;
        var ok = await _save.Rename(id, input.Trim());
        if (!ok) Toast.Error("重命名失败");
        await Refresh();
    }

    [RelayCommand]
    private async Task ImportSave()
    {
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择存档文件（.json / .wbsave）",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".json", ".wbsave", ".zip" } },
                    { DevicePlatform.Android, new[] { "application/json", "application/zip", "application/octet-stream", "text/plain" } },
                    { DevicePlatform.iOS, new[] { "public.json", "public.zip-archive", "public.data" } }
                })
            });
            if (pick is null) return;
            var (imported, message) = await _save.Import(pick.FullPath);
            if (imported) Toast.Success(message);
            else Toast.Error(message);
            await Refresh();
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveViewModel.ImportSave -> " + ex);
        }
    }

    [RelayCommand]
    private async Task BackupAll()
    {
        try
        {
            var folder = await CommunityToolkit.Maui.Storage.FolderPicker.Default.PickAsync();
            if (folder?.Folder is null) return;
            var n = await _save.Backup(folder.Folder.Path);
            if (n > 0) Toast.Success($"已备份 {n} 个存档到 {folder.Folder.Path}");
            else Toast.Info("没有可备份的存档");
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveViewModel.BackupAll -> " + ex);
        }
    }

    /// <summary>小说功能（测试）：仅当设置在实验性里开启后显示入口。</summary>
    [RelayCommand]
    private async Task ImportNovel()
    {
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择小说文本",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".txt", ".md" } },
                    { DevicePlatform.Android, new[] { "text/plain" } },
                    { DevicePlatform.iOS, new[] { "public.plain-text" } }
                })
            });
            if (pick is null) return;
            var result = await _novel.Analyze(pick.FullPath);
            if (result is null)
            {
                Toast.Error("解析失败：请检查 AI 配置或文本内容");
                return;
            }
            var chars = result.Characters.Count > 0 ? "角色：" + string.Join("、", result.Characters) : "角色：无";
            await Shell.Current.DisplayAlert($"小说导入（测试）· {result.Title}",
                $"{chars}\n\n摘要：{result.Summary}", "好");
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveViewModel.ImportNovel -> " + ex);
        }
    }

    [RelayCommand]
    private async Task GoBack() => await Shell.Current.GoToAsync("..");
}

/// <summary>存档列表展示项：补充角色名（存档里只存角色 id）。</summary>
public sealed class SaveSlotItem
{
    public SaveSlot Slot { get; init; } = new();
    public string CharacterName { get; init; } = "";
    public string Id => Slot.Id;
    public string Label => Slot.Label;
    public string Scene => Slot.Scene;
    public DateTime SavedAt => Slot.SavedAt;
}
