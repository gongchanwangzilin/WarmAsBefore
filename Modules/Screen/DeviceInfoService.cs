#nullable enable
using Microsoft.Maui.ApplicationModel;

namespace WarmAsBefore.Modules.Screen;

/// <summary>
/// 设备/屏幕判定，使用 MAUI 原生 DeviceInfo.Idiom。
/// Android 上 DeviceInfo.Idiom == DeviceIdiom.Tablet 基于系统 smallestScreenWidth（官方 600dp 标准），
/// 不会因机型密度不同而误判大屏手机为平板。
/// 桌面端无平板概念，恒 false。
/// </summary>
public static class DeviceInfoService
{
    /// <summary>当前是否平板。非移动端（桌面）恒 false。</summary>
    public static bool IsTabletScreen()
    {
#if ANDROID || IOS
        return DeviceInfo.Idiom == DeviceIdiom.Tablet;
#else
        return false;
#endif
    }
}