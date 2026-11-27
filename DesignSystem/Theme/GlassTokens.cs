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
///   Card    中   —— 卡片（Mica Alt）：一块板，1.9~2.5px 的倒角边 + 一圈贴地的接触影
///   Button  厚   —— 按钮：胶质，顶面有高光带、底面有侧壁暗带、边缘是最宽的倒角
///   Dialog  最厚 —— 弹窗：最实、最暗的侧壁、最大的接触影，因为它是模态的
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
/// 四条规则不变，只是取值按「有真糊底 + 一块有厚度的板」重标定：
///   1. 描边是**倒角**，不是一根线：迎光端（上沿）1.5~2.5px 最亮 → 一道窄折射缝 → 一条内收的暗棱
///      → 背光端（下沿）更暗更厚。暗色在眼睛里比亮色厚一档，所以这条渐变读起来是「光在弧面上滚过去」，
///      而不是「一个盒子」。两端的 2% 品红 / 青仍是极淡的色散（测得出、看不出）。
///      MAUI 的 Border 只有一条均匀宽度的描边，没法真的让下沿更粗，也没法再叠一层内环 ——
///      所以「倒角 → 侧壁」这两层全靠这条渐变上的明暗台阶表达（见 <see cref="Publish"/> 里的 EdgeOf）。
///   2. 一条**接触阴影**规则：厚度越大，影越紧、越暗、越贴地（不是越飘越远）。同排相邻的两块
///      因此各有各的一圈影，中间那道极窄的缝就是两块板互相靠着的证据。
///   3. 一套圆角阶梯：4（小控件）/ 8（卡片、按钮）/ 12（栏、弹窗）。
///   4. 三档厚度 + 弹窗：面越厚，alpha 越高、高光越窄、边越宽越暗、影越紧。
///      **高度 0 也留 0.35 的板厚** —— 默认就是有高度的，「平」不是这一套材质的默认状态。
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
    public const string ButtonStrokeHot = "GlassButtonStrokeHot";
    public const string ButtonStrokeThickness = "GlassButtonStrokeThickness";
    public const string ButtonRingWidthSm = "GlassButtonRingWidthSm";
    /// <summary>Button.BorderColor 只能吃 Color（不吃 Brush）：方向光在这里退化成一颗中间色。</summary>
    public const string ButtonRingColor = "GlassButtonRing";
    public const string ButtonRingColorHot = "GlassButtonRingHot";
    /// <summary>外发光：Shadow.Brush，白色低不透明度。轻量/降级模式发全透明（Shadow 对象不动）。</summary>
    public const string ButtonGlow = "GlassButtonGlow";
    /// <summary>强调色外发光：信号钮用它，让「唯一响的那颗」在透明玻璃上仍然最亮。</summary>
    public const string ButtonGlowAccent = "GlassButtonGlowAccent";

    public const string DialogBg = "GlassDialogBg";
    public const string DialogStroke = "GlassDialogStroke";
    public const string DialogStrokeThickness = "GlassDialogStrokeThickness";
    public const string DialogShadow = "GlassDialogShadow";

    /// <summary>页面里那条「1px 发丝线」的数值版：页面 XAML 不再自带描边数值。</summary>
    public const string HairlineKey = "GlassHairline";

    // ── 规则常量 ────────────────────────────────────────────────────────

    /// <summary>发丝线的定数：栏与分隔线永远是 1px，粗一分就变成盒子。</summary>
    public const double Hairline = 1.0;

    /// <summary>
    /// 按钮亮环的宽度（液态玻璃，2026-11-27）：2.5~3.5 之间取 3。
    /// 环是这套材质立身之本 —— 面几乎全透，按钮之所以还看得见，全靠这一圈和它的外发光。
    /// 小号按钮（34 高）用 <see cref="RingWidthSm"/>：同样的 3px 压在一颗矮钮上会把标签挤扁。
    /// </summary>
    public const double RingWidth = 3.0;
    public const double RingWidthSm = 2.5;

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
    /// 上一次下发的液态高度（0..1）。<see cref="ThicknessOf"/> 与摩擦音要按它算厚度，
    /// 所以它必须留在这里 —— 声音与画面读的是同一个数，不许另抄一份设置或一张档位表。
    /// </summary>
    private static double _height = 0.5;

    /// <summary>
    /// 板厚函式：高度 0 也留 0.35 的厚度。玻璃是一块**板**，不是一张纸 ——
    /// 「最薄」只是薄，不是平；用户可以把它调到最薄，但不该调到「没有边」。
    /// </summary>
    private static double Depth(double height) => 0.35 + 0.65 * Clamp01(height);

    /// <summary>
    /// 档位的墙厚倍率：栏是一根条，按钮比卡片还薄一档（它的倒角本来就只有 1.5px 基准），
    /// 卡片是一块板，弹窗是最厚的那一块。
    /// 只有这一个阶梯 —— 描边宽度、侧壁暗带、接触阴影、摩擦音都乘它。
    /// </summary>
    private static double Wall(GlassKind kind) => kind switch
    {
        GlassKind.Bar => 0.78,
        GlassKind.Button => 0.88,
        GlassKind.Card => 0.95,
        _ => 1.15
    };

    /// <summary>
    /// 这一档材料此刻的厚度（0..1）：液态高度 × 档位墙厚，归一到「弹窗、最高」为 1。
    /// 描边的宽度与暗度、底部侧壁暗带的宽度、接触阴影的紧贴程度，以及摩擦音的
    /// 音高与衰减，全部从这一个数出来 —— 画面与声音讲的是同一件事。
    /// </summary>
    public static double ThicknessOf(GlassKind kind) => Clamp01(Depth(_height) * Wall(kind) / 1.15);

    /// <summary>
    /// 描边宽度：倒角本身要有宽度才读得出「弧面」。档位给基准（栏 1 / 卡 2 / 钮 1.5 / 弹窗 2），
    /// 液态高度再给一半的增量 —— 板越厚，那圈光滚过的弧面就越宽。
    /// </summary>
    private static double RingOf(Spec s, double height)
        => Math.Round(s.Stroke * (0.85 + 0.55 * Depth(height)), 2);

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
        _height = h;   // ThicknessOf（描边宽度 / 侧壁 / 接触阴影 / 摩擦音）读的就是这一份
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

        // 描边：同一根线上的渐变。起点压在迎光那一侧（左上），终点在背光那一侧（右下），
        // 所以「上沿亮、下沿暗」不是靠写两条边画出来的，而是光方向自己算出来的。
        // 四段：倒角高光 → 折射缝 → 内收的暗棱 → 侧壁落地。宽度随厚度长（RingOf），
        // 暗的那一半因此比亮的那一半厚 —— 这就是「一块有弧边的板」在一条描边里的全部表达。
        Brush EdgeOf(GlassKind kind, Spec s, double dispersion)
        {
            var wall = ThicknessOf(kind);
            var start = new Point(0.5 + dx * 0.5, 0.5 + dy * 0.5);
            var end = new Point(0.5 - dx * 0.5, 0.5 - dy * 0.5);
            var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
            // 亮边走到哪儿收：窄光是一道锐利反光（提前结束），宽光是整片漫射
            var lit = Clamp01(0.14 + w * 0.36);
            // ① 倒角：迎光那一小段弧最亮（白 + 2% 品红，色散只给 2%，多一档就变成霓虹边）
            brush.GradientStops.Add(new GradientStop
            {
                Color = Edge(ColorBurn(tint.EdgeHi, 0xFF2E9A, dispersion), s.EdgeLit),
                Offset = 0f
            });
            // ② 折射缝：亮边与暗边之间那道窄缝 —— 玻璃的边在这里「折了一下光」
            brush.GradientStops.Add(new GradientStop
            {
                Color = Edge(tint.EdgeMid, s.EdgeLit * 0.08),
                Offset = (float)lit
            });
            // ③ 棱：紧贴折射缝的一道暗折。外环亮、内收一档更暗 —— 这两层就是倒角与侧壁的分界。
            //    暗度随墙厚走：薄栏那一条压得很轻，弹窗那一条几乎是一圈墨线。
            brush.GradientStops.Add(new GradientStop
            {
                Color = Edge(tint.EdgeLo, (0.30 + 0.40 * wall) * (0.45 + 2.0 * s.EdgeShade)),
                Offset = (float)Clamp01(lit + 0.05)
            });
            // ④ 侧壁落地：整条环最暗的一段（黑 + 2.5% 青）。板越厚这一圈越暗，
            //    眼睛把那圈暗读成「材料的厚度」，而不是「阴影画错了」。
            brush.GradientStops.Add(new GradientStop
            {
                Color = Edge(ColorBurn(Colors.Black, 0x22D3EE, dispersion), s.EdgeShade * (1.0 + 1.4 * wall)),
                Offset = 1f
            });
            return brush;
        }

        Color Edge(Color c, double a)
        {
            if (p.Reduced) return new Color(c.Red, c.Green, c.Blue, 1f);
            return new Color(c.Red, c.Green, c.Blue, (float)Clamp01(a * (1 - 0.32 * z)));
        }

        // ── 面：一条左上到右下的斜渐变。五段是「镜面高光 → 顶面受光 → 主体 → 主体 → 底部侧壁」，
        //    这就是玻璃被读成「有厚度的一块」的全部几何线索。
        //    specShift 让镜面上下移动（悬停留给 HoverFeel 做 6% 滑动），bandK 控高光带宽窄，
        //    bodyK/contactK 是按下与悬停时的压暗/压紧。
        Brush Slab(GlassKind kind, Spec s, double specShift, double bandK, double bodyK, double contactK)
        {
            var wall = ThicknessOf(kind);
            var hiAt = Clamp01(s.SpecularAt + specShift);
            // 板越厚，顶面那道高光越窄：光被收进倒角里，铺不开（薄栏反而是一片漫射）
            var band = s.Band * bandK * (1.10 - 0.20 * wall);
            // 底部侧壁暗带：厚度越大越宽 —— 这一段的宽度就是「侧壁有多高」在面上的投影
            var wallAt = Clamp01(1.0 - (0.10 + 0.24 * wall));
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
            brush.GradientStops.Add(new GradientStop { Color = Face(tint.Mid, s.FaceBody * bodyK), Offset = (float)wallAt });
            // 侧壁：越厚压得越狠。Surface 上那圈亮边配这一整圈暗底，才是「板」而不是「纸」。
            brush.GradientStops.Add(new GradientStop
            {
                Color = Shade(Face(tint.Low, s.FaceLow * (0.9 + 0.1 * bodyK)),
                    s.Contact * contactK * (1.0 + 5.0 * wall)),
                Offset = 1f
            });
            return brush;
        }

        // ── 阴影：唯一的规则。厚度给紧贴与浓度，档位只乘一个权重，背光方向给朝向。
        //    厚度是**贴地**：板越厚，影越紧、越黑、扩散越小 —— 不是越飘越远。
        //    同排相邻的两块各自带一圈这样的影，中间那道极窄的暗缝就是两块板互相靠着的证据
        //    （缝本身不画，它是两圈影在缝里叠出来的）。
        Brush ShadowInk(GlassKind kind, Spec s)
        {
            if (noShadow) return new SolidColorBrush(Colors.Transparent);
            var opacity = Clamp01((0.15 + 0.24 * ThicknessOf(kind)) * (1 - 0.30 * z));
            return new SolidColorBrush(new Color(0f, 0f, 0f, (float)Clamp01(opacity * Math.Min(1.0, s.ShadowWeight))));
        }
        Shadow ShadowOf(GlassKind kind, Spec s)
        {
            var wall = ThicknessOf(kind);
            var reach = (1.2 + 4.0 * wall) * s.ShadowWeight;
            var radius = (9 + 20 * wall) * s.ShadowWeight * (1 + 0.5 * z);
            var opacity = Clamp01((0.15 + 0.24 * wall) * (1 - 0.30 * z));
            return new Shadow
            {
                Brush = Colors.Black,
                // 影子永远投向光的反方向；横向只走三分之一 —— 接触阴影是「压在地上」，
                // 不是斜着打出去的一道光。横向少走，两侧才留得下那圈暗缝。
                Offset = new Point((float)(-dx * reach * 0.33), (float)(-dy * reach)),
                Radius = (float)radius,
                Opacity = (float)opacity
            };
        }

        // ── 落笔：薄 / 中 / 厚 / 弹窗 ──────────────────────────────────────
        var bar = Of(GlassKind.Bar);
        var card = Of(GlassKind.Card);
        // 按钮不再走 Spec 那套「厚板」配方：它的面是透明的，见下面的 ClearFace / ButtonRing。
        var dialog = Of(GlassKind.Dialog);

        var hairline = flat ? 0.0 : Hairline;
        res[HairlineKey] = hairline;

        // 环宽：栏最窄（它只是一根条），卡片与弹窗最宽（倒角要有弧面才折得出光），按钮居中。
        // 宽度由档位基准 × 厚度给 —— 同一个高度下，越厚的那一档，那圈光滚过的路越长。
        res[BarBg] = flat ? Solid(Sink(surface, 0.03)) : Slab(GlassKind.Bar, bar, 0, 1, 1, 1);
        res[BarStroke] = flat ? Solid(Colors.Transparent) : EdgeOf(GlassKind.Bar, bar, 0.020);
        res[BarStrokeThickness] = flat ? 0.0 : RingOf(bar, h);
        res[BarShadow] = noShadow || flat ? null : ShadowOf(GlassKind.Bar, bar);

        res[CardBg] = flat ? Solid(surface) : Slab(GlassKind.Card, card, 0, 1, 1, 1);
        res[CardStroke] = flat ? Solid(Colors.Transparent) : EdgeOf(GlassKind.Card, card, 0.020);
        res[CardStrokeThickness] = flat ? 0.0 : RingOf(card, h);
        // 诊断/降级开关：WARM_NO_SHADOW=1 时不发布卡片阴影。
        // 阴影在 WinUI 上是每个元素一个合成层，卡片多时既是首屏成本也是重排成本；
        // 低配机器或排查卡顿时可用它换流畅。
        res[CardShadow] = noShadow || flat ? null : ShadowOf(GlassKind.Card, card);

        // ── 按钮：透明玻璃（2026-11-27，照 iOS 26 液态玻璃）──────────────────
        // 面几乎不画实色：白 7%~15%，按钮之所以还站得住，全靠外面那一圈亮环 + 一圈外发光。
        // 这里刻意**不走** Face() 那条「没有糊底就补到七成实」的兜底 —— 按钮从来不直接压在
        // 桌面上，它站在卡片或面板上，背后永远有东西可透；把它做实反而是上一版「一块白塑料板」的病根。
        Color Clear(Color c, double a)
        {
            if (p.Reduced) return new Color(c.Red, c.Green, c.Blue, 0.92f);
            return new Color(c.Red, c.Green, c.Blue, (float)Clamp01(a * (0.55 + 0.55 * t)));
        }

        // 面：左上那颗镜面 → 顶面 → 主体 → 侧壁。五段里只有前两段是「白」，后两段是板的暗面 ——
        // 「几乎看不到实色填充」指的是主体：它在 8% 上下，能读出「这里有一块玻璃」，看不出「这里有一块色块」。
        Brush ClearFace(double topA, double bodyA, double lowA, double hiAt, double band)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.45, 1) };
            brush.GradientStops.Add(new GradientStop { Color = Clear(tint.EdgeHi, topA * 1.20), Offset = 0f });
            brush.GradientStops.Add(new GradientStop { Color = Clear(tint.EdgeHi, topA), Offset = (float)Clamp01(hiAt) });
            brush.GradientStops.Add(new GradientStop
            {
                Color = Clear(tint.Mid, bodyA),
                Offset = (float)Clamp01(hiAt + band)
            });
            brush.GradientStops.Add(new GradientStop { Color = Clear(tint.Mid, bodyA), Offset = 0.74f });
            brush.GradientStops.Add(new GradientStop { Color = Clear(Shade(tint.Low, 0.20), lowA), Offset = 1f });
            return brush;
        }

        // 亮环：同一根描边上的四段渐变，沿光照方向走 —— 左上最亮（白 0.92），右下几乎消失（0.05）。
        // 第二段是「环的厚度」：亮端往里收一档，环内侧比外侧暗，读起来是一根被打磨过的棱，不是一条线。
        Brush ButtonRing(double litA, double darkA)
        {
            var start = new Point(0.5 + dx * 0.5, 0.5 + dy * 0.5);
            var end = new Point(0.5 - dx * 0.5, 0.5 - dy * 0.5);
            var brush = new LinearGradientBrush { StartPoint = start, EndPoint = end };
            brush.GradientStops.Add(new GradientStop { Color = RingInk(tint.EdgeHi, litA), Offset = 0f });
            brush.GradientStops.Add(new GradientStop { Color = RingInk(tint.EdgeHi, litA * 0.62), Offset = 0.32f });
            brush.GradientStops.Add(new GradientStop { Color = RingInk(tint.EdgeMid, litA * 0.16), Offset = 0.66f });
            brush.GradientStops.Add(new GradientStop { Color = RingInk(tint.EdgeLo, darkA), Offset = 1f });
            return brush;
        }

        // 环上的透明度：只按光高收一点点，不按 Reduced 做实 —— 一条实心暗环读起来是黑框，不是玻璃边。
        Color RingInk(Color c, double a) =>
            new(c.Red, c.Green, c.Blue, (float)Clamp01(a * (1 - 0.20 * z)));

        // Button.BorderColor 只吃一颗 Color（MAUI 的 Button 没有 BorderBrush）：方向光在这里退化成
        // 亮端与暗端的中间色。它在浅色地面上是一条浅灰棱、在深色地面上是一条发亮的棱 —— 两头都看得见。
        Color RingFlat(double a) => RingInk(Mix(tint.EdgeHi, tint.EdgeLo, 0.42), a);

        // 外发光：Shadow 的 Brush。白色低不透明度 + 样式里那颗大半径 Shadow 就是参考图里最显眼的那层光。
        Brush Glow(Color c, double a) => noShadow || flat
            ? new SolidColorBrush(Colors.Transparent)
            : new SolidColorBrush(new Color(c.Red, c.Green, c.Blue, (float)Clamp01(a)));

        var accent = ReadColor(res, "AccentWarm", "#FAD41F");

        res[ButtonBg] = flat ? Solid(Sink(surface, 0.55)) : ClearFace(0.14, 0.075, 0.13, 0.06, 0.34);
        res[ButtonBgHover] = flat ? Solid(Sink(surface, 0.80)) : ClearFace(0.19, 0.095, 0.16, 0.11, 0.30);
        res[ButtonBgPressed] = flat ? Solid(Sink(surface, 1.00)) : ClearFace(0.26, 0.12, 0.21, 0.02, 0.22);
        res[ButtonStroke] = flat ? Solid(ReadColor(res, "BorderLine", "#D8CFC0")) : ButtonRing(0.92, 0.05);
        res[ButtonStrokeHot] = flat ? Solid(ReadColor(res, "BorderLine", "#D8CFC0")) : ButtonRing(1.00, 0.16);
        // 环宽 3px（34 高的紧凑档 2.5px）：这是按钮在透明面上的唯一轮廓，1px 那档在这个材质里等于没有边。
        res[ButtonStrokeThickness] = flat ? 0.0 : RingWidth;
        res[ButtonRingWidthSm] = flat ? 0.0 : RingWidthSm;
        res[ButtonRingColor] = RingFlat(0.80);
        res[ButtonRingColorHot] = RingFlat(0.96);
        res[ButtonGlow] = Glow(tint.EdgeHi, 0.50);
        res[ButtonGlowAccent] = Glow(accent, 0.42);

        res[DialogBg] = flat ? Solid(Sink(surface, -0.04)) : Slab(GlassKind.Dialog, dialog, 0, 1, 1, 1);
        res[DialogStroke] = flat ? Solid(ReadColor(res, "EdgeStrong", "#DAC49E")) : EdgeOf(GlassKind.Dialog, dialog, 0.020);
        res[DialogStrokeThickness] = flat ? Hairline : RingOf(dialog, h);
        // 弹窗的影走同一规则的厚度上限：Offset/Radius 固定在样式里（换 Shadow 对象=重建合成层），
        // 这里只发颜色，厚度/明暗变了是颜色变，不是几何变。
        res[DialogShadow] = ShadowInk(GlassKind.Dialog, dialog);
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

    /// <summary>两色按比例混（只混 RGB）：环的单色版要把亮端与暗端对半折中，才在明暗两种地面上都看得见。</summary>
    private static Color Mix(Color a, Color b, double k)
    {
        var m = (float)Clamp01(k);
        return new Color(
            Math.Clamp(a.Red + (b.Red - a.Red) * m, 0f, 1f),
            Math.Clamp(a.Green + (b.Green - a.Green) * m, 0f, 1f),
            Math.Clamp(a.Blue + (b.Blue - a.Blue) * m, 0f, 1f),
            Math.Max(a.Alpha, b.Alpha));
    }

    private static Color ReadColor(ResourceDictionary res, string key, string fallback)
        => res.TryGetValue(key, out var v) && v is Color c ? c : Color.FromArgb(fallback);
}
