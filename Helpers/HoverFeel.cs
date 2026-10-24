using System.Reflection;
using System.Runtime.CompilerServices;
using WarmAsBefore.DesignSystem.Worlds;

namespace WarmAsBefore.Helpers;

/// <summary>
/// 「鼠标现在在这儿」：桌面端唯一能实时证明「这一行可以点」的信号。
///
/// 为什么非得写代码：WinUI 上 MAUI 不进 PointerOver 视觉状态，PointerGestureRecognizer 的
/// PointerEntered/Exited 也不回调 —— PressFeel 的注释里记着那次实测（把整个驱动去掉，按钮照样纹丝不动）。
/// 但事件本身没有丢：它只在平台原生元素那一层才会来。所以这里接的是
/// Microsoft.UI.Xaml.UIElement 的 PointerEntered/Exited/Pressed/Released/Canceled，
/// 而且必须用 AddHandler(ev, h, handledEventsToo: true) —— Border 的原生视图是 MAUI 自己的
/// ContentPanel，可见的那层 Border 是它的孩子，MAUI 的输入栈在冒泡途中就把事件标成已处理，
/// 用 += 订阅一个像素也换不来（运行时日志里 attach 成功、enter 一次没来）。
///
/// 验证只能靠真人推鼠标：SetCursorPos 那种「瞬移」不产生输入包，WinUI 的悬停管线不认，
/// 截图比对会得到「移上去毫无变化」的假阴性（踩过，别照着这条再测一遍）。
///
/// 只写 Background，不碰 Opacity / Shadow / Scale：那三条分别被进场动画、禁用态和合成层管着，
/// 抢过来就是「按键莫名其妙消失」「点过的按钮返回后整块空白」那两类回归（见 ButtonShapes.xaml 顶部）。
/// 位移仍归 PressFeel：静止→悬停→按下是颜色上的三级台阶，沉下去那 1.6px 是它的事。
///
/// 颜色从元素**自己当前这张面**算（StyleWorldTokens.Step / Wash），不发固定令牌：
/// 同一条行可能站在页底上、卡在卡面上、盖在一张图上，固定值必然在其中一处变成「没变化」。
/// 半透明的玻璃面只往同一方向压一点，Alpha 原样保留 —— 换成一颗不透明的灰会把玻璃糊成一块塑料。
///
/// 离开时把值**交还给样式**（ClearValue）而不是还原成「我记得的那个颜色」：样式里的 DynamicResource
/// 只有一值不被本地值压着才继续跟着世界换档；本地哪怕写回一个原值，这条动态链接也就永久断了，
/// 那一行从此钉死在旧世界上（换风格时它不会跟着换）。所以只有静止那一档确实由样式给出时才接线，
/// 页面自己内联写 Background 的元素直接不亮 —— 少一次悬停，换一个不会坏的主题跟随。
/// 同理不碰渐变画刷：用纯色盖上去会把它的底纹整个抹掉。
///
/// 触屏没有悬停这件事，所以整套驱动只在 Windows 上接线；Android 那边附加属性照样能写，只是不接。
///
/// 用法：&lt;Setter Property="motion:HoverFeel.Enabled" Value="True" /&gt;（只认 Border）。
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
        if (newValue is not true || bindable is not Border border) return;
#if WINDOWS
        _ = Drivers.GetValue(border, static e => new Driver(e));   // 样式重复套用不会重复接线
#endif
    }

#if WINDOWS
    private static readonly ConditionalWeakTable<Border, Driver> Drivers = new();

    private sealed class Driver
    {
        private readonly Border _e;
        private Microsoft.UI.Xaml.UIElement? _native;
        private Microsoft.UI.Xaml.Input.PointerEventHandler? _onEnter, _onExit, _onDown, _onUp, _onCancel;

        private bool _inside;
        private bool _pressed;
        private bool _hasBase;

        /// <summary>静止时那张面。null 和 Transparent 都算「什么都没画」，那时改用盖色偏那一档。</summary>
        private Brush? _baseFace;
        private Color _face;
        private BindableProperty? _styleProp;

        /// <summary>渐变之类的画刷一律不碰：用纯色盖上去会把它的底纹整个抹掉。</summary>
        private bool _skip;

        public Driver(Border e)
        {
            _e = e;
            e.HandlerChanged += (_, _) => Attach();
            e.Unloaded += (_, _) => Release();
            Attach();
        }

        private void Attach()
        {
            Detach();
            if (_e.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement native) return;
            _native = native;
            _onEnter = (_, _) => { _inside = true; Capture(); Paint(); SetHand(true); };
            _onExit = (_, _) => { _inside = false; _pressed = false; Release(); SetHand(false); };
            _onDown = (_, _) => { _pressed = true; Paint(); };
            _onUp = (_, _) => { _pressed = false; Paint(); };
            // 指针被系统收走（拖出窗口、弹出模态）时不会有抬起，也没有离开 —— 不补这一下会留下一行永远亮着的底。
            _onCancel = (_, _) => { _inside = false; _pressed = false; Release(); SetHand(false); };
            // 必须 AddHandler(..., handledEventsToo: true)：Border 的原生视图是 MAUI 自己的 ContentPanel，
            // 它下面那层可见 Border 才是被点中的东西，而 MAUI 的输入栈会在事件冒泡上来时把它标成已处理。
            // 用 += 订阅实测一个像素都换不来（运行时日志里 attach 成功、enter 一次没来）。
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
            Release();
            SetHand(false);
        }

        /// <summary>基准只在真正进入悬停时抓一次：按下/抬起都在这份基准上走，不会越按越深。</summary>
        private void Capture()
        {
            if (_hasBase) return;
            _baseFace = _e.Background;
            _styleProp = StyleBackgroundProperty(_e.Style);
            // 静止那一档必须真是样式给的，才有人能还回去：页面内联写的 {DynamicResource}
            // 一旦被本地值顶掉就再也接不回主题，写回原画刷也接不回来（丢的是那条动态链接）。
            // 宁可这一行不亮，也不给它留一个「换风格后它不换」的暗伤。
            _skip = _styleProp is null || _baseFace is not null and not SolidColorBrush;
            _face = (_baseFace as SolidColorBrush)?.Color ?? Colors.Transparent;
            _hasBase = true;
        }

        private void Paint()
        {
            if (_skip) return;
            if (!_inside && !_pressed) { Release(); return; }
            var step = _pressed ? PressStep : HoverStep;
            // Alpha 大于 0 就是「这里有一张面」：在它身上沉（Mix 保留 Alpha，玻璃还是玻璃）。
            // 完全透明才改成往父面上压一层色偏。
            var color = _face.Alpha > 0.02f
                ? StyleWorldTokens.Step(_face, step)
                : StyleWorldTokens.Wash(step);
            _e.Background = new SolidColorBrush(color);
        }

        /// <summary>把这一档还给静止态：ClearValue 之后样式那条 DynamicResource 重新生效，跟着世界换档。</summary>
        private void Release()
        {
            _hasBase = false;
            if (_skip) return;
            if (_styleProp is not null) _e.ClearValue(_styleProp);
            _styleProp = null;
            _baseFace = null;
            _face = Colors.Transparent;
        }

        /// <summary>沿 BasedOn 链找样式里那条 Background 的绑定属性 —— 借它才敢 ClearValue。</summary>
        private static BindableProperty? StyleBackgroundProperty(Style? style)
        {
            for (var s = style; s is not null; s = s.BasedOn)
                foreach (var setter in s.Setters)
                    if (setter is Setter st && st.Property.PropertyName == nameof(Border.Background))
                        return st.Property;
            return null;
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
            try
            {
                object? shape = on ? Hand : null;
                CursorProp?.SetValue(_native, shape);
            }
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
