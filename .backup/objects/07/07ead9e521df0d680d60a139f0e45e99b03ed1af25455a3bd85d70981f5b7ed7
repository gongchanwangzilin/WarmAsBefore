using System;
using System.Threading.Tasks;
using System.Text.Json;

namespace WarmAsBefore.Modules.Plugin
{
    /// <summary>
    /// 战斗模组插件示例
    /// 提供回合制战斗功能
    /// </summary>
    public static class BattlePlugin
    {
        /// <summary>
        /// 注册战斗相关命令到插件管理器
        /// </summary>
        public static void RegisterCommands(PluginManager manager)
        {
            // 开始战斗
            manager.Register("battle_start", async args =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(args);
                    var root = doc.RootElement;
                    
                    var battleId = root.TryGetProperty("battle_id", out var bid) 
                        ? bid.GetString() ?? "battle_1" 
                        : "battle_1";
                    
                    var attackerHp = root.TryGetProperty("attacker_hp", out var ahp) 
                        ? ahp.GetInt32() 
                        : 100;
                    
                    var defenderHp = root.TryGetProperty("defender_hp", out var dfp) 
                        ? dfp.GetInt32() 
                        : 100;
                    
                    var attackerAttack = root.TryGetProperty("attacker_attack", out var aatk) 
                        ? aatk.GetInt32() 
                        : 15;
                    
                    var defenderAttack = root.TryGetProperty("defender_attack", out var datk) 
                        ? datk.GetInt32() 
                        : 15;

                    BattleSystem.StartBattle(battleId, attackerHp, defenderHp, attackerAttack, defenderAttack);
                    return JsonSerializer.Serialize(new { success = true, battle_id = battleId });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            });

            // 执行攻击
            manager.Register("battle_attack", async args =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(args);
                    var root = doc.RootElement;
                    
                    var battleId = root.TryGetProperty("battle_id", out var bid) 
                        ? bid.GetString() ?? "" 
                        : "";
                    
                    var isAttacker = root.TryGetProperty("is_attacker", out var ia) 
                        ? ia.GetBoolean() 
                        : true;

                    if (string.IsNullOrEmpty(battleId))
                        return JsonSerializer.Serialize(new { error = "缺少 battle_id" });

                    var result = BattleSystem.Attack(battleId, isAttacker);
                    return result;
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            });

            // 获取战斗状态
            manager.Register("battle_status", async args =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(args);
                    var root = doc.RootElement;
                    
                    var battleId = root.TryGetProperty("battle_id", out var bid) 
                        ? bid.GetString() ?? "" 
                        : "";

                    if (string.IsNullOrEmpty(battleId))
                        return JsonSerializer.Serialize(new { error = "缺少 battle_id" });

                    return BattleSystem.GetBattle(battleId);
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            });

            // 结束战斗
            manager.Register("battle_end", async args =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(args);
                    var root = doc.RootElement;
                    
                    var battleId = root.TryGetProperty("battle_id", out var bid) 
                        ? bid.GetString() ?? "" 
                        : "";

                    if (string.IsNullOrEmpty(battleId))
                        return JsonSerializer.Serialize(new { error = "缺少 battle_id" });

                    var removed = BattleSystem.DeleteBattle(battleId);
                    return JsonSerializer.Serialize(new { success = removed });
                }
                catch (Exception ex)
                {
                    return JsonSerializer.Serialize(new { error = ex.Message });
                }
            });

            // 列出所有战斗
            manager.Register("battle_list", async args =>
            {
                var battles = BattleSystem.ListBattles();
                return JsonSerializer.Serialize(new { battles });
            });
        }
    }
}
