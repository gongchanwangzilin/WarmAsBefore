using System.ComponentModel;
using CommunityToolkit.Maui.Core;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Controls;

/// <summary>
/// 人脸识别面板（结构见 FaceMonitorPane.xaml 顶部注释）：组件状态 + 图像流。
/// 绑定对象由宿主页给（BindingContext = FacePanelViewModel），这里不持有页面状态。
///
/// 面板只做一件事：相机预览的那半截（CameraView 是控件，起停必须贴着界面做）。
/// 什么时候该开、该关由 VM 决定——<see cref="FacePanelViewModel.LivePreview"/> 为真才起预览，
/// 变假或控件卸载立刻停；抓帧的节拍也不在这里，VM 的 2 秒拉帧表通过
/// <see cref="FacePanelViewModel.SampleRequested"/> 递过来，这里只管按一下快门。
/// </summary>
public partial class FaceMonitorPane : ContentView
{
    /// <summary>一帧的最大字节数：超过就当这一帧不正常，丢掉而不是往内存里塞。</summary>
    private const int MaxPreviewFrameBytes = 8 * 1024 * 1024;

    private FacePanelViewModel? _vm;
    private bool _previewing;
    private bool _starting;

    public FaceMonitorPane()
    {
        InitializeComponent();
        Preview.MediaCaptured += OnMediaCaptured;
        Preview.MediaCaptureFailed += OnMediaCaptureFailed;
        Loaded += (_, _) =>
        {
            if (_vm?.LivePreview == true) _ = StartPreviewAsync();
        };
        Unloaded += (_, _) => StopPreview();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.SampleRequested -= OnSampleRequested;
        }
        StopPreview();
        _vm = BindingContext as FacePanelViewModel;
        if (_vm is null) return;
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        _vm.SampleRequested += OnSampleRequested;
        if (_vm.LivePreview && IsLoaded) _ = StartPreviewAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FacePanelViewModel.LivePreview)) return;
        if (_vm?.LivePreview == true && IsLoaded) _ = StartPreviewAsync();
        else StopPreview();
    }

    private void OnSampleRequested() => _ = CaptureOnceAsync();

    /// <summary>起预览。打不开就如实告诉 VM（它会回落到最近一帧），不在这里重试。</summary>
    private async Task StartPreviewAsync()
    {
        if (_previewing || _starting) return;
        _starting = true;
        try
        {
            // 让 IsVisible 的那次变化先落到平台层：CameraView 复用了刚显示出来的预览表面。
            await Task.Yield();
            // 相机被占用 / 桌面应用没被系统放行时，这一步会抛或者一直不回话，所以压一个上限。
            await Preview.StartCameraPreview(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            _previewing = true;
        }
        catch (Exception ex)
        {
            App.WriteLog("FacePane.preview.start -> " + ex.GetType().Name);
            _vm?.ReportPreviewUnavailable(ex is TimeoutException
                ? "相机十秒没有回话（可能被别的应用占着）"
                : "相机没打开（" + ex.GetType().Name + "）");
        }
        finally { _starting = false; }
    }

    /// <summary>停预览：页面离开、面板卸载、或 VM 关门时都会走到这里，重复调用安全。</summary>
    private void StopPreview()
    {
        if (!_previewing) return;
        _previewing = false;
        try { Preview.StopCameraPreview(); }
        catch (Exception ex) { App.WriteLog("FacePane.preview.stop -> " + ex.GetType().Name); }
    }

    /// <summary>按一下快门。上一帧还没回来（IsCameraBusy）就放过这一拍，不排队。</summary>
    private async Task CaptureOnceAsync()
    {
        if (!_previewing || Preview.IsCameraBusy) return;
        try { await Preview.CaptureImage(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(6)); }
        catch (Exception ex) { App.WriteLog("FacePane.preview.capture -> " + ex.GetType().Name); }
    }

    private void OnMediaCaptured(object? sender, MediaCapturedEventArgs e)
    {
        var vm = _vm;
        if (vm is null || e.Media is null) return;
        _ = ReadAndAnalyzeAsync(vm, e.Media);
    }

    /// <summary>把这一帧读成字节交给 VM 识别；读不出来或尺寸离谱就丢，不写日志里的任何路径。</summary>
    private static async Task ReadAndAnalyzeAsync(FacePanelViewModel vm, Stream media)
    {
        try
        {
            using var buffer = new MemoryStream();
            await media.CopyToAsync(buffer);
            if (buffer.Length == 0 || buffer.Length > MaxPreviewFrameBytes) return;
            await vm.AnalyzePreviewFrameAsync(buffer.ToArray());
        }
        catch (Exception ex) { App.WriteLog("FacePane.preview.read -> " + ex.GetType().Name); }
    }

    private void OnMediaCaptureFailed(object? sender, MediaCaptureFailedEventArgs e)
    {
        // 只记类型与原因串本身（不含路径），不打断预览：下一次采样还会再来。
        App.WriteLog("FacePane.preview.failed -> " + (e.FailureReason ?? "unknown"));
    }
}
