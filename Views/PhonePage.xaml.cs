using WarmAsBefore.Controls;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class PhonePage : ContentPage
{
    public PhonePage(PhoneViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        // 状态栏/时钟要等 OnAppearing 才落值：TimeLabel 初始为空，正好当就绪信号。
        LoadingVeil.Attach(this, vm, "TimeLabel");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is PhoneViewModel vm) vm.OnAppearing();
        Motion.ArrivePage(this);
    }
}