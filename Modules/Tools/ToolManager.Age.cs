using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Tools;

/// <summary>
/// 角色年龄/生日工具：AI 读得出「TA 几岁、生日哪天、还有几天过生日」，也改得动——
/// 角色亲口说自己多大、剧情里真的过了一次生日时，改的是**记录值**。
///
/// 为什么改记录值而不是直接把 EffectiveAge 存回去：自然增长是读取时按「过了几个生日」
/// 现算的，写回记录等于把推导结果当成事实，下一年会重复叠加。改年龄时只刷新 AgeAsOf
/// （= 这个岁数从今天起算），之后仍然交给 <see cref="CharacterAgeService.EffectiveAge"/>。
/// </summary>
public sealed partial class ToolManager
{
    private void RegisterAgeTools()
    {
        RegisterBuiltin("character_age",
            "读写角色的年龄与生日。action=read 看当前存档角色（或 character 指定的角色）今年几岁、生日哪天、"
            + "还有几天过生日；action=set 改年龄、生日或自然增长开关。"
            + "只在剧情明确需要时改（角色亲口说自己多大、剧情里过生日、用户明确要求），不要每轮都改；"
            + "平时想知道 TA 多大就用 read，别凭印象编。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                var engine = services?.GetService(typeof(GameEngine)) as GameEngine;
                var library = services?.GetService(typeof(CharacterLibrary)) as CharacterLibrary;
                if (engine is null || library is null)
                    return Fail("角色/存档服务不可用");

                var action = GetString(args, "action", "read").Trim().ToLowerInvariant();
                if (action is not ("read" or "set"))
                    return Fail("action 只能是 read / set");

                var ch = ResolveAgeCharacter(engine, GetString(args, "character", "").Trim());
                if (ch is null)
                    return Fail("找不到这个角色：给名字或 id，或者先进入一局存档。");

                var now = DateTime.Now;
                if (action == "read") return AgeJson(ch, AgeReadMessage(ch, now), now);

                // ── set：只改调用方明确给出的字段，没给的一律原样保留 ──
                var profile = ch.Profile;
                var notes = new List<string>();
                var nextAge = profile.Age;
                var nextBirthday = profile.Birthday;
                var nextNatural = profile.NaturalAging;

                if (TryGetArg(args, "age", out var ageEl))
                {
                    if (ageEl.ValueKind == JsonValueKind.Null)
                    {
                        nextAge = null;
                        notes.Add("清空了年龄");
                    }
                    else if (ageEl.ValueKind == JsonValueKind.Number && ageEl.TryGetInt32(out var rawAge))
                    {
                        if (rawAge == -1) { nextAge = null; notes.Add("清空了年龄"); }
                        else if (rawAge is >= 0 and <= 300) { nextAge = rawAge; notes.Add($"年龄改成 {rawAge} 岁"); }
                        else return Fail("age 必须是 0~300 的整数；清空请传 -1");
                    }
                    else return Fail("age 必须是整数；清空请传 -1");
                }

                if (TryGetArg(args, "birthday", out var birthdayEl))
                {
                    if (birthdayEl.ValueKind != JsonValueKind.String)
                        return Fail("birthday 必须是字符串，如 03-14；清空请传空字符串");
                    var raw = birthdayEl.GetString() ?? "";
                    var normalized = CharacterAgeService.NormalizeBirthday(raw);
                    // 非空但归一不出来 = 写错了，宁可回一句让人改，也不默默清空一个原本有值的生日。
                    if (raw.Trim().Length > 0 && normalized.Length == 0)
                        return Fail("生日看不懂，请写成 MM-dd（如 03-14）或「3月14日」。");
                    nextBirthday = normalized;
                    notes.Add(normalized.Length == 0 ? "清空了生日" : $"生日改成 {CharacterAgeService.BirthdayDisplay(normalized)}");
                }

                if (TryGetArg(args, "natural_aging", out var naturalEl))
                {
                    if (naturalEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        nextNatural = naturalEl.GetBoolean();
                        notes.Add(nextNatural ? "打开了自然增长" : "关掉了自然增长（年龄冻结在记录值）");
                    }
                    else return Fail("natural_aging 必须是 true 或 false");
                }

                if (notes.Count == 0)
                    return AgeJson(ch, "没有给要改的字段（age / birthday / natural_aging），什么都没改。", now);

                // 改了年龄才刷新起算日；只改生日/开关不动它，否则每改一次就重新数生日，等于白长。
                var nextAgeAsOf = nextAge != profile.Age ? DateTime.Today : profile.AgeAsOf;
                var updated = ch with
                {
                    Profile = profile with
                    {
                        Age = nextAge,
                        Birthday = nextBirthday,
                        AgeAsOf = nextAgeAsOf,
                        NaturalAging = nextNatural
                    }
                };
                var ok = await library.UpdateAsync(updated);
                if (!ok) return Fail("保存失败：角色资料没能写进盘（详见日志）。");

                var message = string.Join("；", notes) + "。" + AgeReadMessage(updated, now);
                return AgeJson(updated, message, now);
            },
            @"{""type"":""object"",""properties"":{"
            + @"""action"":{""type"":""string"",""enum"":[""read"",""set""],""description"":""read=读当前值；set=改值""},"
            + @"""character"":{""type"":""string"",""description"":""角色名或角色 Id；不填=当前存档角色""},"
            + @"""age"":{""type"":""integer"",""description"":""新年龄；-1 表示清空年龄""},"
            + @"""birthday"":{""type"":""string"",""description"":""生日，如 03-14 或 3月14日；空字符串表示清空""},"
            + @"""natural_aging"":{""type"":""boolean"",""description"":""自然增长开关；false=年龄冻结在填的值，不再随生日增长""}"
            + @"},""required"":[""action""]}");
    }

    /// <summary>按 id → 名字 → 名字包含 依次找角色；没指定就用当前存档角色。</summary>
    private static CharacterData? ResolveAgeCharacter(GameEngine engine, string key)
    {
        if (key.Length > 0)
        {
            var byId = engine.Roster.Values.FirstOrDefault(c =>
                string.Equals(c.Profile.Id, key, StringComparison.Ordinal));
            if (byId is not null) return byId;
            var byName = engine.Roster.Values.FirstOrDefault(c =>
                string.Equals(c.Profile.Name, key, StringComparison.OrdinalIgnoreCase));
            if (byName is not null) return byName;
            // 名字带前缀/后缀（「小雨（副本）」）也算：AI 说名字时不会一字不差。
            return engine.Roster.Values.FirstOrDefault(c =>
                c.Profile.Name.Contains(key, StringComparison.OrdinalIgnoreCase));
        }
        // 当前会话正在跟谁说话，就读谁；ActiveCharacter 还没就位时退回存档记录的主角。
        if (engine.ActiveCharacter is { } active) return active;
        return engine.Roster.TryGetValue(engine.State.CharacterId, out var found) ? found : null;
    }

    /// <summary>read/set 共用的返回值结构（字段名是给模型读的，固定住）。</summary>
    private static string AgeJson(CharacterData ch, string message, DateTime now)
    {
        var p = ch.Profile;
        return JsonSerializer.Serialize(new
        {
            ok = true,
            character = p.Id,
            name = p.Name,
            age = p.Age,
            effective_age = CharacterAgeService.EffectiveAge(p, now),
            birthday = p.Birthday,
            birthday_display = CharacterAgeService.BirthdayDisplay(p.Birthday),
            natural_aging = p.NaturalAging,
            birthday_today = CharacterAgeService.IsBirthdayToday(p, now),
            days_to_birthday = CharacterAgeService.DaysToNextBirthday(p, now),
            message
        });
    }

    /// <summary>一句人话摘要：模型和看日志的人都能直接读懂。</summary>
    private static string AgeReadMessage(CharacterData ch, DateTime now)
    {
        var p = ch.Profile;
        var effective = CharacterAgeService.EffectiveAge(p, now);
        var birthday = CharacterAgeService.BirthdayDisplay(p.Birthday);
        var parts = new List<string>();
        if (effective is not null) parts.Add($"「{p.Name}」今年 {effective} 岁");
        if (birthday.Length > 0)
        {
            parts.Add(CharacterAgeService.IsBirthdayToday(p, now)
                ? "今天就是 TA 的生日"
                : $"生日是 {birthday}，距下一个生日还有 {CharacterAgeService.DaysToNextBirthday(p, now)} 天");
        }
        if (parts.Count == 0) return $"「{p.Name}」还没填年龄和生日；要的话用 action=set 填上。";
        var text = string.Join("，", parts) + "。";
        if (!p.NaturalAging) text += "（自然增长关着，岁数不随生日往上加）";
        return text;
    }

    /// <summary>判断参数是否**显式给出**：只有给了才改，没给就保留原值。拿出来的是 Clone，
    /// 因为 JsonDocument 一释放，里面取出的 JsonElement 就失效了。</summary>
    private static bool TryGetArg(string? json, string key, out JsonElement value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(key, out var v)) return false;
            value = v.Clone();
            return true;
        }
        catch { return false; }
    }
}
