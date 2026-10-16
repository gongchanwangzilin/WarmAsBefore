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
    private const string StoreKey = "human_memories";

    private readonly StorageProvider _store;
    private readonly object _gate = new();
    private List<HumanMemoryEntry> _cache = new();
    private bool _loaded;

    public HumanMemoryService(StorageProvider store) => _store = store;

    private async Task EnsureLoadedAsync()
    {
        lock (_gate)
        {
            if (_loaded) return;
            _loaded = true;
        }
        try
        {
            var saved = await _store.Load<List<HumanMemoryEntry>>(StoreKey);
            lock (_gate) { if (saved is not null) _cache = saved; }
        }
        catch (Exception ex)
        {
            App.WriteLog("HumanMemory.Load -> " + ex.Message);
        }
    }

    private Task PersistAsync()
    {
        List<HumanMemoryEntry> snapshot;
        lock (_gate) snapshot = _cache.ToList();
        return _store.Save(StoreKey, snapshot);
    }

    /// <summary>记下一件事（默认按内容推测类型与重要性）。</summary>
    public async Task StoreAsync(string charId, string text, HumanMemoryKind? kind = null,
        double? importance = null, string imagePath = "", IEnumerable<string>? tags = null)
    {
        if (string.IsNullOrWhiteSpace(charId) || string.IsNullOrWhiteSpace(text)) return;
        await EnsureLoadedAsync();

        var line = text.Trim();
        if (line.Length > 60) line = line[..60];
        var entry = new HumanMemoryEntry
        {
            CharacterId = charId,
            Text = line,
            ImagePath = imagePath ?? "",
            Kind = kind ?? HumanMemoryMath.GuessKind(line, imagePath ?? ""),
            Importance = importance ?? HumanMemoryMath.EstimateImportance(line),
            Tags = tags is null ? new List<string>() : tags.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList()
        };

        lock (_gate)
        {
            // 同一角色 + 同一句话不重复记：人不会把同一件事记两遍
            var dup = _cache.FirstOrDefault(m => m.CharacterId == charId && m.Text == entry.Text);
            if (dup is not null)
            {
                var idx = _cache.IndexOf(dup);
                _cache[idx] = dup with { Importance = Math.Max(dup.Importance, entry.Importance) };
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

    /// <summary>让她忘掉一条记忆（人工修正：记错了、不该记的都删得掉）。</summary>
    public async Task<bool> ForgetAsync(string charId, string memoryId)
    {
        await EnsureLoadedAsync();
        bool removed;
        lock (_gate) removed = _cache.RemoveAll(m => m.CharacterId == charId && m.Id == memoryId) > 0;
        if (removed) await PersistAsync();
        return removed;
    }

    public async Task<int> CountAsync(string charId)
    {
        await EnsureLoadedAsync();
        lock (_gate) return _cache.Count(m => m.CharacterId == charId);
    }
}
