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
            vm.RefreshSandboxTrust();
            // 首帧渲染后再收起页面级 loading 遮罩（让转场动画完整播放）
            // 延迟 32ms（两帧）再收起遮罩，确保转场动画首帧已渲染、布局已就绪
            _ = System.Threading.Tasks.Task.Delay(32).ContinueWith(_ =>
                MainThread.BeginInvokeOnMainThread(() => vm.PageLoading = false));
        }
    }

#if ANDROID
    private async void OnDebouncedRevert(string revertedTo)
    {
        var vm = Vm as SettingsViewModel;
        if (vm is null) return;
        // 同步 UI Picker 到回弹后的方向
        var display = revertedTo switch
        {
            "landscape" => "横屏",
            "portrait" => "竖屏",
            _ => "自动（竖屏）",
        };
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            vm.ScreenOrientationDisplay = display;
            var act = await DisplayActionSheet(
                "3 秒内未确认，已自动变回原方向。", "取消", null, "保留新方向", "仍用原方向");
            if (act == "保留新方向")
            {
                await vm.ConfirmOrientationKeepNewAsync();
            }
        });
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
