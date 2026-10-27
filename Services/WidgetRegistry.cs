using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>一个主界面小组件的出厂说明：id 是持久化的键，别随手改。</summary>
public sealed record WidgetDefinition(
    string Id,
    string Title,
    string Description,
    string DefaultSlot,
    int DefaultOrder,
    bool DefaultVisible = true);

/// <summary>顶栏上可被 <see cref="UserSettings.TopBarButtons"/> 关掉的按键。</summary>
public sealed record TopBarButtonDefinition(string Id, string Label);

/// <summary>
/// 主界面小组件的登记处。
/// 设置里只存「用户改过的差异」，缺项、旧版本多出来的项、写坏的 slot 都在这里补齐/丢掉，
/// 这样界面永远拿到一份完整、可排序的清单，而用户手上的配置不会被一次升级冲掉。
/// </summary>
public static class WidgetRegistry
{
    /// <summary>可用小组件（顺序即默认顺序）。id 与 <see cref="WidgetPref"/> 注释里的约定一致。</summary>
    public static IReadOnlyList<WidgetDefinition> Widgets { get; } = new[]
    {
        new WidgetDefinition("clock", "时间", "当前时刻，让这一屏有在走的感觉。", "top", 10),
        new WidgetDefinition("date", "日期", "今天几号、星期几。", "top", 20),
        new WidgetDefinition("weather", "天气", "窗外此刻的天气。", "top", 30),
        new WidgetDefinition("location", "位置", "你和 TA 现在所在的地点。", "left", 40),
        new WidgetDefinition("affection", "好感", "好感度与当前等级。", "left", 50),
        new WidgetDefinition("trust", "信任", "信任度读数。", "left", 60),
        new WidgetDefinition("energy", "精力", "TA 今天还剩多少力气。", "left", 70),
        new WidgetDefinition("balance", "余额", "钱包里还剩多少。", "left", 80),
        new WidgetDefinition("brain", "心事", "大脑预判读出的一句「TA 现在在想什么」。", "left", 90),
        new WidgetDefinition("mood", "情绪", "此刻表情对应的情绪。", "left", 100, false),
    };

    /// <summary>顶栏按键（顺序即面板里的展示顺序）。</summary>
    public static IReadOnlyList<TopBarButtonDefinition> TopButtons { get; } = new[]
    {
        new TopBarButtonDefinition("save", "存档"),
        new TopBarButtonDefinition("menu", "菜单"),
        new TopBarButtonDefinition("phone", "手机"),
        new TopBarButtonDefinition("map", "地图"),
        new TopBarButtonDefinition("ai", "AI"),
        new TopBarButtonDefinition("hide", "收起"),
    };

    public static WidgetDefinition? Find(string? id)
        => Widgets.FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));

    public static int DefaultOrderOf(string id) => Find(id)?.DefaultOrder ?? int.MaxValue;

    /// <summary>只认 top / left；老版本的四角 slot 归到同侧，未知值回落到组件的默认栏。</summary>
    public static string NormalizeSlot(string? slot, string fallback)
    {
        var s = (slot ?? "").Trim().ToLowerInvariant();
        if (s == "top" || s.StartsWith("top-", StringComparison.Ordinal)) return "top";
        if (s == "left" || s.StartsWith("left", StringComparison.Ordinal)
            || s.StartsWith("bottom", StringComparison.Ordinal)) return "left";
        return fallback == "top" ? "top" : "left";
    }

    /// <summary>把用户配置合并成一份完整清单：缺项补出厂值、未知项忽略、重复项取第一条。</summary>
    public static List<WidgetPref> Merge(IEnumerable<WidgetPref>? saved)
    {
        var incoming = (saved ?? Enumerable.Empty<WidgetPref>())
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Id))
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var merged = new List<WidgetPref>(Widgets.Count);
        foreach (var def in Widgets)
        {
            if (incoming.TryGetValue(def.Id, out var p))
            {
                merged.Add(new WidgetPref
                {
                    Id = def.Id,
                    Visible = p.Visible,
                    Slot = NormalizeSlot(p.Slot, def.DefaultSlot),
                    // Order 0 是「没排过」而不是「排在最前」—— 旧配置全为 0，直接用会把默认顺序打乱。
                    Order = p.Order > 0 ? p.Order : def.DefaultOrder
                });
            }
            else
            {
                merged.Add(new WidgetPref
                {
                    Id = def.Id,
                    Visible = def.DefaultVisible,
                    Slot = def.DefaultSlot,
                    Order = def.DefaultOrder
                });
            }
        }
        return merged
            .OrderBy(p => p.Order)
            .ThenBy(p => DefaultOrderOf(p.Id))
            .ToList();
    }

    /// <summary>当前允许显示的顶栏按钮 id；空配置=全部。</summary>
    public static IReadOnlyList<string> AllowedTopButtons(string? csv)
    {
        var raw = (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (raw.Length == 0) return TopButtons.Select(b => b.Id).ToList();
        var set = new HashSet<string>(raw, StringComparer.OrdinalIgnoreCase);
        return TopButtons.Where(b => set.Contains(b.Id)).Select(b => b.Id).ToList();
    }

    /// <summary>把勾选结果写回 CSV：全选就写空（空=全部），免得以后新增按钮时默认被关掉。</summary>
    public static string BuildTopButtons(IEnumerable<string> visible)
    {
        var set = new HashSet<string>(visible, StringComparer.OrdinalIgnoreCase);
        var picked = TopButtons.Where(b => set.Contains(b.Id)).Select(b => b.Id).ToList();
        return picked.Count == TopButtons.Count ? "" : string.Join(",", picked);
    }
}
