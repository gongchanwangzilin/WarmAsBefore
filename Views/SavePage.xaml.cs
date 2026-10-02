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
        _vm.RefreshCommand.Execute(null);
    }
}