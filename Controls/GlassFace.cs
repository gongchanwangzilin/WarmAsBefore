using Microsoft.Maui.Graphics.Platform;
using SkiaSharp;
using WarmAsBefore.DesignSystem.Worlds;
using WarmAsBefore.Services;

namespace WarmAsBefore.Controls;

/// <summary>
/// 一块玻璃面的全部绘制参数。由 <see cref="GlassPlate"/> 每次需要重画时算好，
/// 参数里**只有值**（没有控件引用），所以同一块玻璃的结果可以被复用、被缓存。
/// </summary>
public readonly record struct GlassFaceParams(
    double Width,
    double Height,
    double Density,
    double Radius,
    double Thickness,
    bool Pressed,
    bool Hover,
    bool Signal,
    bool Hollow,
    double PageX,
    double PageY,
    double PageW,
    double PageH,
    double TintAmount);

/// <summary>
/// 真·液态玻璃的绘制核心：**用 Skia 自己画一块玻璃**，而不是往控件上叠一层半透明白。
///
/// 为什么必须自己画（这一节的结论是三次实测换来的）：MAUI / WinUI 没有 backdrop-filter，
/// 也没有逐控件的位移着色器 —— CSS 那条 `blur + 半透明 + 边框` 的路在这里是死的，
/// 它只会得到「一块磨砂塑料板」。iOS 26 的玻璃之所以是玻璃，是三件事同时发生：
///   ① 背后的光被**透镜式弯折**（边缘的取样点从别处来，于是边缘的内容是错的、被挤过的）；
///   ② 一条**随光走的镜面高光弧**（左上最亮，右下消失）；
///   ③ 自身**有厚度**（贴边的内阴影、以及面上那一层极淡的色）。
///
/// 本类的画法（坐标全部换算到像素，逻辑单位乘 <see cref="GlassFaceParams.Density"/>）：
///   底   —— 从 <see cref="FrostField"/> 里按「页面坐标 → 糊底像素」的 UniformToFill 映射
///            裁出这块玻璃**背后**的那一小块，铺满圆角矩形。玻璃下面因此是真的背景，不是臆想的颜色。
///   折射 —— 在距边 <see cref="BandOf"/> 的环带里，用**放大 <see cref="RefractGain"/> 倍、
///            绕玻璃中心**再采样一次同一张糊底。放大 = 取样点向中心收，画面上等价于把边缘的内容
///            向外挤出去 —— 这就是廉价的透镜弯折。环带切成 <see cref="RefractBands"/> 条，
///            每条一次裁剪 + 一次绘制，权重按「越靠边越强」的曲线给，四档足以在 7px 上读成渐变。
///   镜面 —— 一条从左腰绕左上角到顶边的弧，用沿对角衰减的线性渐变做描边（ObliqueSpec）。
///            按下时更亮更窄，悬停时略亮一档：光在动，材料没动。
///   内影 —— 同一条圆角路径用**宽描边**画，描边一半落在外面被裁掉，只剩下贴边的内侧那一半；
///            三遍由宽到窄、由淡到浓叠出柔和衰减，右下最浓、左上为零（光是左上来的）。
///
/// 没有糊底（首次启动还在算、Android、解码失败）时退化成**同一套颜色令牌的斜渐变**：
/// 折射没有东西可以弯，但镜面、内影、描边照画 —— 结果是现在这套玻璃按钮的样子，不是白块。
///
/// 性能：一块玻璃只在「几何 / 状态 / 主题 / 糊底版本」变化时合成一次，合出来的位图按缓存键复用
/// （同尺寸同位置的钮共用一张）；每帧只剩一次 DrawImage。糊底本身只解码一次（<see cref="FrostField"/>）。
/// </summary>
internal static class GlassFace
{
    /// <summary>环带基准宽（逻辑单位）。7 是「一眼看得出边缘在折，又不吃掉整个钮」的那一档。</summary>
    private const double RefractBandDp = 7.0;

    /// <summary>环带占短边的最小 / 最大比例：小钮（28 的滑钮）上 7px 会吃掉半个身位，按比例收。</summary>
    private const double BandRatio = 0.17;
    private const double BandMinDp = 3.2;

    /// <summary>
    /// 透镜的放大倍率。1.08 是「看得出来、又说不出」的上限：
    /// 再大边缘就成了哈哈镜，再小（1.03）在 7px 的环带上根本读不出位移。
    /// </summary>
    private const float RefractGain = 1.08f;

    /// <summary>环带的档数。四档在 7px 上每档不到 2px，肉眼即渐变；再多只是多画一遍。</summary>
    private const int RefractBands = 4;

    /// <summary>缓存上限：一屏的玻璃块数远小于它，多了说明页面在不停换尺寸。</summary>
    private const int MaxCached = 64;

    /// <summary>
    /// 色调量的参照值：<see cref="GlassPlate.TintAmount"/> 的默认档（0.07）对上世界下发的
    /// <c>GTintAmount</c>。控件自己的 TintAmount 从此是「相对倍率」（滑杆轨道 0.05、按钮 0.07、
    /// 信号钮 0.11），绝对的浓淡归世界按明暗算 —— 一份规则，两块玻璃两种用途。
    /// </summary>
    private const double TintReference = 0.07;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Microsoft.Maui.Graphics.IImage> Cache = new();
    private static readonly Queue<string> Order = new();

    /// <summary>取这块玻璃的位图（合成或命中缓存）。返回 null 表示这次画不出来，调用方画兜底渐变。</summary>
    public static Microsoft.Maui.Graphics.IImage? Get(in GlassFaceParams p)
    {
        if (!(p.Width > 0.5) || !(p.Height > 0.5)) return null;
        var key = KeyOf(p);
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var hit)) return hit;
        }

        Microsoft.Maui.Graphics.IImage? image;
        try
        {
            image = Compose(p);
        }
        catch (Exception ex)
        {
            App.WriteLog("GlassFace.Compose -> " + ex.Message);
            return null;
        }
        if (image is null) return null;

        lock (Gate)
        {
            if (Cache.TryAdd(key, image))
            {
                Order.Enqueue(key);
                while (Order.Count > MaxCached)
                {
                    var old = Order.Dequeue();
                    if (Cache.Remove(old, out var dead)) (dead as IDisposable)?.Dispose();
                }
            }
        }
        return image;
    }

    /// <summary>缓存键：只带「影响像素的」量，浮点全部落到一位小数，避免亚像素抖动把缓存打成碎片。</summary>
    private static string KeyOf(in GlassFaceParams p) => string.Join('|',
        Math.Round(p.Width, 1), Math.Round(p.Height, 1), Math.Round(p.Density, 2),
        Math.Round(p.Radius, 1), Math.Round(p.Thickness, 2),
        p.Pressed ? 'P' : p.Hover ? 'H' : 'N', p.Signal ? 'S' : '-', p.Hollow ? 'O' : '*',
        Math.Round(p.PageX), Math.Round(p.PageY), Math.Round(p.PageW), Math.Round(p.PageH),
        Math.Round(p.TintAmount, 2), StyleWorldTokens.Dark ? 'd' : 'l', FrostField.Version, TintSignature());

    // ── 合成 ────────────────────────────────────────────────────────────

    private static Microsoft.Maui.Graphics.IImage? Compose(in GlassFaceParams p)
    {
        var density = p.Density is > 0.1 and < 8 ? p.Density : 1.0;
        var w = Math.Max(4, (int)Math.Round(p.Width * density));
        var h = Math.Max(4, (int)Math.Round(p.Height * density));
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);

            var radius = (float)Math.Clamp(p.Radius * density, 0, Math.Min(w, h) * 0.5f);
            using var plate = Rounded(0, 0, w, h, radius);
            canvas.Save();
            canvas.ClipPath(plate, SKClipOperation.Intersect, true);

            var frost = FrostField.Bitmap;
            var map = FrostField.Map(p.PageW, p.PageH);
            var live = frost is not null && map.IsUsable;

            if (!p.Hollow)
            {
                if (live) DrawFrost(canvas, frost!, map, p, w, h, 1f, 1f);
                else DrawFallbackBase(canvas, w, h);
            }

            if (live) DrawRefraction(canvas, frost!, map, p, w, h, density);
            if (!p.Hollow)
            {
                DrawFace(canvas, p, w, h);
                DrawTint(canvas, p, w, h);
            }
            DrawInnerShadow(canvas, p, w, h, radius, density);
            DrawRim(canvas, p, w, h, radius, density);
            canvas.Restore();

            DrawSpecular(canvas, p, w, h, radius, density, p.Hollow ? (float)(1.6 * density) : 0f);
        }

        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return PlatformImage.FromStream(new MemoryStream(data.ToArray()));
    }

    /// <summary>
    /// 糊底取样：把「这块玻璃背后那一块糊底」铺满整块。映射与窗口上那张 UniformToFill 完全一致，
    /// 所以玻璃里的内容是**真的从它背后的位置**来的（换壁纸、换主题之后也是）。
    /// 玻璃跨在图像边缘时按比例裁掉越界的那一截，而不是让它采样到空白。
    /// </summary>
    private static void DrawFrost(SKCanvas canvas, SKBitmap frost, in FrostMap map, in GlassFaceParams p,
        int w, int h, float alpha, float gain, float cx = 0.5f, float cy = 0.5f)
    {
        var sx0 = map.X(p.PageX);
        var sy0 = map.Y(p.PageY);
        var sx1 = map.X(p.PageX + p.Width);
        var sy1 = map.Y(p.PageY + p.Height);
        if (!(sx1 > sx0) || !(sy1 > sy0)) return;

        var cl0 = Math.Clamp(sx0, 0, frost.Width);
        var ct0 = Math.Clamp(sy0, 0, frost.Height);
        var cl1 = Math.Clamp(sx1, 0, frost.Width);
        var ct1 = Math.Clamp(sy1, 0, frost.Height);
        if (cl1 - cl0 < 0.5f || ct1 - ct0 < 0.5f) return;

        var dx0 = (cl0 - sx0) / (sx1 - sx0) * w;
        var dy0 = (ct0 - sy0) / (sy1 - sy0) * h;
        var dx1 = (cl1 - sx0) / (sx1 - sx0) * w;
        var dy1 = (ct1 - sy0) / (sy1 - sy0) * h;

        using var paint = new SKPaint
        {
            FilterQuality = SKFilterQuality.Medium,
            Color = SKColors.White.WithAlpha((byte)Math.Clamp(alpha * 255, 0, 255))
        };

        if (gain == 1f && cx == 0.5f && cy == 0.5f)
        {
            canvas.DrawBitmap(frost, new SKRect(cl0, ct0, cl1, ct1), new SKRect(dx0, dy0, dx1, dy1), paint);
            return;
        }

        // 绕一个点放大 gain 倍：画面上等价于「取样点向该点收」，边缘的内容因此被挤出去。
        canvas.Save();
        canvas.Translate(w * cx, h * cy);
        canvas.Scale(gain, gain);
        canvas.Translate(-w * cx, -h * cy);
        canvas.DrawBitmap(frost, new SKRect(cl0, ct0, cl1, ct1), new SKRect(dx0, dy0, dx1, dy1), paint);
        canvas.Restore();
    }

    /// <summary>
    /// 边缘折射：以玻璃中心为原点把糊底放大 <see cref="RefractGain"/> 倍再画一遍，只留边缘那条环带。
    /// 权重曲线是 (1-t)^1.4（t 为环带内的归一化深度）：外缘最浓、向内迅速化开，
    /// 这样眼睛读到的是「边在折」而不是「一圈描边」。
    /// </summary>
    private static void DrawRefraction(SKCanvas canvas, SKBitmap frost, in FrostMap map, in GlassFaceParams p,
        int w, int h, double density)
    {
        var bandDp = Math.Min(Math.Min(p.Width, p.Height) * BandRatio, RefractBandDp);
        var band = (float)Math.Max(BandMinDp, bandDp * density);
        band = Math.Min(band, Math.Min(w, h) * 0.34f);
        if (band < 1f) return;

        var radius = (float)Math.Clamp(p.Radius * density, 0, Math.Min(w, h) * 0.5f);
        var boost = p.Pressed ? 1.15 : p.Hover ? 1.08 : 1.0;

        for (var i = 0; i < RefractBands; i++)
        {
            var o0 = band * i / RefractBands;
            var o1 = band * (i + 1) / RefractBands;
            using var ring = Ring(0, 0, w, h, radius, o0, o1);
            var t = (i + 0.5) / RefractBands;
            var weight = (float)(Math.Pow(1 - t, 1.4) * 0.62 * boost);
            if (weight < 0.01f) continue;

            canvas.Save();
            canvas.ClipPath(ring, SKClipOperation.Intersect, true);
            DrawFrost(canvas, frost, map, p, w, h, weight, RefractGain);
            canvas.Restore();
        }
    }

    /// <summary>
    /// 面：从左上斜落的一层极淡白 → 中段 → 底边沉一档。这不是装饰，
    /// 它把「糊底」和「玻璃」分成两层 —— 没有这一层，玻璃就只是背景上的一块钱币大小的洞。
    /// 按下时面收薄一档（材料被压扁），悬停时厚一档。
    /// </summary>
    private static void DrawFace(SKCanvas canvas, in GlassFaceParams p, int w, int h)
    {
        // 顶面那道亮：浅档是暖白（GTintTop），深档是面板色相提亮的一档 —— 不再是纯白。
        var hi = Token("GTintTop", "#FEFCF8");
        var mid = Token("GTintMid", "#FDF8F0");
        var low = Token("GTintLow", "#E8DCC8");
        var face = p.Pressed ? 0.86 : p.Hover ? 1.14 : 1.0;
        var dark = StyleWorldTokens.Dark;
        // 深档把面上那层受光压薄一档：暗底上 0.15 的近白叠两遍，读起来就是一层灰蓝的纱。
        var topLight = dark ? 0.55 : 1.0;

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(w * 0.45f, h),
            new[]
            {
                hi.WithAlpha(A(0.15 * face * topLight)),
                hi.WithAlpha(A(0.10 * face * topLight)),
                mid.WithAlpha(A(0.065 * face)),
                mid.WithAlpha(A(0.06 * face)),
                low.WithAlpha(A(0.11 * face))
            },
            new[] { 0f, 0.12f, 0.42f, 0.76f, 1f },
            SKShaderTileMode.Clamp);
        using var paint = new SKPaint { IsAntialias = true, Shader = shader };
        canvas.DrawRect(0, 0, w, h, paint);
    }

    /// <summary>
    /// 世界给的色调：极淡的一层（默认 7%），让这块玻璃读起来属于当前这个世界，
    /// 而不是一块从别的界面借来的塑料。信号钮把它换成强调色并加重 —— 一屏只该有一个声音。
    /// </summary>
    private static void DrawTint(SKCanvas canvas, in GlassFaceParams p, int w, int h)
    {
        // 绝对浓淡由世界按明暗下发（GTintAmount：浅 6~10%、深 8~12%），
        // 控件自己的 TintAmount 只是相对倍率 —— 按钮 0.07、滑杆 0.05、信号钮 0.11。
        var amount = p.TintAmount * TintWeight() * (p.Hover ? 1.1 : 1.0);
        if (amount <= 0.001) return;
        var color = p.Signal ? Token("AccentWarm", "#F5D8B8") : Token("GTintMid", "#FDF8F0");
        if (p.Signal) amount = Math.Min(0.34, amount * 2.4);

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(0, h),
            new[] { color.WithAlpha(A(amount * 1.25)), color.WithAlpha(A(amount * 0.7)) },
            new[] { 0f, 1f }, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { IsAntialias = true, Shader = shader };
        canvas.DrawRect(0, 0, w, h, paint);
    }

    /// <summary>
    /// 内阴影：玻璃的厚度。三遍由宽到窄、由淡到浓，全部沿「右下 → 左上」的渐变上色，
    /// 再裁在板内 —— 右下的边缘因此比左上厚一档，这是「光从左上来」的第二种证据。
    /// 渐变在 45% 处收到一半、80% 处归零：不这么收，那层墨会一直铺到板心，
    /// 整块玻璃读起来是「一块灰玻璃」（离线校样实测：板心会掉 6% 的明度）。
    /// </summary>
    private static void DrawInnerShadow(SKCanvas canvas, in GlassFaceParams p, int w, int h, float radius,
        double density)
    {
        var strength = (p.Pressed ? 1.35 : p.Hover ? 1.1 : 1.0) * Math.Clamp(p.Thickness, 0, 1.4);
        if (strength <= 0.01) return;
        var ink = new SKColor(0, 0, 0, A(0.18 * strength));

        using var path = Rounded(0, 0, w, h, radius);
        var passes = new (float Width, float Alpha)[]
        {
            ((float)(9.0 * density), 0.50f),
            ((float)(17.0 * density), 0.70f),
            ((float)(26.0 * density), 1.00f)
        };
        foreach (var (width, alpha) in passes)
        {
            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(w, h), new SKPoint(w * 0.30f, h * 0.10f),
                new[]
                {
                    ink.WithAlpha((byte)(ink.Alpha * alpha)),
                    ink.WithAlpha((byte)(ink.Alpha * alpha * 0.45)),
                    ink.WithAlpha(0)
                },
                new[] { 0f, 0.45f, 0.80f },
                SKShaderTileMode.Clamp);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = width,
                Shader = shader
            };
            canvas.DrawPath(path, paint);
        }
    }


    /// <summary>
    /// 镜面弧：从左腰绕左上角到顶边的一条 2px 弧。它的颜色沿同一条路径衰减（左上白 0.8 → 右下 0），
    /// 所以「右下几乎没有高光」不是画了两条，而是同一条弧自身走到那里已经透明了。
    /// 外面再罩一层同样路径的宽软笔（0.09），给这条弧一点辉光 —— 参考图里最亮的就是这一笔。
    /// </summary>
    /// <summary>
    /// 镜面弧：从左腰绕左上角到顶边的一条 2px 弧。它的颜色沿同一条路径衰减（左上白 → 右下零），
    /// 所以「右下几乎没有高光」不是画了两条，而是同一条弧走到那里已经透明了 ——
    /// 参考图里最亮的就是这一笔，它是「这块玻璃被一盏灯照到」的唯一几何证据。
    ///
    /// 三个细节是离线校样逐像素比对出来的：
    ///  · 弧要**压在板内侧**约 0.7px：正好骑在边上时一半落在板外被裁掉，亮端只剩 5% 明度；
    ///  · 亮端必须接近满白（0.98）：0.86 在近白的地面上只加 5%，肉眼看不见；
    ///  · 外面再叠一层 4.5 倍宽、13% 的软笔：没有这层辉光，2px 的白线读起来是「一根描边」。
    /// </summary>
    private static void DrawSpecular(SKCanvas canvas, in GlassFaceParams p, int w, int h, float radius,
        double density, float inset = 0f)
    {
        var live = p.Pressed ? 1.15 : p.Hover ? 1.08 : 1.0;
        var inside = inset > 0 ? inset : (float)(0.7 * density);
        var width = (float)((p.Pressed ? 1.4 : 2.0) * density);

        // 高光的峰值由世界按明暗下发：暗底 0.98（一束光真的打上去了），亮底 0.80
        // （亮底上一条近纯白的弧只会显脏，这是用户「白高光在浅面上发灰」的另一半）。
        var peak = SpecularPeak();
        var k = peak / 0.98;

        using var path = new SKPath();
        var r = Math.Max(1f, radius - inside);
        var left = inside;
        path.MoveTo(new SKPoint(left, h * 0.58f));
        path.LineTo(left, inside + r);
        path.ArcTo(new SKRect(left, inside, left + 2 * r, inside + 2 * r), 180, 90, false);
        path.LineTo(Math.Min(w - r, w * 0.62f), inside);

        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(inside, h * 0.55f), new SKPoint(w * 0.72f, inside),
            new[]
            {
                SKColors.White.WithAlpha(A(0.30 * live * k)),
                SKColors.White.WithAlpha(A(peak * live)),
                SKColors.White.WithAlpha(A(0.42 * live * k)),
                SKColors.White.WithAlpha(0)
            },
            new[] { 0f, 0.18f, 0.55f, 1f },
            SKShaderTileMode.Clamp);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width,
            StrokeCap = SKStrokeCap.Round,
            Shader = shader
        };
        canvas.DrawPath(path, paint);

        using var fade = SKShader.CreateLinearGradient(
            new SKPoint(inside, h * 0.55f), new SKPoint(w * 0.72f, inside),
            new[] { SKColors.White.WithAlpha(A(0.13 * live * k)), SKColors.White.WithAlpha(0) },
            new[] { 0f, 1f }, SKShaderTileMode.Clamp);
        using var glow = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = width * 4.5f,
            StrokeCap = SKStrokeCap.Round,
            Shader = fade
        };
        canvas.DrawPath(path, glow);
    }

    private static void DrawRim(SKCanvas canvas, in GlassFaceParams p, int w, int h, float radius, double density)
    {
        using var path = Rounded(0, 0, w, h, radius);
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(w, h),
            new[]
            {
                Token("GEdgeHi", "#FFFFFF").WithAlpha(A(0.55)),
                Token("GEdgeMid", "#FDF6EE").WithAlpha(A(0.20)),
                Token("GEdgeLo", "#8B7D6B").WithAlpha(A(0.16))
            },
            new[] { 0f, 0.45f, 1f },
            SKShaderTileMode.Clamp);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            // 空心环（滑钮）的边就是它的全部轮廓，比面上的那条发丝线粗一档。
            StrokeWidth = (float)((p.Hollow ? 2.4 : 1.2) * density),
            Shader = shader
        };
        canvas.DrawPath(path, paint);
    }

    /// <summary>
    /// 没有糊底时的那块「面」：与旧版 ClearFace 同一套令牌的斜渐变。
    /// 画法一致、颜色一致，只是没有东西可以折射 —— 宁可少一层，也不能画成白块。
    /// </summary>
    private static void DrawFallbackBase(SKCanvas canvas, int w, int h)
    {
        var hi = Token("GTintTop", "#FEFCF8");
        var mid = Token("GTintMid", "#FDF8F0");
        var low = Token("GTintLow", "#E8DCC8");
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(w * 0.45f, h),
            new[]
            {
                hi.WithAlpha(A(0.20)),
                hi.WithAlpha(A(0.13)),
                mid.WithAlpha(A(0.12)),
                low.WithAlpha(A(0.16))
            },
            new[] { 0f, 0.14f, 0.55f, 1f },
            SKShaderTileMode.Clamp);
        using var paint = new SKPaint { IsAntialias = true, Shader = shader };
        canvas.DrawRect(0, 0, w, h, paint);
    }

    // ── 几何小工具 ──────────────────────────────────────────────────────

    private static SKPath Rounded(float x, float y, float w, float h, float r)
    {
        var path = new SKPath();
        var rr = Math.Clamp(r, 0, Math.Min(w, h) * 0.5f);
        path.AddRoundRect(new SKRect(x, y, x + w, y + h), rr, rr, SKPathDirection.Clockwise);
        return path;
    }

    /// <summary>环带：外圈与内圈两条轮廓合成一条路径，用 EvenOdd 填充规则 —— 裁剪时只剩中间那一圈。</summary>
    private static SKPath Ring(float x, float y, float w, float h, float r, float o0, float o1)
    {
        var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        var outerR = Math.Max(0, r - o0);
        path.AddRoundRect(new SKRect(x + o0, y + o0, x + w - o0, y + h - o0),
            outerR, outerR, SKPathDirection.Clockwise);
        var innerR = Math.Max(0, r - o1);
        path.AddRoundRect(new SKRect(x + o1, y + o1, x + w - o1, y + h - o1),
            innerR, innerR, SKPathDirection.CounterClockwise);
        return path;
    }

    private static byte A(double alpha) => (byte)Math.Clamp(Math.Round(alpha * 255), 0, 255);

    private static SKColor Token(string key, string fallback)
    {
        var c = StyleWorldTokens.Pick(key, Color.FromArgb(fallback));
        return new SKColor(
            (byte)Math.Round(c.Red * 255), (byte)Math.Round(c.Green * 255),
            (byte)Math.Round(c.Blue * 255), 255);
    }

    /// <summary>
    /// 世界下发的双精度玻璃参数（<c>GTintAmount</c> / <c>GSpecular</c>）。
    /// 拿不到（App 还没下发令牌、单测里没有 Application）时退回按明暗的合理默认 ——
    /// 宁可少一层色，也不能画成一块没有明暗判断的白板。
    /// </summary>
    private static double Num(string key, double fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var v) == true && v is double d
            ? d
            : fallback;

    /// <summary>色调相对倍率：控件 TintAmount 乘上它才是真正的 alpha。</summary>
    private static double TintWeight() =>
        Num("GTintAmount", StyleWorldTokens.Dark ? 0.11 : 0.06) / TintReference;

    /// <summary>镜面高光的峰值 alpha：暗底 0.98、亮底 0.80（规则见 StyleWorldTokens.GlassTintOf）。</summary>
    private static double SpecularPeak() => Num("GSpecular", StyleWorldTokens.Dark ? 0.98 : 0.80);

    /// <summary>
    /// 缓存键里的色调指纹。换色板/换明暗时玻璃的颜色全变了，但尺寸与糊底版本可能一个字都没动 ——
    /// 不把这三个色标和两个标量写进键里，换主题后已经合成过的玻璃会停在旧色上
    /// （糊底那条有 <see cref="FrostField.Version"/> 兜着，色标没有）。
    /// </summary>
    private static string TintSignature()
    {
        var top = Token("GTintTop", "#FEFCF8");
        var mid = Token("GTintMid", "#FDF8F0");
        var low = Token("GTintLow", "#E8DCC8");
        return string.Join('.', top.GetHashCode(), mid.GetHashCode(), low.GetHashCode(),
            Math.Round(TintWeight(), 2), Math.Round(SpecularPeak(), 2));
    }
}
