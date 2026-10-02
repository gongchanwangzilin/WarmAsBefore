using System.Globalization;
using Microsoft.Maui.Graphics;

namespace WarmAsBefore.Converters
{
    /// <summary>
    /// HP 值 → 颜色转换器（红→黄→绿）
    /// </summary>
    public sealed class HpColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int hp && targetType == typeof(Color))
            {
                // 根据 HP 百分比返回颜色
                var ratio = hp / 100.0;
                return ratio > 0.6 ? Color.FromArgb("#4CAF50")     // 绿色
                     : ratio > 0.3 ? Color.FromArgb("#FFC107")    // 黄色
                     : Color.FromArgb("#F44336");                 // 红色
            }
            return Color.FromArgb("#4CAF50");
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// SkillType → 颜色转换器
    /// </summary>
    public sealed class SkillTypeColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isHeal)
            {
                return isHeal ? Color.FromArgb("#4CAF50")   // 治疗=绿色
                              : Color.FromArgb("#F44336");   // 攻击=红色
            }
            return Color.FromArgb("#9E9E9E");
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// IsCritical → 颜色转换器（暴击显示红色）
    /// </summary>
    public sealed class CriticalColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isCrit)
            {
                return isCrit ? Color.FromArgb("#FF1744")   // 暴击=亮红
                              : Color.FromArgb("#E0E0E0");   // 普通=浅灰
            }
            return Color.FromArgb("#E0E0E0");
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}