using System.Collections.Concurrent;
using Microsoft.Maui.Graphics.Platform;

namespace WarmAsBefore.Services;

/// <summary>
/// 探测图片的原始像素尺寸。标定与角色缩放换算都必须知道"这张图原本多大"。
///
/// 解码走主线程（与 MapCanvasDrawable 加载图片的做法一致：WinUI 的 WIC 解码不适合放在后台线程），
/// 结果按「路径 + 文件指纹」缓存，换图后自动失效。
/// </summary>
public static class ImageProbe
{
    private static readonly ConcurrentDictionary<string, (int W, int H)> Cache = new();

    public static (int W, int H) SizeOf(string? absPath)
    {
        if (string.IsNullOrEmpty(absPath) || !File.Exists(absPath)) return (0, 0);

        var key = absPath + "|" + MapService.StampOf(absPath);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var size = (0, 0);
        try
        {
            using var fs = File.OpenRead(absPath);
            using var img = PlatformImage.FromStream(fs);
            if (img is not null) size = ((int)img.Width, (int)img.Height);
        }
        catch (Exception ex)
        {
            // 视频缩略图缺失、格式不支持等都会走到这里：返回 (0,0) 让调用方降级处理
            App.WriteLog("ImageProbe.SizeOf(" + absPath + ") -> " + ex.Message);
        }
        Cache[key] = size;
        return size;
    }

    public static async Task<(int W, int H)> SizeOfAsync(string? absPath)
    {
        if (MainThread.IsMainThread) return SizeOf(absPath);
        return await MainThread.InvokeOnMainThreadAsync(() => SizeOf(absPath));
    }
}
