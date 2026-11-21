namespace WarmAsBefore.Models;

/// <summary>人的三种基本动力：往外推的那三股力。</summary>
public enum BasePower
{
    /// <summary>攻击性：向外表达的力量——愤怒、竞争、捍卫边界。</summary>
    Aggression = 0,
    /// <summary>性：广义的连接、亲密与生命力的驱力。</summary>
    Sexuality = 1,
    /// <summary>自恋：维持自我价值感、被看见、被认可的需求。</summary>
    Narcissism = 2
}

/// <summary>
/// 七个驱动。名字取「驱动」而不是那七个罪——它是推人去做事的力，
/// 不是道德判词：贪欲可以是渴望名利，也可以是渴望把一件事握在自己手里。
/// </summary>
public enum DriveImpulse
{
    /// <summary>贪欲：名利、金钱、掌控感。三样都是「想要更多」的不同出口。</summary>
    Greed = 0,
    /// <summary>暴食：口腹、囤积、把空处填满。</summary>
    Gluttony = 1,
    /// <summary>色欲：肉身与亲密的渴。</summary>
    Lust = 2,
    /// <summary>嫉妒：看见别人拥有而自己缺的那一块。</summary>
    Envy = 3,
    /// <summary>傲慢：被高看、被服气、不肯低头。</summary>
    Pride = 4,
    /// <summary>怠惰：什么都不想动，只想躺着。</summary>
    Sloth = 5,
    /// <summary>暴怒：火气本身，压不住就要往外冲。</summary>
    Wrath = 6
}

/// <summary>
/// 火气的八种出口，从最强烈到最隐蔽。数值越小越隐蔽：
/// 退缩最隐蔽，叛逆最直接。同一个火气强度，落到哪个形态由自律、攻击性与关系安全度共同决定。
/// </summary>
public enum DischargeForm
{
    /// <summary>退缩：最隐蔽，退回自己的世界。</summary>
    Withdrawal = 1,
    /// <summary>厌烦：不耐烦、冷淡。</summary>
    Irritation = 2,
    /// <summary>拖延：用行动迟缓表达不满。</summary>
    Procrastination = 3,
    /// <summary>郁闷：情绪低落、向内消沉。</summary>
    Gloom = 4,
    /// <summary>任性：固执己见、不配合。</summary>
    Willfulness = 5,
    /// <summary>怄气：绷脸沉默，用时间稀释愤怒。</summary>
    Sulking = 6,
    /// <summary>撒泼：又踢又叫、躺地哭闹。</summary>
    Tantrum = 7,
    /// <summary>叛逆：最直接、最强烈的反抗。</summary>
    Defiance = 8
}

/// <summary>道德观：什么该做、什么绝不越线。</summary>
public enum MoralStance
{
    /// <summary>利己：先算自己那一份（但可以是精算型——舍小利保大利）。</summary>
    Egoism = 0,
    /// <summary>利他：先想着别人。</summary>
    Altruism = 1,
    /// <summary>守序：认规则、契约与原则。</summary>
    Order = 2,
    /// <summary>重情：以关系与体谅为先。</summary>
    Care = 3,
    /// <summary>务实：看结果，不好高骛远。</summary>
    Pragmatic = 4
}

/// <summary>人生观：觉得什么值得、怎么看待日子。</summary>
public enum LifeStance
{
    /// <summary>享乐：及时行乐。</summary>
    Hedonism = 0,
    /// <summary>克制：节制、禁欲、修行。</summary>
    Asceticism = 1,
    /// <summary>责任：守护与担当。</summary>
    Duty = 2,
    /// <summary>虚无：反正没什么意义。</summary>
    Nihilism = 3,
    /// <summary>成长：要变强、要往前走。</summary>
    Growth = 4
}

/// <summary>思维主义：靠什么想事。</summary>
public enum ThinkingStyle
{
    /// <summary>理性：讲逻辑、算得失。</summary>
    Rational = 0,
    /// <summary>直觉：凭感觉走。</summary>
    Intuitive = 1,
    /// <summary>共情：先体察别人的感受。</summary>
    Empathic = 2,
    /// <summary>偏执：盯住一件事不肯放。</summary>
    Obsessive = 3,
    /// <summary>犬儒：先怀疑，不轻信。</summary>
    Cynical = 4
}

/// <summary>
/// 一个角色的内驱状态：三种基本动力、七个驱动、火气与它此刻的出口、内啡肽、以及自律。
///
/// 全部是 0~1 的抽象量，不是临床指标。驱动的语义是「推力」，不是「罪名」：
/// 张力涨上来会让她想做点什么，做成之后释放多巴胺（干劲）与内啡肽（松快），
/// 被自律按下去则只留下一点闷。
///
/// 基本动力是力的来源（有多容易被推动），七个驱动是当下被推到哪儿（想做什么），
/// 自律是闸门：它不消灭驱动，只决定这一次给不给出口、给哪一个出口。
/// </summary>
public sealed class DriveProfile
{
    // ---- 三种基本动力：由角色 id 决定的气质基线 + 事件推移 ----

    /// <summary>攻击性：越高越容易把火往外放。</summary>
    public double Aggression { get; set; }
    /// <summary>性：连接与亲密的驱力，被满足时释放最多的安定。</summary>
    public double Sexuality { get; set; }
    /// <summary>自恋：被看见与被认可是燃料，受挫时掉得最快。</summary>
    public double Narcissism { get; set; }

    // ---- 七个驱动的当前压力 ----

    public double Greed { get; set; }
    public double Gluttony { get; set; }
    public double Lust { get; set; }
    public double Envy { get; set; }
    public double Pride { get; set; }
    public double Sloth { get; set; }
    public double Wrath { get; set; }

    // ---- 火气与发泄 ----

    /// <summary>火气积压 0~1。涨到阈值就得找出口，不给出口会转成郁闷或退缩。</summary>
    public double Anger { get; set; }

    /// <summary>这一次火气选了哪种出口；null = 还没发作。</summary>
    public DischargeForm? Discharge { get; set; }

    /// <summary>这次发作的强度 0~1（决定语气有多重、要多久才收）。</summary>
    public double DischargeCharge { get; set; }

    public DateTime DischargedAt { get; set; } = DateTime.MinValue;

    /// <summary>还在怄气/冷着：这段时间里火气靠时间稀释，不会立刻消。</summary>
    public DateTime SulkingUntil { get; set; } = DateTime.MinValue;

    // ---- 内啡肽：发泄完与忍下来之后的那口松快 ----

    /// <summary>内啡肽 0~1：疼痛与紧绷之后的镇痛，命中时话会变软、身体会松下来。</summary>
    public double Endorphin { get; set; }

    // ---- 自律 ----

    /// <summary>自律 0~1：闸门的力道。可被锻炼（成功克制涨、彻底放纵掉）。</summary>
    public double Discipline { get; set; }

    public MoralStance Moral { get; set; } = MoralStance.Care;
    public LifeStance Life { get; set; } = LifeStance.Growth;
    public ThinkingStyle Thinking { get; set; } = ThinkingStyle.Empathic;

    /// <summary>是否已经从角色的人设（道德观/人生观/思维主义）归类过一次。</summary>
    public bool OutlookClassified { get; set; }

    /// <summary>
    /// 关系安全度 0~1：被好好对待就升高，被顶撞就掉下来。
    /// 它不决定有没有火气，只决定这团火气敢不敢往外放——安全的人面前才撒泼，不安全时只会退缩。
    /// </summary>
    public double RelationSafety { get; set; } = 0.55;

    /// <summary>最近一次按时间结算的时刻。</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>派生：此刻最想干什么的那股劲（0~1），只用于提示词与面板，不落盘。</summary>
    public DriveImpulse? Dominant
    {
        get
        {
            var pairs = new (DriveImpulse Kind, double Value)[]
            {
                (DriveImpulse.Greed, Greed), (DriveImpulse.Gluttony, Gluttony), (DriveImpulse.Lust, Lust),
                (DriveImpulse.Envy, Envy), (DriveImpulse.Pride, Pride), (DriveImpulse.Sloth, Sloth),
                (DriveImpulse.Wrath, Anger)
            };
            var top = pairs.OrderByDescending(p => p.Value).First();
            return top.Value >= 0.42 ? top.Kind : null;
        }
    }
}

/// <summary>
/// 给监测面板与工具读的一份内驱快照：三种基本动力、七个驱动、火气与当前出口、自律与三种观念。
/// 面板只读它，不写它；写入口在 <see cref="Services.InnerDriveService"/>。
/// </summary>
public sealed record DriveReadout(
    double Aggression, double Sexuality, double Narcissism,
    double Greed, double Gluttony, double Lust, double Envy, double Pride, double Sloth, double Wrath,
    double Anger, string DischargeName, string DischargeBehavior, double Endorphin, double Discipline,
    string MoralName, string LifeName, string ThinkingName, double RelationSafety, DateTime UpdatedAt);

/// <summary>一份存档里的内驱状态：按角色 id 分开存，跟着存档走。</summary>
public sealed class DriveBook
{
    public Dictionary<string, DriveProfile> ByCharacter { get; set; } = new(StringComparer.Ordinal);
}
