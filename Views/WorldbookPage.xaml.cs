using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class WorldbookPage : ContentPage
{
    private readonly WorldbookViewModel _vm;

    public WorldbookPage(WorldbookViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_vm.IsEditing) _ = _vm.LoadAsync();
        Motion.ArrivePage(this);
    }
}
