using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Models;
using WarmAsBefore.Services;
using WarmAsBefore.Controls;
using System.Collections.ObjectModel;

namespace WarmAsBefore.ViewModels;

public sealed partial class SettingsViewModel
{
    // 自动更新
    [ObservableProperty] private bool _betaChannel;
    [ObservableProperty] private bool _isCheckingUpdate;
    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty] private string _latestUpdateVersion = "";

    private readonly Modules.Update.UpdateService _updater = new();

    [RelayCommand]
    private async Task CheckUpdate()
    {
        IsCheckingUpdate = true;
        _latestUpdateVersion = "";
        UpdateStatus = "正在检查更新（多镜像测速中）…";
        try
        {
            _updater.BetaChannel = BetaChannel;
            var r = await _updater.CheckAsync();
            if (r.Error is not null)
            {
                UpdateStatus = "检查失败：" + r.Error;
                return;
            }
            if (r.Latest is not null && r.HasUpdate)
            {
                _latestUpdateVersion = r.Latest.Tag;
                // 测速选最快镜像
                var speeds = await _updater.SpeedTestAsync(r.Latest.ZipUrl);
                var fastest = speeds.Count > 0 ? speeds[0] : null;
                UpdateStatus = $"发现新版本 {r.Latest.Tag}（{r.Latest.DownloadNote}）"
                    + (fastest is not null ? $" · 最快源 {fastest.Mirror.TrimEnd('/')}（{fastest.MibPerSec:0.#} MiB/s）" : "");
            }
            else
            {
                UpdateStatus = r.Latest is null ? "已是最新版本" : $"当前为最新版本 {r.Latest.Tag}";
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("Settings.CheckUpdate -> " + ex.Message);
            UpdateStatus = "检查失败：" + ex.Message;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task DownloadUpdate()
    {
        if (string.IsNullOrEmpty(_latestUpdateVersion))
        {
            UpdateStatus = "先点击「检查更新」发现新版本。";
            return;
        }
        IsCheckingUpdate = true;
        UpdateStatus = "正在下载更新包…";
        try
        {
            _updater.BetaChannel = BetaChannel;
            var r = await _updater.CheckAsync();
            if (r.Latest is null) { UpdateStatus = "未发现可下载版本。"; return; }
            var speeds = await _updater.SpeedTestAsync(r.Latest.ZipUrl);
            var url = speeds.Count > 0
                ? speeds[0].Mirror + "gongchanwangzilin/WarmAsBefore/releases/download/" + r.Latest.Tag + "/" + System.IO.Path.GetFileName(r.Latest.ZipUrl)
                : r.Latest.ZipUrl;
            var sha = await _updater.FetchSha256Async(r.Latest.Sha256);
            var dir = System.IO.Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "WarmAsBefore", "updates");
            var zip = await _updater.DownloadAsync(url, dir, sha);
            UpdateStatus = $"已下载到 {zip}（SHA256 {(string.IsNullOrEmpty(sha) ? "未校验" : "已校验")}），重启后替换。";
        }
        catch (Exception ex)
        {
            App.WriteLog("Settings.DownloadUpdate -> " + ex.Message);
            UpdateStatus = "下载失败：" + ex.Message;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }


    // 小游戏 AI 难度
    [ObservableProperty] private string _gameDifficulty = "normal";
    [ObservableProperty] private string _gameDifficultyDisplay = "普通（默认）";
    [ObservableProperty] private bool _aiAutoDifficulty;
    public List<string> GameDifficultyChoices { get; } = new() { "简单（新手练手）", "普通（默认）", "困难（步步紧逼）" };

    // 云端棋力脑
    [ObservableProperty] private bool _chessApiEnabled;
    [ObservableProperty] private string _chessApiUrl = "";
    [ObservableProperty] private string _chessApiKey = "";
    [ObservableProperty] private string _chessApiModel = "gpt-4o-mini";
    private static string GameDifficultyDisplayOf(string v) => v switch
    {
        "easy" => "简单（新手练手）",
        "hard" => "困难（步步紧逼）",
        _ => "普通（默认）"
    };
    partial void OnGameDifficultyDisplayChanged(string value)
    {
        _gameDifficulty = value switch
        {
            "简单（新手练手）" => "easy",
            "困难（步步紧逼）" => "hard",
            _ => "normal"
        };
        // 改字段后必须落盘：否则设置页显示「困难」，服务读到的仍是 normal，UI 与设置不一致
        PersistSettings();
    }

    [ObservableProperty] private bool _alwaysOnTop;
    [ObservableProperty] private int _petIdleMinutes;


    // ============ #E3 桌宠思考 ============

    /// <summary>桌宠思考模式（显示名：关闭 / 随机 / 定时）。</summary>
    [ObservableProperty] private string _petThinkModeDisplay = "关闭";
    public List<string> PetThinkModeChoices { get; } = new() { "关闭", "随机", "定时" };
    /// <summary>思考间隔（分钟）。</summary>
    [ObservableProperty] private int _petThinkIntervalMinutes = 10;
    public List<int> PetThinkIntervalChoices { get; } = new() { 5, 10, 15, 30, 60 };

    /// <summary>多显示器：允许头顶对话小窗跟着角色跨到另一块屏。</summary>
    [ObservableProperty] private bool _petChromeCrossScreen;

    /// <summary>
    /// 桌宠体型倍率（0.4–2.6）。滚轮与捏合写的就是这一个值，
    /// 所以这一行既是调大小的入口，也是「调完想回到原样」的出口。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PetScaleText))]
    private double _petScale = 1.0;

    /// <summary>滑杆旁边的读数，只用于显示，不参与存储。</summary>
    public string PetScaleText => ((int)Math.Round(Math.Clamp(PetScale, 0.4, 2.6) * 100)) + "%";

    private static string ThinkModeDisplayOf(string key) => key switch
    {
        "random" => "随机",
        "timer" => "定时",
        _ => "关闭"
    };

    private static string ThinkModeKeyOf(string display) => display switch
    {
        "随机" => "random",
        "定时" => "timer",
        _ => "off"
    };

    partial void OnPetThinkModeDisplayChanged(string value) => PersistSettings();
    partial void OnPetThinkIntervalMinutesChanged(int value) => PersistSettings();

    partial void OnPetScaleChanged(double value)
    {
        PersistSettings();
        // 角色正站在桌面上时，滑杆要当场看见她变大变小；没进桌宠就是空实现。
        Services.PetService.ApplyScale();
    }


    [RelayCommand]
    private Task DeleteSelectedCustomGender() => DeleteCustomGender(SelectedCustomGender);

    /// <summary>插件中心（列表 / 启停 / 依赖 / 设置 / 崩溃日志 / 工坊）。</summary>
    [RelayCommand]
    private async Task OpenPluginCenter() => await Shell.Current.GoToAsync("pluginCenter");

    /// <summary>心理状况：按角色设定轻/中/重度，AI 只读取当前角色生效的那几条。</summary>
    [RelayCommand]
    private async Task OpenMentalHealth() => await Shell.Current.GoToAsync("mentalHealth");

    /// <summary>用量统计：缓存输入/输出单列，配模型单价算出每一笔。</summary>
    [RelayCommand]
    private async Task OpenUsageStats() => await Shell.Current.GoToAsync("usagestats");

    /// <summary>桌宠设置：带实时预览的那一页。托盘菜单里也有同一入口，这里让没进桌宠模式的人也能改。</summary>
    [RelayCommand]
    private async Task OpenPetSettings() => await Shell.Current.GoToAsync("petsettings");

    /// <summary>AI 制作桌宠：把角色 Q 版化、逐状态出帧。</summary>
    [RelayCommand]
    private async Task OpenPetStudio() => await Shell.Current.GoToAsync("petstudio");

    // ============ 自定义性别：新建 / 导入 / 删除 ============

    [RelayCommand]
    private async Task SaveCustomGender()
    {
        if (_genders is null) return;
        var name = (GenderNewName ?? "").Trim();
        if (name.Length == 0)
        {
            Toast.Warn("先给它起个名字。");
            return;
        }
        var def = new Services.GenderDefinition
        {
            Name = name,
            Description = (GenderNewDescription ?? "").Trim(),
            Pronoun = string.IsNullOrWhiteSpace(GenderNewPronoun) ? "TA" : GenderNewPronoun.Trim(),
            Script = string.IsNullOrWhiteSpace(GenderNewScript)
                ? null
                : new Services.GenderScript { Language = GenderNewScriptLanguage, Code = GenderNewScript }
        };
        var (ok, message) = await _genders.SaveAsync(def);
        await LoadGenderChoicesAsync();
        if (ok)
        {
            GenderNewName = "";
            GenderNewDescription = "";
            GenderNewScript = "";
        }
        if (ok) Toast.Success(message);
        else Toast.Error(message);
    }

    [RelayCommand]
    private async Task ImportGenderJson()
    {
        if (_genders is null) return;
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "选择性别定义 JSON" });
            if (pick is null) return;
            var (ok, message) = await _genders.ImportJsonAsync(pick.FullPath);
            await LoadGenderChoicesAsync();
            if (ok) Toast.Success(message);
            else Toast.Error(message);
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.ImportGender -> " + ex);
        }
    }

    [RelayCommand]
    private async Task DeleteCustomGender(string? name)
    {
        if (_genders is null || string.IsNullOrWhiteSpace(name)) return;
        if (!await Shell.Current.DisplayAlert("删除性别", $"确定删除自定义性别「{name}」？", "删除", "取消")) return;
        await _genders.DeleteAsync(name);
        if (string.Equals(UserGender, name, StringComparison.OrdinalIgnoreCase)) UserGender = "";
        await LoadGenderChoicesAsync();
    }


    /// <summary>把多行文本按行拆成路径列表（去空行与首尾空白）。设置页「每行一个路径」用。</summary>
    private static List<string> SplitLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return text.Split('\n', '\r')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    /// <summary>解析 0-1 概率文本；非法或越界则回退默认值。</summary>
    private static double ParseProb(string? text, double fallback)
    {
        if (double.TryParse((text ?? "").Trim(), out var v)) return Math.Clamp(v, 0, 1);
        return fallback;
    }

    /// <summary>解析帧间隔毫秒文本；非法则回退 120，并夹在 30..5000。</summary>
    private static int ParseFrameMs(string? text)
    {
        if (int.TryParse((text ?? "").Trim(), out var v)) return Math.Clamp(v, 30, 5000);
        return 120;
    }


    // ============ 数据包导出 / 导入 ============
    [ObservableProperty] private string _exportStatus = "";
    [ObservableProperty] private double _exportProgress;
    [ObservableProperty] private string _dataImportStatus = "";
    [ObservableProperty] private double _dataImportProgress;
    [ObservableProperty] private bool _isDataBusy;
    /// <summary>导出时是否把 API 密钥 / 机器人密钥一并写入数据包（默认否）。</summary>
    [ObservableProperty] private bool _includeSecrets;

    /// <summary>导出全部应用数据为一个 zip（默认清空 settings.json 里的密钥）。</summary>
    [RelayCommand]
    private async Task ExportData()
    {
        if (IsDataBusy) return;
        IsDataBusy = true;
        string? tmp = null;
        try
        {
            // 设置是 300ms 防抖落盘的：先把当前值刷到磁盘，否则导出的是改动前的旧设置
            await PersistNowAsync();

            var fileName = $"温暖如初_数据包_{DateTime.Now:yyyyMMdd_HHmm}.zip";
            // 先打到临时文件，再交给 FileSaver 让用户选位置（Android 上也只有 FileSaver 能落盘）
            tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "wab_export_" + Guid.NewGuid().ToString("N") + ".zip");

            ExportStatus = "正在打包…";
            ExportProgress = 0;
            var progress = new Progress<(int Done, int Total, string Path)>(t =>
            {
                ExportProgress = t.Total == 0 ? 0 : (double)t.Done / t.Total;
                ExportStatus = $"正在打包 {t.Done}/{t.Total} 个文件…";
            });

            await DataPackageService.ExportAsync(tmp, IncludeSecrets, progress);
            var size = new FileInfo(tmp).Length;

            ExportStatus = "请选择保存位置…";
            using var fs = File.Open(tmp, FileMode.Open, FileAccess.Read, FileShare.Read);
            var result = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync(fileName, fs);

            ExportStatus = result.IsSuccessful
                ? $"导出成功（{DataPackageService.FormatSize(size)}）"
                : "已取消保存";
            if (result.IsSuccessful)
            {
                // 同步一份到下载文件夹（安卓自动备份；桌面 no-op）
                Services.DownloadBackupService.AfterExport(tmp);
                Toast.Success($"数据包已保存（{DataPackageService.FormatSize(size)}）。"
                    + (IncludeSecrets
                        ? "包含 API 密钥与机器人密钥，请妥善保管。"
                        : "已排除 API 密钥与机器人密钥。"));
            }
        }
        catch (Exception ex)
        {
            ExportStatus = "导出失败：" + ex.Message;
            App.WriteLog("Settings.ExportData -> " + ex);
        }
        finally
        {
            IsDataBusy = false;
            ExportProgress = 0;
            try { if (tmp is not null && File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>导入数据包：解压到临时目录校验 → 用户确认 → 替换数据目录 → 提示重启。</summary>
    [RelayCommand]
    private async Task ImportData()
    {
        if (IsDataBusy) return;
        IsDataBusy = true;
        string? tempDir = null;
        string? picked = null;
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择数据包（.zip）",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".zip" } },
                    { DevicePlatform.Android, new[] { "application/zip" } },
                    { DevicePlatform.iOS, new[] { "public.zip-archive" } }
                })
            });
            if (pick is null) return;

            DataImportStatus = "正在读取文件…";
            DataImportProgress = 0;

            // Android 上 FullPath 是 content:// URI，不能直接喂给 ZipFile；先落到本地临时文件
            picked = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "wab_pick_" + Guid.NewGuid().ToString("N") + ".zip");
            await using (var src = await pick.OpenReadAsync())
            await using (var dst = File.Create(picked))
                await src.CopyToAsync(dst);

            if (!DataPackageService.IsValidPackage(picked))
            {
                // 不是整包数据包：交给自动识别路由，角色包/插件包/素材包/存档包拿错入口也能进（自动识别入口）
                var smart = await _smart.ImportAsync(picked);
                DataImportStatus = smart.Ok
                    ? $"【{smart.KindName}】{smart.Message}"
                    : "导入失败：" + smart.Message;
                if (smart.Ok) Toast.Success($"【{smart.KindName}】{smart.Message}");
                else Toast.Error(smart.Message);
                return;
            }

            DataImportStatus = "正在解压校验…";
            DataImportProgress = 0;
            var progress = new Progress<(int Done, int Total, string Path)>(t =>
            {
                DataImportProgress = t.Total == 0 ? 0 : (double)t.Done / t.Total;
                DataImportStatus = $"正在解压校验 {t.Done}/{t.Total} 个文件…";
            });
            tempDir = await DataPackageService.UnpackForImportAsync(picked, progress);

            var items = await Task.Run(() => DataPackageService.ListImportTargets(tempDir));
            var summary = items.Count == 0
                ? "（空包）"
                : string.Join("、", items.Take(8).Select(i =>
                    i.Size < 0 ? i.Name : $"{i.Name}（{DataPackageService.FormatSize(i.Size)}）"));
            var ok = await Shell.Current.DisplayActionSheet(
                $"将用数据包内容整体替换当前全部数据（共 {items.Count} 项：{summary}），完成后需要重启应用。",
                "取消", null, "确认导入") == "确认导入";
            if (!ok) { DataImportStatus = "已取消导入"; return; }

            DataImportStatus = "正在写入数据目录…";
            var (success, message) = await Task.Run(() => DataPackageService.ReplaceDataDir(tempDir!));
            DataImportStatus = success ? "导入完成，请重启应用生效。" : "导入失败：" + message;
            if (success) Toast.Success("数据已导入。请完全关闭并重新打开应用，改动才会生效。");
            else Toast.Error(message);
            if (success) App.WriteLog("Settings.ImportData: success");
        }
        catch (Exception ex)
        {
            DataImportStatus = "导入失败：" + ex.Message;
            App.WriteLog("Settings.ImportData -> " + ex);
        }
        finally
        {
            IsDataBusy = false;
            DataImportProgress = 0;
            if (tempDir is not null) DataPackageService.CleanupImport(tempDir);
            try { if (picked is not null && File.Exists(picked)) File.Delete(picked); } catch { }
        }
    }

}
