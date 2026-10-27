using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>云存档浏览页：只看远端清单与历史副本，取回落盘仍交给 CloudSyncService。</summary>
public partial class CloudSavesPage : ContentPage
{
    private readonly CloudSavesViewModel _vm;

    public CloudSavesPage(CloudSavesViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Motion.ArrivePage(this);
        try { await _vm.RefreshCommand.ExecuteAsync(null); }
        catch (Exception ex) { App.WriteLog("CloudSavesPage.OnAppearing -> " + ex); }
    }
}
