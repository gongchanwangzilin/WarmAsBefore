using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class WorldbookPage : ContentPage
{
    public WorldbookPage(WorldbookViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}