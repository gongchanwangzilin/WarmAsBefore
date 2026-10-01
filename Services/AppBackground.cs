using System.Runtime.InteropServices;

namespace WarmAsBefore.Services;

/// <summary>
/// 让主窗口客户区变透明（DWM「sheet of glass」），桌面 / 下层窗口就能透上来。
///
/// 为什么必须做这一步：玻璃卡片是半透明材质，可**它背后什么都没有**。
/// 窗口背景是纯色，卡片半透明压上去，出来只是「一块浅一点的板」——
/// 没有可折射的内容，再调 alpha、再加高光都不成立。
/// 只有让背景真的透出东西（自定义图片 / 桌面），玻璃才第一次有东西可透。
/// </summary>
public static class WindowBackdrop
{
    private static bool _transparent;

#if WINDOWS
    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);
#endif

    /// <summary>开启/关闭窗口透视。失败只记日志，绝不让窗口变成黑的。</summary>
    public static void SetTransparent(bool on)
    {
        if (_transparent == on) return;
        _transparent = on;
#if WINDOWS
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var win = Application.Current?.Windows.FirstOrDefault(w => w.Handler is not null);
                if (win?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window w) return;

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(w);
                // 四边都取 -1 = 整个客户区都交给 DWM 合成（背景透明）
                var m = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref m);

                // XAML 根也要透明，否则会被一层不透明画布挡住
                if (w.Content is Microsoft.UI.Xaml.Controls.Panel root)
                    root.Background = on ? null : new Microsoft.UI.Xaml.Media.SolidColorBrush(
                        Microsoft.UI.Colors.Transparent);

                App.WriteLog($"WindowBackdrop: transparent={on}");
            }
            catch (Exception ex)
            {
                App.WriteLog("WindowBackdrop.SetTransparent -> " + ex.Message);
            }
        });
#endif
    }

    /// <summary>
    /// 判断图片是否有透明像素（用于「PNG 透明处穿透到窗口下层」）。
    /// 只抽样扫描，够用且不拖慢启动。
    /// </summary>
    private static readonly Dictionary<string, bool> _alphaCache = new();

    public static bool HasAlpha(string path)
    {
        if (_alphaCache.TryGetValue(path, out var cached)) return cached;
        var result = ScanAlpha(path);
        _alphaCache[path] = result;
        return result;
    }

    private static bool ScanAlpha(string path)
    {
#if WINDOWS
        try
        {
            if (!File.Exists(path)) return false;
            using var bmp = new System.Drawing.Bitmap(path);
            const int step = 8;
            for (var y = 0; y < bmp.Height; y += step)
                for (var x = 0; x < bmp.Width; x += step)
                    if (bmp.GetPixel(x, y).A < 250) return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("WindowBackdrop.HasAlpha -> " + ex.Message);
        }
#endif
        return false;
    }
}
