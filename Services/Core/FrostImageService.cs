using System.Security.Cryptography;
using SkiaSharp;

namespace WarmAsBefore.Services;

/// <summary>
/// 全站唯一的「磨砂底」：把壁纸（或主题地面）压成一张**真的被模糊过**的位图，缓存进应用缓存目录。
///
/// 为什么必须有它：MAUI 没有 backdrop-filter，而玻璃的物理前提是「背后有被模糊的东西」。
/// 上一版把「磨砂」做成了向奶白混色的比例，卡片面 alpha 又到 0.94 —— 于是透出来的是白色本身，
/// 读起来就是一块半透明白板。这里把模糊做在**图层**上，卡片只负责薄。
///
/// 流水线（全部在后台线程，主线程一次大图解码都没有）：
///   1. 解码期降采样 / 绘制期降采样 → 宽 <see cref="DownsampleWidth"/> px（320）
///   2. 高斯模糊 sigma = <see cref="BlurSigma"/>（在 320px 上，等于原图 ~35px，糊到只剩色块）
///   3. 去饱和 18% + 亮度归一化（把平均明度拉到当前世界的目标值）+ 103%~110% 克制对比
///   4. 混入 10% 地面色（照片档）：正文永远压在一层「自己的地面」上，而不是别人家的桌面上
///   5. 叠加 ±4.5/255 的颗粒：糊完的表面是纯色块，不给颗粒就是塑料
///   6. 放大到 <see cref="OutputWidth"/> 宽输出 PNG，文件名带源图内容 hash + 尺寸
///
/// 纯色档与窗口透视档没有壁纸，走 <see cref="Synth"/>：用主题的地面色与井色合成一张
/// 左上带光斑的斜渐变，再进同一条模糊流水线 —— 「不能没有糊底」是第一层不能失效的前提。
///
/// 只在背景（图 / 色 / 明暗）变化时算一次：同一个 key 已经就绪或正在算，直接返回。
/// </summary>
public static class FrostImageService
{
    /// <summary>降采样宽度。320 是「糊完还能看出构图、噪点又不至于被放大成麻子」的那一档。</summary>
    public const int DownsampleWidth = 320;

    /// <summary>模糊半径（在 320px 的降采样图上）。12 在 1280 宽的窗口上约等于 48px 的原图半径。</summary>
    public const float BlurSigma = 12f;

    /// <summary>输出宽度。960 交给 WinUI 的 BitmapImage 缩放，解码只要 ~2MB。</summary>
    private const int OutputWidth = 960;

    /// <summary>输出高度上限：竖图糊底不需要比这更高，多出来的像素也会被 UniformToFill 裁掉。</summary>
    private const int OutputMaxHeight = 1200;

    /// <summary>去饱和比例。全去饱和会变成灰底，一点不留又会跟卡片上的字抢颜色。</summary>
    private const double Desaturate = 0.18;

    /// <summary>归一化亮度之后的克制对比，让糊底不至于「很亮但很平」。</summary>
    private const double Contrast = 1.10;

    /// <summary>
    /// 颗粒幅度（0-255 的层级）。±6 ≈ 2.4% 的单像素噪点：隔着一层纱和一层卡面之后，
    /// 读起来就是 3~5% 的那种「表面有细砂」的感觉。再大就开始像压缩噪点了。
    /// </summary>
    private const float GrainAmount = 6.0f;

    /// <summary>照片档混入地面色的比例。这是「正文对比度」的保险，不是美学选择。</summary>
    private const double PhotoScrim = 0.10;

    /// <summary>缓存里最多留几张糊底（换壁纸/换主题各一张，旧的很快没人要）。</summary>
    private const int KeepFiles = 6;

    private static readonly object Gate = new();
    private static int _generation;
    private static string _key = "";
    private static string _buildingKey = "";
    private static string? _file;
    private static ImageSource? _source;

    /// <summary>糊底已就绪：卡片这才敢把面做到三成到四成。</summary>
    public static bool IsReady { get { lock (Gate) return _source is not null; } }

    /// <summary>糊底的 ImageSource（页面层若要用贴图代替窗口层画刷，取这里）。</summary>
    public static ImageSource? Source { get { lock (Gate) return _source; } }

    /// <summary>糊底 PNG 的绝对路径（窗口层直接读它，避免再走一遍 MAUI 的 ImageSource 管线）。</summary>
    public static string? FilePath { get { lock (Gate) return _file; } }

    /// <summary>就绪（或换了一张新的）。**在后台线程上触发**，订阅方自己切到主线程。</summary>
    public static event EventHandler? Ready;

    /// <summary>
    /// 请求一张糊底。<paramref name="imagePath"/> 为空（纯色 / 窗口透视）时合成一张。
    /// 幂等：同一个 key 已经就绪或在算就什么都不做。
    /// </summary>
    public static void Request(string? imagePath, Color ground, Color well, bool dark)
    {
        var path = !string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath) ? imagePath : "";
        var key = KeyOf(path, ground, well, dark);
        int gen;
        lock (Gate)
        {
            if (_source is not null && key == _key) return;
            if (key == _buildingKey) return;
            _buildingKey = key;
            gen = ++_generation;
        }
        _ = Task.Run(() => Build(gen, key, path, ground, well, dark));
    }

    // ── 后台流水线 ──────────────────────────────────────────────────────

    private static void Build(int gen, string key, string path, Color ground, Color well, bool dark)
    {
        try
        {
            var file = Render(gen, path, ground, well, dark);
            if (file is null) return;
            lock (Gate)
            {
                if (gen != _generation) return;   // 期间背景又换了：这份结果作废
                _file = file;
                _key = key;
                _source = ImageSource.FromFile(file);
            }
            Ready?.Invoke(null, EventArgs.Empty);
            Trim();
        }
        catch (Exception ex)
        {
            App.WriteLog("FrostImage.Build -> " + ex);
        }
        finally
        {
            // 只有「这一代还是最新的」才清占位：否则会把刚接手的那一代的占位抹掉，
            // 那张糊底就永远不会完成。
            lock (Gate) { if (gen == _generation) _buildingKey = ""; }
        }
    }

    /// <summary>
    /// 一张糊底的最小流程：解码 → 降采样 → 模糊 → 归一化 → 上色 → 编码。
    /// 源图解不开（被删、格式怪、内存不够）就静默回落成合成的纯色糊底 —— 有一张糊底总比没有强。
    /// </summary>
    private static string? Render(int gen, string path, Color ground, Color well, bool dark)
    {
        var source = path.Length > 0 ? Downscale(path, DownsampleWidth) : null;
        if (source is null) source = Synth(ground, well, dark, DownsampleWidth);
        using (source)
        {
            if (gen != Volatile.Read(ref _generation)) return null;
            using var blurred = Blur(source, BlurSigma);
            var photo = path.Length > 0;
            using var composed = Compose(blurred, ground, dark, photo);
            return Encode(composed, HashOf(path), dark);
        }
    }

    /// <summary>
    /// 解码期就降采样：JPEG 支持 scaled decode，一张 8K 图不会为了 320px 的糊底摊开几百 MB。
    /// 编解码器不支持时退回整张解码再画缩小（只在后台线程，且只发生在换背景那一次）。
    /// </summary>
    private static SKBitmap? Downscale(string path, int width)
    {
        SKBitmap? src = null;
        try
        {
            using var codec = SKCodec.Create(path);
            var info = codec?.Info ?? default;
            if (codec is not null && info.Width > 0 && info.Height > 0)
            {
                var scale = Math.Min(1f, width / (float)info.Width);
                var dim = codec.GetScaledDimensions(scale);
                var w = Math.Clamp(dim.Width > 0 ? dim.Width : info.Width, 1, info.Width);
                var h = Math.Clamp(dim.Height > 0 ? dim.Height : info.Height, 1, info.Height);
                src = SKBitmap.Decode(codec, new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
            }
        }
        catch (Exception ex) { App.WriteLog("FrostImage.Downscale(scaled) -> " + ex.Message); }

        if (src is null)
        {
            try { src = SKBitmap.Decode(path); }
            catch (Exception ex) { App.WriteLog("FrostImage.Downscale(full) -> " + ex.Message); }
        }
        if (src is null) return null;

        using (src)
        {
            var h = Math.Max(1, (int)Math.Round(width * (double)src.Height / src.Width));
            var dst = new SKBitmap(width, h, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(dst))
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
            {
                canvas.Clear(SKColors.Black);
                canvas.DrawBitmap(src, new SKRect(0, 0, width, h), paint);
                canvas.Flush();
            }
            return dst;
        }
    }

    /// <summary>
    /// 纯色档的糊底：地面上铺一层左上光斑，再叠一道斜向的井色。
    /// 它不是装饰 —— 一整块纯色糊完之后仍然是一整块纯色，玻璃照样看不出是玻璃。
    /// </summary>
    private static SKBitmap Synth(Color ground, Color well, bool dark, int width)
    {
        var h = Math.Max(1, (int)Math.Round(width * 0.62));
        var bmp = new SKBitmap(width, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        {
            var lift = dark ? 0.10f : 0.16f;
            var top = SK(ground, lift);
            using (var basePaint = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0), new SKPoint(width * 0.85f, h),
                    new[] { top, SK(ground), SK(well) }, new[] { 0f, 0.46f, 1f }, SKShaderTileMode.Clamp)
            })
                canvas.DrawRect(new SKRect(0, 0, width, h), basePaint);

            // 左上那颗「屋里的一盏灯」：糊完只剩一片明暗，正好是玻璃面上高光要说的话
            using (var glow = new SKPaint
            {
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(width * 0.26f, h * 0.14f), width * 0.80f,
                    new[] { new SKColor(255, 255, 255, (byte)(dark ? 30 : 58)), new SKColor(255, 255, 255, 0) },
                    new[] { 0f, 1f }, SKShaderTileMode.Clamp)
            })
                canvas.DrawRect(new SKRect(0, 0, width, h), glow);

            canvas.Flush();
        }
        return bmp;
    }

    /// <summary>
    /// 高斯模糊。用 Clamp 平铺把源图「向外拉出」一圈再糊，否则边缘会采样到透明，
    /// 四边糊出一圈黑边 —— 那圈黑边在浅底窗口上非常显眼。
    /// </summary>
    private static SKBitmap Blur(SKBitmap src, float sigma)
    {
        var pad = (int)Math.Ceiling(sigma * 3.5);
        // 外圈底色取源图平均色：拿黑色太错误 —— 模糊会把它采样进来，四角糊出一圈脏黑边，
        // 而浅底窗口上那圈黑边比什么高光都显眼。
        var bleed = Average(src);
        var dst = new SKBitmap(src.Width, src.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(dst))
        using (var shader = SKShader.CreateBitmap(src, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp))
        using (var paint = new SKPaint
        {
            Shader = shader,
            ImageFilter = SKImageFilter.CreateBlur(sigma, sigma),
            FilterQuality = SKFilterQuality.High
        })
        {
            canvas.Clear(bleed);
            canvas.DrawRect(new SKRect(-pad, -pad, src.Width + pad, src.Height + pad), paint);
            canvas.Flush();
        }
        return dst;
    }

    /// <summary>
    /// 放大到输出尺寸，然后一次像素遍历做完四件事：去饱和、亮度归一化、混地面色、加颗粒。
    /// 归一化用整张图的平均明度算增益，目标是「浅色世界糊完仍然亮、深色世界糊完仍然暗」——
    /// 否则一张深色壁纸会把正文直接埋掉，一张白墙又会让卡片失去层次。
    /// </summary>
    private static SKBitmap Compose(SKBitmap blurred, Color ground, bool dark, bool photo)
    {
        var outW = OutputWidth;
        var outH = Math.Clamp(
            (int)Math.Round(outW * (double)blurred.Height / blurred.Width), 1, OutputMaxHeight);
        var bmp = new SKBitmap(outW, outH, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bmp))
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
        {
            canvas.DrawBitmap(blurred, new SKRect(0, 0, outW, outH), paint);
            canvas.Flush();
        }

        var px = bmp.Pixels;
        if (px is null || px.Length == 0) return bmp;

        double sum = 0;
        var n = 0;
        for (var i = 0; i < px.Length; i += 7) { sum += Luma(px[i]); n++; }
        var mean = n > 0 ? sum / n : 0.5;

        // 目标明度跟着世界走：浅色世界 0.72，深色世界 0.24。增益限幅，免得把一张夜景拉成药水白。
        // 0.72 不是审美：页面只补 0.20 的纱，糊底本身不够亮时，深色墨在照片上就只剩 3:1。
        var target = dark ? 0.24 : 0.72;
        var gain = Math.Clamp(target / Math.Max(0.03, mean), 0.62, 1.90);
        var scrim = photo ? PhotoScrim : 0.0;
        var sr = ground.Red;
        var sg = ground.Green;
        var sb = ground.Blue;

        // 固定种子：同一张糊底的颗粒永远长在同一个位置，背景不会「沙沙」地跳
        var rnd = new Random(20261127);

        for (var i = 0; i < px.Length; i++)
        {
            var p = px[i];
            double r = p.Red / 255.0, g = p.Green / 255.0, b = p.Blue / 255.0;
            var l = 0.2126 * r + 0.7152 * g + 0.0722 * b;

            // 去饱和：糊完的色块饱和度通常偏高，降一档才像「隔着毛玻璃看」
            r += (l - r) * Desaturate;
            g += (l - g) * Desaturate;
            b += (l - b) * Desaturate;

            // 归一化 + 克制对比（围绕目标明度而不是 0.5，免得整张图往亮处漂）
            r = target + (r * gain - target) * Contrast;
            g = target + (g * gain - target) * Contrast;
            b = target + (b * gain - target) * Contrast;

            // 亮部软压缩：增益一拉，白墙就会糊出一大块死白（纯 255 的平斑看起来像贴纸）。
            // 这里把它渐近到 1，保住「那片白原本有多亮」的层次。
            r = Knee(r); g = Knee(g); b = Knee(b);

            if (scrim > 0)
            {
                r += (sr - r) * scrim;
                g += (sg - g) * scrim;
                b += (sb - b) * scrim;
            }

            var jitter = (rnd.NextDouble() - 0.5) * GrainAmount / 255.0;
            r += jitter;
            g += jitter;
            b += jitter;

            px[i] = new SKColor(Byte(r), Byte(g), Byte(b), 255);
        }

        bmp.Pixels = px;
        return bmp;
    }

    private static string? Encode(SKBitmap bmp, string hash, bool dark)
    {
        var dir = Path.Combine(FileSystem.CacheDirectory, "frost");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"frost_{hash}_{bmp.Width}x{bmp.Height}_{(dark ? "d" : "l")}.png");
        var tmp = file + ".tmp";
        using (var image = SKImage.FromBitmap(bmp))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 92))
        using (var fs = System.IO.File.Create(tmp))
            data.SaveTo(fs);
        System.IO.File.Move(tmp, file, true);
        return file;
    }

    /// <summary>旧糊底只留最近几张：每换一次壁纸就多一张 960px 的 PNG，不清理会一直长。</summary>
    private static void Trim()
    {
        try
        {
            var dir = Path.Combine(FileSystem.CacheDirectory, "frost");
            if (!Directory.Exists(dir)) return;
            string? current;
            lock (Gate) current = _file;
            var old = Directory.GetFiles(dir, "frost_*.png")
                .Where(p => !string.Equals(p, current, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(p => System.IO.File.GetLastWriteTimeUtc(p))
                .Skip(KeepFiles - 1)
                .ToList();
            foreach (var p in old) { try { System.IO.File.Delete(p); } catch { /* 故意忽略：删缓存失败不影响糊底 */ } }
        }
        catch (Exception ex) { App.WriteLog("FrostImage.Trim -> " + ex.Message); }
    }

    // ── 小工具 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 去重用的指纹：路径 + 大小 + 修改时间。**刻意不读文件内容** ——
    /// Request 是在 UI 线程上被设置下发调用的，这里读 256KB 就是一次没必要的磁盘 IO。
    /// 内容 hash 只在后台算一次，用来命名落盘文件（见 <see cref="HashOf"/>）。
    /// </summary>
    private static string KeyOf(string path, Color ground, Color well, bool dark)
    {
        var src = "solid";
        if (path.Length > 0)
        {
            try
            {
                var fi = new FileInfo(path);
                src = $"{fi.FullName.GetHashCode():x8}_{fi.Length}_{fi.LastWriteTimeUtc.Ticks}";
            }
            catch { src = "unreadable_" + path.GetHashCode().ToString("x8"); }
        }
        return $"{src}_{ground.ToArgbHex()}_{well.ToArgbHex()}_{(dark ? 'd' : 'l')}_{OutputWidth}";
    }

    /// <summary>源图**内容** hash（前 256KB）：改了图但没改名也会重算，而不是继续用旧的糊底。</summary>
    private static string HashOf(string path)
    {
        if (path.Length == 0 || !System.IO.File.Exists(path)) return "solid";
        try
        {
            using var fs = System.IO.File.OpenRead(path);
            var len = (int)Math.Min(262144, fs.Length);
            var buf = new byte[Math.Max(1, len)];
            fs.ReadExactly(buf, 0, buf.Length);
            return Convert.ToHexString(SHA256.HashData(buf))[..16].ToLowerInvariant();
        }
        catch
        {
            return "unreadable_" + path.GetHashCode().ToString("x8");
        }
    }

    /// <summary>源图平均色（抽样），只用来铺模糊的外圈。</summary>
    private static SKColor Average(SKBitmap bmp)
    {
        var px = bmp.Pixels;
        if (px is null || px.Length == 0) return SKColors.Gray;
        long r = 0, g = 0, b = 0;
        var n = 0;
        for (var i = 0; i < px.Length; i += 13) { r += px[i].Red; g += px[i].Green; b += px[i].Blue; n++; }
        if (n == 0) return SKColors.Gray;
        return new SKColor((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }

    private static SKColor SK(Color c, float lift = 0f) => new(
        Byte(c.Red + (1 - c.Red) * lift), Byte(c.Green + (1 - c.Green) * lift), Byte(c.Blue + (1 - c.Blue) * lift), 255);

    private static double Luma(SKColor c) =>
        (0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue) / 255.0;

    /// <summary>亮部软膝：0.86 以下原样，之上渐近到 1（1.3 只到 0.98），绝不硬切。</summary>
    private static double Knee(double v)
    {
        if (v <= 0.86) return Math.Max(0, v);
        return 0.86 + 0.14 * (1 - Math.Exp(-(v - 0.86) * 4.5));
    }

    private static byte Byte(double v) => (byte)Math.Clamp(v * 255.0 + 0.5, 0, 255);
}
