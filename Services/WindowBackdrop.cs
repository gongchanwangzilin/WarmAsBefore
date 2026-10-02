using System.Runtime.InteropServices;

namespace WarmAsBefore.Services;

/// <summary>
/// 让主窗口客户区透明，桌面 / 下层窗口透上来。
///
/// ⚠ 只用 DwmExtendFrameIntoClientArea(hwnd, {-1,-1,-1,-1}) 在 Win10/11 上**不够**：
/// 客户区会被交给 DWM，但底层是窗口的默认背景（白），所以现象是「变白」而不是透出桌面。
/// 真正让它透到桌面的关键是 SetWindowCompositionAttribute 的 ACCENT_ENABLE_ACRYLICBLURBEHIND：
/// 由 DWM 把窗口背后（桌面）采样、模糊后作为窗口背景。
/// 两者一起用：ExtendFrame 让整块客户区参与合成，Acrylic 决定合成出什么。
/// </summary>
public static class WindowBackdrop
{
    private static bool _transparent;

    /// <summary>亚克力色调 ARGB（ABGR 排布）。alpha 越低越透；这里给一点点冷色底，避免完全无色发灰。</summary>
    private const uint AcrylicTint = 0x40_1A1A1A;   // A=0x40

#if WINDOWS
    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS { public int Left, Right, Top, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_DISABLED = 0;
    private const int ACCENT_ENABLE_BLURBEHIND = 3;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
#endif

    /// <summary>开启/关闭窗口透视。失败只记日志，不会把窗口搞黑。</summary>
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

                // 1) 整块客户区参与 DWM 合成
                var m = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref m);

                // 2) 合成成「亚克力」= 桌面模糊透上来；关闭时回到不透明
                ApplyAccent(hwnd, on);

                App.WriteLog($"WindowBackdrop: transparent={on}");
            }
            catch (Exception ex)
            {
                App.WriteLog("WindowBackdrop.SetTransparent -> " + ex.Message);
            }
        });
#endif
    }

#if WINDOWS
    private static void ApplyAccent(IntPtr hwnd, bool on)
    {
        var policy = new AccentPolicy
        {
            AccentState = on ? ACCENT_ENABLE_ACRYLICBLURBEHIND :
                           // Win10 老版本不支持亚克力时退到普通模糊
                           ACCENT_DISABLED,
            AccentFlags = 2,                 // 四边都画
            GradientColor = on ? AcrylicTint : 0,
            AnimationId = 0
        };

        var size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WCA_ACCENT_POLICY,
                Data = ptr,
                SizeOfData = size
            };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>老系统（Win10 1803-）没有亚克力，退回 BLURBEHIND。</summary>
    private static void ApplyBlurFallback(IntPtr hwnd)
    {
        var policy = new AccentPolicy
        {
            AccentState = ACCENT_ENABLE_BLURBEHIND,
            AccentFlags = 2,
            GradientColor = 0,
            AnimationId = 0
        };
        var size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
#endif

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
