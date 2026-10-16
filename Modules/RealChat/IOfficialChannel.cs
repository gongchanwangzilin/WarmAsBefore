namespace WarmAsBefore.Modules.RealChat;

/// <summary>一条来自真实聊天平台的消息。</summary>
public sealed record RealChatMessage
{
    public string Channel { get; init; } = "";
    public string SenderId { get; init; } = "";
    public string SenderName { get; init; } = "";
    public string Content { get; init; } = "";
    public DateTime At { get; init; } = DateTime.Now;

    /// <summary>是否来自群聊。群聊里默认不理（可在设置里打开），避免在群里刷屏。</summary>
    public bool IsGroup { get; init; }

    /// <summary>群会话 openid（仅群聊消息有），回复时要用它回同一个群。</summary>
    public string GroupId { get; init; } = "";
}

/// <summary>
/// 官方接入通道：QQ 官方机器人 / 微信公众号。
/// 官方方案支持单聊（C2C）与群聊（需要机器人有群权限）；群聊默认不回复，需在设置里显式打开。
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
