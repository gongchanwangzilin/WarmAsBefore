namespace WarmAsBefore.DesignSystem.Theme;

/// <summary>一次玻璃下发所需的全部参数。</summary>
public readonly record struct GlassParams(
    string Tier,
    double Translucency,
    double Frost,
    double Height,
    double LightX,
    double LightY,
    double LightZ,
    double LightWidth,
    bool Reduced);

/// <summary>
/// 三档材质，外加弹窗那一档。这是全站**唯一**的厚度阶梯（Win11 的 Mica / Mica Alt / Acrylic）：
///   Bar     薄   —— 页底座、上下栏：只给底色一点奶白，主要让背景透上来
///   Card    中   —— 卡片（Mica Alt）：稍实一档 + 1px 亮边，读者能看出它「浮在页面上」
///   Button  厚   —— 按钮：胶质，顶面有高光带、底面有接触阴影、边缘双线
///   Dialog  最厚 —— 弹窗：最实、最亮边、最大投影，因为它是模态的
/// </summary>
public enum GlassKind
{
    Bar,
    Card,
    Button,
    Dialog
}

/// <summary>
/// 把「材质档 + 各调制量」换算成画刷/阴影资源，写进 Application.Resources。
/// 组件样式（卡片 / 按钮 / 上下栏 / 弹窗）用 {DynamicResource ...} 引用这些键。
///
/// 这一版是**液态玻璃**：面只有三成到四成不透明，因为背后第一次真的有东西在透 ——
/// <see cref="Services.FrostImageService"/> 把壁纸（或合成的地面）先降到 320px、高斯模糊、
/// 归一化亮度，再铺到窗口层（见 <see cref="Services.AppBackgroundService"/>）。
/// 面要透，透出来的才是「磨砂」；面要是七成实，透出来的只是一块白板 —— 这就是上一版的病根。
///
/// 四条规则不变，只是取值按「有真糊底」重标定：
///   1. 一条描边规则：迎光边亮、背光边暗，中段几乎透明 —— 那条 1~2px 的环就是玻璃的**折射环**
///      （两端的 2% 品红 / 青是极淡的色散，刻意做到测得出、看不出）。
///   2. 一条阴影规则：随「液态高度」变化（越高越远越散），档位只当倍率用。
///   3. 一套圆角阶梯：4（小控件）/ 8（卡片、按钮）/ 12（栏、弹窗）。
///   4. 三档厚度 + 弹窗：面越厚，alpha 越高、高光越强、边越亮、影越大。
///
/// 镜面高光是面上第一颗色标（offset 0），落在左上一角：这是 iOS 26 那种「一块被光照到的玻璃」
/// 的唯一几何线索 —— 一条从左上斜落的高光带，悬停时它沿面的轴向滑 6%（由 HoverFeel 的
/// LiquidFace 克隆渐变做，见 Helpers/HoverFeel.SlideGradient）。
/// </summary>
public static class GlassTokens
{
    // ── 键名 ────────────────────────────────────────────────────────────

    public const string BarBg = "GlassBarBg";
    public const string BarStroke = "GlassBarStroke";
    public const string BarStrokeThickness = "GlassBarStrokeThickness";
    public const string BarShadow = "GlassBarShadow";

    public const string CardBg = "GlassCardBg";
    public const string CardStroke = "GlassCardStroke";
    public const string CardStrokeThickness = "GlassCardStrokeThickness";
    public const string CardShadow = "GlassCardShadow";

    public const string ButtonBg = "GlassButtonBg";
    public const string ButtonBgHover = "GlassButtonBgHover";
    public const string ButtonBgPressed = "GlassButtonBgPressed";
    public const string ButtonStroke = "GlassButtonStroke";
    public const string ButtonStrokeThickness = "GlassButtonStrokeThickness";

    public const string DialogBg = "GlassDialogBg";
    public const string DialogStroke = "GlassDialogStroke";
    public const string DialogStrokeThickness = "GlassDialogStrokeThickness";
    public const string DialogShadow = "GlassDialogShadow";

    /// <summary>页面里那条「1px 发丝线」的数值版：页面 XAML 不再自带描边数值。</summary>
    public const string HairlineKey = "GlassHairline";

    // ── 规则常量 ────────────────────────────────────────────────────────

    /// <summary>发丝线的定数：栏与分隔线永远是 1px，粗一分就变成盒子。</summary>
    public const double Hairline = 1.0;

    /// <summary>Win11 圆角阶梯。窗口尺度变化时圆角在这一组里取最近的一档，不做连续缩放。</summary>
    public const double RadiusSmall = 4.0;
    public const double RadiusMedium = 8.0;
    public const double RadiusLarge = 12.0;
    public const double RadiusDialog = 12.0;

    /// <summary>
    /// 光从左上来、宽 0.5、高 0.35。光照控制已从设置在移除（只剩这三个常量），
    /// 想改方向就改这里 —— 它是这套材质里唯一还允许「调光」的地方。
    /// </summary>
    public const double FixedLightX = -0.7;
    public const double FixedLightY = -0.7;
    public const double FixedLightZ = 0.35;
    public const double FixedLightWidth = 0.5;

    /// <summary>轻量模式：不发布阴影。由 RuntimeConfigurator 按用户设置下发。</summary>
    public static bool LightMode { get; set; }

    /// <summary>
    /// 背后是否真的有一层被模糊过的背景（<see cref="Services.FrostImageService"/> 已就绪，
    /// 或系统 acrylic backdrop 已生效）。false 时面自动做实：没有东西可透的时候，
    /// 半透明只是一层脏纱，正文会直接压在照片上 —— 宁可丑，也不能让字读不出来。
    /// 由 <see cref="Services.AppBackgroundService"/> 在糊底就绪后置位，并请求重发令牌。
    /// </summary>
    public static bool FrostedBackdrop { get; set; }

    /// <summary>关闭玻璃时的「面」：比页面底近白一档，保证按钮和卡还站得出来。</summary>
    private const double OpaqueAlpha = 0.94;

    /// <summary>面 alpha 的上限：再实就不叫玻璃了，再透正文就开始被背景吃掉。</summary>
    private const double FaceCeiling = 0.62;

    /// <summary>
    /// 玻璃的三个色标 + 边的三个色标，全部来自 StyleWorldTokens。
    /// GlassTokens 只管「哪一档多透、边多亮、影多远」，颜色一律由世界给 ——
    /// 否则换到晨雾蓝灰或深色档时，玻璃卡片会变成一块不属于当前世界的奶油。
    /// </summary>
    private readonly record struct Tints(Color Top, Color Mid, Color Low, Color EdgeHi, Color EdgeMid, Color EdgeLo);

    /// <summary>
    /// 一档材质的配方。
    ///   FaceTop/Body/Low  面从上到下的 alpha 基准（**有糊底时**的真实不透明度）
    ///   Specular          左上一角那颗镜面白（6~22%）
    ///   EdgeLit/EdgeShade 描边迎光端 / 背光端
    ///   Contact           底部接触阴影的浓度
    ///   SpecularAt        镜面停在面的哪个高度
    ///   Band              高光带宽（面的百分比），按下时按倍率收窄
    ///   Stroke            这一档的环宽（1~2px）
    ///   ShadowWeight      阴影规则在这一档上的倍率
    /// 这些数是「谁多厚」，不是「浮多高」——体积由 GlassHeight 统一给。
    /// </summary>
    private readonly record struct Spec(
        double FaceTop, double FaceBody, double FaceLow, double Specular,
        double EdgeLit, double EdgeShade, double Contact, double SpecularAt, double Band,
        double Stroke, double ShadowWeight);

    private static Spec Of(GlassKind kind) => kind switch
    {
        // 栏最薄：它只是页脚/页头的一块薄玻璃，透出糊底就够了
        GlassKind.Bar => new(0.30, 0.24, 0.17, 0.06, 0.40, 0.07, 0.05, 0.03, 0.22, 1.0, 0.55),
        // 卡片：0.44 → 0.28，中段落在 0.36 —— 用户的验收标准「卡片底下是模糊背景在流」全靠这一档
        GlassKind.Card => new(0.44, 0.36, 0.27, 0.10, 0.55, 0.10, 0.09, 0.05, 0.24, 2.0, 1.00),
        // 按钮最胶质：镜面最亮、环最亮、接触阴影最深
        GlassKind.Button => new(0.54, 0.44, 0.35, 0.22, 0.70, 0.16, 0.14, 0.07, 0.20, 1.5, 1.30),
        // 弹窗最厚：它是模态的，得压得住下面所有的光
        GlassKind.Dialog => new(0.60, 0.52, 0.43, 0.13, 0.72, 0.13, 0.12, 0.04, 0.26, 2.0, 1.80)
    };

    public static void Publish(GlassParams p)
    {
        var app = Application.Current;
        if (app is null) return;
        var res = app.Resources;

        // 轻量模式（设置里可开）或诊断环境变量都会关掉阴影
        var noShadow = LightMode || Environment.GetEnvironmentVariable("WARM_NO_SHADOW") == "1";
        var surface = ReadColor(res, "SurfaceBg", "#FDF8F0");
        var tint = new Tints(
            ReadColor(res, "GTintTop", "#FEFCF8"), ReadColor(res, "GTintMid", "#FDF8F0"),
            ReadColor(res, "GTintLow", "#E8DCC8"), ReadColor(res, "GEdgeHi", "#FFFFFF"),
            ReadColor(res, "GEdgeMid", "#FDF6EE"), ReadColor(res, "GEdgeLo", "#8B7D6B"));

        var t = Clamp01(p.Translucency);
        var f = Clamp01(p.Frost);
        var h = Clamp01(p.Height);
        var w = Clamp01(p.LightWidth);
        var z = Clamp01(p.LightZ);
        // 透明度为 0 等同于关闭玻璃：面变成实心、描边收掉、影不投 —— 与旧档位 "none" 同一后果。
        var flat = string.Equals(p.Tier, "none", StringComparison.OrdinalIgnoreCase) || t <= 0.001;

        var (dx, dy) = Direction(p.LightX, p.LightY);

        // 面：只有背后真有糊底时才敢薄。没有糊底时（Android、糊底还在算、系统 backdrop 不可用）
        // 一律回到七成到九成实 —— 这不是保守，是「玻璃必须有东西可透」的唯一诚实做法。
        Color Face(Color c, double a)
        {
            if (p.Reduced) return new Color(c.Red, c.Green, c.Blue, 1f);
            // 磨砂越重，面越奶一档（alpha 略升）：磨砂不是变模糊（那是底层的事），是变奶
            var milk = 1 + 0.30 * f;
            var a1 = a * (0.80 + 0.30 * t) * milk;
            var alpha = FrostedBackdrop
                ? Math.Clamp(a1, 0, FaceCeiling)
                : Math.Clamp(0.80 + a1 * 0.22, 0, OpaqueAlpha + 0.02);
            return new Color(c.Red, c.Green, c.Blue, (float)Clamp01(alpha));
        }

        // 描边：同一根 1px 线上的渐变。起点压在迎光那一侧（左上），终点在背光那一侧（右下），
        // 所以「上沿亮、下沿暗」不是靠写两条边画出来的，而是光方向自己算出来的。
        // 中段压到几乎透明 = 折射环：眼睛读到的是「玻璃的边折了一下光」，不是「一个盒子」。
        Brush EdgeOf(Spec s, double dispersion)
        {
            var start = new Point(0.5 + dx * 0.5, 0.5 + dy * 0.5);
            var end = new Point(0.5 - dx * 0.5, 0.5 - dy * 0.5);
            var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
            // 迎光端：白 + 2% 品红（色散只给 2%，多一档就变成霓虹边）
            brush.GradientStops.Add(new GradientStop
            {
                Color = Edge(ColorBurn(tint.EdgeHi, 0xFF2E9A, dispersion), s.EdgeLit),
                Offset = 0f
            });
            // 光宽：窄光是一道锐利反光（高光提前结束），宽光是整片漫射
            brush.GradientStops.Add(new GradientStop
            {
                Color = Edge(tint.EdgeMid, s.EdgeLit * 0.10),
                Offset = (float)Clamp01(0.12 + w * 0.42)
            });
            // 背光端：黑 + 2.5% 青
            brush.GradientStops.Add(new GradientStop
            {
                Color = Edge(ColorBurn(Colors.Black, 0x22D3EE, dispersion), s.EdgeShade),
                Offset = 1f
            });
            return brush;
        }

        Color Edge(Color c, double a)
        {
            if (p.Reduced) return new Color(c.Red, c.Green, c.Blue, 1f);
            return new Color(c.Red, c.Green, c.Blue, (float)Clamp01(a * (1 - 0.32 * z)));
        }

        // ── 面：一条左上到右下的斜渐变。五段是「镜面高光 → 顶面受光 → 主体 → 主体 → 底部接触阴影」，
        //    这就是玻璃被读成「有厚度的一块」的全部几何线索。
        //    specShift 让镜面上下移动（悬停留给 HoverFeel 做 6% 滑动），bandK 控高光带宽窄，
        //    bodyK/contactK 是按下与悬停时的压暗/压紧。
        Brush Slab(Spec s, double specShift, double bandK, double bodyK, double contactK)
        {
            var hiAt = Clamp01(s.SpecularAt + specShift);
            // 液态高度越高，光在高处铺得越开（同一束光在更高的地方漫射得更宽）
            var band = s.Band * bandK * (1.06 - 0.12 * h);
            var brush = new LinearGradientBrush
            {
                // 斜轴：左上角起、右下角收。镜面那颗色标因此只落在左上那一块，而不是横贯整条顶边
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0.55, 1)
            };
            // 面做实的时候镜面也要收：不透明面上压一颗 10% 的白会在左上一角糊出一团白雾
            var specular = Face(tint.EdgeHi, s.Specular * (FrostedBackdrop ? 1.0 : 0.55));
            brush.GradientStops.Add(new GradientStop { Color = specular, Offset = 0f });
            brush.GradientStops.Add(new GradientStop { Color = Face(tint.Top, s.FaceTop), Offset = (float)hiAt });
            brush.GradientStops.Add(new GradientStop
            {
                Color = Face(tint.Mid, s.FaceBody * bodyK),
                Offset = (float)Clamp01(hiAt + band)
            });
            brush.GradientStops.Add(new GradientStop { Color = Face(tint.Mid, s.FaceBody * bodyK), Offset = 0.72f });
            brush.GradientStops.Add(new GradientStop
            {
                Color = Shade(Face(tint.Low, s.FaceLow * (0.9 + 0.1 * bodyK)), s.Contact * contactK),
                Offset = 1f
            });
            return brush;
        }

        // ── 阴影：唯一的规则。液态高度给距离与扩散，档位只乘一个权重，背光方向给朝向。
        Brush ShadowInk(Spec s)
        {
            if (noShadow) return new SolidColorBrush(Colors.Transparent);
            var opacity = Clamp01((0.12 + 0.22 * h) * (1 - 0.30 * z));
            return new SolidColorBrush(new Color(0f, 0f, 0f, (float)Clamp01(opacity * Math.Min(1.0, s.ShadowWeight))));
        }
        Shadow ShadowOf(Spec s)
        {
            var reach = (3 + 12 * h) * s.ShadowWeight;
            var radius = (8 + 26 * h) * s.ShadowWeight * (1 + 0.5 * z);
            var opacity = Clamp01((0.12 + 0.22 * h) * (1 - 0.30 * z));
            return new Shadow
            {
                Brush = Colors.Black,
                // 影子永远投向光的反方向；横向只走一半 —— Win11 的影是「垂直落下」，不是斜着打出去的
                Offset = new Point((float)(-dx * reach * 0.5), (float)(-dy * reach)),
                Radius = (float)radius,
                Opacity = (float)opacity
            };
        }

        // ── 落笔：薄 / 中 / 厚 / 弹窗 ──────────────────────────────────────
        var bar = Of(GlassKind.Bar);
        var card = Of(GlassKind.Card);
        var button = Of(GlassKind.Button);
        var dialog = Of(GlassKind.Dialog);

        var hairline = flat ? 0.0 : Hairline;
        res[HairlineKey] = hairline;

        // 环宽：栏 1px（它只是条），卡片与弹窗 2px（折射环要有厚度才折得出来），按钮 1.5px。
        res[BarBg] = flat ? Solid(Sink(surface, 0.03)) : Slab(bar, 0, 1, 1, 1);
        res[BarStroke] = flat ? Solid(Colors.Transparent) : EdgeOf(bar, 0.020);
        res[BarStrokeThickness] = flat ? 0.0 : bar.Stroke;
        res[BarShadow] = noShadow || flat ? null : ShadowOf(bar);

        res[CardBg] = flat ? Solid(surface) : Slab(card, 0, 1, 1, 1);
        res[CardStroke] = flat ? Solid(Colors.Transparent) : EdgeOf(card, 0.020);
        res[CardStrokeThickness] = flat ? 0.0 : card.Stroke;
        // 诊断/降级开关：WARM_NO_SHADOW=1 时不发布卡片阴影。
        // 阴影在 WinUI 上是每个元素一个合成层，卡片多时既是首屏成本也是重排成本；
        // 低配机器或排查卡顿时可用它换流畅。
        res[CardShadow] = noShadow || flat ? null : ShadowOf(card);

        // 按钮：三个面 —— 静止 / 悬停（镜面下滑、边提亮）/ 按下（高光带收窄到 45%、接触阴影压紧）。
        // 这三个面就是「液体」的全部状态：胶质的厚度来自面上那一颗窄镜面，按下时它收窄，
        // 松手沿 MotionTokens.Spring 带过冲弹回（PressFeel 换的是这三张面，不是再加一层颜色）。
        res[ButtonBg] = flat ? Solid(Sink(surface, 0.55)) : Slab(button, 0, 1, 1, 1);
        res[ButtonBgHover] = flat ? Solid(Sink(surface, 0.80)) : Slab(button, 0.06, 1.20, 1.04, 0.82);
        res[ButtonBgPressed] = flat ? Solid(Sink(surface, 1.00)) : Slab(button, -0.04, 0.45, 0.90, 1.55);
        // 描边取主题的 BorderLine 平档；玻璃档用的是同一根渐变光边。
        res[ButtonStroke] = flat ? ReadColor(res, "BorderLine", "#D8CFC0") : EdgeOf(button, 0.025);
        res[ButtonStrokeThickness] = flat ? Hairline : button.Stroke;

        res[DialogBg] = flat ? Solid(Sink(surface, -0.04)) : Slab(dialog, 0, 1, 1, 1);
        res[DialogStroke] = flat ? Solid(ReadColor(res, "EdgeStrong", "#DAC49E")) : EdgeOf(dialog, 0.020);
        res[DialogStrokeThickness] = flat ? Hairline : dialog.Stroke;
        // 弹窗的影走同一规则的高度上限：Offset/Radius 固定在样式里（换 Shadow 对象=重建合成层），
        // 这里只发颜色，高度/明暗变了是颜色变，不是几何变。
        res[DialogShadow] = ShadowInk(dialog);
    }

    /// <summary>光向归一化。-1..1 的入参一律接住（旧配置里可能是任意值），长度为 0 时回到左上。</summary>
    private static (double Dx, double Dy) Direction(double lightX, double lightY)
    {
        var lx = Math.Clamp(double.IsFinite(lightX) ? lightX : FixedLightX, -1, 1);
        var ly = Math.Clamp(double.IsFinite(lightY) ? lightY : FixedLightY, -1, 1);
        var len = Math.Sqrt(lx * lx + ly * ly);
        if (len < 0.001) { lx = FixedLightX; ly = FixedLightY; len = Math.Sqrt(lx * lx + ly * ly); }
        return (lx / len, ly / len);
    }

    private static double Clamp01(double v) => double.IsFinite(v) ? Math.Clamp(v, 0, 1) : 0;

    /// <summary>
    /// 极淡色散：把一颗颜色向另一个色相拉一点点。2% 是刻意的上限 ——
    /// 玻璃边上的色散在真实世界里就是「看得出有，量不出多少」，再多就成了霓虹管。
    /// </summary>
    private static Color ColorBurn(Color c, uint hex, double amount)
    {
        var k = (float)Clamp01(amount);
        var tint = Color.FromArgb("#" + hex.ToString("X6"));
        return new Color(
            Math.Clamp(c.Red + (tint.Red - c.Red) * k, 0f, 1f),
            Math.Clamp(c.Green + (tint.Green - c.Green) * k, 0f, 1f),
            Math.Clamp(c.Blue + (tint.Blue - c.Blue) * k, 0f, 1f),
            c.Alpha);
    }

    /// <summary>只压暗、不动透明度：接触阴影要读成「同一块材料的背光面」，不是一层灰纱。</summary>
    private static Color Shade(Color c, double amount)
    {
        var k = (float)Clamp01(amount);
        return new Color(
            Math.Clamp(c.Red * (1 - 0.85f * k), 0f, 1f),
            Math.Clamp(c.Green * (1 - 0.88f * k), 0f, 1f),
            Math.Clamp(c.Blue * (1 - 0.92f * k), 0f, 1f),
            c.Alpha);
    }

    /// <summary>
    /// 关闭玻璃时的面台阶：往暖褐方向沉，而不是单纯降明度 —— 直接乘同一个系数会变灰，
    /// 看着像被禁用而不是被按住。amount 分档用：按钮静止 0.55 / 悬停 0.80 / 按下 1.00。
    /// </summary>
    private static Color Sink(Color c, double amount)
    {
        var k = (float)Clamp01(amount);
        return new Color(
            Math.Clamp(c.Red * (1 - 0.10f * k), 0f, 1f),
            Math.Clamp(c.Green * (1 - 0.14f * k), 0f, 1f),
            Math.Clamp(c.Blue * (1 - 0.18f * k), 0f, 1f),
            c.Alpha);
    }

    private static SolidColorBrush Solid(Color c) => new(c);

    private static Color ReadColor(ResourceDictionary res, string key, string fallback)
        => res.TryGetValue(key, out var v) && v is Color c ? c : Color.FromArgb(fallback);
}
