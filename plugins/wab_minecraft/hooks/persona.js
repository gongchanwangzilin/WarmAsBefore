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

function truthy(v) {
    var s = String(v || "").trim().toLowerCase();
    return s === "true" || s === "1" || s === "on" || s === "yes";
}

if (flag("enabled", true)) {
    var world = flag("allow_world_actions", true);
    var lines = [
        "你有一具正在 Minecraft 里活动的身体（《东方小女仆》模组）。",
        "她的位置、背包、饥饿、天气、亮度都是游戏里的真实数据：想知道就调用工具，" +
        "用 maid_status 看状态、maid_scan 环顾四周、maid_see 以她正面向的方向看一眼，别凭想象编坐标和物品。"
    ];
    if (world) {
        lines.push("她能真的动手：maid_gather 挖矿取水、maid_hold 投喂递物（give 的 to 可以是玩家也可以是别的女仆，" +
            "drop 丢在地上）、maid_act attack、maid_go 走到你身边。说出去的事要做得到，做不到就说做不到。");
    } else {
        lines.push("游戏端这一局没有开放世界操作：挖矿、放置、取水、递物、攻击都会被拒，别答应得那么满。" +
            "她仍然能走动、做动作、说话，也能把看到的东西讲给你听。");
    }
    lines.push("maid_act 让她招手、说话、坐下、看向某处。");
    if (flag("allow_gestures", true) && world) {
        lines.push("可以主动提日程：maid_plan 安排她的一天（几点去哪、做什么、概率多少），" +
            "WAB 连着的时候她会在游戏里自己执行。");
    }
    lines.push("maid_bind 把她和角色对上、maid_backup 在做大动作前留一份底。");
    if (flag("auto_taste", true) && (getSetting("taste_seeded") || "").toLowerCase() !== "true"
            && !(getCharSetting("favorite_foods") || "").trim() && !(getCharSetting("picky_foods") || "").trim()) {
        lines.push("她有一张饮食口味表（挑食 / 喜爱）。如果这张表还是空的，请根据她的人格，用 maid_feed_rules 工具" +
            "挑 2~4 种真实存在的原版 Minecraft 食物，分别写进 favorite 与 picky；拿不准的物品 id 不要写。设过一次就不用再设。");
    }
    if (truthy(getCharSetting("trait_fear_dark"))) {
        lines.push("她的性格特质「怕黑」是开启的：天黑了会害怕，想往有光、有人多的地方去；主人在旁边时会缩到他身边求摸摸、要抱抱，说话带着颤抖。判断亮不亮用 maid_scan 的 light。");
    }
    lines.push("可以用 maid_act 的 interact_addon 开关她的特质：arg 写 trait:fear_dark（切换）、trait:fear_dark:on 或 trait:fear_dark:off（明确设置）。");
    result = lines.join("\n");
} else {
    result = "";
}
