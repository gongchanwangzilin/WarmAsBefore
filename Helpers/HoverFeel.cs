using System.Reflection;
using System.Runtime.CompilerServices;
using WarmAsBefore.DesignSystem.Theme;
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
/// 默认只画 Background，不碰 Opacity / Shadow / Scale：那三条分别被进场动画、禁用态和合成层管着，
/// 抢过来就是「按键莫名其妙消失」「点过的按钮返回后整块空白」那两类回归（见 ButtonShapes.xaml 顶部）。
/// 颜色只分两级：静止 → 悬停。按下不再继续加深，改由 PressFeel 往下沉 ——
/// 用户的原话是「变深效果和按下效果要分开：移到那里变深，按下的时候再往下有按动」。
/// 两件事各占一个通道，按下去就不会读成「又深了一档颜色」。
///
/// 唯一的例外是 <see cref="LiftProperty"/>：会动的东西（可点卡片）只压深色还是读不出「可以拿起来」。
/// 那里用的是 TranslationY 上浮而不是缩放——WinUI 下缩放触发重布局这条已经在按压那一层踩过
/// （见 <see cref="PressFeel"/>），而卡片要挨的是同一条路：鼠标在网格上快速划过，
/// 十几张卡同时重布局就是「越用越钝」。上浮与按下共用 TranslationY 这一个输出量，
/// 所以抬起那一下要把上浮重新补回去（见 _onUp），否则按过一次之后卡片会瘫在原位。
///
/// 关键取舍：**改的是原生画刷，不是 MAUI 的 Background 属性**。
/// 早先的写法是「往 Forms 侧写一个色，离开时 ClearValue 还给样式」，那样只有静止底由样式给出的
/// 元素才敢接线——页面里内联写了 Background="{DynamicResource …}" 的一律不能碰，因为本地值一旦压上去，
/// 那条动态链接就接不回来了（换风格世界时那一行会钉死在旧色上）。而全应用 155 条可点的 Border
/// 里绝大多数正是内联写法：靠样式那一档，等于只修了主页四条行，别处照旧没反馈。
/// 现在整条链路都在原生侧：进找到那张真正在画的面、换成压深一档的色、离开时**交还现算的静止值**。
/// 「现算」是必须的：静止那档到底多深要问 MAUI 那一侧的属性（FormsRest），不能拿进悬停那一刻
/// 原生面上的画刷当基准——WinUI 上按钮的视觉状态和我们抢同一块底，它可能已经把预备色写上去了，
/// 而 MAUI 中途任意一次重新映射都会换掉画刷**实例**，靠引用相等判断「有没有别人动过」于是再也不还。
/// 用户看到的正是那个：鼠标过去一次，叉号从此卡在要被点击的那一档。
/// MAUI 的属性系统从头到尾没被碰，动态链接原样有效；悬停途中换了风格世界也不留旧色，
/// 因为交还的那一份是从当前世界的属性上现读的。
///
/// 颜色从那张面**自己的颜色**算（StyleWorldTokens.Step / Wash），不发固定令牌：
/// 同一条行可能站在页底上、卡在卡面上、盖在一张图上，固定值必然在其中一处变成「没变化」。
/// 半透明的玻璃面只往同一方向压一点，Alpha 原样保留 —— 换成一颗不透明的灰会把玻璃糊成一块塑料。
/// 渐变画刷直接不接：用纯色盖上去会把它的底纹整个抹掉。
///
/// 0.07 那一档是个量得出的数，不是一个读得见的变化。真正可点、又长得像纸的那一行
/// （导航行、设置行）开 <see cref="AccentProperty"/>：那一档不是「沉一点」，而是**从左边擦进来的一颗强调底**——
/// 静止底是透明时它等于「这块刚被涂了」，静止底有色时它盖掉旧底，两种都读得出。
/// 只换填充、不动字色：强调色的明度两套世界是反着给的（浅档提亮、深档压暗），
/// 现有的墨色压在它上面本来就读得通，再扫一遍字色就是和样式抢同一位。
/// 这一档存在的全部理由就是用户那句话：「鼠标划过去按键要变身，以提醒这里能点击」——
/// 上一版只写了「整块换成强调底」，实测仍然只换来一个手型光标，因为根本没找到可画的面（见 FindFace 的第 ⑤ 档）。
///
/// 触屏没有悬停这件事，所以整套驱动只在 Windows 上接线；Android 那边附加属性照样能写，只是不接。
///
/// 用法：&lt;Setter Property="motion:HoverFeel.Enabled" Value="True" /&gt;，或在内联元素上直接写这颗附加属性。
/// </summary>
public static class HoverFeel
{
    /// <summary>
    /// 悬停压多少。地面 #F3F3F3 上 0.07 大约是 #E1E1E1，
    /// 和导航选中块同一档（选中靠的是左缘那颗胶囊与加粗的字，不是这块底）。
    /// 按下没有第二个颜色台阶：那一下交给 PressFeel 的位移，颜色停在悬停这一档。
    /// </summary>
    public const double HoverStep = 0.10;

    /// <summary>
    /// 强调档实际压下去的浓度。AccentWarm 是**填充档**（浅色世界 L≈0.80），直接铺上去
    /// 在白卡上只有 1.1:1 —— 用户连报三次「还是没有真的变深」，根子就在这：涂了，但读不出。
    /// 所以强调档不再铺 AccentWarm 本身，而是拿它的色相去调一颗**深色**，再用这个透明度压上去：
    /// 色相归强调色（读得出「这块是热的」），明度归墨水（读得出「这块被点到了」）。
    /// 0.20 在白卡上约 #D5D0CA 一档，眼睛不用比对就能看出变深；按下不再加浓，只往下沉。
    /// </summary>
    public const double AccentHoverAlpha = 0.20;

    /// <summary>
    /// 强调底从左擦到右要多久。80ms：鼠标顺势滑过一行往往只停留一两百毫秒，
    /// 120ms 那档还没擦完指针已经走了 —— 用户读到的就是「动画来不及反应」。
    /// 再短会退化成闪一下，所以配一个起手进度（见 <see cref="WipeHeadStart"/>）而不是靠拖长时长。
    /// </summary>
    public const int WipeMs = 80;

    /// <summary>
    /// 第一帧就先涂到的进度。擦入原本从 0 开始，第一帧等于什么都没画，
    /// 指针快速滑过时整段悬停可能都落在「还没轮到画」的那几帧里。起手就给出三分之一，
    /// 后面再补完剩下的 —— 看得见在先，动效在后。
    /// </summary>
    private const double WipeHeadStart = 0.30;

    public static readonly BindableProperty EnabledProperty = BindableProperty.CreateAttached(
        "Enabled", typeof(bool), typeof(HoverFeel), false, propertyChanged: OnEnabledChanged);

    public static bool GetEnabled(VisualElement element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(VisualElement element, bool value) => element.SetValue(EnabledProperty, value);

    /// <summary>悬停上浮的像素数（0 = 不上浮）。和底色压深一起走，是「可点」的第二重证据。</summary>
    public static readonly BindableProperty LiftProperty = BindableProperty.CreateAttached(
        "Lift", typeof(double), typeof(HoverFeel), 0d, propertyChanged: OnLiftChanged);

    public static double GetLift(VisualElement element) => (double)element.GetValue(LiftProperty);
    public static void SetLift(VisualElement element, double value) => element.SetValue(LiftProperty, value);

    /// <summary>
    /// 悬停时整块换成「当前位置」那一档强调底，而不是只往原底上沉 7%。
    /// 给真正可点又长得像纸的行用（导航行、设置行）：那 7% 在浅底上量得出数字、看不出来，
    /// 用户的话是「鼠标划过去按键要变身，以提醒这里能点击」—— 要的是认得出，不是测得出。
    /// 只换填充，不扫文字颜色（理由写在类型注释上）。
    /// 色不是 AccentWarm 本身：那是**填充档**，浅色世界里 L≈0.80，铺在纸上只有 1.1:1，
    /// 用户量得出、看不见。这里取它的色相，明度按世界方向压到墨那端，再以
    /// <see cref="AccentHoverAlpha"/> 半透明铺上去——换风格跟着走，深浅也真的看得见。
    /// </summary>
    public static readonly BindableProperty AccentProperty = BindableProperty.CreateAttached(
        "Accent", typeof(bool), typeof(HoverFeel), false, propertyChanged: OnAccentChanged);

    public static bool GetAccent(VisualElement element) => (bool)element.GetValue(AccentProperty);
    public static void SetAccent(VisualElement element, bool value) => element.SetValue(AccentProperty, value);

    private static void OnAccentChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (newValue is not true || bindable is not VisualElement e) return;
#if WINDOWS
        _ = Drivers.GetValue(e, static v => new Driver(v));
#endif
    }

    private static void OnLiftChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (newValue is not double px || px <= 0 || bindable is not VisualElement e) return;
#if WINDOWS
        _ = Drivers.GetValue(e, static v => new Driver(v));
#endif
    }

    private static void OnEnabledChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (newValue is not true || bindable is not VisualElement e) return;
#if WINDOWS
        _ = Drivers.GetValue(e, static v => new Driver(v));   // 样式重复套用不会重复接线
#endif
    }

#if WINDOWS
    private static readonly ConditionalWeakTable<VisualElement, Driver> Drivers = new();

    /// <summary>
    /// 所有接过线的驱动。换页时要把亮着的面一次性收回静止档 ——
    /// 点一颗按钮直接导航时，指针还压在它身上，系统不会再补一次 PointerExited；
    /// 页面被 Shell 缓存着回来时，那一块就会永远停在悬停色（用户说的
    /// 「续情缘只要变深一次，就会永远留在变深状态」）。这里不依赖 Unloaded 是否触发。
    /// </summary>
    private static readonly List<WeakReference<Driver>> Live = new();
    private static Shell? _sweepShell;

    private static void HookNavigationSweep()
    {
        // 记的是「哪一个壳」而不是「挂没挂过」：Shell 被重建时旧的那个已经不会再发 Navigating，
        // 一个 bool 会让新的壳永远接不上，换页后的收回就此失效。
        if (Shell.Current is not { } shell || ReferenceEquals(shell, _sweepShell)) return;
        _sweepShell = shell;
        shell.Navigating += (_, _) => RestoreAll(null);
        // Navigated 再收一次。Navigating 时旧页还在树上，过渡里重新布局可能让它再收到一次
        // PointerEntered（指针正压在刚点过的那颗钮上，系统不会补 Exited），那一笔就留到换页之后了。
        // 等壳把新页放稳再扫一遍，才收得干净。
        // 留着当前页的驱动不动：指针可能已经在目标页的某一颗钮上，那一档是正当的悬停。
        shell.Navigated += (_, _) => SweepAfterNavigation(shell);
    }

    /// <summary>
    /// 换页之后分两拍再扫：Shell 的 Navigated 早于过渡收尾，旧页在这段时间里仍然可命中，
    /// 指针停在刚点过的那颗钮上就会再涂一次。只收「已经不在当前页上」的驱动，
    /// 目标页自己的悬停不受影响。
    /// </summary>
    private static void SweepAfterNavigation(Shell shell)
    {
        RestoreAll(shell.CurrentPage);
        Delay(TimeSpan.FromMilliseconds(260), () => RestoreAll(shell.CurrentPage));
        Delay(TimeSpan.FromMilliseconds(700), () => RestoreAll(shell.CurrentPage));
    }

    private static void Delay(TimeSpan delay, Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) { action(); return; }
        try { dispatcher.DispatchDelayed(delay, action); }
        catch { action(); }
    }

    /// <summary>
    /// 把亮着的面收回静止档。<paramref name="keep"/> 是当前页：换页后它上面的悬停是正当的，
    /// 不能碰；传 null 表示「全收」（Navigating 时旧页仍是当前页，正是要收的那一批）。
    /// 另外顺手清一次原生模板的 Pressed 档——按下时页面切走，抬起事件整对丢失，
    /// 那一档不会自己退回去（MAUI 把四档底色映射成同一颗画刷，所以它平时看不出来，
    /// 可模板里还留着状态，指针再进来时的第一笔就是从这一档上算的）。
    /// </summary>
    private static void RestoreAll(Page? keep)
    {
        var restored = 0;
        for (var i = Live.Count - 1; i >= 0; i--)
        {
            if (!Live[i].TryGetTarget(out var driver)) { Live.RemoveAt(i); continue; }
            if (keep is not null && ReferenceEquals(driver.HostPage, keep)) continue;
            if (driver.IsActive) restored++;
            driver.LeaveIfActive();
            driver.ResetIfStuck();
        }
        if (restored > 0 && Environment.GetEnvironmentVariable("WAB_HOVER_TRACE") == "1")
            WarmAsBefore.Services.PerfLog.Note($"[hover] 换页收回 {restored} 个悬停面");
    }

    /// <summary>此刻正悬停着的驱动数。为 0 时窗口级的指针核对直接跳过。</summary>
    private static int _insideCount;

    private static readonly HashSet<Microsoft.UI.Xaml.UIElement> _hookedRoots = new();
    private static readonly HashSet<Window> _hookedWindows = new();

    /// <summary>
    /// 窗口根上再补一道核对：**指针已经不在我身上了**。
    /// 这一道才是主路径：WinUI 的 PointerExited 在按钮上不一定送到（用户看到的
    /// 「移上去变深、移开不还原」就是它没来；同一套 handler 挂在前面的元素类型上却是好的）。
    /// 所以不赌 Exited，改为在根上听冒泡的 PointerMoved，只有「此刻有东西亮着」时才逐个核对，
    /// 平时一次整数比较就返回。指针要离开一颗钮，必然会产生移动事件，挪开后的第一下就收干净。
    ///
    /// 根从元素自己的 XamlRoot 取，而且只在 Loaded / Attach 之后挂：窗口刚建好那几拍
    /// `Window.Handler.PlatformView` 还是 null，早先按它挂，一次也没挂上。
    /// 多窗口各挂各的（宠物窗、手机窗是独立的 XamlRoot），用集合去重。
    /// </summary>
    private static void HookPointerReconcile(Microsoft.UI.Xaml.UIElement? native)
    {
        Microsoft.UI.Xaml.UIElement? root = null;
        try { root = native?.XamlRoot?.Content; }
        catch { /* 元素还没进树：下一次 Attach/Loaded 再试 */ }
        if (root is null)
        {
            try
            {
                if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
                    is Microsoft.UI.Xaml.Window { Content: Microsoft.UI.Xaml.UIElement content })
                    root = content;
            }
            catch { }
        }
        try
        {
            if (root is not null && _hookedRoots.Add(root))
            {
                root.AddHandler(Microsoft.UI.Xaml.UIElement.PointerMovedEvent,
                    new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) => ReconcilePointer(e)), true);
                if (Environment.GetEnvironmentVariable("WAB_HOVER_TRACE") == "1")
                    WarmAsBefore.Services.PerfLog.Note($"[hover] 指针核对已挂上 {root.GetType().Name}");
            }
        }
        catch (Exception ex) { WarmAsBefore.App.WriteLog("[hover] 根指针核对挂载失败：" + ex.Message); }

        // 窗口失焦（Alt+Tab、点去别的窗口）时指针已经不在这一页上了，悬停不该留着。
        try
        {
            if (Application.Current?.Windows.FirstOrDefault() is { } window && _hookedWindows.Add(window))
                window.Deactivated += (_, _) => RestoreAll(null);
        }
        catch { }
    }

    private static void ReconcilePointer(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_insideCount <= 0) return;
        for (var i = Live.Count - 1; i >= 0; i--)
        {
            if (!Live[i].TryGetTarget(out var driver)) { Live.RemoveAt(i); continue; }
            driver.ReconcilePointer(e);
        }
    }

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
        private Color _face = Colors.Transparent;
        private bool _skip;
        /// <summary>进悬停时那块面上**没有**画刷（⑤ 那层）。离开时要还 null，不能还一颗 Solid(Transparent)。</summary>
        private bool _blank;
        /// <summary>这一块底是不是元素自己的属性在画（①②③ 档）。子级那张面（④）不算，静止值只能退回缓存。</summary>
        private bool _ownFace;
        /// <summary>
        /// 静止色能不能问 MAUI 那一侧要到。控件（按钮）一律能：面可能落在模板里那颗子 Border 上（④），
        /// 但属性问的是这个元素自己。**只有控件吃这一档**——普通 Border 的 ④⑦ 面可能属于别的元素
        /// （图标底板、外层卡壳），拿自己的底去还它会把别人的颜色抹掉。
        /// </summary>
        private bool _formsWins;
        /// <summary>这次抓的是哪一层（FindFace 的档名），只给诊断日志用。</summary>
        private string _faceKind = "";
        /// <summary>进悬停时读到的 <see cref="AccentProperty"/>：整块换成强调底，而不是在原底上沉一档。</summary>
        private bool _accent;

        /// <summary>擦入/回收进度（线性 0..1，画的时候再过 Mech 曲线）。只有 Accent 那一档用它。</summary>
        private double _wipeP;
        /// <summary>这一程要走到哪：1 = 涂满，0 = 收干净。改它就是换方向，表不必重建。</summary>
        private double _wipeTarget;
        private Microsoft.UI.Xaml.DispatcherTimer? _tick;

        public Driver(VisualElement e)
        {
            _e = e;
            e.HandlerChanged += (_, _) => Attach();
            e.Unloaded += (_, _) => Leave();
            // 页面被缓存后重新露面时，若上一次离开时漏掉了收回，这里补一刀：
            // 已经收干净的驱动 _painted 为 null，Leave 只做一次空转。
            e.Loaded += (_, _) =>
            {
                HookPage();
                // Loaded 时 XamlRoot 一定已经有了——窗口级指针核对在这里才真正挂得上。
                HookPointerReconcile(e.Handler?.PlatformView as Microsoft.UI.Xaml.UIElement);
                if (_painted is not null) Leave();
                else ResetNativeStateIfPressed();
            };
            // 禁用/隐藏时系统不再把指针算在它身上，之后也不会补 Exited（工作室的「保存」
            // 一按完就禁用、列表在刷新时整排禁用，都会走到这里）——不主动收，底色就留在脸上。
            e.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == VisualElement.IsEnabledProperty.PropertyName && !e.IsEnabled) Leave();
                else if (args.PropertyName == VisualElement.IsVisibleProperty.PropertyName && !e.IsVisible) Leave();
            };
            Live.Add(new WeakReference<Driver>(this));
            HookNavigationSweep();
            HookPointerReconcile(null);
            Attach();
        }

        /// <summary>这一颗钮在哪一页上。换页收回要拿它和当前页比，别把新页正当的悬停一起收掉。</summary>
        public Page? HostPage => _page;

        /// <summary>此刻是否有东西亮着（悬停、按下、或画上去还没还）。日志与换页统计用。</summary>
        public bool IsActive => _inside || _pressed || _painted is not null || _get is not null || _wipeP > 0;

        private Page? _page;
        private EventHandler? _onPageAppearing, _onPageDisappearing;

        /// <summary>
        /// 跟住所在页的出现/离场。Shell 会把根页缓存在导航栈里：换页时它既不入栈也不卸载，
        /// Unloaded / Loaded 都不来 —— 只靠那两条，回来时页面顶栏那颗钮还停在悬停色。
        /// Disappearing 每次离场都发；Appearing 回来时再补一刀（上一拍漏掉的在这里兜住）。
        /// </summary>
        private void HookPage()
        {
            var page = AncestorPage(_e);
            if (ReferenceEquals(page, _page)) return;
            if (_page is not null)
            {
                if (_onPageAppearing is not null) _page.Appearing -= _onPageAppearing;
                if (_onPageDisappearing is not null) _page.Disappearing -= _onPageDisappearing;
            }
            _page = page;
            if (page is null) return;
            _onPageAppearing ??= (_, _) =>
            {
                if (_painted is not null) Leave();
                else ResetNativeStateIfPressed();
            };
            _onPageDisappearing ??= (_, _) => { Trace("page-disappearing"); LeaveIfActive(); };
            page.Appearing += _onPageAppearing;
            page.Disappearing += _onPageDisappearing;
        }

        private static Page? AncestorPage(Element e)
        {
            for (var p = e.Parent; p is not null; p = p.Parent)
                if (p is Page page) return page;
            return null;
        }

        /// <summary>诊断用的短名：按钮直接印出文字，其它印类型名。</summary>
        private string Who => _e is Button b && !string.IsNullOrEmpty(b.Text) ? $"Button“{b.Text}”" : _e.GetType().Name;

        /// <summary>
        /// 悬停路径的留痕，只在 WAB_HOVER_TRACE=1 时写一行。
        /// 「移上去变色、移开不还原」到底是 Entered 没来、Exited 没来、还是收回了没画对，
        /// 三条日志一次就能分清。
        /// </summary>
        private void Trace(string what)
        {
            if (Environment.GetEnvironmentVariable("WAB_HOVER_TRACE") != "1") return;
            WarmAsBefore.Services.PerfLog.Note($"[hover] {Who} {what}");
        }

        /// <summary>脱离悬停：收方向、还静态、放下手型。换页、卸载、失焦都走这一条。</summary>
        public void Leave()
        {
            Trace($"leave painted={_painted is not null} inside={_inside}");
            SetInside(false);
            _pressed = false;
            Restore();
            ResetNativeStateIfPressed();
            SetHand(false);
            Raise(false);
        }

        /// <summary>
        /// 只在「确实还亮着」时才收。换页后会扫到已经建好的新页，
        /// 那些元素正在跑进场位移 —— 没被悬停过就别去碰它们的 TranslationY。
        /// </summary>
        public void LeaveIfActive()
        {
            if (IsActive) Leave();
        }

        /// <summary>
        /// 模板还停在 Pressed 就先放掉。换页扫一遍，修「按下时页面切走、抬起事件整对丢失」留下的那一档；
        /// 没停在 Pressed 时只走一次树检查，不改任何可见状态。
        /// </summary>
        public void ResetIfStuck() => ResetNativeStateIfPressed();

        /// <summary>悬停状态的唯一入口：进出都走这里，好让窗口级的核对知道「现在有没有东西亮着」。</summary>
        private void SetInside(bool on)
        {
            if (_inside == on) return;
            _inside = on;
            _insideCount += on ? 1 : -1;
        }

        /// <summary>
        /// 窗口根报来一次指针移动：按当前坐标核对还在不在里面。
        /// 这是 PointerExited 丢失时的兜底，不是主路径 —— 平常进出仍由原生 Entered/Exited 驱动。
        /// </summary>
        public void ReconcilePointer(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_inside || _native is null) return;
            try
            {
                var p = e.GetCurrentPoint(_native).Position;
                var size = _native.ActualSize;
                if (p.X < 0 || p.Y < 0 || p.X > size.X || p.Y > size.Y)
                {
                    Trace($"reconcile-out p={p.X:F0},{p.Y:F0} size={size.X:F0}x{size.Y:F0}");
                    Leave();
                }
            }
            catch { /* 元素已经离树：换页那条收回路径会接手 */ }
        }

        private void Attach()
        {
            Detach();
            // 构造函数跑在 XAML 装配期，那时候 Window.Page 还没接上、Shell.Current 是 null，
            // 导航扫不到这颗钮（标题页的「再续情缘」正是这样漏掉的：离开首页那一次没人收回）。
            // 处理器挂上来时窗口一定已经在，这里再挂一次；同一个壳只接一遍。
            HookNavigationSweep();
            if (_e.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement native)
            {
                HookPointerReconcile(null);   // 元素还没落进树：先用窗口根兜底
                return;
            }
            _native = native;
            HookPointerReconcile(native);
            _onEnter = (_, _) => { Trace("enter"); SetInside(true); Capture(); WipeIn(); SetHand(true); Raise(true); };
            // 离开不硬切：把已经涂上去的那一段按原路收回来。指针又滑回来时，WipeIn 从收回到的位置
            // 接着往满格走，而不是从头再擦一遍 —— 「顺势滑过去」来回蹭两下，看到的是一次连续的动作。
            _onExit = (_, _) =>
            {
                Trace("exit");
                SetInside(false);
                _pressed = false;
                WipeOut();
                ResetNativeStateIfPressed();
                SetHand(false);
                Raise(false);
            };
            _onDown = (_, _) => { _pressed = true; Paint(); Raise(false); };
            // 按下与悬停写的是同一个 TranslationY：抬起后如果指针还在，要把上浮补回来，
            // 否则卡片按过一次就瘫在原地，读起来像它被按坏了。
            _onUp = (_, _) => { _pressed = false; Paint(); Raise(_inside); };
            // 指针被系统收走（拖出窗口、弹出模态）时不会有抬起，也没有离开 —— 不补这一下会留下一行永远亮着的底。
            _onCancel = (_, _) => Leave();
            Add(native, Microsoft.UI.Xaml.UIElement.PointerEnteredEvent, _onEnter);
            Add(native, Microsoft.UI.Xaml.UIElement.PointerExitedEvent, _onExit);
            Add(native, Microsoft.UI.Xaml.UIElement.PointerPressedEvent, _onDown);
            Add(native, Microsoft.UI.Xaml.UIElement.PointerReleasedEvent, _onUp);
            Add(native, Microsoft.UI.Xaml.UIElement.PointerCanceledEvent, _onCancel);
            ResetNativeStateIfPressed();
        }

        /// <summary>
        /// 只在模板确实停在 Pressed 时才收回。带导航的钮在按下时页面就切走，抬起事件不再送达，
        /// 模板会一直停在 Pressed；悬停/按下的颜色是我们自己在画，这一下只清模板留下的那一档。
        /// 平时**不**强推 Normal：那会把 ContentPresenter 的背景过渡掐在半路，离开那一下就成了硬切，
        /// 和「悬停收起、不是强制清空」正好相反。禁用态不碰：那颗钮此刻本来就该是灰的。
        /// </summary>
        private void ResetNativeStateIfPressed()
        {
            if (_native is not Microsoft.UI.Xaml.Controls.Control c || !c.IsEnabled) return;
            try
            {
                if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(c) == 0) return;
                if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(c, 0)
                    is not Microsoft.UI.Xaml.FrameworkElement root) return;
                foreach (var group in Microsoft.UI.Xaml.VisualStateManager.GetVisualStateGroups(root))
                    if (group.Name == "CommonStates" && group.CurrentState is { Name: "Pressed" })
                    {
                        Microsoft.UI.Xaml.VisualStateManager.GoToState(c, "Normal", false);
                        return;
                    }
            }
            catch { /* 树还没搭好：这一次没有可清的 */ }
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
            SetInside(false);
            _pressed = false;
            Restore();
            SetHand(false);
            Raise(false);
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
            if (face is null) { _skip = true; NoteNoFace(); return; }
            (_get, _set, _ownFace, _faceKind) = face.Value;
            // 控件（Button / ImageButton）的底可能画在模板子面上（④ 的 Border），
            // 但「静止该是什么颜色」只有 MAUI 那一侧答得准 —— 按钮的 Background / BackgroundColor
            // 是它自己写下的，模板子面只是照着它画。Border 不在此列：它的 ④⑦ 面可能属于别的元素
            // （图标底板、外层卡壳），拿自己的底去还它会把别人的颜色抹掉。
            _formsWins = _ownFace || _native is Microsoft.UI.Xaml.Controls.Control;
            _accent = GetAccent(_e);
            _rest = _get();
            // 这块面在我们动手之前压根没有画刷（全透明的底 MAUI 不往原生上写）。
            // 记下这一笔，离开时好把 null 原样还回去，而不是替 MAUI 留一颗它自己没写过的画刷。
            _blank = _rest is null;
            // 静止那档以 MAUI 那一侧为准（理由写在 FormsRest 上）：原生面此刻可能已经被
            // 按钮自己的视觉状态涂上了预备色，拿它当基准就等于把预备色还成新的静止值。
            if (FormsRest() is { } forms) _rest = Solid(forms);
            // 渐变/贴图之类的底不碰：纯色盖上去会把它的底纹整个抹掉。
            if (_rest is not null and not Microsoft.UI.Xaml.Media.SolidColorBrush) { _skip = true; return; }
            _face = (_rest as Microsoft.UI.Xaml.Media.SolidColorBrush) is { } solid
                ? ToMaui(solid.Color)
                : Colors.Transparent;
            Trace($"capture {_faceKind} rest={Describe(_rest)} forms={Describe(FormsRest())}"
                + $" own={_ownFace} blank={_blank}");
        }

        /// <summary>
        /// 静止那一档到底多深——**问 MAUI 那一侧的属性**，不问原生面此刻的画刷。
        /// 原生面此刻的值不可信：WinUI 上按钮的视觉状态和 HoverFeel 抢的是同一块底，
        /// 指针一进它就可能已经把「预备色」写上去了（✕ 悬停一次就永久亮着的正凶——
        /// 那份脏值一旦被当成基准交还，从此每一轮悬停都在把预备色还原成静止色）。
        /// 另外，悬停途中换风格世界时，DynamicResource 会把新世界的画刷推进这条属性，
        /// 所以离开时重算一遍比拿缓存那份准得多。
        /// 答不上来（没写底、底是渐变或贴图、画的是子级那张面）就返回 null，让调用方退回缓存。
        /// </summary>
        private Color? FormsRest()
        {
            if (!_formsWins) return null;
            // 画刷档优先：样式里写 Background="{DynamicResource …}" 的行就吃这一支。
            // 注意 Brush.Default（没设过画刷）**不是 null**：它是 MAUI 的一颗空画刷占位，
            // `is { }` 会把它当成「设过」，于是每颗只写 BackgroundColor 的按钮都答不上来 ——
            // 全站的 BtnSignal / BtnPrimary / BtnText 正是这么写的，它们就是「悬停后不还原」的那一批。
            if (_e.Background is SolidColorBrush solid) return solid.Color;
            if (_e.Background is { } brush && !ReferenceEquals(brush, Brush.Default)) return null;
            // 没有画刷才问颜色档。走 GetValue 而不是属性：那一位的默认值是 null，
            // 直接取属性拿到的是一颗装箱前的 struct，判不了「到底设过没有」。
            return _e.GetValue(VisualElement.BackgroundColorProperty) is Color c ? c : null;
        }

        private static Microsoft.UI.Xaml.Media.SolidColorBrush Solid(Color c) =>
            new(Windows.UI.Color.FromArgb(ToByte(c.Alpha), ToByte(c.Red),
                ToByte(c.Green), ToByte(c.Blue)));

        private static Color ToMaui(Windows.UI.Color c) =>
            new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

        private static byte ToByte(float v) => (byte)Math.Clamp(v * 255f + 0.5f, 0f, 255f);

        /// <summary>诊断日志里的颜色写法（#AARRGGBB），两侧各一套单位在这里统一。</summary>
        private static string Describe(Color? c) => c is null ? "null" : c.ToArgbHex();

        private static string Describe(Microsoft.UI.Xaml.Media.Brush? b) => b switch
        {
            null => "null",
            Microsoft.UI.Xaml.Media.SolidColorBrush s =>
                $"#{s.Color.A:X2}{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}",
            _ => b.GetType().Name,
        };

        /// <summary>
        /// 找这张元素自己的那层面，从「谁在画」往下找，顺序是「谁在画就压谁」：
        /// ① 原生视图本身是 Border（MAUI 的 Border 直接落到一颗 Border）；
        /// ② 原生视图本身是 Control（Button / ImageButton）；
        /// ③ 原生视图本身是铺了**看得见**的底的 Panel（页眉条、整块卡头）；
        /// ④ 唯一的子级 Border——MAUI 常在外面再包一层 ContentPanel，真正带圆角在画的是里面那颗
        ///    （PhonePage 的桌面图标：竖排本身透明，有底的是那块 58px 的图标）；
        /// ⑤⑥⑦ 还没有人画过底的那几颗面（兜底，理由单写在下面）。
        ///
        /// ⑤⑥⑦ 是修「主页四行悬停只有手型光标」的兜底三层：画刷还不存在的那几颗面。
        /// 那四行是 <c>TappableRow</c>，样式里写的是 <c>Background="Transparent"</c>，
        /// 而 MAUI 对全透明的底**根本不往原生上写画刷**，于是 ①②③④ 全数落空、整条 bail：
        /// 指针事件确实在跑（手型就是同一个 handler 里换的），就是不画任何东西。
        /// 三层按「离这个元素自己有多近」排：⑤ 自己这颗 Border/Control；⑥ 唯一一颗空着的子级 Border；
        /// ⑦ 外面那颗 Child 正是我们这层的 Border（MAUI 的 Border 把内容面板塞成一颗 Border 的 Child，
        /// 那颗 Border 就是这个元素的原生壳）。⑦ 的判据是 Child 的引用相等，不然会把外层整张卡一起涂黑。
        /// 交还时按 _blank 写回 null——静止那档本来什么都没画过，就别替 MAUI 留一颗画刷。
        ///
        /// ② 这一档是全站按钮「鼠标移上去毫无变化」的正凶：MAUI Button 的原生视图是
        /// MauiButton : Control，既不是 Border 也不是 Panel，过去三层全数落空 → 按钮只有 PressFeel
        /// 那 1.6px 下沉，悬停一点反馈都不给。Control.Background 是模板里的 ContentBorder 用
        /// TemplateBinding 接着的，写控制这一层就等于写在真正画圆角的那块底上。
        ///
        /// 三条是挡回归的：
        /// · 子级要「唯一」才敢用。全屏遮罩（AffectionLevelUp 那种「点任何地方关掉」）根 Grid 是透明的，
        ///   底下摆着好几张卡，蒙对哪张都不对——鼠标一进全屏变深，那是故障不是反馈。
        /// · Panel 那档要 Alpha 够大才算：透明 Panel 铺的是方角，压上去会把圆角卡盖出一圈直角。
        /// · 再往里（孙辈）不找，那里已经是别人的内容。
        /// </summary>
        private static (Func<Microsoft.UI.Xaml.Media.Brush>, Action<Microsoft.UI.Xaml.Media.Brush>,
            bool, string)? FindFace(Microsoft.UI.Xaml.UIElement root)
        {
            // 前三档画的就是这个元素自己写下的底，所以静止值问 MAUI 那一侧要得回来；
            // 最后一档画的是包在外面那张子级 Border，属性对不上它，只能退回进悬停时抓的那份。
            if (BorderFace(root) is { } own) return (own.Item1, own.Item2, true, "①Border");
            if (ControlFace(root) is { } btn) return (btn.Item1, btn.Item2, true, "②Control");
            if (PanelFace(root) is { } plate) return (plate.Item1, plate.Item2, true, "③Panel");
            // ③.5：MAUI 的 Border 在 Windows 上落成 ContentPanel（MauiPanel），**不是**原生 Border，
            // ① 永远够不着它；而透明底的行（TappableRow 就是）连 ③ 的 alpha 都不够。
            // perf.log 里那条「ContentPanel 上找不到可画的面」就是主场那四行本身。
            if (ContentFace(root) is { } content) return (content.Item1, content.Item2, true, "③.5Content");

            (Func<Microsoft.UI.Xaml.Media.Brush>, Action<Microsoft.UI.Xaml.Media.Brush>)? only = null;
            (Func<Microsoft.UI.Xaml.Media.Brush>, Action<Microsoft.UI.Xaml.Media.Brush>)? bareChild = null;
            var painted = 0;
            var bare = 0;
            var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i)
                    is not Microsoft.UI.Xaml.Controls.Border b) continue;
                // 有底的算「谁在画」，没底的只当备胎（⑥）；两类各自数到第二颗就都不用了——
                // 蒙对哪张都不对（全屏遮罩那种透明 Grid 底下摆着好几张卡）。
                if (b.Background is not null) { painted++; only = FaceOf(b); }
                else { bare++; bareChild = FaceOf(b); }
            }
            if (painted == 1 && only is { } c) return (c.Item1, c.Item2, false, "④ChildBorder");

            // ⑤ 走到这里说明这块面上连画刷都没被写过。主页那四行就是这么没的：
            // TappableRow 写的是 Background="Transparent"，MAUI 对全透明的底**根本不往原生上写画刷**，
            // 于是 ① 的 <c>Background: not null</c> 落空、②③ 既不是控件也不是铺了底的面板、
            // ④ 数不出唯一带底的子 Border（行里只有 Label）——四层全数落空，整条直接 bail：
            // 指针事件确实在跑（手型就是同一个 handler 里换的），就是不画任何东西。
            // 兜底按「离这层元素自己的属性有多近」排：自己 → 唯一一个空着的子 Border → 外面那颗正是本元素外壳的 Border。
            if (root is Microsoft.UI.Xaml.Controls.Border ownBorder)
            {
                var self = FaceOf(ownBorder);
                return (self.Item1, self.Item2, true, "⑤SelfBorder");
            }
            if (root is Microsoft.UI.Xaml.Controls.Control ownCtl)
                return (() => ownCtl.Background, value => ownCtl.Background = value, true, "⑤SelfControl");
            if (bare == 1 && bareChild is { } b2) return (b2.Item1, b2.Item2, false, "⑥BareChild");
            // ⑦ MAUI 的 Border 把自己的内容面板塞成一颗 Border 的 Child，那颗 Border 就是这个元素的原生壳。
            // 只认 Child 正是我们这一层的那颗，否则会把外层那张卡一起涂黑。
            if (ParentOf(root) is Microsoft.UI.Xaml.Controls.Border shell && ReferenceEquals(shell.Child, root))
                return (FaceOf(shell).Item1, FaceOf(shell).Item2, false, "⑦ShellBorder");
            return null;
        }

        private static (Func<Microsoft.UI.Xaml.Media.Brush>, Action<Microsoft.UI.Xaml.Media.Brush>) FaceOf(
            Microsoft.UI.Xaml.Controls.Border b) => (() => b.Background, value => b.Background = value);

        /// <summary>往上找一层。已经是树顶时 WinUI 会抛而不是返回 null，这里只想知道「有没有壳」，吞掉即可。</summary>
        private static Microsoft.UI.Xaml.DependencyObject? ParentOf(Microsoft.UI.Xaml.UIElement vo)
        {
            try { return Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(vo); }
            catch { return null; }
        }

        /// <summary>
        /// 找不到可画的面时留一行日志（同一个原生类型只报一次，不然是每次悬停一条）。
        /// 这一条存在的意义：手型换了说明事件在跑，「就是不画」这件事只有树形能解释。
        /// </summary>
        private static readonly HashSet<string> NoFaceLogged = new();

        private void NoteNoFace()
        {
            if (_native is not { } native) return;
            var name = native.GetType().Name;
            if (!NoFaceLogged.Add(name)) return;
            var children = "";
            for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(native) && i < 4; i++)
                children += (i > 0 ? "/" : "") + Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(native, i).GetType().Name;
            var detail = $"[hover] {name} 上找不到可画的面，子级：{(children.Length > 0 ? children : "无")}"
                + $"（父级：{(ParentOf(native) is { } p ? p.GetType().Name : "无")}）";
            WarmAsBefore.App.WriteLog(detail);
            // warm_startup.log 在有机器上根本不会出现（Documents 被重定向时），
            // 而这条诊断是「为什么没有悬停」唯一的证据 —— 再记一份到数据根下的 perf.log。
            WarmAsBefore.Services.PerfLog.Note(detail);
        }

        private static (Func<Microsoft.UI.Xaml.Media.Brush>, Action<Microsoft.UI.Xaml.Media.Brush>)? BorderFace(
            Microsoft.UI.Xaml.DependencyObject vo) =>
            vo is Microsoft.UI.Xaml.Controls.Border { Background: not null } b
                ? (() => b.Background, value => b.Background = value)
                : null;

        /// <summary>
        /// MAUI Border 的原生面（ContentPanel）。有底的那档归 ③，这里只接透明底：
        /// 描边那颗 <c>BorderPath</c> 就是这个 Border 的圆角形状，而且排在内容前面（在下层），
        /// 填它的 Fill 等于把整行连圆角一起涂上，不会像铺面板底那样把四角涂成直角。
        /// 没有描边（SettingRow 这种方角行）才退回铺面板自己的底。
        /// </summary>
        private static (Func<Microsoft.UI.Xaml.Media.Brush>, Action<Microsoft.UI.Xaml.Media.Brush>)? ContentFace(
            Microsoft.UI.Xaml.DependencyObject vo)
        {
            if (vo is not Microsoft.Maui.Platform.ContentPanel panel) return null;
            if (panel.Background is Microsoft.UI.Xaml.Media.SolidColorBrush { Color.A: > 5 }) return null;
            // 有描边几何才敢填那颗 Path：没描边的 Border 里它照样在树上，只是没有形状，
            // 填进去等于什么都没画（那四行就是无描边的方角行，得走面板自己那层底）。
            // BorderPath 是 internal，只能在树里认这颗直接子级 Path。
            if (StrokePath(panel) is { } path) return (() => path.Fill, value => path.Fill = value);
            return (() => panel.Background, value => panel.Background = value);
        }

        /// <summary>
        /// Border 描边那颗 Path（ContentPanel 的第一个子级）。内容里的图标 Path 在内容面板里面，
        /// 不会出现在这一层，所以直接子级里带几何的那颗只可能是描边形状。
        /// </summary>
        private static Microsoft.UI.Xaml.Shapes.Path? StrokePath(Microsoft.UI.Xaml.UIElement panel)
        {
            var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(panel);
            for (var i = 0; i < count; i++)
            {
                if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(panel, i)
                    is Microsoft.UI.Xaml.Shapes.Path { Data: not null } p
                    && p.ActualWidth > 1 && p.ActualHeight > 1)
                    return p;
            }
            return null;
        }

        private static (Func<Microsoft.UI.Xaml.Media.Brush>, Action<Microsoft.UI.Xaml.Media.Brush>)? ControlFace(
            Microsoft.UI.Xaml.DependencyObject vo) =>
            vo is Microsoft.UI.Xaml.Controls.Control { Background: not null } c
                ? (() => c.Background, value => c.Background = value)
                : null;

        private static (Func<Microsoft.UI.Xaml.Media.Brush>, Action<Microsoft.UI.Xaml.Media.Brush>)? PanelFace(
            Microsoft.UI.Xaml.DependencyObject vo) =>
            vo is Microsoft.UI.Xaml.Controls.Panel
                { Background: Microsoft.UI.Xaml.Media.SolidColorBrush { Color: { A: > 5 } } } p
                ? (() => p.Background, value => p.Background = value)
                : null;

        /// <summary>
        /// 强调底擦进来：开一帧一帧的定时器把进度推到 1，推到头就停表，之后静止那一档是一颗纯色画刷。
        ///
        /// 为什么用原生 DispatcherTimer 而不是 MAUI 动画：要换的输出量是**原生画刷**，
        /// MAUI 的动画系统里没有任何一条 API 认它（有动画可写的只有 Opacity/位移/缩放那几样，
        /// 前两条已经被进场和按压占用，缩放在这套材质上禁用）。
        ///
        /// 离开时按原路收回去（<see cref="WipeOut"/>），不是硬切：用户的原话是
        /// 「不应该直接强制清空，而是让其往回收，如果又过去了，还可以再往回收的基础上再去延展」。
        /// 进和出共用这一台表，只有 <see cref="_wipeTarget"/> 换方向；指针在回收途中滑回来，
        /// 就从当前那一段接着往满格推，不会重新从零擦一遍。
        /// 只有「收干净」或「从来没画过」才交还静止值（见 <see cref="Restore"/>）。
        /// </summary>
        private void WipeIn()
        {
            if (!_accent || MotionTokens.Disabled || _set is null || _skip) { _wipeP = 1; Paint(); return; }
            // 从零开始才给起手那三分之一；收了一半又滑回来，就从那一半接着往满格走。
            if (_wipeP <= 0.02) _wipeP = WipeHeadStart;
            _wipeTarget = 1;
            StartWipe();
            Paint();
        }

        /// <summary>离开时按原路回收。收干净了才交还静止值——半路交还就是「硬切」，用户看到的正是那一刀。</summary>
        private void WipeOut()
        {
            if (!_accent || MotionTokens.Disabled || _set is null || _skip || _wipeP <= 0.001)
            {
                Restore();
                return;
            }
            _wipeTarget = 0;
            StartWipe();
            Paint();
        }

        /// <summary>
        /// 一台表管两个方向：进也用它、收也用它，改方向只是改 <see cref="_wipeTarget"/>。
        /// 按真实时钟推进，不按「每 tick 加一帧」：DispatcherTimer 在 WinUI 上常隔 20~30ms
        /// 才跳一次，加法制会把 80ms 拖成两三百毫秒——这正是「滑过去时动画来不及反应」的另一半。
        /// </summary>
        private void StartWipe()
        {
            if (_tick is not null) return;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var last = 0d;
            var t = new Microsoft.UI.Xaml.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(12) };
            t.Tick += (_, _) =>
            {
                var now = clock.Elapsed.TotalMilliseconds;
                var step = (now - last) / WipeMs;
                last = now;
                if (_wipeTarget > _wipeP)
                {
                    _wipeP = Math.Min(_wipeTarget, _wipeP + step);
                    if (_wipeP >= 1) StopWipe();
                    Paint();
                    return;
                }
                _wipeP = Math.Max(_wipeTarget, _wipeP - step);
                if (_wipeP <= 0)
                {
                    StopWipe();
                    if (!_inside && !_pressed) Restore();
                    return;
                }
                Paint();
            };
            _tick = t;
            t.Start();
        }

        private void StopWipe()
        {
            _tick?.Stop();
            _tick = null;
        }

        private void Paint()
        {
            if (_skip || _set is null) return;
            // 回收途中指针已经不在里面了，但这一帧仍然要画：硬切那一下就是在这里发生的。
            if (!_inside && !_pressed && _wipeP <= 0) { Restore(); return; }
            // 颜色只有静止与悬停两级；_pressed 不再改颜色（按下的下沉归 PressFeel）。
            var step = HoverStep;
            // 强调档（开了 HoverFeel.Accent 的行和文字钮走这条）。这里**不再**直接铺 AccentWarm：
            // 那是填充档，浅色世界里 L≈0.80，铺在白卡上 1.1:1 —— 涂是涂了，眼睛读到的是没变。
            // 现在铺的是「强调色相 + 墨水明度」的半透明覆盖：色相让人读得出「这块是热的」，
            // 明度让人读得出「这块被点到了」。只换填充、不扫文字颜色（覆盖掉不下字色对比）。
            var accent = _accent ? AccentInk(AccentHoverAlpha) : null;
            if (accent is { } a)
            {
                // 擦入没走完就铺一颗硬边两段渐变：右边那段是全透明，读起来就是「这块正在被涂上来」。
                // 到头之后换回纯色画刷——静止那一档不该留一颗渐变，别的样式再往里写东西时好认也好还。
                _painted = _wipeP >= 1
                    ? Solid(a)
                    : WipeBrush(a, MotionTokens.Mech(_wipeP));
                _set(_painted);
                return;
            }
            // 沉底那一档：Alpha 大于 0 说明「这里本来就有一张面」，在它身上沉（Step 保留 Alpha，玻璃还是玻璃）；
            // 完全透明才改成往父面上压一层色偏。
            var color = _face.Alpha > 0.02f
                ? StyleWorldTokens.Step(_face, step)
                : StyleWorldTokens.Wash(step);
            _painted = Solid(color);
            _set(_painted);
        }

        /// <summary>硬边擦入画刷：0..frac 是强调色，frac..1 全透明，中间不设过渡带（软边读起来是「渐显」不是「擦」）。</summary>
        private static Microsoft.UI.Xaml.Media.Brush WipeBrush(Color c, double frac)
        {
            var f = (float)Math.Clamp(frac, 0, 1);
            var brush = new Microsoft.UI.Xaml.Media.LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 0),
                MappingMode = Microsoft.UI.Xaml.Media.BrushMappingMode.RelativeToBoundingBox
            };
            var ink = Windows.UI.Color.FromArgb(ToByte(c.Alpha), ToByte(c.Red), ToByte(c.Green), ToByte(c.Blue));
            brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = ink, Offset = 0 });
            brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop { Color = ink, Offset = f });
            brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop
            { Color = Windows.UI.Color.FromArgb(0, 0, 0, 0), Offset = f });
            brush.GradientStops.Add(new Microsoft.UI.Xaml.Media.GradientStop
            { Color = Windows.UI.Color.FromArgb(0, 0, 0, 0), Offset = 1 });
            return brush;
        }

        /// <summary>
        /// 这一档要铺的那颗色。色相取世界现发的 AccentWarm（换世界跟着走），明度取 Wash 的方向色
        /// （浅档是墨、深档是纸）—— 因为 AccentWarm 自己只适合作**填充**，浅色世界里它比地面还亮，
        /// 当覆盖色用就是「鼠标移上去什么也没发生」。往方向色推 0.55：色相留住，明度落到读得出那一侧。
        /// 世界还没发令牌就返回 null，调用方退回沉一档那条路。
        /// </summary>
        private static Color? AccentInk(double alpha)
        {
            if (Application.Current?.Resources.TryGetValue("AccentWarm", out var v) != true || v is not Color accent)
                return null;
            var dir = StyleWorldTokens.Wash(1);
            const float toward = 0.55f;
            return new Color(
                accent.Red + (dir.Red - accent.Red) * toward,
                accent.Green + (dir.Green - accent.Green) * toward,
                accent.Blue + (dir.Blue - accent.Blue) * toward,
                (float)alpha);
        }

        /// <summary>
        /// 交还静止那一档：每次离开都**重新算一遍**（FormsRest），算不出来才退回进悬停时抓的那份。
        ///
        /// 以前这里写的是「只有这块底还是我涂进去的那颗，才还回去」——用引用相等判断有没有别人插手动过。
        /// 那道闸看着谨慎，实际就是「鼠标过去一次，按键卡在要被点击的那一档」的正凶：MAUI 在悬停期间
        /// 只要重新映射一次（量测、样式重套、它自己的视觉状态切换），就会把它那份 SolidColorBrush **实例**
        /// 写进同一块底，引用一对不上我们就再也不还了，那一档从此长在脸上。
        /// 现在一律交还静止值——我们顶多把底交回它本来该有的颜色，不可能交回别人涂的预备色，
        /// 而静止值是从 MAUI 那一侧现算的，主题中途换过也接得回来。
        /// </summary>
        private void Restore()
        {
            StopWipe();
            if (_painted is not null && _set is not null)
            {
                var now = FormsRest();
                // 静止那一档本来没人画过底（⑤⑥⑦ 那三层：全透明的底 MAUI 不往原生上写画刷）
                // → 交还 null，等于什么都没画过；留一颗 Solid(Transparent) 看着一样，
                // 但那是我们替 MAUI 写下去的本地值，它自己的映射以后未必还会来覆盖。
                Microsoft.UI.Xaml.Media.Brush? rest = now is null ? _rest
                    : _blank && now.Alpha <= 0.001f ? null
                    : Solid(now);
                Trace($"restore now={Describe(now)} wrote={Describe(rest)}"
                    + $" painted={Describe(_painted)} blank={_blank}");
                _set(rest!);
            }
            _get = null;
            _set = null;
            _rest = null;
            _painted = null;
            _face = Colors.Transparent;
            _skip = false;
            _blank = false;
            _accent = false;
            _ownFace = false;
            _formsWins = false;
            _faceKind = "";
            _wipeP = 0;
        }

        /// <summary>
        /// 悬停上浮（<see cref="HoverFeel.LiftProperty"/> 给了像素才动）。
        /// 只用位移不用缩放（理由见类型注释），并走和 PressFeel 同一个输出量 TranslationY：
        /// 两边都写 0/上浮/下沉这三档，靠同名提交的抢占特性接力，不会排队追着鼠标抖。
        /// </summary>
        private void Raise(bool on)
        {
            var px = GetLift(_e);
            if (px <= 0 || _e.Handler is null) return;
            if (MotionTokens.Disabled) { _e.TranslationY = on ? -px : 0; return; }
            _ = _e.TranslateTo(0, on ? -px : 0, (uint)MotionTokens.Ms(MotionTokens.SettleMs),
                new Easing(on ? MotionTokens.OutCubic : MotionTokens.Settle));
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
