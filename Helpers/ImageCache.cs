namespace WarmAsBefore.Helpers;

/// <summary>
/// 自绘画布（地图、记忆网）共用的位图缓存：只留最近用到的 N 张，超额的一律 Dispose 掉，
/// 把原生位图内存立刻还给底层，不然要等 GC 才 release，手机上是先 OOM 还是先回收全看运气。
/// </summary>
public static class ImageCache
{
    /// <summary>缓存张数上限。一张 2K 背景解出来是十几 MB ARGB，手机给得起的只有桌面端的几分之一。</summary>
    public static int Cap => DeviceInfo.Platform == DevicePlatform.Android ? 48 : 220;

    /// <summary>
    /// 写入缓存，并按「最久没画到」把超额的踢出去。<paramref name="order"/> 是调用方持有的
    /// 使用顺序队列（与 cache 的键一一对应），队首就是最久没露面的一张。
    /// </summary>
    public static void Put(Dictionary<string, Microsoft.Maui.Graphics.IImage?> cache,
        List<string> order, string key, Microsoft.Maui.Graphics.IImage? img)
    {
        // 刚写过的算「最近用过」，一律挪到队尾（占位那次也算，它马上就轮到出图）
        order.Remove(key);
        order.Add(key);
        if (cache.TryGetValue(key, out var old) && old is not null && !ReferenceEquals(old, img))
            (old as IDisposable)?.Dispose();
        cache[key] = img;
        while (order.Count > Cap)
        {
            var drop = order[0];
            order.RemoveAt(0);
            if (drop == key || !cache.TryGetValue(drop, out var victim)) continue;
            cache.Remove(drop);
            (victim as IDisposable)?.Dispose();
        }
    }

    /// <summary>这张图这次真画在屏上了：挪到队尾，下次要扔先扔别的。
    /// 只在已经顶到上限时才值得调（IndexOf 是线性的，没压力时纯浪费）。</summary>
    public static void Touch(List<string> order, string key)
    {
        var i = order.IndexOf(key);
        if (i < 0 || i == order.Count - 1) return;
        order.RemoveAt(i);
        order.Add(key);
    }
}
