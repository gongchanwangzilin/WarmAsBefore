namespace WarmAsBefore.DesignSystem.Theme;

/// <summary>
/// 把玻璃档位 + 三个调制量换算成一组画刷/阴影资源，写进 Application.Resources。
/// 组件样式（卡片 / 按钮 / 上下栏 / 弹窗）用 {DynamicResource ...} 引用这些键。
///
/// 设计取向：玻璃 = 表面材质，长在 chrome 上（顶栏/底栏、按钮、卡片、弹窗）。
///
/// ⚠ 半透明本身**不足以**让人看出玻璃 —— 一块 45% 白压在同色页背景上，看起来和实心板一样。
/// 真正让玻璃成立的是三件事，这里都做了：
///   1. 高度感：随「液态高度」变化的投影（Offset/Radius/Opacity 一起长）
///   2. 光影感：上沿亮、下沿暖暗的**渐变描边**（光从左上打过来）
///   3. 通透度：由「透明度」统一控制的 alpha
/// 另外「磨砂程度」控制奶白雾气浓度 —— 越高越浑，越低越像清玻璃。
///
/// 三个调制量（都由设置页滑杆控制）：
///   translucency 0→1  0 = 表面拉回不透明（等同于关闭玻璃）
///   frost        0→1  0 = 清玻璃，1 = 厚磨砂
///   height       0→1  0 = 贴在页面上，1 = 明显浮起
/// reduced=true（减弱透明降级）优先级最高：一律不透明，保证正文 4.5:1 对比。
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

    /// <summary>透明度=0 时表面收缩到的 alpha（接近不透明）。</summary>
    private const double OpaqueAlpha = 0.94;

    public static void Publish(string tier, double translucency, double frost, double height, bool reduced)
    {
        var app = Application.Current;
        if (app is null) return;

        var res = app.Resources;
        var surface = ReadColor(res, "SurfaceBg", "#FDF8F0");
        var t = Math.Clamp(translucency, 0, 1);
        var f = Math.Clamp(frost, 0, 1);
        var h = Math.Clamp(height, 0, 1);

        // 底色：alpha 走透明度，RGB 往白里掺（磨砂雾气）
        Color Bg(Color c)
        {
            var r = (float)(c.Red + (1 - c.Red) * f);
            var g = (float)(c.Green + (1 - c.Green) * f);
            var b = (float)(c.Blue + (1 - c.Blue) * f);
            if (reduced) return new Color(r, g, b, 1f);
            var a = c.Alpha * t + OpaqueAlpha * (1 - t);
            return new Color(r, g, b, (float)Math.Clamp(a, 0, 1));
        }
        // 描边：只调 alpha，不改色（上沿的亮/下沿的暖要靠色相体现）
        Color Edge(Color c)
        {
            if (reduced) return new Color(c.Red, c.Green, c.Blue, 1f);
            var a = c.Alpha * t;
            return new Color(c.Red, c.Green, c.Blue, (float)Math.Clamp(a, 0, 1));
        }

        res[BarBg] = BarBrush(tier, surface, Bg);
        res[BarStroke] = EdgeBrush(tier, bar: true, Edge);
        res[BarStrokeThickness] = StrokeWidth(tier, bar: true);
        res[BarShadow] = ShadowFor(h, bar: true);

        res[CardBg] = CardBrush(tier, surface, Bg);
        res[CardStroke] = EdgeBrush(tier, bar: false, Edge);
        res[CardStrokeThickness] = StrokeWidth(tier, bar: false);
        res[CardShadow] = ShadowFor(h, bar: false);

        res[ButtonBg] = ButtonBrush(tier, surface, Bg);
        res[ButtonStroke] = Edge(ButtonStrokeColor(tier));
    }

    /// <summary>
    /// 高度感：投影随「液态高度」一起长。
    /// h=0 几乎贴面（细而淡）；h=1 明显浮起（远而深）。
    /// </summary>
    private static Shadow ShadowFor(double h, bool bar)
    {
        var scale = bar ? 0.6 : 1.0;            // 通栏阴影比卡片收敛一点
        return new Shadow
        {
            Brush = Colors.Black,
            Offset = new Point(0, (float)((1.5 + 7.5 * h) * scale)),
            Radius = (float)((4 + 20 * h) * scale),
            Opacity = (float)((0.10 + 0.26 * h) * scale)
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
    /// 光影感：上沿亮 → 中段几乎无 → 下沿暖暗的**渐变**描边。
    /// 这是让平面看起来有厚度、有光源的关键 —— 单色描边给不了这种「上下沿」。
    /// </summary>
    private static Brush EdgeBrush(string tier, bool bar, Func<Color, Color> Edge)
    {
        if (tier == "none") return Solid(Colors.Transparent);

        var top = tier switch { "frost" => "#59FFFFFF", "glass" => "#B3FFFFFF", _ => "#E6FFFFFF" };
        var mid = tier switch { "frost" => "#14FFFFFF", "glass" => "#24FFFFFF", _ => "#33FFFFFF" };
        var bottom = tier switch { "frost" => "#1F8B7D6B", "glass" => "#3D8B7D6B", _ => "#668B7D6B" };
        if (bar)
        {
            // 通栏只需要一条上沿高光，下沿用更淡的
            bottom = "#1F8B7D6B";
        }

        return new LinearGradientBrush(
            new GradientStopCollection
            {
                new() { Color = Edge(C(top)), Offset = 0f },
                new() { Color = Edge(C(mid)), Offset = 0.5f },
                new() { Color = Edge(C(bottom)), Offset = 1f }
            },
            new Point(0, 0), new Point(0.35, 1));
    }

    private static double StrokeWidth(string tier, bool bar) => tier switch
    {
        "none" => 0,
        "liquid" => bar ? 1 : 1.5,
        _ => 1
    };

    /// <summary>按钮描边：关掉玻璃时保留原来的暖色描边，开启后转成白色反光。</summary>
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

    /// <summary>从主题字典里取颜色（取不到就用兜底值），保证跟随配色主题。</summary>
    private static Color ReadColor(ResourceDictionary res, string key, string fallback)
        => res.TryGetValue(key, out var v) && v is Color c ? c : C(fallback);
}
