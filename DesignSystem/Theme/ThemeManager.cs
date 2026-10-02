namespace WarmAsBefore.DesignSystem.Theme;

public class ThemeManager
{
    private bool _glass;
    private bool _frost;
    private bool _liquid;
    private string _themeName = "classic";

    /// <summary>毛玻璃（磨砂的高级版）：开启时自动同时启用磨砂。</summary>
    public bool Glass
    {
        get => _glass;
        set
        {
            _glass = value;
            if (value && !_frost) _frost = true;
            OnChange();
        }
    }

    /// <summary>磨砂玻璃（半透明磨砂效果）。关闭时自动连带关闭毛玻璃。</summary>
    public bool Frost
    {
        get => _frost;
        set
        {
            _frost = value;
            if (!value) _glass = false;
            OnChange();
        }
    }

    public bool Liquid
    {
        get => _liquid;
        set { _liquid = value; OnChange(); }
    }

    /// <summary>玻璃自适应透明度 0-1（Apple Liquid Glass 标准）：1=全液态透明；0=接近不透明。</summary>
    public double GlassTranslucency { get => _glassTranslucency; set { _glassTranslucency = Math.Clamp(value, 0, 1); OnChange(); } }
    private double _glassTranslucency = 1.0;

    /// <summary>减弱透明降级（Accessibility）：true 时玻璃切不透明磨砂层，保证正文 4.5:1 对比。</summary>
    public bool ReducedTransparency { get => _reducedTransparency; set { _reducedTransparency = value; OnChange(); } }
    private bool _reducedTransparency;

    /// <summary>磨砂程度 0-1：0=清玻璃，1=厚磨砂。</summary>
    public double GlassFrost { get => _glassFrost; set { _glassFrost = Math.Clamp(value, 0, 1); OnChange(); } }
    private double _glassFrost = 0.5;

    /// <summary>液态高度 0-1：投影深度与上沿高光，0=贴面，1=明显浮起。</summary>
    public double GlassHeight { get => _glassHeight; set { _glassHeight = Math.Clamp(value, 0, 1); OnChange(); } }
    private double _glassHeight = 0.5;

    /// <summary>光源 X：屏幕平面内左右（-1 左 · 1 右）。</summary>
    public double GlassLightX { get => _lx; set { _lx = Math.Clamp(value, -1, 1); OnChange(); } }
    private double _lx = -0.7;

    /// <summary>光源 Y：屏幕平面内上下（-1 上 · 1 下）。</summary>
    public double GlassLightY { get => _ly; set { _ly = Math.Clamp(value, -1, 1); OnChange(); } }
    private double _ly = -0.7;

    /// <summary>光源 Z：离表面的高度 0-1 —— 越高高光越柔、影子越散。</summary>
    public double GlassLightZ { get => _lz; set { _lz = Math.Clamp(value, 0, 1); OnChange(); } }
    private double _lz = 0.3;

    /// <summary>光宽 0-1：窄=一道锐利反光，宽=整片漫射。</summary>
    public double GlassLightWidth { get => _lw; set { _lw = Math.Clamp(value, 0, 1); OnChange(); } }
    private double _lw = 0.4;

    public string ActiveEffect =>
        (_liquid, _glass, _frost) switch
        {
            (true, _, _) => "liquid",
            (_, true, _) => "glass",
            (_, _, true) => "frost",
            _ => "none"
        };

    /// <summary>界面配色主题：classic（经典）/ sakura（樱花粉）/ bamboo（翠竹绿）/ mist（晨雾蓝灰）。</summary>
    public string ThemeName
    {
        get => _themeName;
        set
        {
            if (_themeName == value) return;
            _themeName = value;
            lock (_applyLock) _pendingTheme = value;
            ScheduleApply();
        }
    }

    public static string[] ThemeNames { get; } = { "classic", "sakura", "bamboo", "mist" };

    public static string ThemeDisplay(string name) => name switch
    {
        "sakura" => "樱花粉",
        "bamboo" => "翠竹绿",
        "mist" => "晨雾蓝灰",
        _ => "经典"
    };

    /// <summary>替换应用资源中的主题字典（ColorPalette*.xaml），新页面即时生效。</summary>
    private static void ApplyThemeResources(string name)
    {
        try
        {
            var app = Application.Current;
            if (app is null) return;
            var file = name switch
            {
                "sakura" => "DesignSystem/Theme/ColorPaletteSakura.xaml",
                "bamboo" => "DesignSystem/Theme/ColorPaletteBamboo.xaml",
                "mist" => "DesignSystem/Theme/ColorPaletteMist.xaml",
                _ => "DesignSystem/Theme/ColorPalette.xaml"
            };
            // 找到当前的主题字典
            var dicts = app.Resources.MergedDictionaries;
            var themeDict = dicts.FirstOrDefault(d => d.Source?.OriginalString?.Contains("ColorPalette") == true);
            if (themeDict is null)
            {
                App.WriteLog("ThemeManager: 找不到主题字典");
                return;
            }
            // 尝试从文件加载
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, file.Replace('/', Path.DirectorySeparatorChar)),
                Path.Combine(Directory.GetCurrentDirectory(), file.Replace('/', Path.DirectorySeparatorChar)),
                Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory) ?? "", file.Replace('/', Path.DirectorySeparatorChar))
            };
            var filePath = candidates.FirstOrDefault(File.Exists);
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                ApplyPalette(themeDict, File.ReadAllText(filePath));
                App.WriteLog("ThemeManager.Loaded from file: " + filePath);
                return;
            }
            // 尝试从嵌入资源加载。
            // 逻辑名由 csproj 显式指定为 DesignSystem.Theme.ColorPalette<Name>.xaml，
            // 旧代码拼的是 "DesignSystem.Theme." + name + ".xaml"（= DesignSystem.Theme.sakura.xaml），
            // 永远匹配不上；而上面的文件路径分支在发布版里也不成立（这些 xaml 只做嵌入，不随输出拷贝）。
            // 两个分支同时落空 → 换主题完全无效，永远停在经典配色。
            try
            {
                var asm = typeof(ThemeManager).Assembly;
                var logical = name switch
                {
                    "sakura" => "DesignSystem.Theme.ColorPaletteSakura.xaml",
                    "bamboo" => "DesignSystem.Theme.ColorPaletteBamboo.xaml",
                    "mist" => "DesignSystem.Theme.ColorPaletteMist.xaml",
                    _ => "DesignSystem.Theme.ColorPalette.xaml"
                };
                var resName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => string.Equals(n, logical, StringComparison.OrdinalIgnoreCase));
                if (resName is not null)
                {
                    using var stream = asm.GetManifestResourceStream(resName);
                    if (stream is not null)
                    {
                        using var reader = new StreamReader(stream);
                        var xaml = reader.ReadToEnd();
                        ApplyPalette(themeDict, xaml);
                        App.WriteLog("ThemeManager.Loaded from embedded: " + resName);
                    }
                }
                else
                {
                    App.WriteLog("ThemeManager: 找不到嵌入主题资源 " + logical);
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            App.WriteLog("ThemeManager.ApplyThemeResources -> " + ex);
        }
    }

    /// <summary>
    /// 套用新配色，并**整体替换** MergedDictionaries 里的那一项。
    ///
    /// 只改值（dict[key] = color）对已经创建好的页面不会触发 DynamicResource 重解析 ——
    /// 缓存页面（标题页等 ShellContent）会永远停在旧配色，这正是「选哪个主题都只有经典色」。
    /// 替换字典实例属于集合变更，DynamicResource 才会真正刷新。
    /// </summary>
    /// <summary>
    /// 套用新配色。
    ///
    /// 关键点：把颜色写进**应用级顶层字典**（Application.Current.Resources[key] = value）。
    /// 之前是替换 MergedDictionaries 里的字典实例 —— 那属于集合变更，但
    /// **不会通知已经注册的 DynamicResource**，于是元素和 Style 里的颜色全都不会重新解析，
    /// 表现就是「换了主题但颜色不变」（只有 PageBgBrush 会变，因为它是直接写顶层字典的）。
    /// 顶层字典的 indexer setter 会触发 ValuesChanged → DynamicResource 重新解析。
    /// </summary>
    private static void ApplyPalette(ResourceDictionary themeDict, string xaml)
    {
        try
        {
            var app = Application.Current;
            if (app is null) return;

            var parsed = new ResourceDictionary();
            UpdateColorsFromXaml(parsed, xaml);

            foreach (var kv in parsed)
                app.Resources[kv.Key] = kv.Value;

            App.WriteLog($"ThemeManager palette applied: {parsed.Count} colors (palette has {themeDict.Count} keys)");
        }
        catch (Exception ex)
        {
            App.WriteLog("ThemeManager.ApplyPalette -> " + ex);
        }
    }

    private static void UpdateColorsFromXaml(ResourceDictionary dict, string xaml)
    {
        var keys = new[] { "PageBg", "SurfaceBg", "SurfaceFg", "PrimaryAction", "PrimaryHover", "PrimaryFg",
            "SecondaryBg", "MutedFg", "BorderLine", "InputBg", "WarmDark", "WarmBody", "WarmMuted", "WarmFaint",
            "AccentWarm", "AccentWarmFg", "BubbleMine", "BubbleTheirs", "OverlayDim",
            "Cream50", "Cream100", "Cream200", "Beige100", "Beige200", "BeigeAccent50", "BeigeAccent100",
            "BeigeAccent200", "BeigeAccent300", "OffWhite50", "OffWhite100", "OffWhite200", "ShadowLight",
            "ShadowDim", "ShadowDeep", "GreenSoft", "RedSoft", "BlueSoft", "TabActive", "TabInactive" };
        foreach (var key in keys)
        {
            var match = System.Text.RegularExpressions.Regex.Match(xaml, $"<Color x:Key=\"{key}\"[^>]*>([^<]+)</Color>");
            if (match.Success)
            {
                try
                {
                    var colorStr = match.Groups[1].Value.Trim();
                    if (Color.TryParse(colorStr, out var color))
                        dict[key] = color;
                }
                catch { }
            }
        }
    }

    public event Action? Changed;

    // ── 应用调度（防抖） ────────────────────────────────────────────────
    // 换主题要整体替换调色板字典，改档位/透明度要重写 GlassTokens —— 两者都会让
    // 整棵可视树里的 {DynamicResource} 重新解析。连续点击主题选择器时若每次都立刻执行，
    // 就会叠加成数秒卡死。这里统一防抖：连续变更只在停手后跑一次。
    private const int ApplyDebounceMs = 220;
    private System.Threading.Timer? _applyTimer;
    private readonly object _applyLock = new();
    private string? _pendingTheme;

    private void OnChange()
    {
        ScheduleApply();
        Changed?.Invoke();
    }

    private void ScheduleApply()
    {
        lock (_applyLock)
        {
            _applyTimer ??= new System.Threading.Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
            _applyTimer.Change(ApplyDebounceMs, Timeout.Infinite);
        }
    }

    private void Flush()
    {
        string? theme;
        lock (_applyLock)
        {
            theme = _pendingTheme;
            _pendingTheme = null;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                // 先换调色板，再下发画刷 —— 顺序保证 GlassTokens 读到的是新配色
                if (theme is not null)
                {
                    ApplyThemeResources(theme);
                }
                GlassTokens.Publish(new GlassParams(ActiveEffect, GlassTranslucency, GlassFrost, GlassHeight,
                    GlassLightX, GlassLightY, GlassLightZ, GlassLightWidth, ReducedTransparency));
                Services.AppBackgroundService.Publish();   // 换主题别把自定义背景冲掉
                FadeCurrentPage();
            }
            catch (Exception ex) { App.WriteLog("ThemeManager.Flush -> " + ex); }
        });
    }

    /// <summary>
    /// 整页淡入：换主题是全局重绘，给一个短促的过场，
    /// 观感上是「换好了」而不是「卡了一下」。
    /// </summary>
    private static void FadeCurrentPage()
    {
        var page = Shell.Current?.CurrentPage;
        if (page is null) return;
        page.Opacity = 0.55;
        _ = page.FadeTo(1, 200, Easing.CubicOut);
    }

    public void Reset()
    {
        _glass = _frost = _liquid = false;
        OnChange();
    }
}
