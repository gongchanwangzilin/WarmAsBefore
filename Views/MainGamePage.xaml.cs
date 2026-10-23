using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class MainGamePage : ContentPage
{
    public MainGamePage(MainGameViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    /// <summary>
    /// 承载背景与立绘的容器尺寸变化 → 交给 VM 重算立绘位置与缩放。
    /// 手机版与桌面版都有各自的容器，只认当前可见的那一个（隐藏的那个尺寸是陈旧的）。
    /// </summary>
    private void OnStageSizeChanged(object? sender, EventArgs e)
    {
        if (BindingContext is not MainGameViewModel vm) return;
        if (sender is not VisualElement el || !el.IsVisible || el.Width <= 0 || el.Height <= 0) return;
        vm.OnStageSizeChanged(el.Width, el.Height, ReferenceEquals(el, PhoneStage));
    }

    /// <summary>
    /// 未标定遮罩期间吃掉安卓返回键：遮罩本来就是"不选一个出口不让走"，
    /// 返回键绕过去等于把强制标定变成可以随手跳过。
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        if (BindingContext is MainGameViewModel { IsCalibrationBlocked: true })
            return true;
        return base.OnBackButtonPressed();
    }

    /// <summary>离开主页（如进 CG/设置）暂停场景视频，避免声音与画面串场；返回后从当前位置恢复。</summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        PauseSceneVideo();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ResumeSceneVideo();
        HookDesktopArrowKeys();
        // 主页也纳入全站进场契约：只演一次（Arrive 的弱表），从 CG/设置返回不会重播。
        Motion.ArrivePage(this);
    }

    // ============ #9 电脑端键盘方向键切换地点 ============
    // MAUI 的 Button 没有 KeyboardAccelerators 属性（那只在 MenuFlyoutItem/ToolbarItem 上），
    // 所以按项目已有做法（见 WeChatPage）在平台层挂按键：WinUI 窗口级 KeyDown 里接左右方向键。
    // 无地点（CanGoPrev/NextScene=false）时命令内部静默忽略，与按钮置灰一致。
#if WINDOWS
    private bool _arrowsHooked;

    private void HookDesktopArrowKeys()
    {
        if (_arrowsHooked) return;
        try
        {
            if (Application.Current?.Windows.Count > 0
                && Application.Current.Windows[0].Handler?.PlatformView is Microsoft.UI.Xaml.Window w
                && w.Content is Microsoft.UI.Xaml.UIElement root)
            {
                root.KeyDown += (_, e) =>
                {
                    if (BindingContext is not MainGameViewModel vm) return;
                    switch (e.Key)
                    {
                        case Windows.System.VirtualKey.Left:
                            e.Handled = true;
                            if (vm.PrevSceneCommand.CanExecute(null)) vm.PrevSceneCommand.Execute(null);
                            break;
                        case Windows.System.VirtualKey.Right:
                            e.Handled = true;
                            if (vm.NextSceneCommand.CanExecute(null)) vm.NextSceneCommand.Execute(null);
                            break;
                        // #E4：ESC 隐藏/恢复左侧信息栏与顶部状态栏
                        case Windows.System.VirtualKey.Escape:
                            e.Handled = true;
                            vm.ToggleHidePanelsCommand.Execute(null);
                            break;
                        // #E4：F11 全屏（完全全屏、覆盖任务栏）/ 退出全屏
                        case Windows.System.VirtualKey.F11:
                            e.Handled = true;
                            ToggleFullScreen();
                            break;
                    }
                };
                _arrowsHooked = true;
            }
        }
        catch (Exception ex) { App.WriteLog("MainGame.HookDesktopArrowKeys -> " + ex); }
    }

    /// <summary>#E4：WinUI 全屏切换——全屏时用 FullScreen presenter（覆盖任务栏），再按恢复窗口。</summary>
    private void ToggleFullScreen()
    {
        try
        {
            if (Application.Current?.Windows.Count > 0
                && Application.Current.Windows[0].Handler?.PlatformView is Microsoft.UI.Xaml.Window w
                && w.AppWindow is { } app)
            {
                if (app.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
                    app.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
                else
                    app.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
            }
        }
        catch (Exception ex) { App.WriteLog("MainGame.ToggleFullScreen -> " + ex); }
    }
#else
    private void HookDesktopArrowKeys() { }
#endif

    private void PauseSceneVideo()
    {
        try
        {
            if (SceneVideoPhone is { Source: not null }) SceneVideoPhone.Pause();
            if (SceneVideoDesktop is { Source: not null }) SceneVideoDesktop.Pause();
        }
        catch { }
    }

    private void ResumeSceneVideo()
    {
        try
        {
            if (SceneVideoPhone is { Source: not null, IsVisible: true }) SceneVideoPhone.Play();
            if (SceneVideoDesktop is { Source: not null, IsVisible: true }) SceneVideoDesktop.Play();
        }
        catch { }
    }
}