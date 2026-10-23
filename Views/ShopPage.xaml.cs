using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class ShopPage : ContentPage
{
    public ShopPage(ShopViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            if (BindingContext is ShopViewModel vm) vm.OnAppearing();
        }
        catch (Exception ex)
        {
            App.WriteLog($"ShopPage.OnAppearing -> {ex.Message}");
        }
        // 进场：原来是 Content.Opacity = 0 + FadeTo(1)——和 GamePage 同一个坑，透明度被进场占用，
        // 动画一被打断整页就留在看不见那一头。改成全站唯一入口，只做位移。
        Motion.ArrivePage(this);
    }
}