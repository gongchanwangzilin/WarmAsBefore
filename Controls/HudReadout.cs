using Microsoft.Maui.Graphics;
using WarmAsBefore.DesignSystem.Worlds;

namespace WarmAsBefore.Controls;

/// <summary>
/// 终末地 HUD 的「方框读数」一栏：小字标签 + 大号数值 +（可选）一条进度线。
///
/// 为什么单独做一个控件：HUD 的读数是「标签 / 数值 / 进度线」这样一条竖排的小板，
/// 页面上有四五处（时间、天气、好感、精力……）都是这个骨架。写进 XAML 每处要七八行，
/// 而且换值时的「咔哒」动效要各写一遍。这里封装起来：
/// · 只有终末地世界才显示「标签 / 细边框 / 大号数字」这一套排版，其它世界**整块隐藏**，
///   页面原来的读数行原样保留 —— 换世界不会把读数弄丢，也不会多出一块没人要的东西；
/// · 数值变化时在 HUD 世界播一下 <see cref="Helpers.HudMotion.TickAsync"/>（合成层缩放，不触发排版）。
/// </summary>
public sealed class HudReadout : ContentView
{
    private readonly Label _label;
    private readonly Label _value;
    private readonly BoxView _barTrack;
    private readonly BoxView _barFill;
    private readonly Grid _barHost;
    private readonly Border _frame;
    private readonly VerticalStackLayout _stack;

    public HudReadout()
    {
        _label = new Label { FontSize = 10, CharacterSpacing = 1.4, LineBreakMode = LineBreakMode.TailTruncation };
        _label.SetDynamicResource(Microsoft.Maui.Controls.Label.TextColorProperty, "MutedFg");

        _value = new Label
        {
            FontSize = 21,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
        };
        _value.SetDynamicResource(Microsoft.Maui.Controls.Label.TextColorProperty, "SurfaceFg");

        // 进度条宿主（2026-11-15 第三版）：双星列 Grid。
        //  第一版（Grid+Fill+OnSizeAllocated 写 WidthRequest）：条宽反馈进读数自身期望宽度，
        //    Auto 列自引用振荡 → LayoutCycleException（dump 实证死在 OnSizeAllocated）。
        //  第二版（AbsoluteLayout 比例 bounds）：比例子元素在无界宽度（横向 ScrollView 内容区）
        //    测量时得到无穷尺寸，WinUI 原生 Measure 抛 COMException E_FAIL（实测）。
        //  第三版（本版）：双星列 Grid——无界测量下星号按 Auto 收缩、期望宽度恒为 0（不反馈）；
        //    有界排列下按进度权重分割宽度（视觉等价）；进度只改列权重，只触发 Arrange，
        //    期望尺寸不变，布局必然收敛。
        _barFill = new BoxView { HeightRequest = 3, HorizontalOptions = LayoutOptions.Fill };
        _barFill.SetDynamicResource(BackgroundColorProperty, "Signal");
        _barTrack = new BoxView { HeightRequest = 3, HorizontalOptions = LayoutOptions.Fill, Opacity = 0.25 };
        _barTrack.SetDynamicResource(BackgroundColorProperty, "Signal");
        _barHost = new Grid
        {
            HeightRequest = 3,
            IsVisible = false,
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(0, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1, GridUnitType.Star)),
            },
        };
        _barHost.Add(_barFill, 0, 0);
        _barHost.Add(_barTrack, 1, 0);

        _frame = new Border { Padding = new Thickness(9, 5), StrokeThickness = 1 };
        _frame.SetDynamicResource(Border.StrokeProperty, "EdgeStrong");
        _frame.SetDynamicResource(Border.BackgroundColorProperty, "WellBg");

        // 形态（横向还是竖向、有没有边框）由宿主页面的参数决定，
        // 这里只把三个零件装进去 —— 具体版式交回给页面，控件不猜。
        _stack = new VerticalStackLayout { Spacing = 1 };
        _frame.Content = _stack;
        Content = _stack;
        IsVisible = HudSkin.IsActive;
    }

    /// <summary>竖向（默认：标签在上、数值在下、进度线在底）/ 横向（标签与数值并排，适合顶栏）。</summary>
    public bool Horizontal { get; set; }

    /// <summary>是否画那圈细边框（顶栏里通常不要，卡片里要）。</summary>
    public bool Framed { get; set; } = true;

    /// <summary>小字标签（例：好感）。</summary>
    public string Label
    {
        get => _label.Text ?? "";
        set => _label.Text = value ?? "";
    }

    /// <summary>标签要用字色（默认走 MutedFg；顶栏里这一行通常用 SurfaceFg 更清楚）。</summary>
    public Color? LabelColor { get; set; }

    private string _labelValue = "";
    /// <summary>大号读数；变化时在 HUD 世界播一下「咔哒」。</summary>
    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value), typeof(string), typeof(HudReadout), "",
        propertyChanged: (b, _, v) => ((HudReadout)b).ApplyValue((string?)v));

    public string Value
    {
        get => (string?)GetValue(ValueProperty) ?? "";
        set => SetValue(ValueProperty, value ?? "");
    }

    private void ApplyValue(string? value)
    {
        var text = value ?? "";
        if (_labelValue == text) return;
        _labelValue = text;
        _value.Text = text;
        if (Helpers.HudMotion.IsHud) _ = Helpers.HudMotion.TickAsync(Framed ? (VisualElement)_frame : _value);
    }

    private double _progress = -1;
    /// <summary>进度线 0..1；负数 = 不显示这条线。</summary>
    public static readonly BindableProperty ProgressProperty = BindableProperty.Create(
        nameof(Progress), typeof(double), typeof(HudReadout), -1.0,
        propertyChanged: (b, _, v) => ((HudReadout)b).ApplyProgress((double)v));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    private void ApplyProgress(double value)
    {
        _progress = value;
        _barHost.IsVisible = value >= 0;
        if (value >= 0) RefreshBar(value);
    }

    /// <summary>把零件装进版式：页面在构造时调一次（读 Horizontal / Framed / LabelColor）。</summary>
    public void Compose()
    {
        _stack.Children.Clear();
        if (LabelColor is { } lc) _label.TextColor = lc;
        _label.FontSize = Horizontal ? 8.5 : 10;
        _label.CharacterSpacing = 2.0;
        _frame.Padding = Framed ? new Thickness(9, 5) : Thickness.Zero;

        if (Horizontal)
        {
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 7 };
            row.Add(_label, 0, 0);
            row.Add(_value, 1, 0);
            _value.FontSize = 15;
            _value.VerticalOptions = LayoutOptions.Center;
            _label.VerticalOptions = LayoutOptions.Center;
            _stack.Add(row);
        }
        else
        {
            _stack.Add(_label);
            _stack.Add(_value);
        }
        _stack.Add(_barHost);

        _frame.Content = _stack;
        Content = Framed ? _frame : _stack;
    }

    /// <summary>
    /// 进度只落成双星列权重（0..1），不依赖也不回写任何分配宽度：
    /// 无界测量下星号按 Auto 收缩（期望宽度恒为 0，永不反馈），有界排列时按权重分割。
    /// 列权重变更只触发 Arrange，期望尺寸不变，布局必然收敛。
    /// </summary>
    private void RefreshBar(double progress)
    {
        var p = Math.Clamp(progress, 0, 1);
        _barHost.ColumnDefinitions[0].Width = new GridLength(p, GridUnitType.Star);
        _barHost.ColumnDefinitions[1].Width = new GridLength(1 - p, GridUnitType.Star);
    }
}
