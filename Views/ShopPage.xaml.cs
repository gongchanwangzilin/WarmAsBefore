using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class ShopPage : ContentPage
{
    public ShopPage(ShopViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override async void OnAppearing()
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
        // 入场：内容轻微上浮 + 渐入
        Content.Opacity = 0;
        Content.TranslationY = 12;
        _ = Content.FadeTo(1, 200, Easing.CubicOut);
        _ = Content.TranslateTo(0, 0, 240, Easing.CubicOut);
    }
}