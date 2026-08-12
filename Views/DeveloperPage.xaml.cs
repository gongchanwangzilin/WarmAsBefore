using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class DeveloperPage : ContentPage
{
    public DeveloperPage(DeveloperViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}