namespace WarmAsBefore.Modules.Affection;

/// <summary>
/// 能推动安全感的事件类别。前六类是「一段对话里发生过什么」，后两类是「时间本身带来什么」。
/// </summary>
public enum SecurityEventKind
{
    /// <summary>被安慰：对方蹲下来安抚、心疼、说「我在」。</summary>
    Comforted = 0,
    /// <summary>被倾听：对方追问她的感受，愿意接着听。</summary>
    Listened = 1,
    /// <summary>被忽视：有对话机会却没有得到回应。</summary>
    Ignored = 2,
    /// <summary>被拒绝抱抱或触碰。</summary>
    TouchRejected = 3,
    /// <summary>目睹主人对别的角色亲密。</summary>
    RivalIntimacy = 4,
    /// <summary>被辱骂或威胁。</summary>
    Abuse = 5,
    /// <summary>独处过久（时间漂移，缓慢下降）。</summary>
    Solitude = 6,
    /// <summary>稳定陪伴（时间漂移，缓慢回升）。</summary>
    Company = 7
}

/// <summary>一条事件规则：基础分、冷却、每日次数上限。数值即「同一件事第一次最重，连着来就麻木」。</summary>
public sealed record SecurityEventRule(
    string Id,
    SecurityEventKind Kind,
    string Name,
    int BaseDelta,
    int CooldownMinutes,
    int DailyCap);

/// <summary>
/// 安全感刻度：与好感同刻度（0–1000 点、100 点一级、共十级），等级换算一律复用
/// <see cref="Helpers.AffectionScale"/>，这里不另算一套。
///
/// 分工：系统是主力——事件基础分、特质倍率、信任/经历权重、冷却与日上限、时间漂移全在本文件的纯函数里，
/// 可被 tools/affection_selftest 直接按住对边界。小模型只在不属于「规则能算的」两类时机被调用：
/// 一段情绪事件归纳成经历时给一句复述与一个加成步长（<see cref="ClampAiStep"/> 夹住），
/// 连续同向漂移需要复核时给一个目标值（<see cref="ClampAiSuggestion"/> 夹在系统值附近）。
/// 模型永远不能直接把安全感改到它想去的地方。
///
/// 零依赖纯函数：不碰 IO、不碰时间、不碰 MAUI，可直接编进 tools 下的自测工程。
/// </summary>
public static class SecurityScale
{
    /// <summary>满值：与好感同一个上限，显示与等级也走同一套。</summary>
    public const int MaxPoints = Helpers.AffectionScale.MaxPoints;

    /// <summary>「这份存档还没写过安全感」的哨兵值：读档时按旧档补 <see cref="DefaultOnMigrate"/>。</summary>
    public const int Unset = -1;

    /// <summary>
    /// 旧档迁移用的默认值：600 —— 初始信任，但还没被相处验证过。
    /// 不给满值，否则旧档一进来就没有「建立信任」这回事了。
    /// </summary>
    public const int DefaultOnMigrate = 600;

    /// <summary>「安全感缺失」特质的目录 id（条目在 <see cref="Services.MentalHealthService.Catalog"/> 里）。</summary>
    public const string DeficitTraitId = "security_deficit";
    /// <summary>带着该特质时，流失速率 ×1.6。</summary>
    public const double DeficitLossRate = 1.6;
    /// <summary>带着该特质时，回复速率 ×0.6。</summary>
    public const double DeficitRecoverRate = 0.6;

    /// <summary>连续同向事件到这个条数、且净变化到这个点数，才值得请小模型复核一次。</summary>
    public const int ReviewStreakThreshold = 5;
    /// <summary>复核的净变化阈值（点）。</summary>
    public const int ReviewNetThreshold = 60;
    /// <summary>一次复核最多把系统值挪动多少点。</summary>
    public const int AiMaxShift = 60;
    /// <summary>归纳一条经历最多加减多少点。</summary>
    public const int AiMaxStep = 20;

    /// <summary>事件表：唯一事实来源。冷却与日上限是防刷分的两道闸。</summary>
    public static readonly IReadOnlyList<SecurityEventRule> Events = new List<SecurityEventRule>
    {
        new("comforted", SecurityEventKind.Comforted, "被安慰", 18, 20, 3),
        new("listened", SecurityEventKind.Listened, "被倾听", 12, 30, 3),
        new("ignored", SecurityEventKind.Ignored, "被忽视", -20, 90, 2),
        new("touch_rejected", SecurityEventKind.TouchRejected, "被拒绝抱抱或触碰", -28, 30, 2),
        new("rival_intimacy", SecurityEventKind.RivalIntimacy, "目睹主人对其他角色亲密", -40, 120, 1),
        new("abuse", SecurityEventKind.Abuse, "被辱骂或威胁", -65, 20, 2),
        new("solitude", SecurityEventKind.Solitude, "独处过久", -4, 180, 3),
        new("company", SecurityEventKind.Company, "稳定陪伴", 5, 240, 2)
    };

    /// <summary>按类别取规则；类别来自枚举，一定存在。</summary>
    public static SecurityEventRule RuleOf(SecurityEventKind kind)
        => Events.First(e => e.Kind == kind);

    /// <summary>折成合法点数。</summary>
    public static int Clamp(int points) => Helpers.AffectionScale.Clamp(points);

    /// <summary>点数 → 等级（1..10）：与好感同一套换算。</summary>
    public static int LevelOf(int points) => Helpers.AffectionScale.LevelOf(points);

    /// <summary>本级进度（0..1）。</summary>
    public static double ProgressInLevel(int points) => Helpers.AffectionScale.ProgressInLevel(points);

    /// <summary>「Lv.6 · 600/1000」。</summary>
    public static string TextOf(int points) => Helpers.AffectionScale.TextOf(points);

    /// <summary>「Lv.6」短标。</summary>
    public static string LevelTextOf(int points) => Helpers.AffectionScale.LevelTextOf(points);

    /// <summary>
    /// 把存下来的值折成合法安全感：没写过（负值）→ <see cref="DefaultOnMigrate"/>，越界夹回 0..1000。
    /// 旧档（结构版本 &lt; 4）全靠它补默认值——不是满值 1000，而是 600。
    /// </summary>
    public static int Migrate(int stored) => stored < 0 ? DefaultOnMigrate : Clamp(stored);

    /// <summary>流失倍率：安全感缺失特质让每一次打击都更重。</summary>
    public static double LossRate(bool deficitTrait) => deficitTrait ? DeficitLossRate : 1.0;

    /// <summary>回复倍率：同一件好事，带缺失特质的人只能收回六成的底气。</summary>
    public static double RecoverRate(bool deficitTrait) => deficitTrait ? DeficitRecoverRate : 1.0;

    /// <summary>一条事件的基础增量（已含特质倍率，未含信任/经历权重）。</summary>
    public static int DeltaOf(SecurityEventKind kind, bool deficitTrait)
    {
        var rule = RuleOf(kind);
        var rate = rule.BaseDelta >= 0 ? RecoverRate(deficitTrait) : LossRate(deficitTrait);
        return (int)Math.Round(rule.BaseDelta * rate);
    }

    /// <summary>信任作为中间量：越深的关系里，同一个事件越进心里（好事更暖，坏事更疼）。</summary>
    public static double TrustWeight(int delta, int trust)
        => delta == 0 ? 1.0 : 0.8 + 0.4 * (Math.Clamp(trust, 0, 100) / 100.0);

    /// <summary>
    /// 经历作为中间量：归纳出的经历越多，单次事件越不容易把整块安全感一把推翻
    /// （反复被验证过的关系有惯性），也因此不容易靠刷事件快速回满。下限 0.7。
    /// </summary>
    public static double MemoryWeight(int delta, int memories)
        => delta == 0 ? 1.0 : Math.Max(0.7, 1.0 / (1.0 + 0.05 * Math.Max(0, memories)));

    /// <summary>把事件增量折成实际生效的增量，落值后夹死。</summary>
    public static int Apply(int current, int delta) => Clamp(Migrate(current) + delta);

    /// <summary>
    /// 时间漂移的「段数」：独处每 3 小时算一段，稳定陪伴每 4 小时算一段，单次结算最多各 3 / 2 段
    /// ——时间只能慢慢推，不能靠一次长挂机把值推到底。
    /// </summary>
    public static int TimedChunks(SecurityEventKind kind, double elapsedHours)
    {
        var hours = Math.Max(0, elapsedHours);
        return kind switch
        {
            SecurityEventKind.Solitude => (int)Math.Min(3, Math.Floor(hours / 3.0)),
            SecurityEventKind.Company => (int)Math.Min(2, Math.Floor(hours / 4.0)),
            _ => 0
        };
    }

    /// <summary>时间漂移的增量（段数 × 基础分 × 特质倍率）。</summary>
    public static int TimedDelta(SecurityEventKind kind, double elapsedHours, bool deficitTrait)
        => DeltaOf(kind, deficitTrait) * TimedChunks(kind, elapsedHours);

    /// <summary>
    /// 好感增益折扣：安全感低时好感涨得更慢（0.6～1.0），安全感高时回到全速。
    /// 例：security=0 → ×0.6，=500 → ×0.8，=1000 → ×1.0。
    /// </summary>
    public static double AffectionGainFactor(int security)
        => 0.6 + 0.4 * (Clamp(security) / (double)MaxPoints);

    /// <summary>好感衰减倍率：安全感高时掉得更慢（1.25 → 0.75），安全感低时更容易掉。</summary>
    public static double AffectionDecayFactor(int security)
        => 1.25 - 0.5 * (Clamp(security) / (double)MaxPoints);

    /// <summary>好感增量按安全感打折/减速（正增量走增益折扣，负增量走衰减倍率）。</summary>
    public static int ScaleAffection(int delta, int security)
        => delta == 0 ? 0 : (int)Math.Round(delta * (delta > 0 ? AffectionGainFactor(security) : AffectionDecayFactor(security)));

    /// <summary>
    /// 回避倾向 0..1：安全感越低越容易先一步退开（不吭声、找借口离开、抽回手）。
    /// 带缺失特质时再往上抬一档——它本身就是「先退开」的体质。
    /// </summary>
    public static double AvoidanceBias(int security, bool deficitTrait = false)
    {
        var bias = Math.Clamp((500 - Clamp(security)) / 500.0, 0, 1);
        return deficitTrait ? Math.Min(1, bias + 0.15) : bias;
    }

    /// <summary>连续同向变化超过阈值才复核：够长（streak）且够远（净变化）。</summary>
    public static bool NeedsDriftReview(int driftStreak, int driftNet)
        => Math.Abs(driftStreak) >= ReviewStreakThreshold && Math.Abs(driftNet) >= ReviewNetThreshold;

    /// <summary>
    /// 小模型复核的夹取：它给的是「目标值」，最终只允许在系统当前值附近挪 <paramref name="maxShift"/> 点。
    /// 模型说 200 也好、说 1000 也好，落下来都得贴着系统算出来的位置。
    /// </summary>
    public static int ClampAiSuggestion(int current, int suggested, int maxShift = AiMaxShift)
    {
        var now = Migrate(current);
        var shift = Math.Clamp(suggested - now, -Math.Abs(maxShift), Math.Abs(maxShift));
        return Clamp(now + shift);
    }

    /// <summary>小模型归纳经历时给的加成步长夹取（±<paramref name="maxAbs"/>）。</summary>
    public static int ClampAiStep(int step, int maxAbs = AiMaxStep)
        => Math.Clamp(step, -Math.Abs(maxAbs), Math.Abs(maxAbs));
}
