namespace WarmAsBefore.Models;

/// <summary>
/// 立绘的逻辑画布规格：一切立绘都按这块画布来对齐，而不是按各自的原始像素尺寸。
/// 好处是不同来源的立绘（原生 1080×1920、2 倍图、带留白的图）在同一场景里大小与站位一致。
/// </summary>
public static class PortraitCanvas
{
    public const double Width = 1080;
    public const double Height = 1920;

    /// <summary>标准宽高比 9:16。</summary>
    public static double Aspect => Width / Height;

    public static string Spec => "1080×1920";

    /// <summary>比例容差：1% 以内视为符合（允许 1080×1921 这类取整误差）。</summary>
    public const double AspectTolerance = 0.01;

    /// <summary>像素尺寸是否符合标准画布比例。</summary>
    public static bool Matches(int w, int h)
    {
        if (w <= 0 || h <= 0) return false;
        var aspect = (double)w / h;
        return Math.Abs(aspect - Aspect) / Aspect <= AspectTolerance;
    }
}

/// <summary>
/// 立绘标定：把一张素材映射到 <see cref="PortraitCanvas"/> 上。
///
/// 允许角色**溢出**画布（缩放大于 1、偏移出界），但三条硬约束由 <see cref="PortraitCalibrationMath.Validate"/> 守住：
/// · 底部对齐：脚底线（<see cref="FootLine"/>）必须落在画布底边，且底边不得被裁掉；
/// · 大小正确：缩放必须为正且在合理区间，否则角色会大到只剩一条腿或小成一个点；
/// · 方向正确：旋转只能是 0/90/180/270，横躺或倒立必须显式标定而不是碰运气。
/// </summary>
public sealed record PortraitCalibration
{
    /// <summary>是否已经过人工标定（未标定的素材不允许上场，先走调试界面）。</summary>
    public bool Calibrated { get; init; }

    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }

    public bool FlipHorizontal { get; init; }
    public bool FlipVertical { get; init; }

    /// <summary>方向校正的旋转角度：只能是 0 / 90 / 180 / 270。</summary>
    public int Rotation { get; init; }

    /// <summary>缩放：1.0 = 画布 AspFit 后的基准大小；大于 1 表示角色溢出画布。</summary>
    public double Scale { get; init; } = 1.0;

    /// <summary>相对画布的偏移（归一化，正 = 右 / 下）。</summary>
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }

    /// <summary>四边裁剪（归一化 0..0.9）：用来修掉素材里多余的留白。</summary>
    public double CropLeft { get; init; }
    public double CropTop { get; init; }
    public double CropRight { get; init; }
    public double CropBottom { get; init; }

    /// <summary>脚底线在画布中的归一化高度（0.5..1.0）：1.0 = 恰好踩在画布底边。</summary>
    public double FootLine { get; init; } = 1.0;

    public DateTime At { get; init; } = DateTime.UtcNow;
}

/// <summary>立绘标定的换算与校验：纯函数，便于单独验证。</summary>
public static class PortraitCalibrationMath
{
    public const double MinScale = 0.2;
    public const double MaxScale = 5.0;
    public const double MaxCrop = 0.9;

    /// <summary>旋转后是否需要交换宽高（90/270 会让竖图横躺）。</summary>
    public static bool SwapsAxes(int rotation) => rotation is 90 or 270;

    /// <summary>
    /// 裁剪与旋转之后的「有效像素尺寸」：立绘的几何计算一律基于这个尺寸，
    /// 这样 1080×1920 的坐标语义对任何素材都成立。
    /// </summary>
    public static (double W, double H) EffectiveSize(int srcW, int srcH, PortraitCalibration? cal)
    {
        if (srcW <= 0 || srcH <= 0) return (PortraitCanvas.Width, PortraitCanvas.Height);
        if (cal is null || !cal.Calibrated) return (srcW, srcH);

        var w = Math.Max(1.0, srcW * (1 - Math.Clamp(cal.CropLeft + cal.CropRight, 0, 2 * MaxCrop)));
        var h = Math.Max(1.0, srcH * (1 - Math.Clamp(cal.CropTop + cal.CropBottom, 0, 2 * MaxCrop)));
        return SwapsAxes(cal.Rotation) ? (h, w) : (w, h);
    }

    /// <summary>把裁剪后的内容映射回标准画布：返回该素材在画布坐标系里的显示尺寸。</summary>
    public static (double W, double H) CanvasBox(int srcW, int srcH, PortraitCalibration? cal)
    {
        var (ew, eh) = EffectiveSize(srcW, srcH, cal);
        var fit = Math.Min(PortraitCanvas.Width / ew, PortraitCanvas.Height / eh);
        return (ew * fit, eh * fit);
    }

    /// <summary>
    /// 校验标定结果。返回空列表 = 可上场；否则每一项都是给用户看的具体原因。
    /// </summary>
    public static List<string> Validate(PortraitCalibration? cal, int srcW = 0, int srcH = 0)
    {
        var problems = new List<string>();
        if (cal is null || !cal.Calibrated)
        {
            problems.Add($"尚未标定：立绘需要按 {PortraitCanvas.Spec} 逻辑画布标定后才能上场。");
            return problems;
        }

        if (cal.Rotation is not (0 or 90 or 180 or 270))
            problems.Add($"旋转角度 {cal.Rotation}° 不合法，只能是 0/90/180/270。");

        if (cal.Scale < MinScale || cal.Scale > MaxScale)
            problems.Add($"缩放 {cal.Scale:0.##} 超出 {MinScale}~{MaxScale} 的合理区间。");

        if (cal.CropLeft is < 0 or > MaxCrop || cal.CropRight is < 0 or > MaxCrop
            || cal.CropTop is < 0 or > MaxCrop || cal.CropBottom is < 0 or > MaxCrop)
            problems.Add($"裁剪比例必须在 0~{MaxCrop} 之间。");

        if (cal.CropLeft + cal.CropRight >= 1 || cal.CropTop + cal.CropBottom >= 1)
            problems.Add("左右或上下的裁剪加起来把整张图裁空了。");

        // 底部对齐：脚底线必须在画布靠下的位置，且不能把底边裁掉（否则脚被切掉）
        if (cal.FootLine is < 0.5 or > 1.0)
            problems.Add($"脚底线 {cal.FootLine:0.##} 必须落在画布下半部（0.5~1.0）。");
        if (cal.CropBottom > 0.001)
            problems.Add("底部被裁剪：角色会失去脚部，无法保证底部对齐。");

        // 大小正确：溢出的幅度要有限度，否则角色会整块跑出可视区
        if (Math.Abs(cal.OffsetX) > 1.0 || Math.Abs(cal.OffsetY) > 1.0)
            problems.Add("偏移超出画布范围（-1~1）。");

        if (srcW > 0 && srcH > 0 && SwapsAxes(cal.Rotation) && srcH > srcW)
            problems.Add("竖版素材被标定为横躺（90°/270°），方向可能不对，请确认。");

        return problems;
    }

    /// <summary>素材本身是否已经是标准画布尺寸（比例符合即可，允许 2 倍图）。</summary>
    public static bool IsStandardCanvas(int srcW, int srcH) => PortraitCanvas.Matches(srcW, srcH);
}
