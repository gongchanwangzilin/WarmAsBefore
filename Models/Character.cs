namespace WarmAsBefore.Models;

public sealed record CharacterProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; init; } = string.Empty;
    /// <summary>性别：必填，没有默认值。采集走 Helpers.GenderPrompt（女/男/其他/自定义），
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
    public string Mood { get; set; } = "normal";
    public string CurrentOutfit { get; set; } = "default";
    public string CurrentEmotion { get; set; } = "normal";
    public bool IsFacingLeft { get; set; }
    public string Location { get; set; } = "home";
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public PhysiologicalData Cycle { get; set; } = new();
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