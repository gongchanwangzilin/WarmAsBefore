using System.Text.Json.Serialization;

namespace WarmAsBefore.Models;

/// <summary>
/// 灵枢 AI 导出的角色记忆 JSON 格式
/// 支持导入角色设定、记忆片段、对话历史等
/// </summary>
public sealed class LingshuCharacterImport
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = "女";

    [JsonPropertyName("personality")]
    public string Personality { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("greeting")]
    public string Greeting { get; set; } = "";

    [JsonPropertyName("user_address")]
    public string UserAddress { get; set; } = "主人";

    [JsonPropertyName("memories")]
    public List<LingshuMemory> Memories { get; set; } = new();

    [JsonPropertyName("dialogues")]
    public List<LingshuDialogue> Dialogues { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();
}

public sealed class LingshuMemory
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("importance")]
    public int Importance { get; set; } = 5; // 1-10，越高越重要

    [JsonPropertyName("type")]
    public string Type { get; set; } = "general"; // general/important/emotional
}

public sealed class LingshuDialogue
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
