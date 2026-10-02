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
    public const string ButtonStroke = "GlassButtonStroke";
    public const string CardShadow = "GlassCardShadow";
    public const string BarShadow = "GlassBarShadow";

    private const double OpaqueAlpha = 0.94;

    public static void Publish(GlassParams p)
    {
        var app = Application.Current;
        if (app is null) return;

        var res = app.Resources;
        var surface = ReadColor(res, "SurfaceBg", "#FDF8F0");

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

        res[BarBg] = BarBrush(p.Tier, surface, Bg);
        res[BarStroke] = EdgeBrush(p.Tier, bar: true, Edge, dx, dy, w);
        res[BarStrokeThickness] = StrokeWidth(p.Tier, bar: true);
        res[BarShadow] = ShadowFor(h, z, dx, dy, bar: true);

        res[CardBg] = CardBrush(p.Tier, surface, Bg);
        res[CardStroke] = EdgeBrush(p.Tier, bar: false, Edge, dx, dy, w);
        res[CardStrokeThickness] = StrokeWidth(p.Tier, bar: false);
        res[CardShadow] = ShadowFor(h, z, dx, dy, bar: false);

        res[ButtonBg] = ButtonBrush(p.Tier, surface, Bg);
        res[ButtonStroke] = Edge(ButtonStrokeColor(p.Tier));
    }

    /// <summary>
    /// 高度感 + 光源方向：影子永远投向光的反方向。
    /// 高度越高影子越远越深；光越高（z）影子越散越淡。
    /// </summary>
    private static Shadow ShadowFor(double h, double z, double dx, double dy, bool bar)
    {
        var scale = bar ? 0.6 : 1.0;
        var reach = (1.5 + 7.5 * h) * scale;          // 影子长度
        return new Shadow
        {
            Brush = Colors.Black,
            Offset = new Point((float)(-dx * reach), (float)(-dy * reach)),
            Radius = (float)((4 + 20 * h) * (1 + 0.6 * z) * scale),
            Opacity = (float)((0.10 + 0.26 * h) * (1 - 0.35 * z) * scale)
        };
    }

    private static Brush BarBrush(string tier, Color surface, Func<Color, Color> Bg) => tier switch
    {
        "frost" => Solid(Bg(C("#B3FDF8F0"))),
        "glass" => Gradient(Bg, ("#D9FFFFFF", 0.0), ("#A6FDF6EE", 1.0)),
        "liquid" => Gradient(Bg, ("#E6FFFFFF", 0.0), ("#BFFDF6EE", 0.55), ("#99E8DCC8", 1.0)),
        _ => new SolidColorBrush(surface)
    };

    private static Brush CardBrush(string tier, Color surface, Func<Color, Color> Bg) => tier switch
    {
        "frost" => Solid(Bg(C("#8CFEFCF8"))),
        "glass" => Gradient(Bg, ("#B3FFFFFF", 0.0), ("#73FDF6EE", 1.0)),
        "liquid" => Gradient(Bg, ("#CCFFFFFF", 0.0), ("#8CFDF6EE", 0.55), ("#59E8DCC8", 1.0)),
        _ => new SolidColorBrush(surface)
    };

    private static Brush ButtonBrush(string tier, Color surface, Func<Color, Color> Bg) => tier switch
    {
        "frost" => Solid(Bg(C("#99FEFCF8"))),
        "glass" => Gradient(Bg, ("#B3FFFFFF", 0.0), ("#80FDF6EE", 1.0)),
        "liquid" => Gradient(Bg, ("#CCFFFFFF", 0.0), ("#99FDF6EE", 0.55), ("#66E8DCC8", 1.0)),
        _ => new SolidColorBrush(surface)
    };

    /// <summary>
    /// 光影感：描边是**渐变**，方向由光源 X/Y 决定 —— 迎光的那条边亮，背光侧暖暗。
    /// 光宽决定高光多集中：窄光是一道锐利反光，宽光是整片漫射。
    /// </summary>
    private static Brush EdgeBrush(string tier, bool bar, Func<Color, Color> Edge,
        double dx, double dy, double width)
    {
        if (tier == "none") return Solid(Colors.Transparent);

        var top = tier switch { "frost" => "#59FFFFFF", "glass" => "#B3FFFFFF", _ => "#E6FFFFFF" };
        var mid = tier switch { "frost" => "#14FFFFFF", "glass" => "#24FFFFFF", _ => "#33FFFFFF" };
        var bottom = tier switch { "frost" => "#1F8B7D6B", "glass" => "#3D8B7D6B", _ => "#668B7D6B" };
        if (bar) bottom = "#1F8B7D6B";

        // 渐变沿光的方向铺开：起点在迎光侧，终点在背光侧
        var start = new Point(0.5 - dx * 0.5, 0.5 - dy * 0.5);
        var end = new Point(0.5 + dx * 0.5, 0.5 + dy * 0.5);

        // 高光落点：窄光靠前（锐利），宽光铺开（漫射）
        var highlightAt = (float)(0.12 + width * 0.6);

        return new LinearGradientBrush(
            new GradientStopCollection
            {
                new() { Color = Edge(C(top)), Offset = 0f },
                new() { Color = Edge(C(mid)), Offset = highlightAt },
                new() { Color = Edge(C(bottom)), Offset = 1f }
            },
            start, end);
    }

    private static double StrokeWidth(string tier, bool bar) => tier switch
    {
        "none" => 0,
        "liquid" => bar ? 1 : 1.5,
        _ => 1
    };

    private static Color ButtonStrokeColor(string tier) => tier switch
    {
        "frost" => C("#59FFFFFF"),
        "glass" => C("#80FFFFFF"),
        "liquid" => C("#B3FFFFFF"),
        _ => C("#4DF5DFC0")
    };

    private static Color C(string hex) => Color.FromArgb(hex);

    private static SolidColorBrush Solid(Color c) => new(c);

    private static LinearGradientBrush Gradient(Func<Color, Color> Bg, params (string hex, double offset)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (hex, offset) in stops)
            b.GradientStops.Add(new GradientStop { Color = Bg(C(hex)), Offset = (float)offset });
        return b;
    }

    private static Color ReadColor(ResourceDictionary res, string key, string fallback)
        => res.TryGetValue(key, out var v) && v is Color c ? c : C(fallback);
}
