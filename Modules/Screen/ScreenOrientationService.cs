#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Java.Lang;

namespace WarmAsBefore.Modules.Screen;

/// <summary>
/// 屏幕方向控制（Android 实际锁屏，桌面 no-op）。
///
/// 语义：
///   auto      = 默认竖屏（手机 / 平板都竖屏）
///   landscape = 锁定横屏（电脑版布局）
///   portrait  = 锁定竖屏
///
/// 防呆：Set() 切换方向后立即启动 3 秒计时；到点未确认（Confirm）则自动回弹到上一方向，
/// 并由 UI 弹一次确认框（见 SettingsViewModel.ConfirmOrientationKeepNewAsync 的调用方）。
/// 锁屏经 Java Handler(Looper.Main) 投递到主线程，避免非主线程写 Activity.RequestedOrientation。
/// </summary>
public static class ScreenOrientationService
{
    public const string KeyAuto = "auto";
    public const string KeyLandscape = "landscape";
    public const string KeyPortrait = "portrait";

    static string _choice = KeyAuto;
    static string _prev = KeyPortrait;
    static CancellationTokenSource? _debounceCts;
    static readonly object _lock = new();

    /// <summary>当前设置值（auto / landscape / portrait）。</summary>
    public static string Choice { get; private set; } = KeyAuto;

    /// <summary>当前实际锁定方向（auto 已解析成 portrait）。</summary>
    public static string Resolved { get; private set; } = KeyPortrait;

    /// <summary>方向变化事件（锁定完成时）：参数为新方向。</summary>
    public static event Action<string>? Changed;

    /// <summary>3 秒防呆到点、已回弹到上一方向：参数为回弹后的方向（供 UI 弹确认框）。</summary>
    public static event Action<string>? DebouncedRevert;

    /// <summary>启动时调用：恢复设置值并锁定（不启动防呆计时）。</summary>
    public static void ApplyOnStart(string? choice)
    {
        lock (_lock)
        {
            _choice = Normalize(choice);
            _prev = Resolve(_choice);
            Resolved = _prev;
        }
        LockNative();
        Changed?.Invoke(Resolved);
    }

    /// <summary>用户从设置切换方向：立即锁定新方向，启动 3 秒防呆计时。</summary>
    public static void Set(string? choice)
    {
        var normalized = Normalize(choice);
        lock (_lock)
        {
            _prev = Resolved;
            _choice = normalized;
            Resolved = Resolve(normalized);
            StartDebounce();
        }
        LockNative();
        Changed?.Invoke(Resolved);
    }

    /// <summary>用户在 3 秒内确认保留新方向：停止计时（防呆不回弹）。</summary>
    public static void Confirm()
    {
        _debounceCts?.Cancel();
        _debounceCts = null;
    }

    /// <summary>auto 解析成实际锁定方向：auto → 竖屏（默认），landscape → 横屏。</summary>
    public static string Resolve(string? choice)
        => Normalize(choice) == KeyLandscape ? KeyLandscape : KeyPortrait;

    static string Normalize(string? c)
        => (c ?? KeyAuto).Trim().ToLowerInvariant() switch
        {
            "landscape" or "横向" or "横屏" => KeyLandscape,
            "portrait" or "竖向" or "竖屏" => KeyPortrait,
            _ => KeyAuto,
        };

    /// <summary>启动 3 秒防呆：到点且未被 Confirm 取消 → 回弹到上一方向并广播 DebouncedRevert。</summary>
    static void StartDebounce()
    {
        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        var token = cts.Token;
        var target = Resolved;
        _ = Task.Delay(TimeSpan.FromSeconds(3), token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            // 3 秒无确认：回弹
            lock (_lock)
            {
                _choice = Normalize(_prev);
                Resolved = _prev;
            }
            LockNative();
            DebouncedRevert?.Invoke(Resolved);
        }, CancellationToken.None);
    }

    /// <summary>把当前方向锁投递到 Android 主线程；失败只记日志（锁屏失败不该崩应用）。</summary>
    static void LockNative()
    {
#if ANDROID
        try
        {
            var target = Resolved == KeyLandscape
                ? (int)ScreenOrientation.Landscape
                : (int)ScreenOrientation.Portrait;
            var handler = new Handler(Looper.Main!);
            handler.Post(new ScreenOrientationRunnable(target));
        }
        catch (Exception ex)
        {
            App.WriteLog("ScreenOrientationService.LockNative -> " + ex.Message);
        }
#endif
    }
}

/// <summary>Android 侧 Runnable：在主线程把方向值设到当前 Activity。需保持类名与包名一致。</summary>
#if ANDROID
using Android.Views;

namespace WarmAsBefore;

[Register("com/warmasbefore/ScreenOrientationRunnable")]
public class ScreenOrientationRunnable : Java.Lang.Object, IRunnable
{
    int _target;
    public ScreenOrientationRunnable(int target) => _target = target;

    public void Run()
    {
        try
        {
            // ActivityThread.currentActivity()
            var atType = Java.Lang.Class.ForName("android.app.ActivityThread");
            var activity = atType.CallStaticMethod<Java.Lang.Object?>("currentActivity");
            if (activity is null) return;
            // Activity.setRequestedOrientation(int)
            var setter = Java.Lang.Class.ForName("android.app.Activity")
                .GetMethod("setRequestedOrientation",
                    Java.Lang.Class.ForName("int"));
            setter.Invoke(activity, (short)_target);
        }
        catch { /* 锁屏失败静默 */ }
    }
}
#endif
