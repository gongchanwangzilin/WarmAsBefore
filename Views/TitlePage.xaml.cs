using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class TitlePage : ContentPage
{
    public TitlePage(TitleViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is TitleViewModel vm)
            vm.RefreshStateCommand.Execute(null);
    }
}
