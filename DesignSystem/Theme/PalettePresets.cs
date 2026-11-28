namespace WarmAsBefore.DesignSystem.Theme;

/// <summary>
/// 一套预设配色的**种子**：主色 + 状态色 + 底色档。
///
/// 这里一个十六进制都没有，因为预设不提供颜色，只提供方向 ——
/// 明度台阶、墨阶、强调与读数的最终值仍由 <see cref="Worlds.StyleWorldTokens"/> 现算：
/// 墨阶用 Lock() 按对比比锁死（正文 ≥ 4.5:1 是构造性保证，不是调出来的），
/// 强调与读数按卡面锁到 4.8~4.95，状态色过 3.2 兜底。
///
/// 字段沿用 <c>ReadSeed</c> 那套读法：C* 是**彩度差**（通道跨度 max−min，不是 HSL 的 S），
/// 近白/近黑的高明度色在 HSL 里 S 能很高而肉眼几乎看不出色偏，拿 S 当强度会把两套预设算成同一个颜色。
/// 唯一的例外是 <see cref="SignalS"/>：读数色本来就给在 L 0.34 / 0.66 两个中调上，
/// 那里 HSL 的 S 与「看得出多饱和」是一致的。
/// </summary>
public readonly record struct PaletteSeed(
    /// <summary>底色色相（°）。页面/卡面/井三档地的色相出处。</summary>
    double GroundH,
    /// <summary>底色彩度差：这套纸「染了多少色」。浅档用一半，深档全用（暗底上色偏读不出来）。</summary>
    double GroundC,
    /// <summary>主色色相（°）：主行动、当前项、图形笔画。</summary>
    double AccentH,
    /// <summary>主色彩度差。</summary>
    double AccentC,
    /// <summary>读数色色相（°）：只给状态、进度、HUD 读数，不做装饰。</summary>
    double SignalH,
    /// <summary>读数色饱和度（HSL S）：它钉在中调上，指示度与 S 同向。</summary>
    double SignalS,
    /// <summary>危险色色相（°）。</summary>
    double RedH,
    /// <summary>危险色彩度差。</summary>
    double RedC,
    /// <summary>成功色色相（°）。</summary>
    double GreenH,
    /// <summary>成功色彩度差。</summary>
    double GreenC,
    /// <summary>信息色色相（°）。</summary>
    double BlueH,
    /// <summary>信息色彩度差。</summary>
    double BlueC,
    /// <summary>
    /// 底色档：true = 暗档（深底），false = 纸档（浅底）。
    /// 这一档不由系统深浅决定 —— 它是这套配色的一部分：同一个「墨青」在纸档上就不是墨青了。
    /// </summary>
    bool Dark,
    /// <summary>
    /// 底色档的深浅微调：加在页面/井的明度上（卡面加一半）。负 = 更沉，正 = 更浅。
    /// 五套之间靠它拉开「纸白 → 最暗」的差；0 就是这一档的默认深度。
    /// </summary>
    double Level);

/// <summary>一套预设配色：id（落盘用）、中文短名、一句气质说明、种子。</summary>
public sealed record PalettePreset(string Id, string Name, string Note, PaletteSeed Seed);

/// <summary>
/// 五套预设配色的**唯一出处**。界面上的配色卡、<c>palette.swatches</c> 与
/// <see cref="Worlds.StyleWorldTokens"/> 算令牌时的种子，全从这里取。
///
/// 五套覆盖：暖赭（现有）、冷青灰、淡樱、苔绿、琥珀；底色档从纸白排到最暗。
/// 想再添一套，只在这张表里加一行 —— 其余颜色全是 Lock() 现算的。
/// </summary>
public static class PalettePresets
{
    /// <summary>出厂那一套（也就是本仓库一直以来的配色：暖赭印墨 + 铜绿读数）。</summary>
    public const string DefaultId = "ochre";

    /// <summary>顺序就是设置页里那排卡片的顺序：从浅到深。</summary>
    public static IReadOnlyList<PalettePreset> All { get; } = new PalettePreset[]
    {
        // 赭石（现有）：#F6F2EA 的暖白纸（色相 40°、彩度差 0.047）、印墨 #B86F3C（24.68°、0.486）、
        // 铜绿读数 168°、砖红 11.6°、青瓷 164.8°、钢青 201.9°。这组数与现有色板逐位一致，
        // 所以默认档不换色，只是把「从 XAML 读种子」改成「从预设读种子」。
        new("ochre", "赭石", "暖赭印在纸白上",
            new PaletteSeed(40, 0.047, 24.68, 0.486, 168, 0.32,
                11.6, 0.486, 164.8, 0.3098, 201.9, 0.204, Dark: true, Level: 0)),

        // 墨青：青灰主色 + 蓝黑底，砖红做读数。底比默认档再沉 1%，色相偏冷 174°。
        new("slate", "墨青", "青灰底压一点砖红",
            new PaletteSeed(214, 0.030, 197, 0.24, 12, 0.44,
                12, 0.42, 165, 0.22, 205, 0.20, Dark: true, Level: -0.010)),

        // 樱灰：淡樱主色 + 石墨中性，纸白底。底色的彩度差压到 0.012 —— 读成灰，不读成蓝。
        new("sakura", "樱灰", "淡樱挨着石墨",
            new PaletteSeed(240, 0.012, 344, 0.20, 230, 0.30,
                350, 0.30, 150, 0.22, 215, 0.20, Dark: false, Level: 0)),

        // 苔绿：苔绿主色 + 暖砂底。底色色相 38°、彩度差 0.070，纸档上是砂，不是奶油。
        new("moss", "苔绿", "苔绿长在暖砂上",
            new PaletteSeed(38, 0.070, 104, 0.22, 24, 0.42,
                18, 0.34, 120, 0.26, 200, 0.18, Dark: false, Level: -0.004)),

        // 夜琥珀：琥珀主色 + 最暗的暖黑底（页 0.083、卡 0.161），青瓷读数压在上面。
        new("amber", "夜珀", "最暗处一点琥珀",
            new PaletteSeed(30, 0.020, 40, 0.52, 188, 0.34,
                8, 0.44, 160, 0.26, 200, 0.18, Dark: true, Level: -0.030)),
    };

    /// <summary>
    /// 按落盘的 id 取预设。空串、老存档里没有这个键、手改出的错字，一律落回出厂那一套 ——
    /// 这条轴永远有兜底，不会因为一个错字把整屏涂成别的颜色。
    /// </summary>
    public static PalettePreset Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return All[0];
        var key = id.Trim();
        foreach (var preset in All)
            if (string.Equals(preset.Id, key, StringComparison.OrdinalIgnoreCase)) return preset;
        return All[0];
    }
}
