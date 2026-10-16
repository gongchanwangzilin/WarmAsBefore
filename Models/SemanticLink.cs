namespace WarmAsBefore.Models;

/// <summary>
/// 词与词之间的一条关联：由共现关系离线累积而来，不消耗模型调用。
/// 权重越高，两个词在角色脑子里联系越紧（想到 A 就容易想起 B）。
/// </summary>
public sealed record SemanticLink
{
    public string A { get; init; } = "";
    public string B { get; init; } = "";
    public double Weight { get; init; }
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>来源标记：auto = 对话共现自动累积；human / ai = 人工或 AI 纠正过。</summary>
    public string Source { get; init; } = "auto";
}
