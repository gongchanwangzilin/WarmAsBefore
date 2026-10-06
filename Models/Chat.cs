namespace WarmAsBefore.Models;

public sealed record ChatMessage
{
    public string Role { get; init; } = "user";
    public string Content { get; init; } = string.Empty;
    public DateTime Stamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 模型发起的工具调用（只有 assistant 消息可能带）。为 null 时这条消息与旧格式完全一致，
    /// 因此现有的 <c>with</c> 用法（ReplaceLastAssistant 只改 Content）不需要感知新字段。
    /// </summary>
    public List<ToolCall>? ToolCalls { get; init; }

    /// <summary>role="tool" 的结果消息必须回指它应答的那次调用（OpenAI 契约要求成对出现）。</summary>
    public string? ToolCallId { get; init; }
}

/// <summary>一次模型工具调用：OpenAI function calling 里 tool_call 的进程内投影。</summary>
public sealed record ToolCall
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;

    /// <summary>模型给的原始参数 JSON 文本（可能是空串或非法 JSON，执行侧需容错）。</summary>
    public string Arguments { get; init; } = string.Empty;
}

public sealed record ChatSession
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string CharacterId { get; init; } = string.Empty;
    public List<ChatMessage> Messages { get; set; } = new();
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public const int KeepTurns = 10;
}

public sealed record AiEndpoint
{
    public string Provider { get; init; } = "openai";
    public string Key { get; init; } = string.Empty;
    public string Url { get; init; } = "https://api.openai.com/v1/chat/completions";
    public string Model { get; init; } = "gpt-4o";
    public double Temperature { get; init; } = 0.8;
    public int MaxTokens { get; init; } = 500;
    public bool DeepThink { get; init; }
    public string? DeepModel { get; init; }
    public int MemoryTurns { get; init; } = 5;
}