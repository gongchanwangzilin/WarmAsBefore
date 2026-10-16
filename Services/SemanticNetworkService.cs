using System.Text;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 词义关系网：每个角色脑子里一张词与词的关联网，后台随对话自动长出来。
///
/// 关键点：
/// · **默认不花模型钱**：只用共现统计（同一轮对话里一起出现的词，联系就加强），
///   模型辅助是可选项，不由本服务自己发起；
/// · **人与 AI 都能改**：<see cref="LinkAsync"/> / <see cref="UnlinkAsync"/> / <see cref="CorrectAsync"/>
///   直接纠错，纠错过的边标记为 human / ai，不会被自动累积冲淡；
/// · **用于联想检索**：<see cref="ExpandAsync"/> 把一个词扩散成一组相关词，
///   检索记忆时先用它拓宽，再按人类记忆的权重排序。
///
/// 中文没有空格，这里用「按非文字符切段 + 长串按 2 字滑窗」取词，够用且零依赖。
/// </summary>
public sealed class SemanticNetworkService
{
    private const string StoreKey = "semantic_networks";
    private const int MaxLinksPerCharacter = 3000;
    private const double AutoStep = 1.0;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "我们", "你们", "他们", "什么", "怎么", "可以", "这个", "那个", "现在", "然后", "因为", "所以",
        "但是", "如果", "已经", "还是", "就是", "不是", "没有", "一个", "一下", "一点", "这样", "那样",
        "真的", "好像", "也许", "应该", "自己", "时候", "东西", "事情", "感觉", "知道", "觉得", "时候"
    };

    private readonly StorageProvider _store;
    private readonly object _gate = new();
    private Dictionary<string, List<SemanticLink>> _cache = new(StringComparer.Ordinal);
    private bool _loaded;

    public SemanticNetworkService(StorageProvider store) => _store = store;

    private async Task EnsureLoadedAsync()
    {
        lock (_gate)
        {
            if (_loaded) return;
            _loaded = true;
        }
        try
        {
            var saved = await _store.Load<Dictionary<string, List<SemanticLink>>>(StoreKey);
            lock (_gate)
            {
                if (saved is not null) _cache = new Dictionary<string, List<SemanticLink>>(saved, StringComparer.Ordinal);
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("SemanticNetwork.Load -> " + ex.Message);
        }
    }

    private Task PersistAsync()
    {
        Dictionary<string, List<SemanticLink>> snapshot;
        lock (_gate) snapshot = _cache.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.Ordinal);
        return _store.Save(StoreKey, snapshot);
    }

    /// <summary>
    /// 学一轮对话：同一轮里一起出现的词，两两加强联系。
    /// 这就像人听多了「咖啡」和「加班」一起出现，之后想到咖啡就会想起加班。
    /// </summary>
    public async Task LearnAsync(string charId, params string?[] texts)
    {
        if (string.IsNullOrWhiteSpace(charId)) return;
        var terms = texts.Where(t => !string.IsNullOrWhiteSpace(t))
            .SelectMany(t => ExtractTerms(t!))
            .Distinct(StringComparer.Ordinal)
            .Take(40)
            .ToList();
        if (terms.Count < 2) return;

        await EnsureLoadedAsync();
        lock (_gate)
        {
            if (!_cache.TryGetValue(charId, out var links)) _cache[charId] = links = new List<SemanticLink>();
            for (var i = 0; i < terms.Count; i++)
                for (var j = i + 1; j < terms.Count; j++)
                    BumpLocked(links, terms[i], terms[j], AutoStep, "auto", manual: false);
            if (links.Count > MaxLinksPerCharacter)
                _cache[charId] = links.OrderByDescending(l => l.Weight).Take(MaxLinksPerCharacter).ToList();
        }
        await PersistAsync();
    }

    /// <summary>手动/AI 建立或加强一条联系（纠错过的边不会被自动累积冲淡）。</summary>
    public async Task LinkAsync(string charId, string a, string b, double delta = 3, string source = "human")
    {
        if (string.IsNullOrWhiteSpace(charId) || string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return;
        await EnsureLoadedAsync();
        lock (_gate)
        {
            if (!_cache.TryGetValue(charId, out var links)) _cache[charId] = links = new List<SemanticLink>();
            BumpLocked(links, a.Trim(), b.Trim(), delta, source, manual: true);
        }
        await PersistAsync();
    }

    /// <summary>断开一条联系（人为纠正「这两个词其实没关系」）。</summary>
    public async Task UnlinkAsync(string charId, string a, string b)
    {
        await EnsureLoadedAsync();
        lock (_gate)
        {
            if (!_cache.TryGetValue(charId, out var links)) return;
            links.RemoveAll(l => Same(l.A, a) && Same(l.B, b));
        }
        await PersistAsync();
    }

    /// <summary>直接设定一条联系的权重（人工纠错：太强/太弱都改得了）。</summary>
    public async Task CorrectAsync(string charId, string a, string b, double weight, string source = "human")
    {
        await EnsureLoadedAsync();
        lock (_gate)
        {
            if (!_cache.TryGetValue(charId, out var links)) _cache[charId] = links = new List<SemanticLink>();
            links.RemoveAll(l => Same(l.A, a) && Same(l.B, b));
            if (weight > 0) links.Add(new SemanticLink { A = a.Trim(), B = b.Trim(), Weight = weight, Source = source });
        }
        await PersistAsync();
    }

    /// <summary>联想扩散：从一组词出发，按权重走出相关的词（默认两跳）。</summary>
    public async Task<List<(string Term, double Score)>> ExpandAsync(string charId, IEnumerable<string> seeds, int maxTerms = 12, int depth = 2)
    {
        await EnsureLoadedAsync();
        List<SemanticLink> links;
        lock (_gate) links = _cache.TryGetValue(charId, out var l) ? l.ToList() : new List<SemanticLink>();
        if (links.Count == 0) return new List<(string, double)>();

        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        var frontier = seeds.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToList();
        foreach (var f in frontier) result[f] = 10;

        for (var d = 0; d < depth; d++)
        {
            var next = new List<string>();
            foreach (var term in frontier)
            {
                foreach (var link in links)
                {
                    string? other = null;
                    if (Same(link.A, term)) other = link.B;
                    else if (Same(link.B, term)) other = link.A;
                    if (other is null || string.IsNullOrWhiteSpace(other)) continue;

                    var score = link.Weight / (1 + d);
                    if (result.TryGetValue(other, out var cur) && cur >= score) continue;
                    result[other] = Math.Max(cur, score);
                    next.Add(other);
                }
            }
            frontier = next.Distinct(StringComparer.Ordinal).ToList();
            if (frontier.Count == 0) break;
        }

        return result
            .OrderByDescending(kv => kv.Value)
            .Take(Math.Max(1, maxTerms))
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    /// <summary>某个词最紧的几个邻居（给「关系网」查看界面与纠错用）。</summary>
    public async Task<List<SemanticLink>> NeighborsAsync(string charId, string term, int max = 12)
    {
        await EnsureLoadedAsync();
        lock (_gate)
        {
            if (!_cache.TryGetValue(charId, out var links)) return new List<SemanticLink>();
            return links.Where(l => Same(l.A, term) || Same(l.B, term))
                .OrderByDescending(l => l.Weight)
                .Take(max)
                .ToList();
        }
    }

    /// <summary>联系最紧的若干词对（给「关系网」查看与人工纠错用）。</summary>
    public async Task<List<SemanticLink>> TopLinksAsync(string charId, int max = 12)
    {
        await EnsureLoadedAsync();
        lock (_gate)
        {
            if (!_cache.TryGetValue(charId, out var links)) return new List<SemanticLink>();
            return links.OrderByDescending(l => l.Weight).Take(Math.Max(1, max)).ToList();
        }
    }

    public async Task<int> LinkCountAsync(string charId)
    {
        await EnsureLoadedAsync();
        lock (_gate) return _cache.TryGetValue(charId, out var l) ? l.Count : 0;
    }

    // ---- 内部 ----

    private static void BumpLocked(List<SemanticLink> links, string a, string b, double delta, string source, bool manual)
    {
        var idx = links.FindIndex(l => Same(l.A, a) && Same(l.B, b));
        if (idx >= 0)
        {
            var old = links[idx];
            // 人工/AI 纠错过的边不接受自动累积：只在人工纠错时改动它
            if (old.Source != "auto" && !manual) return;
            links[idx] = old with
            {
                Weight = Math.Clamp(old.Weight + delta, 0.1, 500),
                UpdatedAt = DateTime.UtcNow,
                Source = manual ? source : old.Source
            };
        }
        else
        {
            links.Add(new SemanticLink { A = a, B = b, Weight = Math.Clamp(delta, 0.1, 500), Source = source });
        }
    }

    private static bool Same(string x, string y) => string.Equals(x, y, StringComparison.Ordinal);

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
