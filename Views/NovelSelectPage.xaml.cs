using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class NovelSelectPage : ContentPage
{
    public NovelSelectPage(NovelSelectViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is NovelSelectViewModel vm) _ = vm.RefreshCommand.ExecuteAsync(null);
    }
}
