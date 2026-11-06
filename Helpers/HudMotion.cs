using WarmAsBefore.DesignSystem.Theme;

namespace WarmAsBefore.Helpers;

/// <summary>
/// 终末地 HUD 的专属动效：不是「换了个时长」，而是几段只在 HUD 世界出现的机关动作 ——
/// 扫描线扫过、括号逐角亮起、标题「上电」闪一下、选中项来回划一刀。
///
/// 三条硬约束（与全站动效契约一致，见 <see cref="Motion"/>）：
/// 1. 只动合成层属性（TranslationX/Y、ScaleX/Y、Opacity），不碰布局 → 不会在低端机上一帧一帧地重排；
/// 2. 模型值永远先落到终态，动画只是叠在上面的一段过渡 —— 被打断/被页面切换顶掉也不会停在半路；
/// 3. 关闭动效（系统「减少动画」或设置里的性能模式）时直接返回，状态照常切换。
/// </summary>
public static class HudMotion
{
    private static readonly Easing SnapEase = new(MotionTokens.Snap);
    private static readonly Easing MechEase = new(MotionTokens.Mech);

    /// <summary>世界是不是终末地 HUD（每次现算，换世界后立刻生效）；关闭动效时一律为假。</summary>
    public static bool IsHud => !MotionTokens.Disabled && IsHudWorld();

    private static bool IsHudWorld()
    {
        try { return DesignSystem.Worlds.StyleWorldTokens.World == DesignSystem.Worlds.StyleWorld.Endfield; }
        catch { return false; }
    }

    /// <summary>
    /// 扫描线：一条 1–2px 的细线从元素顶部扫到底部再消失（HUD 的「开机自检」）。
    /// 只做一次，2 段位移；落点写入终态之前先归位，下一次再播不会从半路起。
    /// </summary>
    public static async Task ScanAsync(VisualElement sweep, VisualElement host)
    {
        if (sweep is null || host is null) return;
        if (!IsHud) { sweep.Opacity = 0; return; }
        try
        {
            sweep.Opacity = 0;
            sweep.TranslationY = 0;
            sweep.Opacity = 0.9;
            await sweep.TranslateTo(0, Math.Max(24, host.Height), 520, SnapEase);
        }
        catch { /* 页面被顶掉：收尾在 finally 里做，别把线留在半路 */ }
        finally
        {
            sweep.Opacity = 0;
            sweep.TranslationY = 0;
        }
    }

    /// <summary>
    /// 标题上电：字先暗一档、横向抖 2px、再亮回 —— 像终端接口接通的公告。
    /// 用 ScaleX 做「横向压扁再回弹」，代替手写逐字动画，视觉上是「闪一下」而不是「打字机」。
    /// </summary>
    public static async Task PowerOnAsync(VisualElement title)
    {
        if (title is null || !IsHud) return;
        try
        {
            title.ScaleX = 0.985;
            title.Opacity = 0.55;
            await Task.WhenAll(
                title.ScaleTo(1, 260, SnapEase),
                title.FadeTo(1, 260, Easing.CubicOut));
            // 第二下小抖动：让「上电」读成机械反馈，而不是普通的淡入
            await title.TranslateTo(-2, 0, 40, Easing.Linear);
            await title.TranslateTo(2, 0, 50, Easing.Linear);
            await title.TranslateTo(0, 0, 70, MechEase);
        }
        catch { }
        finally
        {
            title.ScaleX = 1;
            title.Opacity = 1;
            title.TranslationX = 0;
        }
    }

    /// <summary>
    /// 划一刀：被选中的行/卡片从左往右「擦」出高亮（ScaleX 0→1，锚点在左）。
    /// 归高亮层自己播，行本身不动：行一动整列表都要重排。
    /// </summary>
    public static async Task WipeInAsync(VisualElement layer)
    {
        if (layer is null) return;
        layer.AnchorX = 0;
        if (!IsHud) { layer.ScaleX = 1; return; }
        try
        {
            layer.ScaleX = 0;
            await layer.ScaleTo(1, 180, MechEase);
        }
        catch { }
        finally { layer.ScaleX = 1; }
    }

    /// <summary>括号逐角亮起：四个角的括线依次淡入（左上一拍、右上二拍…）。元素顺序由调用方给。</summary>
    public static async Task BracketsInAsync(IReadOnlyList<VisualElement> corners)
    {
        if (corners is null || corners.Count == 0) return;
        if (!IsHud)
        {
            foreach (var c in corners) c.Opacity = 1;
            return;
        }
        foreach (var c in corners) c.Opacity = 0;
        try
        {
            for (var i = 0; i < corners.Count; i++)
            {
                var corner = corners[i];
                if (corner is null) continue;
                _ = corner.FadeTo(1, 110, Easing.CubicOut);
                await Task.Delay(60);
            }
        }
        catch { }
        finally
        {
            foreach (var c in corners) if (c is not null) c.Opacity = 1;
        }
    }

    /// <summary>提示条「咔哒」一下：能量条/好感条数值变化时，整条先压暗再弹回（不改布局、不改数值）。</summary>
    public static async Task TickAsync(VisualElement bar)
    {
        if (bar is null || !IsHud) return;
        try
        {
            bar.ScaleY = 0.6;
            await bar.ScaleTo(1, 130, MechEase);
        }
        catch { }
        finally { bar.ScaleY = 1; }
    }
}
