using Microsoft.Maui.Controls.Shapes;
using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Services;

namespace WarmAsBefore.Controls;

/// <summary>
/// 液态玻璃开关：一根 46×27 的玻璃胶囊 + 一颗 21 的玻璃球。
///
/// 为什么不是原生 Switch：原生件的轨道是一块系统色底，没有背面（不会有背后的糊底被裁着铺进来）、
/// 没有边缘折射、没有贴边内影 —— 和已经换成玻璃的按钮、滑杆放在同一行里，它一眼就是「另一个系统」。
/// 本类与 <see cref="GlassPlate"/> 共用 <see cref="GlassFace"/> 的全部画法，所以两者的材料是同一种。
///
/// 三层（自下而上）：
///   轨道   玻璃胶囊：糊底取样 + 边缘折射 + 镜面弧 + 内影（<see cref="GlassPlate"/>）；关态就是它本身，
///          中间是空的 —— 玻璃空腔，背后的糊底从这里透上来；
///   液柱   开态铺满的强调色液柱：<c>WSliderFillBrush</c>（和滑杆同一根液柱同一条渐变），
///          上面再叠一层 <c>WSliderBulgeBrush</c> 的径向光做内发光；
///   圆钮   一颗小一号的玻璃面（<see cref="GlassPlate"/> 非空心）：球在玻璃空腔里也要读得出来，
///          所以它是「实心玻璃球」而不是滑杆那颗空心环 —— 空心环压在空腔上等于没画。
///
/// 动效：位移与液柱宽度都走 <see cref="MotionTokens.Settle"/>（末段约 5% 过冲再收敛），
/// 时长 <see cref="MotionTokens.SettleMs"/>；按下那一帧圆钮涨到 1.07 再落回，读成「材料被推过去」。
/// 性能模式/系统「减少动画」下 <see cref="MotionTokens.Ms"/> 归零，状态直接落到终值。
///
/// 声音：切换一次 = 一次 <see cref="UiSounds.Friction"/>（Button 档厚度）+ 开 Confirm / 关 Back。
/// 只在**用户按的那一下**响（<see cref="Toggle"/> 里发），绑定初值与 ViewModel 回写不出声。
///
/// 与原生 Switch 的等价关系：IsToggled（TwoWay）/ Toggled（同签名 ToggledEventArgs）/ IsEnabled
/// 同名同义，页面改一个标签名就能换过来；AutomationProperties.Name 挂在控件上即被读屏读到
/// （Windows 上还会同步给原生元素的 UIA 名），点击（含键盘 Space/Enter，Windows）即可切换。
/// </summary>
public sealed class GlassSwitch : ContentView
{
    private const double TrackW = 46;
    private const double TrackH = 27;
    private const double KnobD = 21;
    private const double Pad = 3;
    /// <summary>液柱与玻璃边之间留的那一线：液柱在玻璃**腔里**，不是盖在边上。</summary>
    private const double Inset = 1.5;
    private const double FillH = TrackH - 2 * Inset;
    /// <summary>
    /// 液柱宽度的最小值。**不能写 0**：MAUI 的 WidthRequest 没有「设成 0」这回事，
    /// 0 就是默认值（= 未设置），Border 于是按父布局撑满整条轨道 —— 关态会看到一条铺满的
    /// 强调色实心块，把整块玻璃盖死（用户反馈「开关还是没有玻璃效果」就是这个）。
    /// </summary>
    private const double FillMin = 0.01;
    private const int TravelMs = 210;
    private const int FillMs = 190;

    private readonly GlassPlate _track;
    private readonly Border _fill;
    private readonly Border _fillGlow;
    private readonly GlassPlate _knob;

    /// <summary>首帧之前不做动画：页面加载时绑定把初值推进来，不该看到开关自己滑一下。</summary>
    private bool _primed;

    public static readonly BindableProperty IsToggledProperty = BindableProperty.Create(
        nameof(IsToggled), typeof(bool), typeof(GlassSwitch), false, BindingMode.TwoWay,
        propertyChanged: (b, o, n) =>
        {
            var s = (GlassSwitch)b;
            if (o is bool old && old == (bool)n) return;
            s.Apply((bool)n, animate: s._primed);
            // 与原生 Switch.Toggled 同签名：页面里 Toggled="OnProviderToggled" 不用改。
            s.Toggled?.Invoke(s, new ToggledEventArgs((bool)n));
        });

    /// <summary>与原生 Switch 同名同签名（事件值即新状态）。</summary>
    public event EventHandler<ToggledEventArgs>? Toggled;

    public bool IsToggled
    {
        get => (bool)GetValue(IsToggledProperty);
        set => SetValue(IsToggledProperty, value);
    }

    public GlassSwitch()
    {
        HorizontalOptions = LayoutOptions.Start;
        VerticalOptions = LayoutOptions.Center;
        WidthRequest = TrackW;
        HeightRequest = TrackH;

        // 轨道：玻璃空腔。厚度按「管」那一档（0.55），色调压得比按钮轻一档 ——
        // 它是容器不是主体，开态时大部分被液柱盖住。
        _track = new GlassPlate
        {
            CornerRadius = TrackH / 2,
            Thickness = 0.55,
            TintAmount = 0.05
        };

        // 液柱：强调色，横向渐变与滑杆同一根（左端沉一档、右端亮一档）。
        _fillGlow = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = FillH / 2 },
            InputTransparent = true
        };
        _fillGlow.SetDynamicResource(BackgroundProperty, "WSliderBulgeBrush");

        _fill = new Border
        {
            HeightRequest = FillH,
            WidthRequest = FillMin,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            StrokeThickness = 0,
            Padding = 0,
            StrokeShape = new RoundRectangle { CornerRadius = FillH / 2 },
            Content = _fillGlow,
            TranslationX = Inset
        };
        _fill.SetDynamicResource(BackgroundProperty, "WSliderFillBrush");

        // 圆钮：小一号的玻璃面。厚度给满一档、色调比轨道重 —— 球要比身下的腔体亮，才读得出「浮着」。
        _knob = new GlassPlate
        {
            WidthRequest = KnobD,
            HeightRequest = KnobD,
            CornerRadius = KnobD / 2,
            Thickness = 1.0,
            TintAmount = 0.12,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            TranslationX = Pad
        };
        var glow = new Shadow { Offset = new Point(0, 1), Radius = 9, Opacity = 1 };
        glow.SetDynamicResource(Shadow.BrushProperty, GlassTokens.ButtonGlow);
        _knob.Shadow = glow;

        var root = new Grid { WidthRequest = TrackW, HeightRequest = TrackH };
        root.Add(_track);
        root.Add(_fill);
        root.Add(_knob);
        Content = root;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Toggle();
        GestureRecognizers.Add(tap);

        // 禁用态：原生件会灰掉，自绘件用整体透明度补回同样的语义（与 ViewSwitch 同一条规矩）。
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsEnabled)) Opacity = IsEnabled ? 1 : 0.45;
        };

        Loaded += (_, _) =>
        {
            _primed = true;
            SyncAutomation();
        };
        HandlerChanged += (_, _) => HookNative();
        Unloaded += (_, _) => _native = null;

        Apply(IsToggled, animate: false);
    }

    // ── 状态 ────────────────────────────────────────────────────────────

    /// <summary>一次用户按压：换状态、出声、给圆钮一点「被推过去」的形变。</summary>
    private void Toggle()
    {
        if (!IsEnabled) return;
        IsToggled = !IsToggled;

        // 两声：摩擦是「这块玻璃被推动了」，确认/返回是「开关到了一个状态」。
        // 两者都走 UiSounds 的异步入口（判断 + 入队），UI 线程上不合成任何采样。
        UiSounds.Friction(GlassKind.Button);
        if (IsToggled) UiSounds.Confirm();
        else UiSounds.Back();
    }

    private void Apply(bool on, bool animate)
    {
        var knobX = on ? TrackW - KnobD - Pad : Pad;
        var fillW = on ? TrackW - 2 * Inset : 0;
        var ms = MotionTokens.Ms(TravelMs);

        _track.Signal = on;
        _track.Pressed = false;
        SyncAutomation();

        if (!animate || ms <= 0)
        {
            _knob.AbortAnimation("TranslateTo");
            this.AbortAnimation("FillWidth");
            _knob.TranslationX = knobX;
            _fill.WidthRequest = fillW < 0.5 ? FillMin : fillW;
            _knob.Scale = 1;
            return;
        }

        var spring = new Easing(MotionTokens.Settle);
        _ = _knob.TranslateTo(knobX, 0, (uint)ms, spring);

        // 液柱宽度：灌满/退回都走同一条弹簧，末段那点过冲正好是液体「涌过头再落平」的样子。
        new Animation(v => _fill.WidthRequest = v, _fill.WidthRequest, Math.Max(FillMin, fillW))
            .Commit(this, "FillWidth", 16, (uint)MotionTokens.Ms(FillMs), spring);

        // 圆钮被推的那一下涨一点：过冲只给 7%，再多就成了橡皮球。
        _knob.AbortAnimation("ScaleTo");
        _ = _knob.ScaleTo(1.07, (uint)MotionTokens.Ms(MotionTokens.TapMs), Easing.CubicOut)
            .ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
                _ = _knob.ScaleTo(1.0, (uint)ms, spring)));
    }

    // ── 读屏 / 键盘 ─────────────────────────────────────────────────────

    /// <summary>
    /// 把「这是个开关、现在开着还是关着」交给读屏。AutomationProperties.Name 由页面给（20 处都不写，
    /// 读屏就念控件类型与状态）；这里补上状态后缀，否则 Narrator 只能读到名字，读不出现在是开还是关。
    /// </summary>
    private void SyncAutomation()
    {
        var name = AutomationProperties.GetName(this);
        var state = IsToggled ? "已开启" : "已关闭";
        var text = string.IsNullOrWhiteSpace(name) ? state : $"{name}，{state}";
        SemanticProperties.SetDescription(this, text);
#if WINDOWS
        if (_native is not null)
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_native, text);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(_native, "空格或回车切换");
        }
#endif
    }

    // ── Windows 原生指针 / 键盘 ─────────────────────────────────────────
    // MAUI 的 PointerGestureRecognizer 在 WinUI 上不回调 Entered/Exited（HoverFeel 实测过），
    // 所以悬停与按下只能挂在平台元素上；**切换仍然只走一条路**（TapGestureRecognizer），
    // 键盘是另一条独立入口（元素拿到焦点时 Space/Enter），不会和点按叠成两次切换。

#if WINDOWS
    private Microsoft.UI.Xaml.UIElement? _native;

    private void HookNative()
    {
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement el) return;
        if (ReferenceEquals(_native, el)) return;
        _native = el;

        el.PointerEntered += (_, _) => _track.Hover = IsEnabled;
        el.PointerExited += (_, _) =>
        {
            _track.Hover = false;
            _track.Pressed = false;
        };
        el.PointerPressed += (_, _) => _track.Pressed = IsEnabled;
        el.PointerReleased += (_, _) => _track.Pressed = false;
        el.PointerCaptureLost += (_, _) => _track.Pressed = false;
        el.KeyDown += (_, e) =>
        {
            if (e.Key is not (Windows.System.VirtualKey.Space or Windows.System.VirtualKey.Enter)) return;
            Toggle();
            e.Handled = true;
        };

        SyncAutomation();
    }
#else
    private object? _native;
    private void HookNative() { }
#endif
}
