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

public sealed partial class GalleryViewModel : ObservableObject
{
    private readonly Modules.AiChat.MemoryVault _memory;
    private readonly GameEngine _engine;
    private readonly Modules.Automation.DailyDiaryWriter _diary;
    private readonly Modules.Cg.CgStore _cg;
    private readonly Modules.Cg.CgViewPayload _cgView;

    private readonly Services.MemoryOverviewService _overview;
    private readonly Services.HumanMemoryService _human;
    private readonly Services.RelationNetworkService _network;
    private readonly Services.MemoryTagService _tags;
    private readonly Services.PageWindowService _windows;
    [ObservableProperty] private bool _isMemoirSelected = true;
    [ObservableProperty] private bool _isDiarySelected;
    [ObservableProperty] private bool _isCgSelected;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ObservableCollection<AffectionMemoryItem> _affectionItems = new();
    [ObservableProperty] private ObservableCollection<DialogueMemoryItem> _memoryItems = new();
    [ObservableProperty] private ObservableCollection<DiaryDayItem> _diaryItems = new();
    [ObservableProperty] private ObservableCollection<CgCollectionItem> _cgItems = new();
    [ObservableProperty] private bool _hasAffection;
    [ObservableProperty] private bool _hasMemory;
    [ObservableProperty] private bool _hasDiary;
    [ObservableProperty] private bool _hasCg;

    public GalleryViewModel(Modules.AiChat.MemoryVault memory, GameEngine engine,
        Modules.Automation.DailyDiaryWriter diary, Modules.Cg.CgStore cg,
        Modules.Cg.CgViewPayload cgView, Services.MemoryOverviewService overview,
        Services.HumanMemoryService human, Services.RelationNetworkService semantic,
        Services.MemoryTagService tags, Services.PageWindowService windows)
    {
        _memory = memory;
        _engine = engine;
        _diary = diary;
        _cg = cg;
        _cgView = cgView;
        _overview = overview;
        _human = human;
        _network = semantic;
        _tags = tags;
        _windows = windows;
    }

    private string CharacterId
    {
        get
        {
            var id = _engine.State.CharacterId;
            return string.IsNullOrEmpty(id) ? "小雨" : id;
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        // 日记是每日自动生成的核心机制：打开回忆录时确保当天总结已存档
        await _diary.EnsureTodayAsync();
        await ReloadDiaryAsync();
        await ReloadMemoirAsync();
        await ReloadCgAsync();
        await ReloadLayersAsync();
        await ReloadRememberedAsync();
    }

    // ===== 记忆分层总览（B）：五层各自的规模一眼可见 =====

    [ObservableProperty] private ObservableCollection<MemoryLayerItem> _layerStats = new();
    [ObservableProperty] private bool _hasLayerStats;

    // ===== 「记得的事」（C）：人类记忆层 + 标签 + 关系网 =====

    [ObservableProperty] private bool _isRememberedSelected;
    [ObservableProperty] private ObservableCollection<HumanMemoryItem> _humanMemoryItems = new();
    [ObservableProperty] private string _tagSummary = "";
    [ObservableProperty] private bool _hasTags;
    [ObservableProperty] private string _semanticSummary = "";
    [ObservableProperty] private bool _hasSemantic;

    /// <summary>打开记忆管理页（关系网 / 标签 / 记得的事 / 副设定）。</summary>
    [RelayCommand]
    private async Task OpenMemoryManage() =>
        await Shell.Current.GoToAsync($"memoryManage?charId={Uri.EscapeDataString(CharacterId)}");

    /// <summary>关系网、搜事件各自另开一扇窗：这边翻记录，那边同时看着网。</summary>
    [RelayCommand]
    private async Task OpenGraph() =>
        await _windows.OpenOrNavigateAsync(PageWindowKeys.Graph, "关系网",
            typeof(Views.MemoryGraphPage), PageWindowKeys.Graph,
            new Dictionary<string, object> { ["charId"] = CharacterId });

    [RelayCommand]
    private async Task OpenSearch() =>
        await _windows.OpenOrNavigateAsync(PageWindowKeys.Search, "搜事件",
            typeof(Views.MemorySearchPage), PageWindowKeys.Search,
            new Dictionary<string, object> { ["charId"] = CharacterId });

    private async Task ReloadRememberedAsync()
    {        try
        {
            var entries = await _human.AllAsync(CharacterId);
            var items = new List<HumanMemoryItem>();
            foreach (var e in entries)
            {
                ImageSource? img = null;
                if (!string.IsNullOrEmpty(e.ImagePath))
                {
                    var abs = Path.IsPathRooted(e.ImagePath) ? e.ImagePath : Path.Combine(App.RootDirectory, e.ImagePath);
                    if (File.Exists(abs)) img = ImageSource.FromFile(abs);
                }
                items.Add(new HumanMemoryItem
                {
                    Kind = e.Kind switch
                    {
                        HumanMemoryKind.Image => "照片",
                        HumanMemoryKind.Important => "重要时刻",
                        HumanMemoryKind.Abstraction => "印象",
                        _ => "事件"
                    },
                    Text = e.Text,
                    Time = e.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm"),
                    RecallText = e.RecallCount > 0 ? $" · 被想起 {e.RecallCount} 次" : "",
                    TagText = e.Tags.Count == 0 ? "" : "#" + string.Join(" #", e.Tags),
                    Image = img
                });
            }
            HumanMemoryItems = new ObservableCollection<HumanMemoryItem>(items);

            var tags = await _tags.ListAsync(CharacterId);
            HasTags = tags.Count > 0;
            TagSummary = HasTags
                ? string.Join("　", tags.Select(t => $"{t.Name}×{t.Count}"))
                : "";

            var links = await _network.TopLinksAsync(CharacterId, 8);
            HasSemantic = links.Count > 0;
            SemanticSummary = HasSemantic
                ? string.Join("\n", links.Select(l => $"{l.A} ↔ {l.B}（{l.Weight:0}，{SourceText(l.Source)}）"))
                : "";
        }
        catch (Exception ex)
        {
            App.WriteLog("GalleryVM.ReloadRemembered -> " + ex.Message);
        }
    }

    private static string SourceText(string source) => source switch
    {
        "human" => "人工改过",
        "ai" => "AI 改过",
        _ => "自动积累"
    };

    private async Task ReloadLayersAsync()
    {
        try
        {
            var stats = await _overview.StatsAsync(CharacterId);
            LayerStats = new ObservableCollection<MemoryLayerItem>(stats.Select(s => new MemoryLayerItem
            {
                Name = s.Name,
                Describe = s.Describe,
                CountText = $"{s.Count} {s.Unit}",
                Detail = s.Detail
            }));
            HasLayerStats = LayerStats.Count > 0;
        }
        catch (Exception ex)
        {
            App.WriteLog("GalleryVM.ReloadLayers -> " + ex.Message);
            HasLayerStats = false;
        }
    }

    [RelayCommand]
    private void SelectTab(string tab)
    {
        IsMemoirSelected = tab == "memoir";
        IsRememberedSelected = tab == "remembered";
        IsDiarySelected = tab == "diary";
        IsCgSelected = tab == "cg";
    }

    partial void OnSearchTextChanged(string value) => _ = ReloadMemoirAsync();

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    private async Task ReloadMemoirAsync()
    {
        var charId = CharacterId;
        var aff = await _memory.All(charId, "affection");
        AffectionItems = new ObservableCollection<AffectionMemoryItem>(aff.Select(m =>
        {
            ImageSource? img = null;
            if (!string.IsNullOrEmpty(m.ImagePath) && File.Exists(m.ImagePath))
            {
                try { img = ImageSource.FromFile(m.ImagePath); } catch { img = null; }
            }
            return new AffectionMemoryItem
            {
                Delta = m.Weight > 0 ? $"+{m.Weight}" : m.Weight.ToString(),
                Reason = m.Keywords ?? "互动",
                Time = m.At.ToLocalTime().ToString("MM-dd HH:mm"),
                Image = img
            };
        }));
        HasAffection = AffectionItems.Count > 0;

        var all = await _memory.All(charId);
        var query = SearchText?.Trim();
        IEnumerable<Models.MemoryEntry> hits = all;
        if (!string.IsNullOrEmpty(query))
        {
            hits = all.Where(m => m.Content.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (m.Keywords is not null && m.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }
        MemoryItems = new ObservableCollection<DialogueMemoryItem>(hits.Take(200).Select(m => new DialogueMemoryItem
        {
            Text = m.Content.Length > 120 ? m.Content[..120] + "…" : m.Content,
            Time = m.At.ToLocalTime().ToString("MM-dd HH:mm")
        }));
        HasMemory = MemoryItems.Count > 0;
    }

    private async Task ReloadDiaryAsync()
    {
        var notes = await _memory.Diary(CharacterId);
        DiaryItems = new ObservableCollection<DiaryDayItem>(notes.OrderByDescending(n => n.Date)
            .Select(n => new DiaryDayItem
            {
                Date = n.Date.ToLocalTime().ToString("yyyy年M月d日"),
                Mood = n.Mood,
                Content = n.Content
            }));
        HasDiary = DiaryItems.Count > 0;
    }

    private async Task ReloadCgAsync()
    {
        await _cg.RefreshAsync();
        var items = new List<CgCollectionItem>();
        foreach (var cg in _cg.AllUnlocked())
        {
            var path = _cg.ResolvePath(cg);
            ImageSource? img = null;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try { img = ImageSource.FromFile(path); } catch { img = null; }
            }
            items.Add(new CgCollectionItem
            {
                Title = string.IsNullOrWhiteSpace(cg.Title) ? "CG" : cg.Title,
                File = cg.File,
                Path = path ?? "",
                Image = img
            });
        }
        CgItems = new ObservableCollection<CgCollectionItem>(items);
        HasCg = CgItems.Count > 0;
    }

    /// <summary>收藏册点开一张 CG：全屏回看（左上角可跳过）。</summary>
    [RelayCommand]
    private async Task OpenCg(CgCollectionItem item)
    {
        if (item?.HasImage is not true) return;
        _cgView.ImagePath = item.Path;
        _cgView.Title = item.Title;
        _cgView.HasPayload = true;
        await Shell.Current.GoToAsync("cg-view");
    }
}

public sealed class CgCollectionItem
{
    public string Title { get; init; } = "";
    public string File { get; init; } = "";
    public string Path { get; init; } = "";
    public ImageSource? Image { get; init; }
    public bool HasImage => Image is not null;
}

public sealed class AffectionMemoryItem
{
    public string Delta { get; init; } = "";
    public string Reason { get; init; } = "";
    public string Time { get; init; } = "";
    public string Display => $"+{Delta} · {Reason} · {Time}";
    /// <summary>好感提升瞬间截屏，若有则显示。</summary>
    public ImageSource? Image { get; init; }
    public bool HasImage => Image is not null;
}

public sealed class DialogueMemoryItem
{
    public string Text { get; init; } = "";
    public string Time { get; init; } = "";
    public string Display => $"[{Time}] {Text}";
}

public sealed class DiaryDayItem
{
    public string Date { get; init; } = "";
    public string Mood { get; init; } = "";
    public string Content { get; init; } = "";
    public string Header => $"{Date} · {Mood}";
}

/// <summary>记忆分层总览里的一层。</summary>
public sealed class MemoryLayerItem
{
    public string Name { get; init; } = "";
    public string Describe { get; init; } = "";
    public string CountText { get; init; } = "";
    public string Detail { get; init; } = "";
}

/// <summary>人类记忆层里的一条「记得的事」。</summary>
public sealed class HumanMemoryItem
{
    public string Kind { get; init; } = "";
    public string Text { get; init; } = "";
    public string Time { get; init; } = "";
    public string RecallText { get; init; } = "";
    public string TagText { get; init; } = "";
    public bool HasTags => !string.IsNullOrEmpty(TagText);
    public ImageSource? Image { get; init; }
    public bool HasImage => Image is not null;
    public string Header => $"{Kind} · {Time}{RecallText}";
}
