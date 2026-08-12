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
            Incoming?.Invoke(msg);
            var charId = ResolveCharacter();

            // 文本指令：送礼/使用 直接消耗商店库存（不进 AI）
            var command = TryHandleCommand(msg.Content);
            var reply = command is not null
                ? command
                : await _chat.Send(charId, msg.Content);
            Replied?.Invoke(msg, reply);

            var channel = msg.Channel == _qq.Name ? (IOfficialChannel)_qq : _wechat;
            await channel.SendAsync(msg, reply);

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
        var id = _engine.State.CharacterId;
        if (!string.IsNullOrEmpty(id)) return id;
        if (_engine.Roster.Count > 0) return _engine.Roster.Values.First().Profile.Id;
        return "小雨";
    }
}
