namespace WarmAsBefore.Models;

/// <summary>
/// 一局新档的「过去」：从导入的聊天记录里算出来的初始关系，随存档落盘。
///
/// 它不是聊天正文（正文一条都不进档，全部折成记忆条目），只留一份能说清
/// 「你们之前聊过多少、谁先开口、最后聊到哪儿」的摘要 —— 设置页与存档页要能回答用户
/// 「这一局为什么一上来就这么熟」，而不是只给一个凭空的数值。
/// </summary>
public sealed record ChatHistorySeed
{
    /// <summary>导入并归一化后留下的消息数。</summary>
    public int Messages { get; init; }
    /// <summary>归一化时丢掉的重行/空行数量。</summary>
    public int Skipped { get; init; }
    /// <summary>归属到「你」与归属到 TA 的条数；其余发言计在 <see cref="OtherMessages"/>。</summary>
    public int UserMessages { get; init; }
    public int CharacterMessages { get; init; }
    public int OtherMessages { get; init; }

    /// <summary>由「你」开启的对话占比 0~1（隔 30 分钟算新的一段）。</summary>
    public double UserInitiatedRatio { get; init; }

    /// <summary>这段记录覆盖的天数：一天内的记录算 1。</summary>
    public int SpanDays { get; init; }
    public DateTime? FirstAt { get; init; }
    public DateTime? LastAt { get; init; }

    /// <summary>
    /// 写入这一档的初始好感（绝对点数 0–1000，走 <see cref="Helpers.AffectionScale"/> 的唯一刻度）
    /// 与信任（0–100）。
    /// </summary>
    public int Affection { get; init; }
    public int Trust { get; init; }

    /// <summary>最近一段聊天的摘要（人话，≤60 字）。</summary>
    public string RecentSummary { get; init; } = "";
    /// <summary>识别出的明显情绪事件（最多 8 条，原文摘句）。</summary>
    public List<string> Notables { get; init; } = new();

    /// <summary>来源说明：几个文件、识别成什么格式。</summary>
    public string Source { get; init; } = "";
    public DateTime ImportedAt { get; init; } = DateTime.UtcNow;
}
