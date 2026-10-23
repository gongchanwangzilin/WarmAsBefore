namespace WarmAsBefore.Models;

/// <summary>底账里抽一行出来给人看：某个说法出现在几轮。</summary>
public sealed record TermStat(string Term, int Turns);

/// <summary>底账里抽一格出来给人看：某对说法一起出现在几轮，以及由此算出的关联强度。</summary>
public sealed record PairStat(string A, string B, int Turns, double Npmi, double Association);

/// <summary>
/// 一个角色的量化底账：轮数 N、每个说法出现在几轮、每对说法一起出现在几轮。
///
/// 为什么要单独存这份东西：光有一条「权重」说明不了任何事。
/// 「玛丽」和「海边」连着 6 次，可能是它俩真的有关，也可能只是因为「玛丽」在 200 轮里出现过 200 次。
/// 人判断关联时会下意识拿「单独出现的可能性」去除（这就是 PMI 做的事），
/// 存了 N、df、共现三样，这个除法才做得出来 —— 否则每次想算都得回头重读全部聊天记录。
///
/// 这份底账就是关系网的数据库层：网里每条自动联系都能指回这里的一格证据。
/// </summary>
public sealed class RelationStats
{
    public int Version { get; set; } = 1;

    /// <summary>观察过多少轮（N）。0 轮时所有概率都没有意义，量化那一步会直接跳过。</summary>
    public int Turns { get; set; }

    /// <summary>说法 → 出现在多少轮里（document frequency）。</summary>
    public Dictionary<string, int> DocFreq { get; set; } = new(StringComparer.Ordinal);

    /// <summary>「a｜b」（a、b 已按序排好）→ 一起出现在多少轮里。一轮里只加 1，不管提了几次。</summary>
    public Dictionary<string, int> Cooc { get; set; } = new(StringComparer.Ordinal);

    /// <summary>词频上限：再多就不是「记不住」而是「查不动」了。</summary>
    public const int MaxTerms = 4000;

    /// <summary>共现格上限：一轮 40 个词能长出 780 格，不设上限这张底账自己就会把网压垮。</summary>
    public const int MaxPairs = 20000;

    /// <summary>一轮里最多取多少个词参与统计（和取词上限对齐）。</summary>
    public const int MaxTermsPerTurn = 40;

    /// <summary>摊成只读索引：概率、nPMI、语境向量都从这里问。</summary>
    public RelationStatsIndex Index() => new(this);

    /// <summary>记一轮：这些说法各自 +1，两两之间的格子 +1。</summary>
    public void Observe(IReadOnlyList<string> terms, int capPerTurn = MaxTermsPerTurn)
    {
        if (terms.Count < 1) return;
        var clean = terms.Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .Take(capPerTurn)
            .ToList();
        if (clean.Count == 0) return;

        Turns++;
        foreach (var t in clean) DocFreq[t] = DocFreq.TryGetValue(t, out var n) ? n + 1 : 1;
        for (var i = 0; i < clean.Count; i++)
            for (var j = i + 1; j < clean.Count; j++)
            {
                var k = RelationStatsIndex.PairKey(clean[i], clean[j]);
                Cooc[k] = Cooc.TryGetValue(k, out var m) ? m + 1 : 1;
            }
        if (DocFreq.Count > MaxTerms) TrimLowest(DocFreq, MaxTerms);
        if (Cooc.Count > MaxPairs) TrimLowest(Cooc, MaxPairs);
    }

    /// <summary>超出上限时丢掉最弱的格子：低频共现本来就多半是巧合。</summary>
    private static void TrimLowest(Dictionary<string, int> table, int keep)
    {
        var doomed = table.OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Skip(keep)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var k in doomed) table.Remove(k);
    }
}

/// <summary>
/// 一次计算用的只读索引。单独一个类型是因为它的生命周期就是「算一轮」，不该混进可序列化的底账里。
/// </summary>
public sealed class RelationStatsIndex
{
    private readonly Dictionary<string, int> _df;
    private readonly Dictionary<string, int> _cooc;
    private readonly int _turns;

    public RelationStatsIndex(RelationStats stats)
    {
        _turns = Math.Max(0, stats.Turns);
        _df = stats.DocFreq;
        _cooc = stats.Cooc;
    }

    public int Turns => _turns;
    public int DocFreq(string term) => _df.TryGetValue(term, out var v) ? v : 0;
    public int Cooc(string a, string b) => _cooc.TryGetValue(PairKey(a, b), out var v) ? v : 0;
    public IReadOnlyDictionary<string, int> Frequencies => _df;
    public IReadOnlyDictionary<string, int> Pairs => _cooc;

    /// <summary>共现矩阵的键：两个词谁在前谁在后必须固定，不然同一格会被写成两条。</summary>
    public static string PairKey(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;

    /// <summary>P(a,b)：这一对一起出现在任意一轮的概率。</summary>
    public double PairP(string a, string b) => _turns <= 0 ? 0 : (double)Cooc(a, b) / _turns;

    public double MarginalP(string term) => _turns <= 0 ? 0 : (double)DocFreq(term) / _turns;

    /// <summary>归一化 PMI，-1~1：+1 = 提到 A 就必提到 B；0 = 各说各的。</summary>
    public double Npmi(string a, string b) => RelationMath.Npmi(PairP(a, b), MarginalP(a), MarginalP(b));

    /// <summary>
    /// 语境向量：把「A 身边都有谁、各有多紧」当成 A 的意思。
    /// 分布语义的老底子（一个词的含义由它的邻居决定），不花钱、不联网也算得出来。
    /// </summary>
    public Dictionary<string, double> Context(string term, int cap = 64)
        => Contexts(cap).TryGetValue(term, out var v) ? v : new Dictionary<string, double>(StringComparer.Ordinal);

    private Dictionary<string, Dictionary<string, double>>? _contexts;

    /// <summary>
    /// 一次扫完底账，把所有说法的语境向量一起算出来。
    /// 逐个调 <see cref="Context"/> 会把底账扫 N 遍：几千个词 × 几万对共现就是几亿次比较，
    /// 按一次钮等到天黑。这里只扫一遍，之后每个词都从这份表里取。
    /// </summary>
    public IReadOnlyDictionary<string, Dictionary<string, double>> Contexts(int cap = 64)
    {
        if (_contexts is not null) return _contexts;
        var all = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
        foreach (var kv in _cooc)
        {
            var bar = kv.Key.IndexOf('|');
            if (bar <= 0) continue;
            var a = kv.Key[..bar];
            var b = kv.Key[(bar + 1)..];
            var s = Math.Max(0, Npmi(a, b));
            if (s <= 0.0001) continue;
            if (!all.TryGetValue(a, out var va)) all[a] = va = new Dictionary<string, double>(StringComparer.Ordinal);
            va[b] = s;
            if (!all.TryGetValue(b, out var vb)) all[b] = vb = new Dictionary<string, double>(StringComparer.Ordinal);
            vb[a] = s;
        }
        if (cap > 0)
            foreach (var kv in all)
                if (kv.Value.Count > cap)
                    all[kv.Key] = kv.Value.OrderByDescending(x => x.Value)
                        .ThenBy(x => x.Key, StringComparer.Ordinal)
                        .Take(cap).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        _contexts = all;
        return all;
    }

    /// <summary>
    /// 数值上最接近这个说法的一批说法。
    /// 共现只看得见「同框出现过」，这里靠的是语境像不像 —— 从没碰过面的近义词也能被捞出来，
    /// 这正是全文检索做不到的那半边。
    /// </summary>
    public List<(string Term, double Cosine)> Nearest(string term, int k)
    {
        var all = Contexts();
        if (!all.TryGetValue(term, out var mine) || mine.Count == 0) return new();
        var best = new List<(string, double)>();
        foreach (var kv in all)
        {
            if (string.Equals(kv.Key, term, StringComparison.Ordinal)) continue;
            var cos = RelationMath.Cosine(mine, kv.Value);
            if (cos <= 0.0001) continue;
            best.Add((kv.Key, cos));
        }
        return best.OrderByDescending(x => x.Item2)
            .ThenBy(x => x.Item1, StringComparer.Ordinal)
            .Take(Math.Max(1, k)).ToList();
    }

    /// <summary>「a｜b」里除 term 之外的那一边；不含 term 就返回 null。</summary>
    public static string? OtherSide(string pairKey, string term)
    {
        var bar = pairKey.IndexOf('|');
        if (bar <= 0) return null;
        var a = pairKey[..bar];
        var b = pairKey[(bar + 1)..];
        return a == term ? b : b == term ? a : null;
    }

    /// <summary>两个说法的意思有多接近（语境向量夹角的余弦，0~1）。</summary>
    public double Cosine(string a, string b)
        => string.Equals(a, b, StringComparison.Ordinal) ? 1 : RelationMath.Cosine(Context(a), Context(b));

    /// <summary>
    /// 关联强度 0~1：nPMI 说明「像不像真的有关」，共现轮数说明「证据够不够」。
    /// 两项相乘 —— 只见过一次的巧合同时无限高的 nPMI，不该当真。
    /// </summary>
    public double Association(string a, string b)
    {
        var seen = Cooc(a, b);
        if (seen <= 0) return 0;
        var tight = Math.Clamp(Npmi(a, b), 0, 1);
        var evidence = 1 - Math.Exp(-seen / 2.2);
        return Math.Clamp(tight * evidence, 0, 1);
    }

    /// <summary>最相关的几个说法（按关联强度）：想到一个就带出一串。</summary>
    public List<(string Term, double Score)> TopRelated(string term, int max, double floor = 0.06)
    {
        var hits = new List<(string, double)>();
        foreach (var kv in _cooc)
        {
            var other = OtherSide(kv.Key, term);
            if (other is null) continue;
            var s = Association(term, other);
            if (s >= floor) hits.Add((other, s));
        }
        return hits
            .OrderByDescending(x => x.Item2)
            .ThenBy(x => x.Item1, StringComparer.Ordinal)
            .Take(Math.Max(1, max))
            .ToList();
    }

    /// <summary>整张底账里最强的那些格子（页面「证据」面板与自检用）。</summary>
    public List<PairStat> TopPairs(int max, double floor = 0.05)
    {
        var rows = new List<PairStat>();
        foreach (var kv in _cooc)
        {
            var bar = kv.Key.IndexOf('|');
            if (bar <= 0) continue;
            var a = kv.Key[..bar];
            var b = kv.Key[(bar + 1)..];
            var assoc = Association(a, b);
            if (assoc < floor) continue;
            rows.Add(new PairStat(a, b, kv.Value, Npmi(a, b), assoc));
        }
        return rows.OrderByDescending(r => r.Association)
            .ThenBy(r => r.A, StringComparer.Ordinal)
            .Take(Math.Max(1, max))
            .ToList();
    }

    public List<TermStat> TopTerms(int max) => _df
        .OrderByDescending(kv => kv.Value)
        .ThenBy(kv => kv.Key, StringComparer.Ordinal)
        .Take(Math.Max(1, max))
        .Select(kv => new TermStat(kv.Key, kv.Value))
        .ToList();
}

/// <summary>量化公式本身单独放着：底账可以换（重算、迁移、导入），公式不能漂。</summary>
public static class RelationMath
{
    /// <summary>
    /// 归一化 PMI（Bouma 2009）：log2 P(a,b)/(P(a)P(b)) 再除以 -log2 P(a,b)，把无界的裸 PMI 折进 -1~1。
    /// 用归一化版是因为裸值没法横向比：两个罕见词的 PMI 能轻松吊打「玛丽—海边」这种天天一起出现的强关联。
    /// 概率为 0（压根没共现过）时给 0 而不是 -1：「没见过」是缺证据，不是「互相排斥」。
    /// </summary>
    public static double Npmi(double pab, double pa, double pb)
    {
        if (pab <= 0 || pa <= 0 || pb <= 0) return 0;
        var pmi = Math.Log2(pab / (pa * pb));
        var norm = -Math.Log2(pab);
        if (norm <= 1e-9) return 0;
        return Math.Clamp(pmi / norm, -1, 1);
    }

    /// <summary>两个稀疏向量的夹角余弦；任一边是零向量给 0（别把「谁都不挨着」当成一模一样）。</summary>
    public static double Cosine(IReadOnlyDictionary<string, double> a, IReadOnlyDictionary<string, double> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        double dot = 0, na = 0, nb = 0;
        var (small, big) = a.Count <= b.Count ? (a, b) : (b, a);
        foreach (var kv in small)
        {
            na += kv.Value * kv.Value;
            if (big.TryGetValue(kv.Key, out var o)) dot += kv.Value * o;
        }
        foreach (var v in big.Values) nb += v * v;
        if (na <= 1e-12 || nb <= 1e-12) return 0;
        return Math.Clamp(dot / (Math.Sqrt(na) * Math.Sqrt(nb)), 0, 1);
    }

    /// <summary>
    /// 关联强度 → 边的权重（沿用 0.1~500 的量纲，已有的阈值不用重调）。
    /// 开根号是为了把中段拉开：0.25 和 0.5 的差别要看得见，0.9 和 1.0 的差别不必那么大。
    /// </summary>
    public static double ToWeight(double association01)
        => Math.Clamp(0.6 + 26 * Math.Pow(Math.Clamp(association01, 0, 1), 0.55), 0.1, 500);

    /// <summary>饱和曲线：证据越多、每多一条加得越少，不会一条边吃满全表。</summary>
    public static double Saturate(double count, double k) => count <= 0 ? 0 : count / (count + Math.Max(0.01, k));
}
