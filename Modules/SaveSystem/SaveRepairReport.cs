namespace WarmAsBefore.Modules.SaveSystem;

/// <summary>
/// 一次读档的结构化修复结果（2026-11-14，容错自愈）。
/// 取代原来「一句 LastLoadNote 字符串 + bool」的表达：用户要知道「上次出了什么错 /
/// 可能丢了什么 / 修好了什么 / 现在什么状态 / 能做什么」，这几项必须分开说清。
///
/// 两类结局（用户诉求「容错率要高」的落地口径）：
///  · 可修复（字段缺失 / 类型不符 / version 落后 / 角色引用失效）→ <see cref="CanEnter"/>=true，
///    修复后自动进入（走既有 GoToAsync("main")），报告以回执条随目标页保留；
///  · 不可修复（整体 JSON 语法损坏 / 空文件）→ <see cref="CanEnter"/>=false，
///    备份 + 隔离 + 报告「无法修复」，不自动进入、不捏造空档覆盖原件。
/// </summary>
public sealed class SaveRepairReport
{
    /// <summary>发生时间（本地）。</summary>
    public DateTime IncidentTime { get; init; } = DateTime.Now;

    /// <summary>存档 id。</summary>
    public string SaveId { get; init; } = "";

    /// <summary>事件摘要：一句话说清「发生了什么」（标题 ≤8 汉字由 UI 侧另给）。</summary>
    public string IncidentSummary { get; init; } = "";

    /// <summary>
    /// 可能丢失的内容（**推断**，措辞必须含「可能」，≤5 行）。
    /// 层名用 <c>Models/MemoryLayer.cs</c> 的 <c>MemoryLayers.Name()</c>；
    /// 聊天表述为「最近 60 条对话」，不得写「全部聊天记录」。
    /// </summary>
    public List<string> LostItems { get; init; } = new();

    /// <summary>已自动修复的内容（**实证**，逐条列出）。</summary>
    public List<string> FixedItems { get; init; } = new();

    /// <summary>坏档原件备份路径（.corrupt-时间戳.json）；null = 未产生备份。</summary>
    public string? BackupPath { get; init; }

    /// <summary>能否进入游戏。false 时 UI 必须停在存档页，给 重试 / 返回列表 / 查看日志。</summary>
    public bool CanEnter { get; init; } = true;

    /// <summary>
    /// 是否已安全回写。**硬性规则**：任何段解析失败时禁止整档 Commit（防二次损坏），
    /// 此时为 false，报告注明「未回写，原件保留」；仅全段解析成功才回写。
    /// </summary>
    public bool Committed { get; init; }

    /// <summary>失败/中断的阶段号 0..5（0 = 未定）。与 SaveViewModel 的阶段轨同一套编号。</summary>
    public int FailedPhase { get; init; }

    /// <summary>
    /// 错误类别：none / syntax（整体 JSON 语法损坏）/ empty（空文件或读不出）/
    /// section（单段解析失败，已降级默认值）/ stale（结构版本落后）/ refs（角色引用失效）。
    /// </summary>
    public string ErrorKind { get; init; } = "none";

    /// <summary>失败的段名（段解析失败时填 "state" / "char_state" / "char_states" / "sub_profiles" / "chat"；null = 无段失败）。</summary>
    public string? FailedSegment { get; init; }

    /// <summary>回写安全说明（如「未回写，原件保留」）；空 = 正常回写或无需回写。</summary>
    public string WriteBackNote { get; init; } = "";

    /// <summary>状态徽章三态：已修复 / 部分修复 / 无法修复。</summary>
    public string BadgeText =>
        !CanEnter ? "无法修复"
        : (LostItems.Count > 0 ? "部分修复" : "已修复");

    /// <summary>徽章底色资源键（UI 用 DynamicResource 取；零新增颜色 Token）。</summary>
    public string BadgeTintKey => CanEnter ? "InfoTint" : "DangerTint";

    /// <summary>是否需要在目标页显示回执条（可进入且真的修过/丢过东西）。</summary>
    public bool NeedsReceipt => CanEnter && (FixedItems.Count > 0 || LostItems.Count > 0 || !string.IsNullOrEmpty(IncidentSummary));

    /// <summary>
    /// 高损需确认：可进入且是核心状态段（<c>state</c>）整段重建——影响大，先停在存档页让用户确认，
    /// 不静默自动进入。其余段失败（角色状态 / 副设定 / 聊天）一律自动进入 + 回执。
    /// </summary>
    public bool NeedsConfirm =>
        CanEnter && ErrorKind == "section"
        && (FailedSegment == "state" || LostItems.Any(x => x.Contains("游戏状态 state")));

    /// <summary>回执条摘要一行。</summary>
    public string ReceiptSummary =>
        BadgeText.Length == 0 ? ""
        : BadgeText + (FixedItems.Count > 0 ? "：" + FixedItems[0] : "")
        + (LostItems.Count > 0 ? "；可能影响：" + LostItems[0] : "");
}
