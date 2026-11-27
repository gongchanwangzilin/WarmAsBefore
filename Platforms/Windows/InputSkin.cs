#if WINDOWS
using Microsoft.UI.Xaml.Controls;
using Mux = Microsoft.UI.Xaml;

namespace WarmAsBefore.Platforms.Windows;

/// <summary>
/// 剥掉 WinUI 输入控件的系统皮。
///
/// 为什么要动这一层（用户的原话）：「你老是喜欢在不该用库的地方用库来实现……原生库的这些内容
/// 和我们的设计根本不搭，很突兀，就像另一个图层一样，根本不对版。」
///
/// 具体症状：WinUI 的 TextBox / ComboBox 自己带一层 1px 边框、自己的圆角、自己的焦点矩形和
/// 自己的填充色。页面里已经用玻璃卡片与令牌把这一格画好了，系统再压一层边框上去，
/// 同一个位置就出现了两个「框」—— 它不在我们任何一个令牌的控制范围内，所以永远对不上版。
///
/// 做法：只保留**打字所需**的那部分（文本、光标、选区、滚动），把 chrome 全部清零。
/// 背景交给页面自己的玻璃面，边框交给世界的边线令牌，文字颜色由 XAML 的 TextColor 走墨阶。
/// 这一层属于「平台适配」，不是设计决定：设计系统里不写任何 WinUI 的属性名。
/// </summary>
internal static class InputSkin
{
    /// <summary>文本框：去边框、去填充、去焦点矩形，选区用强调色低透明度。</summary>
    public static void Apply(TextBox box)
    {
        try
        {
            box.BorderThickness = new Mux.Thickness(0);
            box.Background = null;
            box.Padding = new Mux.Thickness(0);
            box.MinHeight = 0;
            box.CornerRadius = new Mux.CornerRadius(0);
            box.FocusVisualPrimaryThickness = new Mux.Thickness(0);
            box.FocusVisualSecondaryThickness = new Mux.Thickness(0);
            box.UseSystemFocusVisuals = false;
            // 选区底色跟强调色走：系统那支蓝色是这套界面里唯一不属于世界的高饱和色。
            if (Microsoft.Maui.Controls.Application.Current?.Resources.TryGetValue("AccentWarm", out var accent) == true
                && accent is Microsoft.Maui.Graphics.Color a)
            {
                box.SelectionHighlightColor = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    global::Windows.UI.Color.FromArgb(90, To8(a.Red), To8(a.Green), To8(a.Blue)));
            }
        }
        catch (Exception ex) { App.WriteLog("InputSkin.TextBox -> " + ex.Message); }
    }

    /// <summary>下拉框：合上的那一格也去皮，展开的浮层由 <see cref="ControlTheme"/> 上色。</summary>
    public static void Apply(ComboBox combo)
    {
        try
        {
            combo.BorderThickness = new Mux.Thickness(0);
            combo.Background = null;
            combo.Padding = new Mux.Thickness(0, 0, 8, 0);
            combo.MinHeight = 0;
            combo.CornerRadius = new Mux.CornerRadius(0);
            combo.FocusVisualPrimaryThickness = new Mux.Thickness(0);
            combo.FocusVisualSecondaryThickness = new Mux.Thickness(0);
            combo.UseSystemFocusVisuals = false;
        }
        catch (Exception ex) { App.WriteLog("InputSkin.ComboBox -> " + ex.Message); }
    }

    private static byte To8(float f) => (byte)Math.Clamp((int)Math.Round(f * 255), 0, 255);
}
#endif
