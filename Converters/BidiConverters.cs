using System.Globalization;
using Microsoft.Maui.Controls;
using WarmAsBefore.Helpers;

namespace WarmAsBefore.Converters;

/// <summary>
/// 段落方向：首个强方向字符是阿拉伯 / 希伯来（R / AL）时整段 RightToLeft，其余保持 LeftToRight。
/// 绑在 Label / Editor / Entry 的 FlowDirection 上；混排整段一个方向，不逐词翻转。
/// </summary>
public sealed class BidiFlowDirection : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        BidiText.IsRtl(v as string) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}

/// <summary>RTL 段落对齐到 End（视觉上的右边），LTR 维持 Start。</summary>
public sealed class BidiTextAlignment : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) =>
        BidiText.IsRtl(v as string) ? TextAlignment.End : TextAlignment.Start;

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}

/// <summary>显示层软换行：给无空格长串（邮箱 / URL / 路径 / 长 id）补零宽断点，数据本身一个字不改。</summary>
public sealed class SoftWrapText : IValueConverter
{
    public object Convert(object? v, Type t, object? p, CultureInfo c) => BidiText.SoftWrap(v as string);

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => throw null!;
}
