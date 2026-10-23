using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

[QueryProperty(nameof(PluginId), "id")]
public partial class PluginPermissionsPage : ContentPage
{
    private readonly PluginPermissionsViewModel _vm;
    private bool _loaded;

    public PluginPermissionsPage(PluginPermissionsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    public string PluginId { get; set; } = "";

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        _ = _vm.LoadAsync(Uri.UnescapeDataString(PluginId));
        Helpers.Motion.ArrivePage(this);
    }
}
