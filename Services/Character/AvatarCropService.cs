using SkiaSharp;

namespace WarmAsBefore.Services;

/// <summary>裁剪预览：一张已经转正、已经降过采样的临时 PNG。拖动与缩放都在这张图上做。</summary>
public sealed record AvatarCropPreview(string FilePath, int Width, int Height);

/// <summary>
/// 一次裁剪请求。<see cref="OffsetX"/>/<see cref="OffsetY"/> 以「取景框边长的比例」计：
/// 0 = 居中，0.5 = 图心偏出半框；方向与手指/鼠标拖动的方向一致。
/// </summary>
public sealed record AvatarCropRequest(
    string CharacterId,
    string SourcePath,
    double Scale,
    double OffsetX,
    double OffsetY,
    int OutputEdge);

public sealed record AvatarCropOutcome(bool Ok, string AvatarRelPath, string Message);

/// <summary>
/// 头像裁剪：在游戏内框选正方形区域，不依赖外部软件。
///
/// 视图与落盘共用同一套数学：先把图按 cover 铺满正方形取景框，再乘 zoom；
/// 裁剪边长 = min(宽,高) / zoom，框心按归一化偏移平移。视图端用框的像素算，
/// 这里用源图像素算，两边只在归一化偏移与 zoom 上对话，所以预览与成品不会错位。
///
/// 大图先降采样再进内存：8K 原图直接整张解码就是几百 MB，宁可少一点像素也不冒这个险。
/// EXIF 方向在解码后立刻摆正，否则手机拍的照片预览是正的、存下来却是躺着的。
/// </summary>
public sealed class AvatarCropService
{
    public const string RouteName = "avatarcrop";
    public const int MaxOutputEdge = 1024;
    public const int DefaultOutputEdge = 512;

    /// <summary>预览解码上限：够看清面部，又不至于把一张 8K 图整张摊进内存。</summary>
    private const int PreviewMaxEdge = 1600;

    private readonly StorageProvider _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TaskCompletionSource<string?>? _pending;

    public AvatarCropService(StorageProvider store) => _store = store;

    // ── 页面交接 ────────────────────────────────────────────────────────────
    // 路由参数负责「带进去」，服务里的 TCS 负责「带回来」：
    // Shell 的返回参数只会落到上一页的查询属性上，上一页是不是工作室、接不接得住都不一定。

    public string? PendingCharacterId { get; private set; }
    public string? PendingSourcePath { get; private set; }

    public static string RouteTo(string characterId, string sourcePath)
        => $"{RouteName}?characterId={Uri.EscapeDataString(characterId)}&sourcePath={Uri.EscapeDataString(sourcePath)}";

    /// <summary>
    /// 打开裁剪页并等结果。返回新头像的存储相对路径；用户取消返回 null。
    /// 调用方拿到路径后自己写回角色（<c>ch with { Avatar = rel }</c>），这里不碰角色库。
    /// </summary>
    public async Task<string?> OpenAsync(string characterId, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(characterId) || string.IsNullOrWhiteSpace(sourcePath)) return null;
        if (!File.Exists(sourcePath)) return null;

        await _gate.WaitAsync();
        try
        {
            PendingCharacterId = characterId;
            PendingSourcePath = sourcePath;
            _pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (Shell.Current is not null)
                    await Shell.Current.GoToAsync(RouteTo(characterId, sourcePath));
            });

            return await _pending.Task;
        }
        catch (Exception ex)
        {
            App.WriteLog("AvatarCrop.Open -> " + ex);
            return null;
        }
        finally
        {
            PendingCharacterId = null;
            PendingSourcePath = null;
            _pending = null;
            _gate.Release();
        }
    }

    /// <summary>裁剪页收尾时回调：新头像相对路径，取消传 null。</summary>
    public void Finish(string? avatarRelPath) => _pending?.TrySetResult(avatarRelPath);

    // ── 预览 ────────────────────────────────────────────────────────────────

    public Task<AvatarCropPreview?> CreatePreviewAsync(string sourcePath) => Task.Run(() =>
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return null;
            using var bmp = DecodeOriented(sourcePath, PreviewMaxEdge);
            if (bmp is null) return null;

            var dir = Path.Combine(FileSystem.CacheDirectory, "avatar-crop");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"preview_{Guid.NewGuid():N}.png");
            using (var image = SKImage.FromBitmap(bmp))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 92))
            using (var fs = File.Create(file))
                data.SaveTo(fs);

            return new AvatarCropPreview(file, bmp.Width, bmp.Height);
        }
        catch (Exception ex)
        {
            App.WriteLog("AvatarCrop.Preview -> " + ex);
            return null;
        }
    });

    /// <summary>临时预览不留到下次：每次进裁剪页都是一张新的。</summary>
    public void ReleasePreview(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        try { if (File.Exists(filePath)) File.Delete(filePath); } catch { }
    }

    // ── 落盘 ────────────────────────────────────────────────────────────────

    public Task<AvatarCropOutcome> CropAsync(AvatarCropRequest req) => Task.Run(() =>
    {
        string? tmp = null;
        try
        {
            if (string.IsNullOrWhiteSpace(req.SourcePath) || !File.Exists(req.SourcePath))
                return new AvatarCropOutcome(false, "", "找不到源图。");
            var id = SafeId(req.CharacterId);
            if (id.Length == 0) return new AvatarCropOutcome(false, "", "角色 id 不合法。");

            var edge = Math.Clamp(req.OutputEdge <= 0 ? DefaultOutputEdge : req.OutputEdge, 64, MaxOutputEdge);
            // 解码到两倍输出边长：缩小时还有余量，又不会为了 512 的成品吃下整张 8K
            using var src = DecodeOriented(req.SourcePath, Math.Max(edge * 2, 1024));
            if (src is null) return new AvatarCropOutcome(false, "", "这个格式解不开（支持 png/jpg/webp 等常见位图）。");

            var zoom = Math.Clamp(req.Scale, 1, 8);
            var min = Math.Min(src.Width, src.Height);
            var side = min / zoom;
            var cx = src.Width / 2.0 - req.OffsetX * side;
            var cy = src.Height / 2.0 - req.OffsetY * side;
            cx = Math.Clamp(cx, side / 2, src.Width - side / 2);
            cy = Math.Clamp(cy, side / 2, src.Height - side / 2);
            var crop = SKRect.Create((float)(cx - side / 2), (float)(cy - side / 2), (float)side, (float)side);

            using var dst = new SKBitmap(edge, edge, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(dst))
            {
                canvas.Clear(SKColors.Transparent);
                using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
                canvas.DrawBitmap(src, crop, new SKRect(0, 0, edge, edge), paint);
                canvas.Flush();
            }

            var dir = Path.Combine(_store.Root, "assets", "characters", id);
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, "avatar.png");
            tmp = dest + ".tmp";
            using (var image = SKImage.FromBitmap(dst))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var fs = File.Create(tmp))
                data.SaveTo(fs);
            File.Move(tmp, dest, true);

            return new AvatarCropOutcome(true, $"assets/characters/{id}/avatar.png", $"头像已写入（{edge}×{edge}）。");
        }
        catch (Exception ex)
        {
            App.WriteLog("AvatarCrop.Crop -> " + ex);
            try { if (tmp is not null && File.Exists(tmp)) File.Delete(tmp); } catch { }
            return new AvatarCropOutcome(false, "", "裁剪失败：" + ex.Message);
        }
    });

    /// <summary>角色 id 进文件名：只留安全字符，杜绝 ../ 把头像写到别的目录。</summary>
    private static string SafeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "";
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string(id.Where(c => Array.IndexOf(bad, c) < 0 && c != '.').ToArray()).Trim();
        return clean.Length > 80 ? clean[..80] : clean;
    }

    // ── 解码与转正 ──────────────────────────────────────────────────────────

    private static SKBitmap? DecodeOriented(string path, int maxEdge)
    {
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream);
        if (codec is null) return null;

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0) return null;

        // 降采样按原始长边算；方向在转正时再换宽高，不能先把宽高换了再交给解码器
        var scale = Math.Min(1.0, maxEdge / (double)Math.Max(info.Width, info.Height));
        var tw = Math.Max(1, (int)Math.Round(info.Width * scale));
        var th = Math.Max(1, (int)Math.Round(info.Height * scale));
        var target = new SKImageInfo(tw, th, SKColorType.Rgba8888, SKAlphaType.Premul);

        var decoded = SKBitmap.Decode(codec, target);
        if (decoded is null) return null;

        var origin = codec.EncodedOrigin;
        if (origin == SKEncodedOrigin.TopLeft) return decoded;

        var oriented = ApplyOrigin(decoded, origin);
        decoded.Dispose();
        return oriented;
    }

    /// <summary>EXIF 方向：坐标系仍是「源图的左上为 0,0」，只把落点摆正。</summary>
    private static SKBitmap ApplyOrigin(SKBitmap src, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var w = swap ? src.Height : src.Width;
        var h = swap ? src.Width : src.Height;

        var dst = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.Transparent);

        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Translate(src.Width, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.Translate(src.Width, src.Height);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Translate(0, src.Height);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop:           // 沿主对角线翻转
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop:          // 顺时针 90°
                canvas.Translate(src.Height, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:       // 沿副对角线翻转
                canvas.Translate(src.Height, src.Width);
                canvas.RotateDegrees(90);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.LeftBottom:        // 逆时针 90°
                canvas.Translate(0, src.Width);
                canvas.RotateDegrees(270);
                break;
        }

        using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
        canvas.DrawBitmap(src, 0, 0, paint);
        canvas.Flush();
        return dst;
    }
}
