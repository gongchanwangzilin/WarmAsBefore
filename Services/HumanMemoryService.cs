using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 人类记忆层：按人脑的方式存与取。
///
/// 存：只存关键点、抽象印象、重要时刻与单张图片，不存整段对话原文；
/// 取：按「重要性 × 时间衰减 × 复述次数 × 随机扰动」排序（由近到远为主，带随机扰动），
///     而不是简单取最近 N 条——人本来就会想起很久以前的重要事，也会突然想不起昨天说了什么。
///
/// 与既有的 <c>MemoryVault</c>（全量对话记忆库）并存：这里是「脑子里记得的」，
/// 那边是「翻日记能找到的」。
/// </summary>
public sealed class HumanMemoryService
{
    private const string StoreName = "human_memories";

    private readonly StorageProvider _store;
    private readonly MemoryScope _scope;
    private readonly object _gate = new();
    private List<HumanMemoryEntry> _cache = new();
    private Task? _loading;
    private string _scopeKey = StoreName;

    public HumanMemoryService(StorageProvider store, MemoryScope scope)
    {
        _store = store;
        _scope = scope;
        _scopeKey = scope.Key(StoreName);
        scope.Changed += OnScopeChanged;
    }

    /// <summary>换档/换开关：上一个存档的记忆不能留在缓存里，否则会被写进这个存档的文件。</summary>
    private void OnScopeChanged()
    {
        lock (_gate)
        {
            _cache = new();
            _loading = null;
            _scopeKey = _scope.Key(StoreName);
        }
    }

    /// <summary>
    /// 只读一次盘，但「读过」以读完为准：拿缓存的 Task 而不是布尔标记，
    /// 因为标记先置位的话，同一瞬间进来的第二个调用会以为已经加载好，捧着空缓存就返回。
    /// </summary>
    private Task EnsureLoadedAsync()
    {
        lock (_gate) return _loading ??= LoadAsync();
    }

    private async Task LoadAsync()
    {
        var key = _scopeKey;
        try
        {
            var saved = await _store.Load<List<HumanMemoryEntry>>(key);
            lock (_gate) { if (saved is not null && key == _scopeKey) _cache = saved; }
        }
        catch (Exception ex)
        {
            App.WriteLog("HumanMemory.Load -> " + ex.Message);
        }
    }

    private Task PersistAsync()
    {
        List<HumanMemoryEntry> snapshot;
        string key;
        lock (_gate)
        {
            snapshot = _cache.ToList();
            key = _scopeKey;
        }
        return _store.Save(key, snapshot);
    }

    /// <summary>记下一件事（默认按内容推测类型与重要性）。</summary>
    public async Task StoreAsync(string charId, string text, HumanMemoryKind? kind = null,
        double? importance = null, string imagePath = "", IEnumerable<string>? tags = null)
    {
        if (string.IsNullOrWhiteSpace(charId) || string.IsNullOrWhiteSpace(text)) return;
        await EnsureLoadedAsync();

        var line = text.Trim();
        if (line.Length > 60) line = line[..60];
        var clean = tags is null ? new List<string>() : tags.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
        var entry = new HumanMemoryEntry
        {
            CharacterId = charId,
            Text = line,
            ImagePath = imagePath ?? "",
            Kind = kind ?? HumanMemoryMath.GuessKind(line, imagePath ?? ""),
            Importance = importance ?? HumanMemoryMath.EstimateImportance(line),
            Tags = clean,
            SentenceTags = HumanMemoryMath.AutoTag(line, clean),
            Emotion = EmotionMix.FromText(line)
        };

        lock (_gate)
        {
            // 同一角色 + 同一句话不重复记：人不会把同一件事记两遍
            var dup = _cache.FirstOrDefault(m => m.CharacterId == charId && m.Text == entry.Text);
            if (dup is not null)
            {
                var idx = _cache.IndexOf(dup);
                // 命中旧条目时标签要并进去：AI 第二次给同一件事打标签，不能只留新那一批
                var merged = (dup.SentenceTags.Count > 0
                        ? dup.SentenceTags
                        : HumanMemoryMath.AutoTag(dup.Text, dup.Tags))
                    .Select(s => s with { Tags = s.Tags.ToList() }).ToList();
                foreach (var add in entry.SentenceTags)
                {
                    var at = merged.FindIndex(m => m.Index == add.Index);
                    if (at < 0) merged.Add(new SentenceTag { Index = add.Index, Tags = add.Tags.ToList() });
                    else foreach (var t in add.Tags)
                        if (!merged[at].Tags.Contains(t, StringComparer.OrdinalIgnoreCase)) merged[at].Tags.Add(t);
                }
                _cache[idx] = dup with
                {
                    Importance = Math.Max(dup.Importance, entry.Importance),
                    SentenceTags = merged.OrderBy(m => m.Index).ToList(),
                    Emotion = MergeEmotion(dup.Emotion, entry.Emotion),
                    Tags = merged.SelectMany(m => m.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                };
            }
            else
            {
                _cache.Add(entry);
            }
        }
        await PersistAsync();
    }

    /// <summary>把一次折叠出来的记忆段落逐行记进人类记忆层。</summary>
    public async Task StoreFromDigestAsync(string charId, string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return;
        foreach (var raw in digest.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim().TrimStart('·', '-', '*', ' ', '\t');
            if (line.Length < 4) continue;                       // 太短的碎片不算记忆
            if (line.StartsWith("【", StringComparison.Ordinal)) continue;   // 标记行不算
            await StoreAsync(charId, line);
        }
    }

    /// <summary>
    /// 回想：默认只回想三天内的为主，越久越模糊但不彻底消失；
    /// <paramref name="windowDays"/> 为 0 表示不限（能想起最早的记忆）。
    /// </summary>
    public async Task<List<HumanMemoryEntry>> RecallAsync(string charId, double windowDays,
        double randomness, int limit = 8, IEnumerable<string>? requiredTags = null)
    {
        await EnsureLoadedAsync();
        List<HumanMemoryEntry> pool;
        lock (_gate) pool = _cache.Where(m => m.CharacterId == charId).ToList();
        if (pool.Count == 0) return new List<HumanMemoryEntry>();

        if (requiredTags is not null)
        {
            var need = requiredTags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (need.Count > 0)
                pool = pool.Where(m => need.All(t => m.Tags.Contains(t, StringComparer.OrdinalIgnoreCase))).ToList();
            if (pool.Count == 0) return new List<HumanMemoryEntry>();
        }

        var now = DateTime.UtcNow;
        var scored = pool
            .Select(m => (Entry: m, Score: HumanMemoryMath.Score(m, windowDays, randomness, now)))
            .Where(x => x.Score >= HumanMemoryMath.MinWeight)
            .OrderByDescending(x => x.Score)
            .Take(Math.Max(1, limit))
            .ToList();

        // 记一次「被想起」：复述过的记忆更容易再次被想起（间隔重复效应）
        if (scored.Count > 0)
        {
            lock (_gate)
            {
                foreach (var (entry, _) in scored)
                {
                    var idx = _cache.FindIndex(m => m.Id == entry.Id);
                    if (idx >= 0)
                        _cache[idx] = _cache[idx] with
                        {
                            RecallCount = _cache[idx].RecallCount + 1,
                            LastRecalledAt = now
                        };
                }
            }
            await PersistAsync();
        }
        return scored.Select(x => x.Entry).ToList();
    }

    /// <summary>提示词文本：像人一样「想起」而不是「检索出记录」。</summary>
    public async Task<string> RecallTextAsync(string charId, double windowDays, double randomness, int limit = 6)
    {
        var hits = await RecallAsync(charId, windowDays, randomness, limit);
        if (hits.Count == 0) return "";
        var lines = hits.Select(h => h.Kind switch
        {
            HumanMemoryKind.Image => $"· [照片]{h.Text}" + (string.IsNullOrEmpty(h.ImagePath) ? "" : $"（{Path.GetFileName(h.ImagePath)}）"),
            HumanMemoryKind.Important => $"· （记得很牢）{h.Text}",
            HumanMemoryKind.Abstraction => $"· （印象）{h.Text}",
            _ => "· " + h.Text
        });
        return "你脑子里浮起来的一些事（不是别人告诉你的资料，是你自己记得的）：\n" + string.Join("\n", lines);
    }

    public async Task<List<HumanMemoryEntry>> AllAsync(string charId)
    {
        await EnsureLoadedAsync();
        lock (_gate) return _cache.Where(m => m.CharacterId == charId)
            .OrderByDescending(m => m.CreatedAt).ToList();
    }

    /// <summary>让 TA 忘掉一条记忆（人工修正：记错了、不该记的都删得掉）。</summary>
    public async Task<bool> ForgetAsync(string charId, string memoryId)
    {
        await EnsureLoadedAsync();
        bool removed;
        lock (_gate) removed = _cache.RemoveAll(m => m.CharacterId == charId && m.Id == memoryId) > 0;
        if (removed) await PersistAsync();
        return removed;
    }

    /// <summary>
    /// 手工给某一句贴 / 摘标签（AI 自动贴的经常差一句）。改完把整条的标签表重算成各句的并集，
    /// 免得「按标签检索」和「界面上看到的」各说一套。
    /// </summary>
    public async Task<SentenceTagChange> ToggleSentenceTagAsync(string charId, string memoryId, int sentenceIndex, string tag)
    {
        await EnsureLoadedAsync();
        var name = tag?.Trim() ?? "";
        if (name.Length is 0 or > 12) return SentenceTagChange.None;
        var change = SentenceTagChange.None;
        lock (_gate)
        {
            var at = _cache.FindIndex(m => m.CharacterId == charId && m.Id == memoryId);
            if (at < 0) return SentenceTagChange.None;
            var e = _cache[at];
            // 老数据没有句级标签：先按现文本自动铺一遍，再在这份副本上改
            var marks = (e.SentenceTags.Count > 0
                    ? e.SentenceTags.Select(s => s with { Tags = s.Tags.ToList() })
                    : HumanMemoryMath.AutoTag(e.Text, e.Tags)).ToList();
            var hit = marks.FindIndex(m => m.Index == sentenceIndex);
            var on = hit >= 0 && marks[hit].Tags.Contains(name, StringComparer.OrdinalIgnoreCase);
            if (on)
            {
                marks[hit].Tags.RemoveAll(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
                if (marks[hit].Tags.Count == 0) marks.RemoveAt(hit);
                change = SentenceTagChange.Removed;
            }
            else
            {
                if (hit < 0) marks.Add(new SentenceTag { Index = sentenceIndex, Tags = new List<string>() });
                marks[marks.FindIndex(m => m.Index == sentenceIndex)].Tags.Add(name);
                change = SentenceTagChange.Added;
            }
            _cache[at] = e with
            {
                SentenceTags = marks.OrderBy(m => m.Index).ToList(),
                Tags = marks.SelectMany(m => m.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
        }
        if (change != SentenceTagChange.None) await PersistAsync();
        return change;
    }

    /// <summary>同一件事第二次被记起：两份配比加权合成，越常想起的那份越重。</summary>
    private static EmotionMix? MergeEmotion(EmotionMix? old, EmotionMix? fresh)
    {
        if (old is null || old.IsEmpty) return fresh;
        if (fresh is null || fresh.IsEmpty) return old;
        return EmotionMix.Merge(new[] { (old, 1.0), (fresh, 1.0) });
    }

    /// <summary>人工改这件事的情绪配比（本地词表读错的时候，人可以说算什么）。</summary>
    public async Task<bool> SetEmotionAsync(string charId, string memoryId, EmotionMix mix)
    {
        await EnsureLoadedAsync();
        lock (_gate)
        {
            var at = _cache.FindIndex(m => m.CharacterId == charId && m.Id == memoryId);
            if (at < 0) return false;
            _cache[at] = _cache[at] with { Emotion = mix };
        }
        await PersistAsync();
        return true;
    }

    /// <summary>
    /// 搜事件：话里、标签里、情绪配比里、甚至「哪天」都算命中。
    /// 人找回一件事从来不是按相关度找的，是「那天…」「因为…」「有点委屈的」——
    /// 所以时间说法和情绪名字也得能搜，不然搜出来的只是全文检索。
    /// </summary>
    public async Task<List<HumanMemoryHit>> SearchAsync(string charId, string? query, int limit = 80)
    {
        await EnsureLoadedAsync();
        var q = (query ?? "").Trim();
        List<HumanMemoryEntry> pool;
        lock (_gate) pool = _cache.Where(m => m.CharacterId == charId).ToList();
        if (string.IsNullOrEmpty(q))
            return pool.OrderByDescending(m => m.CreatedAt).Take(Math.Max(1, limit))
                .Select(m => new HumanMemoryHit(m, 0, "")).ToList();

        var hits = new List<HumanMemoryHit>();
        foreach (var m in pool)
        {
            var score = 0.0;
            var why = new List<string>();
            if (Contains(m.Text, q)) { score += 3; why.Add("话里说到"); }
            var tag = m.Tags.FirstOrDefault(t => Contains(t, q));
            if (tag is not null) { score += 2.2; why.Add("贴着「" + tag + "」"); }
            var feel = m.Emotion?.Parts.FirstOrDefault(p => Contains(p.Name, q));
            if (feel is not null) { score += 1.6 + feel.Ratio; why.Add($"那份情绪里「{feel.Name}」占{(int)Math.Round(feel.Ratio * 10)}成"); }
            if (m.ImagePath.Length > 0 && Contains(System.IO.Path.GetFileNameWithoutExtension(m.ImagePath), q))
            { score += 1.2; why.Add("照片名对上"); }
            if (DateHit(m.CreatedAt, q)) { score += 1.8; why.Add("就是那天"); }
            if (score <= 0) continue;
            hits.Add(new HumanMemoryHit(m, score + m.Importance, string.Join(" · ", why)));
        }
        return hits
            .OrderByDescending(h => h.Score)
            .ThenByDescending(h => h.Entry.CreatedAt)
            .Take(Math.Max(1, limit))
            .ToList();
    }

    private static bool Contains(string? haystack, string needle)
        => !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>「8月24」「2026-08-24」「24号」这类说法命中那一天（单日数字太容易误命中，不算）。</summary>
    private static bool DateHit(DateTime at, string q)
    {
        if (q.Length < 2) return false;
        var u = at.ToLocalTime();
        var forms = new[]
        {
            $"{u.Year}-{u.Month:00}-{u.Day:00}", $"{u.Year}/{u.Month}/{u.Day}",
            $"{u.Month}月{u.Day}日", $"{u.Month}月{u.Day}号", $"{u.Day}号", $"{u.Day}日"
        };
        foreach (var f in forms) if (Contains(f, q)) return true;
        return false;
    }

    public async Task<int> CountAsync(string charId)
    {
        await EnsureLoadedAsync();
        lock (_gate) return _cache.Count(m => m.CharacterId == charId);
    }
}
