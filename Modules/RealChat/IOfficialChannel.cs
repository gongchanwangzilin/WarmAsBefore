namespace WarmAsBefore.Modules.RealChat;

/// <summary>一条来自真实聊天平台的消息。</summary>
public sealed record RealChatMessage
{
    public string Channel { get; init; } = "";
    public string SenderId { get; init; } = "";
    public string SenderName { get; init; } = "";
    public string Content { get; init; } = "";
    public DateTime At { get; init; } = DateTime.Now;
}

/// <summary>
/// 官方接入通道：QQ 官方机器人 / 微信公众号。
/// 官方方案仅支持单聊，不支持群聊；群聊需用户自行通过 MCP 解决。
/// </summary>
public interface IOfficialChannel
{
    string Name { get; }
    bool IsRunning { get; }
    string Status { get; }
    event Action<RealChatMessage>? MessageReceived;
    Task StartAsync();
    Task StopAsync();
    Task SendAsync(RealChatMessage from, string text);
}
