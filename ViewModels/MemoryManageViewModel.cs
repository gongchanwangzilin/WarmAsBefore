using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 记忆管理：把「词义关系网」「标签」「人类记忆」「副设定」的查看与修改集中到一页。
///
/// 以前这些东西散在角色库的弹窗里，找起来像捉迷藏（用户反馈「没有入口」）。
/// 这一页按「层」分区，每区都能直接改：改错的联系删掉、标签能加能删、记错的事能删、副设定能回正。
/// </summary>
public sealed partial class MemoryManageViewModel : ObservableObject, IQueryAttributable
{
    private readonly GameEngine _engine;
    private readonly SemanticNetworkService _semantic;
    private readonly MemoryTagService _tags;
    private readonly HumanMemoryService _human;
    private readonly CharacterSubProfileService _subProfiles;

    private string _charId = "";

    public MemoryManageViewModel(GameEngine engine, SemanticNetworkService semantic,
        MemoryTagService tags, HumanMemoryService human, CharacterSubProfileService subProfiles)
    {
        _engine = engine;
        _semantic = semantic;
        _tags = tags;
        _human = human;
        _subProfiles = subProfiles;
    }

    [ObservableProperty] private string _title = "记忆管理";

    // ===== 词义关系网 =====
    [ObservableProperty] private ObservableCollection<string> _links = new();
    [ObservableProperty] private bool _hasLinks;
    [ObservableProperty] private string _linkStatus = "";

    // ===== 标签 =====
    [ObservableProperty] private ObservableCollection<string> _tagItems = new();
    [ObservableProperty] private bool _hasTags;
    [ObservableProperty] private string _tagStatus = "";

    // ===== 人类记忆 =====
    public sealed class MemoryRow
    {
        public string Id { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Text { get; init; } = "";
        public string Meta { get; init; } = "";
        public string TagText { get; init; } = "";
        public bool HasTags => !string.IsNullOrEmpty(TagText);
        public ImageSource? Image { get; init; }
        public bool HasImage => Image is not null;
    }

    [ObservableProperty] private ObservableCollection<MemoryRow> _memories = new();
    [ObservableProperty] private bool _hasMemories;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("charId", out var v) && v is string id && !string.IsNullOrWhiteSpace(id))
            _charId = Uri.UnescapeDataString(id);
        _ = LoadAsync();
    }

    private string CharId
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_charId)) return _charId;
            var id = _engine.State.CharacterId;
            return string.IsNullOrEmpty(id) ? "小雨" : id;
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var name = _engine.Roster.TryGetValue(CharId, out var ch) ? ch.Profile.Name : CharId;
            Title = $"记忆管理 · {name}";

            var links = await _semantic.TopLinksAsync(CharId, 15);
            Links = new ObservableCollection<string>(links.Select(l =>
                $"{l.A} ↔ {l.B}（权重 {l.Weight:0}，{SourceText(l.Source)}）"));
            HasLinks = Links.Count > 0;
            LinkStatus = HasLinks ? "" : "关联网还没长出来——多聊几句会自动积累（不花模型额度）。";

            var tags = await _tags.ListAsync(CharId);
            TagItems = new ObservableCollection<string>(tags.Select(t =>
                $"{t.Name}（{SubjectText(t.Subject)}，用过 {t.Count} 次）"));
            HasTags = TagItems.Count > 0;
            TagStatus = HasTags ? "" : "还没有标签。可以在这里手工加，AI 在对话里也会自己打。";

            var entries = await _human.AllAsync(CharId);
            var rows = new List<MemoryRow>();
            foreach (var e in entries.Take(60))
            {
                ImageSource? img = null;
                if (!string.IsNullOrEmpty(e.ImagePath))
                {
                    var abs = Path.IsPathRooted(e.ImagePath) ? e.ImagePath : Path.Combine(App.RootDirectory, e.ImagePath);
                    if (File.Exists(abs)) img = ImageSource.FromFile(abs);
                }
                rows.Add(new MemoryRow
                {
                    Id = e.Id,
                    Kind = e.Kind switch
                    {
                        HumanMemoryKind.Image => "照片",
                        HumanMemoryKind.Important => "重要时刻",
                        HumanMemoryKind.Abstraction => "印象",
                        _ => "事件"
                    },
                    Text = e.Text,
                    Meta = $"{e.CreatedAt.ToLocalTime():MM-dd HH:mm} · 重要性 {e.Importance:0.00} · 被想起 {e.RecallCount} 次",
                    TagText = e.Tags.Count == 0 ? "" : "#" + string.Join(" #", e.Tags),
                    Image = img
                });
            }
            Memories = new ObservableCollection<MemoryRow>(rows);
            HasMemories = Memories.Count > 0;
        }
        catch (Exception ex)
        {
            App.WriteLog("MemoryManageVM.Load -> " + ex);
            LinkStatus = "读取失败：" + ex.Message;
        }
    }

    private static string SourceText(string s) => s switch
    {
        "human" => "人工改过",
        "ai" => "AI 补过",
        _ => "自动积累"
    };

    private static string SubjectText(string s) => s switch
    {
        "person" => "人",
        "talk" => "话题",
        "place" => "地点",
        "thing" => "物",
        _ => "事"
    };

    // ===== 副设定 =====

    [RelayCommand]
    private async Task OpenSubProfile() =>
        await Shell.Current.GoToAsync($"subProfile?charId={Uri.EscapeDataString(CharId)}");

    // ===== 词义关系网：加 / 删 =====

    [RelayCommand]
    private async Task AddLink()
    {
        var input = await Shell.Current.DisplayPromptAsync("添加联系",
            "输入两个词，用逗号或顿号分隔（例如「咖啡、加班」）。建立后想到一个就容易想起另一个。",
            maxLength: 40);
        var (a, b) = SplitPair(input);
        if (a is null) return;
        await _semantic.LinkAsync(CharId, a, b, 5, "human");
        await LoadAsync();
    }

    [RelayCommand]
    private async Task RemoveLink()
    {
        var input = await Shell.Current.DisplayPromptAsync("删除联系",
            "输入要断开的那两个词，逗号或顿号分隔。", maxLength: 40);
        var (a, b) = SplitPair(input);
        if (a is null) return;
        await _semantic.UnlinkAsync(CharId, a, b);
        await LoadAsync();
    }

    private static (string? A, string? B) SplitPair(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return (null, null);
        var parts = input.Split(new[] { ',', '，', '、', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? (null, null) : (parts[0].Trim(), parts[1].Trim());
    }

    // ===== 标签：加 / 删 =====

    [RelayCommand]
    private async Task AddTag()
    {
        var input = await Shell.Current.DisplayPromptAsync("添加标签",
            "标签名（不超过 12 字）。可以给「人 / 话题 / 事 / 地点 / 物」分类，之后按标签回忆。", maxLength: 12);
        if (string.IsNullOrWhiteSpace(input)) return;

        var subject = await Shell.Current.DisplayActionSheet("这个标签是给什么的", "取消", null,
            "人", "话题", "事", "地点", "物");
        if (subject is null || subject == "取消") return;
        var kind = subject switch
        {
            "人" => "person",
            "话题" => "talk",
            "地点" => "place",
            "物" => "thing",
            _ => "event"
        };
        await _tags.ApplyAsync(CharId, new[] { input.Trim() }, kind, "", 0.6);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task RemoveTag()
    {
        var input = await Shell.Current.DisplayPromptAsync("删除标签", "输入要删掉的标签名：", maxLength: 12);
        if (string.IsNullOrWhiteSpace(input)) return;
        var removed = await _tags.RemoveAsync(CharId, input.Trim());
        await LoadAsync();
        if (removed == 0) await Shell.Current.DisplayAlert("删除标签", "没有找到这个标签。", "好");
    }

    // ===== 人类记忆：加 / 删 =====

    [RelayCommand]
    private async Task AddMemory()
    {
        var text = await Shell.Current.DisplayPromptAsync("新增一条记忆",
            "按 TA 自己的口吻写一件事（不超过 60 字）。这是「TA 记得的事」，会参与回想。", maxLength: 60);
        if (string.IsNullOrWhiteSpace(text)) return;
        await _human.StoreAsync(CharId, text.Trim());
        await LoadAsync();
    }

    [RelayCommand]
    private async Task RemoveMemory(MemoryRow? row)
    {
        if (row is null) return;
        var yes = await Shell.Current.DisplayAlert("删除记忆", $"让 TA 忘掉这件事？\n\n{row.Text}", "忘掉", "取消");
        if (!yes) return;
        await _human.ForgetAsync(CharId, row.Id);
        await LoadAsync();
    }
}
