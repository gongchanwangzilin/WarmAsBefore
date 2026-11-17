namespace WarmAsBefore.Models;

/// <summary>
/// 抽象器官：把现有的「躯体化 + 生理 + 内分泌 + 大脑预判」权威数值，组织成 8 个可读的节点。
/// 器官本身不产生任何新数值，只负责读取、包装成「状态灯 + 症状 + 影响说明」，供超级监测页显示。
/// 数据来源见 <see cref="OrganKinds.Describe"/>；器官之间的连锁规则见 <see cref="OrganInfluence"/>。
/// </summary>
public enum OrganKind
{
    /// <summary>胃：SomatizationService.Appetite + 胃灼烧/绞痛症状（愤怒→灼烧→食欲 -0.45）。</summary>
    Stomach = 0,

    /// <summary>心：InnerChemicals.Adrenaline + 高唤起心悸；想念/安心→暖。</summary>
    Heart = 1,

    /// <summary>肺：SomaticLoad &gt; 0.8 或 negArousal &gt; 0.45 → 呼吸急促。</summary>
    Lungs = 2,

    /// <summary>肌肉：MoveDrive + Restless；悲伤/疲劳→不想动→不想说话。</summary>
    Muscle = 3,

    /// <summary>脑：BrainState.Dopamine（50 平静）+ ExpectedWarmth + InnerChemicals.Drive。</summary>
    Brain = 4,

    /// <summary>肠：SomaticLoad（肚子隐隐发紧）+ 压力。</summary>
    Gut = 5,

    /// <summary>皮肤：PhysiologyState.Warmth（冷/热）。</summary>
    Skin = 6,

    /// <summary>内分泌：InnerChemicals 七项（多巴胺/皮质醇/血清素/疲劳/肾上腺素/胆固醇/长期性激励素）。</summary>
    Endocrine = 7
}

/// <summary>
/// 器官状态灯。亮灯 = Active / Warn / Alert（监测页据此把节点点亮）；
/// Normal 平静、Sleeping 对应服务在但功能关闭或暂无数据、Inactive 连服务都拿不到（降级，不抛异常）。
/// </summary>
public enum OrganStatus
{
    /// <summary>平静：权威数值在舒适区，没有症状。</summary>
    Normal = 0,

    /// <summary>苗头：数值接近阈值，尚未触发症状。</summary>
    Warn = 1,

    /// <summary>告警：已有明显症状或负荷越线（如胃灼烧、心悸、呼吸急）。</summary>
    Alert = 2,

    /// <summary>正激活：不是坏事，而是「正在起作用」——雀跃、踏实、有干劲、暖和，同样点亮节点。</summary>
    Active = 3,

    /// <summary>休眠：服务在，但功能关闭或该角色还没有数据；数值冻结。</summary>
    Sleeping = 4,

    /// <summary>未激活：依赖的服务取不到，这一路降级显示，不参与亮灯。</summary>
    Inactive = 5
}

/// <summary>器官种类（8 个）的中文名、说明与遍历顺序，界面和提示词都从这里取，避免各处写死。</summary>
public static class OrganKinds
{
    /// <summary>固定顺序：胃 → 心 → 肺 → 肌肉 → 脑 → 肠 → 皮肤 → 内分泌。</summary>
    public static IReadOnlyList<OrganKind> All { get; } = new[]
    {
        OrganKind.Stomach, OrganKind.Heart, OrganKind.Lungs, OrganKind.Muscle,
        OrganKind.Brain, OrganKind.Gut, OrganKind.Skin, OrganKind.Endocrine
    };

    public static string Name(OrganKind kind) => kind switch
    {
        OrganKind.Stomach => "胃",
        OrganKind.Heart => "心",
        OrganKind.Lungs => "肺",
        OrganKind.Muscle => "肌肉",
        OrganKind.Brain => "脑",
        OrganKind.Gut => "肠",
        OrganKind.Skin => "皮肤",
        OrganKind.Endocrine => "内分泌",
        _ => "未知器官"
    };

    /// <summary>一句话说清「这个器官读的是谁家的哪个字段、什么条件下会亮」。</summary>
    public static string Describe(OrganKind kind) => kind switch
    {
        OrganKind.Stomach => "读躯体化的食欲与胃灼烧/绞痛：愤怒烦躁（negArousal）或高皮质醇让胃灼烧、食欲下降。",
        OrganKind.Heart => "读内分泌的肾上腺素与情绪配比：高唤起会心悸，想念/安心会心里发暖。",
        OrganKind.Lungs => "读躯体化负荷与高唤起：负荷越线或紧张时呼吸急促。",
        OrganKind.Muscle => "读躯体化的走动欲与坐立难安：悲伤、疲劳时不想动，连带说话欲下降。",
        OrganKind.Brain => "读大脑预判的多巴胺（50 为平静）、预期温暖与干劲：被回应打动或失落都会亮。",
        OrganKind.Gut => "读躯体化负荷：压力大时肚子隐隐发紧，牵连睡眠与心情。",
        OrganKind.Skin => "读生理的冷暖：太冷想靠近暖处，太热想扇风喝凉的。",
        OrganKind.Endocrine => "读内分泌七项：激素按半衰期向气质基线回落，长期性激励素让负荷累积变慢。",
        _ => ""
    };
}

/// <summary>状态灯的中文名与亮灯判定；监测页只需绑 <see cref="OrganState.StatusText"/> 与 <see cref="OrganState.IsLit"/>。</summary>
public static class OrganStatuses
{
    public static string Name(OrganStatus status) => status switch
    {
        OrganStatus.Normal => "平静",
        OrganStatus.Warn => "苗头",
        OrganStatus.Alert => "告警",
        OrganStatus.Active => "激活",
        OrganStatus.Sleeping => "休眠",
        OrganStatus.Inactive => "未激活",
        _ => "未知"
    };

    /// <summary>是否点亮节点（与契约一致：亮 = Active / Warn / Alert）。</summary>
    public static bool IsLit(OrganStatus status) =>
        status is OrganStatus.Active or OrganStatus.Warn or OrganStatus.Alert;
}

/// <summary>
/// 一条器官连锁影响规则（纯展示 + 驱动亮灯，绝不回写权威数值）。
/// <para>From / To 用中文短名而不是枚举：第 6 条规则的「To = 全身」不是真实器官节点，用字符串更好表达。</para>
/// </summary>
public sealed record OrganInfluence
{
    /// <summary>起因器官（中文短名，如「脑」「胃」「内分泌」）。</summary>
    public string From { get; init; } = "";

    /// <summary>受影响对象（中文短名，「全身」表示整体躯体化）。</summary>
    public string To { get; init; } = "";

    /// <summary>人话说明：「因何触发 → 导致什么」，直接可显示。</summary>
    public string Effect { get; init; } = "";

    /// <summary>经验上的传导延迟（分钟），只用于流程图上的时间感，不驱动真实结算。</summary>
    public int DelayMinutes { get; init; }
}

/// <summary>
/// 一个器官此刻的展示状态。所有字段从权威服务读取后包装，<see cref="UpdatedAt"/> 取读取时刻。
/// </summary>
public sealed record OrganState
{
    /// <summary>器官种类（8 选 1）。</summary>
    public OrganKind Kind { get; init; }

    /// <summary>中文名（由 <see cref="Kind"/> 派生，避免各处写死）。</summary>
    public string Name => OrganKinds.Name(Kind);

    /// <summary>权威数值归一化到 0..1。含义见 <see cref="OrganKinds.Describe"/>；拿不到服务时为 0。</summary>
    public double Value { get; init; }

    /// <summary>状态灯：Normal / Warn / Alert / Active / Sleeping / Inactive。</summary>
    public OrganStatus Status { get; init; }

    /// <summary>状态灯的中文名，便于直接绑定显示。</summary>
    public string StatusText => OrganStatuses.Name(Status);

    /// <summary>是否点亮（亮 = Active / Warn / Alert）。</summary>
    public bool IsLit => OrganStatuses.IsLit(Status);

    /// <summary>当前症状（复用现有中文短词：胃里有灼烧感 / 肚子隐隐发紧 / 呼吸有点急 …）。</summary>
    public IReadOnlyList<string> Symptoms { get; init; } = Array.Empty<string>();

    /// <summary>与该器官相关的影响说明（人话：「愤怒→胃灼烧→食欲下降」）。</summary>
    public IReadOnlyList<string> Influences { get; init; } = Array.Empty<string>();

    /// <summary>本次读取时刻（UTC）。</summary>
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}
