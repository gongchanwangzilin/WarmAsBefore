using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.Services.CloudSync;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 云存档浏览器：只做「看」和「取回」，合并仍归 CloudSyncService。
///
/// 页面分两段：当前设备（本机在云端的那一份）、其他设备 / 历史。
/// 四种失败状态（没配、没登录、断网、空）都在这里折成一句能读的话，并写清下一步去哪改。
/// </summary>
public sealed partial class CloudSavesViewModel : ObservableObject
{
    private readonly CloudSyncService _cloud;

    public CloudSavesViewModel(CloudSyncService cloud)
    {
        _cloud = cloud;
    }

    public ObservableCollection<CloudSaveRow> CurrentRows { get; } = new();
    public ObservableCollection<CloudSaveRow> OtherRows { get; } = new();
    public ObservableCollection<CloudConflictRow> ConflictRows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RefreshText))]
    private bool _isLoading;

    [ObservableProperty] private string _syncLine = "上次同步：还没有同步记录";
    [ObservableProperty] private string _deviceLine = "";

    [ObservableProperty] private bool _hasCurrent;
    [ObservableProperty] private bool _hasOther;
    [ObservableProperty] private bool _hasConflicts;
    [ObservableProperty] private bool _hasHistorySection;
    [ObservableProperty] private bool _showCurrentSection;
    [ObservableProperty] private bool _showCurrentEmpty;

    [ObservableProperty] private bool _hasHint;
    [ObservableProperty] private string _hintTitle = "";
    [ObservableProperty] private string _hintText = "";

    [ObservableProperty] private bool _showEmpty;
    [ObservableProperty] private string _emptyTitle = "";
    [ObservableProperty] private string _emptyText = "";

    public string RefreshText => IsLoading ? "刷新中…" : "刷新";
    public string CurrentCountText => CurrentRows.Count > 0 ? $"{CurrentRows.Count} 份" : "";
    public string OtherCountText => OtherRows.Count > 0 ? $"{OtherRows.Count} 份" : "";

    [RelayCommand]
    private async Task Refresh()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            var snapshot = await _cloud.ListRemoteSavesAsync();
            var conflicts = await _cloud.ListConflictsAsync();

            CurrentRows.Clear();
            OtherRows.Clear();
            ConflictRows.Clear();
            foreach (var entry in snapshot.Saves)
                (entry.IsCurrentDevice ? CurrentRows : OtherRows).Add(new CloudSaveRow(entry));
            foreach (var item in conflicts.Items)
                ConflictRows.Add(new CloudConflictRow(item));

            HasCurrent = CurrentRows.Count > 0;
            HasOther = OtherRows.Count > 0;
            HasConflicts = ConflictRows.Count > 0;
            HasHistorySection = HasOther || HasConflicts;

            DeviceLine = string.IsNullOrWhiteSpace(snapshot.DeviceName) ? "" : $"本机 · {snapshot.DeviceName}";
            SyncLine = snapshot.LastSyncUtc is { } at
                ? $"上次同步：{FormatTime(at)}"
                : "上次同步：还没有同步记录";

            ApplyState(snapshot, conflicts);
            OnPropertyChanged(nameof(CurrentCountText));
            OnPropertyChanged(nameof(OtherCountText));
        }
        catch (Exception ex)
        {
            App.WriteLog("CloudSavesVM.Refresh -> " + ex);
            HasHint = true;
            HintTitle = "这次没读到云端";
            HintText = "刷新失败：" + ex.Message + "。检查网络后点右上角「刷新」。";
            ShowEmpty = false;
            ShowCurrentSection = false;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyState(CloudSaveSnapshot snapshot, CloudConflictList conflicts)
    {
        HasHint = false;
        ShowEmpty = false;
        ShowCurrentEmpty = false;

        switch (snapshot.State)
        {
            case CloudReadState.Ok:
                if (HasCurrent || HasOther || HasConflicts)
                {
                    ShowCurrentEmpty = !HasCurrent;
                }
                else
                {
                    ShowEmpty = true;
                    EmptyTitle = "云端还没有存档";
                    EmptyText = "在这台设备上存一次档，等云同步上传后再回到这里，就能看到它。";
                }
                // 云端列表读到了、本机历史副本读失败：单独说一声，别让用户以为副本被清空了
                if (!conflicts.Ok)
                {
                    HasHint = true;
                    HintTitle = "历史副本没读出来";
                    HintText = conflicts.Message + " 云端存档列表不受影响。";
                }
                break;

            case CloudReadState.NotConfigured:
                HasHint = true;
                HintTitle = "云同步还没配好";
                HintText = snapshot.Message;
                break;

            case CloudReadState.NotSignedIn:
                HasHint = true;
                HintTitle = "登录已失效";
                HintText = snapshot.Message;
                break;

            case CloudReadState.Offline:
                HasHint = true;
                HintTitle = "连不上云端";
                HintText = snapshot.Message;
                break;

            default:
                HasHint = true;
                HintTitle = "这次没读到云端";
                HintText = snapshot.Message;
                break;
        }

        ShowCurrentSection = HasCurrent || ShowCurrentEmpty;
    }

    [RelayCommand]
    private async Task Download(CloudSaveRow? row)
    {
        if (row is null || row.Busy || !row.CanAct) return;

        if (row.ExistsLocally)
        {
            try
            {
                var ok = await Shell.Current.DisplayAlert("覆盖本机存档",
                    $"本机已有同一位置的「{row.SaveName}」。下载会用云端这份覆盖它，当前版本会先留一份到历史副本。",
                    "下载并覆盖", "取消");
                if (!ok) return;
            }
            catch (Exception ex) { App.WriteLog("CloudSavesVM.Confirm -> " + ex); }
        }

        row.Busy = true;
        try
        {
            var result = await _cloud.DownloadSaveAsync(row.Source);
            if (result.Ok)
            {
                row.MarkDownloaded();
                Toast.Success(result.Message);
            }
            else
            {
                Toast.Warn(result.Message);
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("CloudSavesVM.Download -> " + ex);
            Toast.Error("下载失败：" + ex.Message);
        }
        finally
        {
            row.Busy = false;
        }
    }

    [RelayCommand]
    private async Task Restore(CloudConflictRow? row)
    {
        if (row is null || row.Busy || !row.CanAct) return;

        if (row.Source.TargetExists)
        {
            try
            {
                var ok = await Shell.Current.DisplayAlert("恢复历史副本",
                    $"会用这份旧版本覆盖本机当前的「{row.Title}」，当前版本会先留一份到历史副本。",
                    "恢复", "取消");
                if (!ok) return;
            }
            catch (Exception ex) { App.WriteLog("CloudSavesVM.Confirm -> " + ex); }
        }

        row.Busy = true;
        try
        {
            var result = await _cloud.RestoreConflictAsync(row.Source);
            if (result.Ok) Toast.Success(result.Message);
            else Toast.Warn(result.Message);
        }
        catch (Exception ex)
        {
            App.WriteLog("CloudSavesVM.Restore -> " + ex);
            Toast.Error("恢复失败：" + ex.Message);
        }
        finally
        {
            row.Busy = false;
        }
    }

    [RelayCommand]
    private async Task GoBack()
    {
        try { await Shell.Current.GoToAsync(".."); }
        catch (Exception ex) { App.WriteLog("CloudSavesVM.GoBack -> " + ex); }
    }

    internal static string FormatTime(DateTime utc)
    {
        var local = utc.Kind == DateTimeKind.Utc
            ? utc.ToLocalTime()
            : DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        return local.Year == DateTime.Now.Year
            ? local.ToString("MM-dd HH:mm")
            : local.ToString("yyyy-MM-dd HH:mm");
    }

    internal static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "大小未记录";
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        if (bytes < 1024 * 1024)
            return (bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
        return (bytes / (1024d * 1024d)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
    }
}

/// <summary>云端一份存档的展示行。Busy 只在「这一次点击」里亮，按钮禁用并换文案。</summary>
public sealed partial class CloudSaveRow : ObservableObject
{
    public CloudSaveRow(CloudSaveEntry source)
    {
        Source = source;
        SaveName = source.SaveName.Length > 0 ? source.SaveName : "未命名存档";
        ExistsLocally = source.ExistsLocally;

        var device = source.DeviceName.Length > 0 ? source.DeviceName : "未知设备";
        if (source.IsCurrentDevice) device += "（本机）";
        var parts = new List<string> { device, CloudSavesViewModel.FormatTime(source.SavedAtUtc),
            CloudSavesViewModel.FormatSize(source.SizeBytes) };
        if (source.HasLocalSameName) parts.Add("本机有同名");
        MetaLine = string.Join(" · ", parts);
    }

    public CloudSaveEntry Source { get; }
    public string SaveName { get; }
    public string MetaLine { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionText))]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    private bool _busy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionText))]
    private bool _existsLocally;

    public string ActionText => Busy ? "下载中…" : ExistsLocally ? "覆盖" : "下载";
    public bool CanAct => !Busy;

    public void MarkDownloaded() => ExistsLocally = true;
}

/// <summary>一份历史副本的展示行：标题是它原本的存档名，说明里写清会不会覆盖当前版本。</summary>
public sealed partial class CloudConflictRow : ObservableObject
{
    public CloudConflictRow(CloudConflictEntry source)
    {
        Source = source;
        Title = source.TargetName.Length > 0 ? source.TargetName : "未知存档";

        var parts = new List<string> { "历史副本" };
        if (source.DeviceName.Length > 0) parts.Add(source.DeviceName);
        parts.Add(CloudSavesViewModel.FormatTime(source.SavedAtUtc));
        parts.Add(CloudSavesViewModel.FormatSize(source.SizeBytes));
        if (source.TargetExists) parts.Add("会覆盖本机当前版本");
        MetaLine = string.Join(" · ", parts);
    }

    public CloudConflictEntry Source { get; }
    public string Title { get; }
    public string MetaLine { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionText))]
    [NotifyPropertyChangedFor(nameof(CanAct))]
    private bool _busy;

    public string ActionText => Busy ? "恢复中…" : "恢复";
    public bool CanAct => Source.CanRestore && !Busy;
}
