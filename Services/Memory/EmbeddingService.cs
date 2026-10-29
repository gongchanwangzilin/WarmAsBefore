using System.Text;
using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 数值近邻这条结果是哪儿来的。界面必须能跟用户讲清「量化模型算的」还是「本地统计凑的」：
/// 后者不是出错，而是没配模型 / 调用失败 / 免费额度用完时的兜底 —— 它不要钱，也一直都在。
/// </summary>
public enum QuantBackend
{
    /// <summary>本地共现统计（nPMI + 稀疏语境向量），离线可算。</summary>
    Statistics = 0,
    /// <summary>量化模型（embedding）算出来的稠密向量。</summary>
    Embedding = 1
}

/// <summary>按数值搜到的一条记忆：接近度和条目本身（页面要显示的都带在 Entry 上）。</summary>
public sealed record MemoryQuantHit(string EntryId, double Cosine, HumanMemoryEntry Entry);

/// <summary>
/// 量化模型：一种专门的知识库管理员 —— 把词语和句子换成数值，近似的说法落在必定近似的数值上，
/// 于是「谁和它活得像同一类东西」这个问题第一次有了可以直接算的答案。
///
/// 走 OpenAI 兼容的 <c>POST {base}/embeddings</c>（input 是数组，一次问一批）。
/// 三条硬规矩：
/// · 算不出来就闭嘴：任何失败只写一行日志，然后调用方退回 <see cref="RelationStatsIndex"/> 那套本地统计，
///   绝不把异常抛到界面上；
/// · 密钥只挂在发往这个端点的那一条请求上，绝不写进 HttpClient 的默认头（本项目真的漏过一次天气密钥）；
/// · 算过的文本按「模型 + 文本」存盘，再按一次同一个节点不该再花一遍钱。
/// </summary>
public sealed class EmbeddingService
{
    /// <summary>文本 → 向量的缓存。故意不按存档隔离：同样的文本配同样的模型本来就该是同一个向量，
    /// 分开存只会让同一个说法在每个存档里各买一次。</summary>
    private const string TextStore = "embedding_cache";

    /// <summary>记忆条目 Id → 向量：按条目存，改过文案的那条才会重算。</summary>
    private const string MemoryStore = "embedding_memories";

    /// <summary>一次请求最多带几条输入：端点对 input 长度普遍有上限，也不想让一次误触把额度打光。</summary>
    public const int MaxInputsPerCall = 64;

    /// <summary>单条最长多少字。记忆不超过 60 字，说法不超过 24 字，这里只挡整段原文这类异常输入。</summary>
    public const int MaxInputChars = 700;

    /// <summary>一个 1536 维向量落盘就接近 10KB，缓存不设上限的话读盘会变成读几十兆。</summary>
    private const int MaxTextRows = 1500;

    private const int MaxMemoryRows = 1200;

    /// <summary>一轮最多新算多少条文本：第一次搜记忆时库里可能有上千条，先算最近这批，别一次买全库。</summary>
    public const int MaxNewPerCall = 240;

    /// <summary>一次调用最多试几枚密钥（免费额度耗尽时换一枚还有戏）。</summary>
    private const int MaxKeysPerCall = 4;

    /// <summary>超时交给每次请求自己的 CTS（和 ApiGateway 一样，HttpClient 自身不设固定超时）。</summary>
    private readonly HttpClient _http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private readonly StorageProvider _store;
    private readonly SettingsManager _settings;
    private readonly HumanMemoryService _human;
    private readonly object _gate = new();

    /// <summary>
    /// 「查缓存 → 问模型 → 落盘」整段排队走。
    /// 搜索框每停一下就会问一次，两个请求撞在一起时后一个必须能看见前一个刚买回来的向量，
    /// 同一段话买两遍是白花钱。
    /// </summary>
    private readonly SemaphoreSlim _busy = new(1, 1);

    private Dictionary<string, VectorRow> _texts = new(StringComparer.Ordinal);
    private Dictionary<string, VectorRow> _memories = new(StringComparer.Ordinal);
    private Task? _loading;

    public EmbeddingService(StorageProvider store, SettingsManager settings, HumanMemoryService human)
    {
        _store = store;
        _settings = settings;
        _human = human;
    }

    /// <summary>量化模型这一路现在可用吗：开关开着、填了模型名，并且至少解析出一枚密钥。</summary>
    public bool IsConfigured => Resolve() is not null;

    /// <summary>给界面念的名字：用着的模型名；没配上就说实话。</summary>
    public string BackendName => Resolve() is { } e ? e.Model : "没配量化模型";

    // ==================== 对外的三件事 ====================

    /// <summary>
    /// 一组文本 → 向量，键是 <see cref="Normalize"/> 之后的文本。
    /// 命中缓存的一条不花钱；没配的、算失败的直接不在结果里（调用方据此退回本地统计）。
    /// </summary>
    public async Task<Dictionary<string, float[]>> MapAsync(IEnumerable<string> texts)
    {
        var result = new Dictionary<string, float[]>(StringComparer.Ordinal);
        var want = new List<string>();
        foreach (var raw in texts)
        {
            var t = Normalize(raw);
            if (t.Length == 0 || want.Contains(t, StringComparer.Ordinal)) continue;
            want.Add(t);
        }
        if (want.Count == 0) return result;
        var e = Resolve();
        if (e is null) return result;

        await EnsureLoadedAsync();
        await _busy.WaitAsync();
        try
        {
            var misses = new List<string>();
            lock (_gate)
            {
                foreach (var t in want)
                {
                    if (_texts.TryGetValue(KeyOf(e, t), out var row) && row.Vector.Length > 0)
                    {
                        result[t] = row.Vector;
                        row.Used = Now();
                        continue;
                    }
                    misses.Add(t);
                }
            }
            if (misses.Count == 0) return result;

            var got = await RequestAsync(e, misses);
            if (got.Count == 0) return result;      // 一条都没算出来：安静退回统计，别在这儿吵
            lock (_gate)
            {
                foreach (var kv in got)
                {
                    _texts[KeyOf(e, kv.Key)] = new VectorRow { Text = kv.Key, Vector = kv.Value, Used = Now() };
                    result[kv.Key] = kv.Value;
                }
                Trim(_texts, MaxTextRows);
            }
            await SaveAsync();
        }
        catch (Exception ex)
        {
            App.WriteLog("Embedding.Map -> " + ex.Message);
        }
        finally
        {
            _busy.Release();
        }
        return result;
    }

    /// <summary>一个说法 / 一句话的向量。没配模型或算不出来返回 null。</summary>
    public async Task<float[]?> EmbedOneAsync(string text)
    {
        var key = Normalize(text);
        if (key.Length == 0) return null;
        var map = await MapAsync(new[] { text });
        return map.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>
    /// 一批记忆条目的向量，按（模型 + 条目 Id）缓存：文案没变的条目一分钱不花。
    /// 键是条目 Id，所以调用方不用关心规范化后的文本长什么样。
    /// </summary>
    public async Task<Dictionary<string, float[]>> MemoryVectorsAsync(string charId, IReadOnlyList<HumanMemoryEntry> entries)
    {
        var result = new Dictionary<string, float[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(charId) || entries is not { Count: > 0 }) return result;
        var e = Resolve();
        if (e is null) return result;
        await EnsureLoadedAsync();

        await _busy.WaitAsync();
        try
        {
            var stale = new List<(string Id, string Text)>();
            lock (_gate)
            {
                foreach (var m in entries)
                {
                    var text = Normalize(m.Text);
                    if (text.Length == 0) continue;
                    if (_memories.TryGetValue(KeyOf(e, m.Id), out var row) && row.Text == text && row.Vector.Length > 0)
                    {
                        result[m.Id] = row.Vector;
                        row.Used = Now();
                        continue;
                    }
                    stale.Add((m.Id, text));
                }
            }
            if (stale.Count == 0) return result;

            // 同一句话说过的记法可能重复出现：先去重再问模型，一段话只买一次。
            // 一次也别买太多：第一次搜记忆时库里可能上千条，先算送进来的这批。
            var texts = stale.Select(x => x.Text).Distinct(StringComparer.Ordinal).Take(MaxNewPerCall).ToList();
            var got = await RequestAsync(e, texts);
            if (got.Count == 0) return result;
            lock (_gate)
            {
                foreach (var (id, text) in stale)
                {
                    if (!got.TryGetValue(text, out var v)) continue;
                    _memories[KeyOf(e, id)] = new VectorRow { Text = text, Vector = v, Used = Now() };
                    result[id] = v;
                }
                Trim(_memories, MaxMemoryRows);
            }
            await SaveAsync();
        }
        catch (Exception ex)
        {
            App.WriteLog("Embedding.Memories -> " + ex.Message);
        }
        finally
        {
            _busy.Release();
        }
        return result;
    }

    /// <summary>
    /// 按数值搜记忆：这句话换成向量，再按稠密余弦给 TA 记得的事排一遍。
    /// 全文检索问的是「这句话里有没有这个词」，这里问的是「哪件事和它活得像一类」。
    /// 量化模型没配上就返回空列表 —— 关键字那条路本来就在 <see cref="HumanMemoryService.SearchAsync"/> 里，
    /// 没有哪条搜索结果比「什么都不加」更诚实。
    /// </summary>
    public async Task<List<MemoryQuantHit>> SearchMemoriesAsync(string charId, string query, int k = 20,
        double floor = 0.5, int maxEntries = MaxNewPerCall)
    {
        var hits = new List<MemoryQuantHit>();
        if (string.IsNullOrWhiteSpace(charId) || string.IsNullOrWhiteSpace(query)) return hits;
        try
        {
            var qv = await EmbedOneAsync(query);
            if (qv is null) return hits;
            var all = await _human.AllAsync(charId);
            if (all.Count == 0) return hits;
            var entries = all.Count > maxEntries ? all.Take(maxEntries).ToList() : all;
            var vectors = await MemoryVectorsAsync(charId, entries);
            if (vectors.Count == 0) return hits;

            foreach (var m in entries)
            {
                if (!vectors.TryGetValue(m.Id, out var v)) continue;
                var cos = Cosine(qv, v);
                if (cos < floor) continue;
                hits.Add(new MemoryQuantHit(m.Id, cos, m));
            }
            return hits.OrderByDescending(h => h.Cosine)
                .ThenBy(h => h.EntryId, StringComparer.Ordinal)
                .Take(Math.Max(1, k)).ToList();
        }
        catch (Exception ex)
        {
            App.WriteLog("Embedding.Search -> " + ex.Message);
            return hits;
        }
    }

    // ==================== 向量的算法 ====================

    /// <summary>
    /// 两个稠密向量的夹角余弦（0~1）。维度不一样说明换了模型或换了维度，一律给 0 ——
    /// 更要紧的是：这套数值绝不和 <see cref="RelationMath.Cosine"/> 那套稀疏语境向量混着比，
    /// 两者的基线差得远，混在一起排序等于把两种答案搅成一锅。
    /// </summary>
    public static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * (double)b[i];
            na += a[i] * (double)a[i];
            nb += b[i] * (double)b[i];
        }
        if (na <= 1e-12 || nb <= 1e-12) return 0;
        return Math.Clamp(dot / (Math.Sqrt(na) * Math.Sqrt(nb)), 0, 1);
    }

    /// <summary>缓存键里的文本规范化：去首尾空白、连续空白压成一个空格，超长剪掉。</summary>
    public static string Normalize(string? text)
    {
        var s = (text ?? "").Trim();
        if (s.Length == 0) return "";
        if (s.Length > MaxInputChars) s = s[..MaxInputChars];
        var sb = new StringBuilder(s.Length);
        var pending = false;
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c)) { pending = sb.Length > 0; continue; }
            if (pending) { sb.Append(' '); pending = false; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    // ==================== 配置解析 ====================

    /// <summary>一次调用要用的端点：地址、模型名、维度，和可以轮着试的密钥。</summary>
    private sealed record Endpoint(string Url, string Model, string Id, int Dimensions, IReadOnlyList<string> Keys);

    /// <summary>
    /// 这个角色的活该由哪个端点干：用户给量化模型单独填的就用单独的，没填的就复用对话那套
    /// （主端点或第一家启用的厂商）的地址与密钥 —— 量化模型常常就住在同一家厂商隔壁。
    /// </summary>
    private Endpoint? Resolve()
    {
        var s = _settings.Current;
        if (!s.EmbeddingEnabled) return null;
        var model = (s.EmbeddingModel ?? "").Trim();
        if (model.Length == 0) return null;

        var keys = new List<string>();
        void AddKey(string? raw)
        {
            var k = (raw ?? "").Trim();
            if (k.Length == 0 || keys.Count >= MaxKeysPerCall || keys.Contains(k, StringComparer.Ordinal)) return;
            keys.Add(k);
        }
        AddKey(s.EmbeddingKey);
        AddKey(s.AiKey);
        foreach (var p in s.AiProviders ?? new List<AiProvider>())
        {
            if (!p.Enabled) continue;
            foreach (var k in p.Keys ?? new List<string>()) AddKey(k);
        }
        if (keys.Count == 0) return null;       // 一枚密钥都没有：跑了也是白跑，留给本地统计

        var url = (s.EmbeddingUrl ?? "").Trim();
        if (url.Length == 0) url = (s.AiUrl ?? "").Trim();
        if (url.Length == 0)
            url = (s.AiProviders ?? new List<AiProvider>()).FirstOrDefault(p => p.Enabled
                && !string.IsNullOrWhiteSpace(p.Url))?.Url?.Trim() ?? "";
        var endpoint = EmbeddingsUrlOf(url);
        if (endpoint.Length == 0) return null;

        var dims = s.EmbeddingDimensions > 0 ? Math.Clamp(s.EmbeddingDimensions, 1, 8192) : 0;
        // 维度并进缓存标识里：换了维度就是一套新向量，拿旧的来比等于拿两次不同的翻译对比
        return new Endpoint(endpoint, model, model + (dims > 0 ? "@" + dims : ""), dims, keys);
    }

    /// <summary>设置里存的多半是 chat/completions 地址，这里折回根地址再补 /embeddings。</summary>
    public static string EmbeddingsUrlOf(string url)
    {
        var u = (url ?? "").Trim().TrimEnd('/');
        foreach (var tail in new[] { "/chat/completions", "/completions", "/embeddings" })
        {
            if (!u.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) continue;
            u = u[..^tail.Length];
            break;
        }
        return u.Length == 0 ? "" : u + "/embeddings";
    }

    private static string KeyOf(Endpoint e, string normalized) => e.Id + "|" + normalized;

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private int TimeoutSeconds()
    {
        var t = _settings.Current.AiTimeoutSeconds;
        // 向量比对话短得多，等不到对话那么久：卡在这儿不如早点退回统计
        return Math.Clamp(t > 0 ? t : 25, 5, 60);
    }

    // ==================== 发请求 ====================

    /// <summary>分批去问模型；某一批整批失败就收手（额度没了或这个模型不认，继续试只是多等几秒）。</summary>
    private async Task<Dictionary<string, float[]>> RequestAsync(Endpoint e, List<string> inputs)
    {
        var all = new Dictionary<string, float[]>(StringComparer.Ordinal);
        for (var start = 0; start < inputs.Count; start += MaxInputsPerCall)
        {
            var batch = inputs.Skip(start).Take(MaxInputsPerCall).ToList();
            var got = await SendBatchAsync(e, batch);
            foreach (var kv in got) all[kv.Key] = kv.Value;
            if (got.Count > 0) continue;
            App.WriteLog($"Embedding: 量化模型「{e.Model}」这一批 {batch.Count} 条没算出来，本地统计兜底");
            break;
        }
        return all;
    }

    private async Task<Dictionary<string, float[]>> SendBatchAsync(Endpoint e, List<string> batch)
    {
        var json = JsonSerializer.Serialize(e.Dimensions > 0
            ? new { model = e.Model, input = batch, dimensions = e.Dimensions }
            : (object)new { model = e.Model, input = batch });

        foreach (var key in e.Keys)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, e.Url);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            // 只给这一条请求带头：默认头上挂密钥 = 下一次请求别的地址时把密钥一起带过去
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds()));
                using var resp = await _http.SendAsync(req, cts.Token);
                var raw = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    App.WriteLog($"Embedding HTTP {(int)resp.StatusCode}：「{e.Model}」换一枚密钥再试");
                    continue;
                }
                return Parse(raw, batch);
            }
            catch (Exception ex)
            {
                App.WriteLog("Embedding.Send -> " + ex.Message);
                return new Dictionary<string, float[]>(StringComparer.Ordinal);
            }
        }
        return new Dictionary<string, float[]>(StringComparer.Ordinal);
    }

    /// <summary>读 data[].embedding；带 index 就按 index 对回原文（并行返回时顺序不保证），没有就按次序。</summary>
    private static Dictionary<string, float[]> Parse(string raw, IReadOnlyList<string> inputs)
    {
        var map = new Dictionary<string, float[]>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return map;
            var ordinal = 0;
            foreach (var item in data.EnumerateArray())
            {
                var at = ordinal;
                ordinal++;
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (item.TryGetProperty("index", out var idx) && idx.TryGetInt32(out var n) && n >= 0 && n < inputs.Count)
                    at = n;
                if (at >= inputs.Count) continue;
                if (!item.TryGetProperty("embedding", out var emb) || emb.ValueKind != JsonValueKind.Array) continue;
                var v = new float[emb.GetArrayLength()];
                var i = 0;
                foreach (var x in emb.EnumerateArray())
                {
                    if (i >= v.Length) break;
                    v[i++] = x.TryGetSingle(out var f) ? f : 0f;
                }
                if (i == 0 || i < v.Length) continue;       // 半截向量宁可不要，也别当成一个点用
                map[inputs[at]] = v;
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("Embedding.Parse -> " + ex.Message);
        }
        return map;
    }

    // ==================== 缓存的读写 ====================

    private Task EnsureLoadedAsync()
    {
        lock (_gate) return _loading ??= LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var texts = await _store.Load<Dictionary<string, VectorRow>>(TextStore);
            var memories = await _store.Load<Dictionary<string, VectorRow>>(MemoryStore);
            lock (_gate)
            {
                if (texts is not null) _texts = new Dictionary<string, VectorRow>(texts, StringComparer.Ordinal);
                if (memories is not null) _memories = new Dictionary<string, VectorRow>(memories, StringComparer.Ordinal);
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("Embedding.Load -> " + ex.Message);
        }
    }

    /// <summary>两份缓存一起落盘（调用方已持 <see cref="_busy"/>，这里不再排队）。</summary>
    private async Task SaveAsync()
    {
        Dictionary<string, VectorRow> texts, memories;
        lock (_gate)
        {
            texts = new Dictionary<string, VectorRow>(_texts, StringComparer.Ordinal);
            memories = new Dictionary<string, VectorRow>(_memories, StringComparer.Ordinal);
        }
        try
        {
            await _store.Save(TextStore, texts);
            await _store.Save(MemoryStore, memories);
        }
        catch (Exception ex)
        {
            App.WriteLog("Embedding.Save -> " + ex.Message);
        }
    }

    /// <summary>超出上限就丢最久没被用到的：向量的价值在于马上还会再按一次这个钮。</summary>
    private static void Trim(Dictionary<string, VectorRow> table, int keep)
    {
        if (table.Count <= keep) return;
        foreach (var k in table.OrderByDescending(kv => kv.Value.Used)
                     .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                     .Skip(keep).Select(kv => kv.Key).ToList())
            table.Remove(k);
    }

    /// <summary>缓存的一格：文本留着是为了认得出「这条记忆改过文案」，Used 是淘汰用的时间戳。</summary>
    private sealed class VectorRow
    {
        public string Text { get; set; } = "";
        public float[] Vector { get; set; } = Array.Empty<float>();
        public long Used { get; set; }
    }
}
