namespace WarmAsBefore.Models;

/// <summary>
/// 场景库：独立于地图的结构化「场景 + 模式」定义。
/// 地图的单场景可以直接作为库条目的引用（MapSceneRef）；
/// 每个库条目可挂多个模式（SceneMode），模式描述一套视觉/音频/光照状态，
/// 以及可选的「变换规则」（AiRules，注入 AI 语境的文本，如“雨天→室内模式”）。
/// 场景可由时间（TimeRanges）或 AI 指令（【场景:名/模式】标记）触发切换。
/// </summary>

public sealed class SceneLibraryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>库内命名（用户可改）。</summary>
    public string Name { get; set; } = "";
    /// <summary>引用地图单场景 ID；为空表示纯库场景（直接给背景/色）。</summary>
    public string? MapSceneRef { get; set; }
    /// <summary>直接背景（库场景无地图引用时用）。</summary>
    public string Background { get; set; } = "";
    public string BackgroundColor { get; set; } = "#2C1810";
    /// <summary>附加备注（显示在库卡片）。</summary>
    public string Note { get; set; } = "";
    public List<SceneMode> Modes { get; set; } = new();
    /// <summary>变换规则文本：注入 AI 语境，让 AI 知道何时主动提议切换（如“下雨时切到室内模式”）。</summary>
    public string AiRules { get; set; } = "";
}

/// <summary>
/// 场景模式：一套可被时间或 AI 指令触发的视觉/音频状态。
/// </summary>
public sealed class SceneMode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>模式命名（如「夜灯」「雨天」「派对」）。</summary>
    public string Name { get; set; } = "";
    /// <summary>背景覆盖（空则沿用库条目的 Background）。</summary>
    public string Background { get; set; } = "";
    /// <summary>背景色覆盖（空则沿用）。</summary>
    public string BackgroundColor { get; set; } = "";
    /// <summary>灯光基调（AI 指令如“关灯”用：dim/normal/bright 仅作语义标签，实际映射到背景亮度）。</summary>
    public string Lighting { get; set; } = "normal";
    /// <summary>BGM 文件名（来自素材库；空则不改音乐）。</summary>
    public string? Bgm { get; set; }
    /// <summary>
    /// 时间触发区间（可空）：如 ["22:00-06:00"] 表示夜间自动启用。
    /// 支持跨午夜区间；多条区间用 List。空 = 不由时间触发。
    /// </summary>
    public List<string> TimeRanges { get; set; } = new();
}
