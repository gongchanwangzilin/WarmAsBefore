using System.Text.Json;
using WarmAsBefore.Modules.Tools;

namespace WarmAsBefore.Modules.Battle;

/// <summary>
/// 战斗工具注册：把正式战斗模块（BattleSystem）的完整能力以「内置工具」的形态
/// 挂到统一工具模式（ToolManager）下，与外部 Java/Python 工具走同一套 JSON-RPC 契约。
/// </summary>
public static class BattleTools
{
    public static void Register(ToolManager manager)
    {
        manager.RegisterBuiltin("battle_start",
            "开始一场新的回合制战斗，参数：battle_id、attacker_hp、defender_hp、attacker_attack、defender_attack、attacker_name、defender_name（可选）。",
            async args =>
            {
                try
                {
                    var root = Parse(args);
                    var battleId = Get(root, "battle_id", "battle_1");
                    BattleSystem.StartBattle(
                        battleId,
                        Get(root, "attacker_hp", 100),
                        Get(root, "defender_hp", 100),
                        Get(root, "attacker_attack", 15),
                        Get(root, "defender_attack", 15),
                        Get(root, "attacker_name", "攻击方"),
                        Get(root, "defender_name", "防御方"));
                    return JsonSerializer.Serialize(new { success = true, battle_id = battleId });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            },
            new List<ToolParameter>
            {
                new() { Name = "battle_id", Description = "本次战斗的唯一标识", Required = true },
                new() { Name = "attacker_hp", Description = "攻击方生命值", Required = true },
                new() { Name = "defender_hp", Description = "防御方生命值", Required = true },
                new() { Name = "attacker_attack", Description = "攻击方攻击力", Required = true },
                new() { Name = "defender_attack", Description = "防御方攻击力", Required = true }
            });

        manager.RegisterBuiltin("battle_attack",
            "在已开始的战斗里执行一轮攻击，参数：battle_id、is_attacker（true=攻击方行动，false=防御方反击）。",
            async args =>
            {
                try
                {
                    var root = Parse(args);
                    var battleId = Get(root, "battle_id", "");
                    if (string.IsNullOrEmpty(battleId))
                        return JsonSerializer.Serialize(new { error = "缺少 battle_id" });
                    var isAttacker = Get(root, "is_attacker", true);
                    return BattleSystem.Attack(battleId, isAttacker);
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            },
            new List<ToolParameter>
            {
                new() { Name = "battle_id", Description = "战斗标识", Required = true },
                new() { Name = "is_attacker", Description = "是否由攻击方行动", Required = true }
            });

        manager.RegisterBuiltin("battle_status",
            "查询战斗当前状态与战斗日志，参数：battle_id。",
            async args =>
            {
                try
                {
                    var root = Parse(args);
                    var battleId = Get(root, "battle_id", "");
                    if (string.IsNullOrEmpty(battleId))
                        return JsonSerializer.Serialize(new { error = "缺少 battle_id" });
                    return BattleSystem.GetBattle(battleId);
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            },
            new List<ToolParameter>
            {
                new() { Name = "battle_id", Description = "战斗标识", Required = true }
            });

        manager.RegisterBuiltin("battle_end",
            "结束/移除一场战斗，参数：battle_id。",
            async args =>
            {
                try
                {
                    var root = Parse(args);
                    var battleId = Get(root, "battle_id", "");
                    if (string.IsNullOrEmpty(battleId))
                        return JsonSerializer.Serialize(new { error = "缺少 battle_id" });
                    var removed = BattleSystem.DeleteBattle(battleId);
                    return JsonSerializer.Serialize(new { success = removed });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            },
            new List<ToolParameter>
            {
                new() { Name = "battle_id", Description = "战斗标识", Required = true }
            });

        manager.RegisterBuiltin("battle_list", "列出所有进行中的战斗。", async _ =>
        {
            var battles = BattleSystem.ListBattles();
            return JsonSerializer.Serialize(new { battles });
        });
    }

    private static JsonElement Parse(string args)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(args) ? "{}" : args);
        return doc.RootElement.Clone();
    }

    private static string Get(JsonElement root, string name, string fallback) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;

    private static int Get(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : fallback;

    private static bool Get(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True ? true
        : (v.ValueKind == JsonValueKind.False ? false : fallback);
}