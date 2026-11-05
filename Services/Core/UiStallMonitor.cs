using Microsoft.Maui.Dispatching;

namespace WarmAsBefore.Services;

/// <summary>
/// UI 线程卡顿看门狗：DispatcherTimer 每 100ms 心跳一次，实际间隔比预期多出的部分就是
/// 「UI 线程被占住、这一帧没出」的时间。超过阈值写一条 perf.log，并带上最后一步阶段标记。
///
/// 为什么需要它：PerfLog 只在手写打点处计时，用户感觉「莫名卡一下」时往往没有对应打点；
/// 心跳是唯一不依赖具体业务代码就能抓到「主线程停摆」的手段。日志本身走 PerfLog 的后台队列，
/// 不在 UI 线程落盘。限制：窗口最小化/被系统挂起时 Timer 也会停，间隔会虚高——超过 5 秒的一律
/// 视为挂起，不记成卡顿（否则每次切出去回来都多一条假 SLOW）。
/// </summary>
public static class UiStallMonitor
{
    /// <summary>低于这个间隔的抖动不记（60fps 一帧 16ms，200ms 已经是 12 帧没出）。</summary>
    private const long ThresholdMs = 200;

    /// <summary>同一条卡顿最多每 2 秒记一次，避免连续卡顿时把日志写爆。</summary>
    private const long ReportCooldownMs = 2000;

    /// <summary>超过这个间隔视为窗口最小化/挂起，不当成 UI 卡顿。</summary>
    private const long SuspendMs = 5000;

    private static IDispatcherTimer? _timer;
    private static long _lastTick;
    private static long _lastReport;
    private static bool _started;

    public static void Start()
    {
        if (_started) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        try
        {
            _started = true;
            _lastTick = Environment.TickCount64;
            _timer = dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(100);
            _timer.Tick += (_, _) => OnTick();
            _timer.Start();
            PerfLog.Note("UI 看门狗", $"已启动（>{ThresholdMs}ms 记一条）");
        }
        catch (Exception ex)
        {
            _started = false;
            App.WriteLog("UiStallMonitor.Start -> " + ex.Message);
        }
    }

    public static void Stop()
    {
        try { _timer?.Stop(); } catch { }
        _timer = null;
        _started = false;
    }

    private static void OnTick()
    {
        var now = Environment.TickCount64;
        var gap = now - _lastTick;
        _lastTick = now;

        if (gap < ThresholdMs) return;
        if (gap > SuspendMs)
        {
            // 窗口挂起/最小化后恢复：Timer 停了这么久，不是 UI 线程被业务占住。
            PerfLog.Note("UI 恢复", $"挂起 {gap} ms（不计卡顿）");
            return;
        }
        if (now - _lastReport < ReportCooldownMs) return;
        _lastReport = now;

        var phase = PerfLog.Phase;
        PerfLog.Note("UI 卡顿",
            $"{gap} ms · {phase}{(phase.Length > 0 ? "" : "（无阶段标记）")} · GC2={GC.CollectionCount(2)}");
    }
}
