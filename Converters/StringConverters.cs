using System.Globalization;
using Microsoft.Maui.Controls;

namespace WarmAsBefore.Converters
{
    /// <summary>
    /// 字符串非空判断转换器（用于 IsVisible）
    /// </summary>
    public sealed class StringNotEmptyConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return !string.IsNullOrEmpty(value as string);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// 字符串为空判断转换器（用于 IsVisible）
    /// </summary>
    public sealed class StringEmptyConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}