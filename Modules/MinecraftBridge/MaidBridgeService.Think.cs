using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace WarmAsBefore.Modules.MinecraftBridge;

/// <summary>
/// 场景触发思考：她碰上事——被摸、被打、同伴倒下、天变了、主人上床——就主动想一次。
/// 想出来的那句话说回游戏；要做什么由她自己在回复里调 maid_* 工具，宿主不替她编排动作。
/// 哪些场景会触发、多久允许再触发一次，全在插件设置里；一个都不勾时这套完全静默。
/// </summary>
public sealed partial class MaidBridgeService
{
    /// <summary>可触发的场景清单。设置面板按这份渲染，键改了面板就点不亮。</summary>
    internal static readonly (string Key, string Label, string Hint)[] ThinkSceneCatalog =
    {
        ("interaction", "玩家对她做动作",
            "摸头、拥抱、亲吻、打开她的背包都算。以她本人的立场想一次此刻的感受，可以只说一句，也可以顺手做点什么。"),
        ("hurt", "她被打伤", "她受伤了。可以委屈、可以躲、可以喊主人，别替玩家找理由。"),
        ("death", "她死了", "她倒下了。这是大事，好好想一次。"),
        ("respawn", "她复活回来", "她重新站起来了。记得刚才发生了什么，再决定说什么。"),
        ("owner_sleep", "主人上床睡觉",
            "主人睡了。可以走过去靠在他身上或床边，捧着脸看他一会儿，说一句只有这时候才会说的话，"
            + "再自己决定睡在哪儿——有床用床，没床就趴在地上。"
            + "要用的动作都在工具里：maid_go 靠过去、maid_act 的 look/gesture/say/sleep。"),
        ("maid_chat", "别的女仆跟她搭话", "同伴说的话。她是自己回，还是找主人告状，由她判。"),
        ("weather", "天气变化", "天变了（放晴、落雨、打雷）。她可以在雨里说一句，也可以跑去躲。"),
        ("time", "时段变化", "天亮或天黑这种整段切换，不是每过一分钟都想一遍。")
    };

    private readonly ConcurrentDictionary<string, DateTime> _thinkAt = new(StringComparer.Ordinal);
    private readonly object _thinkGate = new();

    private bool ThinkSceneOn(string scene) =>
        _config.ThinkScenes.Any(s => string.Equals(s, scene, StringComparison.OrdinalIgnoreCase));

    /// <summary>同一条「角色+场景」隔够了才放行；多个女仆互相搭话时不至于把模型打爆。</summary>
    private bool TryClaimThink(string key)
    {
        var now = DateTime.UtcNow;
        var gap = Math.Max(5, _config.ThinkCooldownSeconds);
        lock (_thinkGate)
        {
            if (_thinkAt.TryGetValue(key, out var last) && (now - last).TotalSeconds < gap) return false;
            _thinkAt[key] = now;
        }
        return true;
    }

    private static string SceneLabel(string scene)
    {
        foreach (var s in ThinkSceneCatalog)
            if (string.Equals(s.Key, scene, StringComparison.OrdinalIgnoreCase)) return s.Label;
        return scene;
    }

    private static string SceneHint(string scene)
    {
        foreach (var s in ThinkSceneCatalog)
            if (string.Equals(s.Key, scene, StringComparison.OrdinalIgnoreCase)) return s.Hint;
        return "以她自己的立场想一次。";
    }

    /// <summary>
    /// 让她为这件事想一次。存档对不上、场景没开、还没绑定角色、刚想过——都安静跳过，不报错。
    /// </summary>
    private async Task ThinkSceneAsync(MaidSession session, string scene, string cue, string maidId)
    {
        if (!session.SaveActive || !ThinkSceneOn(scene)) return;
        if (!session.MaidCharacters.TryGetValue(maidId, out var charId) || charId.Length == 0) return;
        if (!TryClaimThink($"{charId}|{scene}")) return;

        var context = BuildContext(charId);
        if (context.Length == 0) return;

        var hint = SceneHint(scene);
        // 爱语关掉不等于她不陪睡：照样过去、照样睡下，只是这一晚不出声。
        if (scene == "owner_sleep" && !_config.LoveWords)
            hint = "走过去靠在他身上或床边睡下，这一晚不要说话（也别调用 maid_act 的 say）。";
        var prompt = $"(系统事件·{SceneLabel(scene)}：{cue}\n"
                     + $"下面是你此刻的身体与世界状态，不是主人对你说的话。\n{context}\n"
                     + $"{hint}\n"
                     + "想做点什么就直接调用 maid_* 工具去做，想说话就说一句；不要复述这段说明，也不要提「系统」「提示词」这类词。)";
        try
        {
            await _turnLock.WaitAsync(session.Cts.Token);
            string reply;
            try
            {
                reply = await OnUiAsync(() => _chat.Send(charId, prompt));
            }
            finally
            {
                _turnLock.Release();
            }
            if (string.IsNullOrWhiteSpace(reply)) return;

            await session.SendAsync(new JsonObject
            {
                ["v"] = ProtocolVersion,
                ["type"] = "chat_reply",
                ["maid"] = maidId,
                ["character"] = charId,
                ["ok"] = true,
                ["text"] = reply,
                ["unsolicited"] = true,
                ["scene"] = scene
            });
            await StoreGameChatAsync(charId, $"（{SceneLabel(scene)}）", reply);
        }
        catch (Exception ex)
        {
            App.WriteLog($"MaidBridge.Think.{scene} -> " + ex.Message);
        }
    }

    /// <summary>天气/时段这类「世界的事」由第一个绑定到角色的女仆来感知，别一屋子女仆同时想。</summary>
    private async Task ThinkWorldAsync(MaidSession session, string scene, string cue)
    {
        foreach (var (maidId, charId) in session.MaidCharacters)
            if (!string.IsNullOrEmpty(charId))
            {
                await ThinkSceneAsync(session, scene, cue, maidId);
                return;
            }
    }

    /// <summary>模组的天气码翻成人话——她的提示词里不该出现 thunder 这种词。</summary>
    internal static string WeatherName(string weather)
    {
        var w = weather?.Trim().ToLowerInvariant() ?? "";
        return w switch
        {
            "clear" => "晴天",
            "rain" => "下雨",
            "thunder" => "雷暴",
            "snow" => "下雪",
            _ => w.Length > 0 ? w : "变了"
        };
    }

    /// <summary>
    /// 把模组的 "HH:MM" 归成时段。只有跨段才算「时间变了」——
    /// 按分钟比的话她的世界每 50 秒就变一次天，冷却也拦不住这么碎的触发。
    /// </summary>
    internal static string DayPhase(string clock)
    {
        var text = clock?.Trim() ?? "";
        var colon = text.IndexOf(':');
        if (!int.TryParse(colon > 0 ? text[..colon] : text, out var hour)) return "";
        hour = ((hour % 24) + 24) % 24;
        return hour switch
        {
            < 5 => "深夜",
            < 8 => "清晨",
            < 17 => "白天",
            < 20 => "黄昏",
            _ => "夜里"
        };
    }
}
