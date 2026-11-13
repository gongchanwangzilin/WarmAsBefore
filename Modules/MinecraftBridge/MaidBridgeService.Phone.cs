using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.MinecraftBridge;

public sealed partial class MaidBridgeService
{
    /// <summary>同一个角色最短发信间隔：模组侧已经节流，这里是宿主侧的兜底，防止旧 jar 或异常帧刷屏。</summary>
    private readonly ConcurrentDictionary<string, DateTime> _lastPhoneAt = new(StringComparer.Ordinal);

    /// <summary>
    /// 女仆戴上「WAB 手机」饰品后，模组会定时发来 phone_message 帧。
    /// 这里让角色本人写一句微信，投进假微信；接了真实聊天通道时才顺带外发
    /// （是否要人工确认、每小时上限仍由 OfficialChatBridge 把着）。
    /// </summary>
    private async Task OnPhoneMessageAsync(MaidSession session, JsonObject msg)
    {
        var maidId = BridgeJson.Str(msg, "maid");
        var hint = BridgeJson.Str(msg, "text");

        // 戴了手机的鞭炮女仆可能还没绑定角色：用和自动加通讯录一样的回退链解析，避免消息被丢。
        var maidName = maidId.Length > 0 && session.Maids.TryGetValue(maidId, out var snap) ? CleanName(snap.Name) : "";
        var world = GetOrCreateWorldLocked(session.WorldKey);
        var charId = await ResolvePhoneCharacterAsync(session, world, maidId, maidName);
        if (charId.Length == 0 || !session.SaveActive)
        {
            Diag("phone message ignored: no bound character / save inactive");
            return;
        }

        var now = DateTime.UtcNow;
        if (_lastPhoneAt.TryGetValue(charId, out var last) && (now - last).TotalSeconds < 60) return;
        _lastPhoneAt[charId] = now;

        var name = _engine.Roster.TryGetValue(charId, out var ch) ? ch.Profile.Name : "她";

        // 让角色自己把游戏里的情境写成一条微信；AI 不可用时就退回模组给的原句，
        // 这样断网、没配 key 的时候手机也不是个哑巴。
        var prompt = "（你现在在 Minecraft 世界里，拿出手机想给主人发一条微信。眼下的情境："
                   + CleanPhoneText(hint) + "）"
                   + "只用一句自然的话写出这条消息，不超过 40 字；不要解释、不要自称 AI、不要加引号或括号。";
        var text = "";
        try { text = CleanPhoneText(await _chat.Send(charId, prompt)); }
        catch (Exception ex) { App.WriteLog("MaidBridge.PhoneCompose -> " + ex.Message); }
        if (text.Length == 0) text = CleanPhoneText(hint);
        if (text.Length == 0) return;

        // 没加过微信的角色先补进联系人，否则消息落了盘，微信里却找不到入口。
        try
        {
            if (!await _wechat.IsFriendAsync(charId)) await _wechat.AddContactAsync(charId);
        }
        catch (Exception ex) { App.WriteLog("MaidBridge.PhoneContact -> " + ex.Message); }

        await _phoneMessages.PublishAsync(charId, text, name, isUser: false);
        try
        {
            await _wechat.TouchLastMessageAsync(charId, text.Length > 20 ? text[..20] : text, unread: true);
        }
        catch (Exception ex) { App.WriteLog("MaidBridge.PhoneTouch -> " + ex.Message); }

        // 真 QQ / 微信公众号接着了才往外发；没有通道就只留在本地假微信里。
        try
        {
            if (_official.CanReachUser(charId))
            {
                var note = _official.QueueOutbound(text, charId);
                App.WriteLog("MaidBridge.PhoneOutbound -> " + note);
            }
        }
        catch (Exception ex) { App.WriteLog("MaidBridge.PhoneOutbound -> " + ex.Message); }

        // 记一笔：她主动找过主人说话，之后聊天里能接得上。
        try
        {
            await _memory.Store(new MemoryEntry
            {
                CharacterId = charId,
                Content = $"你在 Minecraft 里用手机给主人发了条消息：{Truncate(text, 120)}",
                Category = "minecraft_phone",
                Keywords = "手机,微信,我的世界,主动消息",
                Weight = 1
            });
        }
        catch (Exception ex) { App.WriteLog("MaidBridge.PhoneMemory -> " + ex.Message); }

        // 按设置把这条消息呈现出来：phone = 手机窗口，main = 跳主窗口（60 秒内只呈一次）。
        _ = RunSafe("RevealPhone", RevealDisplayAsync);
    }

    /// <summary>已经因为「戴了手机」加过通讯录的角色，避免每 5 秒一帧 state 都去查一遍。</summary>
    private readonly ConcurrentDictionary<string, byte> _phoneContacts = new(StringComparer.Ordinal);

    /// <summary>上次写摘要时的数目：state 每 5 秒一帧，只在数目变化时写一行，不刷屏。</summary>
    private int _lastLoggedMaidCount = -1;
    private int _lastLoggedPhoneCount = -1;

    /// <summary>
    /// state 帧里的女仆名册摘要：通讯录、认主人、强制加载全靠它。
    /// 名册是不是空的以前只能靠猜，现在游戏日志和这份日志一对照，断点在哪一环当场可见。
    /// </summary>
    private void LogStateMaidSummary(JsonArray maids)
    {
        var total = 0;
        var phoneCount = 0;
        foreach (var item in maids.OfType<JsonObject>())
        {
            total++;
            if (BridgeJson.Bool(item, "phone")) phoneCount++;
        }
        if (total == _lastLoggedMaidCount && phoneCount == _lastLoggedPhoneCount) return;
        _lastLoggedMaidCount = total;
        _lastLoggedPhoneCount = phoneCount;
        App.WriteLog($"MaidBridge: state 女仆数={total}，戴手机={phoneCount}");
        Diag($"state 女仆数={total}，戴手机={phoneCount}");
    }

    /// <summary>模组在 state 帧里报 phone=true 的女仆，把她绑定的角色自动加进微信通讯录。</summary>
    private async Task SyncPhoneContactsAsync(MaidSession session, MaidBridgeWorldBinding world, JsonArray maids)
    {
        foreach (var item in maids.OfType<JsonObject>())
        {
            if (!BridgeJson.Bool(item, "phone")) continue;
            var maidId = BridgeJson.Str(item, "id");
            if (maidId.Length == 0) continue;
            var maidName = CleanName(BridgeJson.Str(item, "name"));

            // 戴了手机就当作授权：先找已绑定，没有就当场按名匹配或新建角色，保证她进通讯录。
            var charId = await ResolvePhoneCharacterAsync(session, world, maidId, maidName);
            if (charId.Length == 0)
            {
                App.WriteLog($"MaidBridge 戴手机的女仆 {maidName} 无法解析到角色，跳过通讯录");
                Diag($"通讯录失败: 戴手机女仆 {maidName}({maidId}) 解析不到角色");
                continue;
            }
            if (!_phoneContacts.TryAdd(charId, 0)) continue; // 同一角色只查一次
            try
            {
                var (_, _, added) = await _wechat.AddContactAsync(charId);
                Diag($"通讯录: {CharacterName(charId)}({charId}) 添加结果 added={added}");
                if (added) App.WriteLog("MaidBridge 戴手机的女仆已进通讯录：" + CharacterName(charId));
            }
            catch (Exception ex)
            {
                Diag($"通讯录异常: {charId} -> {ex.Message}");
                App.WriteLog("MaidBridge.PhoneContact -> " + ex.Message);
            }
        }
    }

    /// <summary>把「戴手机的女仆」解析到 WAB 角色：已绑定 → 世界绑定表 → 按名匹配 → 当场新建（封顶 64）。</summary>
    private async Task<string> ResolvePhoneCharacterAsync(MaidSession session, MaidBridgeWorldBinding world, string maidId, string maidName)
    {
        if (session.MaidCharacters.TryGetValue(maidId, out var c1) && c1.Length > 0) return c1;
        string? knownCharId = null;
        lock (_bindingsGate)
        {
            if (world.Maids.TryGetValue(maidId, out var known) && !string.IsNullOrEmpty(known.CharacterId))
                knownCharId = known.CharacterId;
        }
        if (knownCharId is not null && _engine.Roster.ContainsKey(knownCharId)) return knownCharId;
        if (maidName.Length > 0)
        {
            var match = _engine.Roster.Values.FirstOrDefault(c =>
                string.Equals(CleanName(c.Profile.Name), maidName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match.Profile.Id;
        }
        if (session.MaidCharacters.Count >= 64) return "";
        try
        {
            var created = _characters.CreateDefault(maidName.Length > 0 ? maidName : "女仆", "女", "温柔可爱");
            if (await _characters.AddAsync(created))
            {
                lock (_bindingsGate) world.Maids[maidId] = new MaidBridgeMaidBinding
                {
                    CharacterId = created.Profile.Id,
                    Name = maidName.Length > 0 ? maidName : "女仆",
                    LastSeen = DateTime.UtcNow
                };
                session.MaidCharacters[maidId] = created.Profile.Id;
                await SaveBindingsLockedAsync();
                App.WriteLog($"MaidBridge 为戴手机的女仆 {maidName} 新建角色 {created.Profile.Id} 并加进通讯录");
                return created.Profile.Id;
            }
        }
        catch (Exception ex) { App.WriteLog("MaidBridge.PhoneBind -> " + ex.Message); }
        return "";
    }

    /// <summary>
    /// 双端互通的另一半：WAB 主对话 / 假微信里用户说的话与 AI 的回复，同步到游戏里。
    /// 只发 chat_reply 帧——模组侧只显示气泡与语音，不会回传 chat 帧，天然不形成
    /// WAB → MC → WAB 回环。真正的指令（过去、挖矿、抱抱等）仍由 ChatEngine 的
    /// maid_* 工具闭环在游戏里执行，这里不重复下发命令，免得一个动作做两遍。
    /// </summary>
    public async Task NotifyWabDialogAsync(string charId, string userText, string aiReply)
    {
        var session = CurrentSession;
        if (session is null || !session.SaveActive || charId.Length == 0) return;
        var maidId = "";
        foreach (var (id, cid) in session.MaidCharacters)
        {
            if (!string.Equals(cid, charId, StringComparison.Ordinal)) continue;
            maidId = id;
            break;
        }
        if (maidId.Length == 0) return;   // 这个角色没绑到游戏里的女仆，不用同步
        var reply = CleanPhoneText(aiReply);
        if (reply.Length == 0) return;
        try
        {
            await session.SendAsync(new JsonObject
            {
                ["type"] = "chat_reply",
                ["maid"] = maidId,
                ["character"] = charId,
                ["ok"] = true,
                ["text"] = reply,
                ["userText"] = CleanPhoneText(userText),
                // 来源标记：以后模组想区分「游戏里主人说的」和「WAB 里说的」时有据可依
                ["origin"] = "wab_ui"
            });
        }
        catch (Exception ex)
        {
            App.WriteLog("MaidBridge.NotifyWabDialog -> " + ex.Message);
        }
    }

    /// <summary>
    /// 游戏内手机前端的后端：模组发来 phone_request，这里调 WAB 自己的微信/消息/AI 服务，
    /// 再回一帧 phone_reply。游戏侧不存任何业务逻辑，只把 JSON 渲染成界面。
    /// </summary>
    private async Task OnPhoneRequestAsync(MaidSession session, JsonObject msg)
    {
        var id = BridgeJson.Str(msg, "id");
        var action = BridgeJson.Str(msg, "action").ToLowerInvariant();
        var p = msg["params"] as JsonObject ?? new JsonObject();
        var data = new JsonObject();
        var code = "";
        var error = "";

        try
        {
            switch (action)
            {
                case "whoami":
                {
                    var charId = ActivePhoneCharacter(session);
                    data["characterId"] = charId;
                    data["characterName"] = CharacterName(charId);
                    break;
                }
                case "contacts":
                {
                    var list = await _wechat.ListAsync();
                    var arr = new JsonArray();
                    foreach (var c in list)
                    {
                        arr.Add(new JsonObject
                        {
                            ["characterId"] = c.CharacterId ?? "",
                            ["name"] = CharacterName(c.CharacterId ?? ""),
                            ["lastMessage"] = c.LastMessage ?? "",
                            ["unread"] = c.Unread,
                            ["lastMessageAt"] = c.LastMessageAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                        });
                    }
                    data["contacts"] = arr;
                    break;
                }
                case "messages":
                {
                    var charId = PickPhoneCharacter(session, BridgeJson.Str(p, "characterId"));
                    if (charId.Length == 0)
                    {
                        code = "no_character";
                        error = "还没选定聊天对象。";
                        break;
                    }
                    var limit = Math.Clamp(BridgeJson.Int(p, "limit", 50), 1, 50);
                    var all = await _phoneMessages.ForAsync(charId);
                    var arr = new JsonArray();
                    foreach (var m in all.Skip(Math.Max(0, all.Count - limit)))
                    {
                        arr.Add(new JsonObject
                        {
                            ["id"] = m.Id,
                            ["text"] = m.Text,
                            ["isUser"] = m.IsUser,
                            ["senderName"] = m.SenderName,
                            ["at"] = m.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                        });
                    }
                    data["messages"] = arr;
                    data["characterId"] = charId;
                    data["characterName"] = CharacterName(charId);
                    await _wechat.MarkReadAsync(charId);
                    break;
                }
                case "send":
                {
                    var charId = PickPhoneCharacter(session, BridgeJson.Str(p, "characterId"));
                    var text = BridgeJson.Str(p, "text").Replace('\r', ' ').Replace('\n', ' ').Trim();
                    if (charId.Length == 0 || text.Length == 0)
                    {
                        code = "bad_request";
                        error = "没有聊天对象，或者消息是空的。";
                        break;
                    }
                    if (text.Length > 500) text = text[..500];
                    var reply = await _chat.Send(charId, text);
                    var shown = CleanPhoneText(reply);
                    await _phoneMessages.PublishAsync(charId, text, "我", isUser: true);
                    await _phoneMessages.PublishAsync(charId, shown, CharacterName(charId), isUser: false);
                    await _wechat.TouchLastMessageAsync(charId, text.Length > 20 ? text[..20] : text);
                    data["reply"] = shown;
                    data["characterId"] = charId;
                    data["message"] = "已发送";
                    break;
                }
                case "add_friend":
                {
                    var raw = BridgeJson.Str(p, "nameOrId");
                    if (raw.Length == 0)
                    {
                        code = "bad_request";
                        error = "先写出要加的角色名或 Id。";
                        break;
                    }
                    var (ok, message, added) = await _wechat.AddContactAsync(raw);
                    if (!ok) { code = "add_failed"; error = message; break; }
                    data["added"] = added;
                    data["message"] = message;
                    break;
                }
                case "mark_read":
                {
                    var charId = PickPhoneCharacter(session, BridgeJson.Str(p, "characterId"));
                    if (charId.Length > 0) await _wechat.MarkReadAsync(charId);
                    data["message"] = "已读";
                    break;
                }
                case "remove_friend":
                {
                    var charId = BridgeJson.Str(p, "characterId");
                    var (ok, message) = await _wechat.RemoveAsync(charId);
                    if (!ok) { code = "remove_failed"; error = message; break; }
                    data["message"] = message;
                    break;
                }
                default:
                    code = "bad_action";
                    error = "这个手机动作还没实现：" + action;
                    break;
            }
        }
        catch (Exception ex)
        {
            code = "exception";
            error = ex.Message;
            App.WriteLog("MaidBridge.PhoneRequest(" + action + ") -> " + ex.Message);
        }

        try
        {
            await session.SendAsync(new JsonObject
            {
                ["type"] = "phone_reply",
                ["id"] = id,
                ["ok"] = error.Length == 0,
                ["action"] = action,
                ["code"] = code,
                ["error"] = error,
                ["data"] = data
            });
        }
        catch (Exception ex)
        {
            App.WriteLog("MaidBridge.PhoneReply -> " + ex.Message);
        }
    }

    /// <summary>手机默认聊天对象：优先这个会话里绑着角色的女仆，其次 WAB 当前角色。</summary>
    private string ActivePhoneCharacter(MaidSession session) =>
        session.MaidCharacters.Values.FirstOrDefault() ?? _engine.State.CharacterId ?? "";

    private string PickPhoneCharacter(MaidSession session, string wanted) =>
        wanted.Length > 0 ? wanted : ActivePhoneCharacter(session);

    private string CharacterName(string charId) =>
        charId.Length > 0 && _engine.Roster.TryGetValue(charId, out var ch) ? ch.Profile.Name : charId;

    /// <summary>压成一行、去掉表情标记与前导语，太长就截断。</summary>
    private static string CleanPhoneText(string raw)
    {
        var s = (raw ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        s = Regex.Replace(s, @"\[表情:[^\]]*\]", "");
        s = Regex.Replace(s, @"\s{2,}", " ").Trim();
        // 有些模型爱加「（用手机发的消息）」这类说明，去掉最外层引号即可，不强行改写内容。
        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '「' && s[^1] == '」')))
            s = s[1..^1].Trim();
        return s.Length > 200 ? s[..200] : s;
    }
}
