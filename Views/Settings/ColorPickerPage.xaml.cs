using WarmAsBefore.Drawables;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 调色页（设置 → 背景 → 选个颜色…）。
///
/// 三块画布各管一件事，状态全在 <see cref="ColorPickerViewModel"/> 里：
///   PreviewCanvas  这块色当地面时的样子（糊底光斑 + 半透明卡面）
///   FieldCanvas    饱和度 / 明度方块 + 准星
///   HueCanvas      色相条 + 游标
/// 画布都是 InputTransparent，手势挂在盖在上面的透明 Grid 上 —— 事件不被 CanvasControl 吃掉，
/// 坐标也就在一个已知尺寸的容器里（FieldTouch / HueTouch）归一化。
///
/// 拖动只累加相邻两次 Total 的差：MAUI 在 Windows 上 Started 那一刻的 TotalX/TotalY 是 0
///（ManipulationDelta 的 Cumulative 从按下的那点起算，Started 由第一次位移触发），
/// 把它当绝对坐标会把准星甩到左上角。差量与平台语义无关，所以这里只认差量。
/// </summary>
public partial class ColorPickerPage : ContentPage
{
    private readonly ColorPickerViewModel _vm;
    private readonly ColorFieldDrawable _field = new();
    private readonly HueBarDrawable _hueBar = new();
    private readonly ColorPreviewDrawable _preview = new();

    private double _fieldPanX, _fieldPanY;
    private double _huePanX;

    public ColorPickerPage(ColorPickerViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        FieldCanvas.Drawable = SafeDrawable.Wrap(_field, "ColorPicker.Field");
        HueCanvas.Drawable = SafeDrawable.Wrap(_hueBar, "ColorPicker.Hue");
        PreviewCanvas.Drawable = SafeDrawable.Wrap(_preview, "ColorPicker.Preview");

        _vm.AttachInvalidate(SyncCanvases);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Motion.ArrivePage(this);
        // 首次出现时画布多半还没测出尺寸：先排一帧，尺寸到了 Win2D 会自己按新矩形重画。
        SyncCanvases();
    }

    protected override bool OnBackButtonPressed()
    {
        UiGuard.Run(() => _vm.CancelCommand.ExecuteAsync(null), "ColorPicker.OnBackButtonPressed");
        return true;
    }

    /// <summary>状态变了就整组重画：三块画布读同一份 H/S/V，做到预览与准星同帧。</summary>
    private void SyncCanvases()
    {
        _field.HueColor = _vm.PureHue;
        _field.Saturation = _vm.Saturation;
        _field.Value = _vm.Value;
        _hueBar.Hue = _vm.Hue;
        _preview.Fill = _vm.Current;

        FieldCanvas.Invalidate();
        HueCanvas.Invalidate();
        PreviewCanvas.Invalidate();
    }

    private void OnFieldTapped(object? sender, TappedEventArgs e)
    {
        if (FieldTouch.Width <= 0 || FieldTouch.Height <= 0) return;
        if (e.GetPosition(FieldTouch) is not { } p) return;
        _vm.SetSv(p.X / FieldTouch.Width, p.Y / FieldTouch.Height);
    }

    private void OnHueTapped(object? sender, TappedEventArgs e)
    {
        if (HueTouch.Width <= 0) return;
        if (e.GetPosition(HueTouch) is not { } p) return;
        _vm.SetHue(p.X / HueTouch.Width);
    }

    private void OnFieldPan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _fieldPanX = e.TotalX;
                _fieldPanY = e.TotalY;
                break;
            case GestureStatus.Running:
                _vm.NudgeSv(e.TotalX - _fieldPanX, e.TotalY - _fieldPanY, FieldTouch.Width, FieldTouch.Height);
                _fieldPanX = e.TotalX;
                _fieldPanY = e.TotalY;
                break;
        }
    }

    private void OnHuePan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _huePanX = e.TotalX;
                break;
            case GestureStatus.Running:
                _vm.NudgeHue(e.TotalX - _huePanX, HueTouch.Width);
                _huePanX = e.TotalX;
                break;
        }
    }
}
