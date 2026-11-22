using System.Windows.Input;

namespace WarmAsBefore.Controls;

/// <summary>页头形态：<see cref="Bar"/> 是带底与分隔线的页面级顶栏，<see cref="Ground"/> 是落在地上的一页头。</summary>
public enum PageHeaderVariant
{
    Bar,
    Ground
}

/// <summary>
/// 全站唯一的页头（结构见 PageHeader.xaml 顶部注释）。
///
/// 用 BindableProperty 而不是让页面往里塞控件：页头要能被页面自己的 VM 直接绑定
/// （<c>Title="{Binding CharacterName}"</c>），否则每页还得在 code-behind 里手工推一次文字，
/// 而手工推的那份总会有人忘了跟着数据变。
/// </summary>
public partial class PageHeader : ContentView
{
    public PageHeader()
    {
        InitializeComponent();
        ApplyVariant();
        ApplyBack();
        WireBadge();
    }

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(PageHeader), "");

    public static readonly BindableProperty SubtitleProperty =
        BindableProperty.Create(nameof(Subtitle), typeof(string), typeof(PageHeader), "");

    public static readonly BindableProperty EyebrowProperty =
        BindableProperty.Create(nameof(Eyebrow), typeof(string), typeof(PageHeader), "");

    public static readonly BindableProperty BackTextProperty =
        BindableProperty.Create(nameof(BackText), typeof(string), typeof(PageHeader), "< 返回");

    public static readonly BindableProperty ShowBackProperty =
        BindableProperty.Create(nameof(ShowBack), typeof(bool), typeof(PageHeader), true,
            propertyChanged: (b, _, _) => ((PageHeader)b).ApplyBack());

    public static readonly BindableProperty BackCommandProperty =
        BindableProperty.Create(nameof(BackCommand), typeof(ICommand), typeof(PageHeader), null,
            propertyChanged: (b, _, _) => ((PageHeader)b).ApplyBack());

    public static readonly BindableProperty VariantProperty =
        BindableProperty.Create(nameof(Variant), typeof(PageHeaderVariant), typeof(PageHeader),
            PageHeaderVariant.Bar, propertyChanged: (b, _, _) => ((PageHeader)b).ApplyVariant());

    public static readonly BindableProperty ActionsProperty =
        BindableProperty.Create(nameof(Actions), typeof(View), typeof(PageHeader), null,
            propertyChanged: (b, o, n) => ((PageHeader)b).ApplyActions(o as View, n as View));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public string Eyebrow { get => (string)GetValue(EyebrowProperty); set => SetValue(EyebrowProperty, value); }
    public string BackText { get => (string)GetValue(BackTextProperty); set => SetValue(BackTextProperty, value); }
    public bool ShowBack { get => (bool)GetValue(ShowBackProperty); set => SetValue(ShowBackProperty, value); }
    public ICommand? BackCommand { get => (ICommand?)GetValue(BackCommandProperty); set => SetValue(BackCommandProperty, value); }
    public PageHeaderVariant Variant { get => (PageHeaderVariant)GetValue(VariantProperty); set => SetValue(VariantProperty, value); }
    public View? Actions { get => (View?)GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }

    /// <summary>返回钮只在「有命令且没被显式关掉」时出现 —— 一颗点了不动的东西比没有更糟。</summary>
    private void ApplyBack() => Back.IsVisible = ShowBack && BackCommand is not null;

    /// <summary>
    /// 形态切换。Ground 档把底与线一起摘掉：这两样是「页面级顶栏」的标记，
    /// 一个落在地上的一页头如果也带它们，就退化成一张没用的横条。
    /// </summary>
    private void ApplyVariant()
    {
        var ground = Variant == PageHeaderVariant.Ground;
        Rule.IsVisible = !ground;
        if (ground)
        {
            Bar.Padding = new Thickness(0);
            Bar.MinimumHeightRequest = -1;
            Bar.Background = null;
            return;
        }
        // 走 DynamicResource 而不是把令牌读成值：窗口尺度换档时页头要跟着缩，
        // 一次性读出来的 Thickness 会让整页留在一档旧尺度里。
        Bar.SetDynamicResource(PaddingProperty, "WHeaderPad");
        Bar.SetDynamicResource(MinimumHeightRequestProperty, "WHeaderHeight");
        Bar.SetDynamicResource(BackgroundProperty, "WBarBrush");
    }

    private void ApplyActions(View? oldView, View? newView)
    {
        if (oldView is not null) Right.Remove(oldView);
        if (newView is not null)
        {
            newView.VerticalOptions = LayoutOptions.Center;
            Right.Add(newView);
        }
    }

    /// <summary>
    /// 压测徽标在整页都没有动作时也得站得住：右列本来只装徽标，
    /// 徽标不显示时那一列宽 0，Grid 的 Auto 列不会留下一条空隙。
    /// </summary>
    private void WireBadge()
    {
        Badge.HorizontalOptions = LayoutOptions.End;
        Right.HorizontalOptions = LayoutOptions.End;
    }
}
