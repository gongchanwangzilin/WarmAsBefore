namespace WarmAsBefore.Views;

public partial class OutfitPage : ContentPage
{
    public OutfitPage() => InitializeComponent();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 这页目前是占位（没有 ViewModel、没有装载过程），进场照样走全站唯一入口，
        // 但不给它铺骨架：没有「还没到」的东西，摆一副假骨架是在撒谎。
        WarmAsBefore.Helpers.Motion.ArrivePage(this);
    }
}