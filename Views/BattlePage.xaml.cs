using Microsoft.Maui.Controls;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class BattlePage : ContentPage
{
    public BattlePage()
    {
        InitializeComponent();
        BindingContext = new BattleViewModel();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var vm = (BattleViewModel)BindingContext;
        if (vm.IsInBattle) return;
        _ = StartBattleSafeAsync(vm);
    }

    private static async Task StartBattleSafeAsync(BattleViewModel vm)
    {
        try
        {
            await vm.StartBattleAsync();
        }
        catch (Exception ex)
        {
            App.WriteLog("BattlePage.StartBattle -> " + ex);
            vm.AbortStartup($"战斗启动失败：{ex.Message}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ((BattleViewModel)BindingContext).Cleanup(false);
    }
}