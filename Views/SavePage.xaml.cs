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
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 回到存档列表：撤掉「进存档自动横屏」，恢复用户原来的方向选择
        WarmAsBefore.Modules.Screen.ScreenOrientationService.ExitSaveLandscape();
        _vm.RefreshCommand.Execute(null);
    }
}