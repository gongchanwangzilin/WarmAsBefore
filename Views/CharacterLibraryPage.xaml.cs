using WarmAsBefore.Controls;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class CharacterLibraryPage : ContentPage
{
    public CharacterLibraryPage(CharacterLibraryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        // 角色库要先读盘才填得上：数据到位前铺一层骨架，信号用 VM 已有的 Items（集合非空即就绪）。
        LoadingVeil.Attach(this, vm, "Items");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is CharacterLibraryViewModel vm) _ = vm.RefreshCommand.ExecuteAsync(null);
        Motion.ArrivePage(this);
    }
}
