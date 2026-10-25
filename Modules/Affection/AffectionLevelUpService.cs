namespace WarmAsBefore.Modules.Affection;

/// <summary>
/// 好感等级提升动画的载荷：由主界面在主线程发起导航前写入，
/// 等级动画页（AffectionLevelUpPage）通过构造函数注入读取。
/// 单一实例，同时只有一条动画在播，天然避免并发重叠。
/// </summary>
public sealed class AffectionLevelUpService
{
    public int Level { get; set; } = 1;
    public string CharacterName { get; set; } = "";
    public string Title { get; set; } = "";
    public string? SpritePath { get; set; }
    public string? BackgroundPath { get; set; }
    public string BackgroundColor { get; set; } = "#2b2140";
    public bool HasPayload { get; set; }
}

/// <summary>立绘路径解析：主界面、好感动画、角色工作室都从这里要同一条答案。</summary>
public static class SpritePathResolver
{
    /// <summary>从角色贴图映射里解析出立绘文件的绝对路径；找不到返回 null。</summary>
    public static string? Resolve(Models.CharacterData ch, string outfitKey, string emotion, string? fallbackEmotion, string storeRoot)
    {
        if (ch is null || string.IsNullOrEmpty(outfitKey)) return null;
        var key = $"{outfitKey}/{emotion}";
        var rel = ch.SpriteMap.TryGetValue(key, out var r)
            ? r
            : ch.SpriteMap.ContainsKey($"{outfitKey}/{fallbackEmotion}")
                ? ch.SpriteMap[$"{outfitKey}/{fallbackEmotion}"]
                : ch.SpriteMap.Values.FirstOrDefault();
        return ResolveFile(rel, storeRoot);
    }

    /// <summary>
    /// 把 SpriteMap / Avatar 里存的那条相对路径解析成盘上真有的绝对路径；找不到返回 null。
    /// 同一张图可能躺在存档目录、程序目录或当前工作目录下，路径本身还可能带或不带 assets/ 前缀——
    /// 只试一条路径的后果是「游戏里站得好好的、工作室里是空的」，因为两屏读的是同一份数据、
    /// 却各自按各自的猜法去找文件。
    /// </summary>
    public static string? ResolveFile(string? rel, string storeRoot)
    {
        if (string.IsNullOrWhiteSpace(rel)) return null;
        foreach (var c in Candidates(rel, storeRoot))
        {
            try { if (File.Exists(c)) return c; } catch { }
        }
        return null;
    }

    /// <summary>这条相对路径可能出现的位置，按「最可能」排在前面；调用方自己挑第一个存在的。</summary>
    public static List<string> Candidates(string rel, string storeRoot)
    {
        var rooted = IsRooted(rel);
        var hasAssetsPrefix = rel.StartsWith("assets/", StringComparison.Ordinal);
        var list = new List<string>
        {
            Path.Combine(storeRoot, rel),
            Path.Combine(App.RootDirectory, rel),
            Path.Combine(Directory.GetCurrentDirectory(), rel)
        };
        if (!hasAssetsPrefix && !rooted)
        {
            list.Add(Path.Combine(storeRoot, "assets", rel));
            // 「characters/xxx」这类路径打包时其实落在 assets/ 底下
            if (rel.StartsWith("characters/", StringComparison.Ordinal))
                list.Add(Path.Combine(storeRoot, "assets", rel));
        }
        if (hasAssetsPrefix)
            list.Add(Path.Combine(storeRoot, rel["assets/".Length..]));
        if (rooted)
            list.Add(rel);
        return list;
    }

    private static bool IsRooted(string rel) =>
        rel.StartsWith(Path.DirectorySeparatorChar.ToString())
        || rel.StartsWith(Path.AltDirectorySeparatorChar.ToString())
        || Path.IsPathRooted(rel);
}