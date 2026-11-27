#if WINDOWS
namespace WarmAsBefore.Platforms.Windows;

/// <summary>
/// WinUI 原生控件（下拉框、菜单浮层）自己的配色。
///
/// 为什么必须有这一层：MAUI 的 <c>Picker</c> 只把 <c>TextColor</c> 交给合上的那一格，
/// **展开以后那个浮层是 WinUI 的 ComboBox 模板画的** —— 它的字色与底色来自系统主题资源，
/// 不读我们的墨阶。于是当前界面是深色、墨阶是浅色时，合上的格子看得见，
/// 一点开就变成「浅底白字」：这正是用户说的「下拉栏也是」。
///
/// 做法：把同一套墨与面写进 WinUI 的应用级资源。它不参与令牌重发，
/// 所以每次 <see cref="DesignSystem.Theme.ThemeManager"/> 重发之后都要再调一次。
/// 取不到资源就保持系统默认（宁可默认，也不要写死一套不属于当前世界的颜色）。
/// </summary>
internal static class ControlTheme
{
    /// <summary>浮层底：比卡面沉一档的井色 —— 浮层永远「压」在页面之上，用井色才读得出来。</summary>
    private static readonly string[] BackgroundKeys =
    {
        "ComboBoxDropDownBackground",
        "ComboBoxItemBackground",
        "FlyoutPresenterBackground",
        "MenuFlyoutPresenterBackground",
        "ToolTipBackground"
    };

    private static readonly string[] InkKeys =
    {
        "ComboBoxForeground",
        "ComboBoxForegroundPointerOver",
        "ComboBoxForegroundFocused",
        "ComboBoxForegroundDisabled",
        "ComboBoxDropDownForeground",
        "ComboBoxItemForeground",
        "ComboBoxItemForegroundPointerOver",
        "ComboBoxItemForegroundSelected",
        "ComboBoxItemForegroundPressed",
        "MenuFlyoutItemForeground",
        "ToolTipForeground"
    };

    private static readonly string[] HoverKeys =
    {
        "ComboBoxItemBackgroundPointerOver",
        "ComboBoxItemBackgroundSelected",
        "ComboBoxItemBackgroundSelectedPointerOver",
        "MenuFlyoutItemBackgroundPointerOver"
    };

    public static void Apply()
    {
        try
        {
            var win = Microsoft.UI.Xaml.Application.Current?.Resources;
            var app = Microsoft.Maui.Controls.Application.Current?.Resources;
            if (win is null || app is null) return;

            if (!TryBrush(app, "SurfaceFg", out var ink)) return;
            if (PickColor(app, "WellBg", "SurfaceBg", "PageBg") is not { } surface) return;

            var inkBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(ToWin(ink));
            var bgBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(ToWin(surface));

            foreach (var key in InkKeys) win[key] = inkBrush;
            foreach (var key in BackgroundKeys) win[key] = bgBrush;

            // 悬停/选中：面朝墨的方向挪一档（深底提亮、浅底压暗），不用另造一套色。
            var hover = Lum(surface) < 0.42
                ? Mix(surface, Microsoft.Maui.Graphics.Colors.White, 0.10)
                : Mix(surface, Microsoft.Maui.Graphics.Colors.Black, 0.06);
            var hoverBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(ToWin(hover));
            foreach (var key in HoverKeys) win[key] = hoverBrush;

            win["ComboBoxDropDownBorderBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(ToWin(
                Mix(surface, Microsoft.Maui.Graphics.Colors.Black, 0.18)));
        }
        catch (Exception ex)
        {
            App.WriteLog("ControlTheme.Apply -> " + ex.Message);
        }
    }

    private static bool TryBrush(Microsoft.Maui.Controls.ResourceDictionary res, string key,
        out Microsoft.Maui.Graphics.Color color)
    {
        color = Microsoft.Maui.Graphics.Colors.Transparent;
        if (res.TryGetValue(key, out var v) && v is Microsoft.Maui.Graphics.Color c)
        {
            color = c;
            return true;
        }
        return false;
    }

    private static Microsoft.Maui.Graphics.Color? PickColor(
        Microsoft.Maui.Controls.ResourceDictionary res, params string[] keys)
    {
        foreach (var key in keys)
            if (TryBrush(res, key, out var c)) return c;
        return null;
    }

    private static global::Windows.UI.Color ToWin(Microsoft.Maui.Graphics.Color c)
    {
        byte B(double f) => (byte)Math.Clamp((int)Math.Round(f * 255), 0, 255);
        return global::Windows.UI.Color.FromArgb(B(c.Alpha), B(c.Red), B(c.Green), B(c.Blue));
    }

    private static double Lum(Microsoft.Maui.Graphics.Color c)
        => 0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue;

    private static Microsoft.Maui.Graphics.Color Mix(
        Microsoft.Maui.Graphics.Color a, Microsoft.Maui.Graphics.Color b, double k)
    {
        var t = (float)Math.Clamp(k, 0, 1);
        return new Microsoft.Maui.Graphics.Color(
            a.Red + (b.Red - a.Red) * t,
            a.Green + (b.Green - a.Green) * t,
            a.Blue + (b.Blue - a.Blue) * t,
            1f);
    }
}
#endif
