using System.Text.RegularExpressions;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Modules.Market;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.RealChat;

/// <summary>
/// 官方接入桥：根据设置启停 QQ / 微信通道，
/// 收到真实消息后交给 AI（ChatEngine）生成回复并回发。
/// 支持文本指令：「送礼 商品名」「使用 商品名」直接消耗商店库存。
/// 官方方案仅支持单聊；群聊需用户自行接入 MCP 服务器。
/// </summary>
public sealed class OfficialChatBridge
{
    private readonly SettingsManager _settings;
    private readonly GameEngine _engine;
    private readonly ChatEngine _chat;
    private readonly NotificationService _notify;
    private readonly GiftPanelService _gifts;
    private readonly QqBotChannel _qq = new();
    private readonly WeChatOfficialChannel _wechat = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string _status = "未启用";

    public string Status => _status;
    public event Action<string>? StatusChanged;
    public event Action<RealChatMessage>? Incoming;
    public event Action<RealChatMessage, string>? Replied;

    public OfficialChatBridge(SettingsManager settings, GameEngine engine, ChatEngine chat,
        NotificationService notify, GiftPanelService gifts)
    {
        _settings = settings;
        _engine = engine;
        _chat = chat;
        _notify = notify;
        _gifts = gifts;
        _qq.MessageReceived += OnMessage;
        _wechat.MessageReceived += OnMessage;
        _settings.Applied += Apply;
    }

    public IOfficialChannel[] Channels => new IOfficialChannel[] { _qq, _wechat };

    public void Apply()
    {
        var s = _settings.Current;
        _qq.Configure(s.QqAppId, s.QqAppSecret);
        _wechat.Configure(s.WechatAppId, s.WechatAppSecret, s.WechatToken, s.WechatPort);
        var qqOn = s.QqBotEnabled && !string.IsNullOrWhiteSpace(s.QqAppId) && !string.IsNullOrWhiteSpace(s.QqAppSecret);
        var wxOn = s.WechatEnabled && !string.IsNullOrWhiteSpace(s.WechatAppId)
                   && !string.IsNullOrWhiteSpace(s.WechatAppSecret) && !string.IsNullOrWhiteSpace(s.WechatToken);
        _ = UpdateChannelsAsync(qqOn, wxOn);
    }

    public async Task<string> TestAsync()
    {
        var s = _settings.Current;
        _qq.Configure(s.QqAppId, s.QqAppSecret);
        _wechat.Configure(s.WechatAppId, s.WechatAppSecret, s.WechatToken, s.WechatPort);
        var qq = string.IsNullOrWhiteSpace(s.QqAppId) && string.IsNullOrWhiteSpace(s.QqAppSecret)
            ? "QQ：未配置"
            : "QQ：" + await _qq.TestAsync();
        var wx = string.IsNullOrWhiteSpace(s.WechatAppId) && string.IsNullOrWhiteSpace(s.WechatAppSecret)
            ? "微信：未配置"
            : "微信：" + await _wechat.TestAsync();
        return qq + "\n" + wx;
    }

    private async Task UpdateChannelsAsync(bool qqOn, bool wxOn)
    {
        try
        {
            if (qqOn && !_qq.IsRunning) await _qq.StartAsync();
            else if (!qqOn && _qq.IsRunning) await _qq.StopAsync();
            if (wxOn && !_wechat.IsRunning) await _wechat.StartAsync();
            else if (!wxOn && _wechat.IsRunning) await _wechat.StopAsync();
            RefreshStatus();
        }
        catch (Exception ex)
        {
            App.WriteLog("OfficialChatBridge.Update -> " + ex);
        }
    }

    private void RefreshStatus()
    {
        var parts = new List<string>();
        if (_qq.IsRunning) parts.Add("QQ 在线");
        else if (!string.IsNullOrWhiteSpace(_qq.Status) && !_qq.Status.Contains("未配置"))
            parts.Add("QQ " + _qq.Status);
        if (_wechat.IsRunning) parts.Add("微信在线");
        else if (!string.IsNullOrWhiteSpace(_wechat.Status) && !_wechat.Status.Contains("未配置"))
            parts.Add("微信 " + _wechat.Status);
        _status = parts.Count == 0 ? "未启用" : string.Join("，", parts);
        StatusChanged?.Invoke(_status);
    }

    private async void OnMessage(RealChatMessage msg)
    {
        try
        {
            await _gate.WaitAsync();

            // 群聊默认不理：在群里刷屏是最容易被讨厌的行为，需在设置里显式打开。
            if (msg.IsGroup && !_settings.Current.QqReplyGroup)
            {
                App.WriteLog($"OfficialChatBridge: 忽略群消息（未开启群聊回复）：{msg.SenderName} {msg.Content}");
                return;
            }

            // 识别「用户本人」：配置了 openid 就按它认人，之后角色可以用昵称称呼他
            RememberUserIfKnown(msg);

            Incoming?.Invoke(msg);
            var charId = ResolveCharacter();

            // 认出本人时告诉他「这是谁在跟我说话」——否则角色只会看到一个陌生 openid 的名字
            var s0 = _settings.Current;
            var incoming = msg.Content;
            if (!msg.IsGroup && IsKnownUser && !string.IsNullOrWhiteSpace(s0.QqUserName))
                incoming = $"[这是你认得的「{s0.QqUserName}」从手机 QQ 上发来的] {incoming}";

            // 文本指令：送礼/使用 直接消耗商店库存（不进 AI）
            var command = TryHandleCommand(msg.Content);
            var reply = command is not null
                ? command
                : await _chat.Send(charId, incoming);
            Replied?.Invoke(msg, reply);

            var channel = msg.Channel == _qq.Name ? (IOfficialChannel)_qq : _wechat;
            // #18：真实通道（QQ/微信官方）暂不支持直接发送表情图片（官方协议需要富媒体上传），
            // 回复里的 [表情:名字] 标记降级为文字，保证语义不丢失；本地界面侧仍按图片显示。
            await channel.SendAsync(msg, Services.StickerLibrary.ToPlainFallback(reply));

            var s = _settings.Current;
            if (s.NotificationsEnabled)
                _notify.Show("温暖如初", $"「{msg.Content.Trim()}」 → {reply}");
            // 对话已由 ChatEngine 存入记忆（回忆录可查）；日记由每日自动总结生成，无需逐条写入。
        }
        catch (Exception ex)
        {
            App.WriteLog("OfficialChatBridge.OnMessage -> " + ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 认出「用户本人」：设置里填了 openid 就按它比对；没填则记下最近一个私聊过的人，
    /// 让主动发消息有落点（否则角色想给你发消息也不知道发给谁）。
    /// </summary>
    private void RememberUserIfKnown(RealChatMessage msg)
    {
        if (msg.IsGroup) return;   // 群里的发言不能当成用户本人
        var configured = (_settings.Current.QqUserOpenId ?? "").Trim();
        if (configured.Length > 0)
        {
            IsKnownUser = string.Equals(configured, msg.SenderId, StringComparison.Ordinal);
            if (IsKnownUser) UserOpenId = msg.SenderId;
            return;
        }
        UserOpenId = msg.SenderId;
        UserName = msg.SenderName;
    }

    /// <summary>最近一次与机器人私聊的用户 openid（主动发消息的目标）。</summary>
    public string UserOpenId { get; private set; } = "";
    public string UserName { get; private set; } = "";
    /// <summary>上一次私聊是否确认为设置里指定的「用户本人」。</summary>
    public bool IsKnownUser { get; private set; }

    // ============ 主动外发（角色拿起手机发消息） ============

    /// <summary>等待用户确认的主动消息。</summary>
    public sealed record PendingOutboundMessage(string Id, string Text, DateTime At);

    private readonly List<PendingOutboundMessage> _pending = new();
    private readonly List<DateTime> _sentLog = new();

    /// <summary>待确认的主动消息（主界面给出确认入口）。</summary>
    public IReadOnlyList<PendingOutboundMessage> PendingOutbound
    {
        get { lock (_sentLog) return _pending.ToList(); }
    }

    /// <summary>
    /// 角色主动发消息到用户手机。
    /// 默认不直接发：先排队等用户确认（<see cref="QqProactiveRequireConfirm"/>），
    /// 并受每小时上限约束——这两道闸门是「AI 不能未经同意往外发消息」的保证。
    /// 返回给 AI 看的说明文本。
    /// </summary>
    public string QueueOutbound(string text)
    {
        var s = _settings.Current;
        var body = (text ?? "").Trim();
        if (body.Length == 0) return "要发的内容是空的。";
        if (!s.QqProactiveEnabled)
            return "现在没开「允许角色主动发消息」，所以这条没发出去。可以在设置里打开。";
        if (string.IsNullOrWhiteSpace(UserOpenId))
            return "还不知道该发给谁——让对方先在 QQ 上跟机器人说句话，就能认出他来了。";

        lock (_sentLog)
        {
            _sentLog.RemoveAll(t => t < DateTime.Now.AddHours(-1));
            if (_sentLog.Count >= Math.Max(1, s.QqProactivePerHour))
                return "这一小时已经发得够多了，先别打扰对方了。";
        }

        if (!s.QqProactiveRequireConfirm)
        {
            _ = SendNowAsync(body);
            return "消息已经发出去了。";
        }

        var item = new PendingOutboundMessage(Guid.NewGuid().ToString("N")[..8], body, DateTime.Now);
        lock (_sentLog) _pending.Add(item);
        OutboundPendingChanged?.Invoke();
        _notify.Show("有一条消息等着确认", body);
        return "这条消息要等用户确认后才会真的发出去（已排队）。你可以先告诉对方你发了什么。";
    }

    /// <summary>确认并发出某条待确认消息。</summary>
    public async Task<bool> ConfirmOutboundAsync(string id)
    {
        PendingOutboundMessage? item;
        lock (_sentLog)
        {
            item = _pending.FirstOrDefault(p => p.Id == id);
            if (item is not null) _pending.Remove(item);
        }
        OutboundPendingChanged?.Invoke();
        if (item is null) return false;
        return await SendNowAsync(item.Text);
    }

    public void CancelOutbound(string id)
    {
        lock (_sentLog) _pending.RemoveAll(p => p.Id == id);
        OutboundPendingChanged?.Invoke();
    }

    /// <summary>待确认列表变化（主界面据此显示/隐藏确认入口）。</summary>
    public event Action? OutboundPendingChanged;

    private async Task<bool> SendNowAsync(string text)
    {
        var target = UserOpenId;
        if (string.IsNullOrWhiteSpace(target)) return false;
        var ok = await _qq.SendPrivateAsync(target, Services.StickerLibrary.ToPlainFallback(text));
        lock (_sentLog)
        {
            if (ok) _sentLog.Add(DateTime.Now);
        }
        App.WriteLog($"OfficialChatBridge.Outbound -> {(ok ? "已发送" : "发送失败")}：{text}");
        if (!ok) _notify.Show("消息没发出去", "可能是超过 48 小时没互动，QQ 不允许机器人主动发起会话。");
        return ok;
    }

    /// <summary>识别「送礼 xxx」「使用 xxx」指令并执行，返回小雨的回应；非指令返回 null（走正常 AI）。</summary>
    private string? TryHandleCommand(string content)
    {
        var text = (content ?? "").Trim();
        if (text.Length == 0) return null;
        var giftMatch = Regex.Match(text, @"^送礼(?:物)?\s*(.+)$");
        if (giftMatch.Success)
            return HandleCommand(_gifts.GiftAsync, giftMatch.Groups[1].Value.Trim(), "送礼");
        var useMatch = Regex.Match(text, @"^使用\s*(.+)$");
        if (useMatch.Success)
            return HandleCommand(_gifts.UseAsync, useMatch.Groups[1].Value.Trim(), "使用");
        return null;
    }

    private string HandleCommand(Func<ShopItem, Task<string>> action, string name, string verb)
    {
        if (string.IsNullOrWhiteSpace(name)) return $"想{verb}什么呀？告诉我商品名，比如「{verb} 珍珠奶茶」～";
        var item = _gifts.FindOwnedByName(name);
        if (item is null) return $"还没有「{name}」这件已购商品～去美了么商店买一件吧";
        return action(item).GetAwaiter().GetResult();
    }

    private string ResolveCharacter()
    {
        // #16：QQ/微信机器人绑定过「陪聊角色（存档）」时优先用它；未绑定时回退到当前游戏角色 / 名册第一个 / 兜底。
        var bound = _settings.Current.QqBoundCharacterId;
        if (!string.IsNullOrEmpty(bound)) return bound;
        var id = _engine.State.CharacterId;
        if (!string.IsNullOrEmpty(id)) return id;
        if (_engine.Roster.Count > 0) return _engine.Roster.Values.First().Profile.Id;
        return "小雨";
    }
}
