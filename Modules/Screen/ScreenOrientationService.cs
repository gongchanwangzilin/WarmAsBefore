#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;

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
/// 并由 UI 弹一次确认框（见 SettingsPage.xaml.cs 的 DebouncedRevert 订阅）。
/// 锁屏经 Java Handler(Looper.MainLooper) 投递到主线程，避免非主线程写 Activity.RequestedOrientation。
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

    /// <summary>auto 解析成实际锁定方向：按设备自动选（手机→竖屏，平板→跟随系统/横屏），landscape → 横屏。</summary>
    public static string Resolve(string? choice)
    {
        var normalized = Normalize(choice);
        if (normalized == KeyLandscape) return KeyLandscape;
        if (normalized == KeyPortrait) return KeyPortrait;
        // auto：手机竖屏，平板横屏（电脑版布局）。用 AppInfo/DeviceInfo.Idiom 判断，
        // 避免 DeviceType 枚举缺失（net10 绑定里 DeviceType 无 Tablet 成员，踩过坑）。
#if ANDROID || IOS
        try
        {
            var idiom = DeviceInfo.Idiom;
            return idiom == DeviceIdiom.Tablet ? KeyLandscape : KeyPortrait;
        }
        catch { return KeyPortrait; }
#else
        return KeyPortrait;
#endif
    }

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

    /// <summary>把当前方向锁写到当前 Activity 的 RequestedOrientation；失败只记日志（锁屏失败不该崩应用）。</summary>
    static void LockNative()
    {
#if ANDROID
        try
        {
            var target = Resolved == KeyLandscape
                ? Android.Content.PM.ScreenOrientation.Landscape
                : Android.Content.PM.ScreenOrientation.Portrait;
            var activity = WarmAsBefore.Platforms.Android.MainActivity.CurrentActivity;
            if (activity is not null)
            {
                activity.RequestedOrientation = target;
            }
            else
            {
                App.WriteLog("ScreenOrientationService: 无当前 Activity（启动初期），跳过本次锁屏");
            }
        }
        catch (System.Exception ex)
        {
            App.WriteLog("ScreenOrientationService.LockNative -> " + ex.Message);
        }
#else
        App.WriteLog("ScreenOrientationService: 桌面端忽略屏幕方向=" + Resolved);
#endif
    }
}
