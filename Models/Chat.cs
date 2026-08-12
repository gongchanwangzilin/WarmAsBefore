namespace WarmAsBefore.Models;

public sealed record ChatMessage
{
    public string Role { get; init; } = "user";
    public string Content { get; init; } = string.Empty;
    public DateTime Stamp { get; init; } = DateTime.UtcNow;
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