using System.Globalization;

namespace WarmAsBefore.Converters;

public sealed class BoolOpacity : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        v is true ? 1.0 : 0.4;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}

public sealed class InverseBool : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) => v is false;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => v is false;
}

public sealed class IsNotNull : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) => v is not null;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}

public sealed class AffectionTint : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) => v switch
    {
        int a when a < 20 => Color.FromArgb("#EF9A9A"),
        int a when a < 50 => Color.FromArgb("#FFD54F"),
        int a when a < 80 => Color.FromArgb("#A5D6A7"),
        int => Color.FromArgb("#FFCA28"),
        _ => Color.FromArgb("#FFD54F")
    };
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}

public sealed class CharInitials : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        v is string s && s.Length > 0 ? s[..1] : "?";
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}

public sealed class YesNoLabel : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        v is true ? "已导入" : "未导入";
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}

public sealed class BoolGreenRed : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        v is true ? Color.FromArgb("#4CAF50") : Color.FromArgb("#EF5350");
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}

public sealed class SelectedBg : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        v is true ? Color.FromArgb("#F5DFC0") : Color.FromArgb("#FDF6EE");
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}

/// <summary>bool → 颜色：属性 True/False 指定两色（用于列表行高亮）。</summary>
public sealed class BoolToColor : IValueConverter
{
    public Color True { get; set; } = Colors.Transparent;
    public Color False { get; set; } = Colors.Transparent;
    public object Convert(object? v, Type t, object? p, CultureInfo c) => v is true ? True : False;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}

/// <summary>string → 是否非空（控制提示条显隐）。</summary>
public sealed class StringNotEmpty : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        v is string s && !string.IsNullOrWhiteSpace(s);
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}

/// <summary>int → 是否 &gt; 0（控制"已购 N 件"显隐）。</summary>
public sealed class GreaterThanZero : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c)
    {
        if (v is int n) return n > 0;
        if (v is double d) return d > 0;
        if (v is float f) return f > 0;
        return false;
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}

/// <summary>bool(IsTicket) → 按钮文案：场景券显示「解锁」，普通商品显示「购买」。</summary>
public sealed class TicketLabel : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) => v is true ? "解锁" : "购买";
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}

/// <summary>bool → FontAttributes：选中标签加粗。</summary>
public sealed class BoolFontBold : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        v is true ? FontAttributes.Bold : FontAttributes.None;
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}