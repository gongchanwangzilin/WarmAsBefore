namespace WarmAsBefore.DesignSystem.Theme;

using WarmAsBefore.DesignSystem.Worlds;

public class ThemeManager
{
    private bool _glass;
    private bool _frost;
    private bool _liquid;
    private string _themeName = "classic";
    private StyleWorld _world = StyleWorld.Soft;
    private bool _dark;

    /// <summary>
    /// 风格世界（材质）：轻质感 / 柔和极简 / 柔和渐变 / 液态玻璃。
    /// 它接管了原来「磨砂 / 毛玻璃 / 液态玻璃」三个开关的位置 —— 开关是互相牵制的三个布尔，
    /// 用户要的是一句话选一种，而世界的数量正好是四。
    /// </summary>
    public StyleWorld World
    {
        get => _world;
        set
        {
            if (_world == value) return;
            _world = value;
            OnChange();
        }
    }

    /// <summary>深色档：明暗属于世界而不是色板 —— 四套色板在每个世界里都各算一条暗阶。
    /// 它不是设置项：跟随系统的深浅色偏好（Windows / Android 都有），用户在系统里切，界面跟着换。</summary>
    public bool Dark
    {
        get => _dark;
        set
        {
            if (_dark == value) return;
            _dark = value;
            OnChange();
        }
    }

    private static bool _systemDark;
    public static ThemeManager? Current { get; private set; }

    public ThemeManager()
    {
        _dark = _systemDark;
        Current = this;
    }

    /// <summary>
    /// 把深色档接到系统偏好上。必须在 App 构造时调用一次：
    /// ThemeManager 由 DI 懒建，可能比首帧晚，所以系统值先存在静态里，构造时再取。
    /// </summary>
    public static void FollowSystemTheme()
    {
        var app = Application.Current;
        if (app is null) return;
        Apply(app.RequestedTheme);
        app.RequestedThemeChanged += (_, e) => Apply(e.RequestedTheme);
    }

    private static void Apply(AppTheme theme)
    {
        _systemDark = theme == AppTheme.Dark;
        if (Current is { } tm) tm.Dark = _systemDark;
    }

    /// <summary>毛玻璃（磨砂的高级版）：开启时自动同时启用磨砂。</summary>
    public bool Glass
    {
        get => _glass;
        set
        {
            // 值没变就别刷新：下发设置时是全量赋值，每次不改也刷会让任何一次点击都触发整页重排
            if (_glass == value && (!value || _frost)) return;
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
            if (_frost == value) return;
            _frost = value;
            if (!value) _glass = false;
            OnChange();
        }
    }

    public bool Liquid
    {
        get => _liquid;
        set
        {
            if (_liquid == value) return;
            _liquid = value;
            OnChange();
        }
    }

    /// <summary>玻璃自适应透明度 0-1（Apple Liquid Glass 标准）：1=全液态透明；0=接近不透明。</summary>
    public double GlassTranslucency
    {
        get => _glassTranslucency;
        set
        {
            var v = Math.Clamp(value, 0, 1);
            if (Math.Abs(_glassTranslucency - v) < 1e-6) return;
            _glassTranslucency = v;
            OnChange();
        }
    }
    private double _glassTranslucency = 1.0;

    /// <summary>减弱透明降级（Accessibility）：true 时玻璃切不透明磨砂层，保证正文 4.5:1 对比。</summary>
    public bool ReducedTransparency
    {
        get => _reducedTransparency;
        set
        {
            if (_reducedTransparency == value) return;
            _reducedTransparency = value;
            OnChange();
        }
    }
    private bool _reducedTransparency;

    /// <summary>磨砂程度 0-1：0=清玻璃，1=厚磨砂。</summary>
    public double GlassFrost
    {
        get => _glassFrost;
        set
        {
            var v = Math.Clamp(value, 0, 1);
            if (Math.Abs(_glassFrost - v) < 1e-6) return;
            _glassFrost = v;
            OnChange();
        }
    }
    private double _glassFrost = 0.5;

    /// <summary>液态高度 0-1：投影深度与上沿高光，0=贴面，1=明显浮起。</summary>
    public double GlassHeight
    {
        get => _glassHeight;
        set
        {
            var v = Math.Clamp(value, 0, 1);
            if (Math.Abs(_glassHeight - v) < 1e-6) return;
            _glassHeight = v;
            OnChange();
        }
    }
    private double _glassHeight = 0.5;

    /// <summary>光源 X：屏幕平面内左右（-1 左 · 1 右）。</summary>
    public double GlassLightX
    {
        get => _lx;
        set
        {
            var v = Math.Clamp(value, -1, 1);
            if (Math.Abs(_lx - v) < 1e-6) return;
            _lx = v;
            OnChange();
        }
    }
    private double _lx = -0.7;

    /// <summary>光源 Y：屏幕平面内上下（-1 上 · 1 下）。</summary>
    public double GlassLightY
    {
        get => _ly;
        set
        {
            var v = Math.Clamp(value, -1, 1);
            if (Math.Abs(_ly - v) < 1e-6) return;
            _ly = v;
            OnChange();
        }
    }
    private double _ly = -0.7;

    /// <summary>光源 Z：离表面的高度 0-1 —— 越高高光越柔、影子越散。</summary>
    public double GlassLightZ
    {
        get => _lz;
        set
        {
            var v = Math.Clamp(value, 0, 1);
            if (Math.Abs(_lz - v) < 1e-6) return;
            _lz = v;
            OnChange();
        }
    }
    private double _lz = 0.3;

    /// <summary>光宽 0-1：窄=一道锐利反光，宽=整片漫射。</summary>
    public double GlassLightWidth
    {
        get => _lw;
        set
        {
            var v = Math.Clamp(value, 0, 1);
            if (Math.Abs(_lw - v) < 1e-6) return;
            _lw = v;
            OnChange();
        }
    }
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
            "SecondaryBg", "MutedFg", "BorderLine", "InputBg", "WellBg", "EdgeStrong",
            "WarmDark", "WarmBody", "WarmMuted", "WarmFaint",
            "AccentWarm", "AccentWarmFg", "BubbleMine", "BubbleTheirs", "OverlayDim",
            "Cream50", "Cream100", "Cream200", "Beige100", "Beige200", "BeigeAccent50", "BeigeAccent100",
            "BeigeAccent200", "BeigeAccent300", "OffWhite50", "OffWhite100", "OffWhite200", "ShadowLight",
            "ShadowDim", "ShadowDeep", "GreenSoft", "RedSoft", "DangerInk", "BlueSoft", "TabActive", "TabInactive" };
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

    /// <summary>
    /// 窗口尺度跨档时重发令牌。走 ScheduleApply 而不是直接 Flush：拖窗口一次会报几十次尺寸变化，
    /// 而重发是整屏重排 —— 让它和换色共用同一个防抖窗口，拖动手感才不会变成抽风。
    /// </summary>
    public void RepublishForScale() => ScheduleApply();

    private void Flush()
    {
        string? theme;
        lock (_applyLock)
        {
            theme = _pendingTheme;
            _pendingTheme = null;
        }

        // 回调在线程池上：走 Ui.Post，别让 MainThread 在现场找 DispatcherQueue（找不到即崩进程）。
        Helpers.Ui.Post(() =>
        {
            try
            {
                // 先换调色板，再算世界，最后下发画刷 —— 顺序保证世界读到的是本色相种子，
                // 而 GlassTokens 读到的是世界算好的色标（GTint* / GEdge*）。
                if (theme is not null)
                {
                    ApplyThemeResources(theme);
                }
                StyleWorldTokens.Publish(_world, _dark, _themeName);
                var (tBias, fBias, hBias) = StyleWorldTokens.GlassBias(_world);
                GlassTokens.Publish(new GlassParams(StyleWorldTokens.GlassTier,
                    GlassTranslucency * tBias, GlassFrost * fBias, GlassHeight * hBias,
                    GlassLightX, GlassLightY, GlassLightZ, GlassLightWidth, ReducedTransparency));
                StyleWorldTokens.ApplyFace();   // 玻璃档把算好的面接回世界的名字上（顺序不能颠倒）
                if (Application.Current?.Resources.TryGetValue("PageBg", out var pb) == true && pb is Color pageBg)
                    Services.AppBackgroundService.Recolor(pageBg);   // 纯色背景要跟世界一起换档
                Services.AppBackgroundService.Publish();   // 换主题别把自定义背景冲掉
                // 这里刻意不给整页做淡入过场。换主题常常发生在「刚导航到新页面」的那一刻
                // （进设置首页就会顺手存一次设置 → 下发 → 走到这里），而此时 Shell.Current.CurrentPage
                // 可能还是上一个页面，或者新页面的平台视图还没建好。整页 Opacity 是抢不过导航的：
                // 实测把设置页留在完全透明，窗口只剩一张白底，鼠标点什么都没反应
                // （元素还在树里，所以读屏能读到、界面看不见）。画刷是 DynamicResource，
                // 换完自己就重绘了，不需要再叠一层「换好了」的暗示。
            }
            catch (Exception ex) { App.WriteLog("ThemeManager.Flush -> " + ex); }
        });
    }

    public void Reset()
    {
        _glass = _frost = _liquid = false;
        OnChange();
    }
}
