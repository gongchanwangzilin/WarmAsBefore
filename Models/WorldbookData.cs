using System.Text.Json;

namespace WarmAsBefore.Models;

public sealed class WorldbookEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string CoverImagePath { get; set; } = "";
    public bool HasSprite { get; set; }
    public List<WorldbookCharacter> Characters { get; init; } = new();
    public WorldbookMode Mode { get; set; } = WorldbookMode.TextOnly;
    public string UserDescription { get; set; } = "";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public bool AiPolished { get; set; }
}

public sealed class WorldbookCharacter
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string SpritePath { get; set; } = "";
    public bool HasSprite { get; set; }
    public string Gender { get; set; } = "女";
    public string Personality { get; set; } = "";
}

public enum WorldbookMode
{
    TextOnly,
    WithSprites
}

public sealed class WorldbookGenerationResult
{
    public WorldbookEntry Worldbook { get; set; } = new();
    public string CoverImagePath { get; set; } = "";
    public bool CoverGenerated { get; set; }
    public string AiReply { get; set; } = "";
    public List<string> Warnings { get; init; } = new();
}