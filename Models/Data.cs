namespace WarmAsBefore.Models;

public sealed record MemoryEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string CharacterId { get; init; } = string.Empty;
    public DateTime At { get; init; } = DateTime.UtcNow;
    public string Content { get; init; } = string.Empty;
    public string Category { get; init; } = "dialogue";
    public string? Keywords { get; init; }
    public int Weight { get; init; } = 1;
    /// <summary>好感提升瞬间截屏保存的图片路径（回忆录显示）。</summary>
    public string? ImagePath { get; init; }
}

public sealed record DiaryNote
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string CharacterId { get; init; } = string.Empty;
    public DateTime Date { get; init; } = DateTime.UtcNow;
    public string Content { get; init; } = string.Empty;
    public string Mood { get; init; } = "normal";
}

public sealed record CgRecord
{
    public string Id { get; init; } = string.Empty;
    public string CharacterId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string File { get; init; } = string.Empty;
    public string Condition { get; init; } = string.Empty;
    public string Dialogue { get; init; } = string.Empty;
    public bool Unlocked { get; set; }
}

public sealed record AchievementDef
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Desc { get; init; } = string.Empty;
    public bool Earned { get; set; }
}

public sealed record PackInfo
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0";
    public string? Author { get; init; }
    public string? Description { get; init; }
    public string[] Characters { get; init; } = Array.Empty<string>();
}

public sealed record WeatherReading
{
    public string Condition { get; init; } = "clear";
    public string Description { get; init; } = string.Empty;
    public double Celsius { get; init; }
    public int Humidity { get; init; }
    public string City { get; init; } = string.Empty;
}

public sealed record TimeOfDayInfo
{
    public DateTime Now { get; init; } = DateTime.Now;
    public string Period => Now.Hour switch
    {
        < 6 => "night",
        < 12 => "morning",
        < 14 => "noon",
        < 18 => "afternoon",
        < 21 => "evening",
        _ => "night"
    };
    public string Season => Now.Month switch
    {
        >= 3 and <= 5 => "spring",
        >= 6 and <= 8 => "summer",
        >= 9 and <= 11 => "autumn",
        _ => "winter"
    };
    public string? Holiday { get; init; }
}

public sealed record McpToolDef
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = "utility";
    public bool Active { get; set; } = true;
}

public sealed record ScenarioResult
{
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public List<string> Characters { get; init; } = new();
    public List<PlotBranch> Branches { get; init; } = new();
}

public sealed record PlotBranch
{
    public string Description { get; init; } = string.Empty;
    public string[] Options { get; init; } = Array.Empty<string>();
    public string? ConditionFlag { get; init; }
}