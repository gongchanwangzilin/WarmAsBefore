namespace WarmAsBefore.Models;

public sealed record MemoryEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string CharacterId { get; init; } = string.Empty;
    public DateTime At { get; init; } = DateTime.UtcNow;
    public string Content { get; init; } = string.Empty;
    public string Category { get; init; } = "dialogue";
    public string? Keywords { get; init; }
    public int Weight { get; init; } = 1;
    /// <summary>好感提升瞬间截屏保存的图片路径（回忆录显示）。</summary>
    public string? ImagePath { get; init; }
}

public sealed record DiaryNote
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string CharacterId { get; init; } = string.Empty;
    public DateTime Date { get; init; } = DateTime.UtcNow;
    public string Content { get; init; } = string.Empty;
    public string Mood { get; init; } = "normal";
}

public sealed record CgRecord
{
    public string Id { get; init; } = string.Empty;
    public string CharacterId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string File { get; init; } = string.Empty;
    public string Condition { get; init; } = string.Empty;
    public string Dialogue { get; init; } = string.Empty;
    public bool Unlocked { get; set; }
}

public sealed record AchievementDef
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Desc { get; init; } = string.Empty;
    public bool Earned { get; set; }
}

public sealed record PackInfo
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0";
    public string? Author { get; init; }
    public string? Description { get; init; }
    public string[] Characters { get; init; } = Array.Empty<string>();
}

public sealed record WeatherReading
{
    public string Condition { get; init; } = "clear";
    public string Description { get; init; } = string.Empty;
    public double Celsius { get; init; }
    public int Humidity { get; init; }
    public string City { get; init; } = string.Empty;
}

public sealed record TimeOfDayInfo
{
    public DateTime Now { get; init; } = DateTime.Now;
    public string Period => Now.Hour switch
    {
        < 6 => "night",
        < 12 => "morning",
        < 14 => "noon",
        < 18 => "afternoon",
        < 21 => "evening",
        _ => "night"
    };
    public string Season => Now.Month switch
    {
        >= 3 and <= 5 => "spring",
        >= 6 and <= 8 => "summer",
        >= 9 and <= 11 => "autumn",
        _ => "winter"
    };
    public string? Holiday { get; init; }
}

public sealed record McpToolDef
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = "utility";
    public bool Active { get; set; } = true;
    /// <summary>所属 MCP 数据包（McpPacks 下的目录名）；内置工具为 system。</summary>
    public string Server { get; init; } = "";
    /// <summary>执行方式：builtin（内置实现）/ js（沙箱脚本）/ http（远程端点）。</summary>
    public string Kind { get; init; } = "builtin";
    /// <summary>js 模式的源码。</summary>
    public string Code { get; init; } = "";
    /// <summary>http 模式的端点地址。</summary>
    public string Url { get; init; } = "";
    public string Method { get; init; } = "POST";
    public Dictionary<string, string> Headers { get; init; } = new();
    /// <summary>参数 JSON Schema（字符串），给模型看它该传什么。</summary>
    public string ParametersSchema { get; init; } = "";
}

public sealed record ScenarioResult
{
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public List<string> Characters { get; init; } = new();
    public List<PlotBranch> Branches { get; init; } = new();

    /// <summary>
    /// 关键事件时间线（按叙事顺序）：导入后用于「这个世界发生过什么」的上下文，
    /// 以及后续剧情推进时的锚点。空列表表示模型未给出或文本过短无法抽取。
    /// </summary>
    public List<string> KeyEvents { get; init; } = new();

    /// <summary>
    /// 剧情完整性检查结果（本地启发式 + 模型判定合并）。为 null 表示未做检查
    /// （例如旧版本导入的存档，或文本为空）。
    /// </summary>
    public PlotCompleteness? Completeness { get; init; }
}

public sealed record PlotBranch
{
    public string Description { get; init; } = string.Empty;
    public string[] Options { get; init; } = Array.Empty<string>();
    public string? ConditionFlag { get; init; }
}

/// <summary>
/// 剧情完整性检查结果：小说转 Gala 世界时，判断「导入的文本是否讲得完整」。
/// 目标不是打分为难用户，而是**明确告诉用户缺了什么**，避免导入一个半截故事后
/// AI 在平行宇宙里编不出走向。
/// </summary>
public sealed record PlotCompleteness
{
    /// <summary>0-100 的完整度评分（本地启发式为主，模型判定可上下浮动）。</summary>
    public int Score { get; init; }

    /// <summary>是否判定为「可进入」：Score 达到阈值且无致命缺失。</summary>
    public bool CanEnter { get; init; }

    /// <summary>一句话结论（展示给用户）。</summary>
    public string Verdict { get; init; } = string.Empty;

    /// <summary>发现的问题列表（缺开头/缺结尾/角色不一致/分支不可达等）。</summary>
    public List<string> Issues { get; init; } = new();

    /// <summary>做检查时的原文长度（字符数），用于解释「文本太短」类结论。</summary>
    public int SourceLength { get; init; }

    /// <summary>分析原文被切成了几段（长文分段分析的证据）。</summary>
    public int SegmentCount { get; init; }
}