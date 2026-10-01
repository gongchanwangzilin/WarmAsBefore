namespace WarmAsBefore.Services;

/// <summary>
/// Windows 桌面端窗口置顶。
/// 通知/桌宠类应用：主窗口常驻顶层（WinUI 的 IsAlwaysOnTop 会被其他应用抢占，
/// 故用定时器周期性重新断言）。弹对话框前调用 Force() 立即置顶。
/// </summary>
public static class WindowTopmost
{
    private static System.Threading.Timer? _assertTimer;
    private static bool _forced;

    /// <summary>
    /// 常驻强制置顶：主窗口 + 桌宠窗口永远在最上层。
    /// 调用一次即开启 3 秒周期重断言（WinUI 被其他窗口抢焦后自动抢回）。
    /// </summary>
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

    /// <summary>取消常驻置顶（用户关闭 AlwaysOnTop 设置时调）。</summary>
    public static void Release()
    {
        _forced = false;
        _assertTimer?.Dispose();
        _assertTimer = null;
        SetAll(false);
    }

    /// <summary>弹通知/对话框前立即置顶一次（不启动定时器）。</summary>
    public static void BringToFront()
    {
        SetAll(true);
    }

    private static void SetAll(bool on)
    {
#if WINDOWS
        try
        {
            foreach (var win in Application.Current?.Windows ?? Array.Empty<Window>())
            {
                if (win.Handler?.PlatformView is Microsoft.UI.Xaml.Window wnd
                    && wnd.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
                {
                    p.IsAlwaysOnTop = on;
                }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("WindowTopmost.SetAll(" + on + ") -> " + ex.Message);
        }
#endif
    }
}
