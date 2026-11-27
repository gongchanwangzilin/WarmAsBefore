using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Helpers;
using WarmAsBefore.Services;

namespace WarmAsBefore.Controls;

/// <summary>
/// 液态玻璃按钮：一块 <see cref="GlassPlate"/> + 居中的字，按压时镜面变亮变窄、整体缩到 0.97、
/// 内阴影收紧，悬停时面厚一档。
///
/// 为什么不能直接用 MAUI 的 Button：Button 只有一条均匀的 BorderColor 描边和一块纯色/渐变底，
/// 画不出「边缘折射 + 走动的镜面弧 + 贴边内影」这三层 —— 那三层才是 iOS 26 那颗钮不像塑料的原因。
/// 代价是本类必须自己接管输入：WinUI 上 PointerGestureRecognizer 的 Entered/Exited 不回调
/// （见 HoverFeel 的实测记录），所以悬停/按下走平台元素的原生指针事件，点击走 TapGestureRecognizer。
///
/// 与旧按钮的等价关系：
///   · Text / Command / CommandParameter / Clicked 同名同义，页面改一行标签就能换过来；
///   · IsEnabled 仍由页面绑定（忙碌态那颗 InverseBool 转换器照旧工作），本类只负责按它变灰、不吃点击；
///   · Command 的 CanExecute 会额外地参与「能不能点」，但不回写 IsEnabled —— 否则会和页面上
///     显式绑定的 IsEnabled 互相顶，最后停在谁也不知道哪一档上。
/// </summary>
public sealed class GlassButton : ContentView
{
    private readonly GlassPlate _plate;
    private readonly Label _label;
    private bool _pressed;
    private long _pressAt;

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(GlassButton), "",
        propertyChanged: (b, _, n) => ((GlassButton)b)._label.Text = n as string ?? "");

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(System.Windows.Input.ICommand), typeof(GlassButton), null,
        propertyChanged: (b, o, n) => ((GlassButton)b).OnCommandChanged(o as System.Windows.Input.ICommand, n as System.Windows.Input.ICommand));

    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
        nameof(CommandParameter), typeof(object), typeof(GlassButton), null);

    /// <summary>强调钮：玻璃加一层世界强调色，字用 SignalFg —— 一屏只该有一颗。</summary>
    public static readonly BindableProperty SignalProperty = BindableProperty.Create(
        nameof(Signal), typeof(bool), typeof(GlassButton), false,
        propertyChanged: (b, _, n) => ((GlassButton)b).ApplyTone(n is true));

    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize), typeof(double), typeof(GlassButton), 14.5,
        propertyChanged: (b, _, n) => ((GlassButton)b)._label.FontSize = n is double d ? d : 14.5);

    public static readonly BindableProperty FontAttributesProperty = BindableProperty.Create(
        nameof(FontAttributes), typeof(FontAttributes), typeof(GlassButton), FontAttributes.Bold,
        propertyChanged: (b, _, n) => ((GlassButton)b)._label.FontAttributes = n is FontAttributes f ? f : FontAttributes.None);

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(GlassButton), null,
        propertyChanged: (b, _, _) => ((GlassButton)b).ApplyTone(((GlassButton)b).Signal));

    public static readonly BindableProperty PaddingProperty = BindableProperty.Create(
        nameof(Padding), typeof(Thickness), typeof(GlassButton), new Thickness(18, 0),
        propertyChanged: (b, _, n) => ((GlassButton)b)._label.Margin = n is Thickness t ? t : default);

    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(
        nameof(CornerRadius), typeof(double), typeof(GlassButton), 8d,
        propertyChanged: (b, _, n) => ((GlassButton)b)._plate.CornerRadius = n is double d ? d : 8);

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public System.Windows.Input.ICommand? Command
    {
        get => (System.Windows.Input.ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public bool Signal
    {
        get => (bool)GetValue(SignalProperty);
        set => SetValue(SignalProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontAttributes FontAttributes
    {
        get => (FontAttributes)GetValue(FontAttributesProperty);
        set => SetValue(FontAttributesProperty, value);
    }

    public Color? TextColor
    {
        get => (Color?)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>与 Button.Clicked 同名同义：页面里 <c>Clicked="OnPetSettingsTapped"</c> 不用改。</summary>
    public event EventHandler? Clicked;

    public GlassButton()
    {
        _label = new Label
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            FontAttributes = FontAttributes.Bold,
            FontSize = 14.5,
            Margin = new Thickness(18, 0),
            InputTransparent = true
        };

        _plate = new GlassPlate
        {
            Thickness = 0.75,
            TintAmount = 0.07,
            Slot = _label,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        // 外发光：与旧按钮那颗 0,0/22 同一个来源（GlassButtonGlow 令牌），轻量模式下令牌自己发全透明。
        var glow = new Shadow { Offset = new Point(0, 0), Radius = 22, Opacity = 1 };
        glow.SetDynamicResource(Shadow.BrushProperty, GlassTokens.ButtonGlow);
        _plate.Shadow = glow;

        Content = _plate;
        ApplyTone(false);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => OnTap();
        GestureRecognizers.Add(tap);

        HandlerChanged += (_, _) => HookNative();
        Loaded += (_, _) => HookNative();
        Unloaded += (_, _) =>
        {
            UnhookNative();
            Settle();
        };

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsEnabled)) ApplyEnabled();
        };
    }

    private void ApplyTone(bool signal)
    {
        _plate.Signal = signal;
        _plate.TintAmount = signal ? 0.11 : 0.07;
        if (TextColor is Color c)
        {
            _label.TextColor = c;
            return;
        }
        _label.SetDynamicResource(Label.TextColorProperty, signal ? "SignalFg" : "WarmDark");
    }

    private void ApplyEnabled()
    {
        Opacity = IsEnabled ? 1 : 0.4;
        if (!IsEnabled) Settle();
    }

    private void OnCommandChanged(System.Windows.Input.ICommand? old, System.Windows.Input.ICommand? now)
    {
        if (old is not null) old.CanExecuteChanged -= OnCanExecuteChanged;
        if (now is not null) now.CanExecuteChanged += OnCanExecuteChanged;
    }

    private void OnCanExecuteChanged(object? sender, EventArgs e) => ApplyEnabled();

    private bool CanFire =>
        IsEnabled && (Command is null || Command.CanExecute(CommandParameter));

    // ── 点击 ────────────────────────────────────────────────────────────

    private void OnTap()
    {
        if (!CanFire) return;
        // 平台钩子按下时已经响过（Windows）：300ms 内的这一下属于同一次按压，不重复出声。
        if (Environment.TickCount64 - _pressAt > 300) UiSounds.Tap();
        _pressAt = 0;
        Fire();
    }

    private void Fire()
    {
        if (!CanFire) return;
        try
        {
            Command?.Execute(CommandParameter);
        }
        catch (Exception ex)
        {
            App.WriteLog("GlassButton.Command -> " + ex);
        }
        Clicked?.Invoke(this, EventArgs.Empty);
    }

    // ── 按压 / 悬停 ─────────────────────────────────────────────────────

    private void SetPressed(bool on)
    {
        if (_pressed == on) return;
        _pressed = on;
        _plate.Pressed = on;
        var ms = on ? MotionTokens.Ms(MotionTokens.TapMs) : MotionTokens.Ms(MotionTokens.SettleMs);
        if (ms <= 0)
        {
            Scale = on ? MotionTokens.PressScale : 1.0;
            TranslationY = on ? MotionTokens.Press : 0;
            return;
        }
        // 按压不再各起各的时钟：走 Scene 的全局按压通道，与背景的呼吸**同一拍**推进。
        // 按下用「快说到位」的曲线（QuadOut，前段快、后段收），松手才走弹簧（末段过冲）；
        // 按下也用弹簧的话，前 70ms 里那块玻璃会先往上抬一下再被压下去 —— 手感是黏的。
        // 截停也在这里：连点两下时第二次从**当前位移**接着算，不会先回零再压下去。
        if (on)
            Scene.Play(Scene.Press, ms, ApplyPress, ease: MotionTokens.OutCubic);
        else
            Scene.Release(Scene.Press, ms, ApplyPress, ease: MotionTokens.Settle);
    }

    /// <summary>
    /// 按压进度 → 这一颗的位移与缩放。往右下压 2px、缩 4%：位移方向跟着页面流程走，
    /// 与背景一起被推进，所以「按下去」读成材料被推了一下，而不是按钮自己在抖。
    /// </summary>
    private void ApplyPress(double p)
    {
        TranslationX = 3.0 * p;
        TranslationY = MotionTokens.Press * p;
        Scale = 1.0 + (MotionTokens.PressScale - 1.0) * p;
    }

    private void SetHover(bool on) => _plate.Hover = on && IsEnabled;

    /// <summary>抬起没到、页面先走了：把按着的那一笔落回原位（与 PressFeel 同一条规矩）。</summary>
    private void Settle()
    {
        _pressed = false;
        if (Scale != 1) Scale = 1;
        if (TranslationY != 0) TranslationY = 0;
        if (TranslationX != 0) TranslationX = 0;
        _plate.Pressed = false;
        _plate.Hover = false;
    }

    // ── Windows 原生指针 ────────────────────────────────────────────────
    // MAUI 的 PointerGestureRecognizer 在 WinUI 上不回调 Entered/Exited（HoverFeel 实测过），
    // 所以悬停与按下只能挂在平台元素上；**点击仍然只走一条路**（TapGestureRecognizer），
    // 两套都报点击的话一次按压会响两遍。

#if WINDOWS
    private Microsoft.UI.Xaml.UIElement? _native;

    private void HookNative()
    {
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement el) return;
        if (ReferenceEquals(_native, el)) return;
        _native = el;

        // 只挂一次；元素随 Handler 一起被回收，摘不摘都不影响别的控件。
        el.PointerEntered += (_, _) => SetHover(true);
        el.PointerExited += (_, _) => { SetHover(false); SetPressed(false); };
        el.PointerPressed += (_, e) =>
        {
            if (!CanFire) return;
            UiSounds.Tap();
            _pressAt = Environment.TickCount64;
            SetPressed(true);
            e.Handled = false;
        };
        el.PointerReleased += (_, _) => SetPressed(false);
        el.PointerCaptureLost += (_, _) => SetPressed(false);
    }

    private void UnhookNative()
    {
        // 平台元素会被 Handler 一起丢掉，这里不摘事件（匿名 lambda 也摘不掉）；
        // 只清状态，让重新 Loaded 时对着新元素再挂一遍。
        _native = null;
    }
#else
    private void HookNative() { }
    private void UnhookNative() { }
#endif
}
