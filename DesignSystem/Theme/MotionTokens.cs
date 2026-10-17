namespace WarmAsBefore.DesignSystem.Theme;

/// <summary>
/// 动效令牌：全站「多久、走什么曲线、沉多少像素」的唯一来源。纯数值，不引用 MAUI 类型，
/// 因此能被离线逻辑自检直接编译校验（手感这种东西，至少要能保证不跑偏）。
///
/// 按下与抬起故意用不同时长：手指碰到屏幕时人要的是「确认按到了」，80ms 内必须有变化，
/// 所以进得短；抬起后的回弹是材料的余韵，慢一点才显得软。反过来（进慢出快）就会像隔着棉絮点按钮。
///
/// 按压反馈只用位移，刻意不碰两样东西：
///   · 缩放——WinUI 下按压缩放会触发布局重算，快速连点时按钮会「卡住」，这个坑在 ButtonShapes.xaml 的历史注释里记着。
///   · 不透明度——它同时是进场动画、禁用态和卡片显隐的输出量，驱动再乘一层就会跟它们抢，
///     抢输的那次赋值能把按钮留在半透明上，看起来像「莫名其妙消失」。压暗交给背景的色阶去做。
/// </summary>
public static class MotionTokens
{
    /// <summary>按下（短、果断）。</summary>
    public const int TapMs = 70;
    /// <summary>抬起（阻尼回弹收敛）。</summary>
    public const int SettleMs = 170;
    /// <summary>元素进场（上浮）。</summary>
    public const int EnterMs = 220;
    /// <summary>设置子页逐张建卡的浮起时长（曾实测：再长就开始显得拖沓）。</summary>
    public const int CardEnterMs = 130;
    /// <summary>主题切换后的整页过场。</summary>
    public const int PageFadeMs = 200;
    /// <summary>多个元素依次进场的间隔。</summary>
    public const int StaggerMs = 45;
    /// <summary>帧驱动的节奏，约 60fps。</summary>
    public const int FrameMs = 16;

    /// <summary>按下时下沉的像素。超过 2px 就不像「材料被压下去」，而像按钮在跳。</summary>
    public const double SinkPx = 1.6;
    /// <summary>进场时的上浮距离。</summary>
    public const double EnterRisePx = 6;

    /// <summary>
    /// 关闭过渡：系统「减少动画」偏好或环境变量 WARM_NO_MOTION=1。
    /// 关掉之后状态照样变（按下仍会沉到目标值），只是不再补中间帧——
    /// 前庭功能敏感的用户要的是「别动」，不是「没有反馈」。
    /// </summary>
    public static bool Disabled { get; set; } = Environment.GetEnvironmentVariable("WARM_NO_MOTION") == "1";

    /// <summary>时长出口：禁用动效时一律 0ms（调用处直接落到终值）。</summary>
    public static int Ms(int duration) => Disabled ? 0 : Math.Max(0, duration);

    /// <summary>快出：先冲到位再收，用于按下、状态浮起这类「要有回应」的过渡。</summary>
    public static double OutCubic(double t)
    {
        var u = 1 - Clamp01(t);
        return 1 - u * u * u;
    }

    /// <summary>
    /// 阻尼回弹：末段约 5% 的轻微过冲后收敛到 1，用于松手后的回位。
    /// 过冲压得很小是因为这是软玻璃材质，不是橡皮——弹一下是余韵，弹两下就廉价了。
    /// </summary>
    public static double Settle(double t)
    {
        var x = Clamp01(t);
        return x >= 1 ? 1 : 1 - Math.Exp(-6 * x) * Math.Cos(6.2 * x);
    }

    /// <summary>曲线取值一律先夹到 0..1，避免调用方传进负数或超界后算出越界的透明度/位移。</summary>
    private static double Clamp01(double t) => t < 0 ? 0 : t > 1 ? 1 : t;
}
