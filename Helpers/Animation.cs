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
    /// <summary>入场统一曲线（终末地 snap）。MAUI 的 Easing 要一个委托，这里只建一次。</summary>
    private static readonly Easing SnapEase = new(MotionTokens.Snap);

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
        await e.TranslateTo(0, 0, ms, SnapEase);
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
        await e.TranslateTo(0, 0, ms, SnapEase);
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
            await e.TranslateTo(0, 0, ms, SnapEase);
        }
        finally
        {
            e.TranslationY = 0;
        }
    }

    /// <summary>一行的错位量：第 index 行该等多久（只算到 <paramref name="maxStagger"/> 行，后面的同时到位——三十行排三十次会显得拖沓）。</summary>
    public static int StaggerDelay(int index, int maxStagger = 8)
        => Math.Max(0, Math.Min(index, maxStagger)) * MotionTokens.Ms(MotionTokens.StaggerMs);

    /// <summary>
    /// 页面切换的方向感。WinUI 上的 Shell 换页是硬切，没有过场；用户读到的就是「每页都卡一下」。
    /// 新页入场时整块从右侧（前进）或左侧（返回）滑回原位，只动根布局的 TranslationX，
    /// 与 <see cref="ArrivePage"/> 给子元素排的纵向错峰各写各的通道，互不抢占。
    ///
    /// 只滑 32px：这是「方向提示」，不是「整页飞进来」——幅度大了，页面底色与窗口底之间会露出缝。
    /// 大多数页在 ContentPage 上铺了 PageBgBrush，滑出去的那条边看到的是同一块底色，读不出位移的边界。
    /// </summary>
    public static async Task PageIn(Page page, bool back)
    {
        var root = (page as ContentPage)?.Content;
        if (root is null || MotionTokens.Disabled) return;

#if WINDOWS
        // 合成层：位移/淡入/纵深都跑在渲染线程上，UI 线程在装载新页时再忙也吃不掉这段路。
        // 这是「过场看不见」的根治——以前用 MAUI 的 TranslateTo（UI 线程 tick），
        // 页面一沉，动画的第一帧就落在终点，用户看到的还是硬切。
        if (PageInOnCompositor(root, back)) return;
        // 页面刚被 Shell 顶上来时原生面板可能还没建好；等它进树（Loaded）再起跑一次。
        // 合成动画的时钟从 StartAnimation 那刻算，等到的这一下正好落在它第一次能被看见之前。
        await WaitLoaded(root);
        if (PageInOnCompositor(root, back)) return;
#endif

        // 退路（非 Windows，或原生层还没挂上）：等真帧，再走 MAUI 的位移动画。
        var ms = (uint)MotionTokens.Ms(MotionTokens.PageSlideMs);
        if (ms == 0) { root.TranslationX = 0; return; }
        root.TranslationX = back ? -MotionTokens.PageSlidePx : MotionTokens.PageSlidePx;
        try
        {
            await WaitLoaded(root);
            await WaitPaintedAsync();
            await root.TranslateTo(0, 0, ms, SnapEase);
        }
        finally { root.TranslationX = 0; }
    }

#if WINDOWS
    /// <summary>
    /// 终末地式过场：整页从侧面带着一点点纵深「甩」到位。
    /// 三段同时起跑——位移 56px（返回 44px，反向）、透明度从 0.3 迅速抬到 1、
    /// 缩放 1.8% 收平。幅度都压在「看得出换了方向，但不觉得页面在飞」的范围内。
    ///
    /// 全部用合成动画（<see cref="Microsoft.UI.Composition.Compositor"/>）：
    /// 不占 UI 线程，不怕导航后的排版把首帧拖住；终点一律显式落回基值，
    /// 动画被打断也不会把页面留在偏移或半透明上。
    /// </summary>
    private static bool PageInOnCompositor(VisualElement root, bool back)
    {
        try
        {
            if (root.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement fe) return false;
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(fe);
            var comp = visual.Compositor;

            var ms = MotionTokens.Ms(back ? MotionTokens.PageBackMs : MotionTokens.PageSweepMs);
            if (ms <= 0) { fe.Translation = System.Numerics.Vector3.Zero; fe.Opacity = 1; return true; }
            var dx = (float)(back ? -MotionTokens.PageBackPx : MotionTokens.PageSweepPx);
            var dy = back ? 0f : (float)MotionTokens.PageSweepRisePx;

            // XAML 元素的 Translation 默认只是普通属性，合成层上并没有这一条可动画的属性；
            // 不开这个开关，StartAnimation("Translation") 会抛 ArgumentException
            // （「The specified property was not found or cannot be animated」）。
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(fe, true);

            // 基值先归位：合成动画只是在它上面叠一段过渡。
            fe.Translation = System.Numerics.Vector3.Zero;
            fe.Opacity = 1;

            var ease = comp.CreateCubicBezierEasingFunction(
                new System.Numerics.Vector2(0.16f, 1f),    // 前段极快
                new System.Numerics.Vector2(0.30f, 1f));   // 后段长收，不起回弹

            var move = comp.CreateVector3KeyFrameAnimation();
            move.InsertKeyFrame(0f, new System.Numerics.Vector3(dx, dy, 0f));
            move.InsertKeyFrame(1f, System.Numerics.Vector3.Zero, ease);
            move.Duration = TimeSpan.FromMilliseconds(ms);
            move.StopBehavior = Microsoft.UI.Composition.AnimationStopBehavior.SetToFinalValue;

            var fade = comp.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, back ? 0.55f : 0.30f);
            fade.InsertKeyFrame(0.30f, 0.94f);
            fade.InsertKeyFrame(1f, 1f, ease);
            fade.Duration = TimeSpan.FromMilliseconds(ms);
            fade.StopBehavior = Microsoft.UI.Composition.AnimationStopBehavior.SetToFinalValue;

            // 纵深只给前进方向：返回是「退回上一层」，再收一次会显得这一页在挽留。
            if (!back)
            {
                var zoom = comp.CreateVector3KeyFrameAnimation();
                zoom.InsertKeyFrame(0f, new System.Numerics.Vector3(1.018f, 1.018f, 1f));
                zoom.InsertKeyFrame(1f, System.Numerics.Vector3.One, ease);
                zoom.Duration = TimeSpan.FromMilliseconds(ms);
                zoom.StopBehavior = Microsoft.UI.Composition.AnimationStopBehavior.SetToFinalValue;
                // 缩放中心取元素中心；还没排版完（ActualWidth=0）时退到窗口中心。
                var bounds = fe.XamlRoot?.Size ?? Windows.Foundation.Size.Empty;
                var halfX = (float)(fe.ActualWidth > 1 ? fe.ActualWidth : bounds.Width) / 2f;
                var halfY = (float)(fe.ActualHeight > 1 ? fe.ActualHeight : bounds.Height) / 2f;
                visual.CenterPoint = new System.Numerics.Vector3(halfX, halfY, 0f);
                visual.StartAnimation("Scale", zoom);
            }

            visual.StartAnimation("Translation", move);
            visual.StartAnimation("Opacity", fade);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Motion.PageIn(compositor) -> " + ex.Message);
            return false;
        }
    }
#endif

    /// <summary>
    /// 等这一屏真的合成过两帧再起跑。
    ///
    /// 单纯 Task.Delay 是墙上时钟：UI 线程被页面装载占住时，延时早已到期，
    /// 恢复后动画的第一次 tick 拿到一个巨大的 elapsed，直接跳到终点——用户看到的就是没有动画。
    /// 合成帧只会在主线程真的空出来以后才来，等它两次，起点才落在用户能看见的那一帧上。
    /// 上限 1200ms：窗口被遮住/最小化时合成帧不来，也不能让页面一直停在偏移上。
    /// </summary>
    internal static async Task WaitPaintedAsync()
    {
#if WINDOWS
        try
        {
            var tcs = new TaskCompletionSource();
            var seen = 0;
            void OnRendering(object? sender, object e)
            {
                if (++seen < 2) return;
                Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
                tcs.TrySetResult();
            }
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnRendering;
            if (await Task.WhenAny(tcs.Task, Task.Delay(1200)) != tcs.Task)
                Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnRendering;
        }
        catch
        {
            await Task.Delay(MotionTokens.FrameMs);
        }
#else
        await Task.Delay(MotionTokens.FrameMs);
#endif
    }

    private static async Task WaitLoaded(VisualElement element)
    {
        if (element.IsLoaded) return;
        var tcs = new TaskCompletionSource();
        void OnLoaded(object? sender, EventArgs e)
        {
            element.Loaded -= OnLoaded;
            tcs.TrySetResult();
        }
        element.Loaded += OnLoaded;
        // 上限：页面在装载途中被换掉（快速连点）时不能把这条过场挂死。
        if (await Task.WhenAny(tcs.Task, Task.Delay(900)) != tcs.Task) element.Loaded -= OnLoaded;
    }

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
