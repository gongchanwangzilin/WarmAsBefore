using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 内驱动系统：把「人为什么想动」这件事单独长成一层。
///
/// 三件事各归各位：
/// · 三种基本动力（攻击性 / 性 / 自恋）是力的来源，决定多容易被推动；
/// · 七个驱动（贪欲 / 暴食 / 色欲 / 嫉妒 / 傲慢 / 怠惰 / 暴怒）是当下被推到哪儿，
///   越过阈值就化成一句「想做点什么」进提示词，做完之后释放多巴胺与内啡肽；
/// · 自律是闸门：它不消灭驱动，只决定这一次给不给出口、给哪个出口。
///   闸门的方向由角色的道德观 / 人生观 / 思维主义决定——同样是利己，
///   高自律的人会舍小利保大利，低自律的人只按本能走。
///
/// 生气在这一层被拆细：火气积起来之后不会只有「生气」一个词，而是落到八种出口之一
/// （叛逆 / 撒泼 / 怄气 / 任性 / 郁闷 / 拖延 / 厌烦 / 退缩）。走哪个出口由火气强度、
/// 攻击性、自律与关系安全度共同决定；走完释放内啡肽，压下去则只留下闷，并锻炼自律。
///
/// 状态存在 <see cref="StorageProvider"/> 的 inner_drive 键下，跟着存档走（MemoryScope 隔离）。
/// 关闭后数值冻结、不进提示词。
/// </summary>
public sealed class InnerDriveService
{
    private readonly StorageProvider _store;
    private readonly MemoryScope _scope;
    private readonly SettingsManager _settings;
    private readonly InnerLifeService _inner;
    private readonly object _gate = new();
    private DriveBook _book = new();
    private bool _loaded;
    private bool _saving;

    /// <summary>角色人设里的道德观 / 人生观 / 思维主义：由宿主任入，只在首次归类时读一次。</summary>
    public Func<string, (string Moral, string Life, string Ideology)?>? OutlookProvider { get; set; }

    public InnerDriveService(StorageProvider store, MemoryScope scope, SettingsManager settings,
                             InnerLifeService inner)
    {
        _store = store;
        _scope = scope;
        _settings = settings;
        _inner = inner;
        scope.Changed += ReleaseCache;
    }

    private string Key => _scope.Key("inner_drive");
    private bool Enabled => _settings.Current.InnerDriveEnabled;

    // ---- 存储 ----

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        try
        {
            var loaded = await _store.Load<DriveBook>(Key);
            lock (_gate) { _book = loaded ?? new DriveBook(); _loaded = true; }
        }
        catch (Exception ex)
        {
            App.WriteLog("InnerDrive.Load -> " + ex.Message);
            lock (_gate) _loaded = true;
        }
    }

    /// <summary>作用域换了（读档 / 新开局）：丢缓存重读。</summary>
    public void ReleaseCache()
    {
        lock (_gate) { _book = new DriveBook(); _loaded = false; }
    }

    private async Task SaveAsync()
    {
        if (_saving) return;
        DriveBook snap;
        lock (_gate)
        {
            if (!_loaded) return;
            _saving = true;
            snap = new DriveBook
            {
                ByCharacter = _book.ByCharacter.ToDictionary(
                    kv => kv.Key,
                    kv => new DriveProfile
                    {
                        Aggression = kv.Value.Aggression, Sexuality = kv.Value.Sexuality, Narcissism = kv.Value.Narcissism,
                        Greed = kv.Value.Greed, Gluttony = kv.Value.Gluttony, Lust = kv.Value.Lust,
                        Envy = kv.Value.Envy, Pride = kv.Value.Pride, Sloth = kv.Value.Sloth, Wrath = kv.Value.Wrath,
                        Anger = kv.Value.Anger, Discharge = kv.Value.Discharge, DischargeCharge = kv.Value.DischargeCharge,
                        DischargedAt = kv.Value.DischargedAt, SulkingUntil = kv.Value.SulkingUntil,
                        Endorphin = kv.Value.Endorphin, Discipline = kv.Value.Discipline,
                        Moral = kv.Value.Moral, Life = kv.Value.Life, Thinking = kv.Value.Thinking,
                        OutlookClassified = kv.Value.OutlookClassified,
                        RelationSafety = kv.Value.RelationSafety,
                        UpdatedAt = kv.Value.UpdatedAt
                    },
                    StringComparer.Ordinal)
            };
        }
        try { await _store.Save(Key, snap); }
        catch (Exception ex) { App.WriteLog("InnerDrive.Save -> " + ex.Message); }
        finally { lock (_gate) _saving = false; }
    }

    // ---- 结算 ----

    private DriveProfile Profile(string charId)
    {
        if (!_book.ByCharacter.TryGetValue(charId, out var p))
        {
            var (agg, sex, nar, disc) = DriveRules.Baseline(charId);
            p = new DriveProfile
            {
                Aggression = agg, Sexuality = sex, Narcissism = nar, Discipline = disc,
                RelationSafety = 0.55
            };
            _book.ByCharacter[charId] = p;
        }
        return p;
    }

    /// <summary>按真实时间结算一次（幂等，读状态前都会调）。</summary>
    private async Task<DriveProfile?> SettledAsync(string charId)
    {
        if (!Enabled || string.IsNullOrEmpty(charId)) return null;
        await EnsureLoadedAsync();
        DriveProfile p;
        lock (_gate) p = Profile(charId);
        TryClassifyOutlook(charId, p);
        var hours = (DateTime.UtcNow - p.UpdatedAt).TotalHours;
        if (hours >= 0.02)
        {
            var (agg, sex, nar, _) = DriveRules.Baseline(charId);
            lock (_gate)
            {
                p.Aggression = DriveRules.Settle(p.Aggression, agg, hours, DriveRules.HalfLifePower);
                p.Sexuality = DriveRules.Settle(p.Sexuality, sex, hours, DriveRules.HalfLifePower);
                p.Narcissism = DriveRules.Settle(p.Narcissism, nar, hours, DriveRules.HalfLifePower);
                p.Greed = DriveRules.Settle(p.Greed, 0.28, hours, DriveRules.HalfLifeDrive);
                p.Gluttony = DriveRules.Settle(p.Gluttony, 0.30, hours, DriveRules.HalfLifeDrive);
                p.Lust = DriveRules.Settle(p.Lust, 0.26, hours, DriveRules.HalfLifeDrive);
                p.Envy = DriveRules.Settle(p.Envy, 0.18, hours, DriveRules.HalfLifeDrive);
                p.Pride = DriveRules.Settle(p.Pride, 0.30, hours, DriveRules.HalfLifeDrive);
                p.Sloth = DriveRules.Settle(p.Sloth, 0.32, hours, DriveRules.HalfLifeDrive);
                p.Wrath = DriveRules.Settle(p.Wrath, 0.12, hours, DriveRules.HalfLifeDrive);
                p.Endorphin = DriveRules.Settle(p.Endorphin, 0.08, hours, DriveRules.HalfLifeEndorphin);
                // 火气：怄气那阵子被时间稀释得慢——绷着脸本来就不打算这么快好。
                var angryHalfLife = p.SulkingUntil > DateTime.UtcNow
                    ? DriveRules.HalfLifeAnger * DriveRules.SulkingSlowdown
                    : DriveRules.HalfLifeAnger;
                p.Anger = DriveRules.Settle(p.Anger, 0.05, hours, angryHalfLife);
                p.UpdatedAt = DateTime.UtcNow;
            }
            _ = SaveAsync();
        }
        return p;
    }

    /// <summary>首次见到这个角色时，把它的道德观 / 人生观 / 思维主义归类一次，之后不再覆盖（自律可以练，立场不随便飘）。</summary>
    private void TryClassifyOutlook(string charId, DriveProfile p)
    {
        if (p.OutlookClassified) return;
        try
        {
            var outlook = OutlookProvider?.Invoke(charId);
            if (outlook is null) return;
            var (moral, life, thinking) = DriveRules.ClassifyOutlook(outlook.Value.Moral, outlook.Value.Life, outlook.Value.Ideology);
            lock (_gate)
            {
                p.Moral = moral;
                p.Life = life;
                p.Thinking = thinking;
                p.OutlookClassified = true;
            }
        }
        catch (Exception ex) { App.WriteLog("InnerDrive.Classify -> " + ex.Message); }
    }

    // ---- 输入：一句话 ----

    /// <summary>用户消息进来：顶撞抬火气，夸赞满足自恋与连接，日常话题推动七个驱动。</summary>
    public void PrepareTurn(string charId, string userText)
    {
        if (!Enabled || string.IsNullOrEmpty(charId)) return;
        var text = userText ?? "";
        if (text.Length == 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var p = await SettledAsync(charId);
                if (p is null) return;
                double angerUp = 0, warm = 0;
                foreach (var (cue, weight) in DriveRules.AngerCues)
                    if (text.Contains(cue, StringComparison.Ordinal)) angerUp += weight;
                foreach (var (cue, weight) in DriveRules.WarmCues)
                    if (text.Contains(cue, StringComparison.Ordinal)) warm += weight;
                angerUp = Math.Min(0.45, angerUp * 0.55);
                warm = Math.Min(0.5, warm * 0.6);

                var drives = new Dictionary<DriveImpulse, double>();
                foreach (var (cue, kind, weight) in DriveRules.DriveCues)
                {
                    if (!text.Contains(cue, StringComparison.Ordinal)) continue;
                    drives[kind] = (drives.TryGetValue(kind, out var old) ? old : 0) + weight * 0.35;
                }

                (double Dopa, double Endo, double Ser, double Cort) reward = default;
                lock (_gate)
                {
                    // 关系安全度：被好好对待就慢慢升高，被顶撞就掉下来。它决定火气敢不敢往外放。
                    p.RelationSafety = Math.Clamp(p.RelationSafety + warm * 0.12 - angerUp * 0.20, 0.05, 0.98);
                    if (angerUp > 0)
                    {
                        p.Anger = Math.Clamp(p.Anger + angerUp, 0, 1);
                        p.Wrath = Math.Clamp(p.Wrath + angerUp * 0.8, 0, 1);
                        // 被否定的同时，自恋受挫、傲慢被压。
                        p.Narcissism = Math.Clamp(p.Narcissism - angerUp * 0.25, 0, 1);
                        p.Pride = Math.Clamp(p.Pride + angerUp * 0.15, 0, 1);
                    }
                    if (warm > 0)
                    {
                        // 被夸、被抱住：自恋与性（连接）被满足，火气松一口。
                        p.Narcissism = Math.Clamp(p.Narcissism + warm * 0.30, 0, 1);
                        p.Sexuality = Math.Clamp(p.Sexuality + warm * 0.22, 0, 1);
                        p.Anger = Math.Clamp(p.Anger - warm * 0.22, 0, 1);
                        reward = DriveRules.RewardOf(BasePower.Narcissism);
                    }
                    foreach (var (kind, delta) in drives)
                        DriveRules.SetPressure(p, kind, DriveRules.PressureOf(p, kind) + delta);
                    p.UpdatedAt = DateTime.UtcNow;
                }
                if (reward.Dopa > 0 || warm > 0)
                {
                    // 三种基本动力被满足 → 直接推多巴胺；内啡肽留在本层，慢慢散。
                    _inner.Nudge(charId, dopamine: reward.Dopa * warm, serotonin: reward.Ser * warm, cortisol: reward.Cort * warm);
                    lock (_gate) p.Endorphin = Math.Clamp(p.Endorphin + reward.Endo * warm, 0, 1);
                }
                _ = SaveAsync();
            }
            catch (Exception ex) { App.WriteLog("InnerDrive.PrepareTurn -> " + ex.Message); }
        });
    }

    /// <summary>回复回来：火气压不住了就按闸门选一个出口，卸掉一部分并释放内啡肽；压得住就把自律练硬一点。</summary>
    public void AfterReply(string charId, string reply)
    {
        if (!Enabled || string.IsNullOrEmpty(charId)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var p = await SettledAsync(charId);
                if (p is null) return;
                DischargeForm? justNow = null;
                double dopa = 0, endo = 0, ser = 0, cort = 0;
                lock (_gate)
                {
                    if (p.Anger >= DriveRules.OverflowAnger)
                    {
                        // 火气过顶：闸门跳掉，走最强的那一档。
                        var spec = DriveRules.Forms[DischargeForm.Defiance];
                        Apply(spec, p);
                        justNow = spec.Form;
                    }
                    else if (p.Anger >= DriveRules.AngerThreshold)
                    {
                        var impulse = DriveRules.ImpulseOf(p, DriveImpulse.Wrath);
                        if (impulse >= DriveRules.ImpulseThreshold)
                        {
                            var spec = DriveRules.PickForm(p, p.RelationSafety);
                            Apply(spec, p);
                            justNow = spec.Form;
                        }
                        else
                        {
                            // 压住了：火没发出去，但自律被练硬了一点，代价是皮质醇升高。
                            DriveRules.TrainDiscipline(p, 0.012);
                            cort = 0.03;
                        }
                    }
                    var (d, e) = DriveRules.DrivenRewardOf(DriveImpulse.Wrath);
                    if (justNow is not null) { dopa = d * 0.6; endo = e; }
                    ser = -0.02;
                    p.UpdatedAt = DateTime.UtcNow;
                }
                if (dopa != 0 || ser != 0 || cort != 0)
                    _inner.Nudge(charId, dopamine: dopa, serotonin: ser, cortisol: cort);
                _ = SaveAsync();
            }
            catch (Exception ex) { App.WriteLog("InnerDrive.AfterReply -> " + ex.Message); }
        });
    }

    /// <summary>把一次发作落到状态上：卸火、记形态、留内啡肽、动自律。</summary>
    private static void Apply(DriveRules.DischargeSpec spec, DriveProfile p)
    {
        p.Discharge = spec.Form;
        p.DischargeCharge = Math.Clamp(p.Anger, 0, 1);
        p.DischargedAt = DateTime.UtcNow;
        p.Anger = Math.Clamp(p.Anger - spec.AngerDrain, 0, 1);
        p.Wrath = Math.Clamp(p.Wrath - spec.AngerDrain * 0.6, 0, 1);
        p.Endorphin = Math.Clamp(p.Endorphin + spec.Endorphin, 0, 1);
        if (spec.SulkMinutes > 0) p.SulkingUntil = DateTime.UtcNow.AddMinutes(spec.SulkMinutes);
        DriveRules.TrainDiscipline(p, spec.Discipline);
    }

    /// <summary>
    /// 某一股驱动被满足：张力落下去，多巴胺与内啡肽涌上来。
    /// 由对话里明确达成的事（吃到、拿到、被抱住、被服气）触发，没达成就别调。
    /// </summary>
    public void Satisfy(string charId, DriveImpulse kind, double amount = 1.0)
    {
        if (!Enabled || string.IsNullOrEmpty(charId)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var p = await SettledAsync(charId);
                if (p is null) return;
                amount = Math.Clamp(amount, 0.1, 1.0);
                var (dopa, endo) = DriveRules.DrivenRewardOf(kind);
                lock (_gate)
                {
                    var before = DriveRules.PressureOf(p, kind);
                    DriveRules.SetPressure(p, kind, before - 0.45 * amount);
                    // 满足一次，闸门要么被练硬（忍到正当的时机），要么松掉（想要就立刻要）。
                    var constraint = DriveRules.ConstraintOn(p, kind);
                    DriveRules.TrainDiscipline(p, constraint >= 0.55 ? 0.010 : -0.008);
                    p.Endorphin = Math.Clamp(p.Endorphin + endo * amount, 0, 1);
                    p.UpdatedAt = DateTime.UtcNow;
                }
                _inner.Nudge(charId, dopamine: dopa * amount);
                _ = SaveAsync();
            }
            catch (Exception ex) { App.WriteLog("InnerDrive.Satisfy -> " + ex.Message); }
        });
    }

    // ---- 输出：提示词与快照 ----

    /// <summary>
    /// 给提示词的一段「此刻的内驱」。只写想做什么、火气往哪儿走了，
    /// 不报数字、不点破「驱动」「自律」这些词——她要像一个真人在生气或用劲，不是像在读面板。
    /// </summary>
    public async Task<string> PromptTextAsync(string charId)
    {
        if (!Enabled || string.IsNullOrEmpty(charId)) return "";
        var p = await SettledAsync(charId);
        if (p is null) return "";
        var lines = new List<string>();
        lock (_gate)
        {
            // 火气与它的出口
            if (p.Anger >= 0.22)
            {
                var spec = p.Discharge is { } form && DriveRules.Forms.TryGetValue(form, out var s)
                           && p.DischargedAt > DateTime.UtcNow.AddMinutes(-90)
                    ? s
                    : DriveRules.PickForm(p, p.RelationSafety);
                lines.Add($"心里还压着火，出口是「{spec.Name}」：{spec.Behavior}。");
            }
            // 最想做的事
            var dominant = p.Dominant;
            if (dominant is { } kind && kind != DriveImpulse.Wrath)
                lines.Add($"这会儿最想要的是{DriveRules.NameOf(kind)}那一口——{DriveRules.ChannelOf(kind, p)}。");
            // 自律：只在真的在起作用的场合提一句
            var constraint = DriveRules.ConstraintOn(p, dominant ?? DriveImpulse.Wrath);
            if (dominant is not null && constraint > 0.55)
                lines.Add("但心里那道闸门压着，不会为这点念头就不顾后果。");
            else if (dominant is not null && constraint < 0.25)
                lines.Add("这道闸门本来就不牢，念头一起来就顺着走了。");
            // 内啡肽：痛过之后的松快
            if (p.Endorphin > 0.62)
                lines.Add("身上有点松下来，像刚泄掉什么东西之后的空落落，语气会比刚才软。");
        }
        if (lines.Count == 0) return "";
        return "【此刻的内驱】" + string.Join("", lines)
               + "把这些只落在语气、动作与愿不愿意配合上；不要点破、不要报数值、也不要说成一段自我剖析。";
    }

    /// <summary>给监测面板 / 工具读的结构化快照。</summary>
    public async Task<object> SnapshotAsync(string charId)
    {
        var p = await SettledAsync(charId);
        if (p is null) return new { enabled = false };
        lock (_gate)
        {
            return new
            {
                enabled = true,
                powers = new
                {
                    aggression = Math.Round(p.Aggression, 2),
                    sexuality = Math.Round(p.Sexuality, 2),
                    narcissism = Math.Round(p.Narcissism, 2)
                },
                drives = new
                {
                    greed = Math.Round(p.Greed, 2), gluttony = Math.Round(p.Gluttony, 2),
                    lust = Math.Round(p.Lust, 2), envy = Math.Round(p.Envy, 2),
                    pride = Math.Round(p.Pride, 2), sloth = Math.Round(p.Sloth, 2),
                    wrath = Math.Round(p.Wrath, 2)
                },
                anger = Math.Round(p.Anger, 2),
                discharge = p.Discharge?.ToString(),
                dischargeCharge = Math.Round(p.DischargeCharge, 2),
                endorphin = Math.Round(p.Endorphin, 2),
                discipline = Math.Round(p.Discipline, 2),
                moral = p.Moral.ToString(),
                life = p.Life.ToString(),
                thinking = p.Thinking.ToString(),
                relationSafety = Math.Round(p.RelationSafety, 2)
            };
        }
    }
}
