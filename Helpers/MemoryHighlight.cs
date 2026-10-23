using WarmAsBefore.Models;

namespace WarmAsBefore.Helpers;

/// <summary>
/// 记忆列表的句级高亮：把「第几句贴了什么标签」翻译成一行行有底色、可以点的句子。
///
/// 为什么底色不在 Span 上：MAUI 在 Windows 上把 Span 翻成 WinUI 的 Run，而 Run 没有背景属性，
/// `Span.BackgroundColor` 在桌面上会被静默丢掉（Android 却生效）——同一份数据两端长得不一样。
/// 所以底色画在包住句子的那层容器上，Span 只负责把「#标签」那截字缩小、压深。
///
/// 颜色一律取主题资源（换主题后重新装载就跟着变），淡色填充只做底色；
/// 做文字时先压深，避免「填充令牌当文字用 → 看不见」。
/// </summary>
public static class MemoryHighlight
{
    /// <summary>分类的显示名（图例和提示都用它）。</summary>
    public static string SubjectText(string? subject) => subject switch
    {
        "person" => "人",
        "talk" => "话题",
        "place" => "地点",
        "thing" => "物",
        _ => "事"
    };

    /// <summary>分类 → 主题色键。五个分类色相拉开，不看文字也能分清这句记的是什么。</summary>
    public static string FillKey(string? subject) => subject switch
    {
        "person" => "BlueSoft",
        "talk" => "AccentWarm",
        "place" => "BeigeAccent200",
        "thing" => "RedSoft",
        _ => "GreenSoft"
    };

    /// <summary>一句记忆渲染出来的样子：底色 + 正文 + 句尾的 # 标签。</summary>
    public sealed record Line(
        string MemoryId,
        int Index,
        string Plain,
        IReadOnlyList<string> Tags,
        FormattedString Text,
        Color Tint)
    {
        public bool HasTags => Tags.Count > 0;
    }

    /// <summary>底色图例项（列表顶部一行，给底色一个对照表）。</summary>
    public sealed record LegendItem(string Label, Color Fill);

    /// <summary>
    /// 把一条记忆摊成逐句的显示项。<paramref name="subjectOf"/> 由标签表提供（标签名 → 分类），
    /// 查不到就按「事」上色——手工新贴的标签还没来得及分类时不至于变成无色。
    /// </summary>
    public static List<Line> Build(HumanMemoryEntry e, Func<string, string?> subjectOf)
    {
        var sentences = HumanMemoryMath.SplitSentences(e.Text);
        var outp = new List<Line>();
        if (sentences.Count == 0) return outp;

        // 老数据（或没打过标签的）没有句级记录：按整条的标签自动铺一遍，界面上才看得见
        var marks = e.SentenceTags.Count > 0
            ? e.SentenceTags
            : HumanMemoryMath.AutoTag(e.Text, e.Tags);

        var body = Resource("WarmBody", "#4A3D2C");
        for (var i = 0; i < sentences.Count; i++)
        {
            var tags = marks.FirstOrDefault(m => m.Index == i)?.Tags ?? new List<string>();
            tags = tags.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var subject = PrimarySubject(tags, subjectOf);
            var fill = tags.Count == 0 ? Colors.Transparent : Resource(FillKey(subject), "#A5D6A7");

            var fs = new FormattedString
            {
                Spans = { new Span { Text = sentences[i], FontSize = 14, TextColor = body } }
            };
            foreach (var tag in tags)
                fs.Spans.Add(new Span
                {
                    Text = $"  #{tag}",
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Darken(fill, 0.62)
                });

            outp.Add(new Line(e.Id, i, sentences[i], tags, fs, fill.WithAlpha(tags.Count == 0 ? 1f : 0.5f)));
        }
        return outp;
    }

    /// <summary>图例：五个分类一次给全，底色和列表里的句子取同一档透明度，否则对照不上。</summary>
    public static List<LegendItem> Legend()
        => new()
        {
            new LegendItem(SubjectText("person"), Swatch("BlueSoft", "#90CAF9")),
            new LegendItem(SubjectText("talk"), Swatch("AccentWarm", "#F5D8B8")),
            new LegendItem(SubjectText("event"), Swatch("GreenSoft", "#A5D6A7")),
            new LegendItem(SubjectText("place"), Swatch("BeigeAccent200", "#DCC7A0")),
            new LegendItem(SubjectText("thing"), Swatch("RedSoft", "#EF9A9A"))
        };

    private static Color Swatch(string key, string fallback) => Resource(key, fallback).WithAlpha(0.5f);

    private static readonly string[] Priority = { "person", "place", "thing", "talk", "event" };

    /// <summary>一句挂多个分类时，按「人 &gt; 地点 &gt; 物 &gt; 话题 &gt; 事」取主色。</summary>
    private static string PrimarySubject(IReadOnlyList<string> tags, Func<string, string?> subjectOf)
    {
        string? fallback = null;
        foreach (var tag in tags)
        {
            var subject = subjectOf(tag);
            if (string.IsNullOrEmpty(subject)) continue;
            if (fallback is null) fallback = subject;
            if (Array.IndexOf(Priority, subject) == 0) return subject;
        }
        return fallback ?? "event";
    }

    private static Color Resource(string key, string fallback)
    {
        if (Application.Current is { } app && app.Resources.TryGetValue(key, out var v) && v is Color c) return c;
        try { return Color.FromArgb(fallback); }
        catch { return Colors.Gray; }
    }

    /// <summary>把淡色填充压深成能当文字用的墨色（只做数值缩放，不引入新色相）。</summary>
    private static Color Darken(Color c, double k)
        => new((float)Math.Clamp(c.Red * k, 0, 1), (float)Math.Clamp(c.Green * k, 0, 1), (float)Math.Clamp(c.Blue * k, 0, 1));
}
