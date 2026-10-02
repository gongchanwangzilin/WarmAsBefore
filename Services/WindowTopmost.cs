using System.Runtime.InteropServices;

namespace WarmAsBefore.Services;

/// <summary>
/// Windows 桌面端窗口置顶。
///
/// 这里其实是两件不同的事，旧实现混为一谈，正是「置顶开关无效 + 切页卡顿」的来源：
/// · 策略（持久）：<see cref="Force"/> / <see cref="Release"/> —— 跟随设置页「窗口置顶」开关，
///   开启时 3 秒周期重断言，防止被抢焦。
/// · 一次性抬前台：<see cref="BringToFront"/> —— 弹通知/对话框前把窗口抬到前台并激活，
///   **不改动策略**。旧实现 BringToFront 直接等于 SetAll(true)，于是任何一次导航都会把窗口
///   永久置顶：设置里的「窗口置顶」关掉也失效，且每次切页都做一次 z-order 变更造成卡顿。
/// </summary>
public static class WindowTopmost
{
    private static System.Threading.Timer? _assertTimer;
    private static bool _forced;

    /// <summary>常驻置顶（跟随设置开关），3 秒周期重断言防被其他窗口抢占。</summary>
    public static void Force()
    {
        SetAll(true);
        _forced = true;
        _assertTimer ??= new System.Threading.Timer(_ =>
        {
            if (!_forced) return;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try { SetAll(true); } catch { }
            });
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3));
    }

    /// <summary>取消常驻置顶。</summary>
    public static void Release()
    {
        _forced = false;
        _assertTimer?.Dispose();
        _assertTimer = null;
        SetAll(false);
    }

    /// <summary>
    /// 弹通知 / 对话框前：把主窗口抬到前台并激活，**不改动常驻置顶策略**。
    /// 必须激活而不能只 SetWindowPos(SWP_NOACTIVATE)：MAUI 的 DisplayAlert 是主窗口内的
    /// ContentDialog，窗口不是前台窗口时对话框收不到输入 —— 表现就是「对话框沉在下面、点不到」。
    /// </summary>
    public static void BringToFront()
    {
#if WINDOWS
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var hwnd = GetMainHwnd();
                if (hwnd == IntPtr.Zero) return;

                if (_forced)
                {
                    // 常驻置顶已开：只需重断言 z-order，不抢焦点打断用户输入
                    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    return;
                }
                SetForegroundWindow(hwnd);
            }
            catch (Exception ex)
            {
                App.WriteLog("WindowTopmost.BringToFront -> " + ex.Message);
            }
        });
#endif
    }

#if WINDOWS
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static readonly IntPtr HWND_TOPMOST = new(0xFFFF);
    private static readonly IntPtr HWND_NOTOPMOST = new(0xFFFE);
    // SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE：只改 z-order，不抢焦点、不动位置
    private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;

    private static void SetAll(bool topmost)
    {
        try
        {
            foreach (var win in Application.Current?.Windows ?? Array.Empty<Window>())
            {
                if (win.Handler?.PlatformView is not Microsoft.UI.Xaml.Window wnd) continue;

                if (wnd.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
                    p.IsAlwaysOnTop = topmost;

                var hwnd = GetHwnd(wnd);
                if (hwnd != IntPtr.Zero)
                {
                    SetWindowPos(hwnd, topmost ? HWND_TOPMOST : HWND_NOTOPMOST,
                        0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("WindowTopmost.SetAll -> " + ex.Message);
        }
    }

    /// <summary>主窗口（承载 Shell 的那个）；桌宠是独立窗口，不参与对话框抬升。</summary>
    private static IntPtr GetMainHwnd()
    {
        var win = Application.Current?.Windows.FirstOrDefault(w => w.Handler is not null);
        return win?.Handler?.PlatformView is Microsoft.UI.Xaml.Window w ? GetHwnd(w) : IntPtr.Zero;
    }

    private static IntPtr GetHwnd(Microsoft.UI.Xaml.Window wnd)
    {
        try { return WinRT.Interop.WindowNative.GetWindowHandle(wnd); }
        catch { return IntPtr.Zero; }
    }
#else
    private static void SetAll(bool topmost) { }
#endif
}
