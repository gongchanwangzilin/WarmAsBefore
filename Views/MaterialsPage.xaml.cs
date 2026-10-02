using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class MaterialsPage : ContentPage
{
    public MaterialsPage(MaterialsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is MaterialsViewModel vm) _ = vm.RefreshCommand.ExecuteAsync(null);
    }
}