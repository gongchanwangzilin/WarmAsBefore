namespace WarmAsBefore.Services;

/// <summary>
/// F28「重磅角色」的纯代码结算规则。全部是纯函数，不碰存储、不碰模型、不碰时间：
/// 时间与随机一律由调用方注入，方便边界单测，也符合论文「算法层零模型调用」的分层。
///
/// 四个角色：
/// · 多巴胺 —— 收益与快乐成正比时产生干劲；AI 判定目标有收益也可触发。
/// · 肾上腺素 —— 危急时产生，精力↑警觉↑，但伴虚脱感；过量影响各器官。
/// · 胆固醇 —— 颓废且遭打击时中和：食欲↓、干劲↑、变积极；
///             若一次打击没奏效仍继续过量，则产生 PTSD。
/// · 长期性激励素 —— 长期少量止痛 + 目标达成时的奖励。
///
/// 论文 §8 提醒「反刍正反馈可能锁死状态」，因此所有值都有 [0,1] 钳制与独立半衰期，
/// 且 PTSD 只在「胆固醇越阈值 + 打击未被消化」时触发一次，不会无限累积。
/// </summary>
public static class HormoneRules
{
    /// <summary>气质基线的默认位置。角色 id 的稳定偏移由 InnerLifeService.Baseline 叠加。</summary>
    public const double BaseAdrenaline = 0.08;
    public const double BaseCholesterol = 0.20;
    public const double BaseLongTermDrive = 0.35;

    /// <summary>半衰期（小时）：肾上腺素最短（来得快去得快），长期激励素最长。</summary>
    public const double HalfLifeAdrenaline = 0.25;
    public const double HalfLifeCholesterol = 6.0;
    public const double HalfLifeLongTermDrive = 72.0;

    /// <summary>过量阈值：肾上腺素越此值开始「伤器官」（压血清素/抬皮质醇）。</summary>
    public const double AdrenalineOverload = 0.80;

    /// <summary>PTSD 阈值：胆固醇越此值且打击未被消化才触发。</summary>
    public const double PtsdCholesterol = 0.78;

    public static double Clamp01(double v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 1);

    /// <summary>按半衰期把值拉回基线。</summary>
    public static double Settle(double value, double baseline, double hours, double halfLifeHours)
        => hours <= 0 ? Clamp01(value)
            : Clamp01(baseline + (value - baseline) * Math.Pow(0.5, hours / halfLifeHours));

    /// <summary>危急事件 → 肾上腺素抬升；当前越高，再加越钝（避免瞬间打满）。</summary>
    public static double AdrenalineAfterDanger(double current, double dangerIntensity)
        => Clamp01(current + Clamp01(dangerIntensity) * (1 - current) * 0.8);

    /// <summary>目标达成 / 长期陪伴 → 长期性激励素抬升。</summary>
    public static double LongTermDriveAfterReward(double current, double reward)
        => Clamp01(current + Clamp01(reward) * (1 - current) * 0.35);

    /// <summary>
    /// 胆固醇中和：颓废（低多巴胺 + 高疲劳）且遭打击时抬升，用于把食欲压下去、干劲拉起来。
    /// 打击被消化（快感回来）时不抬升，避免把「中和」变成常态。
    /// </summary>
    public static double CholesterolAfterSetback(double current, double dopamine, double fatigue,
                                                 double setbackIntensity, bool blowSoftened)
    {
        double slack = Clamp01(setbackIntensity);
        bool decadent = dopamine < 0.42 && fatigue > 0.45;
        if (!decadent || blowSoftened) return Clamp01(current);
        return Clamp01(current + slack * 0.30);
    }

    /// <summary>是否该触发 PTSD：胆固醇过量，且这次打击没被消化。</summary>
    public static bool ShouldTriggerPtsd(double cholesterol, bool blowSoftened)
        => !blowSoftened && cholesterol >= PtsdCholesterol;

    /// <summary>肾上腺素过量的「器官代价」：压血清素、抬皮质醇。返回 (Δ血清素, Δ皮质醇)。</summary>
    public static (double Serotonin, double Cortisol) OrganCostOfAdrenaline(double adrenaline)
    {
        double over = Math.Max(0, Clamp01(adrenaline) - AdrenalineOverload);
        return (-over * 0.30, over * 0.35);
    }

    /// <summary>胆固醇对食欲的抑制（-1..0），供 SomatizationService 叠加。</summary>
    public static double AppetiteSuppressionOf(double cholesterol)
        => -Math.Max(0, Clamp01(cholesterol) - 0.35) * 0.60;

    /// <summary>虚脱感：肾上腺素高且正在回落时出现，供提示词出口使用。</summary>
    public static bool HasCrash(double adrenaline, double dopamine)
        => adrenaline > 0.55 && dopamine < 0.38;
}
