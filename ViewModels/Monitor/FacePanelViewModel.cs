using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 「人脸识别」这一块的两段内容：组件状态（模型 / 相机 / 上次结果 / 降级原因）和图像流（最近一帧）。
///
/// 超级监测页与模块详情页共用这一份 VM（控件 FaceMonitorPane 的 BindingContext），
/// 所以两处的措辞与状态判断只有一个出处。这里只读：不写任何设置，也不改存档。
///
/// 图像流是「拉」出来的：页面可见时由 <see cref="BeginMonitor"/> 开门、离开时 <see cref="EndMonitor"/>
/// 关门，期间 <see cref="PullLatestFrame"/> 只读内存里的最近一帧；页面不在时没有谁在拉，
/// 相机也从来不会被这里打开（取帧只有用户按快门或 AI 主动请一次）。
/// </summary>
public sealed partial class FacePanelViewModel : ObservableObject
{
    /// <summary>固定在面板顶上的一句：只读 + 只在本机。两页显示同一句，不各写各的。</summary>
    public string ReadOnlyNote { get; } =
        "只产出情绪 / 年龄 / 是否在场这类信息：不改数值、不推剧情、不触发动作。"
        + "图像只在本机内存与一张本机临时图里，不上传，也不写进日志。";

    /// <summary>组件状态的行（模型 / 相机 / 授权 / 上次识别）。</summary>
    [ObservableProperty] private ObservableCollection<FaceRow> _statusRows = new();

    /// <summary>本次为什么判断不了（人话）；全通时是「没有降级」。</summary>
    [ObservableProperty] private string _degradationText = "正在读取…";

    /// <summary>最近一帧；没有帧时为 null，界面切到空态。</summary>
    [ObservableProperty] private ImageSource? _frameSource;

    [ObservableProperty] private bool _hasFrame;

    /// <summary>帧的说明：时间 · 大小 · 仅本机。</summary>
    [ObservableProperty] private string _frameNote = "";

    [ObservableProperty] private string _emptyTitle = "还没有画面";
    [ObservableProperty] private string _emptyReason = "正在读取…";
    [ObservableProperty] private string _emptyHint = "";

    /// <summary>最近一次动作的结果（抓帧成功 / 失败原因）。</summary>
    [ObservableProperty] private string _actionText = "";

    [ObservableProperty] private bool _busy;

    /// <summary>上次画上去的帧序号：序号没变就不重建 ImageSource，免得每次都换一个。</summary>
    private long _shownSerial = -1;

    /// <summary>当前角色 id：人脸识别的授权是按角色记账的（同一角色换档不再重问），空串就是还没进局。</summary>
    private static string SaveId()
    {
        try
        {
            var engine = Resolve<GameEngine>();
            return engine?.State?.CharacterId ?? "";
        }
        catch (Exception ex) { App.WriteLog("FacePanel.saveId -> " + ex.Message); return ""; }
    }

    /// <summary>页面可见：开门，允许拉帧，并立刻拉一次。</summary>
    public void BeginMonitor()
    {
        FacePresenceService.Instance.BeginMonitor();
        PullLatestFrame();
    }

    /// <summary>页面离开：关门，此后没有任何人在拉帧。</summary>
    public void EndMonitor() => FacePresenceService.Instance.EndMonitor();

    /// <summary>读一次组件状态（不弹相机、不申请权限），并把降级原因写成人话。</summary>
    public async Task LoadAsync()
    {
        try
        {
            var status = await FacePresenceService.Instance.StatusAsync(SaveId());
            StatusRows = new ObservableCollection<FaceRow>(new[]
            {
                new FaceRow("情绪模型", status.ModelInstalled ? "已安装（Models/face_emotion.onnx）" : "未安装"),
                new FaceRow("本地推理", status.InferenceCompiled
                    ? "已编译（WAB_ONNX）"
                    : "未编译（WAB_ONNX 未定义）——当前走降级路径"),
                new FaceRow("年龄模型", status.AgeModelInstalled ? "已安装（可选）" : "未安装（可选，缺了只少年龄）"),
                new FaceRow("摄像头开关", status.CameraSwitchOn ? "开" : "关"),
                new FaceRow("本存档授权", status.Authorized ? "已授权" : "未授权"),
                new FaceRow("相机权限", PermissionText(status.CameraPermission)),
                new FaceRow("上次识别", status.LastAt is DateTime at
                    ? $"{at.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} · {status.LastSummary}"
                    : status.LastSummary),
            });

            var blocked = FacePresenceService.DisabledReason(status);
            DegradationText = blocked.Length > 0
                ? blocked
                : status.DegradationReason.Length > 0 ? status.DegradationReason : "没有降级：这次识别跑通了。";

            RefreshEmptyState(status, blocked);
        }
        catch (Exception ex)
        {
            App.WriteLog("FacePanel.Load -> " + ex.Message);
            DegradationText = "状态没读上来：" + ex.Message;
        }
    }

    /// <summary>
    /// 拉最近一帧。只在监测页可见期间调用（页面离开后没人再调）：
    /// 序号没变就什么都不做，变了才重建 ImageSource。
    /// </summary>
    public void PullLatestFrame()
    {
        try
        {
            var (frame, serial, at) = FacePresenceService.Instance.PullFrame();
            if (frame is null || frame.Length == 0 || at is null)
            {
                if (HasFrame)
                {
                    HasFrame = false;
                    FrameSource = null;
                }
                _shownSerial = -1;
                return;
            }
            if (serial == _shownSerial) return;

            var bytes = frame;
            FrameSource = ImageSource.FromStream(() => new MemoryStream(bytes));
            FrameNote = $"{at.Value.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} · "
                + $"{Math.Max(1, bytes.Length / 1024)} KB · 仅本机";
            HasFrame = true;
            _shownSerial = serial;
        }
        catch (Exception ex) { App.WriteLog("FacePanel.Pull -> " + ex.Message); }
    }

    /// <summary>用户手动抓一帧：会拉起系统相机（要按快门），随后识别并显示这一帧。</summary>
    [RelayCommand]
    private async Task CaptureAsync()
    {
        if (Busy) return;
        Busy = true;
        ActionText = "正在取一帧…";
        try
        {
            var result = await FacePresenceService.Instance.LookNowAsync(SaveId());
            ActionText = result.Captured ? "取到了这一帧。" : result.Conclusion;
            PullLatestFrame();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            App.WriteLog("FacePanel.Capture -> " + ex.Message);
            ActionText = "取图时出了点岔子：" + ex.Message;
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    /// <summary>没有帧时，空态要说清「为什么空」和「怎么才不空」，并且优先说链条上最早卡住的那一环。</summary>
    private void RefreshEmptyState(FaceComponentStatus status, string blocked)
    {
        EmptyTitle = "还没有画面";
        if (blocked.Length > 0)
        {
            EmptyReason = blocked;
            // 模型没装只影响「判断得了什么」，不影响能不能拿到画面：这两句不能说反。
            EmptyHint = status.InferenceCompiled && status.ModelInstalled
                ? "开好开关与授权之后，点「抓一帧」或让 AI 请求「看一眼」就会有画面。"
                : "画面本身不受模型影响：点「抓一帧」就能看到最近一帧，只是情绪与年龄判断不了。";
            return;
        }

        EmptyReason = "这一局还没取过帧：相机只在你按下快门时取帧，不会自己打开。";
        EmptyHint = "点「抓一帧」，或者让 AI 请求「看一眼现在」。";
    }

    private static string PermissionText(Microsoft.Maui.ApplicationModel.PermissionStatus status) => status switch
    {
        Microsoft.Maui.ApplicationModel.PermissionStatus.Granted => "已允许",
        Microsoft.Maui.ApplicationModel.PermissionStatus.Denied => "被拒绝",
        Microsoft.Maui.ApplicationModel.PermissionStatus.Disabled => "系统已关闭",
        Microsoft.Maui.ApplicationModel.PermissionStatus.Restricted => "被系统限制",
        Microsoft.Maui.ApplicationModel.PermissionStatus.Limited => "受限（仅部分可用）",
        _ => "没查过",
    };

    /// <summary>从 MAUI 服务容器取单例服务；取不到就让这一路空着。</summary>
    private static T? Resolve<T>() where T : class
    {
        try
        {
            var services = Microsoft.Maui.IPlatformApplication.Current?.Services
                ?? Application.Current?.Handler?.MauiContext?.Services;
            return services?.GetService(typeof(T)) as T;
        }
        catch (Exception ex)
        {
            App.WriteLog("FacePanel.Resolve -> " + ex.Message);
            return null;
        }
    }

    /// <summary>组件状态的一行：标签 + 值。</summary>
    public sealed record FaceRow(string Label, string Value);
}
