using System.Text.Json.Serialization;

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
    // 图片可能只是「远程 URL」（仓库同步下载中）或一个被清掉的本地缓存路径：
    // 前者照样能显示，后者必须让 Emoji 兜底，否则格子会空着 —— 空比丑更难解释。
    private bool? _imageUsable;

    public bool HasImage => (_imageUsable ??= IsRemote(ImagePath) || File.Exists(ImagePath)) == true;

    /// <summary>
    /// 本地文件已经在盘上，能立刻画出像素。远程 URL 不算：它要先下载，
    /// 下载完成之前 Image 一个像素也画不出来 —— 这段时间由占位层顶着。
    /// </summary>
    public bool HasLocalImage => !IsRemote(ImagePath) && File.Exists(ImagePath);

    /// <summary>
    /// 占位层要不要出现：只要「不能立刻画出图」就出现（远程图在路上、本地文件被清掉、压根没图）。
    /// 它是垫底而不是替代 —— 图片一旦下载完就画在它上面，所以两者同时存在也没关系。
    /// </summary>
    public bool ShowPlaceholder => !HasLocalImage;

    /// <summary>
    /// 垫底用的首字：商品名的第一个字形（不是第一个 char —— emoji 与组合字按字形取，
    /// 取半个代理对会变成替换符）。名字全空时给个中性问号，不留空格子。
    /// </summary>
    public string Initial
    {
        get
        {
            var name = DisplayName;
            if (string.IsNullOrWhiteSpace(name)) return "?";
            var glyph = System.Globalization.StringInfo.GetNextTextElement(name.Trim());
            return string.IsNullOrEmpty(glyph) ? "?" : glyph;
        }
    }

    public bool Bought => Owned > 0;

    /// <summary>行标题：同步商品可以缺 name，空白行读不出是什么，兜一句说明。</summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "未命名商品" : Name;

    /// <summary>品类：同步商品可以缺 category，空白 chip / 空白行首尾相接会读成排版错误。</summary>
    [JsonIgnore]
    public string CategoryText => string.IsNullOrWhiteSpace(Category) ? "未分类" : Category;

    /// <summary>商家：内置商品一定有，仓库商品可以没填 —— 空字符串会留下一个 6px 的假间距。</summary>
    [JsonIgnore]
    public bool HasMerchant => !string.IsNullOrWhiteSpace(Merchant);

    /// <summary>价格：负数是坏数据（手改账本 / 坏 JSON），显示成「价格异常」而不是「-1 币」。</summary>
    [JsonIgnore]
    public string PriceText => Price < 0 ? "价格异常" : Price.ToString("N0") + " 币";

    private static bool IsRemote(string path) =>
        path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
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