using WarmAsBefore.Controls;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class ShowcaseListPage : ContentPage
{
    private readonly ShowcaseListViewModel _vm;
    public ShowcaseListPage(ShowcaseListViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        InitializeComponent();
        LoadingVeil.Attach(this, vm, "Scripts");   // 剧本要扫盘：Scripts 是 VM 已有的集合
    }
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.LoadCommand.Execute(null);
        Motion.ArrivePage(this);
    }
}