using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 「复制角色 · 连记忆一起复制」：一个角色的记忆散落在哪几份文件里、怎么整份搬给副本。
///
/// 三条规矩，缺一条这个功能就是假的：
/// · <b>只在当前作用域里复制</b>：每一份都从 <see cref="MemoryScope.Key"/> 出来的那份文件读、
///   写回同一份文件，换的只有 charId。副本与原角色共享同一个存档的记忆边界，
///   跨存档的一律不搬 —— 开没开「跨存档共用记忆」都成立。
/// · <b>引用全部重映射</b>：条目上的 CharacterId、重发的人类记忆 Id，
///   以及关系网里指向人类记忆 Id 的节点 Id / Ref、边两端、断边名单
///   （<c>RelationNetworkService.SyncFromMemoriesAsync</c> 建记忆节点用的就是 <c>e.Id</c>，
///   见 <see cref="RelationNode.Ref"/>）。不重映射的话，副本的网会指着原角色的记忆。
/// · <b>先全量算完再落盘</b>：算的阶段只读盘，任何异常都发生在第一次写之前；
///   写途中出错就把已经写过的每一份撤回原样，角色本体放在最后一步注册，
///   不留「有名字没记忆」或「有记忆没名字」的半成品。原角色的数据只有追加，没有改动。
/// </summary>
public sealed partial class CharacterLibrary
{
    // ==== 各记忆服务把 store 名留成了 private const，这里只能照抄一份；改那边要记得改这里 ====
    private const string HumanStore = "human_memories";        // HumanMemoryService.StoreName
    private const string TagStore = "memory_tags";             // MemoryTagService.StoreName
    private const string NetStore = "relation_networks";       // RelationNetworkService.StoreKey
    private const string NetParamsStore = "relation_params";   // RelationNetworkService.ParamsKey
    private const string NetStatsStore = "relation_stats";     // RelationNetworkService.StatsKey
    private const string VaultStore = "memories";              // MemoryVault 的全量对话记忆库

    /// <summary>一次连记忆复制的规模：提示里要让用户看见到底带走了什么，而不是「复制成功」。</summary>
    public sealed record MemoryCloneCounts
    {
        public int HumanMemories { get; init; }
        public int Tags { get; init; }
        public int GraphNodes { get; init; }
        public int GraphEdges { get; init; }
        public int VaultEntries { get; init; }
        public int DiaryNotes { get; init; }

        public bool IsEmpty => HumanMemories == 0 && Tags == 0 && GraphNodes == 0
                               && VaultEntries == 0 && DiaryNotes == 0;

        public string Describe() =>
            $"记得的事 {HumanMemories} 条 · 标签 {Tags} 个 · 关系网 {GraphNodes} 点 {GraphEdges} 线"
            + $" · 全量记忆 {VaultEntries} 条 · 日记 {DiaryNotes} 篇";
    }

    /// <summary>一份算好、尚未落盘的写入；Undo 用的是一开始读到的那份原始数据（原本没有这个文件就删掉）。</summary>
    private sealed record PendingWrite(string Key, Func<Task> Apply, Func<Task> Undo);

    private sealed record MemoryWrites(List<PendingWrite> Items, MemoryCloneCounts Counts);

    /// <summary>
    /// 把当前作用域里属于 <paramref name="sourceId"/> 的记忆全部算成副本要写的内容。
    /// 只读盘，不写盘。
    /// </summary>
    private async Task<(bool ok, string message, MemoryWrites? writes)> BuildMemoryWritesAsync(string sourceId, string destId)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(destId) || sourceId == destId)
            return (false, "角色编号不对，没法复制记忆。", null);

        var scope = _scope;
        var writes = new List<PendingWrite>();
        int human = 0, tags = 0, nodes = 0, edges = 0, vault = 0, diary = 0;

        // ---- 人类记忆：条目自带 CharacterId；Id 一律重发，关系网就是拿它当引文的 ----
        var humanKey = scope.Key(HumanStore);
        var humans = await _store.Load<List<HumanMemoryEntry>>(humanKey) ?? new List<HumanMemoryEntry>();
        var memoryIds = new Dictionary<string, string>(StringComparer.Ordinal);   // 旧记忆 Id → 新记忆 Id
        var freshHumans = new List<HumanMemoryEntry>();
        foreach (var m in humans.Where(x => x.CharacterId == sourceId))
        {
            var fresh = Guid.NewGuid().ToString("N")[..12];   // 与 HumanMemoryEntry.Id 同格式
            if (!string.IsNullOrWhiteSpace(m.Id)) memoryIds[m.Id] = fresh;
            freshHumans.Add(m with
            {
                Id = fresh,
                CharacterId = destId,
                // 列表是可变对象，副本和原件不能共用同一份实例
                Tags = m.Tags.ToList(),
                SentenceTags = m.SentenceTags.Select(s => s with { Tags = s.Tags.ToList() }).ToList(),
                Emotion = CloneEmotion(m.Emotion)
            });
        }
        if (freshHumans.Count > 0)
        {
            human = freshHumans.Count;
            writes.Add(MakeWrite(humanKey, Append(humans, freshHumans), humans));
        }

        // ---- 标签表：按 charId 分格的字典，格子整个搬一份（MemoryTag 里没有 charId，也没有 Id）----
        var tagKey = scope.Key(TagStore);
        var tagTable = await _store.Load<Dictionary<string, List<MemoryTag>>>(tagKey);
        if (tagTable is not null && tagTable.TryGetValue(sourceId, out var srcTags)
            && srcTags is { Count: > 0 })
        {
            tags = srcTags.Count;
            var copied = new Dictionary<string, List<MemoryTag>>(tagTable, StringComparer.Ordinal)
            {
                [destId] = srcTags.ToList()
            };
            writes.Add(MakeWrite(tagKey, copied, tagTable));
        }

        // ---- 关系网：字典键换 charId；网内指向记忆 Id 的地方全要跟着重映射 ----
        var netKey = scope.Key(NetStore);
        var nets = await _store.Load<Dictionary<string, RelationGraph>>(netKey);
        if (nets is not null && nets.TryGetValue(sourceId, out var srcGraph) && srcGraph is not null)
        {
            var copy = CloneGraph(srcGraph, memoryIds);
            nodes = copy.Nodes.Count;
            edges = copy.Edges.Count;
            var all = new Dictionary<string, RelationGraph>(nets, StringComparer.Ordinal) { [destId] = copy };
            writes.Add(MakeWrite(netKey, all, nets));
        }

        // ---- 关系网的旋钮与底账：都是 charId → 一份，内容不含角色引用，原样搬 ----
        var paramKey = scope.Key(NetParamsStore);
        var paramTable = await _store.Load<Dictionary<string, RelationParams>>(paramKey);
        if (paramTable is not null && paramTable.TryGetValue(sourceId, out var srcParams) && srcParams is not null)
        {
            var all = new Dictionary<string, RelationParams>(paramTable, StringComparer.Ordinal)
            {
                [destId] = srcParams
            };
            writes.Add(MakeWrite(paramKey, all, paramTable));
        }

        var statKey = scope.Key(NetStatsStore);
        var statTable = await _store.Load<Dictionary<string, RelationStats>>(statKey);
        if (statTable is not null && statTable.TryGetValue(sourceId, out var srcStats) && srcStats is not null)
        {
            var all = new Dictionary<string, RelationStats>(statTable, StringComparer.Ordinal)
            {
                [destId] = new RelationStats
                {
                    Version = srcStats.Version,
                    Turns = srcStats.Turns,
                    DocFreq = new Dictionary<string, int>(srcStats.DocFreq, StringComparer.Ordinal),
                    Cooc = new Dictionary<string, int>(srcStats.Cooc, StringComparer.Ordinal)
                }
            };
            writes.Add(MakeWrite(statKey, all, statTable));
        }

        // ---- 全量记忆库：一份文件装着所有人，条目上的 CharacterId 改写、Id 重发 ----
        var vaultKey = scope.Key(VaultStore);
        var vaultList = await _store.Load<List<MemoryEntry>>(vaultKey) ?? new List<MemoryEntry>();
        var freshVault = vaultList.Where(m => m.CharacterId == sourceId)
            .Select(m => m with { Id = Guid.NewGuid().ToString(), CharacterId = destId })
            .ToList();
        if (freshVault.Count > 0)
        {
            vault = freshVault.Count;
            writes.Add(MakeWrite(vaultKey, Append(vaultList, freshVault), vaultList));
        }

        // ---- 日记：本来就是一个角色一个文件，换键复制；文件内的 CharacterId 也要跟着改 ----
        var srcDiaryKey = DiaryKeyFor(sourceId);
        var destDiaryKey = DiaryKeyFor(destId);
        var diaryNotes = await _store.Load<List<DiaryNote>>(srcDiaryKey);
        if (diaryNotes is { Count: > 0 })
        {
            diary = diaryNotes.Count;
            var freshDiary = diaryNotes
                .Select(d => d with { Id = Guid.NewGuid().ToString(), CharacterId = destId })
                .ToList();
            var existing = await _store.Load<List<DiaryNote>>(destDiaryKey) ?? new List<DiaryNote>();
            writes.Add(MakeWrite(destDiaryKey, Append(existing, freshDiary), existing));
        }

        return (true, "", new MemoryWrites(writes, new MemoryCloneCounts
        {
            HumanMemories = human,
            Tags = tags,
            GraphNodes = nodes,
            GraphEdges = edges,
            VaultEntries = vault,
            DiaryNotes = diary
        }));
    }

    /// <summary>按顺序落盘；中途失败就把已经写过的每一份撤回（存在的写回原样，原本不存在的删掉）。</summary>
    private async Task<bool> ApplyMemoryWritesAsync(MemoryWrites writes)
    {
        var done = new List<PendingWrite>(writes.Items.Count);
        try
        {
            foreach (var w in writes.Items)
            {
                await w.Apply();
                done.Add(w);
            }
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog($"CharacterLibrary.CloneMemory: 落盘失败，回滚 {done.Count} 份 -> {ex.Message}");
            await UndoMemoryWritesAsync(done);
            return false;
        }
    }

    private static async Task UndoMemoryWritesAsync(List<PendingWrite> done)
    {
        for (var i = done.Count - 1; i >= 0; i--)
        {
            try { await done[i].Undo(); }
            catch (Exception ex) { App.WriteLog($"CharacterLibrary.CloneMemory: 回滚 {done[i].Key} 失败 -> {ex.Message}"); }
        }
    }

    private PendingWrite MakeWrite<T>(string key, T updated, T original) where T : class
    {
        // 回滚分两种：这份文件本来就有 → 写回原样；本来没有（角色在这个作用域还没留下这类数据）→ 删掉，
        // 免得留下一个空文件让下次读的人以为真有东西。
        var existed = _store.Exists(key);
        return new PendingWrite(key,
            () => _store.Save(key, updated),
            () =>
            {
                if (!existed)
                {
                    _store.Delete(key);
                    return Task.CompletedTask;
                }
                return _store.Save(key, original);
            });
    }

    /// <summary>日记的键在 MemoryVault 里是私有的，这里照抄它的规则：共用那份沿用旧命名 <c>diary_&lt;角色&gt;</c>。</summary>
    private string DiaryKeyFor(string charId)
        => _scope.IsShared ? $"diary_{charId}" : $"diary/{_scope.Current}/{charId}";

    private static List<T> Append<T>(List<T> original, IEnumerable<T> extra)
    {
        var all = new List<T>(original.Count + extra.Count());
        all.AddRange(original);
        all.AddRange(extra);
        return all;
    }

    private static EmotionMix? CloneEmotion(EmotionMix? mix)
        => mix is null ? null : new EmotionMix { Parts = mix.Parts.ToList() };

    /// <summary>
    /// 复制一张关系网。除了外层字典的键换成新 charId，更要紧的是网内指向人类记忆的那批引用：
    /// 记忆节点的 Id 是「m: + 记忆 Id」、Ref 就是记忆 Id（<c>RelationIds.For</c> 加前缀），
    /// 而边和断边名单记的都是节点 Id，所以记忆 Id 一重发，这三处都要跟着换，
    /// 否则副本的网连着的是原角色的那批记忆，用户一「忘掉」就忘掉别人身上。
    ///
    /// 其余节点（词 / 标签 / 情绪 / 人 / 地点 / 东西）按名字定 Id，与 charId 无关；
    /// 图片节点按图片路径定 Id，也照原样带走 —— 副本和原角色共用同一批素材文件，
    /// 这向来说就是 <see cref="DuplicateAsync"/> 的做法。
    /// </summary>
    private static RelationGraph CloneGraph(RelationGraph src, IReadOnlyDictionary<string, string> memoryIds)
    {
        // 两条路都要认：Ref 存的是完整记忆 Id，节点 Id 是它截断到 24 字加前缀的结果
        var byRef = new Dictionary<string, string>(StringComparer.Ordinal);
        var byNodeId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in memoryIds)
        {
            byRef[kv.Key] = kv.Value;
            byNodeId[RelationIds.For(NodeKind.Memory, kv.Key)] = RelationIds.For(NodeKind.Memory, kv.Value);
        }

        var nodeIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var nodes = new List<RelationNode>(src.Nodes.Count);
        foreach (var n in src.Nodes)
        {
            var target = MemoryNodeIdOf(n, byNodeId, byRef);
            if (target is null)
            {
                nodes.Add(n);                      // 与记忆无关的点：记录本身不可变，直接共用
                continue;
            }
            nodeIdMap[n.Id] = target;
            nodes.Add(n with { Id = target, Ref = byRef.TryGetValue(n.Ref, out var r) ? r : n.Ref });
        }

        var edges = src.Edges
            .Select(e => e with
            {
                From = nodeIdMap.TryGetValue(e.From, out var f) ? f : e.From,
                To = nodeIdMap.TryGetValue(e.To, out var t) ? t : e.To
            })
            .ToList();

        return new RelationGraph
        {
            Version = src.Version,
            Nodes = nodes,
            Edges = edges,
            Pruned = ClonePruned(src.Pruned, nodeIdMap)
        };
    }

    /// <summary>
    /// 这个记忆节点在副本里该用哪个 Id：先按节点 Id 认（它就是「m: + 记忆 Id」截断出来的），
    /// 认不出再按 Ref 认（老数据里两者可能对不上）。都不是本次复制的那批记忆就返回 null，原样带走。
    /// </summary>
    private static string? MemoryNodeIdOf(RelationNode n,
        IReadOnlyDictionary<string, string> byNodeId, IReadOnlyDictionary<string, string> byRef)
    {
        if (n.Kind != NodeKind.Memory) return null;
        if (byNodeId.TryGetValue(n.Id, out var byNode)) return byNode;
        return byRef.TryGetValue(n.Ref, out var freshMemId) ? RelationIds.For(NodeKind.Memory, freshMemId) : null;
    }

    /// <summary>
    /// 「断过」名单是「小 Id ‖ 大 Id」拼出来的（见 RelationNetworkService.Pruned 那条设计说明），
    /// 换了节点 Id 就要按同样的拼法重建，否则副本会把原角色断过的那些联系重新长回来。
    /// 名字里本来就带 ‖ 的（切出来不止两段）认不出哪半是哪半，原样带走，不猜。
    /// </summary>
    private static List<string> ClonePruned(IReadOnlyList<string> pruned, Dictionary<string, string> nodeIdMap)
    {
        const string sep = "‖";
        var result = new List<string>(pruned.Count);
        foreach (var key in pruned)
        {
            var parts = key.Split(new[] { sep }, StringSplitOptions.None);
            if (parts.Length != 2)
            {
                if (!result.Contains(key, StringComparer.Ordinal)) result.Add(key);
                continue;
            }
            var a = nodeIdMap.TryGetValue(parts[0], out var ma) ? ma : parts[0];
            var b = nodeIdMap.TryGetValue(parts[1], out var mb) ? mb : parts[1];
            if (a == parts[0] && b == parts[1])
            {
                if (!result.Contains(key, StringComparer.Ordinal)) result.Add(key);
                continue;
            }
            var rebuilt = string.CompareOrdinal(a, b) <= 0 ? a + sep + b : b + sep + a;
            if (!result.Contains(rebuilt, StringComparer.Ordinal)) result.Add(rebuilt);
        }
        return result;
    }
}
