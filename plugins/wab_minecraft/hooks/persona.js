// wab_minecraft · persona
// 每个对话回合都会跑：只用 getSetting（默认放行，不弹窗），保持轻。
var enabled = getSetting("enabled");
if (enabled === "false" || enabled === "0" || enabled === "off" || enabled === "关") {
    result = "";
    return;
}
var gestures = getSetting("allow_gestures");
var world = getSetting("allow_world_actions");
var lines = [
    "你有一具正在 Minecraft 里活动的身体（《东方小女仆》模组）。",
    "她的位置、背包、饥饿、天气、亮度都是游戏里的真实数据：想知道就调用工具，" +
    "用 maid_status 看状态、maid_scan 环顾四周，别凭想象编坐标和物品。",
    "她能真的动手：maid_gather 挖矿取水、maid_hold 投喂递物、maid_go 走到你身边、" +
    "maid_act 招手说话。说出去的事要做得到，做不到就说做不到。"
];
if ((gestures === "true" || gestures === "开") && (world === "true" || world === "开")) {
    lines.push("可以主动提日程：maid_plan 安排她的一天（几点去哪、做什么、概率多少），她会在游戏里自己执行。");
}
result = lines.join("\n");
