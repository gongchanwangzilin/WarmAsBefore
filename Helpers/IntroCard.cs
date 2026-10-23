using WarmAsBefore.DesignSystem.Theme;

namespace WarmAsBefore.Helpers;

/// <summary>
/// 启动卡：把名牌逐字写出来 → 缩一格 → 从下面引出本地语言的名字。
///
/// 为什么是一张「卡」而不是页面里多出来的一块装饰：这是整趟进程里唯一一次品牌独占整屏，
/// 之后每一屏它都要让位给内容。所以它必须比内容更早、更安静、更短。
///
/// 三条实现约束，都是这库里踩出来的：
///   · **时间就是状态**。整套编排是一个函数 Apply(t)，每一帧把所有属性按 t 重写一遍，
///     没有「上一步的回调里再挂下一步」。丢一帧会被下一帧补回来；丢一整条续接也照样落地，
///     因为还有一个独立的兜底计时器（<see cref="GuardMs"/>）负责把遮罩收掉。
///     动画回调丢失后元素永久留在中间值上，正是「按钮莫名其妙消失」那类 bug 的形状，
///     这里不让它有任何一次发生的机会：最坏是这张卡提前/延后收掉，界面本体完好。
///   · **走 UI 时钟，不自己数帧**。见 PressFeel 里那段实测：Task.Delay(16) 的续接会落到线程池，
///     每帧一次跨线程属性赋值，帧数没多、流畅度反而掉。IDispatcherTimer 回调就在 UI 线程上。
///   · **系统「减少动画」时不演**，直接落到静止的成品卡展示几百毫秒再收（状态照样到位，只是不补中间帧）。
///
/// 缩放（<see cref="VisualElement.Scale"/>）在这里是被请求的东西本身 —— 「缩小引出」没有别的做法。
/// 它能安全成立的理由是：被缩的这一块在遮罩里独占一格、不参与任何其它元素的排版，
/// 而且它是全站唯一一处 Scale 的写者，不会和进场契约或按压反馈抢同一个属性。
/// 同理，「收字距」也做成逐字位移而不是排版属性 —— 见 <see cref="Apply"/> 里那条实测。
/// </summary>
public static class IntroCard
{
    /// <summary>整套编排的名义时长（世界节奏倍率再乘一次）。</summary>
    public const int TotalMs = 2600;

    /// <summary>兜底：编排加退场再加一截余量。到点一定把遮罩收掉，哪怕动画整条断了。</summary>
    private const int GuardMs = TotalMs + 1800;

    /// <summary>减少动画时，成品卡静止展示的时长。</summary>
    private const int StaticHoldMs = 420;

    /// <summary>「全部就位、遮罩还没开始退」的那一刻。静止展示和收场前都落在这里。</summary>
    private const double RestT = 0.86;

    /// <summary>
    /// 一趟进程只演一次。标题页每次导航都是新建的（DI 注册是 Transient），
    /// 而「启动」这件事按进程算，不按页面实例算 —— 从设置页返回时再放一遍开机动画就成了故障。
    /// </summary>
    public static bool HasPlayed { get; private set; }

    /// <summary>遮罩上被编排的那几样。名字由页面交出，这里只按时间给它们赋值。</summary>
    public sealed record Parts(
        VisualElement Curtain,
        VisualElement Band,
        VisualElement Rule,
        VisualElement Lockup,
        VisualElement NameRow,
        VisualElement Hint,
        IReadOnlyList<VisualElement> Chars,
        IReadOnlyList<VisualElement> NameChars);

    private static Parts? _parts;
    private static IDispatcherTimer? _timer;
    private static Action? _finish;
    private static DateTime _started;
    private static int _totalMs = TotalMs;
    private static double _spread;

    /// <summary>开演。<paramref name="finish"/> 负责收掉遮罩（页面里就是那句 ShowIntro = false）。</summary>
    public static void Play(Parts p, Action finish)
    {
        if (HasPlayed) { finish(); return; }
        HasPlayed = true;
        // 首帧之前探测一次系统偏好：以前只有第一个按钮装出来才会触发它，而这张卡比第一个按钮更早。
        MotionPrefs.Apply();
        _parts = p;
        _finish = finish;

        // 收字距的起点按字号给（写死的像素数只在一档世界上成立），再按屏宽夹一次，
        // 免得最开那一帧在手机屏幕上把名字切掉两头。
        // 屏宽这里可能还没量到（卡片刚挂上就来演），退回主屏密度换算，和 App 对尺那处同一个做法。
        var di = DeviceDisplay.MainDisplayInfo;
        var screen = p.Curtain.Width > 64 ? p.Curtain.Width : di.Width / di.Density;
        var size = p.NameChars.Count > 0 && p.NameChars[0] is Label first && first.FontSize > 0 ? first.FontSize : 32;
        var chars = Math.Max(1, p.NameChars.Count);
        _spread = Math.Round(Math.Min(size * 0.34, Math.Max(4, screen / chars * 0.90)), 1);

        Apply(0);
        if (MotionTokens.Disabled || Application.Current?.Dispatcher is null)
        {
            Apply(RestT);
            _ = DropAfter(StaticHoldMs);
            return;
        }

        _totalMs = Math.Max(1, (int)Math.Round(TotalMs * MotionTokens.Scale));
        _started = DateTime.UtcNow;
        _timer = Application.Current.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(8, MotionTokens.FrameMs));
        // 接口没有「重复」这一项：它就一直走到 Stop()。正合意 —— 收场只有一处 Stop。
        _timer.Tick += (_, _) =>
        {
            var t = (DateTime.UtcNow - _started).TotalMilliseconds / _totalMs;
            if (t >= 1) { Finish(); return; }
            Apply(t);
        };
        _timer.Start();
        _ = DropAfter(GuardMs);
    }

    /// <summary>点卡片：立刻收掉。跳过不该把剩下的半截演完。</summary>
    public static void Skip() => Finish();

    private static void Finish()
    {
        if (_parts is null) return;
        _timer?.Stop();
        _timer = null;
        var done = _finish;
        _parts = null;
        _finish = null;
        done?.Invoke();
    }

    private static async Task DropAfter(int ms)
    {
        await Task.Delay(ms);
        if (_parts is null) return;
        await MainThread.InvokeOnMainThreadAsync(Finish);
    }

    // ── 时间轴（全部用归一化比例：世界倍率只乘总时长一处，比例关系就不会被拉开）──────
    //  0.00–0.30  四个字逐个淡入（每字 0.09，间隔按字数摊开）
    //  0.16–0.44  发丝线从左扫到右
    //  0.50–0.74  整组上浮 20px
    //  0.52–0.70  名牌带缩到 0.58
    //  0.56–0.90  本地化名逐字并回中线（= 收字距）
    //  0.58–0.78  它同时淡入
    //  0.88–1.00  整张卡退场
    private static void Apply(double raw)
    {
        if (_parts is not { } p) return;
        var t = Clamp01(raw);

        var n = Math.Max(1, p.Chars.Count);
        const double w = 0.09;
        var step = (0.30 - w) / Math.Max(1, n - 1);
        for (var i = 0; i < p.Chars.Count; i++)
            p.Chars[i].Opacity = Ramp(t, i * step, i * step + w, Out);

        p.Rule.ScaleX = Ramp(t, 0.16, 0.44, Out);
        p.Band.Scale = Lerp(1, 0.58, Ramp(t, 0.52, 0.70, InOut));
        p.Lockup.TranslationY = Lerp(0, -20, Ramp(t, 0.50, 0.74, InOut));

        // 字距是「每个字离中线多远」，不是排版属性：逐字平移，一次布局都不触发。
        // 收字距这件事本身在 WinUI 上不能用 Label.CharacterSpacing —— 实测只有第一次赋值生效，
        // 之后无论改什么都不会重排，那一行字距就从开头到结尾纹丝不动。
        var spread = Lerp(_spread, 0, Ramp(t, 0.56, 0.90, Out));
        var mid = (p.NameChars.Count - 1) / 2.0;
        for (var i = 0; i < p.NameChars.Count; i++)
            p.NameChars[i].TranslationX = (i - mid) * spread;

        p.NameRow.Opacity = Ramp(t, 0.58, 0.78, Out);
        p.Hint.Opacity = Ramp(t, 0.12, 0.24, Out);
        p.Curtain.Opacity = 1 - Ramp(t, 0.88, 1.00, InCubic);
    }

    private static double Ramp(double t, double from, double to, Func<double, double> curve)
    {
        if (to <= from) return t >= to ? 1 : 0;
        return Clamp01((t - from) / (to - from)) switch
        {
            <= 0 => 0,
            >= 1 => 1,
            var u => curve(u)
        };
    }

    private static double Lerp(double a, double b, double u) => a + (b - a) * u;
    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    // 曲线取 MotionTokens 那一族（和按压同一个来源），启动卡不该是全站唯一一套别的节奏。
    private static double Out(double u) => MotionTokens.OutCubic(u);
    private static double InCubic(double u) => u * u * u;
    private static double InOut(double u) => u < 0.5 ? 4 * u * u * u : 1 - Math.Pow(-2 * u + 2, 3) / 2;
}
