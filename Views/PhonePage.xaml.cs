using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class PhonePage : ContentPage
{
    public PhonePage(PhoneViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is PhoneViewModel vm) vm.OnAppearing();
    }
}