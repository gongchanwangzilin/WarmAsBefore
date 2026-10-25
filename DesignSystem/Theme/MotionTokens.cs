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
    /// <summary>
    /// 弹层展开（角色工作室这种盖住整页的一层）。比页面进场长一档：
    /// 进场是「东西摆上来」，弹层是「把注意力从整屏收到一张卡上」，太快会像被硬拽过去。
    /// </summary>
    public const int SheetMs = 320;
    /// <summary>多个元素依次进场的间隔。</summary>
    public const int StaggerMs = 45;
    /// <summary>帧驱动的节奏，约 60fps。</summary>
    public const int FrameMs = 16;

    /// <summary>
    /// 按下时下沉的像素。原来 1.6px 只在颜色上加深，用户在 125% 缩放下看不到「按动」——
    /// 现在颜色只归悬停，按下这一下必须由位移自己说出来。2.4px 约 3 个物理像素，看得见；
    /// 再超过 3px 就会从「材料被压住」变成「按钮掉了一格」。
    /// </summary>
    public const double SinkPx = 2.4;
    /// <summary>进场时的上浮距离。</summary>
    public const double EnterRisePx = 6;

    /// <summary>
    /// 关闭过渡：系统「减少动画」偏好或环境变量 WARM_NO_MOTION=1。
    /// 关掉之后状态照样变（按下仍会沉到目标值），只是不再补中间帧——
    /// 前庭功能敏感的用户要的是「别动」，不是「没有反馈」。
    /// </summary>
    public static bool Disabled { get; set; } = Environment.GetEnvironmentVariable("WARM_NO_MOTION") == "1";

    /// <summary>
    /// 世界给的节奏倍率与按压行程。换风格如果只换颜色和圆角，切过去像只改了描边——
    /// 「干脆」和「松弛」是材质差别的另一半天：极简沉 1px、70% 时长回来；玻璃沉到近 2px、余韵拖长。
    /// 行程上限锁在 2px：超过它就不像材料被压下去，而像按钮在跳。
    /// </summary>
    public static double Scale { get; set; } = 1.0;
    public static double Press { get; set; } = SinkPx;

    /// <summary>时长出口：禁用动效时一律 0ms（调用处直接落到终值）；否则按世界的节奏缩放。</summary>
    public static int Ms(int duration) =>
        Disabled ? 0 : Math.Max(0, (int)Math.Round(Math.Max(0, duration) * Scale));

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

    /// <summary>
    /// 硬切机械曲线：起得有点迟疑、中段冲到位、末段收干净（终末地那套 mech cubic-bezier(0.7,0,0.2,1) 的多项式近似）。
    /// 快门、擦入、灌满和所有退出走这条；元素「出现」走 <see cref="OutCubic"/>。
    /// 用它是因为这三样都是**机器在动作**，不是软材料在回弹——OutCubic 铺在擦入上会显得黏。
    /// </summary>
    public static double Mech(double t)
    {
        var x = Clamp01(t);
        return x < 0.5 ? 4 * x * x * x : 1 - System.Math.Pow(-2 * x + 2, 3) / 2;
    }

    /// <summary>曲线取值一律先夹到 0..1，避免调用方传进负数或超界后算出越界的透明度/位移。</summary>
    private static double Clamp01(double t) => t < 0 ? 0 : t > 1 ? 1 : t;
}
