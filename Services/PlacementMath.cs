using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 标定的几何与缩放换算。纯静态、无外部依赖，便于单独推演与校验。
///
/// 两套缩放模型的语义：
///
/// **双线**：两条线在三维空间里平行且等长（例子：一条渐行渐远的公路，两侧边缘就是这两条线）。
/// 透视下同一段三维长度投影出来的像素长度 ∝ 1/深度，而角色的可视大小同样 ∝ 1/深度，
/// 所以 **缩放因子 ∝ 两条线在该深度处的间距**。间距最大处（最近）定为 1.0 基准。
///
/// **圆球**：所有示例圆球代表同一个三维半径，所以 **缩放因子 ∝ 球的半径**。
/// 球半径最大的那个（最近）定为 1.0 基准。两个球之间取平均（线性插值），
/// 三个以上用距离反比平方加权。
/// </summary>
public static class PlacementMath
{
    /// <summary>折线重采样段数（采样点 = 段数 + 1）。</summary>
    public const int Segments = 64;

    private const double EpsSphere = 0.0004;

    // ============ 折线采样 ============

    /// <summary>按弧长把折线重采样成等距的 segments+1 个点，使 t = i/segments 有几何意义。</summary>
    public static List<NormPoint> SamplePolyline(NormLine? line, int segments = Segments)
    {
        var result = new List<NormPoint>(segments + 1);
        var pts = line?.Points;
        if (pts is null || pts.Count == 0) return result;

        if (pts.Count == 1)
        {
            for (int i = 0; i <= segments; i++) result.Add(new NormPoint(pts[0].X, pts[0].Y));
            return result;
        }

        var cum = new double[pts.Count];
        for (int i = 1; i < pts.Count; i++)
            cum[i] = cum[i - 1] + Dist(pts[i - 1], pts[i]);

        var total = cum[^1];
        if (total <= 0)
        {
            for (int i = 0; i <= segments; i++) result.Add(new NormPoint(pts[0].X, pts[0].Y));
            return result;
        }

        var seg = 1;
        for (int i = 0; i <= segments; i++)
        {
            var target = total * i / segments;
            while (seg < pts.Count - 1 && cum[seg] < target) seg++;
            var segLen = cum[seg] - cum[seg - 1];
            var f = segLen > 0 ? Math.Clamp((target - cum[seg - 1]) / segLen, 0, 1) : 0;
            result.Add(new NormPoint(
                pts[seg - 1].X + (pts[seg].X - pts[seg - 1].X) * f,
                pts[seg - 1].Y + (pts[seg].Y - pts[seg - 1].Y) * f));
        }
        return result;
    }

    private static double Dist(NormPoint a, NormPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // ============ 双线分析 ============

    /// <summary>双线标定的分析结果（按 t 采样的两条线与逐点间距）。</summary>
    public sealed record DualLineScale(
        List<NormPoint> A, List<NormPoint> B, double[] D, double DRef, double DMin, int TRefIndex);

    /// <summary>
    /// 分析双线：采样 → 自动配对方向 → 逐点间距。
    /// 返回 null 表示两条线不可用（数量不对或无法采样）。
    /// </summary>
    public static DualLineScale? AnalyzeDualLine(ScenePlacement? p, int segments = Segments)
    {
        if (p?.ScaleLines is null || p.ScaleLines.Count != 2) return null;
        var a = SamplePolyline(p.ScaleLines[0], segments);
        var b = SamplePolyline(p.ScaleLines[1], segments);
        if (a.Count != segments + 1 || b.Count != segments + 1) return null;

        // 用户可能把第二条线反着画。按端点配对代价自动翻转：
        // 正序代价 = |A(0)-B(0)| + |A(1)-B(1)|，逆序代价 = |A(0)-B(1)| + |A(1)-B(0)|。
        var costFf = Dist(a[0], b[0]) + Dist(a[^1], b[^1]);
        var costFr = Dist(a[0], b[^1]) + Dist(a[^1], b[0]);
        if (costFr < costFf) b.Reverse();

        var d = new double[segments + 1];
        for (int i = 0; i <= segments; i++) d[i] = Dist(a[i], b[i]);

        var dRef = d.Max();
        var dMin = d.Min();
        var tRef = Array.IndexOf(d, dRef);
        return new DualLineScale(a, b, d, dRef, dMin, tRef);
    }

    /// <summary>双线：位置 P 处的相对缩放（最大间距处 = 1.0）。</summary>
    private static double DualLineRelative(ScenePlacement p, NormPoint at, DualLineScale an)
    {
        if (an.DRef <= 0) return 1.0;
        var best = 0;
        var bestD = double.MaxValue;
        for (int i = 0; i < an.A.Count; i++)
        {
            var mx = (an.A[i].X + an.B[i].X) / 2;
            var my = (an.A[i].Y + an.B[i].Y) / 2;
            var dx = at.X - mx;
            var dy = at.Y - my;
            var dd = dx * dx + dy * dy;
            if (dd < bestD) { bestD = dd; best = i; }
        }
        return an.D[best] / an.DRef;
    }

    // ============ 圆球 ============

    /// <summary>圆球：位置 P 处的相对缩放（最大球半径处 = 1.0）。</summary>
    private static double SphereRelative(ScenePlacement p, NormPoint at)
    {
        var spheres = p.Spheres;
        if (spheres.Count == 0) return 1.0;
        var rRef = spheres.Max(s => s.R);
        if (rRef <= 0) return 1.0;

        if (spheres.Count == 2)
        {
            // 两个球之间线性插值：u = 0.5（两球中点）即严格取平均。
            // u 被 clamp 到 [0,1]，端点之外不外推——避免远离球的位置算出离谱的缩放。
            var c1 = spheres[0];
            var c2 = spheres[1];
            var vx = c2.X - c1.X;
            var vy = c2.Y - c1.Y;
            var len2 = vx * vx + vy * vy;
            var u = len2 > 0 ? ((at.X - c1.X) * vx + (at.Y - c1.Y) * vy) / len2 : 0;
            u = Math.Clamp(u, 0, 1);
            var k1 = c1.R / rRef;
            var k2 = c2.R / rRef;
            return k1 + (k2 - k1) * u;
        }

        // 三个以上：Shepard 距离反比平方加权。
        double num = 0, den = 0;
        var kMin = spheres.Min(s => s.R) / rRef;
        var kMax = spheres.Max(s => s.R) / rRef;
        foreach (var s in spheres)
        {
            var dx = at.X - s.X;
            var dy = at.Y - s.Y;
            var w = 1.0 / (dx * dx + dy * dy + EpsSphere);
            num += w * (s.R / rRef);
            den += w;
        }
        var k = den > 0 ? num / den : 1.0;
        return Math.Clamp(k, kMin, kMax);   // 防过冲
    }

    // ============ 对外：缩放与锚点 ============

    /// <summary>位置 at 处的角色缩放（已含 GlobalFactor，并 clamp 到合理区间）。</summary>
    public static double ScaleAt(ScenePlacement? p, NormPoint at)
    {
        if (p is null) return 1.0;
        double rel;
        if (p.ScaleMethod == ScaleMethods.DualLine)
        {
            var an = AnalyzeDualLine(p);
            rel = an is null ? 1.0 : DualLineRelative(p, at, an);
        }
        else
        {
            rel = SphereRelative(p, at);
        }
        return Math.Clamp(rel * p.GlobalFactor, 0.02, 5.0);
    }

    // 整图可站时的安全区：角色不应跑到画面最上缘或贴死左右边。
    private const double SafeXMin = 0.05;
    private const double SafeXMax = 0.95;
    private const double SafeYMin = 0.45;
    private const double SafeYMax = 0.95;

    /// <summary>折线上距 q 最近的点。</summary>
    public static NormPoint NearestOnLine(NormLine? line, NormPoint q)
    {
        var pts = line?.Points;
        if (pts is null || pts.Count == 0) return new NormPoint(q.X, q.Y);
        if (pts.Count == 1) return new NormPoint(pts[0].X, pts[0].Y);

        var bestD = double.MaxValue;
        var best = new NormPoint(pts[0].X, pts[0].Y);
        for (int i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            var vx = b.X - a.X;
            var vy = b.Y - a.Y;
            var len2 = vx * vx + vy * vy;
            var t = len2 > 0 ? Math.Clamp(((q.X - a.X) * vx + (q.Y - a.Y) * vy) / len2, 0, 1) : 0;
            var px = a.X + vx * t;
            var py = a.Y + vy * t;
            var dx = q.X - px;
            var dy = q.Y - py;
            var dd = dx * dx + dy * dy;
            if (dd < bestD) { bestD = dd; best = new NormPoint(px, py); }
        }
        return best;
    }

    /// <summary>把所有落脚折线上距 q 最近的点再取最近的一个。</summary>
    public static NormPoint ProjectOntoLines(ScenePlacement p, NormPoint q)
    {
        var best = q;
        var bestD = double.MaxValue;
        foreach (var line in p.Lines)
        {
            var cand = NearestOnLine(line, q);
            var dx = cand.X - q.X;
            var dy = cand.Y - q.Y;
            var dd = dx * dx + dy * dy;
            if (dd < bestD) { bestD = dd; best = cand; }
        }
        return best;
    }

    /// <summary>
    /// 由「期望位置」解析出真正的落脚锚点与该处的缩放。
    /// - point  ：锚点固定在 Points[0]，不随期望位置变化
    /// - full   ：锚点 = 期望位置（clamp 进安全区），整图可站
    /// - linear ：锚点 = 期望位置投影到最近的落脚线上（线上任意位置皆可站）
    /// </summary>
    public static (NormPoint Anchor, double K) ResolveAnchor(ScenePlacement? p, NormPoint? desired)
    {
        var want = desired ?? new NormPoint(0.5, 0.9);
        if (p is null) return (want, 1.0);

        switch (p.Landing)
        {
            case LandingKinds.Point:
            {
                var pt = p.Points.Count > 0 ? p.Points[0] : new NormPoint(0.5, 0.9);
                return (new NormPoint(pt.X, pt.Y), ScaleAt(p, pt));
            }
            case LandingKinds.Linear:
            {
                var proj = ProjectOntoLines(p, want);
                return (proj, ScaleAt(p, proj));
            }
            case LandingKinds.Full:
            default:
            {
                var a = new NormPoint(
                    Math.Clamp(want.X, SafeXMin, SafeXMax),
                    Math.Clamp(want.Y, SafeYMin, SafeYMax));
                return (a, ScaleAt(p, a));
            }
        }
    }

    // ============ 校验（结构 + 几何）与摘要 ============

    /// <summary>
    /// 完整校验 = ScenePlacement.Validate() 的结构性检查 + 需要几何计算的判定。
    /// UI 的「完成」按钮闸门与素材包 sidecar 校验都走这里，保证两者口径一致。
    /// </summary>
    public static (bool Ok, List<string> Missing) Validate(ScenePlacement? p)
    {
        if (p is null)
            return (false, new List<string> { "尚未标定" });

        var (_, missing) = p.Validate();

        if (p.ScaleMethod == ScaleMethods.DualLine && p.ScaleLines.Count == 2)
        {
            var an = AnalyzeDualLine(p);
            if (an is null)
            {
                missing.Add("缩放标定未完成：双线的两条线无法采样（请确认每条线至少 2 个点）");
            }
            else if (an.DMin <= 0.005)
            {
                missing.Add("两条缩放线相交或贴在一起：无法判断远近。请像公路两侧那样重画（可以逐渐靠近，但不能交叉）");
            }
            else if (an.DRef / an.DMin < 1.05)
            {
                missing.Add($"两条缩放线间距几乎不变（最大 {an.DRef:0.###} / 最小 {an.DMin:0.###}）：" +
                            "看不出透视关系，推不出缩放。请改成有远近变化的双线，或改用圆球标定");
            }
            else if (an.DRef > 1.5)
            {
                missing.Add("两条缩放线间距过大（可能画到图外了），请检查后再完成");
            }
        }

        return (missing.Count == 0, missing);
    }

    /// <summary>落脚点类型的中文名。</summary>
    public static string LandingName(string landing) => landing switch
    {
        LandingKinds.Point => "点选",
        LandingKinds.Full => "整图可站",
        LandingKinds.Linear => "线性",
        _ => landing
    };

    /// <summary>缩放标定方式的中文名。</summary>
    public static string ScaleName(string method) => method switch
    {
        ScaleMethods.DualLine => "双线",
        ScaleMethods.Spheres => "圆球",
        _ => method
    };

    /// <summary>
    /// 标定的三行强制输出：① 落脚点类型 ② 缩放标定方式 ③ 具体标定数据。
    /// 标定页底部常驻显示，落盘前也用它生成确认摘要。
    /// </summary>
    public static string Describe(ScenePlacement? p)
    {
        if (p is null) return "尚未标定";

        var sb = new System.Text.StringBuilder();
        sb.Append("① 落脚点类型：").Append(LandingName(p.Landing));
        switch (p.Landing)
        {
            case LandingKinds.Point:
                sb.Append($"（{p.Points.Count} 个点）");
                break;
            case LandingKinds.Full:
                sb.Append("（整张图均可站立，无落脚几何）");
                break;
            case LandingKinds.Linear:
                sb.Append($"（{p.Lines.Count} 条线，总长 {p.Lines.Sum(NormLengthSafe):0.###}）");
                break;
        }
        sb.AppendLine();

        sb.Append("② 缩放标定方式：").Append(ScaleName(p.ScaleMethod)).AppendLine();

        sb.Append("③ 标定数据：");
        if (p.ScaleMethod == ScaleMethods.DualLine)
        {
            var an = AnalyzeDualLine(p);
            if (an is null)
                sb.Append("双线数据不可用");
            else
                sb.Append($"最大间距 {an.DRef:0.###}（t={an.TRefIndex / (double)Segments:0.##}）、" +
                          $"最小间距 {an.DMin:0.###}、视差比 {(an.DMin > 0 ? an.DRef / an.DMin : 0):0.##}×，" +
                          $"整体系数 {p.GlobalFactor:0.##}，参考身高占比 {p.RefHeightRatio:0.##}");
        }
        else
        {
            var rRef = p.Spheres.Count > 0 ? p.Spheres.Max(s => s.R) : 0;
            sb.Append($"{p.Spheres.Count} 个球，半径 [{string.Join(", ", p.Spheres.Select(s => s.R.ToString("0.###")))}]，" +
                      $"基准半径 {rRef:0.###}，整体系数 {p.GlobalFactor:0.##}，参考身高占比 {p.RefHeightRatio:0.##}");
        }

        return sb.ToString();
    }

    private static double NormLengthSafe(NormLine l) => ScenePlacement.NormLength(l);
}
