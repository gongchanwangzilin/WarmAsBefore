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
                UpdateColorsFromXaml(themeDict, File.ReadAllText(filePath));
                App.WriteLog("ThemeManager.Loaded from file: " + filePath);
                return;
            }
            // 尝试从嵌入资源加载
            try
            {
                var asm = typeof(ThemeManager).Assembly;
                var resName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("DesignSystem.Theme." + name + ".xaml", StringComparison.OrdinalIgnoreCase));
                if (resName is not null)
                {
                    using var stream = asm.GetManifestResourceStream(resName);
                    if (stream is not null)
                    {
                        using var reader = new StreamReader(stream);
                        var xaml = reader.ReadToEnd();
                        UpdateColorsFromXaml(themeDict, xaml);
                        App.WriteLog("ThemeManager.Loaded from embedded: " + resName);
                    }
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            App.WriteLog("ThemeManager.ApplyThemeResources -> " + ex);
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

    private void OnChange() => Changed?.Invoke();

    public void Reset()
    {
        _glass = _frost = _liquid = false;
        OnChange();
    }
}
