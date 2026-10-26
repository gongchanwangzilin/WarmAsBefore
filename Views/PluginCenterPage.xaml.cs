using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class PluginCenterPage : ContentPage
{
    private readonly PluginCenterViewModel _vm;

    public PluginCenterPage(PluginCenterViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadAsync();
        Helpers.Motion.ArrivePage(this);
    }

    /// <summary>
    /// 扫描在后台跑，列表回来时按终末地的节奏一张张摆上来——顺带把「扫描完成 → 渲染」
    /// 这一拍盖住。BindableLayout 的容器要等一次布局才挂齐，所以这里让出一帧再取。
    /// </summary>
    private async Task LoadAsync()
    {
        await _vm.RefreshAsync();
        await Task.Delay(16);
        var rows = RowsHost.Children.OfType<VisualElement>().ToList();
        if (rows.Count == 0) return;
        Helpers.Motion.Arrive(rows,
            (uint)Math.Max(1, DesignSystem.Theme.MotionTokens.Ms(DesignSystem.Theme.MotionTokens.CardEnterMs)));
    }
}
