using WarmAsBefore.Models;

namespace WarmAsBefore.Modules.Market;

/// <summary>
/// 聊天界面送礼/使用面板的共享逻辑：从 ShopService 取已购商品，执行送礼/使用，
/// 返回小雨的回应文本（由聊天界面显示为消息）。供主界面/微信/QQ 等所有聊天入口复用。
/// </summary>
public sealed class GiftPanelService
{
    private readonly ShopService _shop;

    public GiftPanelService(ShopService shop) => _shop = shop;

    /// <summary>已购商品（Owned &gt; 0），按购买数降序。</summary>
    public IReadOnlyList<ShopItem> OwnedItems => _shop.OwnedItems;

    /// <summary>按名称在已购商品中查找（模糊匹配，供文本指令使用）。找不到返回 null。</summary>
    public ShopItem? FindOwnedByName(string name)
    {
        var items = OwnedItems;
        if (string.IsNullOrWhiteSpace(name)) return null;
        return items.FirstOrDefault(x => x.Name == name)
            ?? items.FirstOrDefault(x => x.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            ?? items.FirstOrDefault(x => name.Contains(x.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>送礼：消耗库存 1 件，返回小雨回应文本（失败返回提示语）。</summary>
    public async Task<string> GiftAsync(ShopItem item)
    {
        if (item is null) return "还没有选择要送的礼物～";
        var (ok, msg) = await _shop.GiftAsync(item);
        return ok ? msg : $"送礼失败：{msg}";
    }

    /// <summary>使用：消耗库存 1 件，返回小雨回应文本（失败返回提示语）。</summary>
    public async Task<string> UseAsync(ShopItem item)
    {
        if (item is null) return "还没有选择要使用的商品～";
        var (ok, msg) = await _shop.UseAsync(item);
        return ok ? msg : $"使用失败：{msg}";
    }
}
