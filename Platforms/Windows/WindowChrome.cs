#if WINDOWS
namespace WarmAsBefore.Platforms.Windows;

/// <summary>
/// WinUI 原生标题栏跟随当前主题（终末地是暗色 HUD，白标题栏会像「贴了一张纸条」）。
/// 无 MSIX 的 EXE 默认用系统浅色标题栏；这里在窗口 handler 就绪时、以及系统深浅色变化时按
/// PageBg / SurfaceFg 重刷一次标题栏与最小化/关闭按钮的颜色。取不到资源就保持系统默认，不抛。
/// 只影响桌面窗框，不碰页面内容，也不改变系统设置。
/// </summary>
internal static class WindowChrome
{
    public static void ApplyTitleBar(Microsoft.UI.Xaml.Window? window)
    {
        try
        {
            if (window?.AppWindow?.TitleBar is not { } bar) return;
            var app = Microsoft.Maui.Controls.Application.Current;
            var res = app?.Resources;
            var dark = string.Equals(app?.RequestedTheme.ToString(), "Dark", StringComparison.OrdinalIgnoreCase);

            var bg = dark ? global::Windows.UI.Color.FromArgb(255, 0x13, 0x13, 0x13)
                          : global::Windows.UI.Color.FromArgb(255, 0xF5, 0xF0, 0xE8);
            var fg = dark ? global::Windows.UI.Color.FromArgb(255, 0xF4, 0xF4, 0xF4)
                          : global::Windows.UI.Color.FromArgb(255, 0x4A, 0x3D, 0x2C);
            if (res is not null)
            {
                bg = Pick(res, "PageBg", bg);
                fg = Pick(res, "SurfaceFg", fg);
            }

            bar.BackgroundColor = bg;
            bar.ForegroundColor = fg;
            bar.ButtonBackgroundColor = bg;
            bar.ButtonForegroundColor = fg;
            bar.ButtonHoverBackgroundColor = dark
                ? global::Windows.UI.Color.FromArgb(255, 0x2A, 0x2A, 0x2A)
                : global::Windows.UI.Color.FromArgb(255, 0xE7, 0xE1, 0xD8);
            bar.ButtonHoverForegroundColor = fg;
            bar.ButtonPressedBackgroundColor = dark
                ? global::Windows.UI.Color.FromArgb(255, 0x3A, 0x3A, 0x3A)
                : global::Windows.UI.Color.FromArgb(255, 0xD8, 0xD1, 0xC6);
            bar.ButtonPressedForegroundColor = fg;
        }
        catch (Exception ex)
        {
            App.WriteLog("WindowChrome.TitleBar -> " + ex.Message);
        }
    }

    private static global::Windows.UI.Color Pick(Microsoft.Maui.Controls.ResourceDictionary res, string key,
        global::Windows.UI.Color fallback)
    {
        try
        {
            if (res.TryGetValue(key, out var v) && v is Microsoft.Maui.Graphics.Color c)
            {
                byte B(double f) => (byte)Math.Clamp((int)Math.Round(f * 255), 0, 255);
                return global::Windows.UI.Color.FromArgb(B(c.Alpha), B(c.Red), B(c.Green), B(c.Blue));
            }
        }
        catch { }
        return fallback;
    }
}
#endif
