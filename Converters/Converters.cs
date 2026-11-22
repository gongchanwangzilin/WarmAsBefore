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

/// <summary>F22：工具气泡展开/收起箭头（true=▾ 展开，false=▸ 收起）。</summary>
public sealed class BoolToAsterisk : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        v is true ? "▾" : "▸";
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

/// <summary>识别引擎字符串 → 是否 local（控制本地模型区显隐）。</summary>
public sealed class SttEngineIsLocal : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        v is string s && string.Equals(s, "local", StringComparison.OrdinalIgnoreCase);
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}

/// <summary>service 模式字符串 → 是否 GPT-SoVITS（控制 SoVITS 专用参数行显隐）。</summary>
public sealed class IsSovitsMode : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        v is string s && s.Contains("GPT-SoVITS", System.StringComparison.OrdinalIgnoreCase);
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


/// <summary>
/// 计数显示：所有「几个」都走 CountText，界面上不再出现裸数字。
/// 负数是坏数据（回滚、导入对不上），显示破折号而不是「-1 次」；千分位跟当前区域。
/// 用于 XAML 里 <c>{Binding Count, Converter={StaticResource CountNumber}}</c>。
/// </summary>
public sealed class CountNumber : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        Helpers.CountText.Format(v switch
        {
            int i => i,
            long l => l is > int.MaxValue or < int.MinValue ? -1 : (int)l,
            double d when !double.IsNaN(d) => (int)Math.Round(d),
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => -1
        });
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}

/// <summary>徽标计数（红点、页签角标）：超过 999 显示「999+」，窄容器里不会再被撑开。</summary>
public sealed class CountCompact : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        Helpers.CountText.FormatCompact(v switch
        {
            int i => i,
            long l => l is > int.MaxValue or < int.MinValue ? int.MaxValue : (int)l,
            double d when !double.IsNaN(d) => (int)Math.Round(d),
            _ => -1
        });
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}

/// <summary>状态文字：枚举名一律经 StatusText 翻成人话，页面里不能再出现裸枚举名。</summary>
public sealed class StatusLabel : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) => Helpers.StatusText.Format(v);
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}

/// <summary>
/// 带模板的计数：<c>{Binding Owned, Converter={StaticResource CountText}, ConverterParameter='已购 {0} 件'}</c>。
/// 模板里的 {0} 由 CountText 填 —— 负数变破折号、大数带千分位，页面写不出「已购 -1 件」。
/// 数据坏了时整句只剩破折号（「—」而不是「已购 — 件」）：量词跟着数字一起消失。
/// </summary>
public sealed class CountWithTemplate : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c)
    {
        var n = v switch
        {
            int i => i,
            long l => l is > int.MaxValue or < int.MinValue ? -1 : (int)l,
            double d when !double.IsNaN(d) => (int)Math.Round(d),
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => -1
        };
        if (n < 0) return Helpers.CountText.BadValue;
        var text = Helpers.CountText.Format(n);
        var template = p as string;
        return string.IsNullOrEmpty(template) || !template.Contains("{0}")
            ? text
            : template.Replace("{0}", text);
    }
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}
