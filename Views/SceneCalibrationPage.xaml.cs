using System.Linq;
using Microsoft.Maui.Graphics;
using WarmAsBefore.Services;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 标定页。
///
/// 桌面端需要额外的右键/滚轮事件源（MAUI GraphicsView 的 Touch 事件不含右键与滚轮），
/// 做法与 MapPage 一致：Win2D 的 CanvasControl 会先把指针事件标记为 handled，
/// 所以必须 AddHandler(handledEventsToo: true)，且坐标要相对画布原生元素而不是页面根。
/// </summary>
public partial class SceneCalibrationPage : ContentPage
{
    private readonly SceneCalibrationViewModel _vm;
    private readonly SceneCalibrationService _cal;
    private readonly MapService _maps;
    private bool _loaded;

    public SceneCalibrationPage(SceneCalibrationViewModel vm, SceneCalibrationService cal, MapService maps)
    {
        InitializeComponent();
        _vm = vm;
        _cal = cal;
        _maps = maps;
        BindingContext = vm;
        CalCanvas.Drawable = vm.Drawable;
        vm.AttachInvalidate(() => CalCanvas.Invalidate());
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;

        var scene = _cal.PendingScene;
        if (scene is null)
        {
            // 没有待标定对象（例如被直接导航进来）：空跑一次直接退回，避免卡住调用方
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                _vm.MarkFinished();
                _vm.Finish(false);
                if (Shell.Current is not null) await Shell.Current.GoToAsync("..");
            });
            return;
        }

        var abs = _maps.ResolveBackground(scene);
        // 视频背景没法直接探尺寸也没法当画布底图，退用首帧缩略图；缩略图缺失则尺寸未知
        if (!string.IsNullOrEmpty(abs) && MapService.IsVideoExt(Path.GetExtension(abs)))
            abs = _maps.ThumbnailAbsFor(scene) ?? abs;

        var (w, h) = ImageProbe.SizeOf(abs);
        _vm.Load(scene, abs, w, h, _cal.TargetAspect);
        _vm.OnViewportChanged(CalCanvas.Width, CalCanvas.Height);
    }

    protected override bool OnBackButtonPressed()
    {
        // 安卓返回键等同于取消标定
        MainThread.BeginInvokeOnMainThread(async () => await LeaveAsync(false));
        return true;
    }

    private void OnCanvasSizeChanged(object? sender, EventArgs e) =>
        _vm.OnViewportChanged(CalCanvas.Width, CalCanvas.Height);

    private async void OnCompleteClicked(object? sender, EventArgs e)
    {
        if (!await _vm.TryCommitAsync()) return;
        await LeaveAsync(true);
    }

    private void OnCancelClicked(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(async () => await LeaveAsync(false));

    private async Task LeaveAsync(bool ok)
    {
        if (_vm.IsFinished) return;
        _vm.Finish(ok);
        if (Shell.Current is not null) await Shell.Current.GoToAsync("..");
    }

    // ============ 触摸（双端）============

    private void OnCanvasStart(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length >= 2) { BeginPinch(e); return; }
        _pinchStartDist = 0;
        _vm.CanvasStart(First(e));
    }

    private void OnCanvasDrag(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length >= 2) { UpdatePinch(e); return; }
        if (_pinchStartDist > 0)
        {
            // 从双指回到单指：重置捏合状态，并把这根手指当作新的一次按下
            _pinchStartDist = 0;
            _vm.CanvasStart(First(e));
            return;
        }
        _vm.CanvasDrag(First(e));
    }

    private void OnCanvasEnd(object? sender, TouchEventArgs e)
    {
        _pinchStartDist = 0;
        _vm.CanvasEnd(First(e));
    }

    private float _pinchStartDist;
    private double _pinchStartScale;
    private PointF _pinchCenter;

    private void BeginPinch(TouchEventArgs e)
    {
        var t = e.Touches;
        _pinchStartDist = Distance(t[0], t[1]);
        _pinchStartScale = _vm.Drawable.Scale;
        _pinchCenter = new PointF((t[0].X + t[1].X) / 2, (t[0].Y + t[1].Y) / 2);
    }

    private void UpdatePinch(TouchEventArgs e)
    {
        if (_pinchStartDist <= 0) { BeginPinch(e); return; }
        var t = e.Touches;
        var d = Distance(t[0], t[1]);
        if (d <= 0) return;
        _vm.SetScaleAt(_pinchStartScale * (d / _pinchStartDist), _pinchCenter);
    }

    private static float Distance(PointF a, PointF b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    private static PointF First(TouchEventArgs e) => e.Touches.FirstOrDefault();

#if WINDOWS
    private bool _hooked;
    private Microsoft.UI.Xaml.FrameworkElement? _winEl;
    private Microsoft.UI.Xaml.UIElement? _canvasEl;

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        HookWinUI();
    }

    private void HookWinUI()
    {
        if (_hooked) return;
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement element) return;
        _winEl = element;
        element.Loaded += OnWinLoaded;
        element.Unloaded += OnWinUnloaded;
        if (element.IsLoaded) OnWinLoaded(element, new Microsoft.UI.Xaml.RoutedEventArgs());
    }

    private void OnWinLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_hooked || _winEl is null) return;
        _hooked = true;
        _canvasEl = CalCanvas.Handler?.PlatformView as Microsoft.UI.Xaml.UIElement ?? _winEl;
        _winEl.AddHandler(Microsoft.UI.Xaml.UIElement.PointerPressedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnWinPointerPressed), true);
        _winEl.AddHandler(Microsoft.UI.Xaml.UIElement.PointerWheelChangedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnWinPointerWheelChanged), true);
    }

    private void OnWinUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => _hooked = false;

    private Microsoft.UI.Xaml.UIElement? CoordSource =>
        CalCanvas.Handler?.PlatformView as Microsoft.UI.Xaml.UIElement ?? _canvasEl ?? _winEl;

    private void OnWinPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (CoordSource is not { } source) return;
        var pt = e.GetCurrentPoint(source);
        if (!pt.Properties.IsRightButtonPressed) return;
        // 必须同步标记时间戳：右键的同一按压会先被 GraphicsView 变成 Touch 事件，
        // 不标记的话会顺带落一个点/开始画一条线。
        _vm.MarkRightPress();
        source.DispatcherQueue.TryEnqueue(() => _vm.RightClick());
    }

    private void OnWinPointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (CoordSource is not { } source) return;
        var pt = e.GetCurrentPoint(source);
        var delta = pt.Properties.MouseWheelDelta;
        var pos = new PointF((float)pt.Position.X, (float)pt.Position.Y);
        source.DispatcherQueue.TryEnqueue(() => _vm.Zoom(-delta, pos));
    }
#endif
}
