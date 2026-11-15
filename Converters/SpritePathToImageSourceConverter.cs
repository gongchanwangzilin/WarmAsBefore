using System.Collections.Concurrent;
using WarmAsBefore.Models;

namespace WarmAsBefore.Converters;

/// <summary>将立绘路径字符串转为 ImageSource，用于播放页渲染多立绘。</summary>
public sealed class SpritePathToImageSourceConverter : IValueConverter
{
    /// <summary>
    /// 路径 → ImageSource 缓存（2026-11-14，P0-4）。
    /// 旧实现每次 Convert 都同步 File.Exists 且新建 ImageSource：列表滚动 / 绑定刷新时
    /// 在 UI 线程反复做同步磁盘 IO 并重建源，是「UI 不华丽却卡顿」的直接来源之一。
    /// 命中后零 IO、零分配；文件不存在的结果（null）也缓存，避免对缺失路径反复敲磁盘。
    /// 键是解析后的完整路径，跟窗宽/主题令牌无关，换世界不必失效。
    /// </summary>
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>运行期导入/替换了立绘文件后调用，让下一次 Convert 重新判定（传 null 清全部）。</summary>
    public static void Invalidate(string? relOrFull = null)
    {
        if (relOrFull is null) { Cache.Clear(); return; }
        var full = System.IO.Path.IsPathRooted(relOrFull)
            ? relOrFull
            : System.IO.Path.Combine(App.RootDirectory, relOrFull);
        Cache.TryRemove(full, out _);
    }

    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => ToSource(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();

    public static ImageSource? ToSource(object? value)
    {
        if (value is not string rel || string.IsNullOrWhiteSpace(rel)) return null;
        var full = System.IO.Path.IsPathRooted(rel) ? rel : System.IO.Path.Combine(App.RootDirectory, rel);
        // 每条路径只做一次 File.Exists 与一次 ImageSource.FromFile，之后纯内存查表。
        return Cache.GetOrAdd(full, static p =>
        {
            if (!File.Exists(p)) return null;
            return ImageSource.FromFile(p);
        });
    }
}

/// <summary>将 isVisible bool 反转为 !isVisible，用于排除自身（False=True）。</summary>
public sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : false;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is bool b ? !b : false;
}

/// <summary>将 int 计数转为 bool（0=False，非0=True），用于“立绘集合非空时显示”。</summary>
public sealed class ZeroToFalseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is int n && n > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}