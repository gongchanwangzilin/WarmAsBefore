namespace WarmAsBefore.DesignSystem.Theme;

/// <summary>
/// 把「磨砂 / 毛玻璃 / 液态玻璃」三档换算成一组画刷资源，写进 Application.Resources。
/// 组件样式（卡片 / 按钮 / 上下栏 / 弹窗）用 {DynamicResource ...} 引用这些键，
/// 于是切换档位时所有玻璃表面一起变，而**不需要在内容之上再盖一层**。
///
/// 设计取向（Apple Liquid Glass）：玻璃是「表面材质」，长在 chrome 上 ——
/// 顶栏/底栏、按钮、卡片、弹窗；正文永远画在不透明的画布上。
/// 早期那版把整页罩一层膜的做法，既挡输入又压对比度，已废弃。
///
/// 档位梯度（同一套材质，逐档增加「透」与「亮边」）：
///   none   不透明表面，无描边           —— 关掉就是干净的实心界面
///   frost  轻微半透明 + 极淡描边         —— 能透出一点点底色
///   glass  竖向高光渐变 + 亮描边         —— 明显玻璃感，顶部有一道反光
///   liquid 更亮的高光 + 暖色收底 + 更强描边 —— 最强质感，边缘最清晰
/// </summary>
public static class GlassTokens
{
    // 资源键（组件样式按名字引用）
    public const string BarBg = "GlassBarBg";
    public const string BarStroke = "GlassBarStroke";
    public const string BarStrokeThickness = "GlassBarStrokeThickness";
    public const string CardBg = "GlassCardBg";
    public const string CardStroke = "GlassCardStroke";
    public const string CardStrokeThickness = "GlassCardStrokeThickness";
    public const string ButtonBg = "GlassButtonBg";
    public const string ButtonStroke = "GlassButtonStroke";

    private const string WarmFoot = "#C9BFA8";   // 暖色收底（配色里的 BeigeAccent 系）

    public static void Publish(string tier)
    {
        var app = Application.Current;
        if (app is null) return;

        var res = app.Resources;
        var surface = ReadColor(res, "SurfaceBg", "#FDF8F0");

        // 类型必须与目标属性完全一致：DynamicResource 在运行期直接赋值，不会再走类型转换器。
        //   Border.Stroke → Brush     Border.StrokeThickness → double
        //   Button.BorderColor → Color
        res[BarBg] = BarBrush(tier, surface);
        res[BarStroke] = StrokeBrush(tier, bar: true);
        res[BarStrokeThickness] = StrokeWidth(tier, bar: true);

        res[CardBg] = CardBrush(tier, surface);
        res[CardStroke] = StrokeBrush(tier, bar: false);
        res[CardStrokeThickness] = StrokeWidth(tier, bar: false);

        res[ButtonBg] = ButtonBrush(tier, surface);
        res[ButtonStroke] = ButtonStrokeColor(tier);
    }

    private static Brush BarBrush(string tier, Color surface) => tier switch
    {
        "frost" => Solid("#D9FDF8F0"),
        "glass" => Gradient(("#F2FFFFFF", 0.0), ("#DDFDF6EE", 1.0)),
        "liquid" => Gradient(("#FFFFFF", 0.0), ("#E8FDF6EE", 0.55), ("#D6E8DCC8", 1.0)),
        _ => new SolidColorBrush(surface)
    };

    private static Brush CardBrush(string tier, Color surface) => tier switch
    {
        "frost" => Solid("#D9FEFCF8"),
        "glass" => Gradient(("#E6FFFFFF", 0.0), ("#CCFDF6EE", 1.0)),
        "liquid" => Gradient(("#F2FFFFFF", 0.0), ("#D9FDF6EE", 0.55), ("#C4E8DCC8", 1.0)),
        _ => new SolidColorBrush(surface)
    };

    private static Brush ButtonBrush(string tier, Color surface) => tier switch
    {
        "frost" => Solid("#CCFEFCF8"),
        "glass" => Gradient(("#E6FFFFFF", 0.0), ("#CCFDF6EE", 1.0)),
        "liquid" => Gradient(("#F5FFFFFF", 0.0), ("#D9FDF6EE", 0.55), ("#BFE8DCC8", 1.0)),
        _ => new SolidColorBrush(surface)
    };

    /// <summary>描边画刷：越往液态，上沿反光越亮（这就是「上下沿」的玻璃边）。</summary>
    private static Brush StrokeBrush(string tier, bool bar) => new SolidColorBrush(tier switch
    {
        "frost" => Color.FromArgb(bar ? "#26FFFFFF" : "#33FFFFFF"),
        "glass" => Color.FromArgb(bar ? "#59FFFFFF" : "#66FFFFFF"),
        "liquid" => Color.FromArgb(bar ? "#8CFFFFFF" : "#99FFFFFF"),
        _ => Colors.Transparent
    });

    private static double StrokeWidth(string tier, bool bar) => tier switch
    {
        "none" => 0,
        "liquid" => bar ? 1 : 1.5,
        _ => 1
    };

    /// <summary>按钮描边：关掉玻璃时保留原来的暖色描边，开启后转成白色反光。</summary>
    private static Color ButtonStrokeColor(string tier) => tier switch
    {
        "frost" => Color.FromArgb("#59FFFFFF"),
        "glass" => Color.FromArgb("#80FFFFFF"),
        "liquid" => Color.FromArgb("#B3FFFFFF"),
        _ => Color.FromArgb("#4DF5DFC0")
    };

    private static SolidColorBrush Solid(string hex) => new(Color.FromArgb(hex));

    private static LinearGradientBrush Gradient(params (string hex, double offset)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (hex, offset) in stops)
            b.GradientStops.Add(new GradientStop { Color = Color.FromArgb(hex), Offset = (float)offset });
        return b;
    }

    /// <summary>从主题字典里取颜色（取不到就用兜底值），保证跟随配色主题。</summary>
    private static Color ReadColor(ResourceDictionary res, string key, string fallback)
        => res.TryGetValue(key, out var v) && v is Color c ? c : Color.FromArgb(fallback);
}
