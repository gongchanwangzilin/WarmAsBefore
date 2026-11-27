using Microsoft.Maui.Controls.Shapes;
using WarmAsBefore.DesignSystem.Theme;

namespace WarmAsBefore.Controls;

/// <summary>
/// iOS 26 液态玻璃滑杆：一根 12 高的玻璃管 + 一条液柱 + 一颗 28 的玻璃环滑钮。
///
/// 几何（全部是本类里的常量，页面不覆写）：
///   轨道   高 12，两端半圆（胶囊）；**玻璃是真的在画**（<see cref="GlassPlate"/>）：
///          背后那一块糊底被裁着铺进来，边缘有透镜折射，左上一条镜面弧，下沿有贴边内影；
///   液柱   已填充段，横向渐变：左端沉一档、贴滑钮那头提亮 —— 液面被滑钮挤高了一档；
///   滑钮   直径 28 的**空心玻璃环**：只画环带（折射 + 环 + 镜面 + 内影），环心是透的，
///          液柱从环里穿过去看得见；
///   鼓包   环底下一团 32×16 的径向亮斑，轨道在滑钮处被顶起来的那一小块；
///   张力   环心那道 2px 的白线 —— 液面与玻璃「贴住了」的唯一证据。
///
/// 为什么轨道与滑钮也要走 GlassPlate：上一版它们是 Border + 渐变画刷，读起来是「一根涂了色的管子」。
/// 玻璃与涂色的差别不在颜色，在**边缘的内容是错的**（被折射挤过）以及那条会走的高光。
/// 这两样只有真画才给得出来，所以滑杆和按钮现在共用同一套绘制（GlassFace）。
///
/// 兼容口与以前一字不差：Value / Minimum / Maximum / ValueChanged / MinimumTrackColor /
/// MaximumTrackColor / ThumbColor。显式给了 MaximumTrackColor 或 ThumbColor 的页面
/// （AvatarCropPage、MapCreatePage）会自动退回原来那条实色轨道 / 实色环 —— 它们要的是「这一颗钮是红的」，
/// 不是玻璃。
///
/// 为什么自己画：原生 Slider 的拇指在 WinUI 上是系统灰，两种明暗档下都和纸白地面黏在一起。
/// </summary>
public sealed class ViewSlider : ContentView
{
    private const double TrackH = 12;
    private const double ThumbD = 28;
    private const double BulgeW = 32;
    private const double BulgeH = 16;
    private const double TensionW = 2;
    private const double RowH = 32;

    private readonly Border _track;
    private readonly GlassPlate _trackGlass;
    private readonly Border _fill;
    private readonly Border _bulge;
    private readonly BoxView _tension;
    private readonly Border _thumb;
    private readonly GlassPlate _thumbGlass;
    private bool _dragging;
    /// <summary>按下那一刻的进度（0..1）。拖动按「从这里起算的差量」走 —— 见 OnPanUpdated。</summary>
    private double _dragFrom;

    public static readonly BindableProperty MinimumProperty = BindableProperty.Create(
        nameof(Minimum), typeof(double), typeof(ViewSlider), 0d,
        propertyChanged: (b, _, _) => ((ViewSlider)b).SyncThumb());

    public static readonly BindableProperty MaximumProperty = BindableProperty.Create(
        nameof(Maximum), typeof(double), typeof(ViewSlider), 1d,
        propertyChanged: (b, _, _) => ((ViewSlider)b).SyncThumb());

    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value), typeof(double), typeof(ViewSlider), 0d, BindingMode.TwoWay,
        propertyChanged: (b, o, n) =>
        {
            var s = (ViewSlider)b;
            s.SyncThumb();
            // 与原生 Slider 同名同义：程序化赋值也会响，页面靠自己的重入标记挡回环。
            if (o is double from && n is double to && Math.Abs(from - to) > 1e-9)
                s.ValueChanged?.Invoke(s, new ValueChangedEventArgs(from, to));
        });

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

    /// <summary>与原生 Slider 同名的值变化事件：页面里 <c>ValueChanged="OnPanKnobChanged"</c> 不用改。</summary>
    public event EventHandler<ValueChangedEventArgs>? ValueChanged;

    /// <summary>兼容原生 Slider 的显式着色属性（页面里写了就用页面的，否则走语义令牌）。</summary>
    public static readonly BindableProperty MinimumTrackColorProperty = BindableProperty.Create(
        nameof(MinimumTrackColor), typeof(Color), typeof(ViewSlider), null,
        propertyChanged: (b, _, n) =>
        {
            var s = (ViewSlider)b;
            if (n is Color c) s._fill.Background = new SolidColorBrush(c);
            else s._fill.SetDynamicResource(VisualElement.BackgroundProperty, "WSliderFillBrush");
        });

    public static readonly BindableProperty MaximumTrackColorProperty = BindableProperty.Create(
        nameof(MaximumTrackColor), typeof(Color), typeof(ViewSlider), null,
        propertyChanged: (b, _, n) =>
        {
            var s = (ViewSlider)b;
            if (n is Color c)
            {
                s._track.Background = new SolidColorBrush(c);
                s._track.IsVisible = true;
                s._trackGlass.IsVisible = false;
            }
            else
            {
                s._track.SetDynamicResource(VisualElement.BackgroundProperty, "WSliderTrackBrush");
                s._track.IsVisible = false;
                s._trackGlass.IsVisible = true;
            }
        });

    public static readonly BindableProperty ThumbColorProperty = BindableProperty.Create(
        nameof(ThumbColor), typeof(Color), typeof(ViewSlider), null,
        propertyChanged: (b, _, n) =>
        {
            var s = (ViewSlider)b;
            if (n is Color c)
            {
                s._thumb.Stroke = new SolidColorBrush(c);
                s._thumb.StrokeThickness = 2.8;
                s._thumb.IsVisible = true;
                s._thumbGlass.IsVisible = false;
            }
            else
            {
                s._thumb.SetDynamicResource(Border.StrokeProperty, "WSliderRingBrush");
                s._thumb.IsVisible = false;
                s._thumbGlass.IsVisible = true;
            }
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
        HeightRequest = RowH;
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Center;

        // 玻璃管：真画出来的那一根。只在页面显式给了 MaximumTrackColor 时让位给实色轨道。
        _trackGlass = new GlassPlate
        {
            HeightRequest = TrackH,
            CornerRadius = TrackH / 2,
            Thickness = 0.55,
            TintAmount = 0.05,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill
        };

        // 实色轨道：兼容口（MaximumTrackColor）以及没有糊底时的退路，默认不显示。
        _track = Rounded(TrackH);
        _track.IsVisible = false;
        _track.HorizontalOptions = LayoutOptions.Fill;
        _track.SetDynamicResource(VisualElement.BackgroundProperty, "WSliderTrackBrush");

        _fill = Rounded(TrackH);
        _fill.HorizontalOptions = LayoutOptions.Start;
        _fill.SetDynamicResource(VisualElement.BackgroundProperty, "WSliderFillBrush");

        _bulge = Rounded(BulgeH);
        _bulge.WidthRequest = BulgeW;
        _bulge.HorizontalOptions = LayoutOptions.Start;
        _bulge.SetDynamicResource(VisualElement.BackgroundProperty, "WSliderBulgeBrush");

        _tension = new BoxView
        {
            WidthRequest = TensionW,
            HeightRequest = TrackH,
            CornerRadius = TensionW / 2,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
        };
        _tension.SetDynamicResource(BoxView.ColorProperty, "WSliderTension");

        // 玻璃环滑钮：空心（只画环带），环心的液柱从它里面穿过去。
        _thumbGlass = new GlassPlate
        {
            WidthRequest = ThumbD,
            HeightRequest = ThumbD,
            CornerRadius = ThumbD / 2,
            Hollow = true,
            Thickness = 0.9,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start
        };
        var glow = new Shadow { Offset = new Point(0, 0), Radius = 12, Opacity = 1 };
        glow.SetDynamicResource(Shadow.BrushProperty, "WSliderGlowBrush");
        _thumbGlass.Shadow = glow;

        // 实色环：兼容口（ThumbColor），默认不显示。
        _thumb = new Border
        {
            WidthRequest = ThumbD,
            HeightRequest = ThumbD,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = ThumbD / 2 },
            BackgroundColor = Colors.Transparent,
            InputTransparent = true,
            IsVisible = false
        };
        _thumb.SetDynamicResource(Border.StrokeProperty, "WSliderRingBrush");
        _thumb.SetDynamicResource(Border.StrokeThicknessProperty, "WSliderRingWidth");

        var root = new Grid { HeightRequest = RowH };
        root.Add(_trackGlass);
        root.Add(_track);
        root.Add(_fill);
        root.Add(_bulge);
        root.Add(_tension);
        root.Add(_thumbGlass);
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
                    // 从这里起算差量。TotalX 在 Started 时恒为 0 —— 上一版把它当**绝对坐标**喂给
                    // SetFromX，于是每次按下滑钮都会先跳到最小值（用户报的「一碰就归零」）。
                    _dragging = true;
                    _dragFrom = RatioOf(Value);
                    _thumbGlass.Pressed = true;
                    break;
                case GestureStatus.Running:
                    if (!_dragging) break;
                    SetRatio(_dragFrom + e.TotalX / UsableWidth);
                    break;
                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    _dragging = false;
                    _thumbGlass.Pressed = false;
                    SyncThumb();
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

    /// <summary>胶囊坯子：高度给定，水平方向由调用方决定。</summary>
    private static Border Rounded(double h) => new()
    {
        HeightRequest = h,
        VerticalOptions = LayoutOptions.Center,
        StrokeThickness = 0,
        StrokeShape = new RoundRectangle { CornerRadius = h / 2 },
        InputTransparent = true,
    };

    private double UsableWidth => Math.Max(1, Width - ThumbD);

    private double RatioOf(double value)
    {
        var span = Maximum - Minimum;
        return span <= 0 ? 0 : Math.Clamp((value - Minimum) / span, 0, 1);
    }

    /// <summary>点按跳位：x 是控件内的绝对坐标，先减掉半个滑钮再换算成进度。</summary>
    private void SetFromX(double x) => SetRatio((x - ThumbD / 2) / UsableWidth);

    private void SetRatio(double ratio)
    {
        var r = Math.Clamp(double.IsFinite(ratio) ? ratio : 0, 0, 1);
        var raw = Minimum + (Maximum - Minimum) * r;
        // 整档滑杆（帧率 10–60、字号 10–22）要落在整数上，否则读数会一直抖一位小数。
        if (Maximum - Minimum >= 5) raw = Math.Round(raw);
        Value = Math.Clamp(raw, Minimum, Maximum);
    }

    private void SyncThumb()
    {
        if (Width <= 0) return;
        var x = RatioOf(Value) * UsableWidth;
        var usable = Math.Max(0, Width);
        var thumbX = Math.Min(x, Math.Max(0, usable - ThumbD));
        _thumb.TranslationX = thumbX;
        _thumbGlass.TranslationX = thumbX;
        // 液柱到滑钮中心为止：环心是透的，液面正好停在环里，像被那颗环盛住。
        _fill.WidthRequest = Math.Min(usable, x + ThumbD / 2);
        // 鼓包比滑钮宽一圈（32 vs 28），两端要夹回轨道内：否则在 0 与满值时会有 2px 探出控件外。
        _bulge.TranslationX = Math.Clamp(x + (ThumbD - BulgeW) / 2, 0, Math.Max(0, usable - BulgeW));
        _tension.TranslationX = Math.Min(usable - TensionW, x + ThumbD / 2 - TensionW / 2);
        // 滑钮挪了位置 = 它背后的糊底换了：让玻璃重新取一次样（内部有节流，拖动时约 30fps）。
        _thumbGlass.Repaint();
    }
}
