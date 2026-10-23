using WarmAsBefore.Controls;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class NovelSelectPage : ContentPage
{
    public NovelSelectPage(NovelSelectViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        // 小说库要扫目录，空列表在装载前后看起来一样：用已有的 Novels 当就绪信号铺骨架。
        LoadingVeil.Attach(this, vm, "Novels");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is NovelSelectViewModel vm) _ = vm.RefreshCommand.ExecuteAsync(null);
        Motion.ArrivePage(this);
    }
}
