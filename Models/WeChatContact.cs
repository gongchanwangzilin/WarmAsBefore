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

    /// <summary>加好友时自己写的备注（可空）。只在这一处落盘，联系人列表原样显示。</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>
    /// 未读条数：她在你没开这一格会话时发来一条就 +1，打开会话（<c>WeChatService.MarkReadAsync</c>）清零。
    /// 计数而不是布尔，是因为角标上要写得出「3」；调用方自己要发消息时同样清零（会话已经打开了）。
    /// </summary>
    public int UnreadCount { get; set; }

    /// <summary>
    /// 有没有未读。旧档里只存了这一个布尔（<c>Unread: true</c>），所以它必须留在模型里当兼容位：
    /// 读到 true 就折成 1 条；写盘时照 <see cref="UnreadCount"/> 回写，老版本读新档也不会看不见红点。
    /// 置 false 不清计数 —— 清零只有一条路（MarkReadAsync 直接写 UnreadCount），不在这里偷偷抹掉。
    /// </summary>
    public bool Unread
    {
        get => UnreadCount > 0;
        set { if (value) UnreadCount = Math.Max(UnreadCount, 1); }
    }
}

/// <summary>微信联系人全集（落盘用）。</summary>
public sealed record WeChatContacts
{
    public List<WeChatContact> Items { get; init; } = new();
}
