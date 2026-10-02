using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class CharacterSelectPage : ContentPage
{
    public CharacterSelectPage(CharacterSelectViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is CharacterSelectViewModel vm)
            vm.RefreshCommand.Execute(null);
    }
}
