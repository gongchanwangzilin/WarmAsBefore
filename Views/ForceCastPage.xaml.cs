using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;
namespace WarmAsBefore.Views;
public partial class ForceCastPage : ContentPage
{
    public ForceCastPage(ForceCastViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ForceCastViewModel vm) _ = vm.LoadAsync();
        Motion.ArrivePage(this);
    }
}