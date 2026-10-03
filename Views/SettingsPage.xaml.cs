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
        vm.RefreshSandboxTrust();
#if ANDROID
        // 3 秒防呆到点（服务已自动回弹）→ 弹一次确认框，用户选「保留新方向」则重新锁定
        ScreenOrientationService.DebouncedRevert += OnDebouncedRevert;
        Unloaded += (_, _) => ScreenOrientationService.DebouncedRevert -= OnDebouncedRevert;
#endif
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
