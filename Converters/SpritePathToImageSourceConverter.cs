using WarmAsBefore.Models;

namespace WarmAsBefore.Converters;

/// <summary>将立绘路径字符串转为 ImageSource，用于播放页渲染多立绘。</summary>
public sealed class SpritePathToImageSourceConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => ToSource(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();

    public static ImageSource? ToSource(object? value)
    {
        if (value is not string rel || string.IsNullOrWhiteSpace(rel)) return null;
        var full = System.IO.Path.IsPathRooted(rel) ? rel : System.IO.Path.Combine(App.RootDirectory, rel);
        return File.Exists(full) ? ImageSource.FromFile(full) : null;
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