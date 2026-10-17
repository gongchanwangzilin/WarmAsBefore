using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 副设定对比页：左边是主设定（内核，永不改），右边是副设定（当下之我）。
/// 下面是漂移留影，可以看清「TA 是怎么一步步变成现在这样的」，也可以一键回正。
/// </summary>
public sealed partial class SubProfileViewModel : ObservableObject, IQueryAttributable
{
    private readonly CharacterSubProfileService _sub;
    private string _charId = "";

    public SubProfileViewModel(CharacterSubProfileService sub) => _sub = sub;

    [ObservableProperty] private string _title = "主设定与副设定";
    [ObservableProperty] private string _mainPersonality = "";
    [ObservableProperty] private string _mainDescription = "";
    [ObservableProperty] private string _subPersonality = "";
    [ObservableProperty] private string _subDescription = "";
    [ObservableProperty] private string _tone = "";
    [ObservableProperty] private string _notesText = "";
    [ObservableProperty] private string _driftSummary = "";
    [ObservableProperty] private bool _hasNotes;
    [ObservableProperty] private bool _hasHistory;

    /// <summary>凡是被经历改过的字段都标出来，方便一眼看出哪儿变了。</summary>
    [ObservableProperty] private string _personalityDiffNote = "";
    [ObservableProperty] private string _descriptionDiffNote = "";

    [ObservableProperty] private ObservableCollection<DriftItem> _history = new();

    public sealed class DriftItem
    {
        public string Time { get; init; } = "";
        public string Reason { get; init; } = "";
        public string Tone { get; init; } = "";
        public string Notes { get; init; } = "";
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("charId", out var v) && v is string id && !string.IsNullOrWhiteSpace(id))
            _charId = Uri.UnescapeDataString(id);
        Load();
    }

    private void Load()
    {
        var c = _sub.Compare(_charId);
        if (c is null)
        {
            Title = "角色不存在";
            return;
        }
        Title = $"{c.MainName}：主设定 × 副设定";
        MainPersonality = string.IsNullOrWhiteSpace(c.MainPersonality) ? "（空）" : c.MainPersonality;
        MainDescription = string.IsNullOrWhiteSpace(c.MainDescription) ? "（空）" : c.MainDescription;
        SubPersonality = string.IsNullOrWhiteSpace(c.SubPersonality) ? "（未建立）" : c.SubPersonality;
        SubDescription = string.IsNullOrWhiteSpace(c.SubDescription) ? "（未建立）" : c.SubDescription;
        Tone = string.IsNullOrWhiteSpace(c.Tone) ? "（未建立）" : c.Tone;

        PersonalityDiffNote = Same(c.MainPersonality, c.SubPersonality) ? "与主设定一致" : "已被经历改写";
        DescriptionDiffNote = Same(c.MainDescription, c.SubDescription) ? "与主设定一致" : "已被经历改写";

        NotesText = c.Notes.Count == 0 ? "暂无心事" : "· " + string.Join("\n· ", c.Notes);
        HasNotes = c.Notes.Count > 0;

        DriftSummary = c.DriftCount == 0
            ? "还没有漂移过——TA 就是主设定里的样子。"
            : $"已漂移 {c.DriftCount} 次，最近一次 {c.UpdatedAt.ToLocalTime():MM-dd HH:mm}"
              + (string.IsNullOrWhiteSpace(c.LastReason) ? "" : $"（{c.LastReason}）");

        History = new ObservableCollection<DriftItem>(c.History
            .OrderByDescending(h => h.At)
            .Select(h => new DriftItem
            {
                Time = h.At.ToLocalTime().ToString("MM-dd HH:mm"),
                Reason = string.IsNullOrWhiteSpace(h.Reason) ? "（未记录原因）" : h.Reason,
                Tone = h.Tone,
                Notes = h.Notes.Count == 0 ? "" : string.Join("；", h.Notes)
            }));
        HasHistory = History.Count > 0;
    }

    private static bool Same(string a, string b) =>
        string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.Ordinal);

    [RelayCommand]
    private async Task Reset()
    {
        var yes = await Shell.Current.DisplayAlert("副设定回正",
            "把副设定退回主设定的副本？会清空累积的心境、心事与留影，主设定不受影响。", "回正", "取消");
        if (!yes) return;
        if (await _sub.ResetAsync(_charId))
        {
            Load();
            await Shell.Current.DisplayAlert("副设定", "已回正到主设定。", "好");
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        await Task.CompletedTask;
        Load();
    }
}
