namespace WarmAsBefore.Models;

public sealed record CharacterProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; init; } = string.Empty;
    public string Gender { get; init; } = "女";
    public string Nickname { get; init; } = string.Empty;
    public string Personality { get; init; } = string.Empty;
    public string Greeting { get; init; } = string.Empty;
    public string UserAddress { get; init; } = "主人";
    public string Description { get; init; } = string.Empty;
    public bool IsRomanceTarget { get; init; } = true;
    public string DefaultOutfit { get; init; } = "default";
}

public sealed record CharacterState
{
    public int Affection { get; set; }
    public int Trust { get; set; }
    public int Energy { get; set; } = 100;
    public string Mood { get; set; } = "normal";
    public string CurrentOutfit { get; set; } = "default";
    public string CurrentEmotion { get; set; } = "normal";
    public bool IsFacingLeft { get; set; }
    public string Location { get; set; } = "home";
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public PhysiologicalData Cycle { get; set; } = new();
}

public sealed record PhysiologicalData
{
    public bool Tracked { get; init; }
    public int CycleDay { get; set; }
    public string Phase { get; set; } = "normal";
    public DateTime LastPeriod { get; set; }
}

public sealed record CharacterData
{
    public CharacterProfile Profile { get; init; } = new();
    public CharacterState State { get; set; } = new();
    public Dictionary<string, string> SpriteMap { get; init; } = new();

    /// <summary>头像图片（相对存储根目录的路径，如 assets/characters/{id}/avatar.png），空表示无头像。</summary>
    public string Avatar { get; init; } = "";
}