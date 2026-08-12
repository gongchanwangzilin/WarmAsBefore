namespace WarmAsBefore.Services;

/// <summary>Windows 桌面端窗口辅助（置顶等）。</summary>
public static class WindowTopmost
{
    public static void Apply(bool topmost)
    {
#if WINDOWS
        try
        {
            var win = Application.Current?.Windows.FirstOrDefault(w => w.Handler is not null);
            if (win?.Handler?.PlatformView is Microsoft.UI.Xaml.Window wnd
                && wnd.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = topmost;
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("WindowTopmost.Apply -> " + ex);
        }
#endif
    }
}
