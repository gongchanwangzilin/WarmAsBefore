using WarmAsBefore.Controls;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class SavePage : ContentPage
{
    private readonly SaveViewModel _vm;

    public SavePage(SaveViewModel vm)
    {
        using (WarmAsBefore.Services.PerfLog.Measure("页 · 存档 · XAML 膨胀"))
            InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        LoadingVeil.Attach(this, vm, "Slots");   // 存档要逐个读盘：Slots 是 VM 已有的属性
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // 回到存档列表：撤掉「进存档自动横屏」，恢复用户原来的方向选择
        WarmAsBefore.Modules.Screen.ScreenOrientationService.ExitSaveLandscape();
        Motion.ArrivePage(this);
        using var _ = WarmAsBefore.Services.PerfLog.Measure("页 · 存档 · 刷新");
        await _vm.RefreshCommand.ExecuteAsync(null);
    }
}