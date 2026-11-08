// wab_minecraft · onToolResult
// 任何工具返回后跑：她（AI 或本插件）一旦用过 maid_feed_rules，就记下口味表已设，别再劝 AI 去设。
var ctx = {};
try { ctx = JSON.parse(input) || {}; } catch (e) { ctx = {}; }
try {
    if (ctx && ctx.tool === "maid_feed_rules") setSetting("taste_seeded", "true");
} catch (e) { log("onToolResult -> " + e); }
result = null;
