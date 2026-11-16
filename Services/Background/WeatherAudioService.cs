using WarmAsBefore.Helpers;
using WarmAsBefore.Models.Background;
using WarmAsBefore.Services;

namespace WarmAsBefore.Services.Background;

/// <summary>
/// 天气场景与音效的联动服务：把「正在下雨」这类场景翻译成持续的低频雨声，
/// 并在闪电发生时补一记雷声。
/// </summary>
/// <remarks>
/// <para>
/// 项目本身不内置音频文件。运行时音效依赖用户把音频导入到 <c>{root}/assets/audio</c>，
/// 再由声音分配挂到 AudioController 的卡槽（"rain" / "lightning"）上。
/// </para>
/// <para>
/// AudioController.PlaySfxFile 是「一次性播放、播完自动 Dispose」，并不会循环。
/// 所以这里不做逐帧动画，而是用一个低频 <see cref="IDispatcherTimer"/> 定时重触发：
/// 每隔若干秒、当场景仍是雨且未被停止时，再经 <c>PlayAssigned("rain")</c> 补播一次，
/// 用离散重触发模拟出连续雨声。间隔最密也有 2600ms，绝不退化成逐帧刷新。
/// </para>
/// <para>
/// 关于 <c>level</c>（场景音量）：AudioController 没有暴露单场景音量设置方法
/// （只有全局 Sfx 属性），因此该参数在此仅作保留，实际音量跟随全局 SfxLevel。
/// </para>
/// </remarks>
public sealed class WeatherAudioService
{
    private readonly AudioController _audio;
    private WeatherSceneKind _scene;
    private bool _enabled;
    private double _level;
    private IDispatcherTimer? _rainTimer;

    public WeatherAudioService(AudioController audio) => _audio = audio;

    /// <summary>雨环境音是否在播（timer 存活即视为在播）。</summary>
    public bool IsRunning => _rainTimer is not null;

    /// <summary>
    /// 应用场景 / 开关 / 音量变化。可从 VM 的线程池路径调用，
    /// 所有 timer 与音频操作都经 <see cref="Ui.Post"/> 切回 UI 线程执行。
    /// </summary>
    public void Apply(WeatherSceneKind scene, bool enabled, double level)
    {
        Ui.Post(() =>
        {
            try
            {
                _scene = scene;
                _enabled = enabled;
                _level = level;

                // 关闭 / 音量过低 / 非雨类场景：一律停止，不产生任何环境音。
                if (!enabled || level <= 0.01 || !IsRainScene(scene))
                {
                    StopCore();
                    return;
                }

                var desiredInterval = TimeSpan.FromMilliseconds(IntervalMs(scene));

                if (_rainTimer is null)
                {
                    // 首次进入雨场景：立即补一声，之后交给 timer 低频重触发。
                    SafePlayRain();

                    var timer = Application.Current?.Dispatcher.CreateTimer();
                    if (timer is null) return;
                    timer.Interval = desiredInterval;
                    timer.Tick += OnRainTick;
                    _rainTimer = timer;
                    timer.Start();
                }
                else if (_rainTimer.Interval != desiredInterval)
                {
                    // 场景强度变化：复用同一个 timer，只重设间隔，不重复立即播放。
                    _rainTimer.Interval = desiredInterval;
                }
            }
            catch
            {
                // 计时器 / 音频初始化失败一律吞掉，绝不让缺失的音效卡住 UI。
            }
        });
    }

    /// <summary>闪电时补播雷声：仅在音效启用状态下播放。</summary>
    public void PlayThunder()
    {
        Ui.Post(() =>
        {
            try
            {
                if (!_enabled) return;
                _audio.PlayAssigned("lightning");
            }
            catch
            {
                // 未分配雷声音效时静默跳过。
            }
        });
    }

    /// <summary>停止全部环境音。幂等：重复调用无副作用。</summary>
    public void Stop()
    {
        Ui.Post(() =>
        {
            try { StopCore(); }
            catch { /* 静默 */ }
        });
    }

    /// <summary>timer 回调：仅在 UI 线程触发，再确认一次状态后补播雨声。</summary>
    private void OnRainTick(object? sender, EventArgs e)
    {
        try
        {
            if (!_enabled || _level <= 0.01 || !IsRainScene(_scene))
            {
                StopCore();
                return;
            }
            SafePlayRain();
        }
        catch
        {
            // 单次重触发失败不影响后续 tick。
        }
    }

    /// <summary>真正执行停止的逻辑：停表、退订、置空，并清掉启用标记。</summary>
    private void StopCore()
    {
        _enabled = false;
        var timer = _rainTimer;
        if (timer is null) return;
        _rainTimer = null;
        try
        {
            timer.Stop();
            timer.Tick -= OnRainTick;
        }
        catch { /* 停止失败也要保证字段已清空，维持 IsRunning 正确 */ }
    }

    /// <summary>包一层 try/catch 播放雨声：PlayAssigned 是 fire-and-forget，异常不外泄。</summary>
    private void SafePlayRain()
    {
        try { _audio.PlayAssigned("rain"); }
        catch { /* 未分配 rain 卡槽时静默跳过 */ }
    }

    /// <summary>是否为会产生持续雨声的场景。</summary>
    private static bool IsRainScene(WeatherSceneKind scene) =>
        scene is WeatherSceneKind.Drizzle
            or WeatherSceneKind.LightRain
            or WeatherSceneKind.HeavyRain
            or WeatherSceneKind.Storm;

    /// <summary>按雨势决定重触发间隔（毫秒），间隔越大听感越稀、CPU 占用越低。</summary>
    private static int IntervalMs(WeatherSceneKind scene) => scene switch
    {
        WeatherSceneKind.Drizzle => 6000,    // 毛毛雨：每 6 秒
        WeatherSceneKind.LightRain => 4000,  // 小雨：每 4 秒
        WeatherSceneKind.HeavyRain => 2600,  // 大雨：每 2.6 秒
        WeatherSceneKind.Storm => 2600,      // 雷暴：每 2.6 秒
        _ => 0
    };
}
