namespace WarmAsBefore.Services;

using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.DesignSystem.Worlds;

/// <summary>
/// 页面背景：纯色 / 自定义图片 / 窗口透视。
///
/// 这是玻璃能否「看得出是玻璃」的前提 —— 半透明表面必须有**背后可透出的内容**。
/// 压在一块纯色窗口背景上时，无论怎么调 alpha、加高光，出来都只是一块浅色板；
/// 而这层内容还必须是**糊过的**：原样的壁纸从三成不透明的卡面后面透出来，读起来是「脏照片」，不是玻璃。
///
/// 所以这一层分成两步：
///   1. 糊底：<see cref="FrostImageService"/> 在后台把壁纸（或合成的主题地面）降采样、模糊、
///      归一化、加颗粒，落一张 PNG；这一步只在背景变化时发生一次。
///   2. 铺底：糊底 PNG 画在 WinUI 窗口根节点上（MAUI 的 ImageBrush 在页面层会多一次解码，
///      而且页面本身还要盖一层薄纱），页面层只留一层**很薄的 scrim** 保证正文对比度。
///      「窗口透视」档优先把活交给系统 backdrop（<see cref="SystemBackdropService"/>），
///      那才是真正随桌面变化而流动的模糊；系统不支持时才退回自家糊底。
///
/// 三个档位的页面底：
///   图片 / 纯色 → 糊底图（窗口层）+ 0.20 薄纱（页面层）
///   窗口透视   → 系统 acrylic（或老的 composition 亚克力）+ 0.13~0.20 薄纱
///   糊底还没算好 → 沿用旧行为（不透明地面 / 让窗口透上来），糊底一到自己升级，不掉帧不闪白
///
/// 页面所有 ContentPage 的根背景统一走 {DynamicResource PageBgBrush}（见各页面 XAML），
/// 这里决定它是薄纱还是不透明地面。
/// </summary>
public static class AppBackgroundService
{
    public const string PageBgBrushKey = "PageBgBrush";

    /// <summary>糊底已经在窗口层铺好时，页面只需要这么薄的一层纱。</summary>
    private const double VeilOnFrost = 0.20;

    /// <summary>系统 backdrop 已经糊过一次时，纱要更薄 —— 否则桌面糊 + 页面糊 + 厚纱 = 一块灰板。</summary>
    private const double VeilOnBackdrop = 0.13;

    /// <summary>窗口没就绪时的重试上限（约 8 秒）。超过就不再等 —— 那时该由下次 Apply 接手。</summary>
    private const int MaxRetries = 20;

    private static Brush? _current;
    private static string _imagePath = "";
    /// <summary>世界给的地面（PageBg）。换主题时由 ThemeManager 重新下发。</summary>
    private static Color _pageBg = Colors.WhiteSmoke;
    /// <summary>纯色档的自选底色；null = 跟主题走。只影响纯色档，图片与透视档不看它。</summary>
    private static Color? _solid;
    private static bool _image;          // 自定义图片档
    private static bool _clear;          // 窗口透视档
    private static bool _frostPainted;   // 窗口层已经铺了自家糊底
    private static bool _backdropLive;   // 系统 backdrop 生效
    private static bool _legacyAcrylic;  // 老的 composition 亚克力生效
    private static bool _hooked;
    private static int _retries;         // 窗口还没建好时的重试计数（首帧前下发设置会走到这条）

    /// <summary>
    /// 真正铺在页面下面的那层地面：纯色档用自选色，其余档用世界的地面。
    /// 糊底、薄纱、无糊底时的实心底都只读这一个值 —— 少了这一处间接，
    /// 「换颜色」就要在四个地方各判一次模式。
    /// </summary>
    private static Color Ground => !_image && !_clear && _solid is { } c ? c : _pageBg;

    /// <summary>
    /// 纯色档的自选底色（null = 跟主题走）。设置页选色板时立即调用：
    /// 页面薄纱当场换色，窗口层那块糊底在后台重合成（key 里含色值，所以是新的一张）。
    /// 图片档/透视档不重新算 —— 它们背后不是这层地面。
    /// </summary>
    public static void SetSolidTint(Color? color)
    {
        if (_solid == color) return;
        _solid = color;
        // 首帧之前 Apply 还没跑过：这会儿连地面色都还是兜底值，
        // 现在合成一张糊底等于白算一张（真正那张由紧接着的 Apply 发起）。
        if (_current is null || _image || _clear) return;
        RequestFrost();
        _current = BuildPageBrush();
        Publish();
    }

    /// <summary>由 RuntimeConfigurator 在设置下发时调用。</summary>
    public static void Apply(string mode, string imagePath, Color pageBg)
    {
        if (!_hooked)
        {
            _hooked = true;
            FrostImageService.Ready += OnFrostReady;
        }

        _pageBg = pageBg;
        _image = mode == "image" && !string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath);
        _clear = !_image && mode == "clear";
        _imagePath = _image ? imagePath : "";
        _retries = 0;

        // Shell 自己也有底色（AppShell.xaml 写死了 PageBg）：页面一旦是薄纱，Shell 必须让开，
        // 否则糊底永远透不到页面上来。给它一颗透明画刷（而不是 null）——
        // Background 为 null 时 VisualElement 会回落到 BackgroundColor，那还是那块实色。
        if (Shell.Current is not null)
            Shell.Current.Background = new SolidColorBrush(Colors.Transparent);

        RequestFrost();
        Repaint();
    }

    /// <summary>
    /// 换世界/换色板后重算页面底。纯色档的糊底是从地面色合成的，所以它也必须跟着重算；
    /// 图片档的糊底与颜色无关，key 不变时 <see cref="FrostImageService.Request"/> 直接返回。
    /// 这里只重算、不下发：调用方（ThemeManager.Flush）紧接着就会 Publish。
    /// </summary>
    public static void Recolor(Color pageBg)
    {
        _pageBg = pageBg;
        RequestFrost();
        _current = BuildPageBrush();
    }

    /// <summary>把当前背景回写到资源字典（换主题后要重放，否则会被纯色覆盖）。</summary>
    public static void Publish()
    {
        if (_current is null || Application.Current is null) return;
        Application.Current.Resources[PageBgBrushKey] = _current;
    }

    /// <summary>糊底算好了（后台线程）：切回主线程铺底、发薄纱。</summary>
    private static void OnFrostReady(object? sender, EventArgs e)
        => MainThread.BeginInvokeOnMainThread(Repaint);

    /// <summary>
    /// 定窗口层（系统 backdrop / 糊底图），再定页面那一层。
    /// 只在主线程上跑：WinUI 的背景是主线程的对象，而「糊底有没有真的铺上」决定页面薄纱的浓度。
    /// </summary>
    private static void Repaint()
    {
        var app = Application.Current;
        if (app is null) return;
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(Repaint);
            return;
        }

        var painted = PaintPlatform();
        if (!painted && _retries < MaxRetries)
        {
            // 窗口/根节点还没建好（首帧前就把设置下发下来了）：稍后再来一次。
            // 糊底事件只发一次，这里不重试的话页面会永远停在「背后没东西可透」的实心档。
            _retries++;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(400);
                Repaint();
            });
        }
        else if (painted)
        {
            _retries = 0;
        }

        var blurred = _frostPainted || _backdropLive || _legacyAcrylic;
        var changed = GlassTokens.FrostedBackdrop != blurred;
        GlassTokens.FrostedBackdrop = blurred;

        _current = BuildPageBrush();
        Publish();

        // 玻璃从「实心」切到「薄」是整套令牌的事：重发一遍，已渲染的卡片/按钮跟着换。
        // 走 ThemeManager 的防抖通道，和换主题共用同一个重排窗口。
        if (changed) ThemeManager.Current?.Republish();
    }

    /// <summary>
    /// 页面底：有糊底就只给一层薄纱，没有就沿用今天的做法。
    /// 薄纱不是装饰 —— 卡片面只有三成到四成实，正文的对比度全靠它把糊底压暗/提亮一档。
    /// </summary>
    private static Brush BuildPageBrush()
    {
        if (!GlassTokens.FrostedBackdrop)
        {
            // 背后没有真糊的东西可透：图片档让窗口层先顶上来（糊底算好会自己升级），
            // 纯色档与其它情况照旧铺一层不透明地面。
            if (_image || _clear) return new SolidColorBrush(Colors.Transparent);
            return StyleWorldTokens.GroundBrush(Ground);
        }

        return Veil(_backdropLive && !_frostPainted ? VeilOnBackdrop : VeilOnFrost);
    }

    /// <summary>薄纱：斜着从厚一点走到薄一点，不是为了好看 —— 一整块均匀的纱会把整页压平。</summary>
    private static Brush Veil(double alpha)
    {
        var a = (float)Math.Clamp(alpha, 0, 1);
        var ground = Ground;
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.35, 1) };
        brush.GradientStops.Add(new GradientStop { Color = ground.WithAlpha(a), Offset = 0f });
        brush.GradientStops.Add(new GradientStop { Color = ground.WithAlpha(a * 0.55f), Offset = 1f });
        return brush;
    }

    /// <summary>请求一张糊底（幂等）。三种背景模式都要有：纯色与透视档合成一张，图片档糊壁纸。</summary>
    private static void RequestFrost()
    {
#if WINDOWS
        var res = Application.Current?.Resources;
        var ground = Ground;
        var well = res is not null && res.TryGetValue("WellBg", out var wv) && wv is Color wc ? wc : ground;
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        FrostImageService.Request(_imagePath, ground, well, dark);
#endif
    }

    /// <summary>
    /// 窗口层：系统 backdrop（透视档）／糊底图（图片、纯色档）／什么都不是（糊底还没好）。
    /// 每一支都把三个状态位写全 —— 页面薄纱的浓度完全靠它们，漏写一位就会得到一块不该出现的灰。
    /// </summary>
    private static bool PaintPlatform()
    {
#if WINDOWS
        try
        {
            var win = Application.Current?.Windows
                .FirstOrDefault(w => w.Handler is not null && w is not Views.ToolWindow);
            if (win?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window w) return false;
            var root = w.Content as Microsoft.UI.Xaml.Controls.Panel;

            if (_clear)
            {
                // 系统 backdrop 优先：它会真的跟着桌面动。别再叠自家糊底，两层糊在一起是双重发灰。
                if (SystemBackdropService.TryApply(_pageBg))
                {
                    _backdropLive = true;
                    _frostPainted = false;
                    _legacyAcrylic = false;
                    if (root is not null) root.Background = null;
                    WindowBackdrop.SetTransparent(false);   // 撤掉老的 composition 亚克力，别两层
                    return true;
                }

                _backdropLive = false;
                SystemBackdropService.Clear();
                _legacyAcrylic = WindowBackdrop.SetTransparent(true);
                if (_legacyAcrylic)
                {
                    _frostPainted = false;
                    if (root is not null) root.Background = null;
                    return true;
                }
                // 两条系统路径都不通：落到下面铺自家合成的糊底，至少不是压在裸桌面上
            }
            else
            {
                _backdropLive = false;
                _legacyAcrylic = false;
                SystemBackdropService.Clear();
                WindowBackdrop.SetTransparent(false);
            }

            if (root is null) return false;

            var frost = FrostImageService.FilePath;
            if (!string.IsNullOrEmpty(frost) && File.Exists(frost))
            {
                // 糊底是「已经糊过的位图」：这里只是在铺图，主线程不解码大图
                root.Background = new Microsoft.UI.Xaml.Media.ImageBrush
                {
                    ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(frost)),
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
                };
                _frostPainted = true;
            }
            else if (_image)
            {
                // 糊底还在算：先把原图铺上（就是今天的行为），算好之后无缝换成糊底
                root.Background = new Microsoft.UI.Xaml.Media.ImageBrush
                {
                    ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(_imagePath)),
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
                };
                _frostPainted = false;
            }
            else
            {
                root.ClearValue(Microsoft.UI.Xaml.Controls.Panel.BackgroundProperty);
                _frostPainted = false;
            }
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("AppBackgroundService.PaintPlatform -> " + ex.Message);
            _frostPainted = false;
            return false;
        }
#else
        // 其它平台（Android）：没有窗口层可以铺糊底，页面底保持今天的样子。
        // 卡片那边会因为 GlassTokens.FrostedBackdrop=false 自动退回七成实 —— 没有糊的东西可透，
        // 就不该做成玻璃；这是「宁可丑，也不能让正文压在照片上」的同一条规则。
        _frostPainted = false;
        _backdropLive = false;
        _legacyAcrylic = false;
        return true;
#endif
    }
}
