using System.Reflection;
using System.Runtime.CompilerServices;
using WarmAsBefore.DesignSystem.Worlds;

namespace WarmAsBefore.Helpers;

/// <summary>
/// 「鼠标现在在这儿」：桌面端唯一能实时证明「这一行可以点」的信号。
///
/// 为什么非得写代码：WinUI 上 MAUI 不进 PointerOver 视觉状态，PointerGestureRecognizer 的
/// PointerEntered/Exited 也不回调 —— PressFeel 的注释里记着那次实测。
/// 但事件本身没有丢：它只在平台原生元素那一层才会来，而且必须用
/// AddHandler(ev, handler, handledEventsToo: true) 才收得到 —— Border 的原生视图是 MAUI 自己的
/// ContentPanel，真正被点中的是它下面那层可见 Border，MAUI 的输入栈在冒泡途中就把事件标成已处理，
/// 用 += 订阅一个像素也换不来（运行时日志里 attach 成功、enter 一次没来）。
///
/// 验证只能靠真人推鼠标：SetCursorPos 那种「瞬移」不产生输入包，WinUI 的悬停管线不认，
/// 拿截图比对会得到「移上去毫无变化」的假阴性（踩过，别再照这条测一遍）。
///
/// 只画 Background，不碰 Opacity / Shadow / Scale：那三条分别被进场动画、禁用态和合成层管着，
/// 抢过来就是「按键莫名其妙消失」「点过的按钮返回后整块空白」那两类回归（见 ButtonShapes.xaml 顶部）。
/// 位移仍归 PressFeel：静止→悬停→按下是颜色上的三级台阶，沉下去那 1.6px 是它的事。
///
/// 关键取舍：**改的是原生画刷，不是 MAUI 的 Background 属性**。
/// 早先的写法是「往 Forms 侧写一个色，离开时 ClearValue 还给样式」，那样只有静止底由样式给出的
/// 元素才敢接线——页面里内联写了 Background="{DynamicResource …}" 的一律不能碰，因为本地值一旦压上去，
/// 那条动态链接就接不回来了（换风格世界时那一行会钉死在旧色上）。而全应用 155 条可点的 Border
/// 里绝大多数正是内联写法：靠样式那一档，等于只修了主页四条行，别处照旧没反馈。
/// 现在整条链路都在原生侧：进找到那张真正在画的面、记下它原来的画刷、换成压深一档的色，
/// 离开时**只在没人动过它的前提下**换回去。MAUI 的属性系统从头到尾没被碰，动态链接原样有效；
/// 悬停期间万一真的换了主题，MAUI 会把新画刷写进来，那时我们只交还自己那份判断（值不是我们写的那个
/// 就不还），不会把旧世界的颜色盖回去。
///
/// 颜色从那张面**自己的颜色**算（StyleWorldTokens.Step / Wash），不发固定令牌：
/// 同一条行可能站在页底上、卡在卡面上、盖在一张图上，固定值必然在其中一处变成「没变化」。
/// 半透明的玻璃面只往同一方向压一点，Alpha 原样保留 —— 换成一颗不透明的灰会把玻璃糊成一块塑料。
/// 渐变画刷直接不接：用纯色盖上去会把它的底纹整个抹掉。
///
/// 触屏没有悬停这件事，所以整套驱动只在 Windows 上接线；Android 那边附加属性照样能写，只是不接。
///
/// 用法：&lt;Setter Property="motion:HoverFeel.Enabled" Value="True" /&gt;，或在内联元素上直接写这颗附加属性。
/// </summary>
public static class HoverFeel
{
    /// <summary>
    /// 悬停压多少、按下再压多少。地面 #F3F3F3 上 0.07 大约是 #E1E1E1，
    /// 和导航选中块同一档（选中靠的是左缘那颗胶囊与加粗的字，不是这块底）；
    /// 按下再到 0.12，台阶才是「静止 → 亮 → 沉」而不是「亮 → 还是亮」。
    /// </summary>
    public const double HoverStep = 0.07;
    public const double PressStep = 0.12;

    public static readonly BindableProperty EnabledProperty = BindableProperty.CreateAttached(
        "Enabled", typeof(bool), typeof(HoverFeel), false, propertyChanged: OnEnabledChanged);

    public static bool GetEnabled(VisualElement element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(VisualElement element, bool value) => element.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (newValue is not true || bindable is not VisualElement e) return;
#if WINDOWS
        _ = Drivers.GetValue(e, static v => new Driver(v));   // 样式重复套用不会重复接线
#endif
    }

#if WINDOWS
    private static readonly ConditionalWeakTable<VisualElement, Driver> Drivers = new();

    private sealed class Driver
    {
        private readonly VisualElement _e;
        private Microsoft.UI.Xaml.UIElement? _native;
        private Microsoft.UI.Xaml.Input.PointerEventHandler? _onEnter, _onExit, _onDown, _onUp, _onCancel;

        private bool _inside;
        private bool _pressed;

        /// <summary>那张面的读写把手（原生侧），加进/离开时抓一次。</summary>
        private Func<Microsoft.UI.Xaml.Media.Brush>? _get;
        private Action<Microsoft.UI.Xaml.Media.Brush>? _set;

        /// <summary>进悬停前它自己那颗画刷，和本次写进去的那颗（用来判断有没有人插手动过）。</summary>
        private Microsoft.UI.Xaml.Media.Brush? _rest;
        private Microsoft.UI.Xaml.Media.Brush? _painted;
        /// <summary>
        /// 那张面自己的颜色。存成 MAUI 的 Color（通道 0~1）而不是原生的（0~255）：
        /// 压深那套算法 StyleWorldTokens.Step / Wash 收的就是 MAUI Color，两头各一套单位
        /// 早先踩过一次 —— 拿原生 byte 的 A 去和 0.02f 比，透明底永远判成「有颜色」。
        /// </summary>
        private Color _face;
        private bool _skip;

        public Driver(VisualElement e)
        {
            _e = e;
            e.HandlerChanged += (_, _) => Attach();
            e.Unloaded += (_, _) => Restore();
            Attach();
        }

        private void Attach()
        {
            Detach();
            if (_e.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement native) return;
            _native = native;
            _onEnter = (_, _) => { _inside = true; Capture(); Paint(); SetHand(true); };
            _onExit = (_, _) => { _inside = false; _pressed = false; Restore(); SetHand(false); };
            _onDown = (_, _) => { _pressed = true; Paint(); };
            _onUp = (_, _) => { _pressed = false; Paint(); };
            // 指针被系统收走（拖出窗口、弹出模态）时不会有抬起，也没有离开 —— 不补这一下会留下一行永远亮着的底。
            _onCancel = (_, _) => { _inside = false; _pressed = false; Restore(); SetHand(false); };
            Add(native, Microsoft.UI.Xaml.UIElement.PointerEnteredEvent, _onEnter);
            Add(native, Microsoft.UI.Xaml.UIElement.PointerExitedEvent, _onExit);
            Add(native, Microsoft.UI.Xaml.UIElement.PointerPressedEvent, _onDown);
            Add(native, Microsoft.UI.Xaml.UIElement.PointerReleasedEvent, _onUp);
            Add(native, Microsoft.UI.Xaml.UIElement.PointerCanceledEvent, _onCancel);
        }

        private static void Add(Microsoft.UI.Xaml.UIElement native, Microsoft.UI.Xaml.RoutedEvent ev,
            Microsoft.UI.Xaml.Input.PointerEventHandler handler)
        {
            try { native.AddHandler(ev, handler, true); }
            catch (Exception ex) { WarmAsBefore.App.WriteLog("[hover] 挂事件失败：" + ex.Message); }
        }

        private static void Remove(Microsoft.UI.Xaml.UIElement native, Microsoft.UI.Xaml.RoutedEvent ev,
            Microsoft.UI.Xaml.Input.PointerEventHandler? handler)
        {
            if (handler is null) return;
            try { native.RemoveHandler(ev, handler); } catch { }
        }

        private void Detach()
        {
            if (_native is not { } native) return;
            Remove(native, Microsoft.UI.Xaml.UIElement.PointerEnteredEvent, _onEnter);
            Remove(native, Microsoft.UI.Xaml.UIElement.PointerExitedEvent, _onExit);
            Remove(native, Microsoft.UI.Xaml.UIElement.PointerPressedEvent, _onDown);
            Remove(native, Microsoft.UI.Xaml.UIElement.PointerReleasedEvent, _onUp);
            Remove(native, Microsoft.UI.Xaml.UIElement.PointerCanceledEvent, _onCancel);
            _native = null;
            _inside = false;
            _pressed = false;
            Restore();
            SetHand(false);
        }

        /// <summary>
        /// 进悬停时抓一次基准：按下/抬起都在这份基准上走，不会越按越深。
        /// 面只在两层里找，理由写在 FindFace 上。
        /// </summary>
        private void Capture()
        {
            if (_get is not null || _skip) return;
            if (_native is null) { _skip = true; return; }
            var face = FindFace(_native);
            if (face is null) { _skip = true; return; }
            (_get, _set) = face.Value;
            _rest = _get();
            // 渐变/贴图之类的底不碰：纯色盖上去会把它的底纹整个抹掉。
            if (_rest is not null and not Microsoft.UI.Xaml.Media.SolidColorBrush) { _skip = true; return; }
            _face = (_rest as Microsoft.UI.Xaml.Media.SolidColorBrush) is { } solid
                ? ToMaui(solid.Color)
                : Colors.Transparent;
        }

        private static Color ToMaui(Windows.UI.Color c) =>
            new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

        private static byte ToByte(float v) => (byte)Math.Clamp(v * 255f + 0.5f, 0f, 255f);

        /// <summary>
        /// 找这张元素自己的那层面，只看两层，顺序是「谁在画就压谁」：
        /// ① 原生视图本身是 Border（MAUI 的 Border 直接落到一颗 Border）；
        /// ② 原生视图本身是铺了**看得见**的底的 Panel（页眉条、整块卡头）；
        /// ③ 唯一的子级 Border——MAUI 常在外面再包一层 ContentPanel，真正带圆角在画的是里面那颗
        ///    （PhonePage 的桌面图标：竖排本身透明，有底的是那块 58px 的图标）。
        /// ④ 都不成立就不画。
        ///
        /// 三条都是挡回归的：
        /// · 子级要「唯一」才敢用。全屏遮罩（AffectionLevelUp 那种「点任何地方关掉」）根 Grid 是透明的，
        ///   底下摆着好几张卡，蒙对哪张都不对——鼠标一进全屏变深，那是故障不是反馈。
        /// · Panel 那档要 Alpha 够大才算：透明 Panel 铺的是方角，压上去会把圆角卡盖出一圈直角。
        /// · 再往里（孙辈）不找，那里已经是别人的内容。
        /// </summary>
        private static (Func<Microsoft.UI.Xaml.Media.Brush>, Action<Microsoft.UI.Xaml.Media.Brush>)? FindFace(
            Microsoft.UI.Xaml.UIElement root)
        {
            if (BorderFace(root) is { } own) return own;
            if (PanelFace(root) is { } plate) return plate;

            (Func<Microsoft.UI.Xaml.Media.Brush>, Action<Microsoft.UI.Xaml.Media.Brush>)? only = null;
            var ambiguous = false;
            for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root) && !ambiguous; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
                if (BorderFace(child) is not { } face) continue;
                if (only is not null) { ambiguous = true; break; }
                only = face;
            }
            return only;
        }

        private static (Func<Microsoft.UI.Xaml.Media.Brush>, Action<Microsoft.UI.Xaml.Media.Brush>)? BorderFace(
            Microsoft.UI.Xaml.DependencyObject vo) =>
            vo is Microsoft.UI.Xaml.Controls.Border { Background: not null } b
                ? (() => b.Background, value => b.Background = value)
                : null;

        private static (Func<Microsoft.UI.Xaml.Media.Brush>, Action<Microsoft.UI.Xaml.Media.Brush>)? PanelFace(
            Microsoft.UI.Xaml.DependencyObject vo) =>
            vo is Microsoft.UI.Xaml.Controls.Panel
                { Background: Microsoft.UI.Xaml.Media.SolidColorBrush { Color: { A: > 5 } } } p
                ? (() => p.Background, value => p.Background = value)
                : null;

        private void Paint()
        {
            if (_skip || _set is null) return;
            if (!_inside && !_pressed) { Restore(); return; }
            var step = _pressed ? PressStep : HoverStep;
            // Alpha 大于 0 就是「这里有一张面」：在它身上沉（Step 保留 Alpha，玻璃还是玻璃）。
            // 完全透明才改成往父面上压一层色偏。
            var color = _face.Alpha > 0.02f
                ? StyleWorldTokens.Step(_face, step)
                : StyleWorldTokens.Wash(step);
            _painted = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Windows.UI.Color.FromArgb(ToByte(color.Alpha), ToByte(color.Red),
                    ToByte(color.Green), ToByte(color.Blue)));
            _set(_painted);
        }

        /// <summary>
        /// 交还静止那一档。只在「这块底还是我写进去的那颗」时才还：
        /// 悬停途中主题换了、MAUI 把自己的新画刷写了进来，那就让它留着，我们那份已经过期。
        /// </summary>
        private void Restore()
        {
            if (_get is not null && _set is not null && _painted is not null &&
                ReferenceEquals(_get(), _painted))
                _set(_rest);
            _get = null;
            _set = null;
            _rest = null;
            _painted = null;
            _face = Colors.Transparent;
            _skip = false;
        }

        /// <summary>
        /// 手型。ProtectedCursor 是 protected CLR 属性，而投影出来的 .NET 程序集里根本没有
        /// ProtectedCursorProperty 这个依赖属性字段（编译器直接报 CS0117），站在继承树外面只剩
        /// 非公开属性这一个把手 —— 所以这里走一次反射，句柄缓存成静态字段，每次进出只有一下后期绑定。
        /// 拿不到就只换填充：悬停本身不靠它。
        /// </summary>
        private void SetHand(bool on)
        {
            if (_native is null) return;
            try { CursorProp?.SetValue(_native, on ? Hand : null); }
            catch { /* 这一版运行时没给这颗手型 */ }
        }

        private static readonly PropertyInfo? CursorProp = typeof(Microsoft.UI.Xaml.UIElement)
            .GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly Microsoft.UI.Input.InputSystemCursor? Hand = CreateHand();

        private static Microsoft.UI.Input.InputSystemCursor? CreateHand()
        {
            // 枚举叫 InputSystemCursorShape（WinUI 把它挪进了 Microsoft.UI.Input，不再住 Windows.UI.Core）。
            try { return Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand); }
            catch { return null; }
        }
    }
#endif
}
