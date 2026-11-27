using System.Collections.Generic;

namespace WarmAsBefore.Modules.MinecraftBridge;

/// <summary>
/// maid_* 工具与模组方法名、方法所属通道的对照表。
///
/// 为什么要这张表：模组在握手时会报 <c>capabilities.methods</c>（dispatch 实际放行的方法名）。
/// 宿主只有知道「这个工具落到哪个方法」才能据此决定要不要把它放进提示词——
/// 否则旧模组上模型会看见一堆调用就回 <c>unknown_method</c> 的工具。
///
/// 值给 <c>null</c> 表示这个工具**不走 command 通道**（例如 <c>maid_feed_rules</c> 走的下行
/// <c>feed_rules</c> 帧），能力表里当然没有它，不能按方法名判死。
/// 表里没有登记的工具同样按「不走 command 通道」处理，宁可多给一个工具也不误收。
/// </summary>
internal static class MaidToolMethods
{
    /// <summary>工具名 → 模组方法名；<c>null</c> 表示不受 command 白名单约束。</summary>
    public static readonly IReadOnlyDictionary<string, string?> Map = new Dictionary<string, string?>
    {
        ["maid_status"] = "status",
        ["maid_scan"] = "scan",
        ["maid_see"] = "see",
        ["maid_go"] = "go",
        ["maid_gather"] = "gather",
        ["maid_hold"] = "hold",
        ["maid_act"] = "act",
        ["maid_hug"] = "hug",
        ["maid_plan"] = "plan",
        ["maid_bind"] = "bind",
        ["maid_backup"] = "backup",
        ["maid_feed_rules"] = null,
        ["maid_waypoint"] = "waypoint",
        ["maid_container"] = "container",
        ["maid_craft"] = "craft",
        ["maid_build"] = "build",
        ["maid_ride"] = "ride",
        ["maid_fight"] = "fight",
        ["maid_crew"] = "crew",
        ["maid_item"] = "item",
        ["maid_farm"] = "farm",
        ["maid_escape"] = "escape",
        ["maid_comfort"] = "comfort",
        ["maid_sneak_eat"] = "sneak_eat",
        ["maid_mode_switch"] = "mode_switch",
        ["maid_chat_turn"] = "chat_turn",
        ["maid_plan_flow"] = "plan_flow",
        ["maid_headpat"] = "headpat",
        ["maid_backup_copy"] = "backup_copy",
        ["maid_pos"] = "maid_pos",
        ["maid_collect"] = "maid_collect",
        ["maid_construct"] = "maid_construct",
        ["maid_sleep_consent"] = "sleep_consent",
        ["maid_self_eat"] = "self_eat",
        ["maid_fire_escape"] = "fire_escape",
        ["maid_tame_protect"] = "tame_protect",
    };
}
