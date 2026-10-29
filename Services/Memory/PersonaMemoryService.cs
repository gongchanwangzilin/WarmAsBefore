using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.ApiManager;

namespace WarmAsBefore.Services;

/// <summary>
/// 人格记忆初始化：把「用户人设」和「世界树」整理成 TA 本来就知道的事。
///
/// 用户人设不进上下文原文：先由 AI 拆成一条条第一人称的记忆，存进人类记忆层；
/// 世界树同理，作为「刻在骨子里」的常识。之后每一轮该不该想起、想起哪几条，
/// 交给记忆层的检索，而不是每轮都把整份人设塞进提示词。
///
/// AI 不可用时用本地分句兜底——初始化必须有结果，不能因为没配模型就把新档卡死。
/// </summary>
public sealed class PersonaMemoryService
{
    /// <summary>世界书整理出的常识统一带这个标签，重绑/重整理时按它整批替换。</summary>
    public const string WorldTag = "世界书";

    /// <summary>
    /// 旧版本写下的标签（那时界面叫「世界树」）。老存档里的那批常识还带着它，
    /// 重整理或解绑时要一并撤掉，否则同一份世界会在记忆里留下两套。
    /// </summary>
    public const string WorldTagLegacy = "世界树";

    private static readonly TimeSpan AiBudget = TimeSpan.FromSeconds(22);

    private readonly HumanMemoryService _human;
    private readonly WorldTreeService _worlds;
    private readonly ApiGateway _api;

    public PersonaMemoryService(HumanMemoryService human, WorldTreeService worlds, ApiGateway api)
    {
        _human = human;
        _worlds = worlds;
        _api = api;
    }

    public sealed record PersonaSplit(List<string> Persona, List<string> World, bool AiUsed);

    /// <summary>
    /// 跑一次完整初始化。每次新档、以及用户改写人设/换绑世界树之后都调它。
    /// <paramref name="progress"/> 报告的是给人看的短句，加载层直接显示。
    /// </summary>
    public async Task<PersonaSplit> InitializeAsync(GameState state, string charId,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var persona = (state.UserPersona ?? "").Trim();
        var worldText = "";
        progress?.Report("正在读取世界书…");
        try { worldText = await _worlds.DescribeForPromptAsync(state.WorldId); }
        catch (Exception ex) { App.WriteLog("PersonaMemory.World -> " + ex.Message); }

        PersonaSplit split;
        if (persona.Length == 0 && worldText.Length == 0)
        {
            split = new PersonaSplit(new(), new(), false);
            progress?.Report("这一档没有要整理的人设与世界观");
        }
        else
        {
            progress?.Report("正在把设定整理成记忆…");
            split = await SplitAsync(persona, worldText, progress, ct) ?? LocalSplit(persona, worldText);
            progress?.Report(split.AiUsed ? "AI 已整理好，正在写入记忆…" : "AI 暂不可用，已按本地分句写入记忆…");
        }

        if (split.Persona.Count > 0)
            await _human.ReplaceUserSelfAsync(split.Persona);
        if (!string.IsNullOrWhiteSpace(charId))
        {
            if (split.World.Count > 0)
                await _human.ReplaceTaggedCoreAsync(charId, WorldTag, split.World);
            else
                await _human.RemoveTaggedCoreAsync(charId, WorldTag);
            await _human.RemoveTaggedCoreAsync(charId, WorldTagLegacy);
        }
        state.PersonaMemoryReady = true;
        App.WriteLog($"PersonaMemory: 初始化完成 persona={split.Persona.Count} world={split.World.Count} ai={split.AiUsed}");
        return split;
    }

    /// <summary>
    /// 只补「世界树」那一份常识，用户人设一个字都不碰。
    /// 给桥这类外挂用：ReplaceUserSelfAsync 改的是全部角色共用的「用户是谁」，
    /// 桥替她整理世界书顺手重拆了一遍人设，等于隔空改了主对话的设定。
    /// 返回写进去的条数；这一档没绑世界树、或模型和本地都拆不出东西时返回 0（原有的原样留着）。
    /// </summary>
    public async Task<int> SeedWorldAsync(GameState state, string charId)
    {
        if (state is null || string.IsNullOrWhiteSpace(charId)) return 0;
        var worldText = "";
        try { worldText = await _worlds.DescribeForPromptAsync(state.WorldId); }
        catch (Exception ex)
        {
            App.WriteLog("PersonaMemory.SeedWorld.Read -> " + ex.Message);
            return 0;
        }
        if (worldText.Trim().Length == 0) return 0;

        var split = await SplitAsync("", worldText, null, CancellationToken.None) ?? LocalSplit("", worldText);
        if (split.World.Count == 0) return 0;
        await _human.ReplaceTaggedCoreAsync(charId, WorldTag, split.World);
        await _human.RemoveTaggedCoreAsync(charId, WorldTagLegacy);
        App.WriteLog($"PersonaMemory.SeedWorld: char={charId} world={split.World.Count} ai={split.AiUsed}");
        return split.World.Count;
    }

    /// <summary>只改用户人设时用：世界树原样保留。</summary>
    public async Task RefreshPersonaAsync(GameState state, string charId, IProgress<string>? progress = null)
    {
        var persona = (state.UserPersona ?? "").Trim();
        progress?.Report("正在重新整理你的人设…");
        var split = await SplitAsync(persona, "", progress, CancellationToken.None) ?? LocalSplit(persona, "");
        await _human.ReplaceUserSelfAsync(split.Persona);
        state.PersonaMemoryReady = true;
        App.WriteLog($"PersonaMemory: 人设刷新 persona={split.Persona.Count} ai={split.AiUsed}");
    }

    private async Task<PersonaSplit?> SplitAsync(string persona, string worldText,
        IProgress<string>? progress, CancellationToken ct)
    {
        if (persona.Length == 0 && worldText.Length == 0) return null;
        try
        {
            const string system =
                "你是记忆整理器。把输入拆成角色「本来就知道」的事实记忆，只输出 JSON，不要 markdown 代码块。\n"
                + "规则：\n"
                + "1) persona：用户是谁、和角色是什么关系、长期成立的事实（婚姻、职业、习惯、称呼、边界、忌讳）。"
                + "以角色第一人称记得的口气写，每条不超过 40 字；推断不出的不要编。\n"
                + "2) world：这个世界里一直成立的常识，不要写具体剧情，每条不超过 50 字。\n"
                + "3) 输出 {\"persona\":[\"...\"],\"world\":[\"...\"]}；某一项没有就给空数组。";
            var content = (persona.Length > 0 ? "用户人设：\n" + persona + "\n\n" : "")
                          + (worldText.Length > 0 ? "世界设定：\n" + worldText : "");
            var task = _api.Chat(new List<ChatMessage>
            {
                new() { Role = "system", Content = system },
                new() { Role = "user", Content = content }
            });
            var done = await Task.WhenAny(task, Task.Delay(AiBudget, ct));
            if (done != task)
            {
                App.WriteLog("PersonaMemory.Split -> 超过预算，改用本地分句");
                return null;
            }
            var raw = await task;
            var json = ExtractJson(raw);
            if (json is null) return null;
            using var doc = JsonDocument.Parse(json);
            var p = ReadLines(doc.RootElement, "persona");
            var w = ReadLines(doc.RootElement, "world");
            if (p.Count == 0 && w.Count == 0) return null;
            return new PersonaSplit(p, w, true);
        }
        catch (Exception ex)
        {
            App.WriteLog("PersonaMemory.Split -> " + ex.Message);
            return null;
        }
    }

    private static List<string> ReadLines(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array) return new();
        return el.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => (x.GetString() ?? "").Trim())
            .Where(x => x.Length >= 2)
            .Select(x => x.Length > 60 ? x[..60] : x)
            .Distinct(StringComparer.Ordinal)
            .Take(24)
            .ToList();
    }

    /// <summary>没配模型/额度用完/超时：按标点与换行拆句，至少让新档跑得起来。</summary>
    private static PersonaSplit LocalSplit(string persona, string worldText)
    {
        var p = SplitSentences(persona);
        var w = SplitSentences(worldText);
        return new PersonaSplit(p, w, false);
    }

    private static List<string> SplitSentences(string text)
    {
        var list = new List<string>();
        foreach (var raw in (text ?? "").Split(new[] { '\n', '\r', '。', '！', '？', '；', ';', '，', ',' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim().TrimStart('·', '-', '*', ' ');
            if (line.Length < 2) continue;
            if (line.Length > 60) line = line[..60];
            if (!list.Contains(line, StringComparer.Ordinal)) list.Add(line);
            if (list.Count >= 24) break;
        }
        return list;
    }

    private static string? ExtractJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : null;
    }
}
