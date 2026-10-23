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
    private readonly SettingsManager? _settings;
    private readonly object _gate = new();
    private List<HumanMemoryEntry> _cache = new();
    private Task? _loading;

    /// <summary>整表读盘失败过：这时落盘会把整个存档的记忆抹成只剩新写的那几条。</summary>
    private bool _loadFaulted;
    private string _scopeKey = StoreName;

    public HumanMemoryService(StorageProvider store, MemoryScope scope, SettingsManager? settings = null)
    {
        _store = store;
        _scope = scope;
        _settings = settings;
        _scopeKey = scope.Key(StoreName);
        scope.Changed += OnScopeChanged;
    }

    /// <summary>超省内存模式：每次读完就丢缓存，条目只活在这次调用的返回里。</summary>
    public bool AutoRelease { get; set; }

    /// <summary>超省内存模式：把内存里的条目交还给系统，下次用时再从盘上读。</summary>
    public void ReleaseCache()
    {
        lock (_gate)
        {
            _cache = new();
            _loading = null;
            _loadFaulted = false;
        }
    }

    /// <summary>换档/换开关：上一个存档的记忆不能留在缓存里，否则会被写进这个存档的文件。</summary>
    private void OnScopeChanged()
    {
        lock (_gate)
        {
            _cache = new();
            _loading = null;
            _loadFaulted = false;
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
            lock (_gate) { if (saved is not null && key == _scopeKey) { _cache = saved; _loadFaulted = false; } }
        }
        catch (Exception ex)
        {
            // 读不通的时候表是空的。这时候落盘 = 整个存档的记忆只剩这一次新写的条目，
            // 所以标记故障并放开重读（多数是被云同步/杀毒占用了一瞬），宁可这次没记住，不可把旧的抹了。
            _loadFaulted = true;
            lock (_gate) _loading = null;
            App.WriteLog("HumanMemory.Load -> " + ex.Message);
        }
    }

    private Task PersistAsync()
    {
        List<HumanMemoryEntry> snapshot;
        string key;
        lock (_gate)
        {
            if (_loadFaulted)
            {
                App.WriteLog("HumanMemory.Persist -> 已跳过落盘：这一档的记忆没能读出来，不拿空表盖盘");
                return Task.CompletedTask;
            }
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
        var emotion = EmotionMix.FromText(line);
        var intensity = HumanMemoryMath.EmotionalIntensityOf(emotion);
        var isTrauma = LooksLikeTrauma(line, intensity);
        var coreThreshold = _settings?.Current.CoreMemoryThreshold ?? 0.82;
        var coreEnabled = _settings?.Current.CoreMemoryEnabled ?? true;
        var importanceValue = importance ?? HumanMemoryMath.EstimateImportance(line);
        var entry = new HumanMemoryEntry
        {
            CharacterId = charId,
            Text = line,
            ImagePath = imagePath ?? "",
            Kind = kind ?? HumanMemoryMath.GuessKind(line, imagePath ?? ""),
            Importance = importanceValue,
            Tags = clean,
            SentenceTags = HumanMemoryMath.AutoTag(line, clean),
            Emotion = emotion,
            EmotionalIntensity = intensity,
            Associations = BuildAssociations(line, clean),
            IsTrauma = isTrauma,
            TraumaStage = isTrauma ? HumanMemoryMath.TraumaStageOf(intensity, 0) : 0,
            IsCore = coreEnabled && HumanMemoryMath.CoreEligible(importanceValue, intensity, isTrauma, coreThreshold)
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
                var mergedEmotion = MergeEmotion(dup.Emotion, entry.Emotion);
                var mergedIntensity = Math.Max(dup.EmotionalIntensity,
                    HumanMemoryMath.EmotionalIntensityOf(mergedEmotion, 0.35));
                var mergedTrauma = dup.IsTrauma || entry.IsTrauma;
                _cache[idx] = dup with
                {
                    Importance = Math.Max(dup.Importance, entry.Importance),
                    SentenceTags = merged.OrderBy(m => m.Index).ToList(),
                    Emotion = mergedEmotion,
                    EmotionalIntensity = mergedIntensity,
                    Associations = dup.Associations.Concat(entry.Associations)
                        .Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList(),
                    IsTrauma = mergedTrauma,
                    TraumaStage = mergedTrauma
                        ? HumanMemoryMath.TraumaStageOf(mergedIntensity, dup.TraumaHeals)
                        : dup.TraumaStage,
                    IsCore = dup.IsCore || entry.IsCore,
                    FusedIntoId = "",
                    FusedAt = null,
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
        await MaintainAsync(charId, windowDays, now);

        lock (_gate) pool = _cache.Where(m => m.CharacterId == charId && !m.IsForgotten).ToList();

        // 核心记忆：永远在场。它们不参与打分、不受时间影响，也不占用普通条目的名额。
        var cores = pool.Where(m => m.IsCore).ToList();
        if (requiredTags is not null)
        {
            var need = requiredTags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
            if (need.Count > 0)
                cores = cores.Where(m => need.All(t => m.Tags.Contains(t, StringComparer.OrdinalIgnoreCase))).ToList();
        }
        cores = cores.OrderByDescending(m => m.IsTrauma ? 1 : 0)
            .ThenByDescending(m => m.EmotionalIntensity)
            .ThenByDescending(m => m.CreatedAt)
            .Take(8)
            .ToList();
        var coreIds = cores.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

        // 核心记忆的「辐射」：与核心共享字词/标签的普通记忆会被锚定，权重上调。
        double Radiation(HumanMemoryEntry m)
        {
            if (cores.Count == 0) return 1;
            var boost = 1.0;
            foreach (var c in cores)
            {
                var sharedTags = c.Tags.Intersect(m.Tags, StringComparer.OrdinalIgnoreCase).Count();
                var overlap = HumanMemoryMath.Overlap(c.Text, m.Text);
                boost += Math.Min(0.35, sharedTags * 0.12 + overlap * 0.25);
            }
            return Math.Min(1.6, boost);
        }

        var candidates = pool.Where(m => !m.IsCore).ToList();
        var scored = candidates
            .Select(m => (Entry: m, Score: HumanMemoryMath.Score(m, windowDays, randomness, now) * Radiation(m)))
            .Where(x => x.Score >= HumanMemoryMath.MinWeight)
            .OrderByDescending(x => x.Score)
            .Take(Math.Max(1, limit))
            .ToList();

        // 复述机制：只要被检索到（进入候选池，哪怕最后没被用上），次数就 +1。
        // 核心记忆完全豁免——它们不靠「常被想起」维持存在。
        var touched = candidates
            .Where(m => HumanMemoryMath.Score(m, windowDays, randomness, now) >= HumanMemoryMath.MinWeight)
            .Select(m => m.Id)
            .ToHashSet(StringComparer.Ordinal);
        touched.UnionWith(scored.Select(x => x.Entry.Id));
        if (touched.Count > 0 || cores.Count > 0)
        {
            lock (_gate)
            {
                foreach (var id in touched)
                {
                    var idx = _cache.FindIndex(m => m.Id == id && !m.IsCore);
                    if (idx >= 0)
                        _cache[idx] = _cache[idx] with
                        {
                            RecallCount = _cache[idx].RecallCount + 1,
                            LastRecalledAt = now
                        };
                }
                // 核心记忆只更新「上次想起」，不增加复述次数
                foreach (var c in cores)
                {
                    var idx = _cache.FindIndex(m => m.Id == c.Id);
                    if (idx >= 0) _cache[idx] = _cache[idx] with { LastRecalledAt = now };
                }
            }
            await PersistAsync();
        }

        var result = cores.Concat(scored.Select(x => x.Entry))
            .Where(m => !coreIds.Contains(m.Id) || cores.Contains(m))
            .GroupBy(m => m.Id)
            .Select(g => g.First())
            .ToList();
        if (AutoRelease) ReleaseCache();
        return result;
    }

    /// <summary>只在写锁里做一次廉价维护：遗忘融合 + 创伤随时间的极小衰减（默认一个月才动一次）。</summary>
    private DateTime _lastMaintain = DateTime.MinValue;
    private async Task MaintainAsync(string charId, double windowDays, DateTime now)
    {
        if ((now - _lastMaintain).TotalHours < 6) return;
        _lastMaintain = now;
        var changed = false;
        lock (_gate)
        {
            var pool = _cache.Where(m => m.CharacterId == charId && !m.IsForgotten).ToList();
            // 遗忘融合：久远且已经很淡的普通记忆，化进最相近的一条记忆里（不删除原条目）
            if (windowDays > 0)
            {
                foreach (var m in pool.Where(m => !m.IsCore && !m.IsTrauma))
                {
                    var age = Math.Max(0, (now - m.CreatedAt).TotalDays);
                    var weight = HumanMemoryMath.Decay(age, windowDays, m.Importance, m.RecallCount) * m.Importance;
                    if (weight >= HumanMemoryMath.MinWeight * 1.5 || age < windowDays * 2) continue;
                    var target = pool
                        .Where(x => x.Id != m.Id && !x.IsForgotten)
                        .Select(x => (Entry: x, Edge: HumanMemoryMath.ChainEdge(m, x, now).Score))
                        .Where(x => x.Edge >= 0.18)
                        .OrderByDescending(x => x.Edge)
                        .Select(x => x.Entry)
                        .FirstOrDefault();
                    if (target is null) continue;
                    var idx = _cache.FindIndex(x => x.Id == m.Id);
                    var tidx = _cache.FindIndex(x => x.Id == target.Id);
                    if (idx < 0 || tidx < 0) continue;
                    _cache[idx] = _cache[idx] with { FusedIntoId = target.Id, FusedAt = now };
                    _cache[tidx] = _cache[tidx] with
                    {
                        Tags = target.Tags.Concat(m.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList(),
                        Associations = target.Associations.Concat(m.Associations)
                            .Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList(),
                        Importance = Math.Min(1, Math.Max(target.Importance, m.Importance * 0.65)),
                        Emotion = MergeEmotion(target.Emotion, m.Emotion)
                    };
                    changed = true;
                }
            }

            // 创伤：时间的影响刻意做得极小（一个月约 0.015），真正起作用的是事件/互动
            if (_settings?.Current.TraumaHealingEnabled != false)
            {
                foreach (var m in pool.Where(m => m.IsTrauma && m.EmotionalIntensity > 0.2))
                {
                    var ageDays = Math.Max(0, (now - m.CreatedAt).TotalDays);
                    var decayed = Math.Max(0.15, m.EmotionalIntensity - ageDays * 0.0005);
                    if (Math.Abs(decayed - m.EmotionalIntensity) < 0.0001) continue;
                    var idx = _cache.FindIndex(x => x.Id == m.Id);
                    if (idx < 0) continue;
                    _cache[idx] = _cache[idx] with
                    {
                        EmotionalIntensity = decayed,
                        TraumaStage = HumanMemoryMath.TraumaStageOf(decayed, _cache[idx].TraumaHeals)
                    };
                    changed = true;
                }
            }
        }
        if (changed) await PersistAsync();
    }

    /// <summary>全部核心记忆（含创伤）：记忆总览与提示词用。</summary>
    public async Task<List<HumanMemoryEntry>> CoreAsync(string charId)
    {
        await EnsureLoadedAsync();
        lock (_gate) return _cache.Where(m => m.CharacterId == charId && m.IsCore && !m.IsForgotten)
            .OrderByDescending(m => m.IsTrauma)
            .ThenByDescending(m => m.EmotionalIntensity)
            .ThenByDescending(m => m.CreatedAt)
            .ToList();
    }

    /// <summary>人工/工具指定一条记忆为核心（永不衰减、永远可被想起）。</summary>
    public async Task<bool> SetCoreAsync(string charId, string memoryId, bool core)
    {
        await EnsureLoadedAsync();
        lock (_gate)
        {
            var idx = _cache.FindIndex(m => m.CharacterId == charId && m.Id == memoryId);
            if (idx < 0) return false;
            _cache[idx] = _cache[idx] with
            {
                IsCore = core,
                // 升为核心时把重要性补满：它不是「比较重要」，是人格的锚点
                Importance = core ? 1.0 : _cache[idx].Importance,
                EmotionalIntensity = core ? Math.Max(0.7, _cache[idx].EmotionalIntensity) : _cache[idx].EmotionalIntensity
            };
        }
        await PersistAsync();
        return true;
    }

    /// <summary>
    /// 创伤疗愈：只在具体事件/互动里调用，情绪强度缓慢下降；记忆本身永远保留。
    /// 返回 (是否成功, 说明, 阶段, 新强度)。阶段从「应激」走到「融入人格」。
    /// </summary>
    public async Task<(bool Ok, string Message, int Stage, double Intensity)> HealTraumaAsync(
        string charId, string memoryIdOrHint, string eventKind = "陪伴", double strength = 1.0)
    {
        if (_settings?.Current.TraumaHealingEnabled == false)
            return (false, "设置里关闭了创伤疗愈。", 0, 0);
        await EnsureLoadedAsync();
        HumanMemoryEntry? target;
        lock (_gate)
            target = _cache.FirstOrDefault(m => m.CharacterId == charId && m.Id == memoryIdOrHint)
                     ?? _cache.FirstOrDefault(m => m.CharacterId == charId && m.IsTrauma
                         && m.Text.Contains(memoryIdOrHint ?? "", StringComparison.OrdinalIgnoreCase));
        if (target is null || !target.IsTrauma)
            return (false, "没有找到这条创伤记忆。", 0, 0);

        var effect = eventKind switch
        {
            "和解" => 0.18,
            "重逢" => 0.15,
            "理解" => 0.12,
            "安慰" => 0.10,
            "陪伴" => 0.08,
            "重新经历" => 0.06,
            _ => 0.06
        } * Math.Clamp(strength, 0.2, 3.0);

        var old = target.EmotionalIntensity;
        var next = Math.Clamp(old - effect, 0.12, 1.0);
        var heals = target.TraumaHeals + 1;
        var stage = HumanMemoryMath.TraumaStageOf(next, heals);
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            var idx = _cache.FindIndex(m => m.Id == target.Id);
            if (idx >= 0)
                _cache[idx] = _cache[idx] with
                {
                    EmotionalIntensity = next,
                    TraumaHeals = heals,
                    LastHealedAt = now,
                    TraumaStage = stage,
                    Tags = _cache[idx].Tags.Concat(new[] { "疗愈中" }).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                };
        }
        await PersistAsync();
        var change = old - next;
        var message = change < 0.01
            ? "这次只是陪了一会儿，伤还在原处，但没那么尖了。"
            : $"情绪强度从 {old:0.00} 降到 {next:0.00}；现在是「{HumanMemoryMath.TraumaStageName(stage)}」。这件事没有被忘掉，只是被放回了它该在的位置。";
        return (true, message, stage, next);
    }

    /// <summary>
    /// 链式搜索：从一个线索出发，沿共享字词/标签/相近时间/相同情绪一条条找下去。
    /// 默认 5 条；新的在前，同一天里越相似越前。
    /// </summary>
    public async Task<HumanChainResult> ChainSearchAsync(string charId, string clue, int limit = 5)
    {
        var q = (clue ?? "").Trim();
        if (q.Length == 0) return new HumanChainResult(Array.Empty<HumanChainHit>(), "线索为空。");
        await EnsureLoadedAsync();
        List<HumanMemoryEntry> pool;
        lock (_gate) pool = _cache.Where(m => m.CharacterId == charId && !m.IsForgotten).ToList();
        if (pool.Count == 0) return new HumanChainResult(Array.Empty<HumanChainHit>(), "还没有可以想起来的记忆。");

        var now = DateTime.UtcNow;
        var clueUnits = HumanMemoryMath.Units(q);
        double ClueScore(HumanMemoryEntry m)
        {
            var s = 0.0;
            if (m.Text.Contains(q, StringComparison.OrdinalIgnoreCase)) s += 2.5;
            if (m.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase))) s += 1.8;
            if (m.Associations.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase))) s += 1.2;
            if (clueUnits.Count > 0)
                s += clueUnits.Count(u => m.Text.Contains(u, StringComparison.OrdinalIgnoreCase)) * 0.35;
            return s;
        }

        var seeds = pool
            .Select(m => (Entry: m, Score: ClueScore(m)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Entry.CreatedAt)
            .Take(4)
            .ToList();
        if (seeds.Count == 0)
            return new HumanChainResult(Array.Empty<HumanChainHit>(), $"没有关于「{q}」的记忆。");

        var hits = new Dictionary<string, HumanChainHit>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new List<(HumanMemoryEntry Entry, double Score)>();
        foreach (var (entry, score) in seeds)
        {
            var why = entry.Text.Contains(q, StringComparison.OrdinalIgnoreCase) ? "话里直接提到"
                : entry.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)) ? "标签对上"
                : "字词相近";
            hits[entry.Id] = new HumanChainHit(entry, score + entry.Importance, why, 0, q);
            visited.Add(entry.Id);
            frontier.Add((entry, score));
        }

        // 两跳扩散：一跳找到「当时的上下文」，两跳找到「因此连着的另一件事」
        for (var depth = 1; depth <= 2; depth++)
        {
            var next = new List<(HumanMemoryEntry Entry, double Score)>();
            foreach (var (from, fromScore) in frontier)
            {
                foreach (var m in pool)
                {
                    if (visited.Contains(m.Id)) continue;
                    var (edge, why) = HumanMemoryMath.ChainEdge(from, m, now);
                    if (edge < 0.12) continue;
                    var score = fromScore * 0.55 + edge + ClueScore(m) * 0.7
                                + m.Importance * 0.25 + (m.IsCore ? 0.8 : 0);
                    var bridge = Shorten(from.Text, 18) + " → " + why;
                    var prev = hits.TryGetValue(m.Id, out var old) && old.Score >= score;
                    if (prev) continue;
                    hits[m.Id] = new HumanChainHit(m, score, why, depth, bridge);
                    next.Add((m, score));
                }
            }
            foreach (var n in next) visited.Add(n.Entry.Id);
            frontier = next.OrderByDescending(x => x.Score).Take(6).ToList();
            if (frontier.Count == 0) break;
        }

        var ordered = HumanMemoryMath.OrderChain(hits.Values)
            .Take(Math.Clamp(limit <= 0 ? 5 : limit, 1, 20))
            .ToList();

        // 检索即复述：链上出现的普通记忆次数 +1（核心记忆豁免）
        if (ordered.Count > 0)
        {
            lock (_gate)
            {
                foreach (var h in ordered.Where(h => !h.Entry.IsCore))
                {
                    var idx = _cache.FindIndex(m => m.Id == h.Entry.Id);
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
        var summary = ordered.Count == 0 ? "没有连起来的记忆。"
            : $"从「{q}」连出 {ordered.Count} 条，共 {ordered.Max(h => h.Depth) + 1} 层。";
        return new HumanChainResult(ordered, summary);
    }

    private static string Shorten(string? text, int max)
    {
        var t = (text ?? "").Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }

    /// <summary>把一条记忆的关联词并进去（链式搜索的桥）。</summary>
    public async Task<bool> AddAssociationsAsync(string charId, string memoryId, IEnumerable<string> words)
    {
        await EnsureLoadedAsync();
        var add = (words ?? Array.Empty<string>()).Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(w => w.Trim()).Take(8).ToList();
        if (add.Count == 0) return false;
        lock (_gate)
        {
            var idx = _cache.FindIndex(m => m.CharacterId == charId && m.Id == memoryId);
            if (idx < 0) return false;
            _cache[idx] = _cache[idx] with
            {
                Associations = _cache[idx].Associations.Concat(add)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToList()
            };
        }
        await PersistAsync();
        return true;
    }

    /// <summary>情绪创伤的本地识别：只认强线索，避免把普通难过全都变成创伤。</summary>
    private static bool LooksLikeTrauma(string text, double intensity)
    {
        if (intensity < 0.55) return false;
        var cues = new[] { "阴影", "噩梦", "崩溃", "抛弃", "背叛", "虐待", "暴力", "伤害", "死", "失去",
            "车祸", "自杀", "性侵", "殴打", "关起来", "害怕得", "不敢睡", "创伤", "恐惧" };
        return cues.Any(c => text.Contains(c, StringComparison.Ordinal));
    }

    /// <summary>从一句话里抽关联词：标签 + 引号/书名号里的词 + 高频两字词（保守，最多 8 个）。</summary>
    private static List<string> BuildAssociations(string text, IEnumerable<string> tags)
    {
        var list = tags.Where(t => !string.IsNullOrWhiteSpace(t)).Take(6).ToList();
        try
        {
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(text, "[「『“\"]([^」』”\"]{1,8})[」』”\"]"))
            {
                var v = m.Groups[1].Value.Trim();
                if (v.Length > 0 && !list.Contains(v, StringComparer.OrdinalIgnoreCase)) list.Add(v);
            }
        }
        catch { }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
    }

    /// <summary>提示词文本：像人一样「想起」而不是「检索出记录」。</summary>
    public async Task<string> RecallTextAsync(string charId, double windowDays, double randomness, int limit = 6)
    {
        var cores = await CoreAsync(charId);
        var hits = await RecallAsync(charId, windowDays, randomness, limit);
        var coreIds = cores.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        hits = hits.Where(h => !coreIds.Contains(h.Id)).ToList();
        if (hits.Count == 0 && cores.Count == 0) return "";

        var sb = new System.Text.StringBuilder();
        if (cores.Count > 0)
        {
            sb.AppendLine("刻在骨子里的事（它们不会被时间冲淡，一直在塑造你此刻是什么样的人）：");
            foreach (var c in cores.Take(6))
            {
                var tag = c.IsTrauma
                    ? $"（创伤 · {HumanMemoryMath.TraumaStageName(c.TraumaStage)}）"
                    : "（核心）";
                sb.AppendLine($"· {tag}{c.Text}");
            }
        }
        if (hits.Count > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("你脑子里浮起来的一些事（不是别人告诉你的资料，是你自己记得的）：");
            foreach (var h in hits)
            {
                var line = h.Kind switch
                {
                    HumanMemoryKind.Image => $"· [照片]{h.Text}" + (string.IsNullOrEmpty(h.ImagePath) ? "" : $"（{Path.GetFileName(h.ImagePath)}）"),
                    HumanMemoryKind.Important => $"· （记得很牢）{h.Text}",
                    HumanMemoryKind.Abstraction => $"· （印象）{h.Text}",
                    _ => "· " + h.Text
                };
                if (h.EmotionalIntensity >= 0.7 && h.Emotion is { IsEmpty: false })
                    line += $"（想起时仍有「{h.Emotion.Text}」）";
                sb.AppendLine(line);
            }
        }
        sb.AppendLine("引用时用自然的口气，不要逐条念；想不起来就承认想不起来。");
        return sb.ToString().TrimEnd();
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
            var assoc = m.Associations.FirstOrDefault(a => Contains(a, q));
            if (assoc is not null) { score += 1.4; why.Add("想起它会带出「" + assoc + "」"); }
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
        int count;
        lock (_gate) count = _cache.Count(m => m.CharacterId == charId);
        if (AutoRelease) ReleaseCache();
        return count;
    }
}
