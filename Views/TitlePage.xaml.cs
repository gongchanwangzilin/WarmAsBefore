using WarmAsBefore.Helpers;
using WarmAsBefore.Services;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class TitlePage : ContentPage
{
    public TitlePage(TitleViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is TitleViewModel vm)
            vm.RefreshStateCommand.Execute(null);

        // 启动卡排在进场之前：遮罩盖着的时候底下那四段该已经摆好了，
        // 卡收掉时读到的是「界面本来就在这儿」，而不是界面正在身后现搭。
        BuildMark();
        BuildName();
        IntroCard.Play(
            new IntroCard.Parts(IntroCurtain, IntroBand, IntroRule, IntroLockup, IntroNameRow, IntroHint,
                IntroMark.Children.OfType<VisualElement>().ToList(),
                IntroNameRow.Children.OfType<VisualElement>().ToList()),
            () =>
            {
                if (BindingContext is not TitleViewModel v) return;
                v.ShowIntro = false;
                // 卡一收就交给配置：首跑的人不该先看见标题页再被跳走一次。
                v.OpenSetupIfPending();
            });

        // 四段按同一节奏摆上来：抬头 → 入口列表 → 主行动 → 带底签名。
        // 走全站唯一的进场入口（见 Motion.Arrive）：起点、步长、只演一次都由它管，
        // 页面这里只负责交出「有哪些带」。
        Motion.Arrive(BrandBand, MenuStack, HeroBand, RailFoot);
    }

    /// <summary>点卡片任意处收掉：等不及的人要的是「进去」，不是「看完整版」。</summary>
    private void OnIntroTapped(object? sender, TappedEventArgs e) => IntroCard.Skip();

    /// <summary>
    /// 名牌按当前语言的 Brand_Mark 逐字建出来。一个 Label 一个字是必需的：逐字揭示要的是
    /// 每个字有自己的透明度，而 Label 内部 Span 没有这个可调量。
    /// 起始态一律 Opacity=0（不是靠时间轴的第 0 帧才归零）——「动画还没起跑」的那一帧也得被盖住。
    /// </summary>
    private void BuildMark()
    {
        IntroMark.Children.Clear();
        var brand = LocalizationService.Current["Brand_Mark"];
        var size = Application.Current?.Resources.TryGetValue("WSizeBrand", out var v) == true && v is double d ? d : 40;
        IntroMark.Spacing = Math.Round(size * 0.16);
        foreach (var ch in brand)
        {
            var glyph = new Label { Text = ch.ToString(), FontAttributes = FontAttributes.Bold, Opacity = 0 };
            glyph.SetDynamicResource(Label.FontSizeProperty, "WSizeBrand");
            glyph.SetDynamicResource(Label.TextColorProperty, "WarmDark");
            IntroMark.Children.Add(glyph);
        }
    }

    /// <summary>
    /// 本地化名同样逐字建，但用途不同：这里要的是每个字能被单独平移（收字距），
    /// 淡入则整行一起做（Opacity 落在 IntroNameRow 这个容器上，所以字本身一律 1）。
    /// 空格得自己给宽度：一个只含空白的 Label 在 WinUI 上量出来是 0 宽，
    /// 「Warm As Before」会直接挤成「WarmAsBefore」—— 按字号补一个词宽（0.30 em）。
    /// </summary>
    private void BuildName()
    {
        IntroNameRow.Children.Clear();
        var size = Application.Current?.Resources.TryGetValue("WSizeDisplay", out var v) == true && v is double d ? d : 32;
        foreach (var ch in LocalizationService.Current["Brand_Name"])
        {
            var space = char.IsWhiteSpace(ch);
            var glyph = new Label
            {
                Text = space ? "" : ch.ToString(),
                WidthRequest = space ? Math.Round(size * 0.30) : -1,
                Opacity = 1
            };
            if (!space)
            {
                glyph.SetDynamicResource(Label.FontSizeProperty, "WSizeDisplay");
                glyph.SetDynamicResource(Label.TextColorProperty, "WarmBody");
            }
            IntroNameRow.Children.Add(glyph);
        }
    }
}
