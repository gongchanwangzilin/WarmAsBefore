using WarmAsBefore.Modules.Screen;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class SettingsPage : ContentPage
{
    private SettingsViewModel Vm => (SettingsViewModel)BindingContext;

    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
#if ANDROID
        // 3 秒防呆到点（服务已自动回弹）→ 弹一次确认框，用户选「保留新方向」则重新锁定
        ScreenOrientationService.DebouncedRevert += OnDebouncedRevert;
        Unloaded += (_, _) => ScreenOrientationService.DebouncedRevert -= OnDebouncedRevert;
#endif
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 延迟重活到首帧渲染之后，避免转场动画期间阻塞 UI 线程
        var vm = BindingContext as SettingsViewModel;
        if (vm is not null)
        {
            // RefreshSandboxTrust 是同步文件 IO，放后台跑，避免阻塞 UI 线程（ANR 风险）
            _ = System.Threading.Tasks.Task.Run(() => vm.RefreshSandboxTrustAsync());
            // 首帧渲染后再收起页面级 loading 遮罩（让转场动画完整播放）
            // 延迟 32ms（两帧）再收起遮罩，确保转场动画首帧已渲染、布局已就绪
            _ = System.Threading.Tasks.Task.Delay(32).ContinueWith(_ =>
                MainThread.BeginInvokeOnMainThread(() => vm.PageLoading = false));
        }
    }

#if ANDROID
    private void OnDebouncedRevert(string revertedTo)
    {
        // 事件可能在线程池线程上触发（ScreenOrientationService 的 Task.Delay.ContinueWith），
        // 一律投回主线程再碰 UI；用 try/catch 兜住，避免 async void 内异常进 UnhandledException 崩进程。
        try
        {
            var display = revertedTo switch
            {
                "landscape" => "横屏",
                "portrait" => "竖屏",
                _ => "自动（竖屏）",
            };
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    var vm = BindingContext as SettingsViewModel;
                    if (vm is null) return;
                    // 页面若已不在导航栈（卸载中/转场中），不弹框，仅同步 Picker 状态
                    vm.ScreenOrientationDisplay = display;
                    var act = await DisplayActionSheet(
                        "3 秒内未确认，已自动变回原方向。", "取消", null, "保留新方向", "仍用原方向");
                    if (act == "保留新方向")
                    {
                        await vm.ConfirmOrientationKeepNewAsync();
                    }
                }
                catch (Exception ex)
                {
                    // 弹窗/确认失败（如 Shell 已空）不应崩进程
                    App.WriteLog("OnDebouncedRevert: " + ex);
                }
            });
        }
        catch (Exception ex)
        {
            App.WriteLog("OnDebouncedRevert (schedule): " + ex);
        }
    }
#endif

    /// <summary>滚动到底部时显示开发者展示隐藏入口。</summary>
    private void OnScrollViewScrolled(object? sender, ScrolledEventArgs e)
    {
        if (BindingContext is not SettingsViewModel vm) return;
        if (sender is not ScrollView sv || sv.Content is not VerticalStackLayout layout) return;
        var remaining = layout.Height - sv.Height - e.ScrollY;
        vm.AtScrollBottom = remaining <= 24;
    }
}
