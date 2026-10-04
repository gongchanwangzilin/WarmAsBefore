#nullable enable
using Microsoft.Maui.ApplicationModel;

namespace WarmAsBefore.Modules.Screen;

/// <summary>
/// 设备/屏幕判定（按屏幕分辨率判平板，不用 DeviceInfo.Idiom——部分 Android 机型/模拟器 Idiom 识别不准）。
/// 判定规则：屏幕「短边」≥ 600 逻辑像素（dp）视为平板（Android 7" 平板 min-width 标准 = 600dp，
/// 手机短边 ~400dp 不会误判）。桌面端无平板概念，恒 false。
/// </summary>
public static class DeviceInfoService
{
    /// <summary>短边（dp）达到此值即判定为平板（Android 平板 min-width 标准）。</summary>
    const double TabletShortEdgeDp = 600;

    /// <summary>当前是否平板（按屏幕短边判定）。非移动端恒 false。</summary>
    public static bool IsTabletScreen()
    {
#if ANDROID
        try
        {
            // 直接取原生 DisplayMetrics（真实像素 / 密度比 = dp），不依赖 MAUI 的 DisplayUnit API
            // 通过 Activity 上下文拿 Context 的 DisplayMetrics（Resources.System 是静态属性，不能实例引用）
            var activity = WarmAsBefore.Platforms.Android.MainActivity.CurrentActivity
                ?? Android.App.Application.Context as Android.App.Activity;
            var dm = (activity ?? Android.App.Application.Context).Resources.DisplayMetrics;
            double widthDp = dm.WidthPixels / (double)dm.Density;
            double heightDp = dm.HeightPixels / (double)dm.Density;
            double shortEdge = Math.Min(widthDp, heightDp);
            return shortEdge >= TabletShortEdgeDp;
        }
        catch
        {
            return false;
        }
#elif IOS
        return false;
#else
        return false;
#endif
    }
}
