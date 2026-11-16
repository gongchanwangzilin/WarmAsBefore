using Microsoft.Maui.Graphics;
using WarmAsBefore.Helpers;
using WarmAsBefore.Models.Background;

namespace WarmAsBefore.Services.Background;

/// <summary>
/// 统一背景服务：所有背景 / 调色板 / 天气配置的唯一运行时载体与下发中枢。
///
/// 设计目标：杜绝背景配置散落到多个 ViewModel。任何页面、控件、设置项要读写背景，
/// 都只与本单例打交道；本服务把「统一配置」翻译成 <c>Bg*</c> 语义资源写入
/// <see cref="Application.Current"/> 的资源字典，并通过 <see cref="Changed"/> 通知 UI 自行刷新。
///
/// 约定：
/// 1. Apply 是纯配置下发，不直接触碰任何具体页面的 Layout / Opacity —— 页面通过 Changed 自己更新。
/// 2. 所有颜色用 token key 表示，唯一 hex 只出现在 <see cref="FallbackColor"/> 一处。
/// 3. Changed 订阅者可能是 UI 控件，而 Apply 可能来自线程池，故通知统一用 <see cref="Ui.Post"/> 切回 UI 线程。
/// </summary>
public sealed class BackgroundService
{
    /// <summary>
    /// 全服务唯一允许出现的十六进制颜色：资源字典缺 token 时的最后兜底色，
    /// 保证即使资源尚未就绪也能画出可见背景，而不是透明 / 黑屏。
    /// </summary>
    private static readonly Color FallbackColor = Color.FromArgb("#FDF8F0");

    /// <summary>当前配置（只读引用）。要修改请走 setter / Apply，不要直接改字段。</summary>
    private BackgroundConfig _config = BackgroundConfig.Default;

    /// <summary>当前场景参数（<see cref="CurrentScene"/> 的 backing field），随 Apply 同步更新。</summary>
    private WeatherSceneDefinition _scene;

    /// <summary>
    /// 最后一次真正下发到资源字典 / Changed 的配置快照，专供幂等判断。
    /// 不能拿它和 <see cref="_config"/> 比：setter 会先写 _config 再调 Apply，
    /// 那时两者已经相等，必须留一份「上次已发布」的独立快照才能识别重复下发。
    /// </summary>
    private BackgroundConfig? _lastPublished;

    public BackgroundService()
    {
        // 初值即默认配置；场景同步取自目录，保证构造后 CurrentScene 永远非 null。
        _scene = BackgroundCatalog.Scenes[_config.Scene];
    }

    /// <summary>当前配置，只读引用。</summary>
    public BackgroundConfig Current => _config;

    /// <summary>配置变化通知；订阅者通常是 UI 控件，回调已被切回 UI 线程。</summary>
    public event Action<BackgroundConfig>? Changed;

    /// <summary>当前场景参数，等价于 <c>BackgroundCatalog.Scenes[Current.Scene]</c>。</summary>
    public WeatherSceneDefinition CurrentScene => _scene;

    /// <summary>
    /// 全量替换配置并下发：存配置、更新场景、通知 Changed、刷新 Bg* 资源。
    /// 相同配置重复调用是幂等的（record 值相等）不会触发 Changed。
    /// </summary>
    public void Apply(BackgroundConfig config)
    {
        // 幂等：值相等就跳过，避免天气轮询等高频调用反复重绘。
        if (_lastPublished == config) return;

        _config = config;
        _scene = BackgroundCatalog.Scenes[_config.Scene];

        // 捕获局部快照：Apply 可能在线程池执行，闭包不能引用会被再次改写的字段。
        var cfg = _config;
        // 线程纪律：Changed 订阅者可能是 UI 控件，统一切回 UI 线程；已在 UI 线程时同步执行。
        Ui.Post(() => Changed?.Invoke(cfg));

        PublishToResources();

        _lastPublished = config;
    }

    /// <summary>切换工作模式。</summary>
    public void SetMode(BackgroundMode mode)
    {
        _config = _config.With(mode: mode);
        Apply(_config);
    }

    /// <summary>切换调色板（按 Catalog.Palettes 的 id）。</summary>
    public void SetPalette(string paletteId)
    {
        _config = _config.With(paletteId: paletteId);
        Apply(_config);
    }

    /// <summary>切换天气场景。</summary>
    public void SetScene(WeatherSceneKind scene)
    {
        _config = _config.With(scene: scene);
        Apply(_config);
    }

    /// <summary>设置纯色模式使用的颜色 token key。</summary>
    public void SetSolidColor(string tokenKey)
    {
        _config = _config.With(solidColorToken: tokenKey);
        Apply(_config);
    }

    /// <summary>设置图片模式的图片路径。</summary>
    public void SetImage(string path)
    {
        _config = _config.With(imagePath: path);
        Apply(_config);
    }

    /// <summary>设置背景音效开关与音量。</summary>
    public void SetAudio(bool enabled, double level)
    {
        _config = _config.With(audioEnabled: enabled, audioLevel: level);
        Apply(_config);
    }

    /// <summary>
    /// 从天气描述解析场景并切换。天气轮询可能每秒触发一次，
    /// 故仅当解析出的场景与当前不同才真正 Apply，避免无谓重绘。
    /// </summary>
    public void ApplyWeatherDescription(string? description)
    {
        var target = BackgroundCatalog.Resolve(description);
        if (target == _config.Scene) return;
        SetScene(target);
    }

    /// <summary>
    /// 把用户设置里的散字段聚合成统一配置 —— 「配置不散落多个 VM」的关键入口。
    /// </summary>
    /// <param name="experimentalEnabled">测试版场景特效总开关；false 时天气场景一律回退纯色。默认 false。</param>
    public static BackgroundConfig ConfigFromSettings(
        string? backgroundMode, string? imagePath, string? themeName,
        string? weatherDescription, bool audioEnabled, double audioLevel,
        bool sceneLightEnabled = true, bool experimentalEnabled = false)
    {
        var mode = backgroundMode switch
        {
            "image" => BackgroundMode.Image,
            "clear" => BackgroundMode.Solid,
            _ => BackgroundMode.Weather,
        };

        // 测试版总开关关闭：新场景（灰雾底/云/雨/光柱）完全不参与渲染，背景回退纯色/图片。
        // 放在 switch 之后统一拦截，调用方即使传了 weather 也强制降级。
        if (!experimentalEnabled && mode == BackgroundMode.Weather) mode = BackgroundMode.Solid;

        // 纯色模式统一用 PageBg token；其余模式该字段不被消费，仍保持同一默认值便于切换。
        var solidColorToken = mode == BackgroundMode.Solid
            ? "PageBg"
            : BackgroundConfig.Default.SolidColorToken;

        // themeName 必须是目录里存在的调色板 id，否则回退到 classic。
        var paletteId = themeName is not null && BackgroundCatalog.Palettes.ContainsKey(themeName)
            ? themeName
            : "classic";

        var scene = BackgroundCatalog.Resolve(weatherDescription);

        return new BackgroundConfig(
            mode,
            solidColorToken,
            imagePath ?? string.Empty,
            paletteId,
            scene,
            audioEnabled,
            audioLevel,
            sceneLightEnabled,
            experimentalEnabled);
    }

    /// <summary>
    /// 把当前调色板与场景的语义色写入 Application.Current.Resources，
    /// 统一用 Bg* 前缀避免和已有 token 冲突。所有消费方（Drawable / 控件 / 设置页）
    /// 一律从 Bg* 取色，实现「一处下发、处处消费」。
    /// </summary>
    private void PublishToResources()
    {
        var resources = Application.Current?.Resources;
        if (resources is null) return;

        var scene = _scene;
        var palette = BackgroundCatalog.Palettes[_config.PaletteId];

        // ── 场景语义色（6 个）────────────────────────────────────────────────
        resources["BgSkyTop"] = BackgroundCatalog.ResolveColor(scene.SkyTopToken, FallbackColor);
        resources["BgSkyBottom"] = BackgroundCatalog.ResolveColor(scene.SkyBottomToken, FallbackColor);
        resources["BgCloud"] = BackgroundCatalog.ResolveColor(scene.CloudToken, FallbackColor);
        resources["BgRain"] = BackgroundCatalog.ResolveColor(scene.RainToken, FallbackColor);
        resources["BgSun"] = BackgroundCatalog.ResolveColor(scene.SunToken, FallbackColor);
        // 场景定义里没有 scrim 字段，遮罩色取当前调色板的 ScrimKey。
        resources["BgScrim"] = BackgroundCatalog.ResolveColor(palette.ScrimKey, FallbackColor);

        // ── 调色板语义色（5 个）──────────────────────────────────────────────
        resources["BgSurface"] = BackgroundCatalog.ResolveColor(palette.SurfaceBgKey, FallbackColor);
        resources["BgSurfaceFg"] = BackgroundCatalog.ResolveColor(palette.SurfaceFgKey, FallbackColor);
        resources["BgAccent"] = BackgroundCatalog.ResolveColor(palette.AccentKey, FallbackColor);
        resources["BgPanel"] = BackgroundCatalog.ResolveColor(palette.PanelBgKey, FallbackColor);
        resources["BgPanelBorder"] = BackgroundCatalog.ResolveColor(palette.PanelBorderKey, FallbackColor);
    }
}
