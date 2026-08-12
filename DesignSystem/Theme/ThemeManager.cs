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
            var dicts = app.Resources.MergedDictionaries;
            var list = dicts.ToList();
            var old = list.FirstOrDefault(d =>
                d.Source?.OriginalString.Contains("ColorPalette", StringComparison.OrdinalIgnoreCase) == true);
            var idx = old is not null ? list.IndexOf(old) : 0;
            if (old is not null) list.Remove(old);
            var fresh = new ResourceDictionary { Source = new Uri($"ms-appx:///{file}") };
            list.Insert(Math.Min(idx, list.Count), fresh);
            dicts.Clear();
            foreach (var d in list) dicts.Add(d);
        }
        catch (Exception ex)
        {
            App.WriteLog("ThemeManager.ApplyThemeResources -> " + ex);
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
