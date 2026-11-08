// wab_minecraft · afterTurn
// 每个对话回合结束后跑：① 把该角色的喜爱/挑食推到游戏；② 检查饥饿讨食；③ 把该角色的特质开关同步到游戏。
var ctx = {};
try { ctx = JSON.parse(input) || {}; } catch (e) { ctx = {}; }
var charId = ctx && ctx.charId ? String(ctx.charId) : "";

function flag(key, dflt) {
    var raw = (getSetting(key) || "").trim().toLowerCase();
    if (raw === "") return dflt;
    if (raw === "true" || raw === "1" || raw === "on" || raw === "yes") return true;
    if (raw === "false" || raw === "0" || raw === "off" || raw === "no") return false;
    return dflt;
}
function num(key, dflt) {
    var v = parseFloat(getSetting(key));
    return isNaN(v) ? dflt : v;
}
function truthy(v) {
    var s = String(v || "").trim().toLowerCase();
    return s === "true" || s === "1" || s === "on" || s === "yes";
}
function splitIds(text) {
    var out = [];
    var parts = String(text || "").split(/[,，、;；\s]+/);
    for (var i = 0; i < parts.length; i++) {
        var id = parts[i].trim().toLowerCase();
        if (id.length > 0 && out.indexOf(id) < 0) out.push(id);
        if (out.length >= 32) break;
    }
    return out;
}
function readHunger(text) {
    if (!text) return null;
    var o;
    try { o = JSON.parse(text); } catch (e) { return null; }
    if (!o || typeof o !== "object") return null;
    if (typeof o.hunger === "number") return o.hunger;
    if (o.maid && typeof o.maid.hunger === "number") return o.maid.hunger;
    if (o.maid && typeof o.maid.food === "number") return o.maid.food;
    if (o.data && typeof o.data.hunger === "number") return o.data.hunger;
    return null;
}

// ① 角色级喜爱/挑食 → feed_rules（内容变了才推一次；sig 按角色存）
try {
    if (charId) {
        var favText = getCharSetting("favorite_foods") || "";
        var pickyText = getCharSetting("picky_foods") || "";
        var sig = favText.trim() + "|" + pickyText.trim();
        if (sig !== "|" && getCharSetting("taste_sig") !== sig) {
            var args = {};
            var fav = splitIds(favText);
            var picky = splitIds(pickyText);
            if (fav.length > 0) args.favorite = fav;
            if (picky.length > 0) args.picky = picky;
            if (args.favorite || args.picky) {
                var r = callTool("maid_feed_rules", JSON.stringify(args));
                if (r && String(r).indexOf("\"error\"") < 0) setCharSetting("taste_sig", sig);
            }
        }
    }
} catch (e) { log("afterTurn.taste -> " + e); }

// ② 饿了讨食：每个回合看一次，够饿且过了冷却就让她说一句（阈值/方向用全局设置）。
try {
    if (flag("hunger_beg", true) && flag("allow_gestures", true)) {
        var st = callTool("maid_status", "{}");
        var hunger = readHunger(st);
        if (hunger !== null) {
            var threshold = num("hunger_threshold", 14);
            var higher = flag("hunger_higher_is_hungry", true);
            var hungry = higher ? (hunger >= threshold) : (hunger <= threshold);
            if (hungry) {
                var nowMs = (typeof Date !== "undefined" && Date.now) ? Date.now() : 0;
                var last = num("hunger_last_beg", 0);
                if (nowMs === 0 || nowMs - last >= 180000) {
                    callTool("maid_act", JSON.stringify({ action: "say", text: "我有点饿了……能给我点吃的吗？" }));
                    if (nowMs > 0) setSetting("hunger_last_beg", String(nowMs));
                }
            }
        }
    }
} catch (e) { log("afterTurn.hunger -> " + e); }

// ③ 角色特质「怕黑」→ 游戏开关（变化才推；sig 按角色存）。
try {
    if (charId) {
        var fear = truthy(getCharSetting("trait_fear_dark")) ? "on" : "off";
        if (getCharSetting("trait_sig") !== fear) {
            callTool("maid_act", JSON.stringify({ action: "interact_addon", arg: "trait:fear_dark:" + fear }));
            setCharSetting("trait_sig", fear);
        }
    }
} catch (e) { log("afterTurn.trait -> " + e); }

result = null;
