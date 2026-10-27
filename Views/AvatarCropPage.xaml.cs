using WarmAsBefore.Helpers;
using WarmAsBefore.Services;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 头像裁剪页。参数：<c>avatarcrop?characterId={id}&amp;sourcePath={绝对路径}</c>（两者都要 URL 转义）。
/// 工作室也可以不拼路由，直接 await <see cref="AvatarCropService.OpenAsync"/>，它会带参数进来并等结果。
///
/// 坐标只走一条路：Pan/Pinch 给的是框内像素，VM 换成「框边长比例」再交给服务；
/// Windows 的滚轮从原生指针事件补进来（MAUI 的 Touch 不含滚轮），其余缩放交给滑杆。
/// </summary>
[QueryProperty(nameof(CharacterId), "characterId")]
[QueryProperty(nameof(SourcePath), "sourcePath")]
public partial class AvatarCropPage : ContentPage
{
    private readonly AvatarCropViewModel _vm;
    private readonly AvatarCropService _crop;
    private bool _loaded;
    private double _lastPanX, _lastPanY;

    public AvatarCropPage(AvatarCropViewModel vm, AvatarCropService crop)
    {
        InitializeComponent();
        _vm = vm;
        _crop = crop;
        BindingContext = vm;
    }

    public string CharacterId { get; set; } = "";
    public string SourcePath { get; set; } = "";

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Motion.ArrivePage(this);
        if (_loaded) return;
        _loaded = true;

        var id = string.IsNullOrWhiteSpace(CharacterId) ? _crop.PendingCharacterId ?? "" : Uri.UnescapeDataString(CharacterId);
        var path = string.IsNullOrWhiteSpace(SourcePath) ? _crop.PendingSourcePath ?? "" : Uri.UnescapeDataString(SourcePath);
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(path))
        {
            // 被直接导航进来：空跑一次就退，别把调用方搁在那儿等
            MainThread.BeginInvokeOnMainThread(async () => await _vm.CancelCommand.ExecuteAsync(null));
            return;
        }
        _ = _vm.LoadAsync(id, path);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.Cleanup();
    }

    protected override bool OnBackButtonPressed()
    {
        MainThread.BeginInvokeOnMainThread(async () => await _vm.CancelCommand.ExecuteAsync(null));
        return true;
    }

    private void OnStageSizeChanged(object? sender, EventArgs e)
    {
        if (Stage.Width <= 0 || Stage.Height <= 0) return;
        // 取景框跟着可用空间走；桌面上封顶 620，别让一张脸占到半张桌子
        var side = Math.Min(Math.Min(Stage.Width - 32, Stage.Height - 40), 620);
        side = Math.Max(120, side);
        if (Math.Abs(side - CropFrame.WidthRequest) < 0.5) return;
        CropFrame.WidthRequest = side;
        CropFrame.HeightRequest = side;
        _vm.SetFrameSize(side);
    }

    private void OnFramePan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _lastPanX = 0;
                _lastPanY = 0;
                break;
            case GestureStatus.Running:
                _vm.PanBy(e.TotalX - _lastPanX, e.TotalY - _lastPanY);
                _lastPanX = e.TotalX;
                _lastPanY = e.TotalY;
                break;
        }
    }

    private void OnFramePinch(object? sender, PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started:
                _vm.BeginPinch(e.ScaleOrigin.X, e.ScaleOrigin.Y);
                break;
            case GestureStatus.Running:
                _vm.UpdatePinch(e.Scale);
                break;
        }
    }

    private async void OnConfirmClicked(object? sender, EventArgs e) => await _vm.ConfirmCommand.ExecuteAsync(null);

    private async void OnCancelClicked(object? sender, EventArgs e) => await _vm.CancelCommand.ExecuteAsync(null);

#if WINDOWS
    private bool _hooked;
    private Microsoft.UI.Xaml.FrameworkElement? _winEl;

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (_hooked) return;
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement element) return;
        _winEl = element;
        element.Loaded += OnWinLoaded;
        if (element.IsLoaded) OnWinLoaded(element, new Microsoft.UI.Xaml.RoutedEventArgs());
    }

    private void OnWinLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_hooked || _winEl is null) return;
        _hooked = true;
        _winEl.AddHandler(Microsoft.UI.Xaml.UIElement.PointerWheelChangedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnWinPointerWheel), true);
    }

    private void OnWinPointerWheel(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (CropFrame.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement frame) return;
        var pt = e.GetCurrentPoint(frame);
        var x = pt.Position.X;
        var y = pt.Position.Y;
        if (x < 0 || y < 0 || x > CropFrame.Width || y > CropFrame.Height) return;
        var delta = pt.Properties.MouseWheelDelta;
        if (delta == 0) return;
        frame.DispatcherQueue.TryEnqueue(() => _vm.ZoomBy(delta > 0 ? 1.12 : 1 / 1.12, x, y));
    }
#endif
}
