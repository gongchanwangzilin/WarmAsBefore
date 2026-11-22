using System.Globalization;
using System.Text;

namespace WarmAsBefore.Helpers;

/// <summary>一段文字的方向：由「首个强方向字符」决定，整段一个方向，不逐词翻转。</summary>
public enum BidiDirection
{
    /// <summary>整段没有强方向字符（纯数字、纯标点、纯 emoji、空串）：维持原样。</summary>
    Neutral,
    Ltr,
    Rtl
}

/// <summary>
/// 显示层的双向文本工具：只读文本、只决定「怎么摆」，不改一个字的数据。
/// 纯函数、零依赖（不引 MAUI），所以 tools/text_selftest 能直接编这一份进来跑。
/// </summary>
public static class BidiText
{
    /// <summary>零宽空格：本身不占宽度，只是给排字器多一个断行机会。</summary>
    public const char ZeroWidthSpace = '\u200B';

    /// <summary>只有长过这个长度的无空格串才值得插断行点，正常的短邮箱 / 短路径不动。</summary>
    public const int SoftWrapMinRun = 24;

    /// <summary>一条文本最多插这么多个零宽空格，防止病态长串把字符串撑成两倍。</summary>
    public const int SoftWrapMaxInsertions = 40;

    /// <summary>可以被断开的字符：常见的无空格长串（邮箱 / URL / 路径 / 长 id）就靠它们分隔。</summary>
    private static ReadOnlySpan<char> BreakChars => "@./-_?:";

    /// <summary>
    /// 按首个强方向字符判断段落方向（Unicode bidi class L / R / AL）。
    /// 数字、标点、空白、emoji、组合记号都是弱字符，一律跳过：
    /// 「2026-11-22 是雨天 🥲 يا هلا」整段按「是」→ LTR；「نور · 今天 · 2026」按「ن」→ RTL。
    /// </summary>
    public static BidiDirection DetectDirection(string? text)
    {
        if (string.IsNullOrEmpty(text)) return BidiDirection.Neutral;
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            if (IsRtlStrong(value)) return BidiDirection.Rtl;
            if (IsLtrStrong(rune)) return BidiDirection.Ltr;
        }
        return BidiDirection.Neutral;
    }

    /// <summary>这一段要不要按从右往左摆。</summary>
    public static bool IsRtl(string? text) => DetectDirection(text) == BidiDirection.Rtl;

    private static bool IsLtrStrong(Rune rune) => Rune.GetUnicodeCategory(rune) switch
    {
        UnicodeCategory.UppercaseLetter => true,
        UnicodeCategory.LowercaseLetter => true,
        UnicodeCategory.TitlecaseLetter => true,
        UnicodeCategory.ModifierLetter => true,
        UnicodeCategory.OtherLetter => true,
        _ => false
    };

    /// <summary>
    /// 强 R / AL 的码位段：希伯来、阿拉伯（含叙利亚、它拿、曼达、阿维斯陀等伴生文字）、
    /// 两种阿拉伯显示形、以及 BMP 之外的那些从右往左的文字（阿德拉姆、乌加里特、旧波斯等）。
    /// </summary>
    private static bool IsRtlStrong(int cp) => cp switch
    {
        >= 0x0590 and <= 0x08FF => true,   // Hebrew / Arabic / Syriac / Thaana / NKo / Samaritan / Mandaic
        >= 0xFB1D and <= 0xFDFF => true,   // 希伯来 + 阿拉伯显示形 A
        >= 0xFE70 and <= 0xFEFF => true,   // 阿拉伯显示形 B
        >= 0x10800 and <= 0x10FFF => true, // 塞浦路斯 / 阿拉姆 / 哈特拉 / 帕提亚 / 可罗希提 / 罗兴亚 / 耶兹迪
        >= 0x1E800 and <= 0x1EFFF => true, // 门德基卡库 / 阿德拉姆 / 阿拉伯数学字母
        _ => false
    };

    /// <summary>
    /// 显示层软换行：在没有空格的分段里，于 <c>@ . / - _ : ?</c> 之后插零宽空格，
    /// 让邮箱、URL、路径、长 id 这类「排字器找不到断点」的串能折行，而不是把气泡撑破。
    /// 有几条硬规矩：
    /// <list type="bullet">
    /// <item>空串 / 短串原样返回，连一个新对象都不造；</item>
    /// <item>只有长度超过 <see cref="SoftWrapMinRun"/> 的无空格段才处理，正常中文句子一个字都不动；</item>
    /// <item>一条文本最多插 <see cref="SoftWrapMaxInsertions"/> 个，插在分隔符之后、不在末尾、不重复。</item>
    /// </list>
    /// </summary>
    public static string SoftWrap(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        if (text.Length <= SoftWrapMinRun) return text;

        StringBuilder? sb = null;
        var copied = 0;      // text[..copied] 已经进 sb 了
        var insertions = 0;
        var i = 0;
        while (i < text.Length && insertions < SoftWrapMaxInsertions)
        {
            // 跳过空白：不然段长为 0 时 i 永远不动，这个循环就转不完了。
            while (i < text.Length && IsSpace(text[i])) i++;
            // 一段「没有空格」的连续字符，段内才有断点可言。
            var start = i;
            while (i < text.Length && !IsSpace(text[i])) i++;
            var end = i;
            if (end - start <= SoftWrapMinRun) continue;

            for (var j = start; j + 1 < end && insertions < SoftWrapMaxInsertions; j++)
            {
                if (BreakChars.IndexOf(text[j]) < 0) continue;
                if (text[j + 1] == ZeroWidthSpace || IsSpace(text[j + 1])) continue;
                sb ??= new StringBuilder(text.Length + 16);
                sb.Append(text, copied, j + 1 - copied).Append(ZeroWidthSpace);
                copied = j + 1;
                insertions++;
            }
        }

        if (sb is null) return text;
        sb.Append(text, copied, text.Length - copied);
        return sb.ToString();
    }

    private static bool IsSpace(char c) => char.IsWhiteSpace(c);
}
