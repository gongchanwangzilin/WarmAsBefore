using System.Runtime.CompilerServices;
using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Services;

namespace WarmAsBefore.Helpers;

/// <summary>
/// 「按下去是沉的」：给按钮补上真正会被看见的按压反馈。
///
/// 为什么需要代码：MAUI 的 VisualStateManager 只有 Setter，没有「过渡」这个概念——
/// XAML 里写 Pressed 把透明度掉到 0.7，效果是啪一下跳变，既不像被按住，也像按钮在消失。
/// 所以分工是：**样式负责每个状态长什么样（颜色、描边），本类负责状态之间怎么走**。
///
/// 位移（TranslationY）与缩放（Scale）都由驱动逐帧赋值，终点永远是基值。三条都是踩出来的：
///   · **复制到 VisualState 的 Setter 里的 Scale 会卡住**：带导航的按钮按下即切页，抬起收不到，
///     0.97 就长在按钮脸上了。放在驱动里就没这个问题——换页、卸载、禁用都会主动归位。
///   · 不碰 Opacity——透明度还被进场动画与禁用态写着，多处同时赋值必然互相顶掉：
///     元素会停在某个半程值上，看起来就是「按键莫名其妙消失了」。
///     颜色也不在这里动：按下不换色，只下沉（用户要的是「变深归悬停、下沉归按下」）。
///   · 不记基准值——按下就是 0→SinkPx、1→PressScale，抬起回基准。曾经读过的「进来时的值」
///     很可能正是别处的动画中途值，把它当静止基准就等于把一次性抖动永久化。
///
/// 换页与卸载时要主动落回 0：按下那一刻页面就切走的话，抬起事件不再送达，
/// 不补这一下，按钮会永远比原位低 2.4px —— 和颜色一样，是「点过一次就再没还原」。
/// 只补「确实按过」的元素，免得把新页正在跑的进场位移一起掐掉。
///
/// 悬停不在这里做：WinUI 上实测 MAUI 不进 PointerOver 态，PointerGestureRecognizer 的
/// PointerEntered/Exited 也不回调（把本类整个去掉再测，按钮照样纹丝不动）。挂上去换不来一个像素，
/// 代价却是每个按钮多一套输入处理器——鼠标每动一下都要过一遍，正是「不华丽却越用越钝」的来源。
///
/// <see cref="LiquidProperty"/> 是给「液态玻璃」那一档按钮用的：除了下沉，还缩到 0.97、
/// 并把面换成按下那一面（高光收窄、接触阴影压紧）—— 胶质被压陷的整个过程由三个量同时说，
/// 只沉几个像素读起来是「轻微下移」，不是「材料被压住」。松手沿 <see cref="MotionTokens.Spring"/>
/// 弹回，曲线自带约 5% 过冲，所以回弹是「弹回来」而不是「滑回来」。
///
/// 用法：在按钮样式里加一行 &lt;Setter Property="motion:PressFeel.Enabled" Value="True" /&gt;，
/// 要胶质按压的再加 &lt;Setter Property="motion:PressFeel.Liquid" Value="True" /&gt;。
/// </summary>
public static class PressFeel
{
    public static readonly BindableProperty EnabledProperty = BindableProperty.CreateAttached(
        "Enabled", typeof(bool), typeof(PressFeel), false, propertyChanged: OnEnabledChanged);

    public static bool GetEnabled(VisualElement element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(VisualElement element, bool value) => element.SetValue(EnabledProperty, value);

    /// <summary>
    /// 胶质按压：true 时按下缩到 <see cref="MotionTokens.PressScale"/>，并且**换面** ——
    /// 面从 GlassButtonBg 换成 GlassButtonBgPressed（高光带收窄、底部接触阴影压紧）。
    /// 只有面本来就是玻璃按钮那一档的元素才会换面：卡片、文字钮、强调钮的底各有各的出处，
    /// 一律替换会在按下那一秒把卡片也涂成一颗按钮。
    /// </summary>
    public static readonly BindableProperty LiquidProperty = BindableProperty.CreateAttached(
        "Liquid", typeof(bool), typeof(PressFeel), false);

    public static bool GetLiquid(VisualElement element) => (bool)element.GetValue(LiquidProperty);
    public static void SetLiquid(VisualElement element, bool value) => element.SetValue(LiquidProperty, value);

    /// <summary>
    /// 按下时把「亮环」提亮一档：Button / ImageButton 换 BorderColor，Border 换 Stroke。
    /// 只换颜色、不动宽度 —— 宽度的每一次变化都是一次 WinUI 布局重算，
    /// 而「环变宽」的观感在缩放 0.96 里已经由几何自己给出来了。
    /// 环的颜色同样由驱动归位（换页/卸载时收回），不进视觉状态：理由见文件顶部。
    /// </summary>
    public static readonly BindableProperty RingProperty = BindableProperty.CreateAttached(
        "Ring", typeof(bool), typeof(PressFeel), false);

    public static bool GetRing(VisualElement element) => (bool)element.GetValue(RingProperty);
    public static void SetRing(VisualElement element, bool value) => element.SetValue(RingProperty, value);

    private static readonly ConditionalWeakTable<View, Driver> Drivers = new();

    /// <summary>
    /// 所有接过线的驱动。换页/卸载时把还按着的那一笔位移落回 0 ——
    /// 按下后页面立刻切走时，抬起事件整对丢失，按钮会一直低 2.4px，
    /// 用户看到的就是「点过一次，这个钮再也没还原」。
    /// </summary>
    private static readonly List<WeakReference<Driver>> Live = new();
    private static Shell? _navShell;

    private static void HookNavigationSweep()
    {
        // 副窗口（手机页、工具窗）里没有 Shell。多窗口下 Shell.Current 会直接抛异常
        // （提示改用 App.Windows），而这里挂在每个元素的 Loaded 上——一开手机窗就把进程带崩。
        // 从窗口列表里找承载 Shell 的那一个：找不到就跳过，一次异常都不用抛。
        var shell = Application.Current?.Windows
            .Select(w => w.Page)
            .OfType<Shell>()
            .FirstOrDefault();
        if (shell is null || ReferenceEquals(shell, _navShell)) return;
        _navShell = shell;
        shell.Navigating += (_, _) => SettleAll();
        shell.Navigated += (_, _) => SettleAll();
    }

    private static void SettleAll()
    {
        for (var i = Live.Count - 1; i >= 0; i--)
        {
            if (!Live[i].TryGetTarget(out var driver)) { Live.RemoveAt(i); continue; }
            driver.SettleNow();
        }
    }

    static PressFeel() => MotionPrefs.Apply();

    private static void OnEnabledChanged(BindableObject bindable, object oldValue, object newValue)
    {
        // 手势挂在 View 上（Button / Border / ImageButton 都是），所以只认这一层。
        if (newValue is not true || bindable is not View view) return;
        _ = Drivers.GetValue(view, static e => new Driver(e));   // 样式重复套用不会重复接线
    }

    private sealed class Driver
    {
        private readonly View _e;
        private bool _pressed;
        /// <summary>这一次按下的面是不是我们换掉的：卸载时只复原这一种。</summary>
        private bool _faceSwapped;
        /// <summary>这一次按下的环是不是我们提亮的：卸载时只复原这一种。</summary>
        private bool _ringSwapped;
        private bool _sawPress;
        private bool _tapsHooked;

        public Driver(View e)
        {
            _e = e;
            Live.Add(new WeakReference<Driver>(this));
            HookNavigationSweep();
            // 卸载时（以及换页时）把那一笔下沉落回 0：页面先走、抬起事件后到不了，
            // 留在按钮上的就是一次永远收不回去的按压。
            e.Unloaded += (_, _) => SettleOnUnload();
            e.Loaded += (_, _) => HookNavigationSweep();   // 构造函数跑在 Window.Page 接上之前，这里补挂
            // 禁用/隐藏会让指针静默离开（不再有 Released）——那一刻把还按着的那笔落回 0，
            // 否则按钮会在原地低 2.4px，读起来像被按坏了。
            e.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == VisualElement.IsEnabledProperty.PropertyName && !e.IsEnabled)
                    SettleOnUnload();
                else if (args.PropertyName == VisualElement.IsVisibleProperty.PropertyName && !e.IsVisible)
                    SettleOnUnload();
            };
            if (e is Button button)
            {
                // 按下在指针接触那一刻就触发，比 Clicked（松手才来）早整整一次点击的时长——
                // 「响应快」的感受大部分来自这里，不是来自缩短动画。
                button.Pressed += (_, _) =>
                {
                    _sawPress = true;
                    SetPressed(true);
                    // 按下就响：与下沉同时发生，读者听到的「按到了」和看到的位移是同一件事。
                    UiSounds.Tap();
                };
                button.Released += (_, _) => SetPressed(false);
                // 兜底只补「没收到 Pressed」的平台；正常点击先收起自己的下沉。
                // 带导航的按钮按下时页面就切走，抬起事件可能整条丢失，
                // 这里再收一次，避免回来时按钮还陷在 2.4px 里。
                button.Clicked += (_, _) =>
                {
                    if (_sawPress) { _sawPress = false; SetPressed(false); return; }
                    _ = TapOnce();
                };
            }
            else if (e is ImageButton imageButton)
            {
                imageButton.Clicked += (_, _) => _ = TapOnce();
            }
            else
            {
                // Border 自绘按钮（主游戏互动钮、可点卡片）：附加属性套用时 XAML 里的
                // GestureRecognizers 往往还没填完，等首次 Loaded 再挂。
                e.Loaded += (_, _) => HookTaps();
            }
        }

        private void HookTaps()
        {
            if (_tapsHooked) return;
            _tapsHooked = true;
            foreach (var tap in _e.GestureRecognizers.OfType<TapGestureRecognizer>().ToList())
                tap.Tapped += (_, _) => _ = TapOnce();
        }

        private void SetPressed(bool on)
        {
            if (_pressed == on) return;
            _pressed = on;
            // 按下与松手都走同一条弹簧：进得快（TapMs），出来慢一档（SettleMs）。
            // 曲线自带过冲，所以松手那一下会微微弹过去再落回 —— 这是「材料回弹」，
            // 不是「过渡」；线性与 ease-in-out 都没有这一段，它们读起来是滑，不是弹。
            var ms = on ? MotionTokens.Ms(MotionTokens.TapMs) : MotionTokens.Ms(MotionTokens.SettleMs);
            Move(on ? MotionTokens.Press : 0, ms, MotionTokens.Spring);
            PressRing(on);
            if (!GetLiquid(_e)) return;
            ScaleTo(on ? MotionTokens.PressScale : 1.0, ms);
            PressFace(on);
        }

        /// <summary>
        /// 换面：静止/悬停的面 → 按下那一面。只在「此刻挂的是玻璃按钮那一档」时动手 ——
        /// 认的是画刷实例（令牌每次下发都是新实例，而 DynamicResource 会把同一个实例发给所有引用者）。
        /// 走 SetDynamicResource 而不是直接赋画刷：直接赋值会把 DynamicResource 这条链断开，
        /// 之后换主题这颗钮就永久停在旧色上（HoverFeel 的注释里记着同一个坑）。
        /// </summary>
        private void PressFace(bool on)
        {
            var res = Application.Current?.Resources;
            if (res is null) return;
            var face = _e.Background;
            if (face is null) return;
            if (!res.TryGetValue(GlassTokens.ButtonBg, out var rest) || rest is not Brush) return;
            if (!res.TryGetValue(GlassTokens.ButtonBgPressed, out var pressed) || pressed is not Brush) return;
            var hover = res.TryGetValue(GlassTokens.ButtonBgHover, out var hv) ? hv : null;
            if (!ReferenceEquals(face, rest) && !ReferenceEquals(face, hover)) return;
            _e.SetDynamicResource(VisualElement.BackgroundProperty,
                on ? GlassTokens.ButtonBgPressed : GlassTokens.ButtonBg);
            _faceSwapped = true;
        }

        /// <summary>
        /// 环：按下换成更亮的那一档，松手换回来。三种控件的「环」不是同一个属性 ——
        /// Button / ImageButton 只有 BorderColor（吃 Color），Border 才有 Stroke（吃 Brush）。
        /// 走 SetDynamicResource 而不是直接赋值：直接赋值会断掉 DynamicResource 这条链，
        /// 之后换主题这颗钮的环就永久停在旧色上。
        /// </summary>
        private void PressRing(bool on)
        {
            if (!GetRing(_e)) return;
            var res = Application.Current?.Resources;
            if (res is null) return;
            if (_e is Button b)
            {
                if (!res.ContainsKey(GlassTokens.ButtonRingColorHot)) return;
                b.SetDynamicResource(Button.BorderColorProperty,
                    on ? GlassTokens.ButtonRingColorHot : GlassTokens.ButtonRingColor);
                _ringSwapped = true;
            }
            else if (_e is ImageButton ib)
            {
                if (!res.ContainsKey(GlassTokens.ButtonRingColorHot)) return;
                ib.SetDynamicResource(ImageButton.BorderColorProperty,
                    on ? GlassTokens.ButtonRingColorHot : GlassTokens.ButtonRingColor);
                _ringSwapped = true;
            }
            else if (_e is Border bd)
            {
                if (!res.ContainsKey(GlassTokens.ButtonStrokeHot)) return;
                bd.SetDynamicResource(Border.StrokeProperty,
                    on ? GlassTokens.ButtonStrokeHot : GlassTokens.ButtonStroke);
                _ringSwapped = true;
            }
        }

        /// <summary>
        /// 缩放交回 MAUI 自己的动画时钟（和位移同一个），带同名抢占：
        /// 快速连点时新的一笔从当前值接手，不会排成队追着手指抖。
        /// 这里必须走驱动而不是 VisualState 的 Setter —— 后者在按下后立刻导航时收不到抬起，
        /// 会把按钮永久留在 0.97 上（颜色那一档踩过同样的坑，见 ButtonShapes 顶部）。
        /// </summary>
        private void ScaleTo(double scale, int ms)
        {
            if (ms <= 0) { _e.Scale = scale; return; }
            _ = _e.ScaleTo(scale, (uint)ms, new Easing(MotionTokens.Spring));
        }

        /// <summary>
        /// 换页时把按着的那一笔落回静止位。只碰「这一次确实按过」的元素：
        /// 新页面正在跑进场位移时 TranslationY 也是非零，不能顺手把它掐掉。
        /// </summary>
        public void SettleNow()
        {
            if (!_pressed && !_sawPress) return;
            SettleOnUnload();
        }

        /// <summary>
        /// 卸下这一层时无条件归零。按钮不会再收到抬起事件，所以连动画一起停，终值就是 0；
        /// 元素已经离开树，这里也不会压到任何正在跑的进场。
        /// </summary>
        private void SettleOnUnload()
        {
            _pressed = false;
            _sawPress = false;
            _e.AbortAnimation("TranslateTo");
            if (_e.TranslationY != 0) _e.TranslationY = 0;
            if (_e.Scale != 1)
            {
                _e.AbortAnimation("ScaleTo");
                _e.Scale = 1;
            }
            if (_faceSwapped)
            {
                _faceSwapped = false;
                _e.SetDynamicResource(VisualElement.BackgroundProperty, GlassTokens.ButtonBg);
            }
            if (_ringSwapped)
            {
                _ringSwapped = false;
                PressRing(false);
            }
        }

        /// <summary>没有按下事件可用时的兜底：一次「沉-起」，回弹交给上一步的 Task 完成后再走。</summary>
        private async Task TapOnce()
        {
            if (_pressed) return;
            _pressed = true;
            UiSounds.Tap();
            await Move(MotionTokens.Press, MotionTokens.Ms(MotionTokens.TapMs), MotionTokens.Spring);
            if (!_pressed) return;              // 中途被真正的抬起事件接走，别抢它的回弹
            _pressed = false;
            await Move(0, MotionTokens.Ms(MotionTokens.SettleMs), MotionTokens.Spring);
        }

        /// <summary>
        /// 交回 MAUI 自己的动画时钟：回调跑在 UI 线程的 ticker 上。
        /// 早先用 Task.Delay(16) 自己数帧，续接会落到线程池，每一帧都是一次跨线程属性赋值——
        /// 帧数没多、流畅度却明显不如原生，纯属白付的钱。
        /// 同名提交（TranslateTo）自带抢占：新按压直接从当前值接手，不会排队追着手指抖。
        /// </summary>
        private Task Move(double y, int ms, Func<double, double> curve)
        {
            if (ms <= 0)
            {
                _e.TranslationY = y;            // 减少动画：照样落到终值，只是不补中间帧
                return Task.CompletedTask;
            }
            return _e.TranslateTo(0, y, (uint)ms, new Easing(curve));
        }
    }
}
