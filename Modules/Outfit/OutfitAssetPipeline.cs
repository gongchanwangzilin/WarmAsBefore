using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using SkiaSharp;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Outfit;

/// <summary>
/// 素材表切分参数。边距/间隔按「源素材图像素」计；超大图会被整体等比缩到 4096 再切，
/// 此时边距/间隔跟着缩放，保证格子划分与视觉位置一致。
/// </summary>
public sealed record OutfitSliceOptions
{
    public int Rows { get; init; } = 1;
    public int Columns { get; init; } = 1;

    public int MarginLeft { get; init; }
    public int MarginTop { get; init; }
    public int MarginRight { get; init; }
    public int MarginBottom { get; init; }
    public int GapX { get; init; }
    public int GapY { get; init; }

    /// <summary>alpha 大于该值的像素视为实体，用于修剪透明边。</summary>
    public int AlphaThreshold { get; init; } = 8;

    /// <summary>修剪后向外保留的透明像素，避免抗锯齿边缘被切掉。</summary>
    public int Padding { get; init; } = 2;

    /// <summary>新建形象包时的画布逻辑尺寸；更新已有包时不覆盖原值。</summary>
    public int CanvasWidth { get; init; } = 1080;
    public int CanvasHeight { get; init; } = 1920;

    /// <summary>没有给 <see cref="FigurePartSeed"/> 时，新部件的默认槽位。</summary>
    public string DefaultSlot { get; init; } = "accessory";

    public string PackName { get; init; } = "";
}

/// <summary>
/// 单个格子的部件定义。按行优先顺序与素材表格子一一对应；
/// 不给时按 part_行_列 自动命名，锚点/位置居中，方便先出图再手工调。
/// </summary>
public sealed record FigurePartSeed
{
    public string Id { get; init; } = "";
    public string Slot { get; init; } = "accessory";
    public int Z { get; init; } = 10;
    public double AnchorX { get; init; } = 0.5;
    public double AnchorY { get; init; } = 0.5;
    public double X { get; init; } = 0.5;
    public double Y { get; init; } = 0.5;
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
    public double Scale { get; init; } = 1;
    public double Opacity { get; init; } = 1;
}

/// <summary>素材生成/切图结果。失败也返回对象，UI 直接显示 Message 即可。</summary>
public sealed record OutfitPipelineResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public string? PackDirectory { get; init; }
    public List<string> WrittenFiles { get; init; } = new();
}

/// <summary>动作导入结果（帧序列与视频共用）。</summary>
public sealed record MotionImportResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public string? PackDirectory { get; init; }
    public string MotionId { get; init; } = "";
    public int FrameCount { get; init; }
}

/// <summary>
/// AI 着装素材流水线：生图模型画一张「部件素材表」→ 这里按网格切开 → alpha 修剪 → 写入形象包。
/// 这是 COOPANION「素材表 → 切图」思路的 C# 版，但不依赖 WebView/HTML/FFmpeg。
///
/// 所有失败都转成中文结构化结果，不向 UI 抛异常；抽帧工具缺失时明确说清楚，不伪造成功。
/// 同一形象包的写操作用 SemaphoreSlim 串行，避免两次生成同时改 figure.json。
/// </summary>
public sealed class OutfitAssetPipeline
{
    private const int MaxSheetEdge = 4096;

    private static readonly string[] FrameExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

    // Windows 上这些名字不能做文件名；跨平台统一加前缀，避免同一包在桌面上生成失败。
    private static readonly string[] ReservedFileStems =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly JsonSerializerOptions WriteJson = new()
    {
        WriteIndented = true,
        // 手写/查看 figure.json 时中文保持原样，方便排查素材问题。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly StorageProvider _store;
    private readonly ImageGenerationService _images;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);

    public OutfitAssetPipeline(StorageProvider store, ImageGenerationService images)
    {
        _store = store;
        _images = images;
    }

    public string FiguresRoot => Path.Combine(_store.Root, "assets", "figures");

    /// <summary>本机是否找到可用的 FFmpeg（环境变量 / 应用 tools 目录 / PATH）。</summary>
    public static bool HasVideoFrameExtractor => FindFfmpeg() is not null;

    /// <summary>
    /// 生成部件素材表并切图入库。生成失败、未配置模型、格子参数越界都会返回 Success=false 与原因。
    /// </summary>
    public async Task<OutfitPipelineResult> GeneratePartsAsync(
        string packId,
        string prompt,
        OutfitSliceOptions? slice,
        IReadOnlyList<FigurePartSeed>? parts = null,
        string? sheetSize = null)
    {
        if (!FigurePackService.IsValidPackId(packId))
            return Fail("形象包 id 不合法（不能带路径分隔符或 ..）。");
        if (string.IsNullOrWhiteSpace(prompt))
            return Fail("先写一句素材描述，例如：同一位少女的头发、上衣、裙子、鞋子分件。");

        slice ??= new OutfitSliceOptions();
        if (!_images.Enabled)
            return Fail("画面生成没有开启（设置 → AI → 画面生成 里填接口与密钥）。");

        var gate = GateFor(packId);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var dir = Path.Combine(FiguresRoot, packId);
            var (existing, loadError) = await LoadExistingOrNewAsync(dir, packId, slice).ConfigureAwait(false);
            if (existing is null || loadError is not null)
                return Fail(loadError ?? "形象包读取失败。");

            var (sheetPath, message) = await _images.GenerateAsync(
                BuildSheetPrompt(prompt, slice), "figure_" + packId, sheetSize).ConfigureAwait(false);
            if (sheetPath is null)
                return Fail(message);

            var written = new List<string>();
            var generated = SliceSheet(sheetPath, dir, slice, parts, written, out var sliceError);
            if (sliceError is not null) return Fail(sliceError);
            if (generated.Count == 0)
                return Fail("素材表里没有找到不透明的部件，figure.json 未改动。");

            var pack = ApplyGeneratedParts(existing, generated, slice);
            var saveError = await SavePackAsync(dir, pack).ConfigureAwait(false);
            if (saveError is not null) return Fail(saveError);

            return new OutfitPipelineResult
            {
                Success = true,
                Message = $"已生成并写入 {generated.Count} 个部件。",
                PackDirectory = dir,
                WrittenFiles = written,
            };
        }
        catch (Exception ex)
        {
            App.WriteLog("OutfitAssetPipeline.GenerateParts -> " + ex);
            return Fail("生成着装素材时出错：" + ex.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 从一组已经裁好的帧图导入动作：文件复制到包内 motions/{id}/，并更新 figure.json。
    /// 帧保持原有顺序，每帧使用同一个持续时间（需要非均匀节奏时直接改 JSON）。
    /// </summary>
    public async Task<MotionImportResult> ImportMotionFromFramesAsync(
        string packId,
        string motionId,
        IReadOnlyList<string> framePaths,
        int frameDurationMs = 100,
        bool loop = true)
    {
        if (!FigurePackService.IsValidPackId(packId))
            return FailMotion("形象包 id 不合法。");
        if (!IsSafeId(motionId))
            return FailMotion("动作 id 不合法。");
        if (framePaths is null || framePaths.Count == 0)
            return FailMotion("没有可导入的帧。");

        var sources = framePaths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (sources.Count == 0)
            return FailMotion("没有可导入的帧。");

        // 先全部校验，避免复制到一半才发现某张图不存在。
        foreach (var source in sources)
        {
            if (!File.Exists(source))
                return FailMotion("找不到帧文件：" + source);
            var ext = Path.GetExtension(source).ToLowerInvariant();
            if (Array.IndexOf(FrameExtensions, ext) < 0)
                return FailMotion("不支持的帧格式：" + ext + "（请用 PNG/JPG/WebP/BMP）。");
        }

        var gate = GateFor(packId);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var dir = Path.Combine(FiguresRoot, packId);
            var (pack, loadError) = await LoadExistingPackAsync(dir, packId).ConfigureAwait(false);
            if (pack is null || loadError is not null)
                return FailMotion(loadError ?? "形象包读取失败。");

            var motionFolder = SafeFolderName(motionId);
            var motionDir = Path.Combine(dir, "motions", motionFolder);
            Directory.CreateDirectory(motionDir);

            var duration = Math.Clamp(frameDurationMs, 16, 10_000);
            var frames = new List<FigureMotionFrame>(sources.Count);
            for (var i = 0; i < sources.Count; i++)
            {
                var ext = Path.GetExtension(sources[i]).ToLowerInvariant();
                var fileName = $"frame_{i:000}{ext}";
                var dest = Path.Combine(motionDir, fileName);
                if (!SamePath(sources[i], dest))
                    File.Copy(sources[i], dest, true);

                frames.Add(new FigureMotionFrame
                {
                    Texture = $"motions/{motionFolder}/{fileName}",
                    DurationMs = duration,
                });
            }

            var motion = new FigureMotion
            {
                Id = motionId.Trim(),
                Name = motionId.Trim(),
                Frames = frames,
                Loop = loop,
            };

            var motions = new List<FigureMotion>(pack.Motions);
            var index = motions.FindIndex(m => string.Equals(m.Id, motion.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) motions[index] = motion;
            else motions.Add(motion);

            var updated = pack with { Motions = motions };
            var saveError = await SavePackAsync(dir, updated).ConfigureAwait(false);
            if (saveError is not null) return FailMotion(saveError, motion.Id);

            return new MotionImportResult
            {
                Success = true,
                Message = $"已导入动作「{motion.Id}」，共 {frames.Count} 帧。",
                PackDirectory = dir,
                MotionId = motion.Id,
                FrameCount = frames.Count,
            };
        }
        catch (Exception ex)
        {
            App.WriteLog("OutfitAssetPipeline.ImportMotionFromFrames -> " + ex);
            return FailMotion("导入帧序列时出错：" + ex.Message, motionId);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// 从视频抽帧导入。本项目不自带 FFmpeg：
    /// 找不到外部抽帧工具时明确返回「本机缺少视频抽帧工具，请改用帧序列」，绝不假装成功。
    /// </summary>
    public async Task<MotionImportResult> ImportMotionFromVideoAsync(
        string packId,
        string motionId,
        string videoPath,
        double sampleFps = 8,
        bool loop = true)
    {
        if (!FigurePackService.IsValidPackId(packId))
            return FailMotion("形象包 id 不合法。");
        if (!IsSafeId(motionId))
            return FailMotion("动作 id 不合法。");
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            return FailMotion("找不到视频文件。");

        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null)
            return FailMotion("本机缺少视频抽帧工具，请改用帧序列。", motionId);

        var fps = Math.Clamp(double.IsFinite(sampleFps) ? sampleFps : 8, 1, 30);
        var tempDir = Path.Combine(Path.GetTempPath(), "wab-video-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        Process? process = null;
        try
        {
            var outputPattern = Path.Combine(tempDir, "frame_%04d.png");
            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-loglevel");
            psi.ArgumentList.Add("error");
            psi.ArgumentList.Add("-y");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add(videoPath);
            psi.ArgumentList.Add("-vf");
            psi.ArgumentList.Add("fps=" + fps.ToString("0.###", CultureInfo.InvariantCulture));
            psi.ArgumentList.Add(outputPattern);

            process = new Process { StartInfo = psi };
            if (!process.Start())
                return FailMotion("视频抽帧工具启动失败，请改用帧序列。", motionId);

            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var brief = stderr.Length > 240 ? stderr[..240] + "…" : stderr;
                return FailMotion("视频抽帧失败：" + brief, motionId);
            }

            var frames = Directory.GetFiles(tempDir, "frame_*.png")
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
            if (frames.Count == 0)
                return FailMotion("视频里没有抽出可用帧，请改用帧序列。", motionId);

            var frameDuration = (int)Math.Round(1000.0 / fps);
            return await ImportMotionFromFramesAsync(packId, motionId, frames, frameDuration, loop)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return FailMotion("视频抽帧超时（10 分钟），请改用帧序列。", motionId);
        }
        catch (Exception ex)
        {
            TryKill(process);
            App.WriteLog("OutfitAssetPipeline.ImportMotionFromVideo -> " + ex);
            return FailMotion("视频抽帧失败：" + ex.Message + "；请改用帧序列。", motionId);
        }
        finally
        {
            process?.Dispose();
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    // ── 生图与切图 ──────────────────────────────────────────────────────────

    private static string BuildSheetPrompt(string prompt, OutfitSliceOptions slice)
    {
        var rows = Math.Clamp(slice.Rows, 1, 32);
        var columns = Math.Clamp(slice.Columns, 1, 32);
        var slots = string.IsNullOrWhiteSpace(slice.DefaultSlot) ? "部件" : slice.DefaultSlot.Trim();
        return $"{prompt.Trim()}。请生成一张角色换装部件素材表：严格 {rows} 行 × {columns} 列网格，" +
               $"每格只放一个{slots}部件，部件居中、完整、边缘留透明空隙；" +
               "所有格子保持同一角色比例与画风，纯透明背景，不要网格线、文字、水印、额外装饰。";
    }

    private static List<SlicedPart> SliceSheet(
        string sheetPath,
        string packDirectory,
        OutfitSliceOptions options,
        IReadOnlyList<FigurePartSeed>? seeds,
        List<string> writtenFiles,
        out string? error)
    {
        error = null;
        var results = new List<SlicedPart>();
        var rows = options.Rows;
        var columns = options.Columns;
        if (rows <= 0 || columns <= 0)
        {
            error = "素材表行列数必须大于 0。";
            return results;
        }
        if (rows > 32 || columns > 32)
        {
            error = "素材表行列数过大（最多 32×32）。";
            return results;
        }

        try
        {
            using var stream = File.OpenRead(sheetPath);
            using var codec = SKCodec.Create(stream);
            if (codec is null)
            {
                error = "生成结果无法解码（可能不是图片）。";
                return results;
            }

            var source = codec.Info;
            if (source.Width <= 0 || source.Height <= 0)
            {
                error = "生成结果尺寸无效。";
                return results;
            }

            // 生图模型偶尔会回超大图；先按文件头决定缩放比例，再解码，防止切图时吃爆内存。
            var scale = Math.Min(1.0, MaxSheetEdge / (double)Math.Max(source.Width, source.Height));
            var decodeW = Math.Max(1, (int)Math.Round(source.Width * scale));
            var decodeH = Math.Max(1, (int)Math.Round(source.Height * scale));
            using var sheet = SKBitmap.Decode(codec,
                new SKImageInfo(decodeW, decodeH, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (sheet is null)
            {
                error = "生成结果解码失败。";
                return results;
            }

            var left = ScaledPx(options.MarginLeft, scale);
            var top = ScaledPx(options.MarginTop, scale);
            var right = ScaledPx(options.MarginRight, scale);
            var bottom = ScaledPx(options.MarginBottom, scale);
            var gapX = ScaledPx(options.GapX, scale);
            var gapY = ScaledPx(options.GapY, scale);

            var cellW = (sheet.Width - left - right - gapX * (columns - 1)) / columns;
            var cellH = (sheet.Height - top - bottom - gapY * (rows - 1)) / rows;
            if (cellW <= 0 || cellH <= 0)
            {
                error = $"切片参数超出素材表尺寸（{sheet.Width}×{sheet.Height}），请调小边距/间隔。";
                return results;
            }

            var partsDir = Path.Combine(packDirectory, "parts");
            Directory.CreateDirectory(partsDir);

            var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var index = row * columns + column;
                    var srcRect = SKRect.Create(
                        left + column * (cellW + gapX),
                        top + row * (cellH + gapY),
                        cellW,
                        cellH);

                    using var cell = new SKBitmap(cellW, cellH, SKColorType.Rgba8888, SKAlphaType.Premul);
                    using (var canvas = new SKCanvas(cell))
                    {
                        canvas.Clear(SKColors.Transparent);
                        using var paint = new SKPaint
                        {
                            IsAntialias = true,
                            FilterQuality = SKFilterQuality.High,
                        };
                        canvas.DrawBitmap(sheet, srcRect, new SKRect(0, 0, cellW, cellH), paint);
                    }

                    if (!TryTrim(cell, options.AlphaThreshold, options.Padding, out var trim))
                    {
                        App.WriteLog($"OutfitAssetPipeline: 第 {row + 1} 行 {column + 1} 列是空的，跳过");
                        continue;
                    }

                    var seed = seeds is not null && index < seeds.Count ? seeds[index] : null;
                    var rawId = string.IsNullOrWhiteSpace(seed?.Id)
                        ? $"part_{row + 1}_{column + 1}"
                        : seed!.Id.Trim();
                    var id = MakeUnique(rawId, usedIds);
                    var fileStem = MakeUnique(SafeFileStem(id), usedFiles);
                    var fileName = fileStem + ".png";
                    var outPath = Path.Combine(partsDir, fileName);

                    using var output = new SKBitmap(trim.W, trim.H, SKColorType.Rgba8888, SKAlphaType.Premul);
                    using (var canvas = new SKCanvas(output))
                    {
                        canvas.Clear(SKColors.Transparent);
                        using var paint = new SKPaint
                        {
                            IsAntialias = true,
                            FilterQuality = SKFilterQuality.High,
                        };
                        canvas.DrawBitmap(cell,
                            SKRect.Create(trim.X, trim.Y, trim.W, trim.H),
                            new SKRect(0, 0, trim.W, trim.H), paint);
                    }

                    using var image = SKImage.FromBitmap(output);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                    if (data is null)
                    {
                        App.WriteLog("OutfitAssetPipeline: 部件 PNG 编码失败 -> " + outPath);
                        continue;
                    }
                    File.WriteAllBytes(outPath, data.ToArray());

                    results.Add(new SlicedPart(
                        id,
                        $"parts/{fileName}",
                        seed,
                        index));
                    writtenFiles.Add(outPath);
                }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("OutfitAssetPipeline.SliceSheet -> " + ex);
            error = "切分素材表时出错：" + ex.Message;
        }

        return results;
    }

    /// <summary>修剪透明边：只扫描 alpha，返回源图里的实体包围盒（带 padding）。</summary>
    private static bool TryTrim(SKBitmap bitmap, int alphaThreshold, int padding, out TrimRect rect)
    {
        rect = default;
        var threshold = Math.Clamp(alphaThreshold, 0, 255);
        var minX = bitmap.Width;
        var minY = bitmap.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha <= threshold) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < minX || maxY < minY) return false;

        var pad = Math.Clamp(padding, 0, 64);
        var x0 = Math.Max(0, minX - pad);
        var y0 = Math.Max(0, minY - pad);
        var x1 = Math.Min(bitmap.Width - 1, maxX + pad);
        var y1 = Math.Min(bitmap.Height - 1, maxY + pad);
        rect = new TrimRect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        return true;
    }

    // ── 写回 figure.json ────────────────────────────────────────────────────

    private static FigurePack ApplyGeneratedParts(
        FigurePack pack,
        List<SlicedPart> generated,
        OutfitSliceOptions options)
    {
        var parts = new List<FigurePart>(pack.Parts);
        var indexById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < parts.Count; i++)
            if (!indexById.ContainsKey(parts[i].Id)) indexById[parts[i].Id] = i;

        foreach (var item in generated)
        {
            if (indexById.TryGetValue(item.Id, out var index))
            {
                var old = parts[index];
                // 没给模板时只替换贴图，保住之前手工调好的 z/锚点；给了模板就整套应用。
                parts[index] = item.Seed is null
                    ? old with { Texture = item.RelativePath }
                    : ApplySeed(old with { Id = item.Id }, item);
            }
            else
            {
                var basePart = new FigurePart
                {
                    Id = item.Id,
                    Z = 10 + item.Index * 10,
                    Slot = string.IsNullOrWhiteSpace(options.DefaultSlot) ? "accessory" : options.DefaultSlot,
                };
                var part = item.Seed is null ? basePart with { Texture = item.RelativePath } : ApplySeed(basePart, item);
                indexById[item.Id] = parts.Count;
                parts.Add(part);
            }
        }

        return pack with { Parts = parts };
    }

    private static FigurePart ApplySeed(FigurePart part, SlicedPart item)
    {
        var seed = item.Seed!;
        return part with
        {
            Texture = item.RelativePath,
            Slot = string.IsNullOrWhiteSpace(seed.Slot) ? part.Slot : seed.Slot,
            Z = seed.Z,
            AnchorX = Math.Clamp(double.IsFinite(seed.AnchorX) ? seed.AnchorX : 0.5, 0, 1),
            AnchorY = Math.Clamp(double.IsFinite(seed.AnchorY) ? seed.AnchorY : 0.5, 0, 1),
            X = double.IsFinite(seed.X) ? seed.X : 0.5,
            Y = double.IsFinite(seed.Y) ? seed.Y : 0.5,
            OffsetX = double.IsFinite(seed.OffsetX) ? seed.OffsetX : 0,
            OffsetY = double.IsFinite(seed.OffsetY) ? seed.OffsetY : 0,
            Scale = double.IsFinite(seed.Scale) && seed.Scale > 0 ? seed.Scale : 1,
            Opacity = Math.Clamp(double.IsFinite(seed.Opacity) ? seed.Opacity : 1, 0, 1),
        };
    }

    private async Task<(FigurePack? Pack, string? Error)> LoadExistingOrNewAsync(
        string directory,
        string packId,
        OutfitSliceOptions options)
    {
        var file = Path.Combine(directory, "figure.json");
        if (!File.Exists(file))
        {
            return (new FigurePack
            {
                Id = packId,
                Name = string.IsNullOrWhiteSpace(options.PackName) ? packId : options.PackName.Trim(),
                CanvasWidth = options.CanvasWidth > 0 ? options.CanvasWidth : 1080,
                CanvasHeight = options.CanvasHeight > 0 ? options.CanvasHeight : 1920,
                Parts = new List<FigurePart>(),
                // 新建包时补一个空服装/空配色，换装页一打开就有可选项。
                Outfits = new List<FigureOutfit> { new() { Id = "default", Name = "默认服装" } },
                Schemes = new List<FigureScheme> { new() { Id = "default", Name = "默认配色" } },
                Motions = new List<FigureMotion>(),
            }, null);
        }

        return await LoadExistingPackAsync(directory, packId).ConfigureAwait(false);
    }

    private static async Task<(FigurePack? Pack, string? Error)> LoadExistingPackAsync(string directory, string packId)
    {
        var file = Path.Combine(directory, "figure.json");
        if (!File.Exists(file))
            return (null, "形象包不存在，请先在着装页生成部件。");

        try
        {
            var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
            var pack = JsonSerializer.Deserialize<FigurePack>(json, FigurePackService.ReadJson);
            if (pack is null)
                return (null, "现有 figure.json 内容为空，为避免覆盖已停止。");
            return (FigurePackService.Normalize(pack, packId), null);
        }
        catch (JsonException ex)
        {
            // 解析失败时绝不能拿半个空包覆盖用户数据。
            return (null, "现有 figure.json 解析失败，为避免覆盖已停止：" + ex.Message);
        }
        catch (Exception ex)
        {
            App.WriteLog("OutfitAssetPipeline.LoadExistingPack -> " + ex);
            return (null, "读取 figure.json 失败：" + ex.Message);
        }
    }

    private static async Task<string?> SavePackAsync(string directory, FigurePack pack)
    {
        string? tmp = null;
        try
        {
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "figure.json");
            var json = JsonSerializer.Serialize(pack, WriteJson);
            tmp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(tmp, json, new UTF8Encoding(false)).ConfigureAwait(false);
            File.Move(tmp, file, true);
            return null;
        }
        catch (Exception ex)
        {
            App.WriteLog("OutfitAssetPipeline.SavePack -> " + ex);
            return "figure.json 写入失败：" + ex.Message;
        }
        finally
        {
            if (tmp is not null)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }
    }

    // ── 小工具 ──────────────────────────────────────────────────────────────

    private sealed record SlicedPart(string Id, string RelativePath, FigurePartSeed? Seed, int Index);

    private readonly record struct TrimRect(int X, int Y, int W, int H);

    private SemaphoreSlim GateFor(string packId)
        => _gates.GetOrAdd(packId, _ => new SemaphoreSlim(1, 1));

    private static OutfitPipelineResult Fail(string message)
        => new() { Success = false, Message = message };

    private static MotionImportResult FailMotion(string message, string motionId = "")
        => new() { Success = false, Message = message, MotionId = motionId };

    private static bool IsSafeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id.Length > 80) return false;
        if (id is "." or "..") return false;
        if (id.Contains('/') || id.Contains('\\')) return false;
        if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        return true;
    }

    private static int ScaledPx(int value, double scale)
        => Math.Max(0, (int)Math.Round(Math.Max(0, value) * scale));

    private static string SafeFileStem(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch < 128 && (char.IsLetterOrDigit(ch) || ch is '-' or '_')) sb.Append(ch);
            else sb.Append('_');
            if (sb.Length >= 60) break;
        }
        var stem = sb.ToString().Trim('_', ' ');
        if (stem.Length == 0) return "part";
        return ReservedFileStems.Contains(stem, StringComparer.OrdinalIgnoreCase) ? "part_" + stem : stem;
    }

    private static string SafeFolderName(string value)
    {
        var stem = SafeFileStem(value);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant()[..6];
        return stem + "_" + hash;
    }

    private static string MakeUnique(string value, HashSet<string> used)
    {
        var candidate = value;
        var suffix = 2;
        while (!used.Add(candidate))
            candidate = value + "_" + suffix++;
        return candidate;
    }

    private static void TryKill(Process? process)
    {
        try
        {
            if (process is { HasExited: false }) process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), comparison);
        }
        catch
        {
            return false;
        }
    }

    private static string? FindFfmpeg()
    {
        foreach (var key in new[] { "WAB_FFMPEG", "FFMPEG_PATH" })
        {
            var configured = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        }

        var exeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        try
        {
            var local = Path.Combine(App.RootDirectory, "tools", exeName);
            if (File.Exists(local)) return local;
        }
        catch { }

        try
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), exeName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
        }
        catch { }

        return null;
    }
}
