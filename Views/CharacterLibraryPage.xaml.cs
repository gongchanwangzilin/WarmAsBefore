using WarmAsBefore.Controls;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class CharacterLibraryPage : ContentPage
{
    public CharacterLibraryPage(CharacterLibraryViewModel vm)
    {
        using (WarmAsBefore.Services.PerfLog.Measure("页 · 角色库 · XAML 膨胀"))
            InitializeComponent();
        BindingContext = vm;
        // 角色库要先读盘才填得上：数据到位前铺一层骨架，信号用 VM 已有的 Items（集合非空即就绪）。
        LoadingVeil.Attach(this, vm, "Items");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Motion.ArrivePage(this);
        using var _ = WarmAsBefore.Services.PerfLog.Measure("页 · 角色库 · 刷新");
        if (BindingContext is CharacterLibraryViewModel vm) await vm.RefreshCommand.ExecuteAsync(null);
    }
}
