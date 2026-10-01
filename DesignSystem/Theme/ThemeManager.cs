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
            ApplyThemeResources(value);
            OnChange();
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
    private static void ApplyPalette(ResourceDictionary themeDict, string xaml)
    {
        try
        {
            var fresh = new ResourceDictionary();
            foreach (var kv in themeDict)
                fresh[kv.Key] = kv.Value;
            UpdateColorsFromXaml(fresh, xaml);

            var dicts = Application.Current?.Resources.MergedDictionaries;
            if (dicts is null) return;
            // MergedDictionaries 是 ICollection，没有索引器：移除旧的再加入新的
            if (dicts.Contains(themeDict)) dicts.Remove(themeDict);
            dicts.Add(fresh);
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

    private void OnChange()
    {
        // 把当前档位换算成画刷资源，组件样式用 {DynamicResource} 引用 → 一处切换、全局生效。
        // 顺序很重要：ThemeName setter 会先 ApplyThemeResources 再走到这里，
        // 所以读 SurfaceBg 拿到的一定是新配色。
        GlassTokens.Publish(ActiveEffect);
        Changed?.Invoke();
    }

    public void Reset()
    {
        _glass = _frost = _liquid = false;
        OnChange();
    }
}
