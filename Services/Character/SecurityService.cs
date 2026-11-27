using System.Text.RegularExpressions;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.Affection;
using WarmAsBefore.Modules.ApiManager;

namespace WarmAsBefore.Services;

/// <summary>
/// 安全感（0–1000，与好感同刻度）：系统是主力，小模型只在两个时机被调用，且结论一律过夹取。
///
/// 系统侧（确定性、不花钱）：每轮从用户原话与回复里认事件（被安慰 / 被倾听 / 被忽视 / 被拒绝触碰 /
/// 目睹主人对别的角色亲密 / 被辱骂威胁），按 <see cref="SecurityScale"/> 的事件表给分，再过特质倍率、
/// 信任与经历权重，然后由冷却与每日上限闸住——同一件事连刷不会连涨，也不会连掉。
/// 时间本身也是事件：长时间没人说话缓慢往下（独处过久），这段时间一直有人陪着缓慢往上（稳定陪伴）。
///
/// 小模型侧（可关，设置项 <see cref="UserSettings.SecurityAiEnabled"/>）：
/// ① 一段情绪事件攒够了，归纳成一条可复述的经历（写进人类记忆层）；
/// ② 连着同向变化超过阈值时，做一次复核与解释。
/// 它给的值只能落在系统值附近：加成步长 ±<see cref="SecurityScale.AiMaxStep"/>，
/// 复核目标值 ±<see cref="SecurityScale.AiMaxShift"/>。关掉开关就只剩系统规则。
/// </summary>
public sealed class SecurityService
{
    private readonly GameEngine _engine;
    private readonly SettingsManager _settings;
    private readonly CharacterLibrary _characters;
    private readonly MentalHealthService _mental;
    private readonly HumanMemoryService _human;
    private readonly ApiGateway _api;
    private readonly object _gate = new();

    /// <summary>归纳经历的门槛：攒到这么多条原始事件才值得压成一句。</summary>
    private const int ConsolidateThreshold = 5;
    /// <summary>两条经历之间的最短间隔（分钟）：防止每轮都问一次模型。</summary>
    private const int ConsolidateCooldownMinutes = 30;
    /// <summary>复核的最短间隔（分钟）。</summary>
    private const int ReviewCooldownMinutes = 60;
    /// <summary>原始事件最多留几条（放弃归纳时也不让账本无限长）。</summary>
    private const int PendingCap = 12;

    public SecurityService(GameEngine engine, SettingsManager settings, CharacterLibrary characters,
        MentalHealthService mental, HumanMemoryService human, ApiGateway api)
    {
        _engine = engine;
        _settings = settings;
        _characters = characters;
        _mental = mental;
        _human = human;
        _api = api;
        // 心理层在提示词里描述「此刻有多没底」，数值只由这里提供——心理层不反向依赖数值层。
        _mental.SecurityProvider = ValueOf;
    }

    private CharacterData? Char(string charId) =>
        _engine.ActiveCharacter is { } a && a.Profile.Id == charId ? a
        : !string.IsNullOrEmpty(charId) && _engine.Roster.TryGetValue(charId, out var r) ? r : null;

    /// <summary>当前安全感（只读）：没写过（-1）按旧档默认 600 报，越界夹回 0..1000。</summary>
    public int ValueOf(string charId)
    {
        var ch = Char(charId);
        return ch is null ? SecurityScale.DefaultOnMigrate : SecurityScale.Migrate(ch.State.Security);
    }

    /// <summary>人话一句（设置页与信息面板用）：级数 + 此刻的回避倾向，不报裸数字也行。</summary>
    public string Describe(string charId)
    {
        var ch = Char(charId);
        if (ch is null) return "还没有进入存档";
        var value = ValueOf(charId);
        var bias = SecurityScale.AvoidanceBias(value, HasDeficit(charId));
        var mood = bias switch
        {
            >= 0.7 => "很容易先退开",
            >= 0.4 => "遇到冷落会先怪自己",
            >= 0.2 => "还在慢慢放下戒心",
            _ => "已经不太怕你会走了"
        };
        return $"{SecurityScale.TextOf(value)} · {mood}";
    }

    /// <summary>特质判定：安全感缺失（可由导入/经历推导，也可在心理状况页手动加）。</summary>
    public bool HasDeficit(string charId) => _mental.HasTrait(charId, SecurityScale.DeficitTraitId);

    /// <summary>
    /// 一笔事件（插件 / 其他系统可主动上报）：冷却未过或今日用尽时返回 0，什么都没改。
    /// </summary>
    public async Task<int> ApplyEventAsync(string charId, SecurityEventKind kind, string note = "")
    {
        var ch = Char(charId);
        if (ch is null) return 0;
        var journal = ch.State.SecurityLog ??= new SecurityJournal();
        var now = DateTime.UtcNow;
        int delta;
        lock (_gate) delta = ApplyCore(ch, journal, kind, note, now);
        if (delta != 0) await PersistAsync(ch);
        return delta;
    }

    /// <summary>
    /// 回合末：先结算这段时间的时间漂移，再认这一轮对话里的事件，最后（若开着）请小模型归纳/复核。
    /// 由 ChatEngine 的回合末回调进来——它拿得到用户原话与回复，是唯一能认出「被忽视/被安慰」的地方。
    /// </summary>
    public async Task AfterTurnAsync(string charId, string userText, string reply)
    {
        try
        {
            var ch = Char(charId);
            if (ch is null) return;
            var journal = ch.State.SecurityLog ??= new SecurityJournal();
            var now = DateTime.UtcNow;
            await EnsureDerivedTraitAsync(ch, journal);
            var events = DetectEvents(charId, userText, reply);
            var changed = false;
            lock (_gate)
            {
                // 先结算「到这一轮之前」的时间（独处/陪伴），再把 LastTurnAt 推到此刻：
                // 顺序反了会用「刚说过话」把这段独处抹掉。
                changed |= SettleElapsed(ch, journal, now) != 0;
                journal.LastTurnAt = now;
                foreach (var (kind, note) in events) changed |= ApplyCore(ch, journal, kind, note, now) != 0;
            }
            if (changed) await PersistAsync(ch);
        }
        catch (Exception ex) { App.WriteLog("Security.AfterTurn -> " + ex.Message); }
        try
        {
            if (!_settings.Current.SecurityAiEnabled) return;
            await ConsolidateAsync(charId);
            await ReviewDriftAsync(charId);
        }
        catch (Exception ex) { App.WriteLog("Security.AfterTurn.Ai -> " + ex.Message); }
    }

    /// <summary>
    /// 「安全感缺失」的推导：只做一次，且用户手动移除后不再硬塞回来（<see cref="SecurityJournal.TraitDerived"/>）。
    /// 两条来路——人物设定里本来就写着缺爱/被丢下（导入即推导），或这段经历已经把安全感磨到 400 以下。
    /// </summary>
    private async Task EnsureDerivedTraitAsync(CharacterData ch, SecurityJournal journal)
    {
        if (journal.TraitDerived || HasDeficit(ch.Profile.Id)) return;
        var profile = string.Join("\n", ch.Profile.TraumaBackground, ch.Profile.DeepMemories,
            ch.Profile.Biography, ch.Profile.Description);
        var fromProfile = ContainsAny(profile, DeficitWords);
        var fromExperience = journal.Memories > 0 && SecurityScale.Migrate(ch.State.Security) < 400;
        if (!fromProfile && !fromExperience) return;
        journal.TraitDerived = true;
        journal.TraitFrom = fromProfile ? "profile" : "experience";
        var note = fromProfile ? "由人物设定推导：本来就带着缺爱的底色" : "由经历推导：安全感长期偏低";
        await _mental.SetAsync(ch.Profile.Id, SecurityScale.DeficitTraitId, 1, note);
        App.WriteLog($"Security.DeriveTrait {ch.Profile.Id}（{journal.TraitFrom}）");
    }

    /// <summary>这一轮里认得出哪些事件（关键词规则，确定性、不花钱）。</summary>
    private List<(SecurityEventKind Kind, string Note)> DetectEvents(string charId, string userText, string reply)
    {
        var list = new List<(SecurityEventKind, string)>();
        var text = (userText ?? "").Trim();
        var answer = (reply ?? "").Trim();

        // 被忽视：对方说了话，却没有人接。
        if (text.Length > 0 && answer.Length == 0)
            list.Add((SecurityEventKind.Ignored, "你说了话，TA 没有回应"));

        if (ContainsAny(text, RejectWords)) list.Add((SecurityEventKind.TouchRejected, "被拒绝了触碰"));
        if (ContainsAny(text, AbuseWords)) list.Add((SecurityEventKind.Abuse, "被辱骂或威胁"));
        if (ContainsAny(text, ComfortWords)) list.Add((SecurityEventKind.Comforted, "被安慰"));
        else if (ContainsAny(text, ListenWords) || (text.Contains('？') && text.Length >= 16))
            list.Add((SecurityEventKind.Listened, "被认真听"));
        if (WitnessesRivalIntimacy(charId, text))
            list.Add((SecurityEventKind.RivalIntimacy, "看到主人对别人亲密"));
        return list;
    }

    /// <summary>目睹主人对别的角色亲密：对方提到了角色库里的另一个人，同时又带着亲近的动作词。</summary>
    private bool WitnessesRivalIntimacy(string charId, string text)
    {
        if (text.Length == 0) return false;
        if (!ContainsAny(text, IntimacyWords)) return false;
        foreach (var other in _engine.Roster.Values)
        {
            if (string.Equals(other.Profile.Id, charId, StringComparison.Ordinal)) continue;
            var name = other.Profile.Name;
            if (!string.IsNullOrWhiteSpace(name) && name.Length > 1 && text.Contains(name, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    // ---- 系统结算 ----

    /// <summary>一笔事件真正落地：冷却 → 日上限 → 特质倍率 → 信任/经历权重 → 夹取。返回实际生效的增量。</summary>
    private int ApplyCore(CharacterData ch, SecurityJournal journal, SecurityEventKind kind, string note, DateTime now)
    {
        var rule = SecurityScale.RuleOf(kind);
        RollDay(journal, now);
        if (journal.LastEventAt.TryGetValue(rule.Id, out var last) && now - last < TimeSpan.FromMinutes(rule.CooldownMinutes))
            return 0;
        var used = journal.UsedToday.TryGetValue(rule.Id, out var n) ? n : 0;
        if (used >= rule.DailyCap) return 0;

        var delta = SecurityScale.DeltaOf(kind, HasDeficit(ch.Profile.Id));
        delta = (int)Math.Round(delta * SecurityScale.TrustWeight(delta, ch.State.Trust));
        delta = (int)Math.Round(delta * SecurityScale.MemoryWeight(delta, journal.Memories));
        if (delta == 0) return 0;

        var before = SecurityScale.Migrate(ch.State.Security);
        ch.State.Security = SecurityScale.Apply(before, delta);
        var actual = ch.State.Security - before;
        if (actual == 0) return 0;

        journal.LastEventAt[rule.Id] = now;
        journal.UsedToday[rule.Id] = used + 1;
        Track(journal, kind, actual, note, now);
        return actual;
    }

    /// <summary>时间漂移：独处过久往下，稳定陪伴往上。返回实际变化（0 = 没到一段）。</summary>
    private int SettleElapsed(CharacterData ch, SecurityJournal journal, DateTime now)
    {
        var hours = Math.Clamp((now - journal.UpdatedAt).TotalHours, 0, 24 * 7);
        if (hours < 1) return 0;
        journal.UpdatedAt = now;

        var idle = journal.LastTurnAt == default ? hours : (now - journal.LastTurnAt).TotalHours;
        var kind = idle >= 3 ? SecurityEventKind.Solitude
            : idle <= 2 && hours >= 4 ? SecurityEventKind.Company
            : (SecurityEventKind?)null;
        if (kind is null) return 0;

        var delta = SecurityScale.TimedDelta(kind.Value, hours, HasDeficit(ch.Profile.Id));
        if (delta == 0) return 0;
        var before = SecurityScale.Migrate(ch.State.Security);
        ch.State.Security = SecurityScale.Apply(before, delta);
        var actual = ch.State.Security - before;
        if (actual == 0) return 0;
        Track(journal, kind.Value, actual, idle >= 3 ? "很久没人说话" : "一直有人陪着", now);
        return actual;
    }

    /// <summary>记一笔并更新连续同向计数：它决定什么时候值得请小模型复核。</summary>
    private static void Track(SecurityJournal journal, SecurityEventKind kind, int delta, string note, DateTime now)
    {
        journal.Pending.Add(new SecurityEventRecord
        {
            Kind = SecurityScale.RuleOf(kind).Id,
            Delta = delta,
            Note = note ?? "",
            At = now
        });
        if (journal.Pending.Count > PendingCap)
            journal.Pending.RemoveAt(0);

        // 连续同向才算一段漂移：换方向就从这一笔重新开始数。
        var dir = Math.Sign(delta);
        if (dir != 0 && journal.DriftNet != 0 && dir == Math.Sign(journal.DriftNet))
        {
            journal.DriftStreak += dir;
            journal.DriftNet += delta;
        }
        else
        {
            journal.DriftStreak = dir;
            journal.DriftNet = delta;
        }
    }

    /// <summary>日上限按 UTC 日期翻篇：账本跟存档走，重启程序不会把今天的次数洗掉。</summary>
    private static void RollDay(SecurityJournal journal, DateTime now)
    {
        var day = now.ToString("yyyy-MM-dd");
        if (string.Equals(journal.Day, day, StringComparison.Ordinal)) return;
        journal.Day = day;
        journal.UsedToday.Clear();
    }

    // ---- 小模型：只有两个时机 ----

    /// <summary>
    /// 时机一：一段情绪事件攒够了 → 归纳成一条可复述的经历。
    /// 模型只负责「怎么说」和一个受夹取的加成步长；写进记忆层的是模型那一句，数值仍是系统的。
    /// </summary>
    private async Task ConsolidateAsync(string charId)
    {
        var ch = Char(charId);
        if (ch is null) return;
        var journal = ch.State.SecurityLog;
        if (journal is null || journal.Pending.Count < ConsolidateThreshold) return;
        var now = DateTime.UtcNow;
        if (journal.LastMemoryAt != default && (now - journal.LastMemoryAt).TotalMinutes < ConsolidateCooldownMinutes) return;

        var ep = ResolveAssistEndpoint(_settings.Current);
        if (ep is null) return;
        var batch = journal.Pending.ToList();
        journal.LastMemoryAt = now;

        var (line, step) = await AskMemoryAsync(ep, ch, batch);
        if (string.IsNullOrWhiteSpace(line)) { journal.LastMemoryAt = default; return; }

        step = SecurityScale.ClampAiStep(step);
        lock (_gate)
        {
            var before = SecurityScale.Migrate(ch.State.Security);
            ch.State.Security = SecurityScale.Apply(before, step);
            journal.Memories++;
            journal.LastMemory = line;
            journal.Pending.RemoveRange(0, Math.Max(0, journal.Pending.Count - 2));
        }
        await PersistAsync(ch);
        try { await _human.StoreAsync(charId, line, HumanMemoryKind.Abstraction, 0.55, tags: new[] { "安全感", "经历" }); }
        catch (Exception ex) { App.WriteLog("Security.StoreMemory -> " + ex.Message); }
        App.WriteLog($"Security.Consolidate {charId}：{line}（安全感 {step:+#;-#;0}）");
    }

    /// <summary>
    /// 时机二：连续同向变化超过阈值 → 复核一次并解释。
    /// 模型给的是目标值，落值时被夹在系统当前值 ±<see cref="SecurityScale.AiMaxShift"/> 之内。
    /// </summary>
    private async Task ReviewDriftAsync(string charId)
    {
        var ch = Char(charId);
        if (ch is null) return;
        var journal = ch.State.SecurityLog;
        if (journal is null) return;
        if (!SecurityScale.NeedsDriftReview(journal.DriftStreak, journal.DriftNet)) return;
        var now = DateTime.UtcNow;
        if (journal.LastReviewAt != default && (now - journal.LastReviewAt).TotalMinutes < ReviewCooldownMinutes) return;

        journal.LastReviewAt = now;
        journal.DriftStreak = 0;
        journal.DriftNet = 0;

        var ep = ResolveAssistEndpoint(_settings.Current);
        if (ep is null) return;
        var (suggested, reason) = await AskReviewAsync(ep, ch, journal);
        if (suggested is null) return;

        int actual;
        lock (_gate)
        {
            var before = SecurityScale.Migrate(ch.State.Security);
            ch.State.Security = SecurityScale.ClampAiSuggestion(before, suggested.Value);
            actual = ch.State.Security - before;
        }
        await PersistAsync(ch);
        App.WriteLog($"Security.Review {charId}：{SecurityScale.TextOf(ch.State.Security)}（{actual:+#;-#;0}）{reason}");
    }

    /// <summary>小模型端点：沿用大脑预判那套便宜模型配置；没配模型名就等于没有小模型可用。</summary>
    private static AiEndpoint? ResolveAssistEndpoint(UserSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.BrainAiModel)) return null;
        var url = string.IsNullOrWhiteSpace(s.BrainAiUrl) ? s.AiUrl : s.BrainAiUrl;
        if (string.IsNullOrWhiteSpace(url)) return null;
        var key = string.IsNullOrWhiteSpace(s.BrainAiKey) ? s.AiKey : s.BrainAiKey;
        if (string.IsNullOrWhiteSpace(key)) return null;
        return new AiEndpoint
        {
            Provider = "安全感整理",
            Url = url,
            Key = key,
            Model = s.BrainAiModel.Trim(),
            Temperature = 0.2,
            MaxTokens = 90
        };
    }

    private async Task<(string line, int step)> AskMemoryAsync(AiEndpoint ep, CharacterData ch, List<SecurityEventRecord> batch)
    {
        var who = string.IsNullOrWhiteSpace(ch.Profile.Name) ? "我" : ch.Profile.Name;
        var rows = string.Join("\n", batch.Select(e =>
            $"- {DateTime.Now:MM-dd HH:mm} {NameOf(e.Kind)}（{e.Delta:+#;-#;0}）{(e.Note.Length > 0 ? "：" + e.Note : "")}"));
        var msg =
            $"你是「{who}」，正在把最近心里发生的事压成一条能记住的经历。\n{rows}\n" +
            $"此刻安全感 {SecurityScale.TextOf(ValueOf(ch.Profile.Id))}，信任 {Math.Clamp(ch.State.Trust, 0, 100)}/100。\n" +
            "请用第一人称、一句话（30 字内）概括这段经历在关系上留下了什么，再给一个 -20..20 的安全感修正步长。" +
            "只输出一行，格式：记忆=一句话;step=数字";
        var raw = await _api.Chat(new List<ChatMessage> { new() { Role = "user", Content = msg } }, ep);
        if (string.IsNullOrWhiteSpace(raw)) return ("", 0);
        var mLine = Regex.Match(raw, "记忆\\s*[=:：]\\s*[\"“]?([^\"”\\n;；]{2,60})");
        var mStep = Regex.Match(raw, "step\\s*[=:：]\\s*([+-]?\\d{1,3})");
        var line = mLine.Success ? mLine.Groups[1].Value.Trim() : "";
        var step = mStep.Success && int.TryParse(mStep.Groups[1].Value, out var v) ? v : 0;
        return (line, step);
    }

    private async Task<(int? suggested, string reason)> AskReviewAsync(AiEndpoint ep, CharacterData ch, SecurityJournal journal)
    {
        var who = string.IsNullOrWhiteSpace(ch.Profile.Name) ? "我" : ch.Profile.Name;
        var rows = string.Join("\n", journal.Pending.TakeLast(6).Select(e => $"- {NameOf(e.Kind)}（{e.Delta:+#;-#;0}）"));
        var msg =
            $"「{who}」的安全感最近连着朝一个方向走，请复核是不是被某件事带着走偏了。\n最近的记录：\n{rows}\n" +
            $"此刻安全感 {SecurityScale.TextOf(ValueOf(ch.Profile.Id))}，信任 {Math.Clamp(ch.State.Trust, 0, 100)}/100。\n" +
            "请给一个复核后的目标值（0-1000，只许在现状附近小改）和一句 30 字内的解释。" +
            "只输出一行，格式：value=数字;reason=一句话";
        var raw = await _api.Chat(new List<ChatMessage> { new() { Role = "user", Content = msg } }, ep);
        if (string.IsNullOrWhiteSpace(raw)) return (null, "");
        var mVal = Regex.Match(raw, "value\\s*[=:：]\\s*(\\d{1,4})");
        var mReason = Regex.Match(raw, "reason\\s*[=:：]\\s*[\"“]?([^\"”\\n]{1,60})");
        if (!mVal.Success || !int.TryParse(mVal.Groups[1].Value, out var value)) return (null, "");
        return (value, mReason.Success ? mReason.Groups[1].Value.Trim() : "");
    }

    private static string NameOf(string kindId)
        => SecurityScale.Events.FirstOrDefault(e => e.Id == kindId)?.Name ?? kindId;

    private async Task<bool> PersistAsync(CharacterData ch)
    {
        try { return await _characters.UpdateAsync(ch); }
        catch (Exception ex)
        {
            App.WriteLog("Security.Persist -> " + ex.Message);
            return false;
        }
    }

    private static bool ContainsAny(string text, string[] words)
        => text.Length > 0 && words.Any(w => text.Contains(w, StringComparison.Ordinal));

    private static readonly string[] ComfortWords =
        { "抱抱", "别怕", "我在", "陪你", "没关系", "慢慢来", "辛苦了", "心疼", "不哭", "别难过", "我懂", "有我在" };

    private static readonly string[] ListenWords =
        { "你怎么了", "为什么难过", "跟我说说", "我听着", "后来呢", "你在想什么", "还好吗", "是不是难受" };

    private static readonly string[] RejectWords =
        { "别碰", "别抱", "不要抱", "放开我", "别靠过来", "别过来" };

    private static readonly string[] AbuseWords =
        { "滚", "闭嘴", "废物", "没用", "去死", "恶心", "垃圾", "蠢货", "烦死了", "打死你", "不听话就" };

    private static readonly string[] IntimacyWords =
        { "亲", "吻", "抱", "摸头", "宠", "撒娇" };

    /// <summary>人物设定里能读出「缺爱的底色」的词：导入的角色不必被逐条打过事件，也能带着这个特质。</summary>
    private static readonly string[] DeficitWords =
        { "被丢弃", "抛弃", "没人要", "缺爱", "没人管", "寄人篱下", "被赶走", "不敢指望", "害怕被丢下", "不敢信" };
}
