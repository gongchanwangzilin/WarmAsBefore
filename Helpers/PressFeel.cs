using System.Runtime.CompilerServices;
using WarmAsBefore.DesignSystem.Theme;

namespace WarmAsBefore.Helpers;

/// <summary>
/// 「按下去是沉的」：给按钮补上真正会被看见的按压反馈。
///
/// 为什么需要代码：MAUI 的 VisualStateManager 只有 Setter，没有「过渡」这个概念——
/// XAML 里写 Pressed 把透明度掉到 0.7，效果是啪一下跳变，既不像被按住，也像按钮在消失。
/// 所以分工是：**样式负责每个状态长什么样（颜色、描边），本类负责状态之间怎么走**。
///
/// 只动 TranslationY，而且终点永远是 0。三条都是踩出来的：
///   · 不用 Scale——WinUI 下按压缩放会触发重新布局，快速连点时按钮会卡住。
///   · 不碰 Opacity——透明度还被进场动画与禁用态写着，多处同时赋值必然互相顶掉：
///     元素会停在某个半程值上，看起来就是「按键莫名其妙消失了」。
///     颜色也不在这里动：按下不换色，只下沉（用户要的是「变深归悬停、下沉归按下」）。
///   · 不记基准值——按下就是 0→SinkPx、抬起回 0。曾经读过的「进来时的 TranslationY / Opacity」
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
/// 用法：在按钮样式里加一行 &lt;Setter Property="motion:PressFeel.Enabled" Value="True" /&gt;。
/// </summary>
public static class PressFeel
{
    public static readonly BindableProperty EnabledProperty = BindableProperty.CreateAttached(
        "Enabled", typeof(bool), typeof(PressFeel), false, propertyChanged: OnEnabledChanged);

    public static bool GetEnabled(VisualElement element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(VisualElement element, bool value) => element.SetValue(EnabledProperty, value);

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
        if (Shell.Current is not { } shell || ReferenceEquals(shell, _navShell)) return;
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
                button.Pressed += (_, _) => { _sawPress = true; SetPressed(true); };
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
            Move(on ? MotionTokens.Press : 0,
                on ? MotionTokens.Ms(MotionTokens.TapMs) : MotionTokens.Ms(MotionTokens.SettleMs),
                on ? MotionTokens.OutCubic : MotionTokens.Settle);
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
        }

        /// <summary>没有按下事件可用时的兜底：一次「沉-起」，回弹交给上一步的 Task 完成后再走。</summary>
        private async Task TapOnce()
        {
            if (_pressed) return;
            _pressed = true;
            await Move(MotionTokens.Press, MotionTokens.Ms(MotionTokens.TapMs), MotionTokens.OutCubic);
            if (!_pressed) return;              // 中途被真正的抬起事件接走，别抢它的回弹
            _pressed = false;
            await Move(0, MotionTokens.Ms(MotionTokens.SettleMs), MotionTokens.Settle);
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
