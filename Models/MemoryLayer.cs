namespace WarmAsBefore.Models;

/// <summary>
/// 记忆系统的五层结构。分层不是为了好看，而是每层的**存法、寿命与取法都不同**：
/// 混在一起就会出现「昨天说了什么想不起来，三个月前一句话却记得死死的」这种不像人的表现。
/// </summary>
public enum MemoryLayer
{
    /// <summary>工作记忆：本轮对话的上下文，用完即弃（对应 MemoryTurns / 会话历史）。</summary>
    Working = 0,

    /// <summary>人类记忆：事件、抽象印象、重要时刻、单张照片；按重要性与时间衰减，像人一样会忘。</summary>
    Human = 1,

    /// <summary>词义关系网：词与词的联系，负责「由一个词想起另一件事」的联想式回忆。</summary>
    Semantic = 2,

    /// <summary>长时记忆库：全量对话与好感记录，全部留着，供检索工具按需翻查。</summary>
    Vault = 3,

    /// <summary>档案层：日记与回忆录，是给人看的整理稿，也是角色事后回看的材料。</summary>
    Archive = 4
}

/// <summary>分层的名字与说明：界面与提示词都从这里取，避免各处写死中文。</summary>
public static class MemoryLayers
{
    public static IReadOnlyList<MemoryLayer> All { get; } = new[]
    {
        MemoryLayer.Working, MemoryLayer.Human, MemoryLayer.Semantic, MemoryLayer.Vault, MemoryLayer.Archive
    };

    public static string Name(MemoryLayer layer) => layer switch
    {
        MemoryLayer.Working => "工作记忆",
        MemoryLayer.Human => "人类记忆",
        MemoryLayer.Semantic => "词义关系网",
        MemoryLayer.Vault => "长时记忆库",
        MemoryLayer.Archive => "档案层",
        _ => "未知层"
    };

    /// <summary>一句话说明「这层怎么用、会怎么忘」。</summary>
    public static string Describe(MemoryLayer layer) => layer switch
    {
        MemoryLayer.Working => "本轮对话的上下文，长度由「记忆条数」决定，超出即丢弃",
        MemoryLayer.Human => "只记事件、印象、重要时刻与单张照片；重要的忘得慢，琐碎的自然淡忘",
        MemoryLayer.Semantic => "词与词的联系，负责由一个词想起另一件事",
        MemoryLayer.Vault => "所有对话与好感记录都在，只增不删，靠检索工具按需翻",
        MemoryLayer.Archive => "日记与回忆录，是整理给人看的稿子",
        _ => ""
    };
}
