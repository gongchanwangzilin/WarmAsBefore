using System.Text.Json.Serialization;

namespace WarmAsBefore.Models;

/// <summary>
/// 日程条目的种类。AI 生成时只会输出这里的枚举名（Wear/Chat/...），
/// 所以提示词里可以直接把这些名字列给模型，解析时严格按枚举名匹配，失败就兜底模板。
/// </summary>
public enum PlanKind
{
    Wear,   // 穿衣 / 换装
    Chat,   // 聊天
    Work,   // 工作 / 学习
    Rest,   // 休息
    Move,   // 出行 / 活动
    Eat,    // 吃饭
    Sleep,  // 睡觉
    Mood,   // 情绪相关（发呆、写信、听歌…）
    Social, // 社交（见朋友、回消息…）
    Misc    // 其它杂事
}

/// <summary>
/// 日程里的一条待办：某天的 hour:minute 要做一件事。
/// Done / ResultAt 是「执行痕迹」——只由 Tick / MarkDone 在本地写，AI 不产出这两个字段。
/// </summary>
public sealed class PlanItem
{
    /// <summary>稳定 id（同一天内唯一），MarkDone 靠它定位条目。</summary>
    public string Id { get; set; } = "";

    /// <summary>小时 0-23。</summary>
    public int Hour { get; set; }

    /// <summary>分钟 0-59。</summary>
    public int Minute { get; set; }

    public PlanKind Kind { get; set; } = PlanKind.Misc;

    /// <summary>短标题（时间线一行显示的文字）。</summary>
    public string Title { get; set; } = "";

    /// <summary>补充说明（点开详情才看，可空）。</summary>
    public string Detail { get; set; } = "";

    /// <summary>到点是否已完成。Tick 按当前时间自动置位，不驱动真实聊天。</summary>
    public bool Done { get; set; }

    /// <summary>实际完成时间（UTC）。未完成时为空。</summary>
    public DateTime? ResultAt { get; set; }

    /// <summary>展示用的 HH:mm（Local），不落盘依赖。</summary>
    [JsonIgnore]
    public string TimeText => $"{Hour % 24:00}:{Minute % 60:00}";
}

/// <summary>
/// 某个角色某一天的完整日程。一天一份，生成后只在本机按表执行，不再实时调 AI。
/// </summary>
public sealed class DailyPlan
{
    /// <summary>日期键，Local 日期的 yyyy-MM-dd。跨天即视为过期，会重新生成。</summary>
    public string Date { get; set; } = "";

    /// <summary>角色 id（与 GameEngine.Roster 的 key 一致）。</summary>
    public string CharacterId { get; set; } = "";

    /// <summary>当天的条目，按时间升序保存。</summary>
    public List<PlanItem> Items { get; set; } = new();

    /// <summary>当天穿哪套衣服（AI 生成或模板给定）。</summary>
    public string Outfit { get; set; } = "";

    /// <summary>给这一天的整体备注（一句人话，可空）。</summary>
    public string Note { get; set; } = "";

    /// <summary>true=AI 生成；false=本地模板兜底。供监测页标注来源。</summary>
    public bool GeneratedByAi { get; set; }

    /// <summary>生成时间（UTC）。</summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 日程本：charId → (日期 → 当日日程)。
/// 每个角色一本，角色之间互不干扰；落盘按角色分文件（见 DailyPlanStore），
/// 内存里用这层字典做聚合缓存，方便 GetToday / Snapshot 一次拿到某角色的历史。
/// </summary>
public sealed class PlanBook
{
    public Dictionary<string, Dictionary<string, DailyPlan>> ByCharacter { get; set; } =
        new(StringComparer.Ordinal);
}
