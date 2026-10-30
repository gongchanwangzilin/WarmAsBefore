using Microsoft.Maui.Controls.Shapes;

namespace WarmAsBefore.Controls;

/// <summary>
/// iOS 风开关：51×31 胶囊轨道 + 27 圆拇指。点按切换，IsToggled 双向绑定，
/// 用法与原生 Switch 完全一致（IsToggled / IsEnabled / VerticalOptions 同名），
/// 换掉原生灰皮是为了让设置行在两种明暗档里都读得出来——原生件的默认配色在深色档下会发灰。
/// 不含阴影：每个合成层在 WinUI 上都要单独算，设置页一次几十个开关，省下来的正是首屏时间。
/// </summary>
public sealed class ViewSwitch : ContentView
{
    private const double TrackW = 51;
    private const double TrackH = 31;
    private const double ThumbD = 27;
    private const double Pad = 2;

    private readonly Border _track;
    private readonly Border _thumb;

    public static readonly BindableProperty IsToggledProperty = BindableProperty.Create(
        nameof(IsToggled), typeof(bool), typeof(ViewSwitch), false, BindingMode.TwoWay,
        propertyChanged: (b, o, n) =>
        {
            var s = (ViewSwitch)b;
            s.Apply((bool)n, animate: o is bool old && old != (bool)n);
            // 与原生 Switch.Toggled 同签名：老代码里挂的事件不用改。
            if (o is bool prev && prev != (bool)n)
            {
                s.Toggled?.Invoke(s, new ToggledEventArgs((bool)n));
            }
        });

    /// <summary>与原生 Switch 同名同签名（事件值即新状态）。</summary>
    public event EventHandler<ToggledEventArgs>? Toggled;

    /// <summary>兼容原生 Switch 的显式着色属性（页面里写了就用页面的，否则走语义资源）。</summary>
    public static readonly BindableProperty OnColorProperty = BindableProperty.Create(
        nameof(OnColor), typeof(Color), typeof(ViewSwitch), null,
        propertyChanged: (b, _, _) => ((ViewSwitch)b).Apply(((ViewSwitch)b).IsToggled, animate: false));

    public static readonly BindableProperty ThumbColorProperty = BindableProperty.Create(
        nameof(ThumbColor), typeof(Color), typeof(ViewSwitch), null,
        propertyChanged: (b, _, n) =>
        {
            var s = (ViewSwitch)b;
            if (n is Color c) s._thumb.BackgroundColor = c;
            else s._thumb.SetDynamicResource(BackgroundColorProperty, "PageBg");
        });

    public Color? OnColor
    {
        get => (Color?)GetValue(OnColorProperty);
        set => SetValue(OnColorProperty, value);
    }

    public Color? ThumbColor
    {
        get => (Color?)GetValue(ThumbColorProperty);
        set => SetValue(ThumbColorProperty, value);
    }

    public bool IsToggled
    {
        get => (bool)GetValue(IsToggledProperty);
        set => SetValue(IsToggledProperty, value);
    }

    public ViewSwitch()
    {
        HorizontalOptions = LayoutOptions.Start;
        VerticalOptions = LayoutOptions.Center;
        WidthRequest = TrackW;
        HeightRequest = TrackH;

        _track = new Border
        {
            WidthRequest = TrackW,
            HeightRequest = TrackH,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = TrackH / 2 },
        };

        _thumb = new Border
        {
            WidthRequest = ThumbD,
            HeightRequest = ThumbD,
            StrokeThickness = 0,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = ThumbD / 2 },
            TranslationX = Pad,
        };
        // 拇指用亮色：深色档里它必须是「白点」，不能被世界压成灰。
        _thumb.SetDynamicResource(BackgroundColorProperty, "PageBg");

        var root = new Grid { WidthRequest = TrackW, HeightRequest = TrackH };
        root.Add(_track);
        root.Add(_thumb);
        Content = root;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => IsToggled = !IsToggled;
        GestureRecognizers.Add(tap);

        // 禁用态：原生件会灰掉，自绘件用整体透明度补回同样的语义。
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsEnabled)) Opacity = IsEnabled ? 1 : 0.45;
        };

        Apply(IsToggled, animate: false);
    }

    private void Apply(bool on, bool animate)
    {
        if (on && OnColor is Color onColor) _track.BackgroundColor = onColor;
        else _track.SetDynamicResource(BackgroundColorProperty, on ? "PrimaryAction" : "WSep");
        var x = on ? TrackW - ThumbD - Pad : Pad;
        if (!animate) { _thumb.TranslationX = x; return; }
        _ = _thumb.TranslateTo(x, 0, 160, Easing.CubicOut);
    }
}
