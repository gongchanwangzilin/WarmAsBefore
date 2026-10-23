using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Helpers;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 记忆管理：把「关系网入口」「标签」「人类记忆」「副设定」的查看与修改集中到一页。
///
/// 以前这些东西散在角色库的弹窗里，找起来像捉迷藏（用户反馈「没有入口」）。
/// 这一页按「层」分区，每区都能直接改：改错的联系删掉、标签能加能删、记错的事能删、副设定能回正。
/// </summary>
public sealed partial class MemoryManageViewModel : ObservableObject, IQueryAttributable
{
    private readonly GameEngine _engine;
    private readonly RelationNetworkService _network;
    private readonly MemoryTagService _tags;
    private readonly HumanMemoryService _human;
    private readonly CharacterSubProfileService _subProfiles;
    private readonly PageWindowService _windows;

    private string _charId = "";

    public MemoryManageViewModel(GameEngine engine, RelationNetworkService semantic,
        MemoryTagService tags, HumanMemoryService human, CharacterSubProfileService subProfiles,
        PageWindowService windows, MemoryScope scope)
    {
        _engine = engine;
        _network = semantic;
        _tags = tags;
        _human = human;
        _subProfiles = subProfiles;
        _windows = windows;
        _scope = scope;
        ScopeNote = scope.Label;
        scope.Changed += () => MainThread.BeginInvokeOnMainThread(() => ScopeNote = scope.Label);
    }

    [ObservableProperty] private string _title = "记忆管理";

    /// <summary>这一页看到的记忆归谁：本存档，还是所有存档共用。不说清楚，人会以为换档也带着 TA 的记忆。</summary>
    [ObservableProperty] private string _scopeNote = "";
    private readonly MemoryScope _scope;


    // ===== 标签 =====
    [ObservableProperty] private ObservableCollection<string> _tagItems = new();
    [ObservableProperty] private bool _hasTags;
    [ObservableProperty] private string _tagStatus = "";

    // ===== 人类记忆 =====
    public sealed class MemoryRow
    {
        public string Id { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Meta { get; init; } = "";

        /// <summary>逐句的显示项：贴了标签的句子带底色，句尾跟 # 标签。</summary>
        public IReadOnlyList<MemoryHighlight.Line> Lines { get; init; } = new List<MemoryHighlight.Line>();
        public bool HasLines => Lines.Count > 0;

        public ImageSource? Image { get; init; }
        public bool HasImage => Image is not null;

        // ===== 核心记忆 / 创伤（v1.47） =====
        public bool IsCore { get; init; }
        public bool IsTrauma { get; init; }
        public int TraumaStage { get; init; }

        public string CoreLabel => !IsCore ? "" : IsTrauma ? "核心 · 创伤" : "核心记忆";
        public bool HasCoreLabel => CoreLabel.Length > 0;
        public string TraumaLabel => !IsTrauma ? "" : $"第 {TraumaStage} 阶段 · {HumanMemoryMath.TraumaStageName(TraumaStage)}";
        public bool HasTraumaLabel => IsTrauma;
        public string CoreActionLabel => IsCore ? "取消核心" : "设为核心";
        public bool CanHeal => IsTrauma && TraumaStage < 4;
    }

    [ObservableProperty] private ObservableCollection<MemoryRow> _memories = new();
    [ObservableProperty] private bool _hasMemories;
    [ObservableProperty] private IReadOnlyList<MemoryHighlight.LegendItem> _tagLegend = new List<MemoryHighlight.LegendItem>();

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

            var tags = await _tags.ListAsync(CharId);
            TagItems = new ObservableCollection<string>(tags.Select(t =>
                $"{t.Name}（{SubjectText(t.Subject)}，用过 {t.Count} 次）"));
            HasTags = TagItems.Count > 0;
            TagStatus = HasTags ? "" : "还没有标签。可以在这里手工加，AI 在对话里也会自己打。";
            TagLegend = MemoryHighlight.Legend();

            // 底色由「这个标签属于哪一类」决定，所以先拿标签表做一张对照
            var subjectOf = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tags) subjectOf[t.Name] = t.Subject;

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
                    Meta = $"{e.CreatedAt.ToLocalTime():MM-dd HH:mm} · 重要性 {e.Importance:0.00} · 被想起 {e.RecallCount} 次"
                         + (e.IsTrauma ? $" · 疗愈 {e.TraumaHeals} 次" : ""),
                    Lines = MemoryHighlight.Build(e, key => subjectOf.TryGetValue(key, out var s) ? s : null),
                    Image = img,
                    IsCore = e.IsCore,
                    IsTrauma = e.IsTrauma,
                    TraumaStage = e.TraumaStage
                });
            }
            Memories = new ObservableCollection<MemoryRow>(rows);
            HasMemories = Memories.Count > 0;
        }
        catch (Exception ex)
        {
            App.WriteLog("MemoryManageVM.Load -> " + ex);
            TagStatus = "读取失败：" + ex.Message;
        }
    }

    private static string SubjectText(string s) => MemoryHighlight.SubjectText(s);

    // ===== 副设定 =====

    [RelayCommand]
    private async Task OpenSubProfile() =>
        await Shell.Current.GoToAsync($"subProfile?charId={Uri.EscapeDataString(CharId)}");

    // ===== 关系网 / 搜事件：各开一扇窗，主窗口继续聊天 =====

    /// <summary>
    /// 这两件事都要「一边看着网一边改」「一边搜一边聊」，塞在主窗口的导航栈里就做不到。
    /// 桌面另开一扇；手机上 <see cref="PageWindowService"/> 自己退回页内导航，功能不缺。
    /// </summary>
    [RelayCommand]
    private async Task OpenGraph() =>
        await _windows.OpenOrNavigateAsync(PageWindowKeys.Graph, TitleFor("关系网"),
            typeof(Views.MemoryGraphPage), PageWindowKeys.Graph, CharQuery());

    [RelayCommand]
    private async Task OpenSearch() =>
        await _windows.OpenOrNavigateAsync(PageWindowKeys.Search, TitleFor("搜事件"),
            typeof(Views.MemorySearchPage), PageWindowKeys.Search, CharQuery());

    private Dictionary<string, object> CharQuery() => new() { ["charId"] = CharId };

    private string TitleFor(string what)
    {
        var name = _engine.Roster.TryGetValue(CharId, out var ch) ? ch.Profile.Name : "";
        return string.IsNullOrWhiteSpace(name) ? what : $"{what} · {name}";
    }

    // ===== 标签：加 / 删 =====

    [RelayCommand]
    private async Task AddTag()
    {
        var input = await Shell.Current.DisplayPromptAsync("添加标签",
            "标签名（不超过 12 字）。可以给「人 / 话题 / 事 / 地点 / 物」分类，之后按标签回忆。", maxLength: 12);
        if (string.IsNullOrWhiteSpace(input)) return;

        var kind = await AskSubject("这个标签是给什么的", input.Trim()) ?? "event";
        await _tags.ApplyAsync(CharId, new[] { input.Trim() }, kind, "", 0.6);
        await LoadAsync();
    }

    /// <summary>
    /// 贴 / 摘某一句的标签：AI 自动贴的经常偏一句（把地点贴在情绪那句上），
    /// 这里点句子就能改，改完底色立刻跟着分类变。
    /// </summary>
    [RelayCommand]
    private async Task TagLine(MemoryHighlight.Line? line)
    {
        if (line is null) return;
        var current = line.Tags.Count == 0 ? "（还没贴）" : string.Join("、", line.Tags.Select(t => "#" + t));
        var input = await Shell.Current.DisplayPromptAsync("这句贴什么标签",
            $"「{line.Plain}」\n现在贴着：{current}\n\n输入标签名（不超过 12 字）；输入已经贴着的那个就是摘掉它。",
            maxLength: 12);
        var name = input?.Trim() ?? "";
        if (name.Length == 0) return;

        var wasOn = line.Tags.Contains(name, StringComparer.OrdinalIgnoreCase);
        var change = await _human.ToggleSentenceTagAsync(CharId, line.MemoryId, line.Index, name);
        if (change == SentenceTagChange.None)
        {
            await Shell.Current.DisplayAlert("贴标签", "这条记忆已经不在了，刷新一下就好。", "好");
            await LoadAsync();
            return;
        }
        // 新贴的标签要先有分类才知道底色；摘掉的那個留着不动（别的句子可能还在用）
        if (!wasOn && change == SentenceTagChange.Added)
        {
            var kind = await AskSubject($"#{name} 记的是什么", name) ?? "event";
            await _tags.RegisterAsync(CharId, name, kind);
        }
        await LoadAsync();
    }

    private async Task<string?> AskSubject(string title, string tag)
    {
        var pick = await Shell.Current.DisplayActionSheet(title, "取消", null, "人", "话题", "事", "地点", "物");
        if (string.IsNullOrEmpty(pick) || pick == "取消") return null;
        return pick switch
        {
            "人" => "person",
            "话题" => "talk",
            "地点" => "place",
            "物" => "thing",
            _ => "event"
        };
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
        var preview = row.Lines.Count == 0 ? "（空）" : string.Join(" ", row.Lines.Select(l => l.Plain));
        var yes = await Shell.Current.DisplayAlert("删除记忆", $"让 TA 忘掉这件事？\n\n{preview}", "忘掉", "取消");
        if (!yes) return;
        await _human.ForgetAsync(CharId, row.Id);
        await LoadAsync();
    }

    /// <summary>
    /// 核心记忆：人格的锚点，永不衰减、不参与复述计数、每次回想都在。
    /// 这是一个有后果的操作，所以先把后果说清楚再改。
    /// </summary>
    [RelayCommand]
    private async Task ToggleCore(MemoryRow? row)
    {
        if (row is null) return;
        var preview = row.Lines.Count == 0 ? "（空）" : string.Join(" ", row.Lines.Select(l => l.Plain));
        if (row.IsCore)
        {
            var yes = await Shell.Current.DisplayAlert("取消核心记忆",
                $"让这件事重新受遗忘影响？\n\n{preview}\n\n取消后它不再被无条件想起，但内容仍在。", "取消核心", "保留");
            if (!yes) return;
            await _human.SetCoreAsync(CharId, row.Id, false);
        }
        else
        {
            var yes = await Shell.Current.DisplayAlert("设为核心记忆",
                $"这件事会成为 TA 人格的锚点：\n\n{preview}\n\n核心记忆永远不会忘，每次回想都能被取到，"
                + "还会影响周围记忆的权重、塑造 TA 的性格。确定吗？", "设为核心", "再想想");
            if (!yes) return;
            await _human.SetCoreAsync(CharId, row.Id, true);
        }
        await LoadAsync();
    }

    /// <summary>
    /// 创伤疗愈：只降低情绪强度、推进阶段，绝不删除内容。强调「需要具体互动」，
    /// 不让它变成点一下按钮就痊愈的魔法。
    /// </summary>
    [RelayCommand]
    private async Task HealTrauma(MemoryRow? row)
    {
        if (row is null || !row.IsTrauma) return;
        var preview = row.Lines.Count == 0 ? "（空）" : string.Join(" ", row.Lines.Select(l => l.Plain));
        var yes = await Shell.Current.DisplayAlert("记下一次疗愈",
            $"创伤不会被抹掉，只会随着具体的互动慢慢降温。\n\n{preview}\n\n"
            + $"现在：{HumanMemoryMath.TraumaStageName(row.TraumaStage)}\n"
            + "只在你确实陪 TA 面对过这件事之后再点。要继续吗？", "记下这次疗愈", "还没");
        if (!yes) return;
        var (ok, message, _, _) = await _human.HealTraumaAsync(CharId, row.Id);
        await LoadAsync();
        if (ok) await Shell.Current.DisplayAlert("创伤疗愈", message, "好");
    }
}
