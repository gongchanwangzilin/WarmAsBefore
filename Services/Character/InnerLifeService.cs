using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 内心生活：内分泌化学池 + 执念池。参考数字生命内核（digital-life-core）的两件核心器官，
/// 但按 WAB 的方式长在这里——不自己改代码、不自举，只让她的心绪真的「有来处」。
///
/// 化学池：多巴胺（兴奋/快乐）、皮质醇（压力/警觉）、血清素（安宁/稳定）三个值 0~1，
/// 每个角色有一份由角色 id 决定的「气质基线」（有人天生血清素高、有人更容易紧张），
/// 事件把值推离基线，时间按半衰期把它们拉回去。值只通过一段「此刻的心绪」进提示词，
/// 不报数字，也不单独成为一轮对话——它改变她说话的语气，而不是抢话。
///
/// 执念池：她说了「不知道 / 说不上来」的地方会长出一颗执念，张力随时间慢慢涨；
/// 涨到阈值就轻轻推她一把——她会自己把那件事想一遍，想明白了主动来讲。
/// 执念不删除，解决了就安静地留在那里。
///
/// 状态存在 <see cref="StorageProvider"/> 的 inner_life 键下，跟着存档走（MemoryScope 隔离）。
/// 关闭「内心生活」后：数值冻结、不出现在提示词里、执念不再醒来。
/// </summary>
public sealed class InnerLifeService
{
    private readonly StorageProvider _store;
    private readonly MemoryScope _scope;
    private readonly SettingsManager _settings;
    private readonly object _gate = new();
    private InnerLifeBook _book = new();
    private bool _loaded;
    private bool _saving;

    /// <summary>执念张力涨到这个值，就值得她自己想一遍了。</summary>
    public const double WakeThreshold = 0.9;
    /// <summary>同一个角色两次自主回想之间的最短间隔。</summary>
    private static readonly TimeSpan WakeCooldown = TimeSpan.FromMinutes(45);
    /// <summary>后台巡检周期。</summary>
    private static readonly TimeSpan TickPeriod = TimeSpan.FromMinutes(4);

    public InnerLifeService(StorageProvider store, MemoryScope scope, SettingsManager settings)
    {
        _store = store;
        _scope = scope;
        _settings = settings;
        scope.Changed += ReleaseCache;
    }

    /// <summary>执念醒了：（角色 id, 惦记的那句话）。由后台巡检触发，订阅方决定怎么让她开口。</summary>
    public event Action<string, string>? ObsessionWake;

    /// <summary>到点做梦了：（角色 id）。静下来超过几小时、距上次做梦超过一天才会发生；
    /// 订阅方让她做一段梦/自省并把结果存进记忆，不用回游戏里说。</summary>
    public event Action<string>? DreamRequested;

    private string Key => _scope.Key("inner_life");
    private bool Enabled => _settings.Current.InnerLifeEnabled;

    // ---- 存储 ----

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        try
        {
            var loaded = await _store.Load<InnerLifeBook>(Key);
            lock (_gate) { _book = loaded ?? new InnerLifeBook(); _loaded = true; }
        }
        catch (Exception ex)
        {
            App.WriteLog("InnerLife.Load -> " + ex.Message);
            lock (_gate) _loaded = true;
        }
    }

    /// <summary>作用域换了（读档/新开局）：丢缓存重读。</summary>
    public void ReleaseCache()
    {
        lock (_gate) { _book = new InnerLifeBook(); _loaded = false; }
    }

    /// <summary>同步读取当前（已加载并随背景结算的）化学池；未加载或未开启时返回 null。
    /// 供具体化机制等纯代码服务在不触发异步结算的前提下取用此刻的情绪-生理信号。</summary>
    public InnerChemicals? PeekChemicals(string charId)
    {
        if (!Enabled || string.IsNullOrEmpty(charId)) return null;
        lock (_gate)
            return _book.ByCharacter.TryGetValue(charId, out var p) ? p.Chemicals : null;
    }

    private async Task SaveAsync()
    {
        // 快照后落盘，别让序列化和下一轮改动打架；重入直接跳过（丢一次保存没关系，下轮还会写）。
        if (_saving) return;
        InnerLifeBook snap;
        lock (_gate)
        {
            if (!_loaded) return;
            _saving = true;
            snap = new InnerLifeBook
            {
                ByCharacter = _book.ByCharacter.ToDictionary(
                    kv => kv.Key,
                    kv => new InnerLifeProfile
                    {
                        Chemicals = kv.Value.Chemicals with { },
                        Obsessions = kv.Value.Obsessions.Select(o => o with { }).ToList(),
                        LastWakeAt = kv.Value.LastWakeAt
                    },
                    StringComparer.Ordinal)
            };
        }
        try { await _store.Save(Key, snap); }
        catch (Exception ex) { App.WriteLog("InnerLife.Save -> " + ex.Message); }
        finally { lock (_gate) _saving = false; }
    }

    // ---- 气质基线：由角色 id 决定的稳定偏移，同一个人永远长同一种性子 ----

    private static (double Dopa, double Cort, double Ser) Baseline(string charId)
    {
        // 稳定散列：同 id 永远同一组基线，读档也不会变性格。
        var h = 2166136261;
        foreach (var c in charId ?? "") { h ^= c; h *= 16777619; }
        double Jitter(int salt, double amp)
        {
            var x = (uint)(h ^ (salt * 0x9E3779B9));
            x ^= x >> 13; x *= 0x85EBCA6B; x ^= x >> 16;
            return amp * (x / (double)uint.MaxValue * 2 - 1); // -amp..+amp
        }
        return (
            Math.Clamp(0.50 + Jitter(1, 0.10), 0.3, 0.7),
            Math.Clamp(0.28 + Jitter(2, 0.08), 0.15, 0.45),
            Math.Clamp(0.60 + Jitter(3, 0.10), 0.4, 0.8));
    }

    // ---- 结算：把三个值按半衰期拉回基线 ----

    private static double Settle(double value, double baseline, double hours, double halfLifeHours)
    {
        if (hours <= 0) return value;
        var pull = Math.Pow(0.5, hours / halfLifeHours);
        return Math.Clamp(baseline + (value - baseline) * pull, 0, 1);
    }

    private InnerLifeProfile Profile(string charId)
    {
        if (!_book.ByCharacter.TryGetValue(charId, out var p))
        {
            var (d, c, s) = Baseline(charId);
            p = new InnerLifeProfile { Chemicals = new InnerChemicals { Dopamine = d, Cortisol = c, Serotonin = s } };
            _book.ByCharacter[charId] = p;
        }
        return p;
    }

    /// <summary>按真实时间结算一次（幂等，读状态前都会调）。</summary>
    private async Task<InnerLifeProfile?> SettledAsync(string charId)
    {
        if (!Enabled || string.IsNullOrEmpty(charId)) return null;
        await EnsureLoadedAsync();
        InnerLifeProfile p;
        lock (_gate) p = Profile(charId);
        var hours = (DateTime.UtcNow - p.Chemicals.UpdatedAt).TotalHours;
        if (hours >= 0.02)
        {
            var (d, c, s) = Baseline(charId);
            lock (_gate)
            {
                p.Chemicals.Dopamine = Settle(p.Chemicals.Dopamine, d, hours, 0.75);
                p.Chemicals.Cortisol = Settle(p.Chemicals.Cortisol, c, hours, 1.5);
                p.Chemicals.Serotonin = Settle(p.Chemicals.Serotonin, s, hours, 4.0);
                // 疲劳向 0 消解：安静歇着比说话恢复得快，这里只按时间走，说话的成本在 PrepareTurn 里记。
                p.Chemicals.Fatigue = Settle(p.Chemicals.Fatigue, 0, hours, 5.0);
                p.Chemicals.UpdatedAt = DateTime.UtcNow;
                GrowObsessions(p, hours);
            }
            _ = SaveAsync();
        }
        return p;
    }

    /// <summary>执念张力随时间涨：惦记越久越放不下，封顶 1。</summary>
    private static void GrowObsessions(InnerLifeProfile p, double hours)
    {
        foreach (var o in p.Obsessions)
        {
            if (o.ResolvedAt is not null) continue;
            o.Tension = Math.Min(1, o.Tension + 0.015 * hours);
        }
    }

    private void Shift(string charId, double dopa = 0, double cort = 0, double ser = 0, double fatigue = 0)
    {
        if (!Enabled) return;
        _ = Task.Run(async () =>
        {
            var p = await SettledAsync(charId);
            if (p is null) return;
            lock (_gate)
            {
                p.Chemicals.Dopamine = Math.Clamp(p.Chemicals.Dopamine + dopa, 0, 1);
                p.Chemicals.Cortisol = Math.Clamp(p.Chemicals.Cortisol + cort, 0, 1);
                p.Chemicals.Serotonin = Math.Clamp(p.Chemicals.Serotonin + ser, 0, 1);
                p.Chemicals.Fatigue = Math.Clamp(p.Chemicals.Fatigue + fatigue, 0, 1);
                p.Chemicals.UpdatedAt = DateTime.UtcNow;
            }
            _ = SaveAsync();
        });
    }

    // ---- 输入：游戏事件 ----

    /// <summary>Minecraft 桥的事件直接映射成激素变化。kind 与模组 event 帧一致。</summary>
    public void OnGameEvent(string charId, string kind)
    {
        if (!Enabled || string.IsNullOrEmpty(charId)) return;
        switch ((kind ?? "").ToLowerInvariant())
        {
            case "hurt": Shift(charId, dopa: -0.08, cort: +0.25, ser: -0.05); break;
            case "death": Shift(charId, dopa: -0.20, cort: +0.50, ser: -0.20); break;
            case "respawn": Shift(charId, dopa: +0.12, cort: -0.15); break;
            case "feed": Shift(charId, dopa: +0.10, cort: -0.04); break;
            case "interaction": Shift(charId, dopa: +0.07, cort: -0.02); break;
            case "maid_chat": Shift(charId, dopa: +0.05); break;
            case "owner_sleep": Shift(charId, ser: +0.12, cort: -0.08, fatigue: -0.5); break;
        }
    }

    // ---- 输入：对话 ----

    private static readonly string[] WarmWords =
        { "喜欢", "想你", "爱你", "宝贝", "亲爱的", "贴贴", "抱抱", "亲亲", "乖", "可爱",
          "好棒", "谢谢你", "辛苦", "晚安", "早安", "在乎", "惦记" };
    private static readonly string[] ColdWords =
        { "滚", "烦", "讨厌", "闭嘴", "算了", "随便", "没意思", "无聊", "别来了", "不想理" };
    private static readonly string[] ScaryWords =
        { "害怕", "吓", "疼", "崩溃", "难受", "哭", "救命", "危险" };

    private static int Hits(string text, string[] words)
        => words.Count(w => (text ?? "").Contains(w, StringComparison.Ordinal));

    /// <summary>用户消息进来：warmth 由文本粗估，事件大小按词命中数走。</summary>
    public void PrepareTurn(string charId, string userText)
    {
        if (!Enabled || string.IsNullOrEmpty(charId)) return;
        var t = userText ?? "";
        var warm = Hits(t, WarmWords);
        var cold = Hits(t, ColdWords);
        var scary = Hits(t, ScaryWords);
        if (warm > 0) Shift(charId, dopa: Math.Min(0.18, 0.06 * warm), ser: +0.02 * warm);
        if (cold > 0) Shift(charId, dopa: -0.05 * cold, cort: +0.08 * cold, ser: -0.04 * cold);
        if (scary > 0) Shift(charId, cort: +0.10 * scary, ser: -0.03 * scary);
        // 每轮对话都要花力气；同时记下她最近什么时候被人陪着——静下来才轮到做梦。
        Shift(charId, fatigue: +0.04);
        _ = Task.Run(async () =>
        {
            var p = await SettledAsync(charId);
            if (p is null) return;
            lock (_gate) p.LastInteractionAt = DateTime.UtcNow;
            _ = SaveAsync();
        });
        // 记下这轮用户问了什么，回复回来后判断她有没有「不知道」。
        lock (_gate) _pendingQuestion[charId] = t;
    }

    private readonly Dictionary<string, string> _pendingQuestion = new(StringComparer.Ordinal);

    private static readonly string[] UncertainCues =
        { "不知道", "不清楚", "不确定", "说不上来", "说不好", "我也想知道", "还没想好",
          "让我想想", "想不起来", "记不清", "我去查", "查不到", "答不上来" };

    private static bool LooksLikeQuestion(string text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return false;
        return t.Contains('？') || t.Contains('?')
            || t.Contains("为什么") || t.Contains("怎么") || t.Contains("如何")
            || t.Contains("多少") || t.Contains("是什么") || t.Contains("能不能")
            || t.Contains("会不会") || t.Contains("有没有");
    }

    /// <summary>回复回来：答不上来的地方长执念；答上来了，旧的惦记就放下一点。</summary>
    public void AfterReply(string charId, string reply)
    {
        if (!Enabled || string.IsNullOrEmpty(charId)) return;
        string question;
        lock (_gate)
        {
            question = _pendingQuestion.TryGetValue(charId, out var q) ? q : "";
            _pendingQuestion.Remove(charId);
        }
        var r = reply ?? "";
        // 正常说上话，本身就是一点点快乐。
        if (r.Length > 0) Shift(charId, dopa: +0.03);

        var uncertain = UncertainCues.Any(c => r.Contains(c, StringComparison.Ordinal));
        if (question.Length > 0 && LooksLikeQuestion(question) && uncertain)
        {
            // 她答不上来：这颗执念要么新长出来，要么被重新惦记起来。
            var q = Truncate(question, 60);
            _ = Task.Run(async () =>
            {
                var p = await SettledAsync(charId);
                if (p is null) return;
                lock (_gate)
                {
                    var existing = p.Obsessions.FirstOrDefault(o => o.ResolvedAt is null &&
                        o.Question.Contains(q[..Math.Min(q.Length, 20)], StringComparison.Ordinal));
                    if (existing is not null)
                        existing.Tension = Math.Min(1, existing.Tension + 0.2);
                    else
                    {
                        p.Obsessions.Add(new Obsession
                        {
                            Id = Guid.NewGuid().ToString("N")[..12],
                            Question = q,
                            Tension = 0.5,
                            Source = "chat"
                        });
                        // 执念太多时把最早解决掉的清出去，没解决的永远留着。
                        var resolved = p.Obsessions.Where(o => o.ResolvedAt is not null).ToList();
                        if (resolved.Count > 30)
                            foreach (var o in resolved.Take(resolved.Count - 30))
                                p.Obsessions.Remove(o);
                        if (p.Obsessions.Count(o => o.ResolvedAt is null) > 8)
                        {
                            var oldest = p.Obsessions.Where(o => o.ResolvedAt is null)
                                .OrderBy(o => o.CreatedAt).First();
                            p.Obsessions.Remove(oldest);
                        }
                    }
                }
                _ = SaveAsync();
            });
        }
        else if (_lastWake.TryGetValue(charId, out var wokeAt)
                 && (DateTime.UtcNow - wokeAt).TotalMinutes < 30
                 && !uncertain && r.Length > 0)
        {
            // 刚被推过一把、这次没说「不知道」：多半是想明白了。
            _ = Task.Run(async () =>
            {
                var p = await SettledAsync(charId);
                if (p is null) return;
                lock (_gate)
                {
                    var top = p.Obsessions.Where(o => o.ResolvedAt is null)
                        .OrderByDescending(o => o.Tension).FirstOrDefault();
                    if (top is not null && top.Tension >= WakeThreshold * 0.7)
                    {
                        top.Tension = 0;
                        top.ResolvedAt = DateTime.UtcNow;
                        p.Chemicals.Serotonin = Math.Clamp(p.Chemicals.Serotonin + 0.06, 0, 1);
                    }
                }
                _ = SaveAsync();
            });
        }
    }

    private readonly Dictionary<string, DateTime> _lastWake = new(StringComparer.Ordinal);

    private static string Truncate(string s, int n)
        => string.IsNullOrEmpty(s) ? "" : s.Length <= n ? s : s[..n] + "…";

    // ---- 自主回想：后台巡检，把惦记到阈值的事推给她自己想一遍 ----

    private CancellationTokenSource? _loopCts;

    /// <summary>启动后台巡检（应用启动时调一次；重复调用无害）。</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_loopCts is not null) return;
            _loopCts = new CancellationTokenSource();
        }
        var token = _loopCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(TickPeriod, token); }
                catch (TaskCanceledException) { return; }
                try { await SweepAsync(); }
                catch (Exception ex) { App.WriteLog("InnerLife.Sweep -> " + ex.Message); }
            }
        }, token);
    }

    private async Task SweepAsync()
    {
        if (!Enabled) return;
        await EnsureLoadedAsync();
        List<(string CharId, Obsession Obs)> due;
        var dreamDue = new List<string>();
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            due = _book.ByCharacter
                .Where(kv => (now - kv.Value.LastWakeAt) > WakeCooldown)
                .Select(kv => (CharId: kv.Key,
                        Obs: kv.Value.Obsessions
                            .Where(o => o.ResolvedAt is null && o.Tension >= WakeThreshold)
                            .OrderByDescending(o => o.Tension).FirstOrDefault()))
                .Where(x => x.Obs is not null)
                .Select(x => (x.CharId, x.Obs!))
                .ToList();
            // 作息：安静下来超过 4 小时、距上次做梦超过 20 小时——该做个梦把最近的事过一遍了。
            foreach (var kv in _book.ByCharacter)
            {
                if ((now - kv.Value.LastInteractionAt).TotalHours >= 4
                    && (now - kv.Value.LastDreamAt).TotalHours >= 20)
                    dreamDue.Add(kv.Key);
            }
        }
        foreach (var (charId, obs) in due)
        {
            lock (_gate)
            {
                var p = _book.ByCharacter[charId];
                if ((DateTime.UtcNow - p.LastWakeAt) < WakeCooldown) continue;
                p.LastWakeAt = DateTime.UtcNow;
                p.Chemicals.Cortisol = Math.Clamp(p.Chemicals.Cortisol + 0.05, 0, 1);
                p.Chemicals.UpdatedAt = DateTime.UtcNow;
            }
            _lastWake[charId] = DateTime.UtcNow;
            _ = SaveAsync();
            try { ObsessionWake?.Invoke(charId, obs.Question); }
            catch (Exception ex) { App.WriteLog("InnerLife.WakeEvent -> " + ex.Message); }
        }
        foreach (var charId in dreamDue)
        {
            lock (_gate)
            {
                var p = _book.ByCharacter[charId];
                var now = DateTime.UtcNow;
                // 双重检查：上面那轮执念唤醒也算一次「被占用」，别做梦和想事情撞在同一口气里。
                if ((now - p.LastInteractionAt).TotalHours < 4
                    || (now - p.LastDreamAt).TotalHours < 20
                    || (now - p.LastWakeAt) < WakeCooldown) continue;
                p.LastDreamAt = now;
                // 梦本身是休息：疲劳消掉一截，血清素抬一点。
                p.Chemicals.Fatigue = Math.Clamp(p.Chemicals.Fatigue - 0.25, 0, 1);
                p.Chemicals.Serotonin = Math.Clamp(p.Chemicals.Serotonin + 0.05, 0, 1);
                p.Chemicals.UpdatedAt = now;
            }
            _ = SaveAsync();
            try { DreamRequested?.Invoke(charId); }
            catch (Exception ex) { App.WriteLog("InnerLife.DreamEvent -> " + ex.Message); }
        }
    }

    // ---- 输出：提示词与检索偏置 ----

    /// <summary>
    /// 给提示词的一段「此刻的心绪」。只描述感受与倾向，不给数字；
    /// 平常心时返回空串——不是每一轮都需要心事。
    /// </summary>
    public async Task<string> PromptTextAsync(string charId)
    {
        if (!Enabled || string.IsNullOrEmpty(charId)) return "";
        var p = await SettledAsync(charId);
        if (p is null) return "";
        var (bd, bc, bs) = Baseline(charId);
        var parts = new List<string>();
        var d = p.Chemicals.Dopamine;
        var c = p.Chemicals.Cortisol;
        var s = p.Chemicals.Serotonin;
        if (d - bd > 0.18) parts.Add("这会儿心里是亮的，有点雀跃，话会多一点软一点");
        else if (bd - d > 0.18) parts.Add("有点提不起劲，像是缺了点什么盼头");
        if (c - bc > 0.18) parts.Add("心里发紧，隐隐有些不安或警觉，容易往坏处想");
        else if (bc - c > 0.18 && s - bs > 0.1) parts.Add("难得的踏实，什么都不想防备");
        if (bs - s > 0.15) parts.Add("有点烦躁，容易钻牛角尖，话里可能带点刺");
        if (p.Chemicals.Fatigue > 0.65) parts.Add("身体发沉，有点累了，话会少一点、反应慢半拍，也不太想动");
        Obsession? top;
        lock (_gate)
            top = p.Obsessions.Where(o => o.ResolvedAt is null)
                .OrderByDescending(o => o.Tension).FirstOrDefault();
        var obsessionLine = top is { Tension: >= 0.55 }
            ? $"\n心里一直惦记着一件事没弄明白：「{top.Question}」。说话间可能会自然带出来，"
              + "或者自己琢磨；要是这会儿想明白了，就主动讲给主人听。"
            : "";
        if (parts.Count == 0 && obsessionLine.Length == 0) return "";
        var head = parts.Count > 0
            ? "【此刻的心绪】" + string.Join("；", parts) + "。让这些自然渗进语气和小动作里，不要报数值，也不要每句都提。"
            : "";
        return head + obsessionLine;
    }

    /// <summary>
    /// 记忆检索的情绪偏置（-1..1）：此刻越开心越容易想起好事，越不安越容易想起坏事。
    /// 0 = 不偏置。作为 moodBias 传给 HumanMemoryService.RecallAsync。
    /// </summary>
    public async Task<double> RecallBiasAsync(string charId)
    {
        if (!Enabled || string.IsNullOrEmpty(charId)) return 0;
        var p = await SettledAsync(charId);
        if (p is null) return 0;
        var (bd, bc, _) = Baseline(charId);
        // 相对各自基线的偏移相减：基线本身不算心情，只算「此刻比平时」。
        return Math.Clamp((p.Chemicals.Dopamine - bd) - (p.Chemicals.Cortisol - bc), -0.5, 0.5) * 2;
    }

    /// <summary>给工具/面板读的结构化快照。</summary>
    public async Task<object> SnapshotAsync(string charId)
    {
        var p = await SettledAsync(charId);
        if (p is null) return new { enabled = false };
        lock (_gate)
        {
            return new
            {
                enabled = true,
                dopamine = Math.Round(p.Chemicals.Dopamine, 2),
                cortisol = Math.Round(p.Chemicals.Cortisol, 2),
                serotonin = Math.Round(p.Chemicals.Serotonin, 2),
                fatigue = Math.Round(p.Chemicals.Fatigue, 2),
                obsessions = p.Obsessions.Where(o => o.ResolvedAt is null)
                    .OrderByDescending(o => o.Tension).Take(5)
                    .Select(o => new { o.Question, tension = Math.Round(o.Tension, 2) })
                    .ToList()
            };
        }
    }
}

// ---- 模型 ----

/// <summary>三个激素值 + 疲劳感，0~1。激素按半衰期向气质基线回落，疲劳随时间消解。</summary>
public sealed record InnerChemicals
{
    public double Dopamine { get; set; }
    public double Cortisol { get; set; }
    public double Serotonin { get; set; }

    /// <summary>疲劳：每轮对话 +0.04，时间按半衰期（约 5 小时）消解，睡了恢复一大截。
    /// 太累时话少、反应慢——她不是随时满血的。</summary>
    public double Fatigue { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>一颗执念：她答不上来的那件事。张力随时间涨，醒了就推她自己去想。</summary>
public sealed record Obsession
{
    public string Id { get; init; } = "";
    public string Question { get; init; } = "";
    public double Tension { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public string Source { get; init; } = "";
}

/// <summary>一个角色的内心生活：化学池 + 执念池 + 作息（上次说话、上次做梦）。</summary>
public sealed class InnerLifeProfile
{
    public InnerChemicals Chemicals { get; set; } = new();
    public List<Obsession> Obsessions { get; set; } = new();
    public DateTime LastWakeAt { get; set; } = DateTime.MinValue;

    /// <summary>最近一次和她说话的时间：静下来超过一阵，才轮到做梦。</summary>
    public DateTime LastInteractionAt { get; set; } = DateTime.UtcNow;

    /// <summary>上次做梦反思的时间：一天最多做一次，醒了就记在记忆里。</summary>
    public DateTime LastDreamAt { get; set; } = DateTime.MinValue;
}

public sealed class InnerLifeBook
{
    public Dictionary<string, InnerLifeProfile> ByCharacter { get; set; } = new(StringComparer.Ordinal);
}
