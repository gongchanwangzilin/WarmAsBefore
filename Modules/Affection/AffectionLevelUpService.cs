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

/// <summary>立绘路径快速解析（与主界面 ApplySprite 同策略的简化版）。</summary>
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
        if (string.IsNullOrWhiteSpace(rel)) return null;

        var candidates = new List<string>();
        candidates.Add(Path.Combine(storeRoot, rel));
        candidates.Add(Path.Combine(App.RootDirectory, rel));
        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), rel));
        if (!rel.StartsWith("assets/", StringComparison.Ordinal) && !rel.StartsWith(Path.DirectorySeparatorChar.ToString()))
            candidates.Add(Path.Combine(storeRoot, "assets", rel));
        if (rel.StartsWith("assets/", StringComparison.Ordinal))
            candidates.Add(Path.Combine(storeRoot, rel["assets/".Length..]));
        if (rel.StartsWith(Path.DirectorySeparatorChar.ToString()) || rel.StartsWith("/"))
            candidates.Add(rel);
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }
        return null;
    }
}