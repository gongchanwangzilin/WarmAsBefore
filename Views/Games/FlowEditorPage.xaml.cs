using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>流程图编辑器：一条条规则用人话读，点开就改；试跑当场看效果。</summary>
public partial class FlowEditorPage : ContentPage
{
    private readonly FlowEditorViewModel _vm;

    public FlowEditorPage(FlowEditorViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 图可能刚在地图页被换过：每次进来重新读一遍规则表
        _vm.Refresh();
        Motion.ArrivePage(this);
    }
}
