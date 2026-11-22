using System.Globalization;

namespace WarmAsBefore.Helpers;

/// <summary>
/// 显示层的计数格式：把「数据异常」和「数很大」都变成一句读得懂的话，而不是把 -1 印到界面上。
/// 纯函数、零依赖，tools/text_selftest 直接编这一份进来跑。
/// </summary>
public static class CountText
{
    /// <summary>负数是数据坏了（回滚、导入、旧存档对不上），不是「欠着一条」：只给破折号。</summary>
    public const string BadValue = "—";

    /// <summary>徽标超过这个数就没必要精确了：999+ 比 12345 好读，也不撑坏小红点。</summary>
    public const int CompactCap = 999;

    /// <summary>普通计数：三位的千分位；负数给破折号。</summary>
    public static string Format(int n) =>
        n < 0 ? BadValue : n.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>徽标计数：超过 <see cref="CompactCap"/> 显示「999+」；负数同样给破折号。</summary>
    public static string FormatCompact(int n) =>
        n < 0 ? BadValue : n > CompactCap ? $"{CompactCap}+" : n.ToString(CultureInfo.CurrentCulture);

    /// <summary>
    /// 带量词的计数。量词跟着数字一起消失：数据坏了要显示「—」，不能留下「— 条」这种半截话。
    /// 量词为空时等同于 <see cref="Format"/>。
    /// </summary>
    public static string FormatWithUnit(int n, string? unit) =>
        n < 0 ? BadValue
        : string.IsNullOrEmpty(unit) ? Format(n)
        : $"{Format(n)} {unit}";
}
