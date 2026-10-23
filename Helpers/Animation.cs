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
        // 这里以前还会把 Opacity 归 0 再淡入。删掉了：Opacity 是禁用态/卡片显隐/进场共用的输出量，
        // 动画回调一旦丢（页面被顶掉、同名抢占），元素就永久停在看不见那一头（「再续情缘」按钮消失的成因）。
        // 现在只位移——最坏低 18px，内容不会消失。
        if (ms == 0) { e.TranslationY = 0; return; }
        e.TranslationY = MotionTokens.EnterRisePx * 3;
        await e.TranslateTo(0, 0, ms, Easing.CubicOut);
        e.TranslationY = 0;
    }

    /// <summary>
    /// 一组东西次第浮起：先等 <paramref name="delayMs"/>，再「上浮 6px」走完。
    /// 间隔取 <see cref="MotionTokens.StaggerMs"/>：同一帧里全冒出来是「铺满」，
    /// 一张差 45ms 才是「一件一件摆上来」—— 内容没变，读起来才有先后。
    ///
    /// 和 <see cref="Rise"/> 一样不碰 Opacity，理由在这里更硬：收尾写在 finally 里，
    /// 而 finally 属于 await 之后的续接——动画回调一旦丢失（页面被顶掉、ticker 不再推进），
    /// 续接根本不跑，finally 也就不会跑，元素被永久留在写下去的那个值上。
    /// 淡入的起点是 Opacity=0，于是「再续情缘」就这么不见了；位移的起点是 6px，最坏只是低了一点。
    /// 关掉动效（减少动画偏好 / WARM_NO_MOTION）时延时和时长都归 0：直接落到终值，不排队等待。
    /// </summary>
    public static async Task Enter(VisualElement e, int delayMs = 0, uint durationMs = 0)
    {
        if (e is null) return;
        var ms = durationMs > 0 ? durationMs : (uint)MotionTokens.Ms(MotionTokens.EnterMs);
        if (MotionTokens.Disabled) ms = 0;
        var wait = MotionTokens.Disabled ? 0 : Math.Max(0, delayMs);
        try
        {
            if (wait > 0) await Task.Delay(wait);
            if (ms == 0) return;
            e.TranslationY = MotionTokens.EnterRisePx;
            await e.TranslateTo(0, 0, ms, Easing.CubicOut);
        }
        finally
        {
            e.TranslationY = 0;
        }
    }

    /// <summary>一行的错位量：第 index 行该等多久（只算到 <paramref name="maxStagger"/> 行，后面的同时到位——三十行排三十次会显得拖沓）。</summary>
    public static int StaggerDelay(int index, int maxStagger = 8)
        => Math.Max(0, Math.Min(index, maxStagger)) * MotionTokens.Ms(MotionTokens.StaggerMs);

    // ── 进场契约 ────────────────────────────────────────────────────────
    // 全站只有这一个入口负责「页面第一次出现时怎么进来」。之前是 26 个页面各自没有、
    // 4 个页面自己写 FadeTo、2 个页面自己调 Enter —— 用户在页面之间走一圈，
    // 看到的是「有的门有弹簧、有的门是死的」，这就是「动效不连贯」的全部含义。
    // 连贯不需要花哨，需要的是同一个起点、同一个步长、同一个次数上限。

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, object> Arrived = new();

    /// <summary>
    /// 把一组「带」按同一节奏摆上来。每个元素只演一次（按实例记，不按页面名记）：
    /// 从设置页返回时内容已经在那儿了，再排一次队只会让人觉得「回来得比离开慢」。
    /// 弱表跟着元素一起回收，导航几十次不会攒下一个越写越长的名单。
    /// </summary>
    public static void Arrive(params VisualElement[] bands)
    {
        var n = 0;
        foreach (var b in bands)
        {
            if (b is null) continue;
            if (Arrived.TryGetValue(b, out _)) continue;
            try { Arrived.Add(b, new object()); } catch (ArgumentException) { continue; }
            _ = Enter(b, StaggerDelay(n));
            n++;
        }
    }

    /// <summary>
    /// 自定义时长的「带」进场：和 <see cref="Arrive(VisualElement[])"/> 同一张「演过没有」名单，
    /// 只是时长由调用方给（设置子页的卡片用自己的 CardEnterMs）。
    /// 单独开一个重载而不是给 Arrive 加可选参数，是为了保住 <c>Arrive(a, b, c)</c> 这种 params 写法。
    /// </summary>
    public static void Arrive(IReadOnlyList<VisualElement> bands, uint durationMs = 0)
    {
        var n = 0;
        foreach (var b in bands)
        {
            if (b is null) continue;
            if (Arrived.TryGetValue(b, out _)) continue;
            try { Arrived.Add(b, new object()); } catch (ArgumentException) { continue; }
            _ = Enter(b, StaggerDelay(n), durationMs);
            n++;
        }
    }

    /// <summary>
    /// 整页兜底：页面没有为「带」留名字时，退一步把根布局的直接子元素当成带。
    /// 子元素多于 12 个就不排了（那是列表，不是几条带 —— 给列表排错峰只会让首屏变慢），
    /// 整页一起浮一次；根不是布局时也只动它自己。
    /// </summary>
    public static void ArrivePage(ContentPage page)
    {
        var root = page?.Content;
        if (root is null) { if (page is not null) Arrive(page); return; }
        if (root is Layout l && l.Children.Count is > 0 and <= 12)
        {
            Arrive(l.Children.OfType<VisualElement>().ToArray());
            return;
        }
        Arrive(root);
    }
}
