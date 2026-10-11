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

    /// <summary>
    /// #10 携带的文件附件（假微信/对话里带上文件）。为 null 时与旧格式完全一致，
    /// 因此现有 <c>with</c> 用法不受影响。文本类附件的内容会拼进模型输入，二进制类只报元信息。
    /// </summary>
    public List<ChatAttachment>? Attachments { get; init; }
}

/// <summary>
/// 对话里携带的一个文件附件。文本内容（TextContent）会被注入模型上下文；
/// 图片/音频等二进制只带元信息（路径+类型+大小），由识图工具或用户自行处理。
/// </summary>
public sealed record ChatAttachment
{
    /// <summary>展示用文件名（含扩展名）。</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>绝对路径（用于后续工具读取/预览）。</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>MIME 或简单类型标签，如 text / image / audio / other。</summary>
    public string Kind { get; init; } = "other";

    /// <summary>字节数。</summary>
    public long Size { get; init; }

    /// <summary>文本类附件的正文（已按上限截断）；非文本为 null。</summary>
    public string? TextContent { get; init; }

    /// <summary>是否因超长被截断。</summary>
    public bool Truncated { get; init; }
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