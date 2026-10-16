using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 标签层：AI 给「人 / 对话 / 事件 / 地点 / 物」打标签，用于记忆分类与检索。
///
/// · 打标签时标签会同时落在人类记忆条目上（<see cref="HumanMemoryEntry.Tags"/>），
///   所以「按标签组合检索」实际检索的是记得住的那些事；
/// · 每个角色维护一张标签表（使用次数、指向对象），注入上下文时取前 10~20 个；
/// · 标签本身也能被回想：<see cref="InjectTextAsync"/> 让角色知道自己是怎么记人的。
/// </summary>
public sealed class MemoryTagService
{
    private const string StoreKey = "memory_tags";
    private const int InjectMin = 10;
    private const int InjectMax = 20;

    private readonly StorageProvider _store;
    private readonly HumanMemoryService _human;
    private readonly object _gate = new();
    private Dictionary<string, List<MemoryTag>> _cache = new(StringComparer.Ordinal);
    private bool _loaded;

    public MemoryTagService(StorageProvider store, HumanMemoryService human)
    {
        _store = store;
        _human = human;
    }

    private async Task EnsureLoadedAsync()
    {
        lock (_gate)
        {
            if (_loaded) return;
            _loaded = true;
        }
        try
        {
            var saved = await _store.Load<Dictionary<string, List<MemoryTag>>>(StoreKey);
            lock (_gate)
            {
                if (saved is not null) _cache = new Dictionary<string, List<MemoryTag>>(saved, StringComparer.Ordinal);
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("MemoryTag.Load -> " + ex.Message);
        }
    }

    private Task PersistAsync()
    {
        Dictionary<string, List<MemoryTag>> snapshot;
        lock (_gate) snapshot = _cache.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.Ordinal);
        return _store.Save(StoreKey, snapshot);
    }

    /// <summary>
    /// 打标签：<paramref name="target"/> 是标签指向的那件事/那段话，命中已有记忆就打在那条上，
    /// 没有就新建一条记忆条目承接这些标签（否则标签会指向空气）。
    /// </summary>
    public async Task<int> ApplyAsync(string charId, IEnumerable<string> tags, string subject = "event",
        string target = "", double importance = 0.5)
    {
        var clean = tags?.Select(t => t?.Trim() ?? "")
            .Where(t => t.Length > 0 && t.Length <= 12)
            .Distinct(StringComparer.Ordinal)
            .Take(6)
            .ToList() ?? new List<string>();
        if (string.IsNullOrWhiteSpace(charId) || clean.Count == 0) return 0;

        await EnsureLoadedAsync();
        lock (_gate)
        {
            if (!_cache.TryGetValue(charId, out var list)) _cache[charId] = list = new List<MemoryTag>();
            foreach (var tag in clean)
            {
                var idx = list.FindIndex(t => string.Equals(t.Name, tag, StringComparison.OrdinalIgnoreCase)
                                              && t.Subject == subject);
                if (idx >= 0)
                {
                    var old = list[idx];
                    list[idx] = old with { Count = old.Count + 1, LastUsedAt = DateTime.UtcNow };
                }
                else
                {
                    list.Add(new MemoryTag { Name = tag, Subject = subject, Count = 1 });
                }
            }
        }
        await PersistAsync();

        // 标签落到记忆上：找不到目标就为标签建一条可被想起来的事
        var text = string.IsNullOrWhiteSpace(target) ? $"（{subject}）{string.Join("、", clean)}" : target.Trim();
        await _human.StoreAsync(charId, text, HumanMemoryMath.GuessKind(text, ""), importance, "", clean);
        return clean.Count;
    }

    /// <summary>按标签（可组合）检索记得住的事；<paramref name="matchAll"/> = true 时要求同时具备全部标签。</summary>
    public async Task<List<HumanMemoryEntry>> SearchAsync(string charId, IEnumerable<string> tags, bool matchAll = true, int limit = 10)
    {
        var need = tags?.Select(t => t?.Trim() ?? "").Where(t => t.Length > 0).ToList() ?? new List<string>();
        if (need.Count == 0) return new List<HumanMemoryEntry>();

        var all = await _human.AllAsync(charId);
        var hits = all.Where(m => m.Tags.Count > 0 && (matchAll
            ? need.All(n => m.Tags.Contains(n, StringComparer.OrdinalIgnoreCase))
            : need.Any(n => m.Tags.Contains(n, StringComparer.OrdinalIgnoreCase))));
        return hits.OrderByDescending(m => m.CreatedAt).Take(Math.Max(1, limit)).ToList();
    }

    /// <summary>某个角色的标签表（按使用次数排序）。</summary>
    public async Task<List<MemoryTag>> ListAsync(string charId)
    {
        await EnsureLoadedAsync();
        lock (_gate)
        {
            if (!_cache.TryGetValue(charId, out var list)) return new List<MemoryTag>();
            return list.OrderByDescending(t => t.Count).ThenByDescending(t => t.LastUsedAt).ToList();
        }
    }

    public async Task<int> RemoveAsync(string charId, string tag)
    {
        await EnsureLoadedAsync();
        lock (_gate)
        {
            if (!_cache.TryGetValue(charId, out var list)) return 0;
            var removed = list.RemoveAll(t => string.Equals(t.Name, tag, StringComparison.OrdinalIgnoreCase));
            if (removed > 0) _ = PersistAsync();
            return removed;
        }
    }

    /// <summary>
    /// 注入用的标签清单：放 10~20 个（够了就不再堆），带了角色才会「记得自己是怎么记人的」。
    /// </summary>
    public async Task<string> InjectTextAsync(string charId, int max = InjectMax)
    {
        var list = await ListAsync(charId);
        if (list.Count == 0) return "";
        var take = Math.Clamp(max, InjectMin, InjectMax);
        var picked = list.Take(take).ToList();
        var bySubject = picked.GroupBy(t => t.Subject).ToList();
        var parts = bySubject.Select(g =>
        {
            var label = g.Key switch
            {
                "person" => "人",
                "talk" => "聊过的话题",
                "place" => "地方",
                "thing" => "东西",
                _ => "事"
            };
            return $"{label}：{string.Join("、", g.Select(t => t.Name))}";
        });
        return "你心里给这些打过标记（是你自己的记法，不是别人给的清单）：" + string.Join("；", parts) + "。"
            + "有人问起时你可以顺着这些标记回忆。";
    }
}
