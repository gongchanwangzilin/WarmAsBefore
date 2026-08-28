namespace WarmAsBefore.Models;

/// <summary>
/// 灵枢 AI 导出的角色记忆数据格式
/// </summary>
public sealed record LingshuMemoryImport
{
    public string Role { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public DateTime? Timestamp { get; init; }
    public string Type { get; init; } = "memory"; // memory, dialogue, note
}

/// <summary>
/// 完整导入数据结构
/// </summary>
public sealed record LingshuArchive
{
    public string ArchiveType { get; init; } = string.Empty;
    public List<LingshuMemoryImport> Memories { get; init; } = new();
    public string? CharacterName { get; init; }
    public string? CharacterDescription { get; init; }
    public DateTime? ExportedAt { get; init; }
}