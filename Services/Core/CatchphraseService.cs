using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace WarmAsBefore.Services.Core;

/// <summary>
/// 角色级口头禅（第十六项）：玩家可改、AI 可改可增、也可以没有。
/// AI 改写默认需玩家确认，且只动口头禅字段，不碰人设其它内容。
/// </summary>
public static class CatchphraseService
{
    public const string Key = "catchphrase";

    public static string Read(JsonObject profile, bool enabled)
    {
        if (!enabled) return "";
        return profile[Key]?.GetValue<string>() ?? "";
    }

    public static JsonObject SetByPlayer(JsonObject profile, string? text, bool allowEmpty)
    {
        var next = (JsonObject)profile.DeepClone();
        string value = (text ?? "").Trim();
        if (value.Length == 0 && !allowEmpty) return next;
        next[Key] = value;
        return next;
    }

    /// <summary>AI 改写：调用方必须先拿到玩家确认，未确认一律原样返回。</summary>
    public static JsonObject SetByAi(JsonObject profile, string? text, bool aiEditEnabled, bool confirmed)
    {
        if (!aiEditEnabled || !confirmed) return (JsonObject)profile.DeepClone();
        return SetByPlayer(profile, text, true);
    }

    /// <summary>注入到提示词的一行；没有口头禅时不注入，省 Token。</summary>
    public static string PromptLine(string catchphrase, bool enabled)
    {
        if (!enabled || string.IsNullOrWhiteSpace(catchphrase)) return "";
        return "你的口头禅是「" + catchphrase.Trim() + "」，说话时自然带上，不要每句都用。";
    }
}
