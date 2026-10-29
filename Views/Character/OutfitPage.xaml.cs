using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class OutfitPage : ContentPage
{
    private readonly OutfitViewModel _vm;

    public OutfitPage(OutfitViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _vm.RefreshAsync();
        WarmAsBefore.Helpers.Motion.ArrivePage(this);
    }
}
