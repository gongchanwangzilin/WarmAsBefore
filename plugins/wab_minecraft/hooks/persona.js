// wab_minecraft · persona
// 每个对话回合都会跑：只读设置（settings 属于「自己家里」，默认放行、不弹窗、不占额外预算）。
var OFF = ["false", "0", "off", "no", "关"];
var ON = ["true", "1", "on", "yes", "开"];

// getSetting 在用户还没保存过设置时返回空串——空串不等于 false。
// 这里的默认值与 plugin.json 的 default、宿主的 MaidBridgeSettings.From 三处保持一致。
function flag(key, dflt) {
    var raw = (getSetting(key) || "").trim().toLowerCase();
    if (raw === "") return dflt;
    if (ON.indexOf(raw) >= 0) return true;
    if (OFF.indexOf(raw) >= 0) return false;
    return dflt;
}

if (flag("enabled", true)) {
    var lines = [
        "你有一具正在 Minecraft 里活动的身体（《东方小女仆》模组）。",
        "她的位置、背包、饥饿、天气、亮度都是游戏里的真实数据：想知道就调用工具，" +
        "用 maid_status 看状态、maid_scan 环顾四周，别凭想象编坐标和物品。",
        "她能真的动手：maid_gather 挖矿取水、maid_hold 投喂递物、maid_go 走到你身边、" +
        "maid_act 招手说话。说出去的事要做得到，做不到就说做不到。"
    ];
    if (flag("allow_gestures", true) && flag("allow_world_actions", true)) {
        lines.push("可以主动提日程：maid_plan 安排她的一天（几点去哪、做什么、概率多少），她会在游戏里自己执行。");
    }
    lines.push("maid_bind 把她和角色对上、maid_backup 在做大动作前留一份底。");
    result = lines.join("\n");
} else {
    result = "";
}
