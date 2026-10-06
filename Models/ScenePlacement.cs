namespace WarmAsBefore.Models;

/// <summary>落脚点类型：角色可以站立的位置怎么定义。</summary>
public static class LandingKinds
{
    /// <summary>点选：用户点击若干具体位置作为落脚点。</summary>
    public const string Point = "point";
    /// <summary>整图：整张图都可以站立，不需要点落脚点（但缩放标定仍必须做）。</summary>
    public const string Full = "full";
    /// <summary>线性：只有线上可以站立；可画多条线，线上任意位置均可站。</summary>
    public const string Linear = "linear";
}

/// <summary>缩放标定方式：角色近大远小的依据从哪来。</summary>
public static class ScaleMethods
{
    /// <summary>双线：两条在三维空间平行且等长的线（如渐远公路的两侧边缘），由视差间距推缩放。</summary>
    public const string DualLine = "dualLine";
    /// <summary>圆球：示例圆球标定，近处一个远处一个，由球的半径推缩放。</summary>
    public const string Spheres = "spheres";
}

/// <summary>归一化点（原图左上为原点，0..1）。</summary>
public sealed class NormPoint
{
    public double X { get; set; }
    public double Y { get; set; }

    public NormPoint() { }
    public NormPoint(double x, double y) { X = x; Y = y; }
}

/// <summary>归一化折线。</summary>
public sealed class NormLine
{
    public List<NormPoint> Points { get; set; } = new();
    public string Note { get; set; } = "";
}

/// <summary>归一化示例圆球：R 是「半径 ÷ 原图宽」。</summary>
public sealed class NormSphere
{
    public double X { get; set; }
    public double Y { get; set; }
    public double R { get; set; }
}

/// <summary>
/// 单张背景图的标定数据：落脚点 + 缩放依据。
///
/// 坐标一律**归一化到原图**（原点图片左上角，x/y ∈ 0..1），不带任何像素/设备单位。
/// 原因：游戏里背景是 AspectFill（会裁切），而标定页为了划线必须看整图（AspectFit），
/// 两者显示矩形不同，只有归一化原图坐标能跨两种显示模式唯一对应；顺带与 DPR/分辨率无关。
///
/// 强约束（对应「必须标定，不能跳过」）：
///   1. 落脚点类型三选一，且几何数据必须与类型自洽；
///   2. 缩放标定方式二选一，不能为空——只画落脚线不做缩放标定是非法的；
///   3. 标定必须绑定「哪张背景图」（SourceBackground + SourceStamp + 原图尺寸），换图即失效。
///
/// 本类的 Validate() 只做**结构性**校验（字段取值范围、数量、自洽性）；
/// 需要几何计算的判定（双线是否交叉、间距是否几乎不变因而没有透视信息）在
/// PlacementMath.Validate() 里补完。UI 与素材包校验统一使用 PlacementMath.Validate()。
/// </summary>
public sealed class ScenePlacement
{
    /// <summary>数据版本，便于后续迁移。</summary>
    public int Version { get; set; } = 1;

    /// <summary>标定时的 scene.Background（相对路径）。与当前背景不一致 = 标定作废。</summary>
    public string SourceBackground { get; set; } = "";

    /// <summary>标定时的背景文件指纹（"{长度}@{mtime ticks}"），防同路径换图后旧标定静默生效。</summary>
    public string SourceStamp { get; set; } = "";

    /// <summary>标定时的原图像素宽（0 = 未知，如视频背景探不到）。</summary>
    public int SourceWidth { get; set; }

    /// <summary>标定时的原图像素高（0 = 未知）。</summary>
    public int SourceHeight { get; set; }

    /// <summary>落脚点类型，取值见 <see cref="LandingKinds"/>。</summary>
    public string Landing { get; set; } = LandingKinds.Point;

    /// <summary>缩放标定方式，取值见 <see cref="ScaleMethods"/>。</summary>
    public string ScaleMethod { get; set; } = ScaleMethods.Spheres;

    /// <summary>整体微调系数：乘在标定算出的缩放曲线上，用来修正"整体偏大/偏小"。</summary>
    public double GlobalFactor { get; set; } = 1.0;

    /// <summary>k=1 处角色可视高度占容器高度的比例。</summary>
    public double RefHeightRatio { get; set; } = 0.5;

    /// <summary>point 模式的落脚点。</summary>
    public List<NormPoint> Points { get; set; } = new();

    /// <summary>linear 模式的**落脚**折线（角色可在线上任意位置站立），可多条。</summary>
    public List<NormLine> Lines { get; set; } = new();

    /// <summary>
    /// dualLine 缩放标定用的两条线，**与落脚线相互独立**。
    /// 线性模式下两者要求各自成立（规格明确：线性模式也必须单独做缩放标定），
    /// 所以不能复用同一份线数据。
    /// </summary>
    public List<NormLine> ScaleLines { get; set; } = new();

    /// <summary>spheres 模式的示例圆球。</summary>
    public List<NormSphere> Spheres { get; set; } = new();

    /// <summary>标定备注（完成标定时的摘要文本，便于事后追溯）。</summary>
    public string Note { get; set; } = "";

    /// <summary>标定完成时间（UTC）。</summary>
    public DateTime? CalibratedAt { get; set; }

    // ============ 结构性校验 ============

    /// <summary>坐标是否落在允许范围内（留 2% 容差，允许贴边微出界）。</summary>
    public static bool IsValidNorm(double v) => double.IsFinite(v) && v >= -0.02 && v <= 1.02;

    public static bool IsValidNormPoint(NormPoint p) => p is not null && IsValidNorm(p.X) && IsValidNorm(p.Y);

    /// <summary>
    /// 结构性校验。返回 (是否完整, 缺什么)。
    /// Missing 里的每一条都是可以直接展示给用户 / 回报给 AI 的「追问」文案。
    /// </summary>
    public (bool Ok, List<string> Missing) Validate()
    {
        var missing = new List<string>();

        if (Landing != LandingKinds.Point && Landing != LandingKinds.Full && Landing != LandingKinds.Linear)
            missing.Add($"落脚点类型无效（当前「{Landing}」）：必须是 点选 / 整图 / 线性 之一");

        if (ScaleMethod != ScaleMethods.DualLine && ScaleMethod != ScaleMethods.Spheres)
            missing.Add($"缩放标定方式无效（当前「{ScaleMethod}」）：必须是 双线 / 圆球 之一");

        if (!double.IsFinite(GlobalFactor) || GlobalFactor < 0.2 || GlobalFactor > 3.0)
            missing.Add($"整体缩放系数超范围（当前 {GlobalFactor:0.###}）：应在 0.2 ~ 3.0 之间");

        if (!double.IsFinite(RefHeightRatio) || RefHeightRatio < 0.1 || RefHeightRatio > 0.9)
            missing.Add($"参考身高占比超范围（当前 {RefHeightRatio:0.###}）：应在 0.1 ~ 0.9 之间");

        ValidateLanding(missing);
        ValidateScale(missing);
        return (missing.Count == 0, missing);
    }

    /// <summary>落脚点部分的合法性。</summary>
    private void ValidateLanding(List<string> missing)
    {
        switch (Landing)
        {
            case LandingKinds.Point:
                if (Points.Count == 0)
                    missing.Add("落脚点未标定：点选模式至少要指定 1 个落脚点");
                foreach (var p in Points)
                    if (!IsValidNormPoint(p)) { missing.Add("落脚点坐标越界或非法（应在 0..1 之间）"); break; }
                break;

            case LandingKinds.Full:
                // 整图皆可站，不需要落脚几何数据。
                break;

            case LandingKinds.Linear:
                if (Lines.Count == 0)
                    missing.Add("落脚点未标定：线性模式至少要画 1 条可站立的线");
                ValidateLines(Lines, "落脚线", missing);
                break;
        }
    }

    /// <summary>缩放标定部分的合法性——这里是「禁止跳过缩放标定」的落点。</summary>
    private void ValidateScale(List<string> missing)
    {
        switch (ScaleMethod)
        {
            case ScaleMethods.DualLine:
                if (ScaleLines.Count != 2)
                    missing.Add($"缩放标定未完成：双线方式需要恰好 2 条线，当前 {ScaleLines.Count} 条");
                else
                {
                    ValidateLines(ScaleLines, "缩放线", missing);
                    // 间距是否随深度变化（有没有透视信息）需要几何计算，在 PlacementMath.Validate 里补。
                }
                break;

            case ScaleMethods.Spheres:
                if (Spheres.Count < 2)
                {
                    missing.Add($"缩放标定未完成：圆球方式至少需要 2 个圆球（近处一个、远处一个），当前 {Spheres.Count} 个");
                    break;
                }
                foreach (var s in Spheres)
                {
                    if (!IsValidNorm(s.X) || !IsValidNorm(s.Y))
                    { missing.Add("示例圆球球心坐标越界或非法（应在 0..1 之间）"); break; }
                    if (!double.IsFinite(s.R) || s.R < 0.005 || s.R > 0.6)
                    { missing.Add($"示例圆球半径超范围（当前 {s.R:0.###}）：应在 0.005 ~ 0.6 之间"); break; }
                }
                if (missing.Count > 0) break;

                var rMin = Spheres.Min(s => s.R);
                var rMax = Spheres.Max(s => s.R);
                if (rMin > 0 && rMax / rMin > 20)
                    missing.Add($"示例圆球大小差异过大（最大/最小 = {rMax / rMin:0.#}）：近处球与远处球应能看出比例，差异不应超过 20 倍");

                for (int i = 0; i < Spheres.Count; i++)
                {
                    for (int j = i + 1; j < Spheres.Count; j++)
                    {
                        var dx = Spheres[i].X - Spheres[j].X;
                        var dy = Spheres[i].Y - Spheres[j].Y;
                        if (Math.Sqrt(dx * dx + dy * dy) < 0.01)
                        { missing.Add("示例圆球之间有重叠：球心距离过近，无法区分远近"); break; }
                    }
                    if (missing.Count > 0) break;
                }
                break;
        }
    }

    /// <summary>一组折线的公共校验（点数、坐标合法、总长非零）。</summary>
    private static void ValidateLines(List<NormLine> lines, string label, List<string> missing)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line?.Points is null || line.Points.Count < 2)
            {
                missing.Add($"{label} {i + 1} 至少需要 2 个点");
                continue;
            }
            foreach (var p in line.Points)
                if (!IsValidNormPoint(p)) { missing.Add($"{label} {i + 1} 的顶点坐标越界或非法（应在 0..1 之间）"); break; }

            if (NormLength(line) < 0.02)
                missing.Add($"{label} {i + 1} 太短：请拉开两个端点");
        }
    }

    /// <summary>归一化折线总长。</summary>
    public static double NormLength(NormLine line)
    {
        if (line?.Points is null || line.Points.Count < 2) return 0;
        var sum = 0.0;
        for (int i = 1; i < line.Points.Count; i++)
        {
            var dx = line.Points[i].X - line.Points[i - 1].X;
            var dy = line.Points[i].Y - line.Points[i - 1].Y;
            sum += Math.Sqrt(dx * dx + dy * dy);
        }
        return sum;
    }

    /// <summary>深拷贝（标定页编辑时避免直接改到已落盘的数据）。</summary>
    public ScenePlacement Clone()
    {
        return new ScenePlacement
        {
            Version = Version,
            SourceBackground = SourceBackground,
            SourceStamp = SourceStamp,
            SourceWidth = SourceWidth,
            SourceHeight = SourceHeight,
            Landing = Landing,
            ScaleMethod = ScaleMethod,
            GlobalFactor = GlobalFactor,
            RefHeightRatio = RefHeightRatio,
            Points = Points.Select(p => new NormPoint(p.X, p.Y)).ToList(),
            Lines = Lines.Select(CloneLine).ToList(),
            ScaleLines = ScaleLines.Select(CloneLine).ToList(),
            Spheres = Spheres.Select(s => new NormSphere { X = s.X, Y = s.Y, R = s.R }).ToList(),
            Note = Note,
            CalibratedAt = CalibratedAt
        };
    }

    private static NormLine CloneLine(NormLine l) => new()
    {
        Note = l.Note,
        Points = l.Points.Select(p => new NormPoint(p.X, p.Y)).ToList()
    };
}
