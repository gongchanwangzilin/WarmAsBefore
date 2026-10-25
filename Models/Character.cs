namespace WarmAsBefore.Models;

public sealed record CharacterProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; init; } = string.Empty;
    /// <summary>性别：必填，没有默认值。采集在角色工作室的「设定」栏（本机性别库里的候选 + 设置里的自定义），
    /// 它同时是周期规则的键 —— 猜一个出来会把周期算到错的人身上。</summary>
    public string Gender { get; init; } = string.Empty;
    public string Nickname { get; init; } = string.Empty;
    public string Personality { get; init; } = string.Empty;
    public string Greeting { get; init; } = string.Empty;
    public string UserAddress { get; init; } = "主人";
    public string Description { get; init; } = string.Empty;
    public bool IsRomanceTarget { get; init; } = true;
    public string DefaultOutfit { get; init; } = "default";
}

public sealed record CharacterState
{
    public int Affection { get; set; }

    /// <summary>累计好感积分（独立于 0-100 好感度）：每 500 分升 1 级，共 10 级（上限 5000）。</summary>
    public int AffectionPoints { get; set; }

    public int Trust { get; set; }
    public int Energy { get; set; } = 100;
    /// <summary>精力 AI 修正（有界）：叠加在时间基线之上。精力 = clamp(基线 + 修正, 0, 100)；
    /// 让 AI 对本次互动造成的额外消耗/恢复说话，而不停摆——基线仍随清醒时长自然下滑。</summary>
    public int EnergyOffset { get; set; }
    public string Mood { get; set; } = "normal";
    public string CurrentOutfit { get; set; } = "default";
    public string CurrentEmotion { get; set; } = "normal";
    public bool IsFacingLeft { get; set; }
    public string Location { get; set; } = "home";
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public PhysiologicalData Cycle { get; set; } = new();
    /// <summary>大脑预判（期待差 + 不可预测两层）：随角色一起落盘，换设备也带着 TA 的心理曲线。</summary>
    public BrainState Brain { get; set; } = new();
}

/// <summary>
/// 大脑预判的状态：TA 心里那条「你会怎么反应」的预期曲线、最近一轮预测误差带来的多巴胺，
/// 以及（可选免费模型）下一次要用的预期与惊喜卡。全是有界小字段，默认值即老存档的兼容值。
/// </summary>
public sealed record BrainState
{
    /// <summary>TA 预计「你」这次会有多热（0..1）。按每轮实际温度做误差学习，也会随好感阶段均值回归。</summary>
    public double ExpectedWarmth { get; set; } = 0.5;

    /// <summary>最近一轮预测误差带来的多巴胺水平（0..100，50 为平静）：偏正=被击中，偏负=有点失落。</summary>
    public double Dopamine { get; set; } = 50;

    /// <summary>已结算的轮数：免费模型预判按它节流（每 N 轮才问一次）。</summary>
    public int Turns { get; set; }

    /// <summary>聊天好感结算的小数累积（0..1）：把每轮 0.2~1.8 的倍率攒成整数增量，均摊到跨轮。</summary>
    public double Carry { get; set; }

    /// <summary>免费模型预判出的「下一轮你会多热」（0..1）；-1 = 暂无，用本地期望。</summary>
    public double HintWarmth { get; set; } = -1;

    /// <summary>免费模型给的「意料之外」小提示（≤24 字）；空 = 用本地惊喜卡。</summary>
    public string Hint { get; set; } = "";

    /// <summary>Hint 对应的轮数：过了区间就失效，退回本地，避免拿旧预判套新情境。</summary>
    public int HintTurn { get; set; }

    /// <summary>最近用过的一张惊喜卡（避免连抽同一张）。</summary>
    public string LastCard { get; set; } = "";
}

public sealed record PhysiologicalData
{
    public bool Tracked { get; init; }
    public int CycleDay { get; set; }
    public string Phase { get; set; } = "normal";
    public DateTime LastPeriod { get; set; }
}

public sealed record CharacterData
{
    /// <summary>
    /// 主设定（内核）：角色库/导入包唯一能规定的部分，运行时绝不改写。
    /// </summary>
    public CharacterProfile Profile { get; init; } = new();

    public CharacterState State { get; set; } = new();
    public Dictionary<string, string> SpriteMap { get; init; } = new();

    /// <summary>头像图片（相对存储根目录的路径，如 assets/characters/{id}/avatar.png），空表示无头像。</summary>
    public string Avatar { get; init; } = "";

    /// <summary>该角色的参数覆写（记忆轮数/上下文上限/温度/记忆窗口等）；空字段沿用全局设置。</summary>
    public CharacterOverrides Overrides { get; init; } = new();

    /// <summary>
    /// 副设定（当下状态）：新角色时是主设定的副本，此后随时间与经历漂移，只有它会变。
    /// </summary>
    public CharacterSubProfile SubProfile { get; init; } = new();
}

/// <summary>
/// 副设定：随时间与经历漂移的「当下之我」。主设定是内核（不改），副设定是此刻的样子。
/// 漂移不调用模型：由好感/信任/心情/精力等既有状态按规则推导，另有少量近期心事条目
/// （由记忆折叠与重要事件写入），并按有界历史上限留影，便于查看差异与一键回正。
/// </summary>
public sealed record CharacterSubProfile
{
    /// <summary>仍在延续的主设定性格表述（播种时抄自主设定，供提示词拼装与差异对照）。</summary>
    public string Personality { get; init; } = "";
    public string Description { get; init; } = "";
    public string Nickname { get; init; } = "";
    public string UserAddress { get; init; } = "";

    /// <summary>当下心境基调（由好感/信任/心情/精力推导）。</summary>
    public string Tone { get; init; } = "";

    /// <summary>近期心事：短句，来自记忆折叠与重要事件（有界）。</summary>
    public List<string> RecentNotes { get; init; } = new();

    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
    public string LastDriftReason { get; init; } = "";
    public int DriftCount { get; init; }

    /// <summary>漂移留影（有界）：用于「与主设定的差异」查看与回正。</summary>
    public List<SubProfileSnapshot> History { get; init; } = new();

    /// <summary>是否已按主设定播种（旧存档里的角色一开始没有副设定）。</summary>
    public bool IsSeeded => !string.IsNullOrEmpty(Personality) || !string.IsNullOrEmpty(Tone);
}

/// <summary>副设定的一次漂移留影：只存对照用的文本，避免与主记录互相嵌套。</summary>
public sealed record SubProfileSnapshot
{
    public DateTime At { get; init; } = DateTime.UtcNow;
    public string Reason { get; init; } = "";
    public string Tone { get; init; } = "";
    public List<string> Notes { get; init; } = new();
}