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
///
/// 起停的三条硬规则（曾经踩过的坑，别改回去）：
///   · 停预览**无条件**调用平台接口，不以「我记录的状态是开着」为门——启动还挂在 half-way 时
///     页面就会走停，那时 _previewing 还是 false，用状态当门就等于把相机留在没人管的地方；
///   · 启动拿一个自己的 CancellationTokenSource，停的时候连它一起取消，不让后台任务继续跑；
///   · await 返回后先确认「VM 还要预览 + 控件还挂着」，不成立就当场停掉，绝不把 _previewing 置真。
/// </summary>
public partial class FaceMonitorPane : ContentView
{
    /// <summary>一帧的最大字节数：超过就当这一帧不正常，丢掉而不是往内存里塞。</summary>
    private const int MaxPreviewFrameBytes = 8 * 1024 * 1024;

    private FacePanelViewModel? _vm;
    private bool _previewing;
    private bool _starting;
    private CancellationTokenSource? _previewCts;
    private Window? _window;

    public FaceMonitorPane()
    {
        InitializeComponent();
        Preview.MediaCaptured += OnMediaCaptured;
        Preview.MediaCaptureFailed += OnMediaCaptureFailed;
        Loaded += (_, _) =>
        {
            HookWindow();
            if (_vm?.LivePreview == true) _ = StartPreviewAsync();
        };
        Unloaded += (_, _) =>
        {
            UnhookWindow();
            StopPreview();
        };
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

    /// <summary>
    /// 窗口失焦/最小化时也要停预览：MAUI 的页面生命周期不会因为最小化触发，
    /// 不挂这一层的话相机指示灯会在最小化之后一直亮着（仓库里 HoverFeel 已有同样的先例）。
    /// </summary>
    private void HookWindow()
    {
        try
        {
            var w = Application.Current?.Windows.Count > 0 ? Application.Current.Windows[0] : null;
            if (w is null || ReferenceEquals(w, _window)) return;
            _window = w;
            w.Deactivated += OnWindowDeactivated;
            w.Stopped += OnWindowDeactivated;
        }
        catch (Exception ex) { App.WriteLog("FacePane.window.hook -> " + ex.GetType().Name); }
    }

    private void UnhookWindow()
    {
        if (_window is null) return;
        try
        {
            _window.Deactivated -= OnWindowDeactivated;
            _window.Stopped -= OnWindowDeactivated;
        }
        catch { /* 故意忽略：窗口已经没了，退订失败不影响任何行为 */ }
        _window = null;
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) => StopPreview();

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
        var cts = new CancellationTokenSource();
        _previewCts = cts;
        try
        {
            // 让 IsVisible 的那次变化先落到平台层：CameraView 复用了刚显示出来的预览表面。
            await Task.Yield();
            // 相机被占用 / 桌面应用没被系统放行时，这一步会抛或者一直不回话，所以压一个上限。
            await Preview.StartCameraPreview(cts.Token).WaitAsync(TimeSpan.FromSeconds(10), cts.Token);

            // 回来后先看还要不要：这十秒里可能已经离页、或者用户把开关关了。
            if (_vm?.LivePreview != true || !IsLoaded || cts.IsCancellationRequested)
            {
                SafeStop();
                return;
            }
            _previewing = true;
            _vm.ReportPreviewRunning(true);
        }
        catch (Exception ex)
        {
            // 超时/失败时底层可能其实已经起来了（只是回话慢）：这里必须再停一次，否则相机就没人关了。
            SafeStop();
            App.WriteLog("FacePane.preview.start -> " + ex.GetType().Name);
            _vm?.ReportPreviewUnavailable(ex is OperationCanceledException
                ? "预览被关掉了"
                : ex is TimeoutException
                    ? "相机十秒没有回话（可能被别的应用占着）"
                    : "相机没打开（" + ex.GetType().Name + "）");
        }
        finally
        {
            _starting = false;
            if (_previewCts == cts) _previewCts = null;
            cts.Dispose();
        }
    }

    /// <summary>
    /// 停预览：页面离开、面板卸载、或 VM 关门时都会走到这里，重复调用安全。
    /// 不看 _previewing——启动还在路上时也要能把那颗取消令牌掐掉、并把平台侧叫停。
    /// </summary>
    private void StopPreview()
    {
        var was = _previewing;
        _previewing = false;
        try { _previewCts?.Cancel(); } catch { /* 故意忽略：取消令牌本身失败不影响下面的停 */ }
        SafeStop();
        if (was) _vm?.ReportPreviewRunning(false);
    }

    private void SafeStop()
    {
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
        var media = e.Media;
        if (vm is null || media is null) return;
        _ = ReadAndAnalyzeAsync(vm, media);
    }

    /// <summary>
    /// 把这一帧读成字节交给 VM 识别；读不出来或尺寸离谱就丢，不写日志里的任何路径。
    /// 流的释放和大小判定都在这里：先按 Length 粗筛，再读，读完一定 dispose。
    /// </summary>
    private static async Task ReadAndAnalyzeAsync(FacePanelViewModel vm, Stream media)
    {
        try
        {
            using (media)
            {
                if (media.CanSeek && media.Length > MaxPreviewFrameBytes) return;
                using var buffer = new MemoryStream();
                await media.CopyToAsync(buffer);
                if (buffer.Length == 0 || buffer.Length > MaxPreviewFrameBytes) return;
                await vm.AnalyzePreviewFrameAsync(buffer.ToArray());
            }
        }
        catch (Exception ex) { App.WriteLog("FacePane.preview.read -> " + ex.GetType().Name); }
    }

    private void OnMediaCaptureFailed(object? sender, MediaCaptureFailedEventArgs e)
    {
        // 只记类型与原因串本身（不含路径），不打断预览：下一次采样还会再来。
        App.WriteLog("FacePane.preview.failed -> " + (e.FailureReason ?? "unknown"));
    }
}
