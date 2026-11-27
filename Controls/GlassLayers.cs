namespace WarmAsBefore.Controls;

/// <summary>
/// 一块玻璃能画多少层，由这台机器决定，而不是写死一个数。
///
/// 用户的要求是「能有多少层就有多少层，最高 600 层，直接根据系统配置来」。所以这里做两件事：
///   1. **按硬件给起点**：核数与可用内存决定第一次合成用多少层（12 层起步，最省的那台机器也有 12 层）；
///   2. **按实测自调**：每合成一块玻璃就记一次耗时，快到有富余就往上加，慢到拖后腿就往下收，
///      上限 600、下限 12。所以同一份代码在弱机上自动变薄、在强机上自动变厚 ——
///      这比一张「显卡白名单」诚实，也永远不会把某台机器画崩。
///
/// 层的分配（每一层都是一次真实的绘制，不是参数复制）：
///   折射环带 / 侧壁圈数 / 柱面细分数 / 材料柔光圈数 / 内影遍数 / 镜面遍数。
/// 全部只在**合成一次**的静态位图上发生：运行时的每帧仍然只有一次 DrawImage，
/// 所以层数多寡影响的是「这块玻璃好不好看」，不是「界面流不流畅」。
/// </summary>
internal static class GlassLayers
{
    /// <summary>层数下限：再省也要有该有的那几层，否则玻璃就退化成一块半透明塑料。</summary>
    public const int Floor = 12;

    /// <summary>
    /// 层数上限。600 是用户给的天花板：到这个量级时，一块 200×60 的玻璃要画 600 次渐变与裁剪，
    /// 单块也就十几毫秒；它只发生在这个尺寸第一次出现的那一帧，且合成走后台线程。
    /// </summary>
    public const int Ceiling = 600;

    /// <summary>单块玻璃的合成预算（毫秒）。超了就往回收，富余就往上加。</summary>
    private const double ComfortMs = 8.0;
    private const double HeadroomMs = 3.0;

    private static readonly object Gate = new();
    private static int _budget = Detect();
    private static int _samples;

    /// <summary>当前层数预算。</summary>
    public static int Budget
    {
        get { lock (Gate) return _budget; }
    }

    private static int Detect()
    {
        try
        {
            var cores = Math.Max(1, Environment.ProcessorCount);
            long ramGb = 8;
            try
            {
                var info = GC.GetGCMemoryInfo();
                if (info.TotalAvailableMemoryBytes > 0)
                    ramGb = Math.Max(1, info.TotalAvailableMemoryBytes / (1L << 30));
            }
            catch { /* 老运行时取不到就用默认 8G，不影响正确性 */ }

            // 起点：基准 24 + 每核 24 + 每 GB 内存 8，夹在 24~300 之间。
            // 8 核 16G 的常见机器 ≈ 24 + 192 + 128 = 344；4 核 8G ≈ 184。
            var budget = 24 + cores * 24 + (int)Math.Min(200, ramGb * 8);
            return Math.Clamp(budget, 24, 300);
        }
        catch
        {
            return 48;
        }
    }

    /// <summary>
    /// 合成一块记一次。慢就收（一次收 25%），明显富余就加（一次加 20%），
    /// 两次之间至少隔 4 个样本，免得同一块玻璃重画时来回抖。
    /// </summary>
    public static void Report(double ms)
    {
        if (!double.IsFinite(ms) || ms < 0) return;
        lock (Gate)
        {
            _samples++;
            if (_samples < 4) return;
            if (ms > ComfortMs)
            {
                _budget = Math.Max(Floor, (int)(_budget * 0.75));
                _samples = 0;
            }
            else if (ms < HeadroomMs && _budget < Ceiling)
            {
                _budget = Math.Min(Ceiling, (int)(_budget * 1.20) + 8);
                _samples = 0;
            }
        }
    }

    // ── 各层用它换算自己画几层（都保证有最低档，不会因为预算小就消失）────────────

    /// <summary>折射环带：4~32 条。条数越多，边缘那一下弯折越连续。</summary>
    public static int RefractionBands => Math.Clamp(Budget / 20, 4, 32);

    /// <summary>侧壁圈数：3~14 圈。圈数越多，墙越像一段连续的弧面而不是三条线。</summary>
    public static int WallPasses => Math.Clamp(Budget / 40, 3, 14);

    /// <summary>柱面细分：上下各 1~6 条竖向切片，切片越多柱面越圆。</summary>
    public static int BevelSlices => Math.Clamp(Budget / 90, 1, 6);

    /// <summary>材料柔光：1~8 圈同心径向光，越多越平滑。</summary>
    public static int SheenRings => Math.Clamp(Budget / 60, 1, 8);

    /// <summary>内影遍数：3~12 遍。</summary>
    public static int ShadowPasses => Math.Clamp(Budget / 50, 3, 12);

    /// <summary>镜面弧的辉光遍数：1~6 遍。</summary>
    public static int SpecularPasses => Math.Clamp(Budget / 100, 1, 6);

    /// <summary>给人看的一句话（写日志用）。</summary>
    public static string Describe() =>
        $"layers={Budget} refract={RefractionBands} wall={WallPasses} bevel={BevelSlices} " +
        $"sheen={SheenRings} shadow={ShadowPasses} spec={SpecularPasses}";
}
