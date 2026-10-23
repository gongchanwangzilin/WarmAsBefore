using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class PluginCenterPage : ContentPage
{
    private readonly PluginCenterViewModel _vm;

    public PluginCenterPage(PluginCenterViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _vm.RefreshAsync();
        Helpers.Motion.ArrivePage(this);
    }
}
