using System.Globalization;
using System.Text.RegularExpressions;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 角色年龄/生日：只做「算」与「说」，不改记录。
///
/// 设计取舍：
/// 1. 档案里只存 <see cref="CharacterProfile.Age"/>（用户填的记录岁数）与
///    <see cref="CharacterProfile.AgeAsOf"/>（填这个岁数时的日期），不每年写盘。
///    自然增长是「读取时按过了几个生日推导」——这样旧存档、云同步、导入包都不需要迁移，
///    也不会因为某天没打开 App 就漏长一岁。
/// 2. 生日只存 "MM-dd"，不存年份：角色设定里的年份往往不可信（或干脆没有），
///    真正要用的只有「几月几日」，年份反而会带来跨年比较的麻烦。
/// 3. 关了自然增长只是冻结显示值，年龄/生日仍然进提示词——用户要的是「不涨岁」，
///    不是「把设定藏起来」。
/// </summary>
public static class CharacterAgeService
{
    /// <summary>生日统一存成 "MM-dd"：月份补零、日不补零（"03-14"）。</summary>
    private static readonly Regex MonthDayPattern = new(@"^(?<m>\d{1,2})[-/.](?<d>\d{1,2})$", RegexOptions.Compiled);
    private static readonly Regex FullDatePattern = new(@"^(?:\d{4})[-/.](?<m>\d{1,2})[-/.](?<d>\d{1,2})$", RegexOptions.Compiled);
    private static readonly Regex ChinesePattern = new(@"^(?<m>\d{1,2})月(?<d>\d{1,2})日?$", RegexOptions.Compiled);

    /// <summary>
    /// 把用户/AI 给的生日写法归一成 "MM-dd"：接受 "03-14" / "3-14" / "2020-03-14" /
    /// "3月14日" / "3/14"。非法或空返回 ""，调用方据此判断「没填生日」。
    /// </summary>
    public static string NormalizeBirthday(string? raw)
    {
        if (!TryParseMonthDay(raw, out var month, out var day)) return "";
        return $"{month:00}-{day:00}";
    }

    /// <summary>给界面和提示词看的中文生日："3 月 14 日"；没填返回 ""。</summary>
    public static string BirthdayDisplay(string? raw)
    {
        return TryParseMonthDay(raw, out var month, out var day) ? BirthdayDisplay(month, day) : "";
    }

    /// <summary>
    /// 自然增长后的岁数：开启自然增长、生日合法且有记录值时，数 AgeAsOf（默认今天）
    /// 到 now 之间过了几个生日，加到记录值上。否则原样返回记录值。
    /// 年龄为 null 时返回 null（= 没填，调用方不要编一个）。
    /// </summary>
    public static int? EffectiveAge(CharacterProfile p, DateTime? now = null)
    {
        if (p?.Age is not { } recorded) return null;
        if (!p.NaturalAging) return recorded;
        if (!TryParseMonthDay(p.Birthday, out var month, out var day)) return recorded;

        var today = (now ?? DateTime.Now).Date;
        // AgeAsOf 为空按「今天填的」算：只有知道从哪天起算，数生日才有意义。
        var since = (p.AgeAsOf ?? DateTime.Today).Date;
        // 记录日期在未来（换设备/改系统时间）就冻结，别倒着减出一堆负数。
        if (since > today) return recorded;

        return recorded + CountBirthdaysBetween(since, today, month, day);
    }

    /// <summary>今天是不是这个角色的生日。2 月 29 日的生日在平年按 2 月 28 日过。</summary>
    public static bool IsBirthdayToday(CharacterProfile p, DateTime now)
    {
        if (p is null || !TryParseMonthDay(p.Birthday, out var month, out var day)) return false;
        return AnniversaryInYear(now.Year, month, day) == now.Date;
    }

    /// <summary>距离下一个生日还有几天；今天就是生日返回 0，没填生日返回 null。</summary>
    public static int? DaysToNextBirthday(CharacterProfile p, DateTime now)
    {
        if (p is null || !TryParseMonthDay(p.Birthday, out var month, out var day)) return null;
        var today = now.Date;
        var next = AnniversaryInYear(today.Year, month, day);
        if (next < today) next = AnniversaryInYear(today.Year + 1, month, day);
        return (next - today).Days;
    }

    /// <summary>
    /// 拼进角色人设的年龄/生日段落；两样都没填返回 ""，调用方不拼即可。
    /// 例："你今年 24 岁，生日是 3 月 14 日"；今天生日再补 "今天就是你的生日"。
    /// </summary>
    public static string PromptText(CharacterProfile p, DateTime now)
    {
        if (p is null) return "";
        var hasAge = p.Age is not null;
        var hasBirthday = TryParseMonthDay(p.Birthday, out var month, out var day);
        if (!hasAge && !hasBirthday) return "";

        var parts = new List<string>(2);
        if (hasAge) parts.Add($"你今年 {EffectiveAge(p, now)} 岁");
        if (hasBirthday)
        {
            // 和「你今年 X 岁」连用时省掉「你的」；单独出现时补上，免得人设里飘一句没主语的「生日是」。
            parts.Add(hasAge ? $"生日是 {BirthdayDisplay(month, day)}" : $"你的生日是 {BirthdayDisplay(month, day)}");
            if (IsBirthdayToday(p, now)) parts.Add("今天就是你的生日");
        }
        return string.Join("，", parts);
    }

    /// <summary>
    /// 拼进人设的「用户生日」提示；没设置返回 ""。
    /// 今天生日时提醒模型主动一点，这是用户填生日的意义所在。
    /// </summary>
    public static string UserPromptText(string? userBirthday, DateTime now)
    {
        if (!TryParseMonthDay(userBirthday, out var month, out var day)) return "";
        return AnniversaryInYear(now.Year, month, day) == now.Date
            ? $"今天是用户的生日（{BirthdayDisplay(month, day)}），记得主动祝用户生日快乐"
            : $"用户的生日是 {BirthdayDisplay(month, day)}";
    }

    /// <summary>解析 "MM-dd" / "3-14" / "2020-03-14" / "3月14日" / "3/14" 这类写法。</summary>
    private static bool TryParseMonthDay(string? raw, out int month, out int day)
    {
        month = 0;
        day = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        // 去掉所有空白，兼容「3 月 14 日」「2020 - 03 - 14」这类手写。
        var text = Regex.Replace(raw.Trim(), @"\s+", "");
        if (text.Length == 0) return false;

        Match match;
        if (ChinesePattern.IsMatch(text)) match = ChinesePattern.Match(text);
        else if (FullDatePattern.IsMatch(text)) match = FullDatePattern.Match(text);
        else if (MonthDayPattern.IsMatch(text)) match = MonthDayPattern.Match(text);
        else return false;

        if (!int.TryParse(match.Groups["m"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out month)) return false;
        if (!int.TryParse(match.Groups["d"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out day)) return false;

        // 月份 1-12；日按闰年上限校验，让 2 月 29 日的生日合法（平年按 2 月 28 日过）。
        if (month is < 1 or > 12) return false;
        if (day < 1 || day > DateTime.DaysInMonth(2000, month)) return false;
        return true;
    }

    private static string BirthdayDisplay(int month, int day) => $"{month} 月 {day} 日";

    /// <summary>某年的生日当天；2 月 29 日遇到平年按 2 月 28 日过。</summary>
    private static DateTime AnniversaryInYear(int year, int month, int day)
    {
        var safeDay = Math.Min(day, DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, safeDay);
    }

    /// <summary>数 (startExclusive, endInclusive] 之间过了几个生日。</summary>
    private static int CountBirthdaysBetween(DateTime startExclusive, DateTime endInclusive, int month, int day)
    {
        var count = 0;
        // 上限保护：防御异常大的 AgeAsOf 区间（比如导入了 1900 年），正常角色一两次就到顶。
        var lastYear = Math.Min(endInclusive.Year, startExclusive.Year + 200);
        for (var year = startExclusive.Year; year <= lastYear; year++)
        {
            var anniversary = AnniversaryInYear(year, month, day);
            if (anniversary > startExclusive && anniversary <= endInclusive) count++;
        }
        return count;
    }
}
