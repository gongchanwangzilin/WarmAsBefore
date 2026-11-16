namespace WarmAsBefore.Models.Background;

/// <summary>
/// 统一背景系统的工作模式。<see cref="BackgroundConfig.Mode"/> 取其中之一。
/// </summary>
public enum BackgroundMode
{
    /// <summary>
    /// 纯色背景：颜色取自 <see cref="BackgroundConfig.SolidColorToken"/> 指向的资源字典 Color token。
    /// </summary>
    Solid,

    /// <summary>
    /// 图片背景：图片路径取自 <see cref="BackgroundConfig.ImagePath"/>。
    /// </summary>
    Image,

    /// <summary>
    /// 天气背景：按 <see cref="BackgroundConfig.Scene"/> 绘制的动态天气场景，并叠加调色板。
    /// </summary>
    Weather
}

/// <summary>
/// 天气场景的种类。决定太阳、云、雨、闪电等元素的默认组合。
/// </summary>
public enum WeatherSceneKind
{
    /// <summary>晴天：可见太阳，无云雨。</summary>
    Sunny,

    /// <summary>多云：云层为主，太阳若隐若现。</summary>
    Cloudy,

    /// <summary>毛毛雨：少量细密的雨丝。</summary>
    Drizzle,

    /// <summary>小雨：中等强度的降雨。</summary>
    LightRain,

    /// <summary>大雨：雨丝密集且长。</summary>
    HeavyRain,

    /// <summary>雷暴：强降雨并伴有闪电。</summary>
    Storm,

    /// <summary>雪：飘落的雪花。</summary>
    Snow,

    /// <summary>雾：低能见度的朦胧场景。</summary>
    Fog
}

/// <summary>
/// 语义化调色板：一组指向「资源字典 Color token」的键名，
/// 供背景系统在页面底色、表面、文字、按钮、面板、强调色等处统一取色。
/// </summary>
/// <remarks>
/// <para>
/// <b>核心契约（所有消费方必须遵守）：</b>
/// 这里每一个颜色字段都是资源字典里 <see cref="Microsoft.Maui.Controls.Color"/> 的 token key，
/// 是语义化的键名（例如 <c>PageBg</c>），绝不是十六进制颜色字符串。
/// </para>
/// <para>
/// 消费方必须写 <c>Application.Current.Resources[key] as Color</c> 取色；
/// 严禁在代码或数据文件里硬编码任何十六进制颜色值。
/// 这样一来，换肤只需替换资源字典，模型与配置文件保持中性、可复用。
/// </para>
/// </remarks>
public sealed record PaletteDefinition(
    /// <summary>调色板唯一 Id（例如 <c>classic</c>），被 <see cref="BackgroundConfig.PaletteId"/> 引用。</summary>
    string Id,

    /// <summary>面向用户的显示名称（例如「经典」）。</summary>
    string DisplayName,

    /// <summary>页面底色资源的 token key（非十六进制颜色值）。</summary>
    string PageBgKey,

    /// <summary>表面（卡片 / 容器）底色资源的 token key（非十六进制颜色值）。</summary>
    string SurfaceBgKey,

    /// <summary>表面前景色（表面上的内容色）资源的 token key（非十六进制颜色值）。</summary>
    string SurfaceFgKey,

    /// <summary>主文字色资源的 token key（非十六进制颜色值）。</summary>
    string TextKey,

    /// <summary>次要 / 弱化文字色资源的 token key（非十六进制颜色值）。</summary>
    string TextMutedKey,

    /// <summary>按钮底色资源的 token key（非十六进制颜色值）。</summary>
    string ButtonBgKey,

    /// <summary>按钮文字 / 前景色资源的 token key（非十六进制颜色值）。</summary>
    string ButtonFgKey,

    /// <summary>面板底色资源的 token key（非十六进制颜色值）。</summary>
    string PanelBgKey,

    /// <summary>面板描边色资源的 token key（非十六进制颜色值）。</summary>
    string PanelBorderKey,

    /// <summary>强调色资源的 token key（非十六进制颜色值）。</summary>
    string AccentKey,

    /// <summary>信号 / 状态提示色资源的 token key（非十六进制颜色值）。</summary>
    string SignalKey,

    /// <summary>遮罩（半透明压暗层）色资源的 token key（非十六进制颜色值）。</summary>
    string ScrimKey);

/// <summary>
/// 天气场景参数表：位置 / 比例 / 强度都是 0..1 归一化值或相对值；颜色字段同样是资源字典的 Color token key。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="PaletteDefinition"/> 一致：所有颜色字段（<see cref="SkyTopToken"/>、<see cref="SkyBottomToken"/>、
/// <see cref="CloudToken"/>、<see cref="RainToken"/>、<see cref="SunToken"/>）都是 token key，
/// 消费方必须写 <c>Application.Current.Resources[key] as Color</c> 取色，禁止硬编码任何十六进制颜色值。
/// </para>
/// </remarks>
public sealed record WeatherSceneDefinition(
    /// <summary>场景种类，决定该参数表用于哪种天气。</summary>
    WeatherSceneKind Kind,

    /// <summary>面向用户的显示名称（例如「小雨」）。</summary>
    string DisplayName,

    /// <summary>天气强度，归一化 0..1：0 最弱、1 最强。</summary>
    double Intensity,

    /// <summary>太阳的横向相对位置，归一化 0..1：0 最左、1 最右。</summary>
    double SunX,

    /// <summary>太阳的纵向相对位置，归一化 0..1：0 最上、1 最下。</summary>
    double SunY,

    /// <summary>太阳的相对尺寸（相对值，1 为基准大小）。</summary>
    double SunSize,

    /// <summary>太阳的不透明度，归一化 0..1：0 全透明、1 完全不透明。</summary>
    double SunAlpha,

    /// <summary>云朵数量（非负整数，0 表示无云）。</summary>
    int CloudCount,

    /// <summary>云朵的不透明度，归一化 0..1。</summary>
    double CloudAlpha,

    /// <summary>云朵的相对缩放（相对值，1 为基准大小）。</summary>
    double CloudScale,

    /// <summary>雨丝 / 雪片数量（非负整数，0 表示无降水）。</summary>
    int RainCount,

    /// <summary>雨丝的相对长度（相对值，数值越大雨丝越长）。</summary>
    double RainLength,

    /// <summary>雨丝的相对下落速度（相对值，数值越大越快）。</summary>
    double RainSpeed,

    /// <summary>雨丝的不透明度，归一化 0..1。</summary>
    double RainAlpha,

    /// <summary>天空渐变顶部颜色资源的 token key（非十六进制颜色值）。</summary>
    string SkyTopToken,

    /// <summary>天空渐变底部颜色资源的 token key（非十六进制颜色值）。</summary>
    string SkyBottomToken,

    /// <summary>云朵颜色资源的 token key（非十六进制颜色值）。</summary>
    string CloudToken,

    /// <summary>雨丝颜色资源的 token key（非十六进制颜色值）。</summary>
    string RainToken,

    /// <summary>太阳颜色资源的 token key（非十六进制颜色值）。</summary>
    string SunToken,

    /// <summary>是否绘制闪电（雷暴场景为真）。</summary>
    bool ShowLightning);

/// <summary>
/// 统一背景配置：整个背景系统唯一的配置载体，是被持久化的最小完整单元。
/// </summary>
/// <remarks>
/// <para>
/// 禁止把颜色拆散成视图模型里的单个颜色字段；一律通过本记录传递。
/// <see cref="SolidColorToken"/> 以及 <see cref="PaletteId"/> 间接引用的调色板颜色，全部是资源字典的 Color token key：
/// 消费方必须写 <c>Application.Current.Resources[key] as Color</c> 取色，严禁硬编码任何十六进制颜色值。
/// </para>
/// <para>
/// 本记录不可变（init 属性）；用 <see cref="With"/> 基于现有实例派生新配置，用 <see cref="Default"/> 获取默认值。
/// </para>
/// </remarks>
public sealed record BackgroundConfig(
    /// <summary>当前工作模式。</summary>
    BackgroundMode Mode,

    /// <summary>纯色模式使用的颜色资源 token key（非十六进制颜色值）。</summary>
    string SolidColorToken,

    /// <summary>图片模式使用的图片绝对路径；非图片模式可为空串。</summary>
    string ImagePath,

    /// <summary>使用的调色板 Id，对应 <see cref="PaletteDefinition.Id"/>。</summary>
    string PaletteId,

    /// <summary>天气模式使用的场景种类。</summary>
    WeatherSceneKind Scene,

    /// <summary>是否启用背景音效（雨声 / 雷声等）。</summary>
    bool AudioEnabled,

    /// <summary>背景音效音量，归一化 0..1：0 静音、1 最大。</summary>
    double AudioLevel,

    /// <summary>天气场景光照（光柱/折射与明暗）是否启用。</summary>
    bool SceneLightEnabled,

    /// <summary>测试版场景特效总开关：false 时场景层/近景雨/光照/真天气驱动全部不参与渲染。默认关闭。</summary>
    bool ExperimentalEnabled)
{
    /// <summary>
    /// 默认配置：天气 + 晴天 + <c>classic</c> 调色板 + 纯色回退 token <c>PageBg</c>，
    /// 无图片、开启音效且音量 0.6、开启场景光照。
    /// </summary>
    public static BackgroundConfig Default { get; } = new(
        BackgroundMode.Weather,
        "PageBg",
        "",
        "classic",
        WeatherSceneKind.Sunny,
        true,
        0.6,
        true,
        false);

    /// <summary>
    /// 基于当前配置派生新配置：只有传入非 <c>null</c> 的参数才会覆盖对应字段，其余字段保持原值。
    /// </summary>
    /// <param name="mode">要覆盖的模式；<c>null</c> 表示保持原值。</param>
    /// <param name="solidColorToken">要覆盖的纯色 token key；<c>null</c> 表示保持原值。</param>
    /// <param name="imagePath">要覆盖的图片绝对路径；<c>null</c> 表示保持原值。</param>
    /// <param name="paletteId">要覆盖的调色板 Id；<c>null</c> 表示保持原值。</param>
    /// <param name="scene">要覆盖的天气场景；<c>null</c> 表示保持原值。</param>
    /// <param name="audioEnabled">要覆盖的音效开关；<c>null</c> 表示保持原值。</param>
    /// <param name="audioLevel">要覆盖的音效音量；<c>null</c> 表示保持原值。</param>
    /// <param name="sceneLightEnabled">要覆盖的场景光照开关；<c>null</c> 表示保持原值。</param>
    /// <param name="experimentalEnabled">要覆盖的测试版总开关；null 表示保持原值。</param>
    /// <returns>应用覆盖后的新实例；本实例不被修改。</returns>
    public BackgroundConfig With(
        BackgroundMode? mode = null,
        string? solidColorToken = null,
        string? imagePath = null,
        string? paletteId = null,
        WeatherSceneKind? scene = null,
        bool? audioEnabled = null,
        double? audioLevel = null,
        bool? sceneLightEnabled = null,
        bool? experimentalEnabled = null)
    {
        return this with
        {
            Mode = mode ?? Mode,
            SolidColorToken = solidColorToken ?? SolidColorToken,
            ImagePath = imagePath ?? ImagePath,
            PaletteId = paletteId ?? PaletteId,
            Scene = scene ?? Scene,
            AudioEnabled = audioEnabled ?? AudioEnabled,
            AudioLevel = audioLevel ?? AudioLevel,
            SceneLightEnabled = sceneLightEnabled ?? SceneLightEnabled,
            ExperimentalEnabled = experimentalEnabled ?? ExperimentalEnabled,
        };
    }
}
