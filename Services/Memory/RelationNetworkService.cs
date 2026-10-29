using System.Text;
using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 关系网：一个人脑子里「什么和什么有关系」的那张网，后台随对话与记忆自动长出来。
///
/// 和旧版「词义关系网」的三点区别：
/// · 节点不再只有词：一段记得的事、一张照片、一个标签、一个人、一个地点都能成为节点；
/// · 联系分红好几类：等价（老妈＝妈妈）与一般联想（妈妈→厨房）必须分开，
///   否则扩散检索会顺着弱关联一路跑偏；
/// · 存的就是这层结构本身（JSON），界面上的 JSON 视图与磁盘上的文件是同一份东西。
///
/// 依然**默认不花模型钱**：共现统计自己会长，模型整理是用户在页面上按下去才做的。
///
/// 「按数值找近邻」这件事有两条路，优先级是定的：
/// · 量化模型（<see cref="EmbeddingService"/>）：把说法和整句直接换成稠密向量，近义词数值就近 —— 主力；
/// · 本地共现统计（<see cref="RelationStatsIndex"/>）：没配模型、调用失败或额度用完时的兜底，不要钱也一直在。
/// 每次量化都记下是哪条路答的（<see cref="LastQuantBackend"/>），界面才能老实告诉用户
/// 「量化模型算的」还是「本地统计凑的」。
/// </summary>
public sealed class RelationNetworkService
{
    private const string StoreKey = "relation_networks";
    private const string ParamsKey = "relation_params";
    private const string StatsKey = "relation_stats";
    private const string LegacyKey = "semantic_networks";
    private const int MaxEdgesPerCharacter = 4000;
    private const int MaxPruned = 2000;
    private const double AutoStep = 1.0;

    /// <summary>关联强度到这条线才值得存进网里（低于它的是巧合）。</summary>
    public const double WeaveFloor = 0.12;

    /// <summary>一轮最多织进多少条量化出来的联系：不封顶的话一个长句能把半张网重写一遍。</summary>
    public const int MaxWeavePerPairPass = 900;

    /// <summary>语境比较只看最常用的这些说法：一万个词两两比夹角，按一次钮要等到天黑。</summary>
    public const int SimilarTerms = 120;

    /// <summary>语境余弦到这个线才算「说的是同一类东西」。</summary>
    public const double SimilarFloor = 0.42;

    /// <summary>
    /// 量化模型只在这么多个说法里找近邻：底账几千个词一次全送过去，就是几千条输入。
    /// 人想看的也就是眼前这几十个里谁最像。
    /// </summary>
    public const int QuantCandidates = 96;

    /// <summary>稠密余弦低于这个线就别提「像」：向量法的基线比稀疏法高，但基线高不等于挨着。</summary>
    public const double VectorFloor = 0.20;

    /// <summary>
    /// 按数值再织密时，每个说法只跟最挨着的前几个牵线。
    /// 不敢用绝对阈值：各家模型的余弦基线差得很远（有的互不相干也能给到 0.5），
    /// 阈值一横就是整张网糊成一团；kNN 这种「相对最像」的说法才换模型也不塌。
    /// </summary>
    public const int VectorNearestPerTerm = 2;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "我们", "你们", "他们", "什么", "怎么", "可以", "这个", "那个", "现在", "然后", "因为", "所以",
        "但是", "如果", "已经", "还是", "就是", "不是", "没有", "一个", "一下", "一点", "这样", "那样",
        "真的", "好像", "也许", "应该", "自己", "时候", "东西", "事情", "感觉", "知道", "觉得"
    };

    private readonly StorageProvider _store;
    private readonly HumanMemoryService _human;
    private readonly MemoryTagService _tags;
    private readonly MemoryScope _scope;
    private readonly EmbeddingService _embed;
    private readonly object _gate = new();
    private Dictionary<string, RelationGraph> _cache = new(StringComparer.Ordinal);
    private Dictionary<string, RelationParams> _params = new(StringComparer.Ordinal);
    private Dictionary<string, RelationStats> _stats = new(StringComparer.Ordinal);
    private Dictionary<string, List<RelationNet>> _nets = new(StringComparer.Ordinal);
    private Task? _loading;
    private Task? _paramsLoading;
    private Task? _statsLoading;
    private bool _loadFaulted;
    private bool _statsFaulted;
    private bool _paramsFaulted;
    private string _netKey = StoreKey, _paramKey = ParamsKey, _statKey = StatsKey;

    /// <summary>已经顺手补过底账的角色（见 <see cref="HealEmptyStatsAsync"/>：一次运行只补一遍）。</summary>
    private readonly HashSet<string> _statsHealed = new(StringComparer.Ordinal);

    /// <summary>上一次量化结果是哪条路算的（界面照着这个告诉用户钱花没花）。</summary>
    public QuantBackend LastQuantBackend { get; private set; } = QuantBackend.Statistics;

    /// <summary>给人看的那句话：量化模型算的 / 本地统计凑的。</summary>
    public string QuantBackendText => LastQuantBackend switch
    {
        QuantBackend.Embedding => "量化模型算的（" + _embed.BackendName + "）",
        _ => "本地统计凑的"
    };

    public RelationNetworkService(StorageProvider store, HumanMemoryService human, MemoryTagService tags,
        MemoryScope scope, EmbeddingService embed)
    {
        _store = store;
        _human = human;
        _tags = tags;
        _scope = scope;
        _embed = embed;
        Rekey();
        scope.Changed += OnScopeChanged;
    }

    private void Rekey()
    {
        _netKey = _scope.Key(StoreKey);
        _paramKey = _scope.Key(ParamsKey);
        _statKey = _scope.Key(StatsKey);
    }

    /// <summary>超省内存模式：网、统计、参数、分片都交还给系统，下次用时重读盘。</summary>
    public void ReleaseCache()
    {
        lock (_gate)
        {
            _cache = new(StringComparer.Ordinal);
            _params = new(StringComparer.Ordinal);
            _stats = new(StringComparer.Ordinal);
            _nets = new(StringComparer.Ordinal);
            _loading = _paramsLoading = _statsLoading = null;
            _loadFaulted = _statsFaulted = _paramsFaulted = false;
        }
    }

    /// <summary>换档：网、统计、参数、分片全是这个存档的东西，一个都不能留到下一个存档。</summary>
    private void OnScopeChanged()
    {
        lock (_gate)
        {
            _cache = new(StringComparer.Ordinal);
            _params = new(StringComparer.Ordinal);
            _stats = new(StringComparer.Ordinal);
            _nets = new(StringComparer.Ordinal);
            _statsHealed.Clear();          // 补过的底账属于上一个存档，新档还得补自己那一份
            _loading = _paramsLoading = _statsLoading = null;
            _loadFaulted = _statsFaulted = _paramsFaulted = false;
            Rekey();
        }
    }

    // ==================== 装载与迁移 ====================

    /// <summary>
    /// 「读过盘」以读完为准：缓存装载这个动作本身，而不是先置一个布尔标记。
    /// 标记先置位时，同一瞬间进来的第二个调用会以为加载好了，捧着空表就往下走。
    /// </summary>
    private Task EnsureLoadedAsync()
    {
        lock (_gate) return _loading ??= LoadAsync();
    }

    private async Task LoadAsync()
    {
        string key;
        lock (_gate)
        {
            _loadFaulted = false;
            key = _netKey;
        }
        try
        {
            var saved = await _store.Load<Dictionary<string, RelationGraph>>(key);
            lock (_gate)
            {
                if (saved is not null && key == _netKey)
                    _cache = new Dictionary<string, RelationGraph>(saved, StringComparer.Ordinal);
            }
        }
        catch (Exception ex)
        {
            // 读不通的时候这张网是空的。这时候落盘 = 这一档的关系网只剩这一次新织的几条，
            // 所以标记故障并放开重读（多数是被云同步/杀毒占用了一瞬），
            // 宁可这次没织上，不可把旧的抹了。
            _loadFaulted = true;
            lock (_gate) _loading = null;
            App.WriteLog("RelationNetwork.Load -> " + ex.Message);
        }
        await MigrateLegacyAsync();
    }

    /// <summary>
    /// 旧档案（只有词与词的 <c>semantic_networks.json</c>）升成节点/边结构：
    /// 词变成节点，旧联系变成「一般联系」边。老数据一条不丢，也不用重新聊一遍才长出来。
    /// </summary>
    private async Task MigrateLegacyAsync()
    {
        bool needs;
        lock (_gate) needs = _cache.Count == 0 && !_loadFaulted;
        if (!needs) return;
        try
        {
            var raw = await _store.LoadRawAsync(LegacyKey);
            if (string.IsNullOrWhiteSpace(raw)) return;
            var old = JsonSerializer.Deserialize<Dictionary<string, List<LegacyLink>>>(raw);
            if (old is null || old.Count == 0) return;

            var moved = 0;
            lock (_gate)
            {
                foreach (var kv in old)
                {
                    var graph = GraphLocked(kv.Key);
                    foreach (var l in kv.Value)
                    {
                        if (string.IsNullOrWhiteSpace(l.A) || string.IsNullOrWhiteSpace(l.B)) continue;
                        var a = UpsertNodeLocked(graph, NodeKind.Word, l.A, l.A, 0.3, l.Source);
                        var b = UpsertNodeLocked(graph, NodeKind.Word, l.B, l.B, 0.3, l.Source);
                        BumpEdgeLocked(graph, a, b, RelationKind.Associated, Math.Max(0.1, l.Weight), l.Source, manual: true);
                        moved++;
                    }
                }
            }
            await PersistAsync();
            App.WriteLog($"RelationNetwork: 迁移旧关联网 {moved} 条联系");
        }
        catch (Exception ex)
        {
            App.WriteLog("RelationNetwork.Migrate -> " + ex.Message);
        }
    }

    private sealed record LegacyLink
    {
        public string A { get; init; } = "";
        public string B { get; init; } = "";
        public double Weight { get; init; }
        public string Source { get; init; } = "auto";
    }

    private Task PersistAsync()
    {
        Dictionary<string, RelationGraph> snapshot;
        lock (_gate)
        {
            if (_loadFaulted)
            {
                App.WriteLog("RelationNetwork.Persist -> 已跳过落盘：这一档的关系网没能读出来，不拿空表盖盘");
                return Task.CompletedTask;
            }
            snapshot = _cache.ToDictionary(kv => kv.Key,
                kv => new RelationGraph
                {
                    Version = kv.Value.Version,
                    Nodes = kv.Value.Nodes.ToList(),
                    Edges = kv.Value.Edges.ToList(),
                    Pruned = kv.Value.Pruned.ToList()
                }, StringComparer.Ordinal);
        }
        return _store.Save(_netKey, snapshot);
    }

    private static RelationGraph GraphLocked(Dictionary<string, RelationGraph> table, string charId)
    {
        if (!table.TryGetValue(charId, out var graph)) table[charId] = graph = new RelationGraph();
        return graph;
    }

    private RelationGraph GraphLocked(string charId) => GraphLocked(_cache, charId);

    // ==================== 写入 ====================

    /// <summary>
    /// 学一轮对话：先记进底账（N、词频、共现），再顺带把量化出来的联系织进网里。
    /// 就像人听多了「咖啡」和「加班」一起出现，之后想到咖啡就会想起加班。
    /// 这一路只走本地统计：每轮对话都去问量化模型，等于在后台替用户默默烧额度。
    /// </summary>
    public async Task LearnAsync(string charId, params string?[] texts)
    {
        if (string.IsNullOrWhiteSpace(charId)) return;
        var terms = texts.Where(t => !string.IsNullOrWhiteSpace(t))
            .SelectMany(t => ExtractTerms(t!))
            .Distinct(StringComparer.Ordinal)
            .Take(RelationStats.MaxTermsPerTurn)
            .ToList();
        if (terms.Count < 2) return;

        await EnsureLoadedAsync();
        await EnsureStatsLoadedAsync();
        lock (_gate)
        {
            var graph = GraphLocked(charId);
            var ids = terms.Select(t => UpsertNodeLocked(graph, NodeKind.Word, t, t, 0.3, "auto")).ToList();
            for (var i = 0; i < ids.Count; i++)
                for (var j = i + 1; j < ids.Count; j++)
                    BumpEdgeLocked(graph, ids[i], ids[j], RelationKind.Associated, AutoStep, "auto", manual: false);

            StatsLocked(charId).Observe(terms);
            WeaveLocked(charId, graph);
            TrimLocked(graph);
            _nets.Remove(charId);       // 边变了，片要重切
        }
        await PersistAsync();
        await PersistStatsAsync();
    }

    // ==================== 量化：底账 → 联系 ====================

    /// <summary>这个角色的底账（调用方必须已持锁）。</summary>
    private RelationStats StatsLocked(string charId)
    {
        if (!_stats.TryGetValue(charId, out var st)) _stats[charId] = st = new RelationStats();
        return st;
    }

    public async Task<RelationStats> StatsAsync(string charId)
    {
        if (string.IsNullOrWhiteSpace(charId)) return new RelationStats();
        await EnsureStatsLoadedAsync();
        lock (_gate)
        {
            var st = StatsLocked(charId);
            return new RelationStats
            {
                Version = st.Version,
                Turns = st.Turns,
                DocFreq = new Dictionary<string, int>(st.DocFreq, StringComparer.Ordinal),
                Cooc = new Dictionary<string, int>(st.Cooc, StringComparer.Ordinal)
            };
        }
    }

    /// <summary>
    /// 把底账里「真的有关」的那些对子织进网里：共现计数只说明「一起出现过」，
    /// nPMI 才说明「一起出现的频率高过碰巧」。两个都够格才落一条 quant 边，权重由关联强度算。
    /// 人工/AI 定过的边一律不覆盖 —— 人说了算的地方，统计不许插手。
    /// </summary>
    private int WeaveLocked(string charId, RelationGraph graph, RelationStatsIndex? alsoSimilar = null)
    {
        var st = StatsLocked(charId);
        if (st.Turns < 2 || st.Cooc.Count == 0) return 0;
        var index = alsoSimilar ?? st.Index();
        var idOf = WordIdsLocked(graph);

        var written = 0;
        foreach (var row in index.TopPairs(MaxWeavePerPairPass, WeaveFloor))
        {
            if (!idOf.TryGetValue(row.A, out var a) || !idOf.TryGetValue(row.B, out var b)) continue;
            if (a == b) continue;
            var weight = RelationMath.ToWeight(row.Association);
            if (UpsertQuantEdgeLocked(graph, a, b, weight)) written++;
        }
        if (alsoSimilar is not null) written += WeaveSimilarLocked(index, graph, idOf);
        return written;
    }

    /// <summary>网里已有的词节点：说法 → 节点 Id（调用方必须已持锁）。</summary>
    private static Dictionary<string, string> WordIdsLocked(RelationGraph graph)
    {
        var idOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var n in graph.Nodes)
        {
            if (n.Kind != NodeKind.Word) continue;
            idOf[n.Label] = n.Id;
            idOf.TryAdd(RelationIds.For(NodeKind.Word, n.Label), n.Id);
            // 量化那条路递回来的说法是「把空白抹平后」的形式，别名留着才认得出来
            idOf.TryAdd(EmbeddingService.Normalize(n.Label), n.Id);
        }
        return idOf;
    }

    /// <summary>
    /// 语境相同 = 有关系：两个词哪怕从没同框出现，只要身边老是同一批词，人也会把它们归成一类
    /// （分布语义假说，Firth 1957）。共现边只看得见「同框」，这一遍补的是「同场景」。
    /// 只在用户按「再织密一遍」时用：全对比较贵，每轮对话跑不起。
    /// </summary>
    private static int WeaveSimilarLocked(RelationStatsIndex index, RelationGraph graph, Dictionary<string, string> idOf)
    {
        var terms = index.Frequencies.Keys
            .Where(idOf.ContainsKey)
            .OrderByDescending(t => index.DocFreq(t))
            .ThenBy(t => t, StringComparer.Ordinal)
            .Take(SimilarTerms)
            .Select(t => (Term: t, Id: idOf[t], Ctx: index.Context(t)))
            .Where(x => x.Ctx.Count >= 2)
            .ToList();

        var linked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in graph.Edges) linked.Add(PruneKey(e.From, e.To));

        var written = 0;
        for (var i = 0; i < terms.Count && written < MaxWeavePerPairPass; i++)
            for (var j = i + 1; j < terms.Count; j++)
            {
                var cos = RelationMath.Cosine(terms[i].Ctx, terms[j].Ctx);
                if (cos < SimilarFloor) continue;
                var key = PruneKey(terms[i].Id, terms[j].Id);
                if (linked.Contains(key)) continue;         // 已经同框连过了，不重复画一条
                if (UpsertQuantEdgeLocked(graph, terms[i].Id, terms[j].Id, RelationMath.ToWeight(cos * 0.6)))
                {
                    linked.Add(key);
                    written++;
                }
            }
        return written;
    }

    /// <summary>写/抬一条量化边；返回是否真的动了。已存在的人工/AI 边原样保留。</summary>
    private static bool UpsertQuantEdgeLocked(RelationGraph graph, string a, string b, double weight)
    {
        if (IsPrunedLocked(graph, a, b)) return false;
        var hit = false;
        var changed = false;
        for (var i = 0; i < graph.Edges.Count; i++)
        {
            var e = graph.Edges[i];
            if (!((e.From == a && e.To == b) || (e.From == b && e.To == a))) continue;
            hit = true;
            if (e.Source is "human" or "ai") return false;     // 人定过的：统计闭嘴
            if (e.Source == "quant" && Math.Abs(e.Weight - weight) < 0.05) continue;
            graph.Edges[i] = e with { Weight = Math.Clamp(weight, 0.1, 500), Source = "quant", UpdatedAt = DateTime.UtcNow };
            changed = true;
        }
        if (!hit)
        {
            graph.Edges.Add(new RelationEdge
            {
                From = a, To = b, Kind = RelationKind.Associated,
                Weight = Math.Clamp(weight, 0.1, 500), Source = "quant"
            });
            return true;
        }
        return changed;
    }

    /// <summary>整张网重算一遍底账：把记忆库里存过的每一轮对话当作一轮，重新统计共现。</summary>
    public async Task<int> RebuildStatsAsync(string charId)
    {
        if (string.IsNullOrWhiteSpace(charId)) return 0;
        await EnsureLoadedAsync();
        await EnsureStatsLoadedAsync();

        var turns = new List<List<string>>();
        try
        {
            var vault = await _store.Load<List<MemoryEntry>>(_scope.Key("memories"));
            if (vault is not null)
                foreach (var m in vault.Where(m => m.CharacterId == charId))
                {
                    var t = ExtractTerms(m.Content ?? "");
                    if (t.Count >= 2) turns.Add(t);
                }
            foreach (var e in await _human.AllAsync(charId))
            {
                var t = ExtractTerms(e.Text ?? "");
                if (t.Count >= 2) turns.Add(t);
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("RelationNetwork.RebuildStats -> " + ex.Message);
            return 0;
        }

        lock (_gate)
        {
            var st = StatsLocked(charId);
            st.DocFreq.Clear();
            st.Cooc.Clear();
            st.Turns = 0;
            foreach (var t in turns) st.Observe(t);
        }
        await PersistStatsAsync();
        App.WriteLog($"RelationNetwork: 底账重算完，{turns.Count} 轮");
        return turns.Count;
    }

    /// <summary>
    /// 底账空着就先补一遍。
    ///
    /// 老存档和脚本灌进来的记忆从没被逐轮统计过：网里的点是迁移时搬过来的，共现底账却是零。
    /// 那时「按数值找近邻」「按数值补线」必然一条也答不上 —— 而用户看到的会是「这个说法还没攒够语境」，
    /// 一句听上去很合理、其实把他支走的话。补账只花本地 CPU，不花模型钱，所以首次问到就顺手补掉，
    /// 而不是等他自己去找一个叫不出名字的钮。每个角色在一次程序运行里只补一次。
    /// </summary>
    private async Task HealEmptyStatsAsync(string charId)
    {
        bool heal;
        lock (_gate)
        {
            heal = StatsLocked(charId).Turns == 0 && _statsHealed.Add(charId);
        }
        if (!heal) return;
        var turns = await RebuildStatsAsync(charId);
        App.WriteLog($"RelationNetwork: 「{charId}」底账是空的，按记得的事补了一遍（{turns} 轮）");
    }

    /// <summary>
    /// 把网再织密一遍（页面「重新摊网」和后台整理都走这里）。
    /// 共现那批证据照旧免费落边；「从没同框、但说的是同一类东西」这一遍优先让量化模型按数值判，
    /// 模型没答上才用稀疏语境向量比对。
    /// </summary>
    public async Task<int> WeaveAsync(string charId)
    {
        if (string.IsNullOrWhiteSpace(charId)) return 0;
        await EnsureLoadedAsync();
        await EnsureStatsLoadedAsync();
        await HealEmptyStatsAsync(charId);

        List<string> terms;
        lock (_gate) terms = VectorTermsLocked(charId);
        Dictionary<string, float[]> vectors = terms.Count > 1 ? await _embed.MapAsync(terms) : new();
        // 夹角全是纯乘法，不碰锁也不碰盘：先在外面算完，回到锁里只做「写边」这一件事
        List<(string A, string B, double Cos)> near = vectors.Count > 1 ? NearestPairsByVector(vectors) : new();

        int written;
        lock (_gate)
        {
            var graph = GraphLocked(charId);
            if (near.Count > 0)
            {
                LastQuantBackend = QuantBackend.Embedding;
                written = WeaveLocked(charId, graph);                                   // 共现证据（不要钱）
                written += WriteQuantPairsLocked(graph, WordIdsLocked(graph), near);     // 模型说的近义
            }
            else
            {
                LastQuantBackend = QuantBackend.Statistics;
                written = WeaveLocked(charId, graph, StatsLocked(charId).Index());       // 共现 + 稀疏语境
            }
            TrimLocked(graph);
            _nets.Remove(charId);
        }
        if (written > 0) await PersistAsync();
        return written;
    }

    /// <summary>量化模型这一遍比哪些说法：底账里最常用的那批（和稀疏那条路同一个上限）。调用方必须已持锁。</summary>
    private List<string> VectorTermsLocked(string charId)
    {
        var index = StatsLocked(charId).Index();
        return index.Frequencies.Keys
            .OrderByDescending(t => index.DocFreq(t))
            .ThenBy(t => t, StringComparer.Ordinal)
            .Take(SimilarTerms)
            .Select(t => EmbeddingService.Normalize(t))
            .Where(t => t.Length > 0)
            .ToList();
    }

    /// <summary>
    /// 稠密向量之间的「谁挨着谁」：每个说法只留最挨着的前 <see cref="VectorNearestPerTerm"/> 个，
    /// 一对说法只记一次（A 挨着 B，B 就不必再挨着 A 一遍）。
    /// </summary>
    private static List<(string A, string B, double Cos)> NearestPairsByVector(Dictionary<string, float[]> vectors)
    {
        var named = vectors.Keys.ToList();
        var pairs = new List<(string, string, double)>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < named.Count; i++)
        {
            var mine = vectors[named[i]];
            var row = new List<(string Term, double Cos)>();
            for (var j = 0; j < named.Count; j++)
            {
                if (i == j) continue;
                var cos = EmbeddingService.Cosine(mine, vectors[named[j]]);
                // 织进网的门槛比页面看近邻高：画出来的线要经得起看，宁可少连也别把网连成一团
                if (cos >= SimilarFloor) row.Add((named[j], cos));
            }
            foreach (var other in row.OrderByDescending(x => x.Cos)
                         .ThenBy(x => x.Term, StringComparer.Ordinal).Take(VectorNearestPerTerm))
            {
                var key = RelationStatsIndex.PairKey(named[i], other.Term);
                if (!taken.Add(key)) continue;
                pairs.Add((named[i], other.Term, other.Cos));
                if (pairs.Count >= MaxWeavePerPairPass) return pairs;
            }
        }
        return pairs;
    }

    /// <summary>把量化模型认定的近义对写进网（只写已有节点之间的；人工/AI 定过的边不动）。</summary>
    private static int WriteQuantPairsLocked(RelationGraph graph, Dictionary<string, string> idOf,
        List<(string A, string B, double Cos)> pairs)
    {
        var written = 0;
        foreach (var p in pairs)
        {
            if (!idOf.TryGetValue(p.A, out var a) || !idOf.TryGetValue(p.B, out var b) || a == b) continue;
            if (UpsertQuantEdgeLocked(graph, a, b, RelationMath.ToWeight(p.Cos * 0.6))) written++;
        }
        return written;
    }

    /// <summary>
    /// 量化模型当知识库管理员：把一个说法（或一整句记得的事）换成数值，再按数值找出最像它的一批说法。
    /// 全文检索问的是「这句话里有这个词吗」，这里问的是「谁和它活得像同一类东西」——
    /// 从没同框出现过的近义词，只有语境向量看得见。
    /// 整句本身不在底账里（底账记的是词），所以从句子里取词逐个问，再合并：
    /// 人选中一条记忆时想知道「这件事和哪些说法挨得近」，不是「这句话有没有近义词」。
    /// 配上量化模型时这一句也能直接整段换成向量（统计那条路做不到），两条路的近邻都并进来排。
    /// </summary>
    public async Task<List<QuantMatch>> NearestForAsync(string charId, string labelOrText, int k = 8)
    {
        if (string.IsNullOrWhiteSpace(charId) || string.IsNullOrWhiteSpace(labelOrText)) return new();
        await EnsureStatsLoadedAsync();
        await HealEmptyStatsAsync(charId);
        var t = labelOrText.Trim();
        List<string> seeds;
        List<string> candidates;
        lock (_gate)
        {
            var index = StatsLocked(charId).Index();
            seeds = index.DocFreq(t) > 0 ? new List<string> { t } : ExtractTerms(t).Take(4).ToList();
            if (seeds.Count == 0) seeds = new List<string> { t };
            candidates = QuantCandidatesLocked(charId, index);
        }

        var byModel = await NearestByVectorAsync(charId, t, seeds, candidates, k);
        if (byModel.Count > 0) return byModel;

        // 模型没配上 / 一条也没算出来：本地统计接着答，这条钮不能按下去没反应
        LastQuantBackend = QuantBackend.Statistics;
        RelationStatsIndex sparse;
        lock (_gate) sparse = StatsLocked(charId).Index();
        var merged = new Dictionary<string, QuantMatch>(StringComparer.Ordinal);
        foreach (var seed in seeds)
            foreach (var (term, cos) in sparse.Nearest(seed, Math.Max(1, k)))
            {
                if (string.Equals(term, seed, StringComparison.Ordinal)) continue;
                var row = new QuantMatch(term, cos, sparse.Npmi(seed, term), sparse.DocFreq(term), seed);
                if (!merged.TryGetValue(term, out var old) || old.Cosine < row.Cosine) merged[term] = row;
            }
        return merged.Values.OrderByDescending(x => x.Cosine)
            .ThenBy(x => x.Term, StringComparer.Ordinal).Take(Math.Max(1, k)).ToList();
    }

    /// <summary>量化候选：底账里最常用的那批，再补上网里已有的词节点。调用方必须已持锁。</summary>
    private List<string> QuantCandidatesLocked(string charId, RelationStatsIndex index)
    {
        var picked = index.Frequencies.Keys
            .OrderByDescending(t => index.DocFreq(t))
            .ThenBy(t => t, StringComparer.Ordinal)
            .Take(QuantCandidates)
            .ToList();
        if (picked.Count >= QuantCandidates || !_cache.TryGetValue(charId, out var graph)) return picked;
        foreach (var n in graph.Nodes)
        {
            if (n.Kind != NodeKind.Word || n.Label.Length == 0) continue;
            if (picked.Contains(n.Label, StringComparer.Ordinal)) continue;
            picked.Add(n.Label);
            if (picked.Count >= QuantCandidates) break;
        }
        return picked;
    }

    /// <summary>
    /// 问量化模型：种子说法（连同整句）和候选说法一起换成数值，按稠密余弦取最挨着的 k 个。
    /// 算不出任何东西就返回空列表，让 <see cref="NearestForAsync"/> 退回本地统计 —— 别在这儿抛异常。
    /// </summary>
    private async Task<List<QuantMatch>> NearestByVectorAsync(string charId, string whole,
        IReadOnlyList<string> seeds, IReadOnlyList<string> candidates, int k)
    {
        if (candidates.Count == 0 || !_embed.IsConfigured) return new();
        var vectors = await _embed.MapAsync(seeds.Append(whole).Concat(candidates));
        if (vectors.Count < 2) return new();

        // 有向量的那几个种子才配当提问的一方（整句也算一个，它不在底账里）
        var asking = new List<(string Seed, string Key, float[] Vec)>();
        foreach (var seed in seeds.Append(whole))
        {
            var key = EmbeddingService.Normalize(seed);
            if (key.Length == 0 || !vectors.TryGetValue(key, out var v)) continue;
            if (asking.Any(a => a.Key == key)) continue;
            asking.Add((seed, key, v));
        }
        if (asking.Count == 0) return new();

        RelationStatsIndex index;
        lock (_gate) index = StatsLocked(charId).Index();

        var merged = new Dictionary<string, QuantMatch>(StringComparer.Ordinal);
        foreach (var term in candidates)
        {
            var key = EmbeddingService.Normalize(term);
            if (!vectors.TryGetValue(key, out var tv)) continue;
            foreach (var (seed, sKey, sv) in asking)
            {
                if (string.Equals(sKey, key, StringComparison.Ordinal)) continue;
                var cos = EmbeddingService.Cosine(sv, tv);
                if (cos < VectorFloor) continue;
                // Via 记的是「靠哪个说法算出来的」，页面点「牵一条线」就拿它当一端的词 ——
                // 短句在前、整句在后，同一个近邻两边都得平时留下先那个（用词牵线比用整句牵线像话）。
                var row = new QuantMatch(term, cos, index.Npmi(seed, term), index.DocFreq(term), seed);
                if (!merged.TryGetValue(term, out var old) || old.Cosine < row.Cosine) merged[term] = row;
            }
        }
        if (merged.Count == 0) return new();
        LastQuantBackend = QuantBackend.Embedding;
        return merged.Values.OrderByDescending(x => x.Cosine)
            .ThenBy(x => x.Term, StringComparer.Ordinal).Take(Math.Max(1, k)).ToList();
    }

    /// <summary>
    /// 按数值牵一条线：两个说法之间照语境余弦落一条 quant 边，网里还没有的顺手建成节点。
    /// 量化模型不能只在后台记账 —— 用户按一下就看得见它连出来的线，才算它真的在当这个库的管理员。
    /// 人工/AI 定过的边照旧不覆盖。
    /// </summary>
    public async Task<bool> WeavePairAsync(string charId, string a, string b)
    {
        if (string.IsNullOrWhiteSpace(charId)) return false;
        var ta = a.Trim();
        var tb = b.Trim();
        if (ta.Length == 0 || tb.Length == 0 || string.Equals(ta, tb, StringComparison.Ordinal)) return false;
        await EnsureLoadedAsync();
        await EnsureStatsLoadedAsync();

        // 这两个说法的数值各自是什么，问量化模型；问不着就用语境向量比
        var vectors = await _embed.MapAsync(new[] { ta, tb });
        var ka = EmbeddingService.Normalize(ta);
        var kb = EmbeddingService.Normalize(tb);
        double? byModel = vectors.TryGetValue(ka, out var va) && vectors.TryGetValue(kb, out var vb)
            ? EmbeddingService.Cosine(va, vb)
            : null;

        bool changed;
        lock (_gate)
        {
            var graph = GraphLocked(charId);
            double cos;
            if (byModel is { } m)
            {
                cos = m;
                LastQuantBackend = QuantBackend.Embedding;
            }
            else
            {
                cos = StatsLocked(charId).Index().Cosine(ta, tb);
                LastQuantBackend = QuantBackend.Statistics;
            }
            // 用户点名要连的两个说法：哪怕数值上还不算近，也按「至少算一类」的下限连上，
            // 但权重跟着真实余弦走，连上之后它在线里就是细的，一眼看得出是勉强牵的手。
            var ia = NodeIdForWord(graph, ta, "quant");
            var ib = NodeIdForWord(graph, tb, "quant");
            changed = UpsertQuantEdgeLocked(graph, ia, ib, RelationMath.ToWeight(Math.Max(cos, SimilarFloor) * 0.6));
            TrimLocked(graph);
            _nets.Remove(charId);
        }
        if (changed) await PersistAsync();
        return changed;
    }

    // ==================== 多张网 ====================

    /// <summary>
    /// 切成几片网：标签传播跑出来的结果按角色缓存着，边一改就作废。
    /// 一张网不该只有一个中心 —— 记得的事本来就是一堆各自成坨、互相拽着的片。
    /// </summary>
    public async Task<List<RelationNet>> NetsAsync(string charId)
    {
        await EnsureLoadedAsync();
        lock (_gate)
        {
            if (_nets.TryGetValue(charId, out var cached)) return cached;
            var nets = RelationNets.Cluster(GraphLocked(charId));
            _nets[charId] = nets;
            return nets;
        }
    }

    private Task EnsureStatsLoadedAsync()
    {
        lock (_gate) return _statsLoading ??= LoadStatsAsync();
    }

    private async Task LoadStatsAsync()
    {
        lock (_gate) _statsFaulted = false;
        try
        {
            var saved = await _store.Load<Dictionary<string, RelationStats>>(_statKey);
            if (saved is null) return;
            lock (_gate)
            {
                foreach (var kv in saved)
                {
                    if (kv.Value is null) continue;
                    _stats[kv.Key] = new RelationStats
                    {
                        Version = kv.Value.Version,
                        Turns = kv.Value.Turns,
                        DocFreq = new Dictionary<string, int>(kv.Value.DocFreq, StringComparer.Ordinal),
                        Cooc = new Dictionary<string, int>(kv.Value.Cooc, StringComparer.Ordinal)
                    };
                }
            }
        }
        catch (Exception ex)
        {
            // 底账读不出来时 _stats 是空的，落盘就等于把这一档攒了很久的共现计数换成这一轮的。
            lock (_gate) { _statsFaulted = true; _statsLoading = null; }
            App.WriteLog("RelationNetwork.LoadStats -> " + ex.Message);
        }
    }

    private Task PersistStatsAsync()
    {
        Dictionary<string, RelationStats> snapshot;
        lock (_gate)
        {
            if (_statsFaulted)
            {
                App.WriteLog("RelationNetwork.PersistStats -> 已跳过落盘：这一档的共现底账没能读出来，不拿空表盖盘");
                return Task.CompletedTask;
            }
            snapshot = _stats.ToDictionary(kv => kv.Key,
                kv => new RelationStats
                {
                    Version = kv.Value.Version,
                    Turns = kv.Value.Turns,
                    DocFreq = new Dictionary<string, int>(kv.Value.DocFreq, StringComparer.Ordinal),
                    Cooc = new Dictionary<string, int>(kv.Value.Cooc, StringComparer.Ordinal)
                }, StringComparer.Ordinal);
        }
        return _store.Save(_statKey, snapshot);
    }


    /// <summary>建或加强一条联系（人工/AI 纠正过的边不会被自动累积冲淡）。</summary>
    public async Task LinkAsync(string charId, string a, string b, double delta = 3,
        string source = "human", RelationKind kind = RelationKind.Associated)
    {
        if (string.IsNullOrWhiteSpace(charId) || string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return;
        await EnsureLoadedAsync();
        lock (_gate)
        {
            var graph = GraphLocked(charId);
            var from = NodeIdForWord(graph, a, source);
            var to = NodeIdForWord(graph, b, source);
            UnpruneLocked(graph, from, to);
            BumpEdgeLocked(graph, from, to, kind, delta, source, manual: true);
            // 等价是双向的：只存一个方向，读到「老妈」就扩散不到「妈妈」
            if (kind == RelationKind.Equivalent) BumpEdgeLocked(graph, to, from, kind, delta, source, manual: true);
        }
        await PersistAsync();
    }

    /// <summary>直接按节点 Id 连（界面上点两个节点建立联系；Id 已经是 p:/t:/m: 前缀）。</summary>
    public async Task LinkNodesAsync(string charId, string fromId, string toId, RelationKind kind,
        double delta = 3, string source = "human")
    {
        if (string.IsNullOrWhiteSpace(fromId) || string.IsNullOrWhiteSpace(toId) || fromId == toId) return;
        await EnsureLoadedAsync();
        lock (_gate)
        {
            var graph = GraphLocked(charId);
            UnpruneLocked(graph, fromId, toId);
            BumpEdgeLocked(graph, fromId, toId, kind, delta, source, manual: true);
            if (kind == RelationKind.Equivalent) BumpEdgeLocked(graph, toId, fromId, kind, delta, source, manual: true);
        }
        await PersistAsync();
    }

    /// <summary>断开两个词之间的全部联系（人为纠正「这两个词其实没关系」）。</summary>
    public async Task UnlinkAsync(string charId, string a, string b)
    {
        await EnsureLoadedAsync();
        var ka = RelationIds.For(NodeKind.Word, a);
        var kb = RelationIds.For(NodeKind.Word, b);
        lock (_gate)
        {
            if (!_cache.TryGetValue(charId, out var graph)) return;
            RemoveEdgesLocked(graph, e => (e.From == ka && e.To == kb) || (e.From == kb && e.To == ka));
            graph.Nodes.RemoveAll(n => IsOrphan(n, graph));
        }
        await PersistAsync();
    }

    public async Task RemoveEdgeAsync(string charId, string fromId, string toId, RelationKind kind)
    {
        await EnsureLoadedAsync();
        lock (_gate)
        {
            if (!_cache.TryGetValue(charId, out var graph)) return;
            RemoveEdgesLocked(graph, e =>
                (e.From == fromId && e.To == toId && e.Kind == kind) ||
                (kind == RelationKind.Equivalent && e.From == toId && e.To == fromId && e.Kind == kind));
            graph.Nodes.RemoveAll(n => IsOrphan(n, graph));
        }
        await PersistAsync();
    }

    /// <summary>断开两个概念之间的**全部**联系（界面上点「断开这条」、AI 判定这层关系是噪音时用；不分方向、不分类型）。</summary>
    public async Task<bool> RemoveBetweenAsync(string charId, string fromId, string toId)
    {
        await EnsureLoadedAsync();
        var removed = 0;
        lock (_gate)
        {
            if (!_cache.TryGetValue(charId, out var graph)) return false;
            removed = RemoveEdgesLocked(graph, e =>
                (e.From == fromId && e.To == toId) || (e.From == toId && e.To == fromId));
            graph.Nodes.RemoveAll(n => IsOrphan(n, graph));
        }
        if (removed > 0) await PersistAsync();
        return removed > 0;
    }

    /// <summary>把两个概念之间那条联系的权重直接改成给定值（人工纠错「哪有这么强/这么弱」；AI 用来调弱虚高的边）。</summary>
    public async Task<bool> SetEdgeWeightAsync(string charId, string fromId, string toId, double weight, string source = "human")
    {
        await EnsureLoadedAsync();
        var hit = false;
        lock (_gate)
        {
            if (!_cache.TryGetValue(charId, out var graph)) return false;
            for (var i = 0; i < graph.Edges.Count; i++)
            {
                var e = graph.Edges[i];
                if (!((e.From == fromId && e.To == toId) || (e.From == toId && e.To == fromId))) continue;
                graph.Edges[i] = e with { Weight = Math.Clamp(weight, 0.1, 500), Source = source, UpdatedAt = DateTime.UtcNow };
                hit = true;
            }
            if (!hit && fromId != toId && graph.Nodes.Any(n => n.Id == fromId) && graph.Nodes.Any(n => n.Id == toId))
            {
                UnpruneLocked(graph, fromId, toId);
                graph.Edges.Add(new RelationEdge
                {
                    From = fromId, To = toId, Kind = RelationKind.Associated,
                    Weight = Math.Clamp(weight, 0.1, 500), Source = source
                });
                hit = true;
            }
        }
        if (hit) await PersistAsync();
        return hit;
    }

    /// <summary>删掉一个节点（连带它的所有边）。</summary>
    public async Task RemoveNodeAsync(string charId, string nodeId)
    {
        await EnsureLoadedAsync();
        lock (_gate)
        {
            if (!_cache.TryGetValue(charId, out var graph)) return;
            graph.Nodes.RemoveAll(n => n.Id == nodeId);
            graph.Edges.RemoveAll(e => e.From == nodeId || e.To == nodeId);
            graph.Pruned.RemoveAll(k => k.StartsWith(nodeId + "‖", StringComparison.Ordinal)
                                        || k.EndsWith("‖" + nodeId, StringComparison.Ordinal));
        }
        await PersistAsync();
    }

    /// <summary>直接设定一条联系的权重（人工纠错：太强/太弱都改得了）。</summary>
    public async Task CorrectAsync(string charId, string a, string b, double weight, string source = "human")
    {
        await EnsureLoadedAsync();
        var ka = RelationIds.For(NodeKind.Word, a);
        var kb = RelationIds.For(NodeKind.Word, b);
        lock (_gate)
        {
            var graph = GraphLocked(charId);
            RemoveEdgesLocked(graph, e => (e.From == ka && e.To == kb) || (e.From == kb && e.To == ka));
            if (weight > 0)
            {
                UnpruneLocked(graph, ka, kb);
                NodeIdForWord(graph, a, source);
                NodeIdForWord(graph, b, source);
                graph.Edges.Add(new RelationEdge
                {
                    From = ka, To = kb, Kind = RelationKind.Associated,
                    Weight = Math.Clamp(weight, 0.1, 500), Source = source
                });
            }
        }
        await PersistAsync();
    }

    // ==================== 记忆同步 ====================

    /// <summary>
    /// 把「记得的事 + 标签 + 情绪 + 照片 + 句子里的词」摊进网里：一条记忆是一个节点，
    /// 它身上的标签、它的情绪配比、它的照片、它说到的词是邻居。
    ///
    /// 全部走 <see cref="SetAutoEdgeLocked"/>（设绝对值）而不是累加：这个方法是每次进页面都要跑的，
    /// 累加一次涨一次，看半小时网就等于把粗细交给「我刷新了几次」。
    /// 情绪按配比给权重——安心五成和想念一成落在图上必须是两种粗细，
    /// 否则「复合情绪」只是个存在字段里的字符串，画出来还是一颗孤点。
    /// </summary>
    public async Task SyncFromMemoriesAsync(string charId, int maxEntries = 120)
    {
        if (string.IsNullOrWhiteSpace(charId)) return;
        await EnsureLoadedAsync();
        var entries = (await _human.AllAsync(charId)).Take(maxEntries).ToList();
        var subjects = (await _tags.ListAsync(charId))
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Subject, StringComparer.OrdinalIgnoreCase);

        lock (_gate)
        {
            var graph = GraphLocked(charId);
            var wordIds = new HashSet<string>(
                graph.Nodes.Where(n => n.Kind == NodeKind.Word).Select(n => n.Id), StringComparer.Ordinal);

            foreach (var e in entries)
            {
                var first = HumanMemoryMath.SplitSentences(e.Text).FirstOrDefault() ?? e.Text;
                var label = first.Length <= 14 ? first : first[..14];
                var mem = UpsertNodeLocked(graph, NodeKind.Memory, e.Id, label,
                    Math.Clamp(e.Importance, 0.05, 1.0), "auto", e.Id);

                if (!string.IsNullOrWhiteSpace(e.ImagePath))
                {
                    var img = UpsertNodeLocked(graph, NodeKind.Image, e.ImagePath, label, 0.6, "auto", e.ImagePath);
                    SetAutoEdgeLocked(graph, mem, img, RelationKind.Spatial, 3.5 + e.RecallCount * 0.1);
                }

                var tagIds = new List<string>();
                foreach (var mark in e.SentenceTags.Take(6))
                    foreach (var tag in mark.Tags.Take(4))
                    {
                        var kind = KindForSubject(subjects.TryGetValue(tag, out var s) ? s : "event");
                        var tagNode = UpsertNodeLocked(graph, kind, tag, tag,
                            0.3 + Math.Clamp(e.Importance * 0.3, 0, 0.3), "auto");
                        if (tagIds.Contains(tagNode)) continue;
                        tagIds.Add(tagNode);
                        SetAutoEdgeLocked(graph, mem, tagNode, RelationKind.BelongsTo,
                            2 + e.Importance * 3 + Math.Min(2, e.RecallCount * 0.2));
                        // 同一个名字既是标签也是聊天里出现过的词：这是真正的「等价」，合并成一个概念
                        var wordId = RelationIds.For(NodeKind.Word, tag);
                        if (!wordIds.Contains(wordId)) continue;
                        SetAutoEdgeLocked(graph, tagNode, wordId, RelationKind.Equivalent, 4.5);
                        SetAutoEdgeLocked(graph, wordId, tagNode, RelationKind.Equivalent, 4.5);
                    }

                if (e.Emotion is { IsEmpty: false } mix)
                    foreach (var p in mix.Parts)
                    {
                        var em = UpsertNodeLocked(graph, NodeKind.Emotion, p.Name, p.Name,
                            Math.Clamp(0.25 + p.Ratio * 0.7, 0.05, 1.0), "auto");
                        // 占比直接换成粗细：五成和一两成必须看得出差别
                        SetAutoEdgeLocked(graph, mem, em, RelationKind.Emotional, 1.5 + 7 * p.Ratio);
                    }

                // 这件事说到的词：事件不再是一颗孤点，它由这些说法组成，也顺着这些说法被想起来
                foreach (var term in ExtractTerms(e.Text).Distinct(StringComparer.Ordinal).Take(5))
                {
                    var id = RelationIds.For(NodeKind.Word, term);
                    if (!wordIds.Contains(id)) continue;
                    SetAutoEdgeLocked(graph, mem, id, RelationKind.Associated, 1.2 + e.Importance * 2.5);
                }

                // 同一件事里一起被记下的标签，本身也算「一起过」：网要紧密，不该只有记忆连着标签
                for (var i = 0; i < tagIds.Count; i++)
                    for (var j = i + 1; j < tagIds.Count; j++)
                        SetAutoEdgeLocked(graph, tagIds[i], tagIds[j], RelationKind.Temporal, 1.5 + e.Importance * 1.5);
            }
            TrimLocked(graph);
            _nets.Remove(charId);
        }
        await PersistAsync();
    }

    private static NodeKind KindForSubject(string subject) => subject switch
    {
        "person" => NodeKind.Person,
        "place" => NodeKind.Place,
        "thing" => NodeKind.Thing,
        _ => NodeKind.Tag
    };

    // ==================== 读出 ====================

    /// <summary>画布与 JSON 视图用的整张网（返回副本，改不坏缓存）。</summary>
    public async Task<RelationGraph> SnapshotAsync(string charId)
    {
        await EnsureLoadedAsync();
        lock (_gate)
        {
            var g = _cache.TryGetValue(charId, out var found) ? found : new RelationGraph();
            return new RelationGraph
            {
                Version = g.Version,
                Nodes = g.Nodes.ToList(),
                Edges = g.Edges.ToList(),
                Pruned = g.Pruned.ToList()
            };
        }
    }

    /// <summary>存盘用的 JSON 原文（缩进版）。界面上的 JSON 视图显示的就是这份东西。</summary>
    public async Task<string> ToJsonAsync(string charId)
    {
        var graph = await SnapshotAsync(charId);
        return JsonSerializer.Serialize(graph, ShowJson);
    }

    /// <summary>中文不转义成 \uXXXX：这张网要能直接读懂、改对，不然「存成 JSON 给人看」就是空话。</summary>
    public static readonly JsonSerializerOptions ShowJson = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // ==================== 参数模型 ====================

    /// <summary>这张网的算法旋钮（没调过就是默认值）。扩散检索与画面衰减共用这一份。</summary>
    public async Task<RelationParams> ParamsAsync(string charId)
    {
        if (string.IsNullOrWhiteSpace(charId)) return RelationParams.Default;
        await EnsureParamsLoadedAsync();
        lock (_gate) return _params.TryGetValue(charId, out var p) ? p : RelationParams.Default;
    }

    public async Task SaveParamsAsync(string charId, RelationParams p)
    {
        if (string.IsNullOrWhiteSpace(charId)) return;
        await EnsureParamsLoadedAsync();
        var clean = p.Clamped();
        lock (_gate) _params[charId] = clean;
        try
        {
            Dictionary<string, RelationParams> snapshot;
            lock (_gate)
            {
                if (_paramsFaulted)
                {
                    App.WriteLog("RelationNetwork.SaveParams -> 已跳过落盘：参数表没能读出来，不拿单张改动的覆写整份");
                    return;
                }
                snapshot = new Dictionary<string, RelationParams>(_params, StringComparer.Ordinal);
            }
            await _store.Save(_paramKey, snapshot);
        }
        catch (Exception ex)
        {
            App.WriteLog("RelationNetwork.SaveParams -> " + ex.Message);
        }
    }

    private Task EnsureParamsLoadedAsync()
    {
        lock (_gate) return _paramsLoading ??= LoadParamsAsync();
    }

    private async Task LoadParamsAsync()
    {
        lock (_gate) _paramsFaulted = false;
        try
        {
            var saved = await _store.Load<Dictionary<string, RelationParams>>(_paramKey);
            if (saved is null) return;
            lock (_gate)
            {
                foreach (var kv in saved)
                    _params[kv.Key] = kv.Value?.Clamped() ?? RelationParams.Default;
            }
        }
        catch (Exception ex)
        {
            lock (_gate) { _paramsFaulted = true; _paramsLoading = null; }
            App.WriteLog("RelationNetwork.LoadParams -> " + ex.Message);
        }
    }

    /// <summary>联系人视图（列表与自检用）。</summary>
    public async Task<List<LinkView>> TopLinksAsync(string charId, int max = 12)
    {
        var graph = await SnapshotAsync(charId);
        var nameOf = graph.Nodes.ToDictionary(n => n.Id, n => n.Label);
        return graph.Edges
            .Where(e => nameOf.ContainsKey(e.From) && nameOf.ContainsKey(e.To))
            .OrderByDescending(e => e.Weight)
            .Take(Math.Max(1, max))
            .Select(e => new LinkView(nameOf[e.From], nameOf[e.To], e.Weight, e.Source, e.Kind))
            .ToList();
    }

    public async Task<List<LinkView>> NeighborsAsync(string charId, string labelOrId, int max = 12)
    {
        var graph = await SnapshotAsync(charId);
        var id = graph.Nodes.FirstOrDefault(n => n.Id == labelOrId)?.Id
                 ?? graph.Nodes.FirstOrDefault(n => string.Equals(n.Label, labelOrId, StringComparison.OrdinalIgnoreCase))?.Id
                 ?? RelationIds.For(NodeKind.Word, labelOrId);
        var nameOf = graph.Nodes.ToDictionary(n => n.Id, n => n.Label);
        return graph.Edges
            .Where(e => (e.From == id || e.To == id) && nameOf.ContainsKey(e.From) && nameOf.ContainsKey(e.To))
            .OrderByDescending(e => e.Weight)
            .Take(Math.Max(1, max))
            .Select(e => new LinkView(nameOf[e.From], nameOf[e.To], e.Weight, e.Source, e.Kind))
            .ToList();
    }

    public async Task<int> LinkCountAsync(string charId)
    {
        await EnsureLoadedAsync();
        lock (_gate) return _cache.TryGetValue(charId, out var g) ? g.Edges.Count : 0;
    }

    /// <summary>
    /// 联想扩散：从几个种子出发把激活顺边传开（ACT-R 式的扩散激活 + fan effect）。
    ///
    /// 两个心理学设定决定它和「BFS 取前 N 个」不一样：
    /// · 一个概念连得越散，分给每条邻居的激活越少（fan effect：读到「妈妈」时，
    ///   它身上的联系是「摊开」的，不是每条都满格点亮）；
    /// · 等价边几乎不衰减（0.95），对立边只留一点（0.25），
    ///   所以「老妈」能顶上来，而跟它「相反」的词不会误当成同一件事被检索出来。
    /// </summary>
    public async Task<List<(string Term, double Score)>> ExpandAsync(string charId, IEnumerable<string> seeds,
        int maxTerms = 12, int depth = 2)
    {
        var graph = await SnapshotAsync(charId);
        if (graph.Nodes.Count == 0) return new List<(string, double)>();
        var p = await ParamsAsync(charId);
        var now = DateTime.UtcNow;

        // fan effect 里的「扇形」= 这个概念身上挂着多少条联系，不分方向
        var degree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in graph.Edges)
        {
            degree[e.From] = degree.TryGetValue(e.From, out var a) ? a + 1 : 1;
            degree[e.To] = degree.TryGetValue(e.To, out var b) ? b + 1 : 1;
        }
        var labelOf = graph.Nodes.ToDictionary(n => n.Id, n => n.Label);
        var adj = new Dictionary<string, List<(string To, RelationKind Kind, double Weight, DateTime At)>>(StringComparer.Ordinal);
        void AddArc(string from, string to, RelationKind kind, double weight, DateTime at)
        {
            if (!adj.TryGetValue(from, out var arcs)) adj[from] = arcs = new List<(string, RelationKind, double, DateTime)>();
            arcs.Add((to, kind, weight, at));
        }
        foreach (var e in graph.Edges)
        {
            AddArc(e.From, e.To, e.Kind, e.Weight, e.UpdatedAt);
            // 「导致」「先后」有方向：想到原因不会自动点亮结果的反面；其余联系双向都能走
            if (e.Kind != RelationKind.Causal && e.Kind != RelationKind.Temporal)
                AddArc(e.To, e.From, e.Kind, e.Weight, e.UpdatedAt);
        }

        var activation = new Dictionary<string, double>(StringComparer.Ordinal);
        var frontier = new List<string>();
        foreach (var s in seeds.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var id = Resolve(graph, s.Trim());
            if (id is null) continue;
            activation[id] = 1.0;
            frontier.Add(id);
        }
        if (frontier.Count == 0) return new List<(string, double)>();

        for (var d = 0; d < Math.Max(1, depth); d++)
        {
            var hop = Math.Pow(p.HopDamping, d);
            var next = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var from in frontier)
            {
                var a = activation.TryGetValue(from, out var cur) ? cur : 0;
                if (a <= 0) continue;
                if (!adj.TryGetValue(from, out var arcs)) continue;
                var fan = p.FanPenalty(degree.TryGetValue(from, out var f) ? f : 1);
                foreach (var arc in arcs)
                {
                    if (!labelOf.ContainsKey(arc.To)) continue;
                    var give = a * p.Strength(arc.Weight, arc.At, now) * Relations.SpreadFactor(arc.Kind) * fan * hop;
                    if (give <= 0) continue;
                    // 多条路都能走到就累加，不是取最大：人「想起来了」的感觉正是好几条线索同时对上
                    next[arc.To] = (next.TryGetValue(arc.To, out var q) ? q : 0) + give;
                }
            }
            foreach (var kv in next) activation[kv.Key] = (activation.TryGetValue(kv.Key, out var v) ? v : 0) + kv.Value;
            frontier = next.Where(kv => kv.Value >= p.ActivationFloor).Select(kv => kv.Key).ToList();
            if (frontier.Count == 0) break;
        }

        return activation
            .Where(kv => kv.Value >= p.ActivationFloor)
            .OrderByDescending(kv => kv.Value)
            .Take(Math.Max(1, maxTerms))
            .Select(kv => (labelOf[kv.Key], kv.Value * 10))
            .ToList();
    }

    /// <summary>种子可以是节点 Id、词、标签名；实在对不上就退一步按子串找最接近的那个点。</summary>
    private static string? Resolve(RelationGraph graph, string seed)
    {
        if (graph.Nodes.Any(n => n.Id == seed)) return seed;
        var exact = graph.Nodes.FirstOrDefault(n => string.Equals(n.Label, seed, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.Id;
        var wordId = RelationIds.For(NodeKind.Word, seed);
        if (graph.Nodes.Any(n => n.Id == wordId)) return wordId;
        // 「那晚停电」该落到「停电」上：子串互含，取最短的候选（越短越接近本名）
        return graph.Nodes
            .Where(n => !string.IsNullOrWhiteSpace(n.Label) && n.Label.Length >= 2
                        && (seed.Contains(n.Label, StringComparison.OrdinalIgnoreCase)
                            || n.Label.Contains(seed, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(n => n.Label.Length)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .Select(n => n.Id)
            .FirstOrDefault();
    }

    // ==================== 内部：节点与边的累积 ====================

    private string NodeIdForWord(RelationGraph graph, string word, string source)
    {
        var trimmed = word.Trim();
        return UpsertNodeLocked(graph, NodeKind.Word, trimmed, trimmed, 0.3, source);
    }

    /// <summary>同类同名（或同引用）合到一个点上；已存在的点只抬显著性，不改来源标签。</summary>
    private static string UpsertNodeLocked(RelationGraph graph, NodeKind kind, string nameOrRef, string label,
        double salience, string source, string? reference = null)
    {
        var id = RelationIds.For(kind, nameOrRef);
        var at = graph.Nodes.FindIndex(n => n.Id == id);
        if (at >= 0)
        {
            var old = graph.Nodes[at];
            var manual = old.Source != "auto" || source != "auto";
            graph.Nodes[at] = old with
            {
                Label = string.IsNullOrWhiteSpace(old.Label) ? label : old.Label,
                Salience = Math.Clamp(Math.Max(old.Salience, salience), 0.05, 1.0),
                Source = manual ? (old.Source == "auto" ? source : old.Source) : "auto",
                Ref = string.IsNullOrEmpty(old.Ref) ? (reference ?? "") : old.Ref,
                UpdatedAt = DateTime.UtcNow
            };
        }
        else
        {
            graph.Nodes.Add(new RelationNode
            {
                Id = id, Label = label, Kind = kind, Salience = Math.Clamp(salience, 0.05, 1.0),
                Source = source, Ref = reference ?? ""
            });
        }
        return id;
    }

    private static void BumpEdgeLocked(RelationGraph graph, string from, string to, RelationKind kind,
        double delta, string source, bool manual)
    {
        if (from == to) return;
        // 自动累积不越过「人工断过」：否则断开的联系下次同步又长回来
        if (!manual && IsPrunedLocked(graph, from, to)) return;
        var idx = graph.Edges.FindIndex(e => e.From == from && e.To == to && e.Kind == kind);
        if (idx >= 0)
        {
            var old = graph.Edges[idx];
            // 人工/AI 定过的边不接受自动累积：只在人工纠错时改动它
            if (old.Source != "auto" && !manual) return;
            graph.Edges[idx] = old with
            {
                Weight = Math.Clamp(old.Weight + delta, 0.1, 500),
                UpdatedAt = DateTime.UtcNow,
                Source = manual ? source : old.Source
            };
        }
        else
        {
            graph.Edges.Add(new RelationEdge
            {
                From = from, To = to, Kind = kind,
                Weight = Math.Clamp(delta, 0.1, 500), Source = source
            });
        }
    }

    /// <summary>
    /// 同步用的「设绝对值」，不是累加：同一条记忆每次进页面都同步一遍，
    /// 用 BumpEdgeLocked 的话「记得的事→标签」会一路涨到 500，枝条粗得没法看。
    /// 结构边的粗细只该由这件事本身决定（重要性、占比、回想次数）。
    /// </summary>
    private static void SetAutoEdgeLocked(RelationGraph graph, string from, string to, RelationKind kind, double weight)
    {
        if (from == to || IsPrunedLocked(graph, from, to)) return;
        var w = Math.Clamp(weight, 0.1, 500);
        var idx = graph.Edges.FindIndex(e => e.From == from && e.To == to && e.Kind == kind);
        if (idx >= 0)
        {
            var old = graph.Edges[idx];
            if (old.Source != "auto") return;      // 人工/AI 定过的粗细以定过的为准
            if (Math.Abs(old.Weight - w) < 0.01) return;
            graph.Edges[idx] = old with { Weight = w, UpdatedAt = DateTime.UtcNow };
        }
        else
        {
            graph.Edges.Add(new RelationEdge { From = from, To = to, Kind = kind, Weight = w, Source = "auto" });
        }
    }

    /// <summary>删边并记下「这两点之间断过」：名单不分类型、不分方向，断了就是全断。</summary>
    private static int RemoveEdgesLocked(RelationGraph graph, Func<RelationEdge, bool> hit)
    {
        var n = 0;
        for (var i = graph.Edges.Count - 1; i >= 0; i--)
        {
            var e = graph.Edges[i];
            if (!hit(e)) continue;
            graph.Edges.RemoveAt(i);
            var key = PruneKey(e.From, e.To);
            if (!graph.Pruned.Contains(key, StringComparer.Ordinal)) graph.Pruned.Add(key);
            n++;
        }
        if (graph.Pruned.Count > MaxPruned) graph.Pruned.RemoveRange(0, graph.Pruned.Count - MaxPruned);
        return n;
    }

    // 断过的名单用 ‖ 分隔：节点 Id 里可能带 |（图片路径、标签名），用 | 会把两条记录切成三条。
    private static string PruneKey(string a, string b)
        => string.CompareOrdinal(a, b) <= 0 ? a + "‖" + b : b + "‖" + a;

    private static bool IsPrunedLocked(RelationGraph graph, string from, string to)
        => graph.Pruned.Count > 0 && graph.Pruned.Contains(PruneKey(from, to), StringComparer.Ordinal);

    /// <summary>人工重新连上 = 撤销那次「断过」，否则用户连完下次同步还是看不见这条线。</summary>
    private static void UnpruneLocked(RelationGraph graph, string from, string to)
    {
        if (graph.Pruned.Count == 0) return;
        var key = PruneKey(from, to);
        graph.Pruned.RemoveAll(k => string.Equals(k, key, StringComparison.Ordinal));
    }

    private static bool IsOrphan(RelationNode n, RelationGraph graph)
        => n.Kind == NodeKind.Word && !graph.Edges.Any(e => e.From == n.Id || e.To == n.Id);

    private static void TrimLocked(RelationGraph graph)
    {
        if (graph.Edges.Count <= MaxEdgesPerCharacter) return;
        graph.Edges = graph.Edges.OrderByDescending(e => e.Weight).Take(MaxEdgesPerCharacter).ToList();
        var live = new HashSet<string>(graph.Edges.SelectMany(e => new[] { e.From, e.To }), StringComparer.Ordinal);
        graph.Nodes = graph.Nodes.Where(n => live.Contains(n.Id) || n.Kind != NodeKind.Word).ToList();
    }

    // ==================== 取词 ====================

    /// <summary>取词：按非文字符切段；中文长串再按 2 字滑窗补词；去掉停用词与单字。</summary>
    public static List<string> ExtractTerms(string text)
    {
        var terms = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return terms;

        var buf = new StringBuilder();
        void Flush()
        {
            if (buf.Length >= 2) terms.Add(buf.ToString());
            buf.Clear();
        }
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch)) buf.Append(ch);
            else Flush();
        }
        Flush();

        var windows = new List<string>();
        foreach (var t in terms)
        {
            if (t.Length > 4 && t.Any(IsCjk))
                for (var i = 0; i + 2 <= t.Length; i += 2) windows.Add(t.Substring(i, 2));
        }
        terms.AddRange(windows);

        return terms
            .Where(t => t.Length >= 2 && !StopWords.Contains(t))
            .Distinct(StringComparer.Ordinal)
            .Take(60)
            .ToList();
    }

    private static bool IsCjk(char c) => c >= '\u4E00' && c <= '\u9FFF';
}
