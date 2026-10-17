using System.Runtime.InteropServices;

namespace WarmAsBefore.DesignSystem.Theme;

/// <summary>
/// 「该不该动」的探测：把系统的减少动画偏好落到 <see cref="MotionTokens.Disabled"/> 上。
/// 关掉之后状态照样切换（按下仍会沉、仍会压暗），只是不再补中间帧——
/// 前庭功能敏感或远程桌面这类场景要的是「别晃」，不是「没有反馈」。
/// </summary>
public static class MotionPrefs
{
    /// <summary>探测一次并写入开关；探测不到时保持原值（宁可多动一下，也别把反馈弄没）。</summary>
    public static void Apply()
    {
#if WINDOWS
        try { MotionTokens.Disabled |= SystemAnimationsOff(); }
        catch { /* 取不到就当没关 */ }
#endif
    }

#if WINDOWS
    private const uint SPI_GETANIMATION = 0x0048;

    [StructLayout(LayoutKind.Sequential)]
    private struct AnimationInfo
    {
        public int cbSize;
        public int iMinAnimate;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref AnimationInfo value, uint flags);

    /// <summary>iMinAnimate 为 0 表示用户在系统里关掉了动画（Win32 的老开关，WinUI 的设置项也写这里）。</summary>
    private static bool SystemAnimationsOff()
    {
        var info = new AnimationInfo { cbSize = Marshal.SizeOf<AnimationInfo>() };
        return SystemParametersInfo(SPI_GETANIMATION, 0, ref info, 0) && info.iMinAnimate == 0;
    }
#endif
}
