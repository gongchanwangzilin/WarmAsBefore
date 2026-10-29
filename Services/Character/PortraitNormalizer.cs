using SkiaSharp;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 立绘规范化：把非标准尺寸的素材真正落成 <see cref="PortraitCanvas"/>（1080×1920）画布文件。
///
/// 与「标定」的分工：
/// · 标定 = 不改文件，只记下怎么映射（可随时改、可清除）；
/// · 规范化 = 真的重采样出一个 1080×1920 的 PNG，适合素材本来就歪得离谱的场合，
///   落盘后这张图在任何场景都按标准画布对待，连标定都不必再做。
///
/// 用法上是「可选的一步」而不是强制：允许留原图 + 标定。
/// </summary>
public sealed class PortraitNormalizer
{
    private readonly StorageProvider _store;

    public PortraitNormalizer(StorageProvider store) => _store = store;

    /// <summary>规范化后的文件名后缀（与原文同目录）。</summary>
    public const string Suffix = "_1080x1920";

    /// <summary>
    /// 把 <paramref name="absSourcePath"/> 重采样进 1080×1920 画布。
    /// 等比缩放居中放置（contain，不裁切、不拉伸），余量留透明——所以角色的完整性不会被动过。
    /// 返回新文件的存储相对路径；失败返回 null。
    /// </summary>
    public async Task<(string? RelPath, string Message)> NormalizeAsync(string absSourcePath, string spriteRelPath, bool flipHorizontal = false, int rotation = 0)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(absSourcePath) || !File.Exists(absSourcePath))
                return (null, "找不到源文件。");

            var dir = Path.GetDirectoryName(absSourcePath) ?? _store.Root;
            var name = Path.GetFileNameWithoutExtension(absSourcePath);
            var ext = Path.GetExtension(absSourcePath);
            if (!string.IsNullOrEmpty(ext) && name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
                name = name[..^Suffix.Length];
            var destAbs = Path.Combine(dir, name + Suffix + ".png");

            using var src = SKBitmap.Decode(absSourcePath);
            if (src is null) return (null, "这个格式解不开（支持 png/jpg/webp 等常见位图）。");

            var target = new SKBitmap((int)PortraitCanvas.Width, (int)PortraitCanvas.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(target))
            {
                canvas.Clear(SKColors.Transparent);

                // 先按需要转正/镜像，再等比缩放居中
                var rotated = rotation is 90 or 180 or 270;
                var effW = rotated && (rotation == 90 || rotation == 270) ? src.Height : src.Width;
                var effH = rotated && (rotation == 90 || rotation == 270) ? src.Width : src.Height;
                var scale = Math.Min((float)PortraitCanvas.Width / effW, (float)PortraitCanvas.Height / effH);

                canvas.Translate((float)PortraitCanvas.Width / 2, (float)PortraitCanvas.Height / 2);
                if (rotation != 0) canvas.RotateDegrees(rotation);
                if (flipHorizontal) canvas.Scale(-1, 1);
                canvas.Scale(scale, scale);

                var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
                canvas.DrawBitmap(src, new SKRect(-src.Width / 2f, -src.Height / 2f, src.Width / 2f, src.Height / 2f), paint);
                canvas.Flush();
            }

            using (var image = SKImage.FromBitmap(target))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var fs = File.OpenWrite(destAbs))
            {
                target.Dispose();
                data.SaveTo(fs);
            }

            var rel = RelPathOf(absSourcePath, destAbs, spriteRelPath);
            await Task.CompletedTask;
            return (rel, $"已生成标准画布文件：{Path.GetFileName(destAbs)}（1080×1920，等比居中，余量透明）。");
        }
        catch (Exception ex)
        {
            App.WriteLog("PortraitNormalizer.Normalize -> " + ex);
            return (null, "规范化失败：" + ex.Message);
        }
    }

    /// <summary>新文件相对存储根目录的路径（沿用源文件所在位置，保证 SpriteMap 里可直接用）。</summary>
    private string RelPathOf(string absSource, string absDest, string spriteRelPath)
    {
        var root = _store.Root.TrimEnd(Path.DirectorySeparatorChar, '/') + Path.DirectorySeparatorChar;
        if (absDest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return absDest[root.Length..].Replace(Path.DirectorySeparatorChar, '/');

        // 源文件在存储目录之外时：用原 SpriteMap 路径的目录 + 新文件名
        if (!string.IsNullOrWhiteSpace(spriteRelPath))
        {
            var dir = Path.GetDirectoryName(spriteRelPath.Replace('\\', '/'));
            var file = Path.GetFileName(absDest);
            return string.IsNullOrEmpty(dir) ? file : $"{dir}/{file}";
        }
        return absDest.Replace(Path.DirectorySeparatorChar, '/');
    }
}
