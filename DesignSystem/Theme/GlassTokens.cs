namespace WarmAsBefore.DesignSystem.Theme;

/// <summary>
/// 把「磨砂 / 毛玻璃 / 液态玻璃」三档 + 透明度 + 减弱透明降级，换算成一组画刷资源，
/// 写进 Application.Resources。组件样式（卡片 / 按钮 / 上下栏 / 弹窗）用
/// {DynamicResource ...} 引用这些键，切换档位时所有玻璃表面一起变，
/// 而**不需要在内容之上再盖一层**。
///
/// 设计取向（Apple Liquid Glass）：玻璃是「表面材质」，长在 chrome 上 ——
/// 顶栏/底栏、按钮、卡片、弹窗；正文永远画在不透明画布上。
///
/// 档位梯度（同一套材质，逐档增加「透」与「亮边」）：
///   none   不透明表面，无描边
///   frost  轻微半透明 + 极淡描边
///   glass  竖向高光渐变 + 亮描边
///   liquid 更亮的高光 + 暖色收底 + 更强描边
///
/// 两个调制量：
///   translucency 0→1：0 时所有表面被拉回接近不透明，1 时用档位设计值。
///   reduced=true：无障碍「减弱透明降级」，表面一律不透明，保证正文 4.5:1 对比。
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

    /// <summary>透明度=0 时表面收缩到的 alpha（接近不透明，但还留一丝材质感）。</summary>
    private const double OpaqueAlpha = 0.94;

    public static void Publish(string tier, double translucency, bool reduced)
    {
        var app = Application.Current;
        if (app is null) return;

        var res = app.Resources;
        var surface = ReadColor(res, "SurfaceBg", "#FDF8F0");
        var t = Math.Clamp(translucency, 0, 1);

        Color A(Color c) => Adjust(c, t, reduced);

        res[BarBg] = BarBrush(tier, surface, A);
        res[BarStroke] = Solid(A(StrokeColor(tier, bar: true)));
        res[BarStrokeThickness] = StrokeWidth(tier, bar: true);

        res[CardBg] = CardBrush(tier, surface, A);
        res[CardStroke] = Solid(A(StrokeColor(tier, bar: false)));
        res[CardStrokeThickness] = StrokeWidth(tier, bar: false);

        res[ButtonBg] = ButtonBrush(tier, surface, A);
        res[ButtonStroke] = A(ButtonStrokeColor(tier));
    }

    /// <summary>
    /// 透明度调制：t=1 用档位设计 alpha；t=0 拉回接近不透明。
    /// 减弱透明降级优先级最高 —— 直接全不透明。
    /// </summary>
    private static Color Adjust(Color c, double t, bool reduced)
    {
        if (reduced) return new Color(c.Red, c.Green, c.Blue, 1f);
        var a = c.Alpha * t + OpaqueAlpha * (1 - t);
        return new Color(c.Red, c.Green, c.Blue, (float)Math.Clamp(a, 0, 1));
    }

    private static Brush BarBrush(string tier, Color surface, Func<Color, Color> A) => tier switch
    {
        "frost" => Solid(A(C("#B3FDF8F0"))),                                    // 0.70
        "glass" => Gradient(A, ("#D9FFFFFF", 0.0), ("#A6FDF6EE", 1.0)),          // 0.85→0.65
        "liquid" => Gradient(A, ("#E6FFFFFF", 0.0), ("#BFFDF6EE", 0.55), ("#99E8DCC8", 1.0)),
        _ => new SolidColorBrush(surface)
    };

    private static Brush CardBrush(string tier, Color surface, Func<Color, Color> A) => tier switch
    {
        "frost" => Solid(A(C("#8CFEFCF8"))),                                    // 0.55
        "glass" => Gradient(A, ("#B3FFFFFF", 0.0), ("#73FDF6EE", 1.0)),          // 0.70→0.45
        "liquid" => Gradient(A, ("#CCFFFFFF", 0.0), ("#8CFDF6EE", 0.55), ("#59E8DCC8", 1.0)),
        _ => new SolidColorBrush(surface)
    };

    private static Brush ButtonBrush(string tier, Color surface, Func<Color, Color> A) => tier switch
    {
        "frost" => Solid(A(C("#99FEFCF8"))),                                    // 0.60
        "glass" => Gradient(A, ("#B3FFFFFF", 0.0), ("#80FDF6EE", 1.0)),          // 0.70→0.50
        "liquid" => Gradient(A, ("#CCFFFFFF", 0.0), ("#99FDF6EE", 0.55), ("#66E8DCC8", 1.0)),
        _ => new SolidColorBrush(surface)
    };

    /// <summary>描边色：越往液态，上沿反光越亮（这就是「上下沿」的玻璃边）。</summary>
    private static Color StrokeColor(string tier, bool bar) => tier switch
    {
        "frost" => C(bar ? "#26FFFFFF" : "#33FFFFFF"),
        "glass" => C(bar ? "#59FFFFFF" : "#66FFFFFF"),
        "liquid" => C(bar ? "#8CFFFFFF" : "#99FFFFFF"),
        _ => Colors.Transparent
    };

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

    private static LinearGradientBrush Gradient(Func<Color, Color> A, params (string hex, double offset)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (hex, offset) in stops)
            b.GradientStops.Add(new GradientStop { Color = A(C(hex)), Offset = (float)offset });
        return b;
    }

    /// <summary>从主题字典里取颜色（取不到就用兜底值），保证跟随配色主题。</summary>
    private static Color ReadColor(ResourceDictionary res, string key, string fallback)
        => res.TryGetValue(key, out var v) && v is Color c ? c : C(fallback);
}
