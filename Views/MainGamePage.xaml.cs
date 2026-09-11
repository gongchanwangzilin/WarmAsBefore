using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class MainGamePage : ContentPage
{
    public MainGamePage(MainGameViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}