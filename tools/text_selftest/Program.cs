using System.Globalization;
using WarmAsBefore.Helpers;

// 显示层文本边界自测：方向判定 / 软换行 / 计数格式，全是纯函数。
// 跑法：dotnet run --project tools/text_selftest
// 不启动应用、不联网：只把三个函数按住对一遍最坏值（阿拉伯文、混排、纯数字、纯 emoji、长串、负数）。

var pass = 0;
var fail = 0;

void Check(string name, bool ok, string detail = "")
{
    if (ok)
    {
        pass++;
        Console.WriteLine($"  ok   {name}{(detail.Length > 0 ? "  " + detail : "")}");
    }
    else
    {
        fail++;
        Console.WriteLine($"  FAIL {name}  {detail}");
    }
}

void Eq(string name, string actual, string expected)
    => Check(name, actual == expected, $"实际「{Show(actual)}」/ 期望「{Show(expected)}」");

void Same(string name, string actual, string expected)
    => Check(name, actual == expected, actual == expected ? "" : $"实际「{Show(actual)}」/ 期望「{Show(expected)}」");

void Dir(string name, string? text, BidiDirection expected)
{
    var got = BidiText.DetectDirection(text);
    Check(name, got == expected, $"实际 {got} / 期望 {expected}");
}

// 把不可见字符显示出来，失败时看得见「差在哪一个字符」。
string Show(string s) => s.Replace(BidiText.ZeroWidthSpace.ToString(), "␣");

int ZwsCount(string s) => s.Count(c => c == BidiText.ZeroWidthSpace);
string Strip(string s) => s.Replace(BidiText.ZeroWidthSpace.ToString(), "");

// ==================== 1. 段落方向（首个强方向字符） ====================

Console.WriteLine("== 1. BidiText.DetectDirection ==");
{
    const string arabic = "مرحبا بك يا صديقي، هل ما زلت هناك؟";
    const string hebrew = "שלום, מה שלומך היום?";
    Dir("阿拉伯文全句 → RTL", arabic, BidiDirection.Rtl);
    Dir("希伯来文全句 → RTL", hebrew, BidiDirection.Rtl);

    // 混排：整段按首个强字符，不逐词翻转。
    Dir("中阿混排（中以「今」开头）→ LTR", "今天她说了 مرحبا 就走了", BidiDirection.Ltr);
    Dir("阿中混排（阿以「م」开头）→ RTL", "مرحبا，今天见", BidiDirection.Rtl);

    // 数字、标点在首强字符之前一律跳过。
    Dir("数字开头的阿拉伯句 → RTL", "2026-11-22 · " + arabic, BidiDirection.Rtl);
    Dir("数字开头的中文句 → LTR", "12:30 她才起床", BidiDirection.Ltr);
    Dir("标点开头的希伯来句 → RTL", "「" + hebrew, BidiDirection.Rtl);

    // 没有强方向字符：维持原样（Neutral）。
    Dir("纯数字 → Neutral", "2026-11-22 12:30:45", BidiDirection.Neutral);
    Dir("纯 emoji → Neutral", "🥹🥲👋🏽🧑🏽‍🚀", BidiDirection.Neutral);
    Dir("纯标点 → Neutral", "……？！「」—", BidiDirection.Neutral);
    Dir("纯空白 → Neutral", " \t\n  ", BidiDirection.Neutral);
    Dir("空串 → Neutral", "", BidiDirection.Neutral);
    Dir("null → Neutral", null, BidiDirection.Neutral);

    // 常见 CJK / 拉丁都算 L 强字符。
    Dir("中文 → LTR", "那件灰毛衣穿着刚好", BidiDirection.Ltr);
    Dir("拉丁 → LTR", "Aleksandra Wiśniewska", BidiDirection.Ltr);
    Check("IsRtl 与 DetectDirection 一致", BidiText.IsRtl(arabic) && !BidiText.IsRtl("晚安"));
}

// ==================== 2. 软换行（零宽空格） ====================

Console.WriteLine("== 2. BidiText.SoftWrap ==");
{
    // 长 URL：每个分隔符之后都要有一个断点，剥掉零宽空格必须原样还原。
    const string url = "https://example.com/workspaces/acme/projects/q3-launch/docs/9f8e7d6c5b4a?tab=comments&filter=unresolved";
    var wrappedUrl = BidiText.SoftWrap(url);
    Same("长 URL 剥掉 ZWSP 还原原文", Strip(wrappedUrl), url);
    Eq("长 URL 在每个 @ . / - _ : ? 之后断开", ZwsCount(wrappedUrl).ToString(), "12");
    Check("长 URL 插在 '/' 之后", wrappedUrl.Contains("/" + BidiText.ZeroWidthSpace));

    // 40 字符左右的邮箱：整行没有空格，本来一个断点都没有。
    const string email = "bartholomew.fitzgerald@northwind-mail.com";
    Check("邮箱长度在 40 上下", email.Length is >= 39 and <= 44, $"实际 {email.Length}");
    var wrappedEmail = BidiText.SoftWrap(email);
    Check("40 字符邮箱被插了断点", ZwsCount(wrappedEmail) == 4, $"实际 {ZwsCount(wrappedEmail)} 个");
    Same("邮箱剥掉 ZWSP 还原原文", Strip(wrappedEmail), email);
    Check("邮箱插在 '@' 之后", wrappedEmail.Contains("@" + BidiText.ZeroWidthSpace));

    // 短邮箱 / 短路径：长度不到 24，一个字都不动。
    Eq("短邮箱原样返回", BidiText.SoftWrap("a.b@c.cn"), "a.b@c.cn");
    Eq("短路径原样返回", BidiText.SoftWrap("/sdcard/DCIM/a.png"), "/sdcard/DCIM/a.png");

    // 正常中文句子（超过 24 字、没有空格）：不许动。
    const string chinese = "今天下午那阵风把阳台上的衣架全吹倒了，我蹲在地上一件件捡，捡到一半就不想动了。";
    Check("中文长句超过 24 字", chinese.Length > BidiText.SoftWrapMinRun);
    Eq("中文长句一个字都不动", BidiText.SoftWrap(chinese), chinese);

    // 中文句子里夹一条长 URL：只动 URL 那一段。
    var mixed = "刚看到你发的那个文件： " + url + " 打开看看";
    var wrappedMixed = BidiText.SoftWrap(mixed);
    Same("中英混排剥掉 ZWSP 还原原文", Strip(wrappedMixed), mixed);
    Check("中文部分不受影响", wrappedMixed.StartsWith("刚看到你发的那个文件： ", StringComparison.Ordinal)
                          && wrappedMixed.EndsWith(" 打开看看", StringComparison.Ordinal));

    // 分隔符在段尾：后面没有字可断，不插。
    Eq("段尾分隔符不插", BidiText.SoftWrap("abcdefghijklmnopqrstuvwxyz."), "abcdefghijklmnopqrstuvwxyz.");

    // 每串最多 40 个。
    var pathological = new string('-', 200) + "x";
    Check("病态长串被截到 40 个断点", ZwsCount(BidiText.SoftWrap(pathological)) == BidiText.SoftWrapMaxInsertions,
        $"实际 {ZwsCount(BidiText.SoftWrap(pathological))} 个");

    // 空串 / null 原样。
    Eq("空串原样返回", BidiText.SoftWrap(""), "");
    Eq("null 返回空串", BidiText.SoftWrap(null), "");
}

// ==================== 3. 计数格式 ====================

Console.WriteLine("== 3. CountText ==");
{
    var saved = CultureInfo.CurrentCulture;
    CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    try
    {
        Eq("负数给破折号", CountText.Format(-1), "—");
        Eq("大负数也给破折号", CountText.Format(int.MinValue), "—");
        Eq("0", CountText.Format(0), "0");
        Eq("9999 千分位", CountText.Format(9999), "9,999");
        Eq("常规值", CountText.Format(1284), "1,284");

        Eq("徽标 0", CountText.FormatCompact(0), "0");
        Eq("徽标 999 仍是精确值", CountText.FormatCompact(999), "999");
        Eq("徽标 1000 收成 999+", CountText.FormatCompact(1000), "999+");
        Eq("徽标 9999 收成 999+", CountText.FormatCompact(9999), "999+");
        Eq("徽标负数给破折号", CountText.FormatCompact(-3), "—");

        Eq("量词跟着数字一起消失", CountText.FormatWithUnit(-1, "条"), "—");
        Eq("正常带量词", CountText.FormatWithUnit(5, "条"), "5 条");
        Eq("带长量词且千分位", CountText.FormatWithUnit(1200, "条联系"), "1,200 条联系");
        Eq("量词为空等同 Format", CountText.FormatWithUnit(0, null), "0");
    }
    finally
    {
        CultureInfo.CurrentCulture = saved;
    }

    Check("本机区域不产生负号", !CountText.Format(9999).Contains('-'));
}

Console.WriteLine();
Console.WriteLine(fail == 0 ? $"全部通过：{pass} 项" : $"失败 {fail} 项，通过 {pass} 项");
return fail == 0 ? 0 : 1;
