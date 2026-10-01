namespace WarmAsBefore.Services;

/// <summary>
/// Windows 桌面端窗口辅助（置顶等）。
/// 主窗口 + 通知（DisplayAlert / 弹窗）都会用到。
/// 通知置顶策略：AlertWindowTopmost —— 在弹 Alert 前把主窗口提升到顶层，
/// 避免通知被其他应用挡住；Alert 结束后不自动回落（保持用户设置的 AlwaysOnTop）。
/// </summary>
public static class WindowTopmost
{
    /// <summary>把主窗口设为/取消置顶（主窗口 + 通知弹窗共用）。</summary>
    public static void Apply(bool topmost)
    {
#if WINDOWS
        try
        {
            // 对「所有」应用窗口统一应用，避免仅主窗口被置顶而弹窗/桌宠没跟上
            foreach (var win in Application.Current?.Windows ?? Array.Empty<Window>())
            {
                if (win.Handler?.PlatformView is Microsoft.UI.Xaml.Window wnd
                    && wnd.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    presenter.IsAlwaysOnTop = topmost;
                }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("WindowTopmost.Apply -> " + ex);
        }
#endif
    }

    /// <summary>
    /// 弹通知（DisplayAlert/DisplayActionSheet）前调用：临时把主窗口提到顶层，
    /// 让系统级通知/对话框不被其他窗口遮挡。结束后不自动回落。
    /// </summary>
    public static void BringAllToTop()
    {
#if WINDOWS
        try
        {
            foreach (var win in Application.Current?.Windows ?? Array.Empty<Window>())
            {
                if (win.Handler?.PlatformView is Microsoft.UI.Xaml.Window wnd)
                {
                    wnd.Activate();
                    if (wnd.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
                        p.IsAlwaysOnTop = true;
                }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("WindowTopmost.BringAllToTop -> " + ex);
        }
#endif
    }

    /// <summary>当前是否「至少有一个窗口」处于置顶。</summary>
    public static bool AnyTopmost()
    {
#if WINDOWS
        foreach (var win in Application.Current?.Windows ?? Array.Empty<Window>())
        {
            if (win.Handler?.PlatformView is Microsoft.UI.Xaml.Window wnd
                && wnd.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p
                && p.IsAlwaysOnTop)
                return true;
        }
        return false;
#else
        return false;
#endif
    }
}
