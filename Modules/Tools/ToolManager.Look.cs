using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.Outfit;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Tools;

/// <summary>
/// 立绘工具：装扮与表情一律「先查编号，再报编号」。
///
/// 分两个工具而不是一个工具两种模式（list / set 分开）：查询是一次读取，选择是一次写入，
/// 分开之后 set 的参数只剩一个整数，模型不必复述「夏日泳装」这类中文名（既贵又容易写错）。
/// 生成新装扮仍然是创造，走 generate_image；本文件只负责「穿上已经有的那一套」。
///
/// 编号的稳定性：编号按「default/normal 固定在前，其余按 key 排序」生成，
/// 同一份数据算几次都一样；换了装扮或换了角色，表情轴会跟着变，要重新 list。
/// </summary>
public sealed partial class ToolManager
{
    /// <summary>list 一次最多给多少条：够一屏角色能穿的装扮，也不至于把上下文塞满。</summary>
    private const int LookPageSize = 20;

    private void RegisterLookTools()
    {
        RegisterBuiltin("outfit_list",
            "列出 TA 能穿的装扮：number（编号）、name、正在穿第几号。"
            + "换装扮先 list 再 outfit_set(number)，别凭记忆写装扮名；要一套新的用 generate_image。",
            async args =>
            {
                var (ch, book, error) = await LoadLookAsync(args);
                if (ch is null || book is null) return Fail(error);
                var page = Math.Max(1, GetInt(args, "page", 1));
                var limit = Math.Clamp(GetInt(args, "limit", LookPageSize), 1, LookPageSize);
                var worn = book.Outfits.FirstOrDefault(o =>
                    string.Equals(o.Key, ch.State.CurrentOutfit, StringComparison.OrdinalIgnoreCase));
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    character = new { id = ch.Profile.Id, name = ch.Profile.Name },
                    wearing = worn?.Number,
                    total = book.Outfits.Count,
                    page,
                    page_size = limit,
                    has_more = page * limit < book.Outfits.Count,
                    outfits = Page(book.Outfits, page, limit).Select(o => new { number = o.Number, name = o.Name }).ToList(),
                    hint = book.Outfits.Count == 0
                        ? "TA 现在一套装扮立绘都没有。要一套就用 generate_image（kind=outfit，prompt 写画面）。"
                        : "换装扮用 outfit_set，参数只给 number。"
                });
            },
            """
            {"type":"object","properties":{
              "character":{"type":"string","description":"可选：角色 id 或名字，缺省=当前主角"},
              "page":{"type":"integer","description":"页码，从 1 开始"},
              "limit":{"type":"integer","description":"每页条数 1~20"}},
             "additionalProperties":false}
            """);

        RegisterBuiltin("outfit_set",
            "换上已有的一套装扮：只给 number（取自 outfit_list），先 list 再 set。"
            + "生成新装扮用 generate_image，不要用本工具重画。",
            async args =>
            {
                var number = GetInt(args, "number", 0);
                if (number <= 0) return Fail("需要 number（装扮编号，取自 outfit_list）");
                var (ch, book, error) = await LoadLookAsync(args);
                if (ch is null || book is null) return Fail(error);

                var item = LookAt(book.Outfits, number);
                if (item is null)
                    return Fail($"没有 {number} 号装扮（现在共 {book.Outfits.Count} 套）。先调 outfit_list 重新取编号。");

                // 换了装扮，原来的表情未必还在这一套里：不在就落到这一套的第一个，别留一个空表情。
                var emotions = book.FigurePack
                    ? book.Emotions
                    : Numbered(SpriteEmotions(ch, item.Key), withHint: true);
                var keep = emotions.FirstOrDefault(e =>
                    string.Equals(e.Key, ch.State.CurrentEmotion, StringComparison.OrdinalIgnoreCase));
                var emotion = keep?.Key ?? emotions.FirstOrDefault()?.Key;

                ch.State.CurrentOutfit = item.Key;
                // 这一套没有表情立绘时保留原表情：硬写一个不存在的键会让立绘变空。
                if (!string.IsNullOrEmpty(emotion)) ch.State.CurrentEmotion = emotion;
                var saved = await PersistLookAsync(ch);
                await RefreshPetLookAsync();

                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    outfit = new { number = item.Number, name = item.Name },
                    emotion = ch.State.CurrentEmotion,
                    saved,
                    hint = saved
                        ? "已换上。表情清单可能跟着变，换表情前先 expression_list。"
                        : "已经改了当前状态，但落盘失败：重启后可能回到旧装扮。"
                });
            },
            """
            {"type":"object","properties":{
              "number":{"type":"integer","description":"装扮编号，取自 outfit_list"},
              "character":{"type":"string","description":"可选：角色 id 或名字，缺省=当前主角"}},
             "required":["number"],"additionalProperties":false}
            """);

        RegisterBuiltin("expression_list",
            "列出 TA 此刻能换的表情：number（编号）、name、use（适合什么场面）。"
            + "表情跟着当前装扮变，换过装扮要重新 list；换表情用 expression_set(number)。",
            async args =>
            {
                var (ch, book, error) = await LoadLookAsync(args);
                if (ch is null || book is null) return Fail(error);
                var page = Math.Max(1, GetInt(args, "page", 1));
                var limit = Math.Clamp(GetInt(args, "limit", LookPageSize), 1, LookPageSize);
                var worn = book.Outfits.FirstOrDefault(o =>
                    string.Equals(o.Key, ch.State.CurrentOutfit, StringComparison.OrdinalIgnoreCase));
                var current = book.Emotions.FirstOrDefault(e =>
                    string.Equals(e.Key, ch.State.CurrentEmotion, StringComparison.OrdinalIgnoreCase));
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    character = new { id = ch.Profile.Id, name = ch.Profile.Name },
                    outfit = worn is null ? null : new { number = worn.Number, name = worn.Name },
                    wearing = current?.Number,
                    total = book.Emotions.Count,
                    page,
                    page_size = limit,
                    has_more = page * limit < book.Emotions.Count,
                    emotions = Page(book.Emotions, page, limit)
                        .Select(e => new { number = e.Number, name = e.Name, use = e.Hint }).ToList(),
                    hint = book.Emotions.Count == 0
                        ? "这一套装扮没有表情立绘：只能保持现在这张脸。"
                        : "换表情用 expression_set，参数只给 number。"
                });
            },
            """
            {"type":"object","properties":{
              "character":{"type":"string","description":"可选：角色 id 或名字，缺省=当前主角"},
              "page":{"type":"integer","description":"页码，从 1 开始"},
              "limit":{"type":"integer","description":"每页条数 1~20"}},
             "additionalProperties":false}
            """);

        RegisterBuiltin("expression_set",
            "换一个表情：只给 number（取自 expression_list），先 list 再 set。"
            + "只管当下这一幕，下一句回复仍可能自己换。",
            async args =>
            {
                var number = GetInt(args, "number", 0);
                if (number <= 0) return Fail("需要 number（表情编号，取自 expression_list）");
                var (ch, book, error) = await LoadLookAsync(args);
                if (ch is null || book is null) return Fail(error);

                var item = LookAt(book.Emotions, number);
                if (item is null)
                    return Fail(book.Emotions.Count == 0
                        ? "这一套装扮没有表情立绘。"
                        : $"没有 {number} 号表情（现在共 {book.Emotions.Count} 个）。先调 expression_list 重新取编号。");

                ch.State.CurrentEmotion = item.Key;
                var saved = await PersistLookAsync(ch);
                await RefreshPetLookAsync();
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    emotion = new { number = item.Number, name = item.Name },
                    saved,
                    hint = saved ? "表情已换。" : "已经改了当前状态，但落盘失败：重启后可能回到旧表情。"
                });
            },
            """
            {"type":"object","properties":{
              "number":{"type":"integer","description":"表情编号，取自 expression_list"},
              "character":{"type":"string","description":"可选：角色 id 或名字，缺省=当前主角"}},
             "required":["number"],"additionalProperties":false}
            """);
    }

    // ============ 编号表 ============

    /// <summary>编号表里的一项。编号只对「这一份表」成立：换装/换角色后要重新 list。</summary>
    private sealed record LookEntry(int Number, string Key, string Name, string Hint);

    /// <summary>一次查询的结果：装扮轴 + 当前装扮的表情轴。</summary>
    private sealed record LookBook(List<LookEntry> Outfits, List<LookEntry> Emotions, bool FigurePack);

    private static LookEntry? LookAt(List<LookEntry> list, int number)
        => number >= 1 && number <= list.Count ? list[number - 1] : null;

    private static IEnumerable<LookEntry> Page(List<LookEntry> list, int page, int limit)
        => list.Skip((page - 1) * limit).Take(limit);

    /// <summary>
    /// 编号：default / normal 固定在第一号，其余按 key 排序 —— 同一份数据算几次都一样，
    /// 模型先 list 后 set 之间不会因为排序变化而错位。
    /// </summary>
    private static List<LookEntry> Numbered(IEnumerable<(string Key, string Name)> items, bool withHint)
    {
        var ordered = items
            .Where(i => !string.IsNullOrWhiteSpace(i.Key))
            .DistinctBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(i => IsHeadKey(i.Key) ? 0 : 1)
            .ThenBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return ordered
            .Select((i, idx) => new LookEntry(
                idx + 1,
                i.Key,
                string.IsNullOrWhiteSpace(i.Name) ? i.Key : i.Name,
                withHint ? EmotionHint(i.Key) : ""))
            .ToList();
    }

    private static bool IsHeadKey(string key)
        => string.Equals(key, "default", StringComparison.OrdinalIgnoreCase)
           || string.Equals(key, "normal", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 表情名 → 什么时候用。名字是素材里自带的（中文/英文都有），认识的给一句，不认识的留空，
    /// 让模型按名字自己判断 —— 编一句不相关的说明比空着更糟。
    /// </summary>
    private static string EmotionHint(string key)
    {
        var k = key.ToLowerInvariant();
        foreach (var (words, hint) in EmotionHintTable)
        {
            foreach (var w in words)
                if (k.Contains(w, StringComparison.Ordinal)) return hint;
        }
        return "";
    }

    private static readonly (string[] Words, string Hint)[] EmotionHintTable =
    {
        (new[] { "normal", "平静", "无表情" }, "日常对话，不必特意做表情"),
        (new[] { "smile", "开心", "高兴", "微笑" }, "心情好，但别夸张"),
        (new[] { "shy", "害羞", "脸红", "blush" }, "被夸、被靠近、被看穿"),
        (new[] { "sad", "伤心", "难过", "失落" }, "低落、被冷落"),
        (new[] { "angry", "生气", "恼", "不满" }, "真的介意，不是撒娇"),
        (new[] { "surprise", "惊讶", "吃惊" }, "被打断、听到意外的话"),
        (new[] { "cry", "哭", "泪" }, "情绪到极点，少用"),
        (new[] { "sleep", "困", "闭眼" }, "困倦、安心贴着你"),
        (new[] { "serious", "认真", "思考" }, "正经话题、在想事情"),
        (new[] { "worry", "担心", "不安" }, "替你揪心"),
    };

    // ============ 读角色与立绘轴 ============

    private static async Task<(CharacterData? Char, LookBook? Book, string Error)> LoadLookAsync(string? args)
    {
        var services = ServicesHelper.GetServices();
        var engine = services?.GetService(typeof(GameEngine)) as GameEngine;
        if (engine is null) return (null, null, "游戏引擎不可用");

        var wanted = GetString(args, "character", "").Trim();
        var ch = ResolveLookCharacter(engine, wanted);
        if (ch is null)
            return (null, null, wanted.Length > 0
                ? $"没有这个角色：{wanted}"
                : "当前没有角色：先进一局，或者用 character 指定一个");

        var book = await BuildLookAsync(ch);
        return (ch, book, "");
    }

    /// <summary>
    /// 优先取花名册里那份：generate_image 用 <c>ch with { SpriteMap = … }</c> 换过一次实例，
    /// ActiveCharacter 可能还是旧对象，照着它列装扮会漏掉刚生成的那一套。
    /// </summary>
    private static CharacterData? ResolveLookCharacter(GameEngine engine, string wanted)
    {
        if (wanted.Length > 0)
        {
            if (engine.Roster.TryGetValue(wanted, out var byId)) return byId;
            return engine.Roster.Values.FirstOrDefault(c =>
                string.Equals(c.Profile.Name, wanted, StringComparison.OrdinalIgnoreCase));
        }
        var id = engine.State.CharacterId;
        if (!string.IsNullOrEmpty(id) && engine.Roster.TryGetValue(id, out var active)) return active;
        return engine.ActiveCharacter;
    }

    private static async Task<LookBook?> BuildLookAsync(CharacterData ch)
    {
        var services = ServicesHelper.GetServices();
        var settings = services?.GetService(typeof(SettingsManager)) as SettingsManager;
        var figures = services?.GetService(typeof(FigurePackService)) as FigurePackService;

        FigurePack? pack = null;
        if (figures is not null && !FigureCompat.ShouldFallbackToSpriteMap(settings, ch))
            pack = await figures.LoadAsync(ch.FigurePackId);

        if (pack is not null)
        {
            // 形象包：服装与表情是合成器的两条轴，服装名取自包里登记的名字。
            var outfits = pack.Outfits.Select(o => (Key: o.Id, Name: o.Name)).ToList();
            if (outfits.Count == 0) outfits.Add(("default", "默认装扮"));
            var emotions = new List<(string Key, string Name)> { ("normal", "normal") };
            foreach (var part in pack.Parts)
            {
                foreach (var emotion in part.EmotionTextures.Keys)
                {
                    if (string.IsNullOrWhiteSpace(emotion)) continue;
                    if (emotions.Any(e => string.Equals(e.Key, emotion, StringComparison.OrdinalIgnoreCase))) continue;
                    emotions.Add((emotion, emotion));
                }
            }
            return new LookBook(Numbered(outfits, withHint: false), Numbered(emotions, withHint: true), true);
        }

        // 旧版：SpriteMap 的 key 形如 "{装扮}/{表情}"。
        var spriteOutfits = ch.SpriteMap.Keys
            .Select(k => k.Split('/')[0])
            .Select(k => (Key: k, Name: string.Equals(k, "default", StringComparison.OrdinalIgnoreCase) ? "默认装扮" : k))
            .ToList();
        return new LookBook(
            Numbered(spriteOutfits, withHint: false),
            Numbered(SpriteEmotions(ch, ch.State.CurrentOutfit), withHint: true),
            false);
    }

    private static List<(string Key, string Name)> SpriteEmotions(CharacterData ch, string outfitKey)
        => ch.SpriteMap.Keys
            .Where(k => k.StartsWith(outfitKey + "/", StringComparison.OrdinalIgnoreCase))
            .Select(k => k.Split('/'))
            .Where(p => p.Length >= 2 && !string.IsNullOrWhiteSpace(p[1]))
            .Select(p => (Key: p[1], Name: p[1]))
            .ToList();

    // ============ 写入 ============

    private static async Task<bool> PersistLookAsync(CharacterData ch)
    {
        try
        {
            var services = ServicesHelper.GetServices();
            var library = services?.GetService(typeof(CharacterLibrary)) as CharacterLibrary;
            if (library is null) return false;
            return await library.UpdateAsync(ch);
        }
        catch (Exception ex)
        {
            App.WriteLog("ToolManager.PersistLook -> " + ex.Message);
            return false;
        }
    }

    /// <summary>桌宠立即换脸；主界面立绘属于页面自己的渲染时机，这里不碰 UI。</summary>
    private static Task RefreshPetLookAsync()
        => Helpers.Ui.PostAsync(PetService.RefreshForActiveSaveAsync);
}
