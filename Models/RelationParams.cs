namespace WarmAsBefore.Models;

/// <summary>
/// 关系网的「参数模型」：这张网**怎么算**的旋钮，和网里有什么（节点/边）分开存。
///
/// 为什么值得单独调：同样一张网，参数不同就是不同的脑子。
/// · 饱和常数 K 小 → 一点点共现就把联系顶满，这人「想哪儿是哪儿」；K 大 → 得反复遇到才算数，这人较真；
/// · 扇形指数（fan effect，Anderson 1983）越大，一个概念连得越杂就越不容易被单独想起 ——
///   「妈妈」挂 20 条联系时，每条只摊到 1/20 的激活，这是人类检索的真实行为，不是 bug；
/// · 联系半衰期 + 永久成分决定画面：哪些枝条已经淡下去、哪些年深日久还发亮。
///   用的还是 HumanMemoryMath 那条叠加遗忘曲线（R = c + (1−c)·2^(−t/半衰期)），
///   只不过对象从「一件事」换成「一条联系」。
///
/// 界面上的滑块改的就是这几个数，改完立刻重算联想预览 —— 能当场看见「调参换了一个人」。
/// </summary>
public sealed record RelationParams
{
    /// <summary>权重→强度的饱和常数：strength = w / (w + K)。ACT-R 的归一化做法。</summary>
    public double SaturationK { get; init; } = 8.0;

    /// <summary>扇形抑制指数：激活 ÷ 度数^此值。0 = 不抑制（机器），1 = 人类式摊平，&gt;1 = 极易被别的事挤掉。</summary>
    public double FanExponent { get; init; } = 1.0;

    /// <summary>每多走一跳再乘这一档：二跳的联想本来就该比一跳弱。</summary>
    public double HopDamping { get; init; } = 0.55;

    /// <summary>联系半衰期（天）：多久没人碰就只剩一半力气。0 = 不遗忘。</summary>
    public double ForgetHalfLifeDays { get; init; } = 60.0;

    /// <summary>永久成分：衰减到底还剩多少。老记忆不会归零，琐碎联系才会沉底。</summary>
    public double PermanentFraction { get; init; } = 0.30;

    /// <summary>检索阈值：低于这个激活的词根本想不起来（画面里也不点亮）。</summary>
    public double ActivationFloor { get; init; } = 0.02;

    public static RelationParams Default => new();

    /// <summary>滑块能拖出来的范围都在这儿；越界的输入一律夹回去，别让手滑把脑子调没了。</summary>
    public RelationParams Clamped() => this with
    {
        SaturationK = Math.Clamp(SaturationK, 0.5, 60),
        FanExponent = Math.Clamp(FanExponent, 0, 2.5),
        HopDamping = Math.Clamp(HopDamping, 0.05, 1.0),
        ForgetHalfLifeDays = Math.Clamp(ForgetHalfLifeDays, 0, 3650),
        PermanentFraction = Math.Clamp(PermanentFraction, 0, 0.9),
        ActivationFloor = Math.Clamp(ActivationFloor, 0.001, 0.5)
    };

    /// <summary>这条联系**现在**还剩几分力气（0~1）。画枝条的粗细明暗、算扩散都用它。</summary>
    public double Live(double weight, DateTime updatedAt, DateTime now)
    {
        if (weight <= 0) return 0;
        var ageDays = Math.Max(0, (now - updatedAt).TotalDays);
        if (ForgetHalfLifeDays <= 0) return 1;
        var faded = Math.Pow(2.0, -ageDays / ForgetHalfLifeDays);
        return PermanentFraction + (1 - PermanentFraction) * faded;
    }

    /// <summary>权重→强度（饱和后不再往上堆）；再乘上今天还剩的力气。</summary>
    public double Strength(double weight, DateTime updatedAt, DateTime now)
    {
        var s = weight / (weight + SaturationK);
        return s * Live(weight, updatedAt, now);
    }

    /// <summary>扇形抑制：这个概念身上挂了 n 条联系，每条摊到的激活。</summary>
    public double FanPenalty(int degree) =>
        degree <= 1 ? 1.0 : 1.0 / Math.Pow(degree, FanExponent);

    public string Summary =>
        $"饱和 K={SaturationK:0.#}｜扇形^{FanExponent:0.##}｜二跳×{HopDamping:0.##}｜半衰期 {HalfLifeText}｜永久 {PermanentFraction:P0}｜阈值 {ActivationFloor:0.###}";

    private string HalfLifeText => ForgetHalfLifeDays <= 0
        ? "不遗忘"
        : ForgetHalfLifeDays >= 365 ? $"{ForgetHalfLifeDays / 365.0:0.#} 年" : $"{ForgetHalfLifeDays:0.#} 天";
}
