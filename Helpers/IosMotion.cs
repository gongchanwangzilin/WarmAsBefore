using WarmAsBefore.DesignSystem.Theme;

namespace WarmAsBefore.Helpers;

/// <summary>
/// 一套弹簧参数：刚度 k 与阻尼 c。手机端所有「松手之后怎么走」都从这里取，
/// 不再出现 ease-in-out / OutCubic 那种「起点慢、终点也慢」的线性过渡 ——
/// iOS 的手感来自三件事：起步**带着手指当前的速度**、末段**刚好过冲一点**再收住、时长随行程走。
///
/// 曲线是阻尼振子的解析解，不是关键帧拼出来的：
///   u(t) = e^(-ζωt) · [ u0·cos(ωd·t) + ((u'0 + ζω·u0)/ωd)·sin(ωd·t) ]
/// u0 = -1（从起点到终点的归一化位移），u'0 = 松手那一刻的速度（按「每秒走过总行程的几倍」归一化）。
/// 手指甩得越快，u'0 越大，曲线前段冲得越狠、过冲越多 —— 这就是「跟手」在数学上的样子。
/// </summary>
public readonly record struct IosSpring(double Stiffness, double Damping)
{
    private const double MinStiffness = 1.0;

    public double Omega => Math.Sqrt(Math.Max(Stiffness, MinStiffness));

    /// <summary>阻尼比。小于 1 才有回弹；等于 1 是最快的无过冲收敛（iOS 的「不急不抢」大概在 0.75~0.9）。</summary>
    public double Zeta => Damping / (2 * Math.Sqrt(Math.Max(Stiffness, MinStiffness)));

    public double OmegaD => Omega * Math.Sqrt(Math.Max(1 - Zeta * Zeta, 1e-4));

    /// <summary>
    /// 收敛到 0.5% 以内要多久（秒）。上限 0.9s：再慢就不像「松手归位」，像一段过场动画。
    /// </summary>
    public double SettleSeconds => Math.Min(0.9, 5.3 / Math.Max(Omega * Zeta, 0.001));

    /// <summary>
    /// 归一化进度：0 → 1。<paramref name="v0"/> 是松手速度 / 总行程（每秒），0 表示静止松手。
    /// 返回值可以超过 1（过冲），也可以小于 0（速度快到先往回走一点，指反方向甩的时候）。
    /// </summary>
    public double Sample(double t, double v0)
    {
        var x = Math.Clamp(t, 0, 1);
        var seconds = x * SettleSeconds;
        var w = Omega;
        var z = Zeta;
        var u0 = -1.0;
        double u;
        if (z >= 0.999)
        {
            // 临界阻尼：没有过冲，只剩指数收敛（兜底分支，现有预设都不到这里）。
            u = (u0 + (v0 + w * u0) * seconds) * Math.Exp(-w * seconds);
        }
        else
        {
            var wd = Math.Max(OmegaD, 1e-4);
            u = Math.Exp(-z * w * seconds)
                * (u0 * Math.Cos(wd * seconds) + ((v0 + z * w * u0) / wd) * Math.Sin(wd * seconds));
        }
        return 1 + u;
    }

    /// <summary>MAUI 的 Easing 入口：把弹簧交给平台的动画时钟，不自己数帧。</summary>
    public Easing Ease(double v0 = 0)
    {
        var self = this;
        return new Easing(t => self.Sample(t, v0));
    }

    /// <summary>这一趟该走多少毫秒（受全站节奏倍率与「减少动画」开关约束）。</summary>
    public uint Ms() => MotionTokens.Disabled
        ? 0
        : (uint)Math.Max(1, Math.Round(SettleSeconds * 1000 * Math.Max(0.2, MotionTokens.Scale)));
}

/// <summary>
/// 手机端弹簧的四个预设。四个都在 0.75~0.9 阻尼比之间：
/// 再小就「弹两下」（廉价），再大就读不出回弹（那又回到线性过渡了）。
/// </summary>
public static class IosMotion
{
    /// <summary>页 / 列表位移的标准收敛：约 0.4s，末端过冲 1~2%。</summary>
    public static readonly IosSpring Standard = new(220, 26);

    /// <summary>抽屉、弹层：稍软一点，推出来时有一点「被甩到位」的余韵。</summary>
    public static readonly IosSpring Sheet = new(260, 28);

    /// <summary>按下 / 抬手：快、几乎不回弹。按压反馈要的是「立刻」，不是「好看」。</summary>
    public static readonly IosSpring Press = new(900, 46);

    /// <summary>返回、回弹归位：过冲稍多一点点，让「没成功」读起来是被弹簧拉住，而不是撞到墙。</summary>
    public static readonly IosSpring Back = new(240, 23);

    /// <summary>
    /// 把元素位移到 <paramref name="targetX"/>，带上松手速度。
    /// <paramref name="velocity"/> 是 dp/s（右为正）；内部换算成「每秒走过总行程的几倍」，
    /// 归一化后交给 <see cref="IosSpring.Sample"/>。
    /// </summary>
    public static Task SlideToX(VisualElement e, double targetX, double velocity, IosSpring spring)
    {
        if (e is null) return Task.CompletedTask;
        var distance = targetX - e.TranslationX;
        var ms = spring.Ms();
        if (ms == 0 || Math.Abs(distance) < 0.5)
        {
            e.AbortAnimation("TranslateTo");
            e.TranslationX = targetX;
            return Task.CompletedTask;
        }
        var v0 = Math.Clamp(velocity / distance, -8, 8);
        return e.TranslateTo(targetX, e.TranslationY, ms, spring.Ease(v0));
    }

    /// <summary>同 <see cref="SlideToX"/>，走 Y 轴（抽屉从下往上、Toast 之类）。</summary>
    public static Task SlideToY(VisualElement e, double targetY, double velocity, IosSpring spring)
    {
        if (e is null) return Task.CompletedTask;
        var distance = targetY - e.TranslationY;
        var ms = spring.Ms();
        if (ms == 0 || Math.Abs(distance) < 0.5)
        {
            e.AbortAnimation("TranslateTo");
            e.TranslationY = targetY;
            return Task.CompletedTask;
        }
        var v0 = Math.Clamp(velocity / distance, -8, 8);
        return e.TranslateTo(e.TranslationX, targetY, ms, spring.Ease(v0));
    }

    /// <summary>缩放收敛。按下是**直接赋值**（见 IosCellFeedback），这里只负责抬手回弹。</summary>
    public static Task ScaleTo(VisualElement e, double target, IosSpring spring)
    {
        if (e is null) return Task.CompletedTask;
        var ms = spring.Ms();
        if (ms == 0)
        {
            e.AbortAnimation("ScaleTo");
            e.Scale = target;
            return Task.CompletedTask;
        }
        return e.ScaleTo(target, ms, spring.Ease(0));
    }
}

/// <summary>
/// 松手速度：从连续的位移采样里算，取最近几帧的均值再压一点尖峰。
/// 只用 <see cref="Environment.TickCount64"/>，不引 Stopwatch —— 它便宜，且不会随 GC 抖。
/// </summary>
public sealed class DragVelocity
{
    private const double SampleWindowMs = 90;
    private long _lastMs;
    private double _lastPos;
    private double _velocity;

    public void Reset(double position)
    {
        _lastMs = Environment.TickCount64;
        _lastPos = position;
        _velocity = 0;
    }

    /// <summary>喂一个采样，返回当前速度（dp/s）。</summary>
    public double Push(double position)
    {
        var now = Environment.TickCount64;
        var dt = now - _lastMs;
        if (dt <= 0) return _velocity;
        var v = (position - _lastPos) / (dt / 1000.0);
        // 用「最近一段」而不是上一帧：单帧抖动不该决定翻不翻页。
        var blend = Math.Clamp(dt / SampleWindowMs, 0.15, 1.0);
        _velocity += (v - _velocity) * blend;
        _lastMs = now;
        _lastPos = position;
        return _velocity;
    }

    public double Value => _velocity;

    /// <summary>停顿超过一帧以上（手指停住了再松手）速度归零：iOS 的「停住再松」不会翻页。</summary>
    public double Resolve()
    {
        if (Environment.TickCount64 - _lastMs > 110) return 0;
        return _velocity;
    }
}
