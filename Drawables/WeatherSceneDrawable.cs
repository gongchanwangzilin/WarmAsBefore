using Microsoft.Maui.Graphics;
using WarmAsBefore.Models.Background;
using WarmAsBefore.Services.Background;

namespace WarmAsBefore.Drawables;

/// <summary>
/// 天气氛围底：满屏灰雾天空渐变 + 实体云 + 外部光照驱动的光柱/光路折射 + 柔和暗角 + 白闪。
/// 不画近景雨（由顶层控件负责）；云与光都在本层自绘，不依赖任何 Shadow API。
///
/// 光照参数（<see cref="LightDirX"/> / <see cref="LightElevation"/> /
/// <see cref="LightIntensity"/> / <see cref="LightEffectsEnabled"/>）由外部 VM 按时间与能见度
/// 算出后下发；关掉光效则只画实体云与底色。
///
/// 设计要点（硬红线）：
/// - Draw 完全无状态、无 Timer、无 Random、无 async：所有位置只由公开属性与 dirtyRect 决定；
///   外部改属性后调用 GraphicsView.Invalidate 即触发重绘。
/// - 颜色一律走 <see cref="BackgroundCatalog.ResolveColor"/> 或 Colors 预定义色，且统一过
///   <see cref="Mist"/> 去饱和；写死 hex 只出现在 ResolveColor 内部兜底，本文件不新增。
/// - 所有尺寸按 dirtyRect 相对比例计算；小画布（w &lt; 300）自动减半层数与椭圆数。
/// - 单帧 Fill / Draw 调用控制在 200 次以内；柔边全靠多层低 alpha 叠加，不用任何 Shadow API。
/// - 光路折射只是低 alpha 短亮线的视觉暗示（ICanvas 无 shader），不是物理折射。
/// </summary>
public sealed class WeatherSceneDrawable : IDrawable
{
    /// <summary>当前场景；null 时画柔和米灰兜底氛围。</summary>
    public WeatherSceneDefinition? Scene { get; set; }

    /// <summary>0..1 循环相位：仅用于雨幕折射抖动的确定性推导，不驱动粒子。</summary>
    public double Phase { get; set; }

    /// <summary>0..1 闪电白度，0 = 无。</summary>
    public double Lightning { get; set; }

    /// <summary>光源方向 x（-1..1，负为左正为右）。由 VM 按时间算。</summary>
    public double LightDirX { get; set; } = -0.7;

    /// <summary>光源仰角 0..1（0=地平线 1=正上方）。由 VM 按时间算。</summary>
    public double LightElevation { get; set; } = 0.5;

    /// <summary>光照强度 0..1。由 VM 按能见度算（能见度越高越强）。</summary>
    public double LightIntensity { get; set; } = 0.7;

    /// <summary>是否启用光柱与折射效果。关掉则只画云与底色。</summary>
    public bool LightEffectsEnabled { get; set; } = true;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var w = dirtyRect.Width;
        var h = dirtyRect.Height;
        if (w <= 0 || h <= 0) return;

        var scene = Scene;
        var small = w < 300;                       // 小窗口减半层数，控制 GPU 填充次数
        var light = Clamp01(LightIntensity);       // 外部光强，统一夹到 0..1
        var dirX = Math.Clamp(LightDirX, -1.0, 1.0);
        var elev = Clamp01(LightElevation);

        // 云色基调：光柱与实体云共用，保证 token 一致（无场景时用 Cream100 兜底）。
        var cloudTint = Mist(BackgroundCatalog.ResolveColor(scene?.CloudToken ?? "Cream100", Colors.White), 0.55);

        // ---------- 1. 天空渐变底色（Z 最底）----------
        // token 来源：Scene.SkyTopToken / Scene.SkyBottomToken，取到后统一去饱和向中性灰靠拢。
        Color skyTop, skyBottom;
        if (scene is null)
        {
            // 柔和米灰兜底：仍走 ResolveColor，解析失败用 Colors 预定义灰白色，不写 hex。
            skyTop = BackgroundCatalog.ResolveColor("Cream100", Colors.WhiteSmoke);
            skyBottom = BackgroundCatalog.ResolveColor("Beige200", Colors.LightGray);
        }
        else
        {
            skyTop = BackgroundCatalog.ResolveColor(scene.SkyTopToken, Colors.White);
            skyBottom = BackgroundCatalog.ResolveColor(scene.SkyBottomToken, Colors.LightBlue);
        }
        // 35% 原色 + 65% 灰度：低饱和雾感的关键，天色不再鲜艳，仍保留冷暖倾向。
        skyTop = Mist(skyTop, 0.65);
        skyBottom = Mist(skyBottom, 0.65);
        // 按外部光强微调明度：强光向白提亮、弱光向黑压暗，幅度上限约 0.12。
        skyTop = LightTone(skyTop, light);
        skyBottom = LightTone(skyBottom, light);
        // 复用本文件已有的分段竖向渐变辅助方法（16 段肉眼已连续）。
        FillVerticalGradient(canvas, 0f, 0f, w, h, skyTop, skyBottom, 16);

        // ---------- 2. 实体云层（替代原极低 alpha 雾带）----------
        // 每朵云 8~10 个 FillEllipse 叠出体积：底排暗（阴影）、顶排亮（蓬松云冠）；
        // 朝光源一侧的椭圆再提亮、背光侧压暗。alpha 0.55~0.9，是"实体"而非雾影。
        if (scene is not null && scene.CloudCount > 0)
        {
            var cloudShade = Toward(cloudTint, Colors.Black, 0.35);  // 背光深灰
            var count = Math.Min(scene.CloudCount, 8);               // 数量封顶，守住填充预算
            var halfPer = small ? 4 : 5;                             // 每排椭圆数，合计 8 / 10
            var fullW = w * 0.30 * scene.CloudScale;                 // 云团全宽
            var fullH = h * 0.13 * scene.CloudScale;                 // 云团全高（扁）
            var baseAlpha = (float)(0.55 + 0.35 * Clamp01(scene.CloudAlpha)); // 0.55~0.9

            for (var i = 0; i < count; i++)
            {
                // 确定性分布（无 Random）：横向 0.27 步进取余，纵向 i%3 三档错落。
                var fx = (i * 0.27 + 0.1) % 1.0;
                var fy = 0.12 + (i % 3) * 0.13;
                var cx = (float)(fx * w);
                var cy = (float)(fy * h);
                var hw = (float)(fullW * 0.5);
                var hh = (float)(fullH * 0.5);

                // 底排：偏暗的体积阴影，靠下的扁椭圆。
                for (var k = 0; k < halfPer; k++)
                {
                    var u = halfPer == 1 ? 0.5 : (double)k / (halfPer - 1);
                    var xo = (float)((u - 0.5) * fullW * 0.9);
                    var yo = (float)(hh * 0.35);
                    var rx = (float)(hw * 0.42);
                    var ry = (float)(hh * 0.7);
                    var facing = xo * dirX > 0; // 与光源同侧 → 受光
                    var col = facing ? Toward(cloudShade, Colors.White, 0.25) : cloudShade;
                    canvas.FillColor = col.WithAlpha(baseAlpha * 0.9f);
                    canvas.FillEllipse(cx + xo - rx, cy + yo - ry, rx * 2, ry * 2);
                }

                // 顶排：蓬松云冠，中间高两端低；受光侧更亮、背光侧压暗。
                for (var k = 0; k < halfPer; k++)
                {
                    var u = halfPer == 1 ? 0.5 : (double)k / (halfPer - 1);
                    var bump = Math.Sin(u * Math.PI);                    // 中间 1、两端 0
                    var xo = (float)((u - 0.5) * fullW * 0.72);
                    var yo = (float)(-hh * (0.15 + 0.5 * bump));
                    var rx = (float)(hw * (0.28 + 0.06 * bump));
                    var ry = (float)(hh * (0.5 + 0.25 * bump));
                    var facing = xo * dirX > 0;
                    var col = facing
                        ? Toward(cloudTint, Colors.White, 0.55)
                        : Toward(cloudTint, cloudShade, 0.55);
                    canvas.FillColor = col.WithAlpha(baseAlpha);
                    canvas.FillEllipse(cx + xo - rx, cy + yo - ry, rx * 2, ry * 2);
                }
            }
        }

        // 光照锚点：由外部方向 + 仰角算出，光柱与折射共用。
        var sunX = (float)((0.5 - dirX * 0.4) * w);
        var sunY = (float)((1 - elev) * 0.5 * h);

        // ---------- 3. 光柱 / 丁达尔光束 ----------
        // 仅在开启光效时画：从锚点向下发散的低 alpha 粗线束（画在云之后、暗角之前），
        // 中间光束更亮，模拟光穿过云层缝隙洒下。
        if (LightEffectsEnabled && light > 0.01)
        {
            var beamColor = Toward(Colors.White, cloudTint, 0.3); // 近白带云色倾向（暖/冷由 token 定）
            var beams = small ? 6 : 9;                            // 6~9 道
            var spread = Math.Max(w, h) * 1.2f;
            canvas.StrokeSize = (float)(Math.Max(w, h) * 0.02 * (0.6 + 0.4 * light));
            for (var b = 0; b < beams; b++)
            {
                var t = beams == 1 ? 0.5 : (double)b / (beams - 1);
                var ang = (t - 0.5) * 0.9 + dirX * 0.15;          // 扇形张角 + 行进方向偏置
                var ex = sunX + (float)(Math.Sin(ang) * spread);
                var ey = sunY + (float)(Math.Cos(ang) * spread);  // cos>0 → 向下发散
                var middle = 1 - Math.Abs(t - 0.5) * 2;           // 中间光束更亮
                var a = (0.04 + 0.08 * light) * (0.5 + 0.5 * middle); // 单层 alpha 0.04~0.12
                canvas.StrokeColor = beamColor.WithAlpha((float)a);
                canvas.DrawLine(sunX, sunY, ex, ey);
            }
            // 光源处柔光晕并入光效层，保留"光透过雾"的软亮区（DrawGlow 内部不用 Shadow）。
            DrawGlow(canvas, sunX, sunY, Math.Max(w, h) * 0.26f, beamColor, 0.12 * light);
        }

        // ---------- 4. 光路扭曲 / 折射 ----------
        // 仅雨场景且开启光效：在光束经过的区域画确定性抖动的短亮线，暗示雨滴把光线打散、抖动。
        // 这是视觉暗示，不是物理折射（ICanvas 无 shader）；位置由 Phase 与索引推导，零 Random。
        if (LightEffectsEnabled && light > 0.01 && scene is not null &&
            scene.Kind is WeatherSceneKind.Drizzle or WeatherSceneKind.LightRain
                or WeatherSceneKind.HeavyRain or WeatherSceneKind.Storm)
        {
            var streaks = small ? 20 : 36;                        // 20~36 条
            canvas.StrokeSize = Math.Max(1f, w * 0.0016f);
            for (var i = 0; i < streaks; i++)
            {
                var seed = i * 12.9898 + Phase * 78.233;
                var jitter = Math.Sin(seed) * 0.5 + 0.5;          // 0..1 确定性抖动
                var px = (float)(((i * 0.173 + jitter * 0.07) % 1.0) * w);
                var py = (float)((((i * 0.079 + Phase * 0.6) % 1.0)) * h);
                var near = 1.0 - Math.Abs(px - sunX) / Math.Max(w, 1f) * 1.4; // 靠近光束才画
                if (near <= 0) continue;
                var len = (float)(h * 0.012 * (0.5 + jitter));
                var tilt = (float)(w * 0.006 * Math.Cos(seed));   // 抖动倾斜，模拟折射方向扰动
                canvas.StrokeColor = Colors.White.WithAlpha((float)((0.05 + 0.10 * light) * near));
                canvas.DrawLine(px, py, px + tilt, py + len);
            }
        }

        // ---------- 5. 暗角 vignette（参考图关键特征）----------
        // 四边向中心叠半透明深色带，越靠边 alpha 越高（单层上限约 0.16），
        // 四周沉入柔和阴影、中心保持明亮。
        var dim = Mist(BackgroundCatalog.ResolveColor("OverlayDim", Colors.Black), 0.5);
        var vigLayers = small ? 4 : 7;
        const float maxDimAlpha = 0.16f;
        var coverTop = h * 0.35f;   // 上下各覆盖 35% 高度
        var coverSide = w * 0.35f;  // 左右各覆盖 35% 宽度
        var vBandH = coverTop / vigLayers;
        var vBandW = coverSide / vigLayers;
        for (var i = 0; i < vigLayers; i++)
        {
            var t = (double)i / vigLayers;               // 0 靠边 → 越往内越大
            var a = maxDimAlpha * (float)((1 - t) * (1 - t));
            canvas.FillColor = dim.WithAlpha(a);
            var yOff = (float)(i * vBandH);
            var xOff = (float)(i * vBandW);
            // 上 / 下
            canvas.FillRectangle(0f, yOff, w, vBandH + 0.6f);
            canvas.FillRectangle(0f, h - yOff - vBandH - 0.6f, w, vBandH + 0.6f);
            // 左 / 右
            canvas.FillRectangle(xOff, 0f, vBandW + 0.6f, h);
            canvas.FillRectangle(w - xOff - vBandW - 0.6f, 0f, vBandW + 0.6f, h);
        }

        // ---------- 6. 闪电（Z 最顶，白色叠加特效）----------
        // 保留既有逻辑：颜色走 ResolveColor 兜底白色，整幅叠白闪（白色过 Mist 仍是白色）。
        if (Lightning > 0.01)
        {
            var flash = Mist(BackgroundCatalog.ResolveColor(null, Colors.White), 0.65);
            canvas.FillColor = flash.WithAlpha((float)(Lightning * 0.55));
            canvas.FillRectangle(dirtyRect);
        }
    }

    /// <summary>
    /// 竖向渐变：把 [top, from] 到 [bottom, to] 切成 steps 段纯色矩形插值填充。
    /// 当前 MAUI Graphics 的 ICanvas 没有 CreateLinearGradient/SetFillPaint，分段填充是
    /// 零依赖的确定性替代；段数固定，低分辨率下也不退化。
    /// </summary>
    private static void FillVerticalGradient(ICanvas canvas, float x, float top, float w, float h,
        Color from, Color to, int steps)
    {
        if (w <= 0 || h <= 0) return;
        var band = h / steps;
        for (var i = 0; i < steps; i++)
        {
            var t = steps == 1 ? 0f : (float)i / (steps - 1);
            var c = new Color(
                (float)(from.Red + (to.Red - from.Red) * t),
                (float)(from.Green + (to.Green - from.Green) * t),
                (float)(from.Blue + (to.Blue - from.Blue) * t),
                (float)(from.Alpha + (to.Alpha - from.Alpha) * t));
            canvas.FillColor = c;
            canvas.FillRectangle(x, top + i * band, w, band + 0.6f); // 略重叠，消除段间缝
        }
    }

    /// <summary>画一层柔边光晕：同色、指定 alpha 的实心圆（无 Shadow API）。</summary>
    private static void DrawGlow(ICanvas canvas, float cx, float cy, float radius, Color color, double alpha)
    {
        if (radius <= 0 || alpha <= 0) return;
        canvas.FillColor = color.WithAlpha((float)Math.Clamp(alpha, 0, 1));
        canvas.FillCircle(cx, cy, radius);
    }

    /// <summary>去饱和并向中性灰靠拢：mix=0 原色，mix=1 全灰（亮度不变）。</summary>
    private static Color Mist(Color c, double mix)
    {
        var gray = c.Red * 0.299 + c.Green * 0.587 + c.Blue * 0.114;
        return Toward(c, new Color((float)gray, (float)gray, (float)gray, c.Alpha), mix);
    }

    /// <summary>把颜色向 target 靠拢：t=0 原色，t=1 target；alpha 取原色。</summary>
    private static Color Toward(Color c, Color target, double t) => new(
        (float)(c.Red + (target.Red - c.Red) * t),
        (float)(c.Green + (target.Green - c.Green) * t),
        (float)(c.Blue + (target.Blue - c.Blue) * t),
        c.Alpha);

    /// <summary>把值夹到 0..1。</summary>
    private static double Clamp01(double v) => Math.Clamp(v, 0.0, 1.0);

    /// <summary>光照调明度：强光向白提亮、弱光向黑压暗，幅度上限约 0.12。</summary>
    private static Color LightTone(Color c, double light) =>
        light >= 0.5
            ? Toward(c, Colors.White, (light - 0.5) * 0.24)
            : Toward(c, Colors.Black, (0.5 - light) * 0.24);
}
