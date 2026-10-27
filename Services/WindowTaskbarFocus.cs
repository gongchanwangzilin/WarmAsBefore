using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace WarmAsBefore.Services;

/// <summary>
/// 焦点在时压在任务栏之上，失焦让位。
///
/// 和「窗口置顶」是两件事：那个是用户在设置里选的永久策略（见 <see cref="WindowTopmost"/>），
/// 这里只管「正在用的这一程」—— 所以失焦时先看 Presenter 的 IsAlwaysOnTop，
/// 用户本来就开着永久置顶就原样保留，不替用户关闭。
/// </summary>
public static class WindowTaskbarFocus
{
    // 窗口可能重建：按实例记账，同一个窗口挂第二次直接返回。
    private static readonly ConditionalWeakTable<Window, object> Hooked = new();

    /// <summary>在窗口创建处挂一次；非 Windows 平台整个类是空操作。</summary>
    public static void Attach(Window window)
    {
#if WINDOWS
        if (window is null) return;
        if (!Hooked.TryAdd(window, new object())) return;

        window.Activated += (_, _) => Apply(window, true);
        window.Deactivated += (_, _) =>
        {
            if (IsPinned(window)) return;
            Apply(window, false);
        };
        // 建窗时窗口通常已经是前台，但 Handler 还没好；好了之后补一次，免得要等用户切走再切回。
        if (window.Handler?.PlatformView is not null) Apply(window, true);
#endif
    }

#if WINDOWS
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new(0xFFFF);
    private static readonly IntPtr HWND_NOTOPMOST = new(0xFFFE);
    private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;

    private static void Apply(Window window, bool topmost)
    {
        try
        {
            if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window wnd) return;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(wnd);
            if (hwnd == IntPtr.Zero) return;
            // 只改 z-order：不抢焦点、不动位置，切窗口时不会抖一下。
            SetWindowPos(hwnd, topmost ? HWND_TOPMOST : HWND_NOTOPMOST,
                0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch (Exception ex)
        {
            App.WriteLog("WindowTaskbarFocus.Apply -> " + ex.Message);
        }
    }

    /// <summary>用户开着永久置顶（WindowTopmost.Force 用 SetWindowPos 实现，不一定反映在 Presenter 上）。</summary>
    private static bool IsPinned(Window window)
    {
        try
        {
            if (WindowTopmost.IsForced) return true;
            return window.Handler?.PlatformView is Microsoft.UI.Xaml.Window wnd
                && wnd.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { IsAlwaysOnTop: true };
        }
        catch { return false; }
    }
#endif
}
