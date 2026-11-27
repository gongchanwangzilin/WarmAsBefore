using Microsoft.Maui.Media;

namespace WarmAsBefore.Services;

/// <summary>人脸识别组件当前状态（只读快照，供监测页显示）。</summary>
public sealed class FaceComponentStatus
{
    /// <summary>编译期是否启用了本地 ONNX 推理（WAB_ONNX 未定义时为 false）。</summary>
    public bool InferenceCompiled { get; init; }

    /// <summary>情绪模型文件是否在位。</summary>
    public bool ModelInstalled { get; init; }

    /// <summary>可选年龄模型文件是否在位。</summary>
    public bool AgeModelInstalled { get; init; }

    /// <summary>设置里的摄像头人脸识别总开关。</summary>
    public bool CameraSwitchOn { get; init; }

    /// <summary>当前存档是否已授权。</summary>
    public bool Authorized { get; init; }

    /// <summary>系统相机权限状态（本轮查询的结果）。</summary>
    public PermissionStatus CameraPermission { get; init; } = PermissionStatus.Unknown;

    /// <summary>上次识别的时间（本地时间）；从没识别过为 null。</summary>
    public DateTime? LastAt { get; init; }

    /// <summary>上次识别的人话结论。</summary>
    public string LastSummary { get; init; } = "还没识别过";

    /// <summary>上次没识别成 / 这次没识别的原因（人话）；跑通了就是空串。</summary>
    public string DegradationReason { get; init; } = "";

    /// <summary>监测页是否正在拉帧（页面可见且已 BeginMonitor）。</summary>
    public bool Monitoring { get; init; }
}

/// <summary>一次「看一眼」的结果：文字结论 + 本机临时图像路径 + 结构化分析。</summary>
public sealed class FaceLookResult
{
    /// <summary>是否真的拍到了一帧（没拍到的话其余字段只有 <see cref="Conclusion"/>）。</summary>
    public bool Captured { get; init; }

    /// <summary>给 AI / 监测页的人话结论。</summary>
    public string Conclusion { get; init; } = "";

    /// <summary>这一帧在本机临时目录里的路径（拍到时才有；图像不上传）。</summary>
    public string? ImagePath { get; init; }

    /// <summary>结构化分析结果（拍到了但模型没装时，其中 Status 就是降级原因）。</summary>
    public FaceAnalysisResult? Analysis { get; init; }

    /// <summary>取图时间（本地时间）。</summary>
    public DateTime At { get; init; } = DateTime.Now;
}

/// <summary>
/// 人脸取帧与在场观察：抓一帧 → 丢给 <see cref="FaceAnalysisService"/> 识别 → 留一句人话结论。
///
/// <para>
/// <b>只读声明：</b>这里产出的全部是信息（情绪、年龄、这一帧有没有跑出结果），
/// 不参与任何决策——不改数值、不推剧情、不触发动作。上层只许展示或转述。
/// </para>
/// <para>
/// <b>取帧方式：</b>两条路。①监测页里有相机实时预览（CommunityToolkit.Maui.Camera 的 CameraView），
/// 页面可见时每两秒抓一帧交给 <see cref="AnalyzePreviewFrameAsync"/>；②除此之外（AI 主动「看一眼」、
/// 对话开始时的静默观察）仍然只走系统相机 <see cref="MediaPicker.CapturePhotoAsync"/> 的那一帧，
/// 也就是「你在快门上看一眼」。CameraView 只能在屏幕上连好 handler 时取帧，没有可用的
/// 无界面抓帧接口，所以静默那条路不改成预览，也不会偷偷把镜头打开。
/// </para>
/// <para>
/// <b>节流与降级：</b>对话开始时只观察一次，最小间隔 <see cref="MinIntervalSeconds"/> 秒；
/// 采集被占用 / 未授权 / 没有权限一律快速失败并记账；推理永远在线程池（
/// <see cref="FaceAnalysisService.AnalyzeAsync"/> 内部走 Task.Run），失败不抛给调用方，
/// 因此绝不阻塞对话，也绝不在 UI 线程上推理。
/// </para>
/// <para>
/// <b>隐私：</b>帧只存在于内存与<b>一张</b>本机临时文件（<c>face_look.jpg</c>，每次覆盖）里，
/// 不上传；日志只记字节数与状态，不记图像内容，也不记带用户名的路径。
/// </para>
/// </summary>
public sealed class FacePresenceService
{
    /// <summary>对话开始时两次自动观察之间的最小间隔（秒）。</summary>
    public const int MinIntervalSeconds = 30;

    /// <summary>拍一帧最多等这么久：相机被别的应用占着时不能把调用方吊死。</summary>
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(25);

    /// <summary>对话开始时只复用这么新的帧；更旧的一段不回看（她不该拿十分钟前的脸说话）。</summary>
    private static readonly TimeSpan FreshFrameWindow = TimeSpan.FromSeconds(120);

    /// <summary>本机临时文件目录：只在缓存区，随系统清理。</summary>
    private static string TempDir => Path.Combine(FileSystem.CacheDirectory, "face_frames");

    private static string TempFramePath => Path.Combine(TempDir, "face_look.jpg");

    public static FacePresenceService Instance { get; } = new();

    private readonly FaceAnalysisService _face = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();

    // 最近一帧：整体替换引用、绝不原地改写，所以读方不必拷贝就能安全展示。
    private byte[]? _frame;
    private long _frameSerial;
    private DateTime? _frameAt;

    private DateTime? _lastAttempt;
    private DateTime? _lastRun;
    private string _lastSummary = "还没识别过";
    private string _lastReason = "";
    /// <summary>
    /// 正在监测的页面数，不是布尔：人脸面板同时挂在「超级监测」和「模块详情」两页上，
    /// 导航时新页 OnAppearing 完全可能早于旧页 OnDisappearing——用布尔的话旧页一关门
    /// 就把新页的拉帧资格一起关了，新页会停在回落路径且不会自愈。
    /// </summary>
    private int _monitorCount;

    /// <summary>监测页是否正在拉帧。开的时候对话开始那条路径不再重复识别。</summary>
    public bool Monitoring => Volatile.Read(ref _monitorCount) > 0;

    /// <summary>监测页出现：允许拉帧。</summary>
    public void BeginMonitor() => Interlocked.Increment(ref _monitorCount);

    /// <summary>监测页离开：立刻停掉拉帧资格（页面自己也不再取数）。</summary>
    public void EndMonitor()
    {
        // 只在确实有人持有时递减，避免多调一次把计数压到负数、让后面所有 Begin 都失效。
        if (Interlocked.Decrement(ref _monitorCount) < 0) Interlocked.Exchange(ref _monitorCount, 0);
    }

    /// <summary>拉走最近一帧（序列号变了才需要重画）。</summary>
    public (byte[]? Frame, long Serial, DateTime? At) PullFrame()
    {
        lock (_lock) return (_frame, _frameSerial, _frameAt);
    }

    /// <summary>组件状态快照：会顺带查一次系统相机权限（不弹窗、不申请）。</summary>
    public async Task<FaceComponentStatus> StatusAsync(string? saveId)
    {
        var permission = PermissionStatus.Unknown;
        try { permission = await Permissions.CheckStatusAsync<Permissions.Camera>(); }
        catch (Exception ex) { SafeLog("face.status.permission -> " + ex.GetType().Name); }

        string summary, reason;
        DateTime? at;
        lock (_lock)
        {
            summary = _lastSummary;
            reason = _lastReason;
            at = _lastRun;
        }

        return new FaceComponentStatus
        {
            InferenceCompiled = FaceAnalysisService.InferenceCompiled,
            ModelInstalled = FaceAnalysisService.ModelInstalled,
            AgeModelInstalled = FaceAnalysisService.AgeModelInstalled,
            CameraSwitchOn = _face.CameraEnabled,
            Authorized = _face.IsAuthorized(saveId),
            CameraPermission = permission,
            LastAt = at,
            LastSummary = summary,
            DegradationReason = reason,
            Monitoring = Monitoring,
        };
    }

    /// <summary>
    /// 现在到底为什么判断不了（人话，一次说清链条上最早卡住的那一环）；全通时返回空串。
    /// 监测页直接展示这一句，不自己编原因。
    /// </summary>
    public static string DisabledReason(FaceComponentStatus status)
    {
        if (!status.CameraSwitchOn) return "摄像头人脸识别总开关关着：设置 → 情感与身体 里打开它。";
        if (!status.Authorized) return "这个存档还没授权人脸识别：进游戏时会问一次，也可以在设置里重新授权。";
        if (status.CameraPermission != PermissionStatus.Granted) return "系统没给相机权限：到 系统设置 → 应用 → 温暖如初 → 权限 里允许「相机」。";
        if (!status.InferenceCompiled) return "本地 ONNX 推理没有编译进来（WAB_ONNX 未定义）：能拿到画面，但判断不了情绪与年龄。";
        if (!status.ModelInstalled) return "模型没装（缺 Models/face_emotion.onnx）：能拿到画面，但判断不了情绪与年龄。";
        return "";
    }

    /// <summary>
    /// AI 主动「看一眼现在」：拍一帧、识别、返回结论与本机临时图像路径。
    /// 任一环节不满足都快速失败并给出人话原因，绝不抛异常、绝不阻塞。
    /// </summary>
    public async Task<FaceLookResult> LookNowAsync(string? saveId, CancellationToken ct = default)
    {
        var now = DateTime.Now;
        lock (_lock) _lastAttempt = now;

        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
            return Failed("上一次取图还没结束，等它停下来再试。");

        try
        {
            if (!_face.CameraEnabled)
                return Failed("摄像头人脸识别总开关关着：请让用户到 设置 → 情感与身体 打开它。");
            if (!string.IsNullOrWhiteSpace(saveId) && !_face.IsAuthorized(saveId))
                return Failed("这个存档没授权人脸识别：只有用户自己点「允许」之后才能看。");

            PermissionStatus permission;
            try { permission = await Permissions.CheckStatusAsync<Permissions.Camera>().ConfigureAwait(false); }
            catch (Exception ex)
            {
                SafeLog("face.look.permission -> " + ex.GetType().Name);
                return Failed("查不到相机权限状态，这次先不看了。");
            }
            if (permission != PermissionStatus.Granted)
                return Failed("没有相机权限：请让用户到 系统设置 → 应用 → 温暖如初 → 权限 里允许「相机」。");

            // 监测页的实时预览正开着：这时候再拉系统相机只会互相抢（大概率两边都失败）。
            // 用预览刚抓的那一帧就够——它本来就是同一台相机、同一时刻、同一份只读结论。
            if (Monitoring)
            {
                byte[]? cached;
                DateTime? cachedAt;
                lock (_lock) { cached = _frame; cachedAt = _frameAt; }
                if (cached is not null && cachedAt is DateTime shotAt && now - shotAt <= FreshFrameWindow)
                {
                    var reused = await _face.AnalyzeAsync(cached, saveId).ConfigureAwait(false);
                    RecordFrame(cached, shotAt, reused.Success ? "" : reused.Status, FaceAnalysisService.Describe(reused));
                    return new FaceLookResult
                    {
                        Captured = true,
                        Conclusion = "（用的是监测页实时预览刚才那一帧，没有另外开相机）" + FaceAnalysisService.Describe(reused),
                        ImagePath = null,
                        Analysis = reused,
                        At = shotAt,
                    };
                }
            }

            FileResult? shot;
            try
            {
                // 系统相机要在 UI 线程上拉起来；外面压一个超时，相机被占用时不至于让这一轮对话吊着。
                shot = await MainThread
                    .InvokeOnMainThreadAsync(() => MediaPicker.Default.CapturePhotoAsync(new MediaPickerOptions { Title = "看一眼现在" }))
                    .WaitAsync(CaptureTimeout, ct)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                RecordNote("相机太久没回话（可能在等快门，或者相机被别的应用占着）");
                return Failed("相机太久没回话：可能在等快门，或者被别的应用占着。");
            }
            catch (Exception ex)
            {
                SafeLog("face.look.capture -> " + ex.GetType().Name);
                RecordNote("相机打不开（" + ex.GetType().Name + "）");
                return Failed("相机打不开，这次先不看了。");
            }

            if (shot is null || string.IsNullOrWhiteSpace(shot.FullPath) || !File.Exists(shot.FullPath))
            {
                RecordNote("没取到画面（拍摄被取消，或者相机正被别的应用占用）");
                return Failed("没取到画面：拍摄取消了，或者相机正被别的应用占着。");
            }

            byte[] bytes;
            try { bytes = await File.ReadAllBytesAsync(shot.FullPath, ct).ConfigureAwait(false); }
            catch (Exception ex)
            {
                SafeLog("face.look.read -> " + ex.GetType().Name);
                RecordNote("这一帧读不出来");
                return Failed("这一帧读不出来，这次先不看了。");
            }
            if (bytes.Length == 0)
            {
                RecordNote("这一帧是空的");
                return Failed("这一帧是空的，什么都没读到。");
            }

            // 推理在 AnalyzeAsync 内部丢线程池，不占 UI 线程。
            var analysis = await _face.AnalyzeAsync(bytes, saveId).ConfigureAwait(false);
            var at = DateTime.Now;
            var conclusion = analysis.Success
                ? $"画面拿到了：{FaceAnalysisService.Describe(analysis)}。这一帧只是此刻的参考，之后的样子不一定一样。"
                : $"画面拿到了，但这次判断不了：{FaceAnalysisService.Describe(analysis)}。图在，情绪和年龄读不出来。";

            RecordFrame(bytes, at, analysis.Success ? "" : analysis.Status, FaceAnalysisService.Describe(analysis));
            var path = SaveTempFrame(bytes);
            SafeLog($"face.look ok bytes={bytes.Length} status={(analysis.Success ? "ok" : "degraded")}");
            return new FaceLookResult
            {
                Captured = true,
                Conclusion = conclusion,
                ImagePath = path,
                Analysis = analysis,
                At = at,
            };
        }
        catch (Exception ex)
        {
            SafeLog("face.look -> " + ex.GetType().Name);
            return Failed("取图时出了点岔子，这次先不看了。");
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// 监测页实时预览抓到的一帧：识别，并把结果写成「最近一帧 / 上次识别」。
    /// 与 <see cref="LookNowAsync"/> 共用同一道单飞闸，但抢不到闸就直接跳过这一帧（两秒后还有下一帧），
    /// 不排队、不堆积；任何失败只记账，不抛给界面。
    /// </summary>
    public async Task<FaceAnalysisResult?> AnalyzePreviewFrameAsync(byte[]? bytes, string? saveId, CancellationToken ct = default)
    {
        if (bytes is null || bytes.Length == 0) return null;
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            RecordNote("实时预览这一帧跳过了：上一次识别还没结束");
            return null;
        }

        try
        {
            var analysis = await _face.AnalyzeAsync(bytes, saveId).ConfigureAwait(false);
            RecordFrame(bytes, DateTime.Now, analysis.Success ? "" : analysis.Status, FaceAnalysisService.Describe(analysis));
            SafeLog($"face.preview ok bytes={bytes.Length} status={(analysis.Success ? "ok" : "degraded")}");
            return analysis;
        }
        catch (Exception ex)
        {
            SafeLog("face.preview -> " + ex.GetType().Name);
            return null;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// 对话开始时的静默观察：立即返回，识别丢后台；节流到 <see cref="MinIntervalSeconds"/> 秒一次；
    /// 任何失败只记账，绝不影响这一轮对话。监测页开着时跳过（那边自己拉帧）。
    /// 相机只在你按下快门时取帧，所以这里只复用足够新的那一帧，不弹相机。
    /// </summary>
    public void ObserveOnConversationStart(string? saveId)
    {
        var held = false;
        try
        {
            if (Monitoring) return;
            if (!_gate.Wait(0)) return;
            held = true;

            lock (_lock)
            {
                if (_lastAttempt is DateTime last && DateTime.Now - last < TimeSpan.FromSeconds(MinIntervalSeconds)) return;
                _lastAttempt = DateTime.Now;
            }

            held = false;   // 交棒给后台任务，由它在 finally 里释放
            try
            {
                _ = Task.Run(async () =>
                {
                    try { await ObserveCoreAsync(saveId).ConfigureAwait(false); }
                    catch (Exception ex) { SafeLog("face.observe -> " + ex.GetType().Name); }
                    finally { _gate.Release(); }
                });
            }
            catch (Exception ex)
            {
                // 关停瞬间线程池拒收：闸这时候只剩自己拿着，必须当场放掉，否则本次运行内人脸观察永久失效。
                _gate.Release();
                SafeLog("face.observe.schedule -> " + ex.GetType().Name);
            }
        }
        catch (Exception ex) { SafeLog("face.observe.begin -> " + ex.GetType().Name); }
        finally { if (held) _gate.Release(); }
    }

    private async Task ObserveCoreAsync(string? saveId)
    {
        if (!_face.CameraEnabled) { RecordNote("对话开始时的观察跳过了：摄像头总开关关着"); return; }
        if (!string.IsNullOrWhiteSpace(saveId) && !_face.IsAuthorized(saveId))
        {
            RecordNote("对话开始时的观察跳过了：这个存档没授权人脸识别");
            return;
        }

        byte[]? frame;
        DateTime? at;
        lock (_lock) { frame = _frame; at = _frameAt; }

        if (frame is null || at is null || DateTime.Now - at.Value > FreshFrameWindow)
        {
            RecordNote("对话开始时没有足够新的画面：相机只在你按下快门时取帧，这一次就不判断了");
            return;
        }

        var analysis = await _face.AnalyzeAsync(frame, saveId).ConfigureAwait(false);
        RecordFrame(frame, at.Value, analysis.Success ? "" : analysis.Status, FaceAnalysisService.Describe(analysis));
    }

    private void RecordFrame(byte[] bytes, DateTime at, string reason, string summary)
    {
        lock (_lock)
        {
            _frame = bytes;
            _frameSerial++;
            _frameAt = at;
            _lastRun = at;
            _lastSummary = summary;
            _lastReason = reason;
        }
    }

    /// <summary>记下「这一次为什么没识别 / 没判断」，给监测页的降级原因用。</summary>
    private void RecordNote(string reason)
    {
        lock (_lock) _lastReason = reason;
    }

    /// <summary>把这一帧写成唯一一张本机临时图（同名覆盖，不累积），供 AI 或用户按路径查看。</summary>
    private static string? SaveTempFrame(byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(TempDir);
            File.WriteAllBytes(TempFramePath, bytes);
            return TempFramePath;
        }
        catch (Exception ex)
        {
            SafeLog("face.temp -> " + ex.GetType().Name);
            return null;
        }
    }

    private static FaceLookResult Failed(string conclusion) => new() { Captured = false, Conclusion = conclusion };

    /// <summary>日志不带路径也不带图像内容：只记类型与状态。</summary>
    private static void SafeLog(string message)
    {
        try { App.WriteLog(message); } catch { /* 忽略：本服务保证不崩 */ }
    }
}
