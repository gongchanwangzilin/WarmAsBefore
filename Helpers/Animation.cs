using WarmAsBefore.DesignSystem.Theme;

namespace WarmAsBefore.Helpers;

/// <summary>
/// 进场与强调类动效。时长与曲线一律取自 <see cref="MotionTokens"/>，
/// 这样「整站节奏」这件事只有一个地方能改。
///
/// 按压反馈不在这里：那需要跟视觉状态与连点互相协调，见 <see cref="PressFeel"/>。
/// </summary>
public static class Motion
{
    /// <summary>
    /// 元素进场：从下方 6px 浮起（用位移而不是缩放，WinUI 下缩放会牵动布局）。
    ///
    /// 故意只位移、不淡入。透明度是好几处共用的输出量——禁用态、卡片自身的显隐、按压反馈，
    /// 进场把 Opacity 归 0 再交给动画，中途任何一次打断（页面被顶掉、动画被同名抢占）都会把
    /// 元素永久留在「看不见」那一头。位移被顶掉最多差 1.6px，看不出来也不会丢内容。
    /// </summary>
    public static async Task Rise(VisualElement e, uint d = 0, double off = 0)
    {
        var ms = d > 0 ? d : (uint)MotionTokens.Ms(MotionTokens.EnterMs);
        var rise = off > 0 ? off : MotionTokens.EnterRisePx;
        if (ms == 0)
        {
            e.TranslationY = 0;
            return;
        }
        e.TranslationY = rise;
        await e.TranslateTo(0, 0, ms, Easing.CubicOut);
        e.TranslationY = 0;
    }

    /// <summary>呼吸：用于「该点了」这类持续提示，别同时挂在多个元素上。</summary>
    public static async Task Pulse(VisualElement e, uint d = 2400)
    {
        while (true)
        {
            await e.FadeTo(0.55, d / 2, Easing.SinInOut);
            await e.FadeTo(1, d / 2, Easing.SinInOut);
        }
    }

    /// <summary>拒绝/失败的横向抖动。</summary>
    public static async Task ShakeX(VisualElement e, uint d = 0)
    {
        var ms = d > 0 ? d : (uint)MotionTokens.Ms(MotionTokens.SettleMs);
        var o = e.TranslationX;
        if (ms == 0) { e.TranslationX = o; return; }
        var step = Math.Max(16u, ms / 5);
        foreach (var _ in Enumerable.Range(0, 2))
        {
            await e.TranslateTo(o - 6, 0, step, Easing.CubicOut);
            await e.TranslateTo(o + 6, 0, step, Easing.CubicOut);
        }
        await e.TranslateTo(o, 0, step, Easing.CubicOut);
    }

    /// <summary>底部弹出/展开。</summary>
    public static async Task SlideUp(VisualElement e, uint d = 0)
    {
        var ms = d > 0 ? d : (uint)MotionTokens.Ms(MotionTokens.EnterMs);
        if (ms == 0) { e.Opacity = 1; e.TranslationY = 0; return; }
        e.TranslationY = MotionTokens.EnterRisePx * 3;
        e.Opacity = 0;
        await Task.WhenAll(e.TranslateTo(0, 0, ms, Easing.CubicOut), e.FadeTo(1, ms, Easing.CubicOut));
        e.TranslationY = 0;
    }
}
