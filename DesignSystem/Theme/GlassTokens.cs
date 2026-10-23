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
/// 把玻璃档位 + 各调制量换算成画刷/阴影资源，写进 Application.Resources。
/// 组件样式（卡片 / 按钮 / 上下栏 / 弹窗）用 {DynamicResource ...} 引用这些键。
///
/// 让玻璃成立的三件事：
///   1. 高度感：投影随「液态高度」变化
///   2. 光影感：**由三维光源决定的**渐变描边（上沿亮、背光侧暗）
///   3. 通透度 + 磨砂浓度
///
/// 光源 (LightX, LightY, LightZ) + 光宽 (LightWidth) 一起作用：
///   · X/Y（-1..1，屏幕平面方向）→ 决定高光落在哪条边，以及投影往哪边投
///   · Z（0..1，离表面的高度）    → 越高，高光越柔越淡、投影越散
///   · LightWidth（0..1）         → 高光的宽窄：窄 = 一道锐利反光，宽 = 大面积漫射
/// </summary>
public static class GlassTokens
{
    public const string BarBg = "GlassBarBg";
    public const string BarStroke = "GlassBarStroke";
    public const string BarStrokeThickness = "GlassBarStrokeThickness";
    public const string CardBg = "GlassCardBg";
    public const string CardStroke = "GlassCardStroke";
    public const string CardStrokeThickness = "GlassCardStrokeThickness";
    public const string ButtonBg = "GlassButtonBg";
    public const string ButtonBgHover = "GlassButtonBgHover";
    public const string ButtonBgPressed = "GlassButtonBgPressed";
    public const string ButtonStroke = "GlassButtonStroke";
    public const string CardShadow = "GlassCardShadow";
    public const string BarShadow = "GlassBarShadow";

    /// <summary>轻量模式：不发布阴影。由 RuntimeConfigurator 按用户设置下发。</summary>
    public static bool LightMode { get; set; }

    private const double OpaqueAlpha = 0.94;

    /// <summary>
    /// 玻璃的三个色标 + 边的三个色标，全部来自 StyleWorldTokens。
    /// 这些值原来是写死的奶油系 hex（#FDF8F0 / #FFFFFF / #8B7D6B），于是换成晨雾蓝灰、
    /// 翠竹绿或深色档时，玻璃卡片会变成一块不属于当前世界的奶油 —— 材质算法没错，颜色串了门。
    /// 现在 GlassTokens 只管「哪一档多透、边多亮、影多远」，颜色一律由世界提供。
    /// </summary>
    private readonly record struct Tints(Color Top, Color Mid, Color Low, Color EdgeHi, Color EdgeMid, Color EdgeLo);

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

        var t = Math.Clamp(p.Translucency, 0, 1);
        var f = Math.Clamp(p.Frost, 0, 1);
        var h = Math.Clamp(p.Height, 0, 1);
        var w = Math.Clamp(p.LightWidth, 0, 1);
        var z = Math.Clamp(p.LightZ, 0, 1);

        // 光在屏幕平面内的方向（归一化）。默认左上 → 高光在左上、影子投右下。
        var lx = Math.Clamp(p.LightX, -1, 1);
        var ly = Math.Clamp(p.LightY, -1, 1);
        var len = Math.Sqrt(lx * lx + ly * ly);
        if (len < 0.001) { lx = -0.7; ly = -0.7; len = Math.Sqrt(lx * lx + ly * ly); }
        var dx = lx / len;
        var dy = ly / len;

        Color Bg(Color c)
        {
            var r = (float)(c.Red + (1 - c.Red) * f);
            var g = (float)(c.Green + (1 - c.Green) * f);
            var b = (float)(c.Blue + (1 - c.Blue) * f);
            if (p.Reduced) return new Color(r, g, b, 1f);
            var a = c.Alpha * t + OpaqueAlpha * (1 - t);
            return new Color(r, g, b, (float)Math.Clamp(a, 0, 1));
        }
        Color Edge(Color c)
        {
            if (p.Reduced) return new Color(c.Red, c.Green, c.Blue, 1f);
            var a = c.Alpha * t * (1 - 0.35 * z);   // 光越高，高光越柔
            return new Color(c.Red, c.Green, c.Blue, (float)Math.Clamp(a, 0, 1));
        }

        res[BarBg] = BarBrush(p.Tier, surface, tint, Bg);
        res[BarStroke] = EdgeBrush(p.Tier, bar: true, tint, Edge, dx, dy, w);
        res[BarStrokeThickness] = StrokeWidth(p.Tier, bar: true);
        res[BarShadow] = noShadow ? null : ShadowFor(h, z, dx, dy, bar: true);

        res[CardBg] = CardBrush(p.Tier, surface, tint, Bg);
        res[CardStroke] = EdgeBrush(p.Tier, bar: false, tint, Edge, dx, dy, w);
        res[CardStrokeThickness] = StrokeWidth(p.Tier, bar: false);
        // 诊断/降级开关：WARM_NO_SHADOW=1 时不发布卡片阴影。
        // 阴影在 WinUI 上是每个元素一个合成层，卡片多时既是首屏成本也是重排成本；
        // 低配机器或排查卡顿时可用它换流畅。
        res[CardShadow] = noShadow ? null : ShadowFor(h, z, dx, dy, bar: false);

        res[ButtonBg] = ButtonBrush(p.Tier, surface, tint, Bg);
        // 悬停/按下各发一份，压暗程度分两级：静止 → 悬停（三成）→ 按下（十成）。
        // 桌面端悬停是主要的「我能点」信号，只靠 1px 描边加粗在实测里根本看不出来。
        res[ButtonBgHover] = ButtonBrush(p.Tier, surface, tint, c => Sink(Bg(c), 0.32));
        // 按下态另发一份：按钮样式在 Pressed 视觉状态里引用它，才能既压暗又不脱离当前玻璃档位。
        res[ButtonBgPressed] = ButtonBrush(p.Tier, surface, tint, c => Sink(Bg(c), 1.0));
        if (p.Tier == "none")
        {
            // 「关闭玻璃」过去连按钮底色一起关掉：静止态 = 页面底色，描边 = Transparent，
            // 一整片按钮和背景同色，用户看到的是「按键没有边框，和背景合为一体」。
            // 关掉的应该只有通透和阴影，可点击性不在此列，所以平档自己给三档深度。
            res[ButtonBg] = Solid(Sink(surface, 0.60));
            res[ButtonBgHover] = Solid(Sink(surface, 0.85));
            res[ButtonBgPressed] = Solid(Sink(surface, 1.00));
        }
        // 描边取主题的 BorderLine，不再是半透明白：这套材质近白，白描边等于没有描边。
        // 实测静止按钮面 #F7F9FB 对页面 #EFF2F5 只有 1.06:1，边界全靠这一圈线撑。
        res[ButtonStroke] = ReadColor(res, "BorderLine", "#D8CFC0");
    }

    /// <summary>
    /// 高度感 + 光源方向：影子永远投向光的反方向。
    /// 高度越高影子越远越深；光越高（z）影子越散越淡。
    ///
    /// 卡片与上下栏不再共用一条曲线：卡片是这套界面里的「浮档」（CardNeumorph），
    /// 栏是贴在边缘的结构件。以前两者只差一个 0.6 的等比缩放，实测卡片和旁边的平档
    /// 卡完全分不出高低 —— 现在卡片按 raised 走远、走散，栏维持原来的浅而紧。
    /// </summary>
    private static Shadow ShadowFor(double h, double z, double dx, double dy, bool bar)
    {
        var reach = bar ? (1.5 + 6.0 * h) : (3.0 + 13.0 * h);      // 影子离物体走多远
        var radius = bar ? (4 + 16 * h) * (1 + 0.6 * z) : (8 + 30 * h) * (1 + 0.5 * z);
        var opacity = bar ? (0.10 + 0.20 * h) * (1 - 0.35 * z) : (0.14 + 0.30 * h) * (1 - 0.30 * z);
        return new Shadow
        {
            Brush = Colors.Black,
            Offset = new Point((float)(-dx * reach), (float)(-dy * reach)),
            Radius = (float)radius,
            Opacity = (float)opacity
        };
    }

    // 每一档只规定「三个色标各多透」；色标本身由世界给（见 Tints）。
    private static Brush BarBrush(string tier, Color surface, Tints t, Func<Color, Color> Bg) => tier switch
    {
        "frost" => Solid(Bg(Alpha(t.Top, 0.70))),
        "glass" => Gradient(Bg, (Alpha(t.Top, 0.85), 0.0), (Alpha(t.Mid, 0.65), 1.0)),
        "liquid" => Gradient(Bg, (Alpha(t.Top, 0.90), 0.0), (Alpha(t.Mid, 0.75), 0.55), (Alpha(t.Low, 0.60), 1.0)),
        _ => new SolidColorBrush(surface)
    };

    private static Brush CardBrush(string tier, Color surface, Tints t, Func<Color, Color> Bg) => tier switch
    {
        "frost" => Solid(Bg(Alpha(t.Top, 0.55))),
        "glass" => Gradient(Bg, (Alpha(t.Top, 0.70), 0.0), (Alpha(t.Mid, 0.45), 1.0)),
        "liquid" => Gradient(Bg, (Alpha(t.Top, 0.80), 0.0), (Alpha(t.Mid, 0.55), 0.55), (Alpha(t.Low, 0.35), 1.0)),
        _ => new SolidColorBrush(surface)
    };

    private static Brush ButtonBrush(string tier, Color surface, Tints t, Func<Color, Color> Bg) => tier switch
    {
        "frost" => Solid(Bg(Alpha(t.Top, 0.60))),
        "glass" => Gradient(Bg, (Alpha(t.Top, 0.70), 0.0), (Alpha(t.Mid, 0.50), 1.0)),
        "liquid" => Gradient(Bg, (Alpha(t.Top, 0.80), 0.0), (Alpha(t.Mid, 0.60), 0.55), (Alpha(t.Low, 0.40), 1.0)),
        _ => new SolidColorBrush(surface)
    };

    /// <summary>
    /// 光影感：描边是**渐变**，方向由光源 X/Y 决定 —— 迎光的那条边亮，背光侧暖暗。
    /// 光宽决定高光多集中：窄光是一道锐利反光，宽光是整片漫射。
    /// </summary>
    private static Brush EdgeBrush(string tier, bool bar, Tints t, Func<Color, Color> Edge,
        double dx, double dy, double width)
    {
        if (tier == "none") return Solid(Colors.Transparent);

        var top = tier switch { "frost" => 0.35, "glass" => 0.70, _ => 0.90 };
        var mid = tier switch { "frost" => 0.08, "glass" => 0.14, _ => 0.20 };
        var bottom = tier switch { "frost" => 0.12, "glass" => 0.24, _ => 0.40 };
        if (bar) bottom = 0.12;

        // 渐变沿光的方向铺开：起点在迎光侧，终点在背光侧
        var start = new Point(0.5 - dx * 0.5, 0.5 - dy * 0.5);
        var end = new Point(0.5 + dx * 0.5, 0.5 + dy * 0.5);

        // 高光落点：窄光靠前（锐利），宽光铺开（漫射）
        var highlightAt = (float)(0.12 + width * 0.6);

        return new LinearGradientBrush(
            new GradientStopCollection
            {
                new() { Color = Edge(Alpha(t.EdgeHi, top)), Offset = 0f },
                new() { Color = Edge(Alpha(t.EdgeMid, mid)), Offset = highlightAt },
                new() { Color = Edge(Alpha(t.EdgeLo, bottom)), Offset = 1f }
            },
            start, end);
    }

    private static double StrokeWidth(string tier, bool bar) => tier switch
    {
        "none" => 0,
        "liquid" => bar ? 1 : 1.5,
        _ => 1
    };

    private static Color C(string hex) => Color.FromArgb(hex);

    /// <summary>只换透明度、不动色值：色标由世界给，这里只管这一档该多透。</summary>
    private static Color Alpha(Color c, double a) => new(c.Red, c.Green, c.Blue, (float)Math.Clamp(a, 0, 1));

    /// <summary>
    /// 按下压暗：往暖褐方向沉，而不是单纯降明度——这套材质是奶油 + 暖光，
    /// 直接乘同一个系数会变灰，看着像被禁用而不是被按住。
    /// amount 分档用：悬停只要三成就够，按下一成。
    /// </summary>
    private static Color Sink(Color c, double amount)
    {
        var k = (float)Math.Clamp(amount, 0d, 1d);
        return new Color(
            Math.Clamp(c.Red * (1 - 0.10f * k), 0f, 1f),
            Math.Clamp(c.Green * (1 - 0.14f * k), 0f, 1f),
            Math.Clamp(c.Blue * (1 - 0.18f * k), 0f, 1f),
            c.Alpha);
    }

    private static SolidColorBrush Solid(Color c) => new(c);

    private static LinearGradientBrush Gradient(Func<Color, Color> Bg, params (Color color, double offset)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (color, offset) in stops)
            b.GradientStops.Add(new GradientStop { Color = Bg(color), Offset = (float)offset });
        return b;
    }

    private static Color ReadColor(ResourceDictionary res, string key, string fallback)
        => res.TryGetValue(key, out var v) && v is Color c ? c : C(fallback);
}
