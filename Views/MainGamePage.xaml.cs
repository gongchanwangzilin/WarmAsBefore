using WarmAsBefore.Controls;
using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class MainGamePage : ContentPage
{
    private readonly MainGameViewModel _vm;
    private readonly CharacterBackdropPresenter _backdrop;
    private MainGamePhoneView? _phone;
    private bool _started;
    private bool _backdropSynced;

    public MainGamePage(MainGameViewModel vm)
    {
        using (WarmAsBefore.Services.PerfLog.Measure("页 · 主界面 · XAML 膨胀"))
            InitializeComponent();
        BindingContext = vm;
        _vm = vm;
        _backdrop = new CharacterBackdropPresenter(CharacterBgA, CharacterBgB, CharacterBgHost);
        // 窗口尺寸一变（拉窗、分屏）舞台预算就变；栏的显隐同样会改预算，但那时舞台
        // 未必收到 SizeChanged（它被立绘钉住了），所以两处都要主动再量一次。
        SizeChanged += OnPageSizeChanged;
        vm.PropertyChanged += OnVmPropertyChanged;
        // 手机竖版整块只在手机版式下构建：Windows 上它永远不显示，却会和桌面版一起建完
        // （两条 MediaElement、两个 CollectionView、两百来颗控件），那就是"进存档卡一下"的大头。
        if (vm.IsPhoneLayout)
        {
            using var _ = WarmAsBefore.Services.PerfLog.Measure("页 · 主界面 · 手机版构建");
            _phone = new MainGamePhoneView();
            PhoneHost.Content = _phone;
        }
    }

    /// <summary>
    /// 承载背景与立绘的容器尺寸变化 → 交给 VM 重算立绘位置与缩放。
    /// 手机版在 MainGamePhoneView 里自己上报；这里只认桌面版这一块。
    /// </summary>
    private void OnStageSizeChanged(object? sender, EventArgs e)
    {
        if (sender is VisualElement el) ReportStageSize(el);
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (BindingContext is MainGameViewModel { IsDesktopLayout: true }) ReportStageSize(DesktopStage);
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainGameViewModel.CharacterBackdrop))
        {
            MainThread.BeginInvokeOnMainThread(() => _ = ApplyCharacterBackdropAsync());
            return;
        }
        if (e.PropertyName is not (nameof(MainGameViewModel.HideDesktopPanels)
            or nameof(MainGameViewModel.IsRightPanelVisible)
            or nameof(MainGameViewModel.WidgetModeEnabled)
            or nameof(MainGameViewModel.IsWidgetPanelVisible))) return;
        if (!_vm.IsDesktopLayout) return;
        // 等这一帧的布局跑完再量：此刻舞台的 X/Y 还是旧值。
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(32), () => ReportStageSize(DesktopStage));
    }

    /// <summary>角色背景换场：两层图片交叉淡入；没有新背景时收掉，露出场景底色。</summary>
    private async Task ApplyCharacterBackdropAsync(bool instant = false)
    {
        if (!_vm.IsDesktopLayout) return;
        var state = _vm.CharacterBackdrop;
        try
        {
            if (state is null)
            {
                await _backdrop.ApplyAsync(null, "fill", 0, 0, 1, 1, false, instant);
                return;
            }
            await _backdrop.ApplyAsync(
                state.Path, state.Fit, state.CropX, state.CropY, state.CropW, state.CropH,
                state.IsVideo, instant);
        }
        catch (Exception ex) { App.WriteLog("MainGame.CharacterBackdrop -> " + ex.Message); }
    }

    /// <summary>
    /// 上报给 VM 的舞台尺寸 = 「窗口可用空间」，而不是舞台自己被量出来的尺寸。
    /// 星号行列会被子元素的期望尺寸撑大（MAUI GridLayoutManager 对星号列取
    /// max(star, child.DesiredSize)），而立绘的固定宽高正是上一次测量算出来的：
    /// 一旦舞台被撑得比窗口还大，它就再也收不回来 —— 尺寸不变 → SizeChanged 不触发
    /// → 立绘不重算 → 舞台连同铺满它的输入栏一直顶在窗外。
    /// 页面尺寸才是窗口，减去舞台偏移和可见的右栏，就是它真正能用的地方。
    /// </summary>
    private void ReportStageSize(VisualElement el)
    {
        if (BindingContext is not MainGameViewModel vm) return;
        if (!el.IsVisible || el.Width <= 0 || el.Height <= 0 || Width <= 0 || Height <= 0) return;
        var right = RightChatPanel.IsVisible ? Math.Max(RightChatPanel.Width, RightChatPanel.WidthRequest) : 0;
        var w = Math.Min(el.Width, Width - el.X - right);
        var h = Math.Min(el.Height, Height - el.Y);
        if (w < 1 || h < 1) return;
        vm.OnStageSizeChanged(w, h, false);
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
        _vm.SyncWidgetModeFromSettings();
        if (!_backdropSynced)
        {
            _backdropSynced = true;
            _ = ApplyCharacterBackdropAsync(instant: true);
        }
        else
        {
            _backdrop.Resume();
        }
        HookDesktopArrowKeys();
        // 主页也纳入全站进场契约：只演一次（Arrive 的弱表），从 CG/设置返回不会重播。
        Motion.ArrivePage(this);
        StartGameOnce();
    }

    /// <summary>
    /// 启动重活（读角色/立绘、地图、天气）等过场演完再跑：进门这段只留给画面。
    /// 压在构造函数里会和 190ms 的进场位移抢主线程，读起来就是「卡一下、动画也没了」。
    /// </summary>
    private void StartGameOnce()
    {
        if (_started) return;
        _started = true;
        if (MotionTokens.Disabled) { _vm.Begin(); return; }
        Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(MotionTokens.PageSlideMs + MotionTokens.FrameMs),
            _vm.Begin);
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
                        // #E4：F11 全屏（完全全屏、覆盖任务栏）/ 退出全屏
                        case Windows.System.VirtualKey.F11:
                            e.Handled = true;
                            ToggleFullScreen();
                            break;
                    }
                };
                // #E4：ESC 收起/恢复信息栏与顶部。单独走 handledEventsToo 通道——
                // 焦点在聊天输入框里时 KeyDown 会先被 Entry 消费，普通订阅根本收不到，
                // 表现就是「明明可以收起，按 ESC 却没反应」。
                root.AddHandler(
                    Microsoft.UI.Xaml.UIElement.KeyDownEvent,
                    new Microsoft.UI.Xaml.Input.KeyEventHandler((_, e) =>
                    {
                        if (e.Key != Windows.System.VirtualKey.Escape) return;
                        if (BindingContext is not MainGameViewModel vm) return;
                        vm.ToggleHidePanelsCommand.Execute(null);
                        e.Handled = true;
                    }),
                    handledEventsToo: true);
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
            _phone?.Pause();
            if (SceneVideoDesktop is { Source: not null }) SceneVideoDesktop.Pause();
            _backdrop.Pause();
        }
        catch { }
    }

    private void ResumeSceneVideo()
    {
        try
        {
            _phone?.Play();
            if (SceneVideoDesktop is { Source: not null, IsVisible: true }) SceneVideoDesktop.Play();
        }
        catch { }
    }
}