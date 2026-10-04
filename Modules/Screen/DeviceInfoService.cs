#nullable enable
using Microsoft.Maui.ApplicationModel;

namespace WarmAsBefore.Modules.Screen;

/// <summary>
/// 设备/屏幕判定（走屏幕分辨率，不用 DeviceInfo.Idiom——部分 Android 机型/模拟器 Idiom 识别不准）。
/// 判定规则：屏幕「长边」≥ 500 逻辑像素（dp）视为平板。
///   - 手机典型：720p 竖屏 ≈ 411×740dp（长边 740 但短边 411，按长边 740 会误判？见下）
///   - 实际用「短边」判定更稳：短边 ≥ 600dp = 平板（Google 官方 Android 7" 平板 = 600dp 最小宽度标准）。
///   这里取短边 ≥ 600 判定平板，覆盖主流 7"+ 平板，手机（短边 ~400dp）不会被误判。
/// </summary>
public static class DeviceInfoService
{
    /// <summary>短边（dp）达到此值即判定为平板（Android 平板 min-width 标准 = 600dp）。</summary>
    const double TabletShortEdgeDp = 600;

    /// <summary>当前是否平板（按屏幕短边判定）。非移动端（桌面）恒 false。</summary>
    public static bool IsTabletScreen()
    {
#if ANDROID || IOS
        try
        {
            var info = DeviceInfo.Info;
            var width = info.Width;
            var height = info.Height;
            var shortEdge = Math.Min(width, height);
            return shortEdge >= TabletShortEdgeDp;
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }
}
