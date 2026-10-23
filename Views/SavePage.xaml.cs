using WarmAsBefore.Controls;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class SavePage : ContentPage
{
    private readonly SaveViewModel _vm;

    public SavePage(SaveViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        LoadingVeil.Attach(this, vm, "Slots");   // 存档要逐个读盘：Slots 是 VM 已有的属性
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 回到存档列表：撤掉「进存档自动横屏」，恢复用户原来的方向选择
        WarmAsBefore.Modules.Screen.ScreenOrientationService.ExitSaveLandscape();
        _vm.RefreshCommand.Execute(null);
        Motion.ArrivePage(this);
    }
}