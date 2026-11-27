using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Helpers;
using WarmAsBefore.Services;

namespace WarmAsBefore.Controls;

/// <summary>
/// 一块**自己画的**液态玻璃面：背景是它背后那一块真实糊底，边缘带透镜折射，左上一条镜面弧，
/// 右下有贴边的内阴影。画法、参数与理由全部在 <see cref="GlassFace"/> 里，本类只负责三件事：
///   1. 量出自己在页面里的位置（父链 Bounds 累加 + 跨过 ScrollView 时减掉滚动偏移）；
///   2. 只在「看得见 + 尺寸有效 + 参数变了」时重画，滚动与尺寸变化做节流；
///   3. 把画好的位图放进一个输入透传的 GraphicsView，内容槽叠在它上面。
///
/// 为什么不是一个 Border + 半透明画刷：那在 WinUI 上永远只是「一块磨砂塑料」——
/// 透明不等于玻璃，玻璃的辨识度来自边缘那一下弯折和那条会走的高光（见 GlassFace 文件头）。
///
/// 用法：给它宽高（或被父布局撑开），需要跟随鼠标/按下状态时把 <see cref="Interactive"/> 打开
/// （或者由 <see cref="GlassButton"/> 直接写 <see cref="Pressed"/> / <see cref="Hover"/>）。
/// </summary>
public class GlassPlate : ContentView
{
    private readonly GraphicsView _canvas;
    private readonly Grid _root;
    private View? _slot;

    /// <summary>上一次重画的时间戳（Environment.TickCount64）。滚动一秒钟能报几十次，全靠它节流。</summary>
    private long _lastPaint;
    private bool _trailing;
    private bool _hooked;
    private bool _pressed;
    private bool _hover;

    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(
        nameof(CornerRadius), typeof(double), typeof(GlassPlate), 8d,
        propertyChanged: (b, _, _) => ((GlassPlate)b).Repaint());

    /// <summary>板厚（0~1.4）：只影响内阴影的浓度与宽度 —— 玻璃是块板，不是一张纸。</summary>
    public static readonly BindableProperty ThicknessProperty = BindableProperty.Create(
        nameof(Thickness), typeof(double), typeof(GlassPlate), 0.7,
        propertyChanged: (b, _, _) => ((GlassPlate)b).Repaint());

    /// <summary>世界色调的浓度（0~0.2）。默认 7%：看得见它属于这个世界，看不出它被涂了色。</summary>
    public static readonly BindableProperty TintAmountProperty = BindableProperty.Create(
        nameof(TintAmount), typeof(double), typeof(GlassPlate), 0.07,
        propertyChanged: (b, _, _) => ((GlassPlate)b).Repaint());

    /// <summary>强调钮：色调换成世界强调色并加重，一屏只该有一颗。</summary>
    public static readonly BindableProperty SignalProperty = BindableProperty.Create(
        nameof(Signal), typeof(bool), typeof(GlassPlate), false,
        propertyChanged: (b, _, _) => ((GlassPlate)b).Repaint());

    /// <summary>空心：只画环带（折射 + 环 + 镜面 + 内影），中间留透 —— 滑杆上的玻璃环用它。</summary>
    public static readonly BindableProperty HollowProperty = BindableProperty.Create(
        nameof(Hollow), typeof(bool), typeof(GlassPlate), false,
        propertyChanged: (b, _, _) => ((GlassPlate)b).Repaint());

    /// <summary>打开后本控件自己不再吃点击（滑杆轨道、卡片底这类纯装饰用）。</summary>
    public static readonly BindableProperty InteractiveProperty = BindableProperty.Create(
        nameof(Interactive), typeof(bool), typeof(GlassPlate), false,
        propertyChanged: (b, o, n) => ((GlassPlate)b).InputTransparent = n is not true);

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public double TintAmount
    {
        get => (double)GetValue(TintAmountProperty);
        set => SetValue(TintAmountProperty, value);
    }

    public bool Signal
    {
        get => (bool)GetValue(SignalProperty);
        set => SetValue(SignalProperty, value);
    }

    public bool Hollow
    {
        get => (bool)GetValue(HollowProperty);
        set => SetValue(HollowProperty, value);
    }

    public bool Interactive
    {
        get => (bool)GetValue(InteractiveProperty);
        set => SetValue(InteractiveProperty, value);
    }

    /// <summary>
    /// 鼠标作为点光源时，光在块内的归一化位置（0~1）。-1 = 指针不在这块玻璃上，
    /// 材质回到固定的左上光。位置被量化到 1/8 网格：玻璃的位图按参数键缓存，
    /// 不量化的话每一次像素级移动都要重新合成一张（等于白干），量化后一块玻璃最多 81 张。
    /// </summary>
    private double _lightX = -1;
    private double _lightY = -1;

    /// <summary>按下：镜面更亮更窄、折射更强、内阴影收紧（参数变化 → 重画一次）。</summary>
    public bool Pressed
    {
        get => _pressed;
        set { if (_pressed == value) return; _pressed = value; Repaint(); }
    }

    /// <summary>悬停：面厚一档、镜面亮一档。鼠标离开时必须收回（见 GlassButton 的平台钩子）。</summary>
    public bool Hover
    {
        get => _hover;
        set { if (_hover == value) return; _hover = value; Repaint(); }
    }

    /// <summary>叠在玻璃上面的内容（文字、图标）。玻璃本身输入透传，所以它挡不住任何点击。</summary>
    public View? Slot
    {
        get => _slot;
        set
        {
            if (ReferenceEquals(_slot, value)) return;
            if (_slot is not null) _root.Remove(_slot);
            _slot = value;
            if (_slot is not null) _root.Add(_slot);
        }
    }

    public GlassPlate()
    {
        _canvas = new GraphicsView { InputTransparent = true };
        _canvas.Drawable = SafeDrawable.Wrap(new PlateDrawable(this), "GlassPlate");

        _root = new Grid();
        _root.Add(_canvas);
        Content = _root;

        // 尺寸一变就得重画（折射的环带宽度、圆角、取样区域全部依赖它）。
        SizeChanged += (_, _) => Repaint();
        Loaded += (_, _) => OnLoaded();
        HandlerChanged += (_, _) => HookPointerLight();
        Unloaded += (_, _) => OnUnloaded();

        // 触点密度：糊底色值是按物理像素画的，1:1 才能既清晰又不重采样。
        Density = DeviceDisplay.MainDisplayInfo.Density;
    }

    /// <summary>物理像素 / 逻辑单位。取不到（或明显不合理）时按 1 处理，画出来只是略糊，不会崩。</summary>
    internal double Density { get; private set; } = 1.0;

    // ── 重画调度 ────────────────────────────────────────────────────────

    /// <summary>
    /// 请求重画。默认节流到约 30fps：滚动与拖窗口会在一秒内报几十次，
    /// 每次都重合成一张位图等于白烧 CPU。节流窗口内的请求并成一次尾随重画。
    /// </summary>
    public void Repaint()
    {
        if (!IsLoaded) return;
        var now = Environment.TickCount64;
        if (now - _lastPaint < 32)
        {
            if (_trailing) return;
            _trailing = true;
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(36), () =>
            {
                _trailing = false;
                if (IsLoaded) PaintNow();
            });
            return;
        }
        PaintNow();
    }

    private void PaintNow()
    {
        if (!IsLoaded) return;
        // 页面切走、控件隐藏时停画：不可见的玻璃没有理由继续合成位图。
        if (!IsVisible || Width <= 0.5 || Height <= 0.5) return;
        _lastPaint = Environment.TickCount64;
        _canvas.Invalidate();
    }

    private void OnLoaded()
    {
        FrostField.Ensure();
        FrostField.Changed -= OnFrostChanged;
        FrostField.Changed += OnFrostChanged;
        Density = DeviceDisplay.MainDisplayInfo.Density is > 0.1 and < 8
            ? DeviceDisplay.MainDisplayInfo.Density
            : 1.0;
        HookScrollers();
        HookTheme();
        HookPointerLight();
        Repaint();
    }

    private void OnUnloaded()
    {
        FrostField.Changed -= OnFrostChanged;
        HookTheme(unhook: true);
        UnhookScrollers();
    }

    /// <summary>
    /// 挂在祖先 ScrollView 上：滚动时子控件的 Bounds 不变（滚的是内容面板），
    /// 但玻璃背后那一块糊底变了 —— 不重画的话，玻璃会一直显示「滚动前它背后的样子」。
    /// </summary>
    private readonly List<ScrollView> _scrollers = new();

    private void HookScrollers()
    {
        UnhookScrollers();
        for (Element? e = Parent; e is not null; e = e.Parent)
        {
            if (e is ScrollView sv)
            {
                sv.Scrolled += OnScrolled;
                _scrollers.Add(sv);
            }
        }
    }

    private void UnhookScrollers()
    {
        foreach (var sv in _scrollers) sv.Scrolled -= OnScrolled;
        _scrollers.Clear();
    }

    private void OnScrolled(object? sender, ScrolledEventArgs e) => Repaint();

    private ThemeManager? _theme;

    private void HookTheme(bool unhook = false)
    {
        var current = ThemeManager.Current;
        if (unhook)
        {
            if (_theme is not null) _theme.Changed -= OnThemeChanged;
            _theme = null;
            return;
        }
        if (ReferenceEquals(_theme, current)) return;
        if (_theme is not null) _theme.Changed -= OnThemeChanged;
        _theme = current;
        if (_theme is not null) _theme.Changed += OnThemeChanged;
    }

    private void OnThemeChanged() => Ui.Post(() => Repaint());

    private void OnFrostChanged() => Ui.Post(() => Repaint());

    // ── 坐标：我在页面里的哪一块 ────────────────────────────────────────

    /// <summary>
    /// 本控件在页面坐标里的位置与页面尺寸。沿父链一路累加 Bounds（平台那套「取屏幕坐标」的 API
    /// 在 MAUI 各版本间换过名字，Bounds 每版都跟着排版走），跨过 ScrollView 时再减掉滚动偏移。
    /// </summary>
    private void MeasureInPage(out double x, out double y, out double pageW, out double pageH)
    {
        double ox = 0, oy = 0;
        Page? page = null;
        for (Element? e = this; e is not null; e = e.Parent)
        {
            if (e is Page p) { page = p; break; }
            if (e is VisualElement v)
            {
                ox += v.X;
                oy += v.Y;
            }
            if (e is ScrollView sv)
            {
                ox -= sv.ScrollX;
                oy -= sv.ScrollY;
            }
        }
        x = ox;
        y = oy;
        pageW = page?.Width ?? 0;
        pageH = page?.Height ?? 0;
        if (!(pageW > 0) || !(pageH > 0))
        {
            // 页面尺寸还没量好（首帧）：按本控件的父级链上能拿到的最外层尺寸兜底。
            pageW = Application.Current?.Windows.FirstOrDefault()?.Width ?? Width;
            pageH = Application.Current?.Windows.FirstOrDefault()?.Height ?? Height;
        }
    }

    internal GlassFaceParams CurrentParams()
    {
        MeasureInPage(out var x, out var y, out var pw, out var ph);
        // 大的面读起来更厚（Apple 的材质重量规则）：同一档厚度，桌面级的大卡片要比赛道级的小圆点
        // 更厚一档，否则小控件的边看起来比整张卡还沉，眼睛立刻觉得假。
        // 尺度按面积开方算，夹在 0.75~1.6 倍之间，只影响内影、柱面、侧壁与镜面辉光的铺开范围。
        var sizeK = Math.Clamp(Math.Sqrt(Math.Max(1.0, Width) * Math.Max(1.0, Height)) / 260.0, 0.75, 1.6);
        return new GlassFaceParams(
            Width, Height, Density, CornerRadius, Thickness * sizeK, _pressed, _hover, Signal, Hollow,
            x, y, pw, ph, TintAmount, _lightX, _lightY);
    }

    /// <summary>
    /// 问一次这块玻璃的位图。缓存命中直接拿到；未命中返回**上一次那张**（尺寸没变时照旧画），
    /// 同时后台开始合成，好了一帧之后再来要。所以玻璃永远不闪白：宁可慢半帧换新衣，也不要一帧空白。
    ///
    /// 尺寸变了（窗口缩放）就不再复用旧的：拉伸一张旧尺寸的位图会把折射与棱拉歪。
    /// </summary>
    internal Microsoft.Maui.Graphics.IImage? Face()
    {
        var image = GlassFace.Get(CurrentParams(), RequestRepaint);
        if (image is not null)
        {
            _last = image;
            _lastW = Width;
            _lastH = Height;
            return image;
        }
        return Math.Abs(_lastW - Width) < 2 && Math.Abs(_lastH - Height) < 2 ? _last : null;
    }

    private Microsoft.Maui.Graphics.IImage? _last;
    private double _lastW, _lastH;
    private bool _repaintQueued;

    /// <summary>后台合成完成：合并成一次重画（同一帧里堆十次 Invalidate 等于画十次）。</summary>
    private void RequestRepaint()
    {
        if (_repaintQueued) return;
        _repaintQueued = true;
        Dispatcher.Dispatch(() =>
        {
            _repaintQueued = false;
            if (IsLoaded) _canvas.Invalidate();
        });
    }

    /// <summary>
    // ── 鼠标 = 点光源 ────────────────────────────────────────────────────

    private object? _lightHost;

    /// <summary>
    /// 挂上平台指针事件：鼠标在这块玻璃上移动时，把光的位置交给材质。
    ///
    /// MAUI 的 PointerGestureRecognizer 在 WinUI 上不回调 Moved（HoverFeel 实测过），
    /// 所以只能挂平台元素。位置量化到 1/8 网格再比较，同一格内移动不触发重画 ——
    /// 否则每一像素都会让这块玻璃重新合成一张位图。
    /// </summary>
    private void HookPointerLight()
    {
#if WINDOWS
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement el) return;
        if (ReferenceEquals(_lightHost, el)) return;
        _lightHost = el;
        el.PointerMoved += (_, e) =>
        {
            var w = Math.Max(1.0, Width);
            var h = Math.Max(1.0, Height);
            var pt = e.GetCurrentPoint(el).Position;
            var nx = Math.Clamp(pt.X / w, 0, 1);
            var ny = Math.Clamp(pt.Y / h, 0, 1);
            // 1/8 网格 + 中心吸附：光停在格子中心，免得高光在网格线上跳。
            var qx = Math.Round(nx * 8) / 8;
            var qy = Math.Round(ny * 8) / 8;
            if (Math.Abs(qx - _lightX) < 1e-6 && Math.Abs(qy - _lightY) < 1e-6) return;
            _lightX = qx;
            _lightY = qy;
            Repaint();
        };
        el.PointerExited += (_, _) =>
        {
            if (_lightX < 0) return;
            _lightX = -1;
            _lightY = -1;
            Repaint();
        };
#endif
    }

    /// <summary>GraphicsView 的画笔：只做一次 DrawImage。每帧向 <see cref="GlassFace"/> 要位图 ——
    /// 它内部按参数键缓存，命中时只是一次字典查找；参数真的变了才会重新合成。
    /// </summary>
    private sealed class PlateDrawable : IDrawable
    {
        private readonly GlassPlate _owner;

        public PlateDrawable(GlassPlate owner) => _owner = owner;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var image = _owner.Face();
            if (image is null)
            {
                // 连位图都没画出来（尺寸异常或合成失败）：一块极淡的白，至少读得出「这里有个控件」。
                var radius = (float)(_owner.CornerRadius * _owner.Density);
                canvas.FillColor = Color.FromRgba(255, 255, 255, 28);
                canvas.FillRoundedRectangle(dirtyRect.X, dirtyRect.Y, dirtyRect.Width, dirtyRect.Height,
                    Math.Min(radius, Math.Min(dirtyRect.Width, dirtyRect.Height) * 0.5f));
                return;
            }
            canvas.DrawImage(image, dirtyRect.X, dirtyRect.Y, dirtyRect.Width, dirtyRect.Height);
        }
    }
}
