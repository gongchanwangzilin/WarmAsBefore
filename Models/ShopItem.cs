namespace WarmAsBefore.Models;

/// <summary>商品条目（美了么商店）。</summary>
public sealed class ShopItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";       // 品类
    public string Desc { get; set; } = "";
    public int Price { get; set; }
    public string Emoji { get; set; } = "🎁";
    public bool IsTicket { get; set; }               // 场景券
    public string TicketScene { get; set; } = "";    // 券解锁的场景名
    public string TicketLocation { get; set; } = ""; // 券场景归属地点（地图已有地点）
    /// <summary>已购买次数（持久化）。</summary>
    public int Owned { get; set; }
    /// <summary>是否 AI 实时生成。</summary>
    public bool AiGenerated { get; set; }
    /// <summary>虚拟商家名（如「茶语茶馆」）。内置商品按品类自动分配，GitHub 同步商品用仓库字段。</summary>
    public string Merchant { get; set; } = "";
    /// <summary>本地缓存图片路径（null/空 = 无图，UI 用 Emoji 兜底）。GitHub 同步时下载到本地。</summary>
    public string ImagePath { get; set; } = "";
    /// <summary>图片来源标记：github / builtin / ai。</summary>
    public string Source { get; set; } = "builtin";
    public bool HasImage => !string.IsNullOrEmpty(ImagePath);
    public bool Bought => Owned > 0;
}

/// <summary>一局小游戏的战绩记录（存入商店钱包档案）。</summary>
public sealed class GameRecord
{
    public string Game { get; set; } = "";
    public bool Won { get; set; }
    public int Moves { get; set; }
    public string Note { get; set; } = "";
    public DateTime At { get; set; } = DateTime.Now;
}

/// <summary>
/// buff = 角色身上的状态标记（标记功能）。送礼/使用商品后挂到小雨身上，
/// 注入 AI 对话上下文影响后续言行；每聊一轮对话 TurnsLeft 减 1，归零自动移除。
/// </summary>
public sealed class CharacterBuff
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";          // 标记名，如「喝了奶茶」
    public string Emoji { get; set; } = "✨";
    public string Desc { get; set; } = "";          // 一句话描述（注入 AI 上下文用）
    public string Source { get; set; } = "送礼";     // 来源：送礼 / 使用
    public int TurnsLeft { get; set; } = 6;         // 剩余生效对话轮数
    public DateTime AppliedAt { get; set; } = DateTime.Now;
}