namespace WarmAsBefore.Models;

/// <summary>
/// 标签：AI 可以给「人」「一段对话」「一件事」打标签用于分类，之后能按标签或标签组合检索。
/// 标签自己也能被回想（问「你都怎么记我的」时，翻的就是这张表）。
/// </summary>
public sealed record MemoryTag
{
    public string Name { get; init; } = "";

    /// <summary>标签指向的对象类型：person 人 / talk 对话 / event 事件 / place 地点 / thing 物。</summary>
    public string Subject { get; init; } = "event";

    /// <summary>被打过几次（越多说明这个标签越重要，注入时优先带它）。</summary>
    public int Count { get; init; }

    public DateTime FirstUsedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastUsedAt { get; init; } = DateTime.UtcNow;
}
