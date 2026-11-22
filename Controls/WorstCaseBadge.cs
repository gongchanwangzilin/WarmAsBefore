using Microsoft.Maui.Controls.Shapes;
using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Services.Dev;

namespace WarmAsBefore.Controls;

/// <summary>
/// 压测档徽标：压测档生效时，在页头右侧挂一枚「压测 · Worst」的胶囊。
///
/// 为什么不是原来那条底部小 bar：那条 bar 说明了「现在是什么档」，但只在有人低头看页面底
/// 时成立；截一张屏、读一个数字的时候，没人知道这一屏是 fixture 还是真数据。
/// 所以档位的身份要跟页头走 —— 页头是每页唯一一定被读到的那一行。
///
/// 三条约束：
/// 1. 它不是被测设计的一部分，所以颜色只借用世界的信号色（胶囊 = 信号淡底 + 信号墨），
///    不发明新的色相；换世界它跟着变，不会在四档主题里显得像外来件。
/// 2. 字号与页头副标题同级（WSizeCaption），加粗一档 —— 比标题轻、比正文里的说明重。
/// 3. 只在「压测开关带了参数 + 当前档不是 Demo」时出现；进场淡入一次，之后换档只换字。
/// </summary>
public sealed class WorstCaseBadge : ContentView
{
    private readonly Label _ink;
    private readonly Border _pill;
    private bool _entered;
    private bool _hooked;

    public WorstCaseBadge()
    {
        _ink = new Label
        {
            MaxLines = 1,
            LineBreakMode = LineBreakMode.NoWrap,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center
        };
        _ink.SetDynamicResource(Label.FontSizeProperty, "WSizeCaption");
        _ink.SetDynamicResource(Label.TextColorProperty, "SignalFg");

        _pill = new Border
        {
            StrokeThickness = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = _ink
        };
        _pill.SetDynamicResource(Border.PaddingProperty, "WPillPad");
        _pill.SetDynamicResource(Border.BackgroundColorProperty, "SignalTint");
        _pill.SetDynamicResource(Border.StrokeShapeProperty, "WRadiusPill");

        Content = _pill;
        VerticalOptions = LayoutOptions.Center;
        HorizontalOptions = LayoutOptions.Center;

        IsVisible = false;
        Refresh();
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        // 页头是 transient：来回导航会造出一大把徽标。离开页面时摘掉静态事件的引用，
        // 否则每条压测切换都要唤醒一堆早就没人看的页头（也是页面的隐形泄漏）。
        if (Parent is null) WorstCaseProvider.ModeChanged -= Refresh;
        else if (!_hooked) { _hooked = true; WorstCaseProvider.ModeChanged += Refresh; }
        Refresh();
        if (IsVisible && !_entered) Reveal();
    }

    private void Refresh()
    {
        var on = WorstCaseProvider.EnabledAtLaunch && WorstCaseProvider.IsActive;
        _ink.Text = "压测 · " + WorstCaseProvider.Name(WorstCaseProvider.Mode);
        if (on == IsVisible) return;
        IsVisible = on;
        if (on && Parent is not null && !_entered) Reveal();
    }

    /// <summary>
    /// 淡入一次。动效关掉时（性能模式 / 系统「减少动画」）直接落到终值 ——
    /// 一条开发用的徽标不值得为它留一帧空白。
    /// </summary>
    private void Reveal()
    {
        _entered = true;
        var ms = MotionTokens.Ms(240);
        if (ms == 0) { Opacity = 1; return; }
        Opacity = 0;
        _ = Microsoft.Maui.Controls.ViewExtensions.FadeTo(this, 1, (uint)ms, Easing.CubicOut);
    }
}
