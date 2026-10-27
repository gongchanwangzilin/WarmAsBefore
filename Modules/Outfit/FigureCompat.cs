using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Outfit;

/// <summary>
/// 新旧着装的兼容判定。调用方只需要问一句「要不要回退」：
/// 新版开关关闭、角色没绑定形象包、或包目录里根本没有 figure.json 时，回退到现有
/// <c>CharacterData.SpriteMap["{outfit}/{emotion}"]</c> 的静态立绘逻辑。
/// 旧代码一行都不用改，旧存档也不会因为缺 FigurePackId 而变成空白。
/// </summary>
public static class FigureCompat
{
    public static bool ShouldFallbackToSpriteMap(bool outfitSystemNew, string? figurePackId, bool packExists)
        => !outfitSystemNew || string.IsNullOrWhiteSpace(figurePackId) || !packExists;

    /// <summary>便捷重载：直接按当前设置与角色判定（包是否存在的检查走静态数据根）。</summary>
    public static bool ShouldFallbackToSpriteMap(SettingsManager? settings, CharacterData? character)
    {
        if (character is null || settings?.Current is null) return true;
        return ShouldFallbackToSpriteMap(
            settings.Current.OutfitSystemNew,
            character.FigurePackId,
            FigurePackService.PackExists(character.FigurePackId));
    }

    /// <summary>旧版立绘键：与 SpriteMap 的既有约定保持一致，方便回退分支直接取值。</summary>
    public static string SpriteKey(string? outfitId, string? emotion)
    {
        var outfit = string.IsNullOrWhiteSpace(outfitId) ? "default" : outfitId.Trim();
        var emo = string.IsNullOrWhiteSpace(emotion) ? "normal" : emotion.Trim();
        return outfit + "/" + emo;
    }
}
