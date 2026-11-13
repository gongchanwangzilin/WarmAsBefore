namespace WarmAsBefore.Services;

/// <summary>
/// F16 精力的纯代码结算规则。零 IO、零模型、零时间源：时间与随机由调用方注入。
///
/// 用户口径：「精力机制根据睡眠、干的活、思考的内容来评定，说话本身耗的精力比较少。」
/// 于是四项驱动分别是：
/// · 睡眠（含 F05 抱睡）—— 按小时恢复，是唯一的大额回血；
/// · 干的活 —— 按 Job 强度分级扣（挖矿/砍树 > 走动/交互）；
/// · 思考的内容 —— 按思考档位 / 深度思考扣，档位越高越耗神；
/// · 说话 —— 每轮只扣很小值（这里 0.15），「聊天」不该把人聊虚脱。
///
/// 时间流逝让精力自然向满值回落（休息），用半衰期而不是线性加满，
/// 与论文 §8「反刍正反馈可能锁死状态」一致：所有量都有界、都可回落。
/// </summary>
public static class EnergyRules
{
    /// <summary>说话每轮的耗神：刻意做得很小，说话本身耗的精力比较少。</summary>
    public const double TalkCostPerTurn = 0.15;

    /// <summary>每档思考的耗神（档位 0=关闭不扣）。</summary>
    public const double ThinkCostPerTier = 1.2;

    /// <summary>睡一小时恢复多少（睡 8 小时约回到满）。</summary>
    public const double SleepRecoveryPerHour = 11.0;

    /// <summary>精力向满值回落的半衰期（小时）：什么都不干，约 12 小时回一半。</summary>
    public const double RestHalfLifeHours = 12.0;

    /// <summary>单次事件最多扣多少，防一次误报把人打空。</summary>
    public const double MaxSingleDrain = 25.0;

    public static double Clamp(double v, double lo, double hi) => double.IsNaN(v) ? lo : v < lo ? lo : v > hi ? hi : v;

    /// <summary>劳作强度：把游戏事件/Job 名称映射成 0~1 的强度，纯查表，无随机。</summary>
    public static double WorkIntensityOf(string? kind)
    {
        var k = (kind ?? "").Trim().ToLowerInvariant();
        return k switch
        {
            // 重活：采矿、挖巷道、砍树、挖土
            "mine" or "tunnel" or "chop" or "dig" or "mining" or "lumber" => 1.0,
            // 中等：收割、建造、放置方块、舀水
            "harvest" or "build" or "place" or "scoop" or "farm" or "craft" => 0.65,
            // 轻活：受伤、死亡、复活这类体力事件
            "hurt" or "death" or "respawn" => 0.55,
            // 轻活动
            "feed" or "interaction" or "maid_chat" or "work" or "gather" => 0.25,
            _ => 0.15
        };
    }

    /// <summary>一次劳作扣多少：强度 × 基准 × 时长（小时，默认按一次动作 0.1 小时）。</summary>
    public static double WorkCost(string? kind, double hours = 0.1)
        => Clamp(WorkIntensityOf(kind) * 12.0 * Math.Max(0, hours), 0, MaxSingleDrain);

    /// <summary>一轮思考扣多少：档位 × 每档耗神，深度思考再乘 1.5。</summary>
    public static double ThinkCost(int thinkTier, bool deepThink = false)
        => Clamp(Math.Max(0, thinkTier) * ThinkCostPerTier * (deepThink ? 1.5 : 1.0), 0, MaxSingleDrain);

    /// <summary>说话扣多少：轮数 × 很小的单价。</summary>
    public static double TalkCost(int turns = 1)
        => Clamp(Math.Max(0, turns) * TalkCostPerTurn, 0, MaxSingleDrain);

    /// <summary>睡了 hours 小时恢复多少，封顶 100。</summary>
    public static double SleepRecovery(double hours)
        => Clamp(Math.Max(0, hours) * SleepRecoveryPerHour, 0, 100);

    /// <summary>时间流逝：精力向满值（100）按半衰期回落。hours 为距上次结算的小时数。</summary>
    public static double RestToward(double current, double hours)
        => hours <= 0 ? Clamp(current, 0, 100)
            : Clamp(100 - (100 - Clamp(current, 0, 100)) * Math.Pow(0.5, hours / RestHalfLifeHours), 0, 100);

    /// <summary>
    /// 综合结算：先按时间休息回落，再叠睡眠恢复，最后扣劳作 / 思考 / 说话，并叠加 AI 修正。
    /// 返回 0~100，钳制防止正反馈锁死。
    /// </summary>
    public static double Settle(double current, double hours, double sleepHours,
                                double workCost, double thinkCost, double talkCost, double aiOffset = 0)
        => Clamp(RestToward(current, hours) + SleepRecovery(sleepHours)
                 - workCost - thinkCost - talkCost + aiOffset, 0, 100);
}
