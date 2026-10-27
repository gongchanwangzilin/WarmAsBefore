using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace WarmAsBefore.Modules.Outfit;

/// <summary>
/// 合成请求。Width/Height &lt;= 0 时沿用形象包画布逻辑尺寸。
/// MotionId 为空是静态表情；非空时 FrameIndex 决定取动作里的第几帧（越界会循环）。
/// </summary>
public sealed record FigureRenderRequest
{
    public string? OutfitId { get; init; }
    public string? SchemeId { get; init; }
    public string? Emotion { get; init; }
    public string? MotionId { get; init; }
    public int FrameIndex { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
}

/// <summary>
/// SkiaSharp 合成器：把部件按 z 序叠到透明画布，应用服装覆写、槽位配色与整体色调。
/// 不依赖 WebGL / WebView，输出是带 alpha 的 PNG；
/// 缓存放在形象包自己的 .cache 目录下，便于随包一起清理。
///
/// 线程安全：同一个缓存文件的写入用 Lazy 单飞合并；文件先写临时名再原子替换，
/// UI 线程连续点换装不会产生半张图。
/// </summary>
public sealed class FigureComposer
{
    /// <summary>单边上限：4K 足够桌面宠物与大屏立绘，也避免 Android 上一次性吃掉过多内存。</summary>
    public const int MaxOutputEdge = 4096;

    private const long MaxOutputPixels = 16_000_000;
    private const int MaxDecodeEdge = 4096;
    private const string CacheVersion = "v1";

    // 指纹扫描只是为了让手改贴图尽快生效；动画逐帧调用时不必每帧都扫。
    private static readonly TimeSpan SignatureTtl = TimeSpan.FromSeconds(2);

    // 以包目录为键，缓存最近一次“figure.json 版本 + 全包贴图指纹”。
    private sealed record SignatureEntry(string JsonStamp, string Signature, DateTime ComputedUtc);

    private static readonly ConcurrentDictionary<string, Lazy<Task<string?>>> Inflight =
        new(StringComparer.Ordinal);

    // 动作每帧都会走 RenderCachedAsync；素材指纹要扫全包贴图，不能每帧都做。
    // 以 figure.json 的 mtime 为版本，2 秒内复用上次扫描结果——既让动画请求便宜，
    // 又能在手改贴图后很快生效（流水线写 figure.json 会立刻换版本）。
    private static readonly ConcurrentDictionary<string, SignatureEntry> SignatureCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 合成并返回 PNG 字节。任何异常都在这里收口，返回 null 并写日志，调用方（UI）不需要 try。
    /// </summary>
    public byte[]? ComposePng(FigurePack pack, string packDirectory, FigureRenderRequest request)
    {
        if (pack is null || request is null || string.IsNullOrWhiteSpace(packDirectory))
        {
            App.WriteLog("FigureComposer.ComposePng -> 参数为空");
            return null;
        }

        try
        {
            var (targetW, targetH) = ResolveTarget(pack, request.Width, request.Height);
            var canvasW = pack.CanvasWidth > 0 ? pack.CanvasWidth : targetW;
            var canvasH = pack.CanvasHeight > 0 ? pack.CanvasHeight : targetH;
            var outfit = FindOutfit(pack, request.OutfitId);
            var scheme = FindScheme(pack, request.SchemeId);
            var frame = ResolveFrame(pack, request.MotionId, request.FrameIndex);

            var layers = new List<Layer>();
            var fullFrame = false;

            // 整帧动作贴图：视为完整画面，只额外叠该帧声明的部件覆写，
            // 避免「整身帧 + 基础身体」叠出两个身子。
            if (frame is not null && !string.IsNullOrWhiteSpace(frame.Texture))
            {
                var fullPath = ResolveInsidePack(packDirectory, frame.Texture);
                if (fullPath is not null && File.Exists(fullPath))
                {
                    layers.Add(new Layer(null, fullPath, FullCanvas: true, Required: false));
                    fullFrame = true;
                }
                else
                {
                    App.WriteLog("FigureComposer: 整帧贴图缺失，回退到部件拼接 -> " + frame.Texture);
                }
            }

            if (fullFrame)
            {
                foreach (var part in PartsOf(pack))
                {
                    if (!TryGetIgnoreCase(frame!.PartOverrides, part.Id, out var rel)) continue;
                    if (string.IsNullOrWhiteSpace(rel)) continue; // 空值 = 隐藏
                    var abs = ResolveInsidePack(packDirectory, rel);
                    if (abs is null || !File.Exists(abs))
                    {
                        App.WriteLog("FigureComposer: 动作覆写贴图缺失 -> " + rel);
                        continue;
                    }
                    layers.Add(new Layer(part, abs, FullCanvas: false, Required: false));
                }
            }
            else
            {
                var fatal = false;
                foreach (var part in PartsOf(pack))
                {
                    var layer = ResolvePartLayer(part, outfit, frame, request.Emotion, packDirectory, ref fatal);
                    if (layer is not null) layers.Add(layer);
                }
                if (fatal)
                {
                    // 基础部件都缺图，硬合成只会得到一只缺胳膊少腿的角色；按约定返回 null 让 UI 回退。
                    App.WriteLog("FigureComposer: 基础部件贴图缺失，放弃合成");
                    return null;
                }
            }

            if (layers.Count == 0)
            {
                App.WriteLog("FigureComposer: 没有任何可绘制图层");
                return null;
            }

            using var bitmap = new SKBitmap(targetW, targetH, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);

            var drawn = 0;
            var ordered = layers
                .Select((layer, index) => (layer, index))
                .OrderBy(t => t.layer.Part?.Z ?? int.MinValue)
                .ThenBy(t => t.index);

            foreach (var (layer, _) in ordered)
            {
                var opacity = ResolveOpacity(layer.Part, scheme);
                var multiply = ResolveMultiplyColor(layer.Part, outfit, scheme);
                using var filter = CreateFilter(multiply);
                using var paint = new SKPaint
                {
                    IsAntialias = true,
                    FilterQuality = SKFilterQuality.High,
                    Color = new SKColor(255, 255, 255, (byte)Math.Round(opacity * 255)),
                    ColorFilter = filter,
                };

                if (layer.FullCanvas)
                {
                    var info = ProbeTexture(layer.AbsolutePath);
                    if (info is null)
                    {
                        App.WriteLog("FigureComposer: 整帧贴图无法解码 -> " + layer.AbsolutePath);
                        continue;
                    }

                    using var fullBitmap = DecodeTexture(layer.AbsolutePath, targetW, targetH);
                    if (fullBitmap is null)
                    {
                        App.WriteLog("FigureComposer: 整帧贴图解码失败 -> " + layer.AbsolutePath);
                        continue;
                    }
                    canvas.DrawBitmap(fullBitmap, new SKRect(0, 0, targetW, targetH), paint);
                    drawn++;
                    continue;
                }

                var part = layer.Part!;
                var probe = ProbeTexture(layer.AbsolutePath);
                if (probe is null)
                {
                    if (layer.Required)
                    {
                        App.WriteLog("FigureComposer: 基础部件贴图无法解码 -> " + layer.AbsolutePath);
                        return null;
                    }
                    App.WriteLog("FigureComposer: 覆写贴图无法解码 -> " + layer.AbsolutePath);
                    continue;
                }

                var scaleX = targetW / (double)canvasW;
                var scaleY = targetH / (double)canvasH;
                var partScale = IsFinite(part.Scale) && part.Scale > 0 ? part.Scale : 1;
                var destW = probe.Value.W * partScale * scaleX;
                var destH = probe.Value.H * partScale * scaleY;
                if (destW < 1 || destH < 1) continue;

                var anchorX = Math.Clamp(IsFinite(part.AnchorX) ? part.AnchorX : 0.5, 0, 1);
                var anchorY = Math.Clamp(IsFinite(part.AnchorY) ? part.AnchorY : 0.5, 0, 1);
                var centerX = ((IsFinite(part.X) ? part.X : 0.5) * canvasW + (IsFinite(part.OffsetX) ? part.OffsetX : 0)) * scaleX;
                var centerY = ((IsFinite(part.Y) ? part.Y : 0.5) * canvasH + (IsFinite(part.OffsetY) ? part.OffsetY : 0)) * scaleY;
                var left = centerX - anchorX * destW;
                var top = centerY - anchorY * destH;
                var destRect = new SKRect((float)left, (float)top, (float)(left + destW), (float)(top + destH));

                using var partBitmap = DecodeTexture(layer.AbsolutePath, (int)Math.Ceiling(destW), (int)Math.Ceiling(destH));
                if (partBitmap is null)
                {
                    if (layer.Required)
                    {
                        App.WriteLog("FigureComposer: 基础部件贴图解码失败 -> " + layer.AbsolutePath);
                        return null;
                    }
                    App.WriteLog("FigureComposer: 覆写贴图解码失败 -> " + layer.AbsolutePath);
                    continue;
                }

                canvas.DrawBitmap(partBitmap, destRect, paint);
                drawn++;
            }

            if (drawn == 0)
            {
                App.WriteLog("FigureComposer: 图层全部解码失败，未生成图片");
                return null;
            }

            canvas.Flush();
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data?.ToArray();
        }
        catch (Exception ex)
        {
            App.WriteLog("FigureComposer.ComposePng -> " + ex);
            return null;
        }
    }

    /// <summary>
    /// 带缓存合成：key 由形象包/服装/配色/表情/动作帧/尺寸 + 素材文件时间戳组成。
    /// 同一 key 并发请求只合成一次；命中已有 PNG 直接返回绝对路径。
    /// </summary>
    public async Task<string?> RenderCachedAsync(FigurePack pack, string packDirectory, FigureRenderRequest request)
    {
        if (pack is null || request is null || string.IsNullOrWhiteSpace(packDirectory)) return null;

        try
        {
            var key = BuildCacheKey(pack, packDirectory, request);
            var cacheDir = Path.Combine(packDirectory, ".cache");
            var cachePath = Path.GetFullPath(Path.Combine(cacheDir, key + ".png"));

            if (IsUsableFile(cachePath)) return cachePath;

            var lazy = Inflight.GetOrAdd(cachePath, path => new Lazy<Task<string?>>(
                () => Task.Run(() => RenderAndWrite(pack, packDirectory, request, path))));

            try
            {
                return await lazy.Value.ConfigureAwait(false);
            }
            finally
            {
                // 无论成败都移除：成功时文件已在盘上，失败时下一次请求可以重试。
                Inflight.TryRemove(cachePath, out _);
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("FigureComposer.RenderCachedAsync -> " + ex);
            return null;
        }
    }

    /// <summary>
    /// 缓存键：可读段（服装/配色/表情/动作帧/尺寸）+ 素材指纹。
    /// 指纹把 figure.json 与相关贴图的修改时间/长度算进去，换图后不会继续吐旧缓存。
    /// </summary>
    public string BuildCacheKey(FigurePack pack, string packDirectory, FigureRenderRequest request)
    {
        var (w, h) = ResolveTarget(pack, request.Width, request.Height);
        var outfit = SafeSegment(request.OutfitId, "base");
        var scheme = SafeSegment(request.SchemeId, "default");
        var emotion = SafeSegment(request.Emotion, "none");
        var motion = SafeSegment(request.MotionId, "static");
        var frame = Math.Max(0, request.FrameIndex);
        var signature = AssetSignature(pack, packDirectory);

        var canonical = string.Join("|", new[]
        {
            CacheVersion,
            pack.Id ?? "",
            request.OutfitId ?? "",
            request.SchemeId ?? "",
            request.Emotion ?? "",
            request.MotionId ?? "",
            frame.ToString(),
            w.ToString(),
            h.ToString(),
            signature,
        });

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant()[..8];

        return $"{CacheVersion}_{outfit}_{scheme}_{emotion}_{motion}_{frame}_{w}x{h}_{hash}";
    }

    // ── 缓存写入 ────────────────────────────────────────────────────────────

    private string? RenderAndWrite(FigurePack pack, string packDirectory, FigureRenderRequest request, string cachePath)
    {
        string? tmp = null;
        try
        {
            var bytes = ComposePng(pack, packDirectory, request);
            if (bytes is null || bytes.Length == 0) return null;

            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            // 同键已经有单飞锁；这里仍用唯一临时名 + 替换，防跨进程/上层重复调用写坏文件。
            tmp = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, cachePath, true);
            return cachePath;
        }
        catch (Exception ex)
        {
            App.WriteLog("FigureComposer.RenderAndWrite -> " + ex);
            return null;
        }
        finally
        {
            if (tmp is not null)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }
    }

    private static bool IsUsableFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    // ── 图层解析 ────────────────────────────────────────────────────────────

    private sealed record Layer(FigurePart? Part, string AbsolutePath, bool FullCanvas, bool Required);

    private static Layer? ResolvePartLayer(
        FigurePart part,
        FigureOutfit? outfit,
        FigureMotionFrame? frame,
        string? emotion,
        string packDirectory,
        ref bool fatal)
    {
        // 逐级覆写：动作帧 → 表情 → 服装 → 基础。空字符串是「隐藏」；
        // 非空但文件不存在则继续往下一级找，避免一套衣服缺一张图就整个人消失。
        var candidates = new List<string>();

        if (frame is not null && TryGetIgnoreCase(frame.PartOverrides, part.Id, out var framePath))
        {
            if (string.IsNullOrWhiteSpace(framePath)) return null; // 动作帧点名隐藏
            candidates.Add(framePath);
        }

        if (!string.IsNullOrWhiteSpace(emotion) &&
            TryGetIgnoreCase(part.EmotionTextures, emotion, out var emotionPath))
        {
            if (string.IsNullOrWhiteSpace(emotionPath)) return null; // 表情点名隐藏
            candidates.Add(emotionPath);
        }

        if (outfit is not null && TryGetIgnoreCase(outfit.Overrides, part.Id, out var outfitPath))
        {
            if (string.IsNullOrWhiteSpace(outfitPath)) return null; // 服装点名隐藏
            candidates.Add(outfitPath);
        }

        var baseRel = part.Texture ?? "";
        if (!string.IsNullOrWhiteSpace(baseRel)) candidates.Add(baseRel);

        foreach (var rel in candidates)
        {
            var abs = ResolveInsidePack(packDirectory, rel);
            if (abs is not null && File.Exists(abs))
            {
                var isBase = string.Equals(rel, baseRel, StringComparison.OrdinalIgnoreCase);
                return new Layer(part, abs, FullCanvas: false, Required: isBase);
            }
            App.WriteLog("FigureComposer: 部件贴图缺失 -> " + rel);
        }

        // 走到这里说明该部件所有候选都不存在。基础贴图声明过却缺文件，视为包损坏；
        // 没有基础贴图的占位部件则安静跳过。
        if (!string.IsNullOrWhiteSpace(baseRel)) fatal = true;
        return null;
    }

    private static IEnumerable<FigurePart> PartsOf(FigurePack pack)
        => (pack.Parts ?? new List<FigurePart>()).Where(p => p is not null);

    private static FigureOutfit? FindOutfit(FigurePack pack, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return (pack.Outfits ?? new List<FigureOutfit>()).FirstOrDefault(
            o => o is not null && string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private static FigureScheme? FindScheme(FigurePack pack, string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return (pack.Schemes ?? new List<FigureScheme>()).FirstOrDefault(
            s => s is not null && string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private static FigureMotionFrame? ResolveFrame(FigurePack pack, string? motionId, int frameIndex)
    {
        if (string.IsNullOrWhiteSpace(motionId)) return null;
        var motion = (pack.Motions ?? new List<FigureMotion>()).FirstOrDefault(
            m => m is not null && string.Equals(m.Id, motionId, StringComparison.OrdinalIgnoreCase));
        var frames = (motion?.Frames ?? new List<FigureMotionFrame>()).Where(f => f is not null).ToList();
        if (frames.Count == 0) return null;

        var index = ((frameIndex % frames.Count) + frames.Count) % frames.Count;
        return frames[index];
    }

    private static bool TryGetIgnoreCase(Dictionary<string, string>? map, string? key, out string value)
    {
        value = "";
        if (map is null || string.IsNullOrWhiteSpace(key)) return false;
        if (map.TryGetValue(key, out var exact))
        {
            value = exact ?? "";
            return true;
        }
        foreach (var (k, v) in map)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                value = v ?? "";
                return true;
            }
        }
        return false;
    }

    // ── 颜色 / 透明度 ──────────────────────────────────────────────────────

    private static double ResolveOpacity(FigurePart? part, FigureScheme? scheme)
    {
        var schemeOpacity = scheme is not null && IsFinite(scheme.Opacity) ? scheme.Opacity : 1;
        var partOpacity = part is not null && IsFinite(part.Opacity) ? part.Opacity : 1;
        return Math.Clamp(schemeOpacity * partOpacity, 0, 1);
    }

    private static SKColor ResolveMultiplyColor(FigurePart? part, FigureOutfit? outfit, FigureScheme? scheme)
    {
        var slotColor = SKColors.White;
        if (part is not null && scheme is not null &&
            TryGetIgnoreCase(scheme.SlotColors, part.Slot, out var hex) &&
            !string.IsNullOrWhiteSpace(hex))
        {
            slotColor = ParseColor(hex);
        }

        var tint = string.IsNullOrWhiteSpace(outfit?.Tint) ? SKColors.White : ParseColor(outfit!.Tint);
        return Multiply(slotColor, tint);
    }

    private static SKColor ParseColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return SKColors.White;
        try
        {
            return SKColor.Parse(text.Trim());
        }
        catch
        {
            App.WriteLog("FigureComposer: 颜色无法解析 -> " + text);
            return SKColors.White;
        }
    }

    private static SKColor Multiply(SKColor a, SKColor b) => new(
        (byte)(a.Red * b.Red / 255),
        (byte)(a.Green * b.Green / 255),
        (byte)(a.Blue * b.Blue / 255),
        (byte)(a.Alpha * b.Alpha / 255));

    private static SKColorFilter? CreateFilter(SKColor color)
    {
        if (color.Red == 255 && color.Green == 255 && color.Blue == 255 && color.Alpha == 255)
            return null;
        return SKColorFilter.CreateBlendMode(color, SKBlendMode.Modulate);
    }

    // ── 贴图读取 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 只读文件头拿原始尺寸。先知道多大，才能决定解码到多少像素，避免为了画一个 300px 的部件吃下整张 8K。
    /// </summary>
    private static (int W, int H)? ProbeTexture(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream);
            if (codec is null) return null;
            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0) return null;
            return (info.Width, info.Height);
        }
        catch (Exception ex)
        {
            App.WriteLog("FigureComposer.ProbeTexture(" + path + ") -> " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 按目标像素降采样解码；ReturnType 带 alpha 的 premul 位图，PNG 合成才有透明边。
    /// </summary>
    private static SKBitmap? DecodeTexture(string path, int wantedW, int wantedH)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream);
            if (codec is null) return null;

            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0) return null;

            var decW = Math.Clamp(wantedW, 1, Math.Min(info.Width, MaxDecodeEdge));
            var decH = Math.Clamp(wantedH, 1, Math.Min(info.Height, MaxDecodeEdge));
            var target = new SKImageInfo(decW, decH, SKColorType.Rgba8888, SKAlphaType.Premul);
            var decoded = SKBitmap.Decode(codec, target);
            if (decoded is not null) return decoded;

            // 少数编码器不支持缩放解码：图不大时才退回全解码，大图宁可放弃也不冒 OOM 风险。
            if ((long)info.Width * info.Height <= MaxOutputPixels &&
                info.Width <= MaxDecodeEdge * 2 && info.Height <= MaxDecodeEdge * 2)
            {
                return SKBitmap.Decode(path);
            }
            return null;
        }
        catch (Exception ex)
        {
            App.WriteLog("FigureComposer.DecodeTexture(" + path + ") -> " + ex.Message);
            return null;
        }
    }

    // ── 路径 / 尺寸 / 指纹 ──────────────────────────────────────────────────

    private static (int W, int H) ResolveTarget(FigurePack pack, int width, int height)
    {
        var w = width > 0 ? width : (pack.CanvasWidth > 0 ? pack.CanvasWidth : 1080);
        var h = height > 0 ? height : (pack.CanvasHeight > 0 ? pack.CanvasHeight : 1920);
        w = Math.Clamp(w, 1, MaxOutputEdge);
        h = Math.Clamp(h, 1, MaxOutputEdge);

        var pixels = (long)w * h;
        if (pixels > MaxOutputPixels)
        {
            var factor = Math.Sqrt(MaxOutputPixels / (double)pixels);
            w = Math.Max(1, (int)Math.Floor(w * factor));
            h = Math.Max(1, (int)Math.Floor(h * factor));
        }
        return (w, h);
    }

    /// <summary>只允许包目录内的相对路径，挡住 JSON 里写 ../.. 把任意文件读进画面的可能。</summary>
    private static string? ResolveInsidePack(string packDirectory, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        try
        {
            var root = Path.GetFullPath(packDirectory);
            // 素材包可能在 Windows 上手写、在 Android 上运行：两种分隔符都收。
            var normalized = relative.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
            var combined = Path.IsPathRooted(normalized)
                ? normalized
                : Path.Combine(root, normalized);
            var full = Path.GetFullPath(combined);
            var rel = Path.GetRelativePath(root, full);
            if (rel == ".." || rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                Path.IsPathRooted(rel))
                return null;
            return full;
        }
        catch
        {
            return null;
        }
    }

    private static string AssetSignature(FigurePack pack, string packDirectory)
    {
        // 动作播放时每帧都会构建缓存键：先用 figure.json 的版本判断，
        // 两秒内直接复用指纹，避免按帧率 stat 整个包的贴图。
        var fullDirectory = Path.GetFullPath(packDirectory);
        var jsonPath = Path.Combine(packDirectory, "figure.json");
        var jsonStamp = FileStamp(jsonPath);

        if (SignatureCache.TryGetValue(fullDirectory, out var cached)
            && string.Equals(cached.JsonStamp, jsonStamp, StringComparison.Ordinal)
            && DateTime.UtcNow - cached.ComputedUtc < SignatureTtl)
        {
            return cached.Signature;
        }

        var sb = new StringBuilder();
        AppendAbsoluteStamp(sb, jsonPath);

        foreach (var part in PartsOf(pack))
        {
            AppendRelativeStamp(sb, packDirectory, part.Texture);
            foreach (var value in (part.EmotionTextures ?? new Dictionary<string, string>()).Values)
                AppendRelativeStamp(sb, packDirectory, value);
        }

        foreach (var outfit in pack.Outfits ?? new List<FigureOutfit>())
        {
            if (outfit is null) continue;
            foreach (var value in (outfit.Overrides ?? new Dictionary<string, string>()).Values)
                AppendRelativeStamp(sb, packDirectory, value);
        }

        foreach (var motion in pack.Motions ?? new List<FigureMotion>())
        {
            if (motion is null) continue;
            foreach (var frame in motion.Frames ?? new List<FigureMotionFrame>())
            {
                if (frame is null) continue;
                AppendRelativeStamp(sb, packDirectory, frame.Texture);
                foreach (var value in (frame.PartOverrides ?? new Dictionary<string, string>()).Values)
                    AppendRelativeStamp(sb, packDirectory, value);
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        var signature = Convert.ToHexString(hash).ToLowerInvariant()[..12];
        SignatureCache[fullDirectory] = new SignatureEntry(jsonStamp, signature, DateTime.UtcNow);
        return signature;
    }

    // 版本戳只用于判断“是否要重新扫贴图”，取不到文件时返回 0，交给后续逻辑按缺失处理。
    private static string FileStamp(string? absolutePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath)) return "0";
            var info = new FileInfo(absolutePath);
            return info.LastWriteTimeUtc.Ticks + "/" + info.Length;
        }
        catch
        {
            return "0";
        }
    }

    private static void AppendRelativeStamp(StringBuilder sb, string packDirectory, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            sb.Append("-;");
            return;
        }
        var abs = ResolveInsidePack(packDirectory, relative);
        AppendAbsoluteStamp(sb, abs, relative);
    }

    private static void AppendAbsoluteStamp(StringBuilder sb, string? absolutePath, string? label = null)
    {
        sb.Append(label ?? absolutePath).Append(':');
        try
        {
            if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            {
                sb.Append('0');
            }
            else
            {
                var info = new FileInfo(absolutePath);
                sb.Append(info.LastWriteTimeUtc.Ticks).Append('/').Append(info.Length);
            }
        }
        catch
        {
            sb.Append('0');
        }
        sb.Append(';');
    }

    private static string SafeSegment(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            if (ch < 128 && (char.IsLetterOrDigit(ch) || ch is '-' or '_')) sb.Append(ch);
            if (sb.Length >= 24) break;
        }
        return sb.Length == 0 ? fallback : sb.ToString();
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
