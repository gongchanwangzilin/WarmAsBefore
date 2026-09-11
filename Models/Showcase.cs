namespace WarmAsBefore.Models;

/// <summary>
/// 开发者展示案（Showcase Script）：一段可完整回放的 AI×用户对话场景。
/// 由一系列步骤组成，播放时自动依次执行，模拟实时对话。
/// </summary>
public sealed record ShowcaseScript
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..10];
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public string CharacterName { get; set; } = "";
    /// <summary>可选背景图（相对存储根目录的路径，可来自导入的临时素材）。</summary>
    public string BackgroundPath { get; set; } = "";
    public List<ShowcaseStep> Steps { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 展示案的一步。Type：dialogue=对话，cg=插入CG，sprites=立绘（支持多立绘同时在场）。
/// dialogue 使用 Speaker+Text；cg 使用 CgPath；sprites 使用 Sprites 列表。
/// </summary>
public sealed record ShowcaseStep
{
    public string Type { get; set; } = "dialogue";
    /// <summary>对话发言人：character=AI 角色，user=用户。</summary>
    public string Speaker { get; set; } = "character";
    public string Text { get; set; } = "";
    /// <summary>CG 图片相对存储根目录路径。</summary>
    public string CgPath { get; set; } = "";
    /// <summary>该时刻在场立绘（多立绘支持）。</summary>
    public List<ShowcaseSprite> Sprites { get; set; } = new();
}

/// <summary>在场立绘：路径 + 位置槽 + 透明度。</summary>
public sealed record ShowcaseSprite
{
    /// <summary>立绘图片相对存储根目录路径（通常来自角色 SpriteMap）。</summary>
    public string Path { get; set; } = "";
    /// <summary>水平位置：left / center / right。</summary>
    public string Position { get; set; } = "center";
    public double Opacity { get; set; } = 1.0;
}