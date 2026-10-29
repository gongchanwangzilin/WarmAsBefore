namespace WarmAsBefore.Services;

/// <summary>
/// 角色背景的共用取法：角色库预览、工作室舞台、游戏内舞台都从这里要同一条答案。
/// 背景可以是图片 / GIF / 视频，适配方式由 CharacterData.BackgroundFit 决定。
/// </summary>
public static class CharacterBackgroundService
{
    /// <summary>解析出盘上真有的背景绝对路径；没设背景/文件不在返回 null（调用方回退到场景背景）。</summary>
    public static string? Resolve(Models.CharacterData? ch, string storeRoot, bool inGame = false)
    {
        if (ch is null || string.IsNullOrWhiteSpace(ch.Background)) return null;
        if (inGame && !ch.BackgroundInGame) return null;
        return Modules.Affection.SpritePathResolver.ResolveFile(ch.Background, storeRoot);
    }

    public static string NormalizeFit(string? fit) => (fit ?? "fill").ToLowerInvariant() switch
    {
        "tile" => "tile",
        "stretch" => "stretch",
        "fit" => "fit",
        "crop" => "crop",
        _ => "fill",
    };

    public static string FitLabel(string? fit) => NormalizeFit(fit) switch
    {
        "tile" => "平铺",
        "stretch" => "拉伸",
        "fit" => "完整显示",
        "crop" => "自定义裁剪",
        _ => "填充",
    };

    public static bool IsVideo(string? path)
    {
        var ext = Path.GetExtension(path ?? "").ToLowerInvariant();
        return ext is ".mp4" or ".mov" or ".m4v" or ".webm";
    }

    public static bool IsAnimated(string? path)
        => string.Equals(Path.GetExtension(path ?? ""), ".gif", StringComparison.OrdinalIgnoreCase);

    public static bool IsImage(string? path)
    {
        var ext = Path.GetExtension(path ?? "").ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".gif";
    }

    /// <summary>把图片/GIF 存到角色目录（背景统一放在 assets/characters/{id}/background{ext}）。</summary>
    public static async Task<string> ImportAsync(string characterId, string sourcePath, string storeRoot)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".gif"
            or ".mp4" or ".mov" or ".m4v" or ".webm"))
            throw new InvalidOperationException("不支持的背景格式");

        var dir = Path.Combine(storeRoot, "assets", "characters", characterId);
        Directory.CreateDirectory(dir);
        // 换格式时旧文件一并删掉，否则角色目录里会留下再也用不到的背景。
        foreach (var old in Directory.EnumerateFiles(dir, "background.*"))
        {
            try { File.Delete(old); } catch { }
        }
        var target = Path.Combine(dir, "background" + ext);
        await using (var src = File.OpenRead(sourcePath))
        await using (var dst = File.Create(target))
            await src.CopyToAsync(dst);
        return Path.GetRelativePath(storeRoot, target).Replace('\\', '/');
    }
}
