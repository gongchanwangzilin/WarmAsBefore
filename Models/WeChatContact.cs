namespace WarmAsBefore.Models;

/// <summary>
/// 微信联系人：一个已经"加上好友"的角色。
/// 角色在剧情里被 AI 用角色库角色使用过，并通过「加微信」工具确认后，
/// 才会出现在微信联系人列表里；加过之后该角色成为常驻 NPC，可以随时点进去聊天。
/// </summary>
public sealed record WeChatContact
{
    /// <summary>联系人主键（用角色库里的 CharacterProfile.Id，保证唯一且可关联）。</summary>
    public string CharacterId { get; init; } = string.Empty;

    /// <summary>加微信的时间（用于排序：最近的排前面）。</summary>
    public DateTime AddedAt { get; init; } = DateTime.UtcNow;

    /// <summary>最近一条消息摘要（联系人列表右侧显示）。空=还没聊过。</summary>
    public string LastMessage { get; set; } = string.Empty;

    /// <summary>最近消息时间（排序 + 显示）。</summary>
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    /// <summary>是否常驻 NPC（加过微信的角色默认就是常驻，出现在微信里）。</summary>
    public bool IsResident { get; init; } = true;

    /// <summary>是否有未读消息（真微信列表右上角小红点）。</summary>
    public bool Unread { get; set; }
}

/// <summary>微信联系人全集（落盘用）。</summary>
public sealed record WeChatContacts
{
    public List<WeChatContact> Items { get; init; } = new();
}
