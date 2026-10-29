using WarmAsBefore.Models;
using WarmAsBefore.Views;

namespace WarmAsBefore.Services;

/// <summary>
/// 强制标定的统一入口。
///
/// 需求前提：**每张地图背景导入时都必须完成标定，不能跳过**。所以这里提供两种进入方式：
///  - 导入路径用 <see cref="EnsureCalibratedAsync"/>：await 到底，未标定完就不放行（导入方回滚背景）；
///  - 运行路径用 <see cref="CalibrationRequired"/> 事件：切到未标定场景时通知界面弹不可关的遮罩。
///
/// 用信号量做单飞，避免批量导入时多个场景同时弹标定页。
/// </summary>
public sealed class SceneCalibrationService
{
    private readonly MapService _maps;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TaskCompletionSource<bool>? _tcs;

    public SceneCalibrationService(MapService maps) => _maps = maps;

    /// <summary>
    /// 游戏主界面承载背景的容器宽高比（宽/高）的近似值，用于在标定页画出"游戏里实际可见框"。
    /// 手机是竖屏窄条、桌面是横屏宽条，两者裁切掉的区域完全不同，所以必须区分。
    /// </summary>
    public static double DefaultTargetAspect
    {
        get
        {
            try
            {
                return DeviceInfo.Idiom == DeviceIdiom.Phone || DeviceInfo.Idiom == DeviceIdiom.Tablet
                    ? 9.0 / 19.5
                    : 16.0 / 9.0;
            }
            catch
            {
                return 16.0 / 9.0;
            }
        }
    }

    /// <summary>当前正在标定的场景（由标定页读取）。</summary>
    public MapScene? PendingScene { get; private set; }

    /// <summary>目标容器宽高比（宽/高），用于在标定页画出"游戏里实际可见框"。null = 不画。</summary>
    public double? TargetAspect { get; private set; }

    /// <summary>切到未标定场景、需要用户去标定时触发（界面据此弹遮罩）。</summary>
    public event Action<MapScene>? CalibrationRequired;

    public void RaiseRequired(MapScene scene) => CalibrationRequired?.Invoke(scene);

    /// <summary>
    /// 场景是否需要标定：**确实有图可标**，但标定缺失或不完整。
    ///
    /// 注意这里要求背景能被解析成真实文件：JSON 导入的地图里 Background 可能指向本机不存在的路径，
    /// 那种情况下没有图可标，强制标定只会把用户卡在一个做不完的流程里——
    /// 此时退回居中渲染（界面会提示背景缺失），而不是拦人。
    /// </summary>
    public bool NeedsCalibration(MapScene? scene)
    {
        if (scene is null) return false;
        if (string.IsNullOrWhiteSpace(scene.Background)) return false;   // 纯色场景不需要标定
        if (_maps.ResolveBackground(scene) is null) return false;        // 图不存在，无可标之物
        return !PlacementMath.Validate(scene.Placement).Ok;
    }

    /// <summary>还缺什么（用于遮罩上的提示文案与导入失败原因）。</summary>
    public string DescribeMissing(MapScene? scene)
    {
        if (scene is null) return "";
        var (_, missing) = PlacementMath.Validate(scene.Placement);
        return missing.Count == 0 ? "" : string.Join("\n", missing);
    }

    /// <summary>
    /// 确保场景已标定：未标定则导航到标定页并等待其结果。
    /// 返回 true = 标定完成（或本来就不需要）；false = 用户放弃。
    /// <paramref name="force"/> = true 时无视"已标定"直接进标定页（用户主动重标）。
    /// </summary>
    public async Task<bool> EnsureCalibratedAsync(MapScene scene, double? targetAspect = null, bool force = false)
    {
        if (!force && !NeedsCalibration(scene)) return true;

        await _gate.WaitAsync();
        try
        {
            // 排队期间可能已被前一次标定覆盖（批量导入时的常见情形）
            if (!force && !NeedsCalibration(scene)) return true;

            PendingScene = scene;
            TargetAspect = targetAspect;
            _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (Shell.Current is not null)
                    await Shell.Current.GoToAsync(nameof(SceneCalibrationPage));
            });

            return await _tcs.Task;
        }
        catch (Exception ex)
        {
            App.WriteLog("SceneCalibrationService.EnsureCalibrated -> " + ex);
            return false;
        }
        finally
        {
            PendingScene = null;
            TargetAspect = null;
            _tcs = null;
            _gate.Release();
        }
    }

    /// <summary>标定页收尾时回调：true = 标定完成，false = 放弃。</summary>
    internal void Finish(bool ok) => _tcs?.TrySetResult(ok);

    // ============ 待标定队列（批量导入用）============

    private readonly List<MapScene> _queue = new();

    /// <summary>待标定场景数。</summary>
    public int PendingCount => _queue.Count;

    /// <summary>
    /// 把一批场景加入待标定队列。
    /// 素材包一次可能带进来十几张地图，逐个被动撞上标定页体验很差，
    /// 所以由导入方先入队、再在合适的时机统一驱动（见 <see cref="DrainAsync"/>）。
    /// </summary>
    public void Enqueue(IEnumerable<MapScene> scenes)
    {
        foreach (var s in scenes)
            if (NeedsCalibration(s) && !_queue.Contains(s))
                _queue.Add(s);
    }

    /// <summary>逐张驱动队列里的标定。返回 (已标定数, 用户放弃数)。</summary>
    public async Task<(int Done, int Skipped)> DrainAsync(double? targetAspect = null)
    {
        var done = 0;
        var skipped = 0;
        while (_queue.Count > 0)
        {
            var scene = _queue[0];
            _queue.RemoveAt(0);
            if (!NeedsCalibration(scene)) { done++; continue; }
            if (await EnsureCalibratedAsync(scene, targetAspect)) done++;
            else skipped++;
        }
        return (done, skipped);
    }

    public void ClearQueue() => _queue.Clear();
}
