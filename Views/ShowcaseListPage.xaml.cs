using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class ShowcaseListPage : ContentPage
{
    private readonly ShowcaseListViewModel _vm;
    public ShowcaseListPage(ShowcaseListViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        InitializeComponent();
    }
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.LoadCommand.Execute(null);
    }
}