namespace WarmAsBefore.Models;

/// <summary>
/// 双系统大脑的两个档位（v1.65 契约 A 节）。
/// · Wandering 神游：低精度、无专属精力消耗、直接用总精力；负责反刍 / 整理 / 纠错 / 新想法 / 回忆连线。
/// · Focused   专注：高精度交给大模型、高精力消耗（主 Agent 在 ChatEngine 扣总精力）+ 专属精力 Reserve 消耗。
/// 数值 0/1 固定，落盘按 int 存，老档缺字段时默认 Wandering。
/// </summary>
public enum CognitionMode
{
    /// <summary>神游：低精度、低耗，脑子自己漫游。</summary>
    Wandering = 0,

    /// <summary>专注：高精度、高耗，集中处理当前任务。</summary>
    Focused = 1
}

/// <summary>
/// 专注训练簿：一个角色一份，记录专属精力的当前值、训练上限、专注次数、上次进入专注的时刻。
/// 全部字段带 init 默认值，老档缺字段自动兼容；数值的钳制与半衰期回归统一交给 <see cref="CognitionRules"/>。
/// </summary>
public sealed record FocusTraining
{
    /// <summary>专属精力当前值（0..Capacity）；专注时衰减，神游时按半衰期回满。</summary>
    public double Reserve { get; set; } = CognitionRules.InitialReserve;

    /// <summary>训练上限（Capacity）：用得越多越大，封顶 <see cref="CognitionRules.MaxCapacity"/>。</summary>
    public double Capacity { get; set; } = CognitionRules.DefaultCapacity;

    /// <summary>累计进入专注的次数。</summary>
    public int Sessions { get; set; }

    /// <summary>上次进入专注的时刻（老档缺省为 default，服务读取时兜底）。</summary>
    public DateTime LastFocusAt { get; set; }
}

/// <summary>
/// 专注/神游的纯代码结算规则。零 IO、零模型、零时间源（时间与随机由调用方注入），
/// 与 EnergyRules / HormoneRules 同款：所有量都有界、都可回落，防反刍正反馈锁死。
/// </summary>
public static class CognitionRules
{
    public const double InitialReserve = 100;
    public const double DefaultCapacity = 100;
    public const double MinCapacity = 100;
    public const double MaxCapacity = 200;

    /// <summary>专注时专属精力每分钟衰减多少（约 50 分钟耗尽满 reserve）。</summary>
    public const double FocusDrainPerMinute = 2.0;

    /// <summary>神游时专属精力回满的半衰期（小时）：休息着就慢慢回稳。</summary>
    public const double RestHalfLifeHours = 6.0;

    /// <summary>进入专注的一次性专属精力开销。</summary>
    public const double EnterReserveCost = 4.0;

    /// <summary>每次训练涨多少上限。</summary>
    public const double CapacityTrainStep = 5.0;

    /// <summary>专属精力低于这个比例且仍在专注 → 神游该介入。</summary>
    public const double InterveneRatio = 0.25;

    public static double Clamp(double v, double lo, double hi)
        => double.IsNaN(v) ? lo : v < lo ? lo : v > hi ? hi : v;

    public static double Clamp01(double v) => Clamp(v, 0, 1);

    /// <summary>上限钳到 [MinCapacity, MaxCapacity]。</summary>
    public static double CapacityOf(double capacity) => Clamp(capacity, MinCapacity, MaxCapacity);

    /// <summary>当前专属精力钳到 [0, Capacity]。</summary>
    public static double ReserveOf(double reserve, double capacity)
        => Clamp(reserve, 0, CapacityOf(capacity));

    /// <summary>
    /// 多巴胺越高进入越快。兼容两种量纲：&gt;1 视为 0..100（BrainState.Dopamine 的口径）先归一化，
    /// 否则视为 0..1。即便多巴胺为 0 也保留 0.25 的基础进入速度，不会完全进不去。
    /// </summary>
    public static double EnteringSpeedOf(double dopamine)
    {
        var d = dopamine > 1 ? dopamine / 100.0 : dopamine;
        return Clamp01(0.25 + 0.75 * Clamp01(d));
    }

    /// <summary>专注 min 分钟后的专属精力：线性衰减，封顶在 [0, Capacity]。</summary>
    public static double DrainDuringFocus(double reserve, double capacity, double minutes)
        => minutes <= 0 ? ReserveOf(reserve, capacity)
            : ReserveOf(reserve - Math.Max(0, minutes) * FocusDrainPerMinute, capacity);

    /// <summary>神游/休息 hours 小时后，专属精力按半衰期回归满值。</summary>
    public static double RestToward(double reserve, double capacity, double hours)
    {
        var cap = CapacityOf(capacity);
        var r = ReserveOf(reserve, cap);
        return hours <= 0 ? r
            : Clamp(cap - (cap - r) * Math.Pow(0.5, hours / RestHalfLifeHours), 0, cap);
    }

    /// <summary>训练一次：当前上限 + 步长，封顶 MaxCapacity。</summary>
    public static double Train(double capacity)
        => Clamp(CapacityOf(capacity) + CapacityTrainStep, MinCapacity, MaxCapacity);

    /// <summary>专注重 Reserve 掉到阈值以下但还没归零 → 神游介入（反刍/整理/纠错）。</summary>
    public static bool ShouldIntervene(CognitionMode mode, double reserve, double capacity)
        => mode == CognitionMode.Focused
           && reserve > 0
           && reserve < CapacityOf(capacity) * InterveneRatio;
}
