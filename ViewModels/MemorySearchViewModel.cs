using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Helpers;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 搜事件：把 TA 记得的事按「人怎么找回一件事」的方式找出来。
///
/// 全文检索只会问「这句话里有这个词吗」，可人回忆不是这样：想不起原话，就靠
/// 一个标签（#第一次）、一种感觉（有点委屈）、甚至一个日子（8月24号）把整件事拽回来。
/// 所以这些全都算命中，而且每条都写明<b>凭什么命中</b> —— 不写出来的搜索结果只能信一半。
///
/// 字面一个都没对上时还有第二条路：量化模型把这句话换算成数值，按语境找出挨得近的说法再问一遍。
/// 这一路捞回来的东西会标明「字面没有 · 数值上像…」，近似命中和字面命中从来不该长一个样。
///
/// 情绪在这里是配比不是一个词：结果里那条比例尺把「几分害怕、几分安心」摊开，
/// 命中里出现过的情绪名做成快捷条，点一下就按那种感觉再搜一遍。
///
/// 这一页通常自己就是一扇独立窗口（<see cref="PageWindowService"/>）：那边看着网，这边搜，
/// 搜到了点「在网里看它」，那个点就在关系网里被钉住。
/// </summary>
public sealed partial class MemorySearchViewModel : ObservableObject, IQueryAttributable
{
    private const int MaxHits = 120;
    private const int BarTotal = 168;

    private readonly GameEngine _engine;
    private readonly HumanMemoryService _human;
    private readonly MemoryTagService _tags;
    private readonly PageWindowService _windows;
    private readonly RelationNetworkService _network;
    private readonly EmbeddingService _embed;
    private readonly MemoryScope _scope;
    private readonly SettingsManager _settings;

    private string _charId = "";
    private List<HumanMemoryHit> _all = new();
    private Dictionary<string, string?> _subjectCache = new();
    private CancellationTokenSource? _debounce;

    public MemorySearchViewModel(GameEngine engine, HumanMemoryService human,
        MemoryTagService tags, PageWindowService windows, RelationNetworkService network,
        EmbeddingService embed, MemoryScope scope, SettingsManager settings)
    {
        _engine = engine;
        _human = human;
        _tags = tags;
        _windows = windows;
        _network = network;
        _embed = embed;
        _scope = scope;
        _settings = settings;
        scope.Changed += () => MainThread.BeginInvokeOnMainThread(() => ScopeNote = scope.Label);
    }

    // ==================== 页面状态 ====================

    [ObservableProperty] private string _title = "搜事件";
    [ObservableProperty] private string _characterName = "";
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _emptyHint = "";
    [ObservableProperty] private bool _searching;
    [ObservableProperty] private ObservableCollection<HitRow> _results = new();
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private bool _hasNoResults;
    [ObservableProperty] private ObservableCollection<Chip> _kindChips = new();
    [ObservableProperty] private ObservableCollection<Chip> _emotionChips = new();
    [ObservableProperty] private bool _hasEmotionChips;
    [ObservableProperty] private string _kindFilter = "";

    /// <summary>
    /// 量化模型给的另一条检索路：把这句话换算成数值，列出数值上接近它的说法。
    /// 全文检索问「这话里有这个词吗」，这里问「谁和它活得像同一类东西」——
    /// 从没和「下雨」同框出现过的「落雨」，只有语境向量看得见。
    /// </summary>
    [ObservableProperty] private ObservableCollection<Chip> _quantChips = new();
    [ObservableProperty] private bool _hasQuantChips;

    /// <summary>这些结果是按数值捞回来的（字面没命中），得让人一眼分辨出来。</summary>
    [ObservableProperty] private bool _approximate;

    /// <summary>
    /// 链式模式：不是「哪几条直接提到」，而是从线索出发沿共享字词/标签/时间/情绪一段段连下去。
    /// 默认 5 条（设置里可改），新的在前，同一天里越像线索的越前。
    /// </summary>
    [ObservableProperty] private bool _chainMode;
    private string _chainSummary = "";

    public string ChainText => ChainMode ? "链式 · 开" : "链式";
    public Color ChainFill => Fill(ChainMode);
    public Color ChainInk => Ink(ChainMode);

    partial void OnChainModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ChainText));
        OnPropertyChanged(nameof(ChainFill));
        OnPropertyChanged(nameof(ChainInk));
        if (Query.Trim().Length > 0) _ = SearchCoreAsync(Query);
    }

    /// <summary>这份记忆记在哪个作用域下：「只在这份存档里」还是「所有存档共用」。</summary>
    [ObservableProperty] private string _scopeNote = "";

    private string CharId
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_charId)) return _charId;
            return _engine.State.CharacterId;
        }
    }

    // ==================== 进入 ====================

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("charId", out var v) && v is string id && !string.IsNullOrWhiteSpace(id))
            _charId = Uri.UnescapeDataString(id);
        CharacterName = _engine.Roster.TryGetValue(CharId, out var ch) ? ch.Profile.Name : "";
        Title = "搜事件" + (string.IsNullOrWhiteSpace(CharacterName) ? "" : $" · {CharacterName}");
        ScopeNote = _scope.Label;

        // 带着词进来就直接搜那个词（OnQueryChanged 接手）；没带词才先摊开「最近记得的这些」。
        if (query.TryGetValue("q", out var qv) && qv is string seed && !string.IsNullOrWhiteSpace(seed))
            Query = Uri.UnescapeDataString(seed);
        else _ = SearchCoreAsync("");
    }

    /// <summary>边打字边搜，但等一下再动手：每敲一个字就查一遍，输入框会先卡住。</summary>
    partial void OnQueryChanged(string value)
    {
        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _debounce, cts)?.Cancel();
        if (value.Length == 0) { _ = SearchCoreAsync(""); return; }
        _ = DebouncedAsync(cts.Token, value);
    }

    private async Task DebouncedAsync(CancellationToken token, string text)
    {
        try { await Task.Delay(240, token); }
        catch (OperationCanceledException) { return; }
        if (token.IsCancellationRequested) return;
        await SearchCoreAsync(text);
    }

    [RelayCommand]
    private Task SearchNowAsync() => SearchCoreAsync(Query);

    [RelayCommand]
    private void ClearQuery() => Query = "";

    [RelayCommand]
    private void ToggleChain() => ChainMode = !ChainMode;

    // ==================== 搜 ====================

    private async Task SearchCoreAsync(string text)
    {
        Searching = true;
        try
        {
            if (CharId.Length == 0)
            {
                // 既没带角色进来、主窗口也还没定下跟谁说话：直说。
                // 用「0 件」收尾会把一个接线问题说成「TA 什么都没记住」，人就白花时间去翻记忆。
                _all = new();
                _near = new List<QuantMatch>();
                Approximate = false;
                _quantNote = "";
                _chainSummary = "";
                RebuildQuantChips("");
                RebuildRows();
                Status = "还没定下跟谁说话";
                EmptyHint = "从角色卡「··· → 搜事件」进来，或先在主窗口选一个角色。";
                return;
            }
            var q = text.Trim();

            // 链式：先钉住线索本身，再一跳一跳往外连。这条路上的结果不按「像不像」排，
            // 而是按「新的在前、同一天里更像的在前」——人顺着一条线回忆时就是这个顺序。
            if (ChainMode && q.Length > 0)
            {
                var limit = Math.Clamp(_settings.Current.ChainSearchLimit <= 0 ? 5 : _settings.Current.ChainSearchLimit, 1, 30);
                var chain = await _human.ChainSearchAsync(CharId, q, limit);
                _chainSummary = chain.Summary;
                _all = chain.Hits.Select(h => new HumanMemoryHit(h.Entry, h.Score,
                        h.Depth <= 0
                            ? h.Why
                            : $"第 {h.Depth + 1} 层 · {h.Why} · 由「{h.Bridge}」连过来"))
                    .ToList();
                Approximate = false;
                _quantNote = "";
                _near = new List<QuantMatch>();
                RebuildQuantChips(q);
                _subjectCache = await SubjectsAsync();
                RebuildRows();
                return;
            }
            _chainSummary = "";
            _all = await _human.SearchAsync(CharId, q, MaxHits);
            Approximate = false;
            _quantNote = "";
            _near = q.Length == 0
                ? new List<QuantMatch>()
                : await _network.NearestForAsync(CharId, q, 8);
            RebuildQuantChips(q);
            // 字面一个都没对上，才轮到量化模型说话：这正是全文检索捞不到的那半边。
            // 这里不看 _near：近邻说法是统计那条路的产物，模型直接按整句话比记忆时可能一个都不必借。
            if (q.Length > 0 && _all.Count == 0)
            {
                var byNumber = await ByNumbersAsync(q);
                if (byNumber.Count > 0)
                {
                    _all = byNumber;
                    Approximate = true;
                }
            }
            _subjectCache = await SubjectsAsync();
            RebuildRows();
        }
        catch (Exception ex)
        {
            Status = "搜失败了：" + ex.Message;
            App.WriteLog("MemorySearch -> " + ex.Message);
        }
        finally
        {
            Searching = false;
        }
    }

    private List<QuantMatch> _near = new();

    /// <summary>这一批近似命中是谁算的：界面要能告诉用户钱花没花。</summary>
    private string _quantNote = "";

    /// <summary>
    /// 字面没命中时的第二条路，两条子路按优先级走：
    /// · 量化模型：整句话换成向量，直接和 TA 记得的每一件事比 —— 它问的是「哪件事和这句话活得像一类」，
    ///   连原话里的词都没出现过也能捞回来；
    /// · 本地统计：模型没配上才借近义说法再搜一遍，分数乘以接近度，「像 12%」不该和「像 90%」并列。
    /// </summary>
    private async Task<List<HumanMemoryHit>> ByNumbersAsync(string query)
    {
        var byVector = await _embed.SearchMemoriesAsync(CharId, query, MaxHits);
        if (byVector.Count > 0)
        {
            _quantNote = "量化模型（" + _embed.BackendName + "）按整句数值比出来的";
            return byVector
                .Select(h => new HumanMemoryHit(h.Entry, h.Cosine,
                    $"字面没有 · 这句话数值上像它({h.Cosine:P0})"))
                .OrderByDescending(h => h.Score)
                .ThenByDescending(h => h.Entry.CreatedAt)
                .Take(MaxHits).ToList();
        }

        var extra = new List<HumanMemoryHit>();
        foreach (var n in _near)
        {
            var found = await _human.SearchAsync(CharId, n.Term, 24);
            foreach (var h in found)
                extra.Add(h with
                {
                    Score = h.Score * Math.Clamp(n.Cosine, 0, 1),
                    Why = $"字面没有 · 数值上像「{n.Term}」({n.Cosine:P0})"
                });
            if (extra.Count >= 40) break;
        }
        if (extra.Count > 0) _quantNote = _network.QuantBackendText;
        return extra.GroupBy(h => h.Entry.Id, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(x => x.Score).First())
            .OrderByDescending(h => h.Score)
            .ThenByDescending(h => h.Entry.CreatedAt)
            .Take(MaxHits).ToList();
    }

    private async Task<Dictionary<string, string?>> SubjectsAsync()
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var t in await _tags.ListAsync(CharId)) map.TryAdd(t.Name, t.Subject);
        }
        catch (Exception ex) { App.WriteLog("MemorySearch.Tags -> " + ex.Message); }
        return map;
    }

    /// <summary>摊成行 + 重算快捷条：底色要看标签分类，所以分类表得跟着进来。</summary>
    private void RebuildRows()
    {
        var rows = new List<HitRow>();
        foreach (var h in _all)
        {
            var kind = KindText(h.Entry.Kind);
            if (KindFilter.Length > 0 && kind != KindFilter) continue;
            rows.Add(BuildRow(h, kind));
        }
        Results = new ObservableCollection<HitRow>(rows);
        HasResults = rows.Count > 0;
        HasNoResults = !HasResults;

        var q = Query.Trim();
        Status = q.Length == 0
            ? $"TA 记得 {rows.Count} 件（最新在这些）"
            : ChainMode
                ? _chainSummary + (KindFilter.Length > 0 ? $" · 只看{KindFilter}" : "")
                : Approximate
                    ? $"「{q}」字面上没命中 · {_quantNote}，捞回 {rows.Count} 件"
                    : $"「{q}」命中 {rows.Count} 件" + (KindFilter.Length > 0 ? $" · 只看{KindFilter}" : "");
        EmptyHint = q.Length == 0
            ? "还没记下什么事。多聊几句，重要的那些会被 TA 自己记住。"
            : ChainMode
                ? $"顺着「{q}」一条也没连上。换个线索：那个人名、那天的天气、当时说过的半句话。"
                : _near.Count > 0
                    ? $"按数值问了 {_near.Count} 个近的说法（{string.Join("、", _near.Take(3).Select(x => x.Term))}），也没有哪件事对上。"
                    : $"没有哪件事对上「{q}」。换条路试试：贴过的标签名、那种感觉的名字、或者那天的日子（8月24号）。" +
                      (_embed.IsConfigured ? "" : "\n按整句话的数值找近义这条路还没开：设置 → AI 与模型 → 量化模型（现在是本地统计在顶）。");

        KindChips = new ObservableCollection<Chip>(BuildKindChips());
        var names = _all.SelectMany(h => h.Entry.Emotion?.Parts ?? (IReadOnlyList<EmotionPart>)Array.Empty<EmotionPart>())
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(8)
            .Select(g => new Chip(g.Key, g.Key, ChipRole.Emotion,
                Fill(q.Length > 0 && q == g.Key), Ink(q.Length > 0 && q == g.Key)))
            .ToList();
        EmotionChips = new ObservableCollection<Chip>(names);
        HasEmotionChips = names.Count > 0;
    }

    private IEnumerable<Chip> BuildKindChips()
    {
        yield return new Chip("全部", "", ChipRole.Kind, Fill(KindFilter.Length == 0), Ink(KindFilter.Length == 0));
        foreach (var k in new[] { "事件", "重要时刻", "印象", "照片" })
            yield return new Chip(k, k, ChipRole.Kind, Fill(KindFilter == k), Ink(KindFilter == k));
    }

    /// <summary>数值上接近的这些说法做成快捷条：点一个就按它再搜一遍，人不用自己想到近义词。</summary>
    private void RebuildQuantChips(string q)
    {
        var chips = _near
            .Select(x => new Chip($"{x.Term} {x.Cosine:P0}", x.Term, ChipRole.Quant,
                Fill(q.Length > 0 && string.Equals(q, x.Term, StringComparison.Ordinal)),
                Ink(false)))
            .ToList();
        QuantChips = new ObservableCollection<Chip>(chips);
        HasQuantChips = chips.Count > 0;
    }

    [RelayCommand]
    private void PickChip(Chip? chip)
    {
        if (chip is null) return;
        if (chip.Role == ChipRole.Kind)
        {
            KindFilter = KindFilter == chip.Value ? "" : chip.Value;
            RebuildRows();
            return;
        }
        Query = chip.Value;
    }

    private HitRow BuildRow(HumanMemoryHit h, string kind)
    {
        var e = h.Entry;
        var parts = e.Emotion is { IsEmpty: false } mix ? mix.Parts.Take(3).ToList() : new List<EmotionPart>();
        var slices = new List<EmotionSlice>();
        if (parts.Count > 0)
        {
            var sum = Math.Max(0.001, parts.Sum(p => p.Ratio));
            var left = BarTotal;
            for (var i = 0; i < parts.Count; i++)
            {
                var w = i == parts.Count - 1
                    ? left
                    : Math.Clamp((int)Math.Round(parts[i].Ratio / sum * BarTotal), 10, Math.Max(10, left - 10 * (parts.Count - i - 1)));
                left -= w;
                slices.Add(new EmotionSlice(parts[i].Name, parts[i].Ratio,
                    Color.FromArgb(EmotionWords.ColorOf(parts[i].Name)), w));
            }
        }

        ImageSource? img = null;
        if (!string.IsNullOrEmpty(e.ImagePath))
        {
            var abs = Path.IsPathRooted(e.ImagePath) ? e.ImagePath : Path.Combine(App.RootDirectory, e.ImagePath);
            if (File.Exists(abs)) img = ImageSource.FromFile(abs);
        }

        return new HitRow
        {
            Id = e.Id,
            Kind = kind,
            Why = h.Why,
            HasWhy = !string.IsNullOrWhiteSpace(h.Why),
            Meta = $"{e.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm} · 重要性 {e.Importance:0.00} · 被想起 {e.RecallCount} 次",
            EmotionText = e.Emotion is { IsEmpty: false } m ? m.Text : "",
            HasEmotion = slices.Count > 0,
            Slices = slices,
            Lines = MemoryHighlight.Build(e, key => _subjectCache.TryGetValue(key, out var s) ? s : null),
            Image = img,
            HasImage = img is not null
        };
    }

    private static string KindText(HumanMemoryKind k) => k switch
    {
        HumanMemoryKind.Image => "照片",
        HumanMemoryKind.Important => "重要时刻",
        HumanMemoryKind.Abstraction => "印象",
        _ => "事件"
    };

    // ==================== 出去 ====================

    /// <summary>在网里看它：另开一扇关系网，把这一点钉住（记忆 id 就是那个点的来历）。</summary>
    [RelayCommand]
    private async Task OpenInGraphAsync(HitRow? row)
    {
        var jump = row?.Id ?? "";
        await _windows.OpenOrNavigateAsync(PageWindowKeys.Graph,
            "关系网" + (string.IsNullOrWhiteSpace(CharacterName) ? "" : $" · {CharacterName}"),
            typeof(Views.MemoryGraphPage), PageWindowKeys.Graph,
            new Dictionary<string, object> { ["charId"] = CharId, ["jump"] = jump });
    }

    [RelayCommand]
    private async Task OpenGraphAsync() => await OpenInGraphAsync(null);

    // ==================== 主题色 ====================

    private static Color Res(string key, string fallback)
        => Application.Current?.Resources.TryGetValue(key, out var v) is true && v is Color c
            ? c : Color.FromArgb(fallback);

    /// <summary>条目的底色：亮着的那枚用暖色填充，没选中的留在输入底色上。</summary>
    private static Color Fill(bool on) => on ? Res("AccentWarm", "#F5D8B8") : Res("InputBg", "#FFFBF3");

    /// <summary>文字一律用墨色档：淡填充令牌当文字会看不见（这页踩过两次）。</summary>
    private static Color Ink(bool on) => on ? Res("WarmDark", "#5D4A3A") : Res("WarmBody", "#4A3D2C");

    // ==================== 显示项 ====================

    /// <summary>一条命中：句子按标签上底色，命中理由单独一行，情绪摊成比例尺。</summary>
    public sealed class HitRow
    {
        public string Id { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Why { get; init; } = "";
        public bool HasWhy { get; init; }
        public string Meta { get; init; } = "";
        public string EmotionText { get; init; } = "";
        public bool HasEmotion { get; init; }
        public IReadOnlyList<EmotionSlice> Slices { get; init; } = new List<EmotionSlice>();
        public IReadOnlyList<MemoryHighlight.Line> Lines { get; init; } = new List<MemoryHighlight.Line>();
        public ImageSource? Image { get; init; }
        public bool HasImage { get; init; }
    }

    /// <summary>配比里的一份：宽度已按整条 <see cref="BarTotal"/> 算好，画面只管摆。</summary>
    public sealed record EmotionSlice(string Name, double Ratio, Color Tint, double Width)
    {
        public string Label => $"{Name} {(int)Math.Round(Ratio * 100)}%";
    }

    public sealed record Chip(string Text, string Value, ChipRole Role, Color Fill, Color Ink);

    public enum ChipRole { Kind, Emotion, Quant }
}
