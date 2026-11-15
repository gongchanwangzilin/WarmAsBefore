using System.Collections.Concurrent;
using Microsoft.Maui.Graphics.Platform;

namespace WarmAsBefore.Services;

/// <summary>
/// 探测图片的原始像素尺寸。标定与角色缩放换算都必须知道"这张图原本多大"。
///
/// 解码在线程池执行（2026-11-15 修正）：旧实现强制回 UI 线程解码（旧注释称
/// 「WinUI 的 WIC 解码不适合放后台线程」——不成立，WIC 解码线程安全），
/// 而 PlatformImage.FromStream 内部是 sync-over-async，在 UI 线程上阻塞等待时
/// 会泵 WinUI 消息，布局 pass 重入 → LayoutCycleException（dump 实证 UI 线程
/// 卡在 FromStream 的 Monitor.Wait），大图解码还冻结 UI 数秒。
/// 结果按「路径 + 文件指纹」缓存，换图后自动失效；本方法线程安全。
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
        // 不再回 UI 线程：解码与缓存查询都线程安全，放线程池，
        // UI 线程只收最终 (宽, 高)。SizeOf 内部有缓存，重复调用零解码。
        return await Task.Run(() => SizeOf(absPath));
    }
}
