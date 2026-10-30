using Microsoft.Maui.Controls.Shapes;

namespace WarmAsBefore.Controls;

/// <summary>
/// iOS 风滑杆：4 高圆角轨道 + 主色填充 + 22 圆拇指。支持点按跳位与拖动，
/// Minimum / Maximum / Value 与原生 Slider 同名（双向绑定），
/// 设置页里的 77 根滑杆因此能整批换过来而不用改绑定。
///
/// 为什么自己画：原生 Slider 的拇指在 WinUI 上是系统灰，两种明暗档下都和纸白地面黏在一起；
/// 自绘件只用一个 Grid + 两个 BoxView + 一个 Border，没有阴影，拖动时只有拇指位移。
/// </summary>
public sealed class ViewSlider : ContentView
{
    private const double TrackH = 4;
    private const double ThumbD = 22;

    private readonly BoxView _track;
    private readonly BoxView _fill;
    private readonly Border _thumb;
    private bool _dragging;

    public static readonly BindableProperty MinimumProperty = BindableProperty.Create(
        nameof(Minimum), typeof(double), typeof(ViewSlider), 0d,
        propertyChanged: (b, _, _) => ((ViewSlider)b).SyncThumb());

    public static readonly BindableProperty MaximumProperty = BindableProperty.Create(
        nameof(Maximum), typeof(double), typeof(ViewSlider), 1d,
        propertyChanged: (b, _, _) => ((ViewSlider)b).SyncThumb());

    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value), typeof(double), typeof(ViewSlider), 0d, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((ViewSlider)b).SyncThumb());

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>兼容原生 Slider 的显式着色属性（页面里写了就用页面的，否则走语义资源）。</summary>
    public static readonly BindableProperty MinimumTrackColorProperty = BindableProperty.Create(
        nameof(MinimumTrackColor), typeof(Color), typeof(ViewSlider), null,
        propertyChanged: (b, _, n) =>
        {
            var s = (ViewSlider)b;
            if (n is Color c) s._fill.BackgroundColor = c;
            else s._fill.SetDynamicResource(BackgroundColorProperty, "PrimaryAction");
        });

    public static readonly BindableProperty MaximumTrackColorProperty = BindableProperty.Create(
        nameof(MaximumTrackColor), typeof(Color), typeof(ViewSlider), null,
        propertyChanged: (b, _, n) =>
        {
            var s = (ViewSlider)b;
            if (n is Color c) s._track.BackgroundColor = c;
            else s._track.SetDynamicResource(BackgroundColorProperty, "WSep");
        });

    public static readonly BindableProperty ThumbColorProperty = BindableProperty.Create(
        nameof(ThumbColor), typeof(Color), typeof(ViewSlider), null,
        propertyChanged: (b, _, n) =>
        {
            var s = (ViewSlider)b;
            if (n is Color c) s._thumb.BackgroundColor = c;
            else s._thumb.SetDynamicResource(BackgroundColorProperty, "PrimaryAction");
        });

    public Color? MinimumTrackColor
    {
        get => (Color?)GetValue(MinimumTrackColorProperty);
        set => SetValue(MinimumTrackColorProperty, value);
    }

    public Color? MaximumTrackColor
    {
        get => (Color?)GetValue(MaximumTrackColorProperty);
        set => SetValue(MaximumTrackColorProperty, value);
    }

    public Color? ThumbColor
    {
        get => (Color?)GetValue(ThumbColorProperty);
        set => SetValue(ThumbColorProperty, value);
    }

    public ViewSlider()
    {
        HeightRequest = 28;
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Center;

        _track = new BoxView { HeightRequest = TrackH, VerticalOptions = LayoutOptions.Center, CornerRadius = TrackH / 2 };
        _track.SetDynamicResource(BackgroundColorProperty, "WSep");

        _fill = new BoxView { HeightRequest = TrackH, VerticalOptions = LayoutOptions.Center, CornerRadius = TrackH / 2, HorizontalOptions = LayoutOptions.Start };
        _fill.SetDynamicResource(BackgroundColorProperty, "PrimaryAction");

        _thumb = new Border
        {
            WidthRequest = ThumbD,
            HeightRequest = ThumbD,
            StrokeThickness = 0,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = ThumbD / 2 },
            TranslationX = 0,
        };
        _thumb.SetDynamicResource(BackgroundColorProperty, "PrimaryAction");

        var root = new Grid { HeightRequest = 28 };
        root.Add(_track);
        root.Add(_fill);
        root.Add(_thumb);
        Content = root;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, e) => SetFromX(e.GetPosition(this)?.X ?? 0);
        GestureRecognizers.Add(tap);

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _dragging = true;
                    SetFromX(e.TotalX);
                    break;
                case GestureStatus.Running:
                    SetFromX(e.TotalX);
                    break;
                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    _dragging = false;
                    break;
            }
        };
        GestureRecognizers.Add(pan);

        SizeChanged += (_, _) => SyncThumb();

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsEnabled)) Opacity = IsEnabled ? 1 : 0.45;
        };
    }

    private double UsableWidth => Math.Max(1, Width - ThumbD);

    private void SetFromX(double x)
    {
        var ratio = Math.Clamp((x - ThumbD / 2) / UsableWidth, 0, 1);
        var raw = Minimum + (Maximum - Minimum) * ratio;
        // 整档滑杆（帧率 10–60、字号 10–22）要落在整数上，否则读数会一直抖一位小数。
        if (Maximum - Minimum >= 5) raw = Math.Round(raw);
        Value = Math.Clamp(raw, Minimum, Maximum);
    }

    private void SyncThumb()
    {
        if (_dragging && Width <= 0) return;
        var span = Maximum - Minimum;
        var ratio = span <= 0 ? 0 : Math.Clamp((Value - Minimum) / span, 0, 1);
        var x = ratio * UsableWidth;
        _thumb.TranslationX = x;
        _fill.WidthRequest = x + ThumbD / 2;
    }
}
