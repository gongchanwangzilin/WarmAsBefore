using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// F27 的纯代码规则层：① 图像 → 具体图形/词汇/联系；② 淡忘 → 沿「联系」向邻居扩散。
/// 全部静态纯函数，不碰存储、不碰时间、不碰模型，方便独立自测。
///
/// 论文 §8 的教训：衰减传播是正反馈，若不设下界与跳数上限会「一淡全淡」锁死。
/// 所以这里：只传一跳、每邻居一次最多削 SpreadCap、Importance 不低于 Floor。
/// </summary>
public static class MemoryAbstractionRules
{
    /// <summary>淡忘沿联系传播时，单条邻居一次最多被削多少。</summary>
    public const double SpreadCap = 0.12;

    /// <summary>重要性下界：再怎么淡也不会低于它，防正反馈锁死。</summary>
    public const double Floor = 0.05;

    /// <summary>形状线索词表：纯代码从文字里认「具体图形」。</summary>
    private static readonly (string Word, string Shape)[] ShapeCues =
    {
        ("圆", "圆形"), ("球", "球形"), ("月", "弯月形"), ("方", "方形"), ("框", "方框"),
        ("三角", "三角形"), ("尖", "尖角"), ("长条", "长条形"), ("柱", "柱形"), ("拱", "拱形"),
        ("心", "心形"), ("星", "星形"), ("山", "山形轮廓"), ("波浪", "波浪线"), ("螺旋", "螺旋")
    };

    /// <summary>主色线索词表：把颜色词抽象成「主色」图形属性。</summary>
    private static readonly string[] ColorCues =
    {
        "红", "橙", "黄", "绿", "青", "蓝", "紫", "粉", "白", "黑", "灰", "金", "银"
    };

    private static readonly char[] SplitChars =
    {
        ' ', '，', '。', '、', '：', '；', '！', '？', '"', '"', '「', '」', '『', '』',
        '(', ')', '（', '）', '\n', '\t', ',', '.', '!', '?', '-', '_'
    };

    /// <summary>
    /// 把一张图片记忆抽象成三元组。width/height 由调用方传入（ImageProbe 读到的原始尺寸），
    /// 传 0 表示读不到，就只按文字线索抽象；绝不为了抽象而去解码像素、更不调模型。
    /// </summary>
    public static ImageAbstraction Abstract(string? text, string? imagePath, IEnumerable<string>? tags,
        int width, int height, IEnumerable<string>? existingAssociations)
    {
        var t = text ?? "";
        var shapes = new List<string>();

        // ① 图形：宽高比 → 画幅，线索词 → 具体形状，颜色词 → 主色
        if (width > 0 && height > 0)
        {
            var ratio = width / (double)height;
            shapes.Add(ratio > 1.35 ? "横幅" : ratio < 0.74 ? "竖幅" : "方幅");
        }
        foreach (var (word, shape) in ShapeCues)
            if (t.Contains(word, StringComparison.Ordinal) && !shapes.Contains(shape)) shapes.Add(shape);
        foreach (var c in ColorCues)
            if (t.Contains(c, StringComparison.Ordinal) && !shapes.Contains(c + "色主调"))
                shapes.Add(c + "色主调");

        // 文件名线索也承认（截图名常常写着内容），但不把路径本身当词汇
        var file = "";
        try { file = Path.GetFileNameWithoutExtension(imagePath ?? ""); } catch { }
        foreach (var (word, shape) in ShapeCues)
            if (file.Contains(word, StringComparison.Ordinal) && !shapes.Contains(shape)) shapes.Add(shape);

        // ② 词汇：文本切词 + 标签，去空、去重、限长
        var words = new List<string>();
        foreach (var raw in t.Split(SplitChars, StringSplitOptions.RemoveEmptyEntries))
        {
            var w = raw.Trim();
            if (w.Length is >= 2 and <= 8 && !words.Contains(w, StringComparer.OrdinalIgnoreCase)) words.Add(w);
            if (words.Count >= 10) break;
        }
        foreach (var tag in tags ?? Array.Empty<string>())
        {
            var w = (tag ?? "").Trim();
            if (w.Length is >= 2 and <= 8 && !words.Contains(w, StringComparer.OrdinalIgnoreCase)) words.Add(w);
            if (words.Count >= 12) break;
        }

        // ③ 联系：与已有记忆共享的关联词，越常出现越靠前
        var links = (existingAssociations ?? Array.Empty<string>())
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .GroupBy(a => a.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Key)
            .Take(8)
            .ToList();

        var confidence = Math.Clamp(
            (shapes.Count > 0 ? 0.4 : 0) + Math.Min(0.4, words.Count * 0.05) + Math.Min(0.2, links.Count * 0.04),
            0, 1);
        return new ImageAbstraction
        {
            Shapes = shapes.Take(8).ToList(),
            Words = words,
            Links = links,
            Confidence = confidence
        };
    }

    /// <summary>
    /// ② 网络化淡忘：一条记忆要淡时，按「联系」权重把淡忘传给邻居。
    /// 返回每个邻居应被削多少（0~SpreadCap）。邻居先淡、中心后消：
    /// 中心这次不动，只有邻居被削；中心要等邻居也淡下去才会真正融合消失。
    /// </summary>
    /// <param name="centerImportance">中心记忆此刻的重要性。</param>
    /// <param name="neighborLinks">邻居 id → 与中心的联系强度 0~1（由共享关联词/边权给出）。</param>
    public static Dictionary<string, double> SpreadFade(double centerImportance,
        IEnumerable<KeyValuePair<string, double>> neighborLinks)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        // 中心还很重要时不扩散；越淡、传得越明显，但永远封顶。
        var intensity = Math.Clamp(1.0 - centerImportance, 0, 1);
        if (intensity <= 0) return result;
        foreach (var (id, weight) in neighborLinks)
        {
            if (string.IsNullOrEmpty(id)) continue;
            var w = Math.Clamp(double.IsNaN(weight) ? 0 : weight, 0, 1);
            if (w <= 0) continue;
            result[id] = Math.Min(SpreadCap, SpreadCap * intensity * w);
        }
        return result;
    }

    /// <summary>把扩散来的淡忘落到邻居重要性上，带下界防锁死。</summary>
    public static double ApplyFade(double importance, double fade)
        => Math.Max(Floor, Math.Clamp(importance - Math.Max(0, fade), Floor, 1));

    /// <summary>
    /// 中心是否该在这一轮真正消失：只有当联系到的邻居都已淡到 NearbyFadedBelow 以下
    /// （或压根没有邻居）时，中心才最后消失。这样是「周围先淡、中心后消」。
    /// </summary>
    public static bool CenterMayFade(double neighborMaxImportance, bool hasNeighbors)
        => !hasNeighbors || neighborMaxImportance <= 0.35;

    public const double NearbyFadedBelow = 0.35;
}
