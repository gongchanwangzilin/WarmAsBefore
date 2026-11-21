using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 内驱动系统的纯规则层：把所有「谁推谁、什么时候给出口、给哪个出口、给完释放多少」
/// 写成可读的常数与判定，不调模型、不碰存储。
///
/// 分三层看：
/// · 基本动力（攻击性/性/自恋）是力的来源，决定「多容易被推动」；
/// · 七个驱动是当下被推到哪儿，决定「想做什么」；
/// · 自律是闸门，由道德观、人生观、思维主义共同决定方向与力道，
///   所以同样一团火气，有人拍桌子，有人绷着脸不说话，有人直接退回自己的世界。
/// </summary>
public static class DriveRules
{
    /// <summary>火气涨到这个值就必须找出口：再压就不叫克制，叫闷着。</summary>
    public const double AngerThreshold = 0.30;

    /// <summary>火气涨到这个值以上，自律再高也压不住——闸门会跳。</summary>
    public const double OverflowAnger = 0.88;

    /// <summary>驱动压力涨到这个值，她会有明显的「想做点什么」。</summary>
    public const double ImpulseThreshold = 0.42;

    // ---- 半衰期（小时）：往各自的气质基线回落 ----

    public const double HalfLifeAnger = 1.2;
    public const double HalfLifeEndorphin = 0.8;
    public const double HalfLifePower = 3.5;
    public const double HalfLifeDrive = 2.6;

    /// <summary>怄气时火气被时间稀释得更慢：绷着脸本来就不打算这么快好。</summary>
    public const double SulkingSlowdown = 2.2;

    // ---- 气质基线：由角色 id 决定，同一个人永远同一副性子 ----

    public static (double Aggression, double Sexuality, double Narcissism, double Discipline) Baseline(string charId)
    {
        var h = 2166136261;
        foreach (var c in charId ?? "") { h ^= c; h *= 16777619; }
        double Jitter(int salt, double amp)
        {
            var x = (uint)(h ^ (salt * 0x9E3779B9));
            x ^= x >> 13; x *= 0x85EBCA6B; x ^= x >> 16;
            return amp * (x / (double)uint.MaxValue * 2 - 1);
        }
        return (
            Math.Clamp(0.48 + Jitter(11, 0.14), 0.20, 0.80),
            Math.Clamp(0.52 + Jitter(12, 0.14), 0.25, 0.85),
            Math.Clamp(0.50 + Jitter(13, 0.14), 0.22, 0.82),
            Math.Clamp(0.52 + Jitter(14, 0.12), 0.28, 0.78));
    }

    public static double Settle(double value, double baseline, double hours, double halfLifeHours)
    {
        if (hours <= 0 || halfLifeHours <= 0) return value;
        var pull = Math.Pow(0.5, hours / halfLifeHours);
        return Math.Clamp(baseline + (value - baseline) * pull, 0, 1);
    }

    public static double PressureOf(DriveProfile p, DriveImpulse kind) => kind switch
    {
        DriveImpulse.Greed => p.Greed,
        DriveImpulse.Gluttony => p.Gluttony,
        DriveImpulse.Lust => p.Lust,
        DriveImpulse.Envy => p.Envy,
        DriveImpulse.Pride => p.Pride,
        DriveImpulse.Sloth => p.Sloth,
        DriveImpulse.Wrath => p.Anger,
        _ => 0
    };

    public static void SetPressure(DriveProfile p, DriveImpulse kind, double value)
    {
        value = Math.Clamp(value, 0, 1);
        switch (kind)
        {
            case DriveImpulse.Greed: p.Greed = value; break;
            case DriveImpulse.Gluttony: p.Gluttony = value; break;
            case DriveImpulse.Lust: p.Lust = value; break;
            case DriveImpulse.Envy: p.Envy = value; break;
            case DriveImpulse.Pride: p.Pride = value; break;
            case DriveImpulse.Sloth: p.Sloth = value; break;
            case DriveImpulse.Wrath: p.Wrath = value; break;
        }
    }

    // ---- 自律：闸门的力道与方向 ----

    /// <summary>
    /// 某一种驱动此刻被压制的程度 0~1。自律是公共力道，
    /// 道德观/人生观/思维主义决定「管得着哪几样、管得多紧」：
    /// 守序与克制几乎压住全部，享乐只在贪欲上松、利己只压短期冲动。
    /// </summary>
    public static double ConstraintOn(DriveProfile p, DriveImpulse kind)
    {
        // 每种气质对每类驱动的天生敏感度（守序的人对贪欲/色欲/暴食更敏感，共情的人对暴怒更敏感……）
        var tuned = (p.Moral, p.Life) switch
        {
            (MoralStance.Order, _) => 1.15,
            (MoralStance.Altruism, _) => 1.05,
            (MoralStance.Care, _) => 1.00,
            (MoralStance.Pragmatic, _) => 0.90,
            (MoralStance.Egoism, _) => 0.78,
            _ => 1.00
        };
        tuned *= p.Life switch
        {
            LifeStance.Asceticism => 1.25,
            LifeStance.Duty => 1.12,
            LifeStance.Growth => 1.00,
            LifeStance.Nihilism => 0.80,
            LifeStance.Hedonism => 0.72,
            _ => 1.00
        };
        // 理性的人先算后果，所以克制得更早；偏执的人容易在某一样上不肯松手。
        tuned *= p.Thinking switch
        {
            ThinkingStyle.Rational => 1.12,
            ThinkingStyle.Empathic => 1.05,
            ThinkingStyle.Intuitive => 0.96,
            ThinkingStyle.Cynical => 0.92,
            ThinkingStyle.Obsessive => 0.88,
            _ => 1.00
        };
        // 利己 + 高自律：能舍小利保大利——短期贪欲压得住，但对自己的大利照样下手。
        if (p.Moral == MoralStance.Egoism)
            tuned *= kind switch
            {
                DriveImpulse.Greed => p.Discipline > 0.5 ? 1.06 : 0.70,
                DriveImpulse.Wrath => 0.86,
                _ => 0.94
            };
        // 性这一项另算：克制型人生观对它的压制最明显。
        if (kind == DriveImpulse.Lust && p.Life == LifeStance.Asceticism) tuned *= 1.2;
        return Math.Clamp(p.Discipline * tuned, 0, 1);
    }

    /// <summary>被自律按过之后，真正推动行为的力 0~1。闸门越高，剩下来的越少。</summary>
    public static double ImpulseOf(DriveProfile p, DriveImpulse kind)
    {
        var pressure = PressureOf(p, kind);
        var keep = 1.0 - 0.75 * ConstraintOn(p, kind);
        return Math.Clamp(pressure * keep, 0, 1);
    }

    /// <summary>自律的锻炼：成功克制一次涨一点，彻底放纵掉一点。上下限由人生观兜住。</summary>
    public static double TrainDiscipline(DriveProfile p, double delta)
    {
        var floor = p.Life == LifeStance.Hedonism ? 0.15 : 0.20;
        var ceil = p.Life == LifeStance.Asceticism ? 0.98 : (p.Thinking == ThinkingStyle.Obsessive ? 0.90 : 0.95);
        p.Discipline = Math.Clamp(p.Discipline + delta, floor, ceil);
        return p.Discipline;
    }

    // ---- 人设 → 三种观念：本地关键词归类，不花钱 ----

    public static (MoralStance Moral, LifeStance Life, ThinkingStyle Thinking) ClassifyOutlook(
        string? moral, string? life, string? ideology)
    {
        var m = Score(moral, new (string, MoralStance)[]
        {
            ("利己", MoralStance.Egoism), ("自私", MoralStance.Egoism), ("自己的利益", MoralStance.Egoism),
            ("利他", MoralStance.Altruism), ("奉献", MoralStance.Altruism), ("为别人", MoralStance.Altruism),
            ("规则", MoralStance.Order), ("契约", MoralStance.Order), ("秩序", MoralStance.Order), ("原则", MoralStance.Order),
            ("体谅", MoralStance.Care), ("感情", MoralStance.Care), ("在乎", MoralStance.Care), ("温柔", MoralStance.Care),
            ("实用", MoralStance.Pragmatic), ("务实", MoralStance.Pragmatic), ("结果", MoralStance.Pragmatic)
        }, MoralStance.Care);

        var l = Score(life, new (string, LifeStance)[]
        {
            ("享乐", LifeStance.Hedonism), ("及时行乐", LifeStance.Hedonism), ("快乐", LifeStance.Hedonism),
            ("克制", LifeStance.Asceticism), ("禁欲", LifeStance.Asceticism), ("节制", LifeStance.Asceticism), ("修行", LifeStance.Asceticism),
            ("责任", LifeStance.Duty), ("守护", LifeStance.Duty), ("担当", LifeStance.Duty),
            ("虚无", LifeStance.Nihilism), ("没意义", LifeStance.Nihilism), ("无所谓", LifeStance.Nihilism),
            ("成长", LifeStance.Growth), ("变强", LifeStance.Growth), ("学习", LifeStance.Growth)
        }, LifeStance.Growth);

        var t = Score(ideology, new (string, ThinkingStyle)[]
        {
            ("理性", ThinkingStyle.Rational), ("逻辑", ThinkingStyle.Rational), ("计算", ThinkingStyle.Rational),
            ("直觉", ThinkingStyle.Intuitive), ("凭感觉", ThinkingStyle.Intuitive),
            ("共情", ThinkingStyle.Empathic), ("体察", ThinkingStyle.Empathic), ("温柔", ThinkingStyle.Empathic),
            ("偏执", ThinkingStyle.Obsessive), ("执念", ThinkingStyle.Obsessive), ("认死理", ThinkingStyle.Obsessive),
            ("犬儒", ThinkingStyle.Cynical), ("怀疑", ThinkingStyle.Cynical), ("看透", ThinkingStyle.Cynical)
        }, ThinkingStyle.Empathic);

        return (m, l, t);
    }

    private static T Score<T>(string? text, (string Cue, T Value)[] table, T fallback)
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;
        var best = fallback;
        var bestHits = 0;
        foreach (var value in table.Select(t => t.Value).Distinct())
        {
            var hits = table.Count(t => Equals(t.Value, value)
                                        && text.Contains(t.Cue, StringComparison.Ordinal));
            if (hits > bestHits) { bestHits = hits; best = value; }
        }
        return best;
    }

    // ---- 火气的出口：八种形态 ----

    /// <summary>一种发泄形态的完整定义。</summary>
    public sealed record DischargeSpec(
        DischargeForm Form,
        string Name,
        string Behavior,
        double Dopamine,
        double Endorphin,
        double Cortisol,
        double Serotonin,
        double AngerDrain,
        double Discipline,
        int SulkMinutes);

    /// <summary>
    /// 八种出口的取值表，从最强烈到最隐蔽。
    /// AngerDrain 是这一次发作当场卸掉多少火气；没卸完的留在身上，转成闷、转成松快之后的空。
    /// </summary>
    public static readonly IReadOnlyDictionary<DischargeForm, DischargeSpec> Forms =
        new Dictionary<DischargeForm, DischargeSpec>
        {
            [DischargeForm.Defiance] = new(DischargeForm.Defiance, "叛逆",
                "正面对抗：偏要这么干、当面顶回去，话直、声音大",
                0.10, 0.10, 0.06, -0.02, 0.58, -0.020, 0),
            [DischargeForm.Tantrum] = new(DischargeForm.Tantrum, "撒泼",
                "又踢又叫、躺地哭闹：要人看见、要人哄，闹到对方让步为止",
                0.08, 0.12, 0.05, 0.00, 0.52, -0.028, 0),
            [DischargeForm.Sulking] = new(DischargeForm.Sulking, "怄气",
                "绷着脸不说话：不吵也不走，用沉默和时间把气耗掉，要对方先低头",
                -0.02, 0.04, 0.04, 0.00, 0.20, 0.012, 45),
            [DischargeForm.Willfulness] = new(DischargeForm.Willfulness, "任性",
                "固执己见、不配合：道理听不进，反正就要按自己的来",
                0.03, 0.03, 0.02, 0.00, 0.36, 0.000, 0),
            [DischargeForm.Gloom] = new(DischargeForm.Gloom, "郁闷",
                "情绪低落、向内消沉：话变少、兴致没了，连平时喜欢的东西都提不起劲",
                -0.06, 0.02, 0.03, -0.02, 0.28, 0.006, 0),
            [DischargeForm.Procrastination] = new(DischargeForm.Procrastination, "拖延",
                "行动迟缓：应下的事拖着不动，用「等一下」表达不满",
                -0.04, 0.00, 0.02, 0.00, 0.22, -0.004, 0),
            [DischargeForm.Irritation] = new(DischargeForm.Irritation, "厌烦",
                "不耐烦、冷淡：嗯、哦、随便，语气短，懒得解释",
                0.00, 0.01, 0.01, -0.01, 0.18, 0.002, 0),
            [DischargeForm.Withdrawal] = new(DischargeForm.Withdrawal, "退缩",
                "退回自己的世界：躲开、不接话，把门关上自己待着",
                -0.08, 0.02, 0.02, -0.03, 0.15, 0.010, 0)
        };

    /// <summary>
    /// 选出这一团火气此刻走哪个出口。
    ///
    /// 表达力 = 火气 + 修正项。修正项由三样东西给：她有多容易被推动（攻击性）、
    /// 闸门有多硬（自律）、以及在谁面前（关系安全度）——再加上道德观/人生观/思维主义的偏移。
    /// 用加法而不是连乘，是为了让「火气 0.5 的人」落在中段（郁闷到任性），
    /// 而不是被三个小于 1 的系数压成一概「厌烦」。
    ///
    /// 有两条硬门槛：叛逆要求够狠的攻击性，撒泼要求够强的「要人看见」（自恋），
    /// 达不到就退一档——这也符合生活里的样子：不那么横的人再气也只是绷着脸。
    /// </summary>
    public static DischargeSpec PickForm(DriveProfile p, double relationSafety)
    {
        var anger = Math.Clamp(p.Anger, 0, 1);
        var safety = Math.Clamp(relationSafety <= 0 ? 0.55 : relationSafety, 0, 1);
        var bias = 0.5 * (p.Aggression - 0.5)
                   + 0.5 * (0.5 - p.Discipline)
                   + 0.5 * (safety - 0.5)
                   + 0.30 * (p.Narcissism - 0.5);
        bias += p.Moral switch
        {
            MoralStance.Order => -0.10, MoralStance.Altruism => -0.05,
            MoralStance.Egoism => 0.06, _ => 0
        };
        bias += p.Life switch
        {
            LifeStance.Asceticism => -0.08, LifeStance.Hedonism => 0.06, _ => 0
        };
        bias += p.Thinking switch
        {
            ThinkingStyle.Rational => -0.06, ThinkingStyle.Cynical => -0.05,
            ThinkingStyle.Obsessive => 0.08, _ => 0
        };
        var expressive = Math.Clamp(anger + bias * 0.45, 0, 1);
        var band = Math.Clamp((int)Math.Round(expressive * 8), 1, 8);
        // 在不安全的关系里，火不会往外放——不是不想，是不敢。她只往内消，缩到郁闷为止；
        // 只有在火气快到失控（≥0.95）时才会破例炸出来。
        if (safety < 0.30 && anger < 0.95) band = Math.Min(band, 4);
        var form = band switch
        {
            >= 8 => p.Aggression >= 0.45 ? DischargeForm.Defiance : DischargeForm.Tantrum,
            7 => p.Narcissism >= 0.45 ? DischargeForm.Tantrum : DischargeForm.Sulking,
            6 => DischargeForm.Sulking,
            5 => DischargeForm.Willfulness,
            4 => DischargeForm.Gloom,
            3 => DischargeForm.Procrastination,
            2 => DischargeForm.Irritation,
            _ => DischargeForm.Withdrawal
        };
        return Forms[form];
    }

    // ---- 输入：一句话里能读出点什么 ----

    /// <summary>线索词 → 驱动：（说法, 哪股驱动, 权重）。命中多条就叠加。</summary>
    public static readonly IReadOnlyList<(string Cue, DriveImpulse Kind, double Weight)> DriveCues = new (string, DriveImpulse, double)[]
    {
        ("钱", DriveImpulse.Greed, 0.5), ("赚", DriveImpulse.Greed, 0.6), ("涨", DriveImpulse.Greed, 0.4),
        ("工资", DriveImpulse.Greed, 0.7), ("奖金", DriveImpulse.Greed, 0.7), ("出名", DriveImpulse.Greed, 0.8),
        ("名利", DriveImpulse.Greed, 0.9), ("权力", DriveImpulse.Greed, 0.8), ("掌控", DriveImpulse.Greed, 0.8),
        ("升职", DriveImpulse.Greed, 0.7), ("买", DriveImpulse.Greed, 0.3),
        ("吃", DriveImpulse.Gluttony, 0.5), ("饿", DriveImpulse.Gluttony, 0.7), ("零食", DriveImpulse.Gluttony, 0.8),
        ("奶茶", DriveImpulse.Gluttony, 0.8), ("甜", DriveImpulse.Gluttony, 0.5), ("火锅", DriveImpulse.Gluttony, 0.8),
        ("夜宵", DriveImpulse.Gluttony, 0.8), ("饱", DriveImpulse.Gluttony, 0.6),
        ("亲", DriveImpulse.Lust, 0.6), ("吻", DriveImpulse.Lust, 0.8), ("抱", DriveImpulse.Lust, 0.5),
        ("贴", DriveImpulse.Lust, 0.5), ("身体", DriveImpulse.Lust, 0.6), ("想要你", DriveImpulse.Lust, 0.9),
        ("别人", DriveImpulse.Envy, 0.4), ("同学", DriveImpulse.Envy, 0.3), ("比我", DriveImpulse.Envy, 0.9),
        ("凭什么", DriveImpulse.Envy, 0.7), ("晒", DriveImpulse.Envy, 0.6),
        ("夸", DriveImpulse.Pride, 0.5), ("厉害", DriveImpulse.Pride, 0.6), ("面子", DriveImpulse.Pride, 0.8),
        ("丢脸", DriveImpulse.Pride, 0.8), ("看得起", DriveImpulse.Pride, 0.7), ("看不起", DriveImpulse.Pride, 0.9),
        ("累", DriveImpulse.Sloth, 0.5), ("困", DriveImpulse.Sloth, 0.5), ("不想动", DriveImpulse.Sloth, 0.8),
        ("麻烦", DriveImpulse.Sloth, 0.6), ("躺", DriveImpulse.Sloth, 0.6)
    };

    /// <summary>让人上火的说法：顶撞、否定、冷话。</summary>
    public static readonly IReadOnlyList<(string Cue, double Weight)> AngerCues = new (string, double)[]
    {
        ("滚", 0.9), ("烦", 0.7), ("讨厌", 0.8), ("闭嘴", 0.9), ("算了", 0.4), ("随便", 0.5),
        ("没意思", 0.5), ("无聊", 0.4), ("不想理", 0.8), ("你懂什么", 0.8), ("凭什么", 0.7),
        ("不行", 0.4), ("闭嘴", 0.9), ("别来了", 0.7), ("不理你", 0.7), ("够了", 0.6)
    };

    /// <summary>让人心里松下来的说法：被看见、被认可、被抱住。</summary>
    public static readonly IReadOnlyList<(string Cue, double Weight)> WarmCues = new (string, double)[]
    {
        ("喜欢", 0.7), ("想你", 0.7), ("爱你", 0.9), ("宝贝", 0.6), ("亲爱的", 0.6),
        ("抱抱", 0.7), ("亲亲", 0.7), ("乖", 0.5), ("可爱", 0.6), ("好棒", 0.8),
        ("谢谢你", 0.7), ("辛苦", 0.6), ("晚安", 0.5), ("早安", 0.5), ("在乎", 0.7), ("惦记", 0.6)
    };

    /// <summary>三种基本动力各自的满足回报：被满足时释放多少多巴胺与内啡肽。</summary>
    public static (double Dopamine, double Endorphin, double Serotonin, double Cortisol) RewardOf(BasePower power) => power switch
    {
        // 攻击性满足是「赢」的快感：最锐、最短暂，也最耗人。
        BasePower.Aggression => (0.14, 0.10, -0.02, -0.10),
        // 性（连接）满足是「被接住」：多巴胺不一定最高，但安定与镇痛最强。
        BasePower.Sexuality => (0.10, 0.14, 0.10, -0.14),
        // 自恋满足是「被看见」：短期最亮，掉得也快。
        BasePower.Narcissism => (0.16, 0.04, 0.04, -0.08),
        _ => (0, 0, 0, 0)
    };

    /// <summary>驱动被满足后的回报：贪欲与色欲给的快感最直接，怠惰给的是「终于不用动了」。</summary>
    public static (double Dopamine, double Endorphin) DrivenRewardOf(DriveImpulse kind) => kind switch
    {
        DriveImpulse.Greed => (0.12, 0.05),
        DriveImpulse.Gluttony => (0.10, 0.08),
        DriveImpulse.Lust => (0.12, 0.10),
        DriveImpulse.Envy => (0.08, 0.03),
        DriveImpulse.Pride => (0.14, 0.04),
        DriveImpulse.Sloth => (0.02, 0.10),
        DriveImpulse.Wrath => (0.08, 0.08),
        _ => (0, 0)
    };

    /// <summary>给提示词用的人话名字。</summary>
    public static string NameOf(DriveImpulse kind) => kind switch
    {
        DriveImpulse.Greed => "贪欲",
        DriveImpulse.Gluttony => "暴食",
        DriveImpulse.Lust => "色欲",
        DriveImpulse.Envy => "嫉妒",
        DriveImpulse.Pride => "傲慢",
        DriveImpulse.Sloth => "怠惰",
        DriveImpulse.Wrath => "暴怒",
        _ => "驱动"
    };

    /// <summary>这一股驱动被满足时会往哪个方向去做（给提示词的行为线索，不是台词）。</summary>
    public static string ChannelOf(DriveImpulse kind, DriveProfile p) => kind switch
    {
        DriveImpulse.Greed => p.Moral switch
        {
            MoralStance.Egoism => "想弄到手、想握住更多（钱、名、或者说话的份量）",
            MoralStance.Altruism => "想替在意的人多争一点",
            _ => "想把一件事办成、做得比现在更大一点"
        },
        DriveImpulse.Gluttony => "想吃点好的、想把空的地方填满",
        DriveImpulse.Lust => "想靠近、想被抱住、想有身体的接触",
        DriveImpulse.Envy => "心里在跟别人比，越比越不是滋味",
        DriveImpulse.Pride => "想被高看、想被服气，不肯低头",
        DriveImpulse.Sloth => "只想躺着，什么都不动",
        DriveImpulse.Wrath => "憋着火，等一个出口",
        _ => ""
    };
}
