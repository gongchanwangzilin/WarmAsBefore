using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>心理状况页：只编辑「当前存档的主角」，目录里的病名不当作标签写进对话。</summary>
public sealed partial class MentalHealthViewModel : ObservableObject
{
    private readonly GameEngine _engine;
    private readonly MentalHealthService _mental;
    private readonly CharacterLibrary _chars;

    public ObservableCollection<MentalRow> Active { get; } = new();
    public List<string> CatalogNames { get; } = MentalHealthService.Catalog.Select(c => c.Name).ToList();
    public List<string> SeverityChoices { get; } = new() { "轻度", "中度", "重度" };

    [ObservableProperty] private string _characterLabel = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _hasCharacter;
    [ObservableProperty] private string _newCondition = "";
    [ObservableProperty] private string _newSeverity = "轻度";
    [ObservableProperty] private string _newNote = "";

    public MentalHealthViewModel(GameEngine engine, MentalHealthService mental, CharacterLibrary chars)
    {
        _engine = engine;
        _mental = mental;
        _chars = chars;
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        try
        {
            var id = _engine.State.CharacterId;
            HasCharacter = !string.IsNullOrWhiteSpace(id);
            Active.Clear();
            if (!HasCharacter)
            {
                CharacterLabel = "还没有进入存档";
                Status = "进入一局游戏后，才能给当前角色设置心理状况。";
                return;
            }
            var ch = (await _chars.ListAsync()).FirstOrDefault(c => c.Profile.Id == id);
            CharacterLabel = ch?.Profile.Name ?? "当前角色";
            foreach (var s in await _mental.ListAsync(id))
            {
                var c = MentalHealthService.Catalog.FirstOrDefault(x => x.Id == s.Id);
                Active.Add(new MentalRow
                {
                    Id = s.Id,
                    Name = c?.Name ?? s.Id,
                    Category = c?.Category ?? "",
                    Severity = s.Severity,
                    SeverityText = MentalHealthService.SeverityName(s.Severity),
                    StabilityText = $"稳定度 {s.Stability:0.00}",
                    Note = s.Note,
                    Safety = c?.Safety == true
                });
            }
            Status = Active.Count == 0
                ? "还没有设置。目录共 " + MentalHealthService.Catalog.Count + " 条；只写当前角色，换存档不串。"
                : $"当前生效 {Active.Count} 条；严重度与稳定度会随对话慢慢变化。";
        }
        catch (Exception ex)
        {
            Status = "读取失败：" + ex.Message;
            App.WriteLog("MentalHealthViewModel.Load -> " + ex);
        }
    }

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");

    [RelayCommand]
    private async Task Add()
    {
        if (!HasCharacter) return;
        var name = (NewCondition ?? "").Trim();
        if (name.Length == 0)
        {
            Status = "先选一条心理状况。";
            return;
        }
        var severity = Math.Clamp(SeverityChoices.IndexOf(NewSeverity) + 1, 1, 3);
        var (ok, message) = await _mental.SetAsync(_engine.State.CharacterId, name, severity, (NewNote ?? "").Trim());
        Status = message;
        if (ok) NewNote = "";
        await LoadAsync();
    }

    [RelayCommand]
    private Task SetMild(MentalRow? row) => SetLevelAsync(row, 1);

    [RelayCommand]
    private Task SetModerate(MentalRow? row) => SetLevelAsync(row, 2);

    [RelayCommand]
    private Task SetSevere(MentalRow? row) => SetLevelAsync(row, 3);

    private async Task SetLevelAsync(MentalRow? row, int severity)
    {
        if (!HasCharacter || row is null) return;
        var (_, message) = await _mental.SetAsync(_engine.State.CharacterId, row.Id, severity);
        Status = message;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task Remove(MentalRow? row)
    {
        if (!HasCharacter || row is null) return;
        if (!await Shell.Current.DisplayAlert("心理状况", $"移除「{row.Name}」？这只影响表现，不会删掉相关的记忆。", "移除", "取消")) return;
        await _mental.RemoveAsync(_engine.State.CharacterId, row.Id);
        await LoadAsync();
    }
}

/// <summary>列表里的一行。每次改动整表重建，所以是只读快照。</summary>
public sealed class MentalRow
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public int Severity { get; init; }
    public string SeverityText { get; init; } = "";
    public string StabilityText { get; init; } = "";
    public string Note { get; init; } = "";
    public bool Safety { get; init; }
    public string LevelHint => Severity switch
    {
        3 => "重度",
        2 => "中度",
        _ => "轻度"
    };
}
