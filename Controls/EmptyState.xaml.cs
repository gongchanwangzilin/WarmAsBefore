namespace WarmAsBefore.Controls;

/// <summary>
/// 空态（结构见 EmptyState.xaml 顶部注释）：标题说「这里是空的」，
/// 原因说「为什么空」，出路说「怎么才不空」。
///
/// 三段是三个属性而不是一整段文字，因为它们在页面上承担不同的重量：
/// 标题是结论（正文号加粗），原因是解释（灰），出路是行动（信号墨）。
/// 合成一段就只能一种字重，读起来仍是一句「暂无数据」。
/// </summary>
public partial class EmptyState : ContentView
{
    public EmptyState() => InitializeComponent();

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(EmptyState), "还没有内容");

    public static readonly BindableProperty ReasonProperty =
        BindableProperty.Create(nameof(Reason), typeof(string), typeof(EmptyState), "");

    public static readonly BindableProperty HintProperty =
        BindableProperty.Create(nameof(Hint), typeof(string), typeof(EmptyState), "");

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Reason { get => (string)GetValue(ReasonProperty); set => SetValue(ReasonProperty, value); }
    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }
}
