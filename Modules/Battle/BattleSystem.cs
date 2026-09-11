using System.Collections.Concurrent;
using System.Text.Json;

namespace WarmAsBefore.Modules.Battle;

/// <summary>
/// 回合制战斗系统：内化为游戏正式模块（原插件系统的战斗能力迁移至此）。
/// 线程安全：战斗字典使用 ConcurrentDictionary。
/// </summary>
public static class BattleSystem
{
    private static readonly ConcurrentDictionary<string, BattleData> Battles = new();

    /// <summary>开始一场新的战斗。</summary>
    public static string StartBattle(string battleId, int attackerHp = 100, int defenderHp = 100,
        int attackerAttack = 15, int defenderAttack = 15,
        string attackerName = "攻击方", string defenderName = "防御方")
    {
        var battle = new BattleData
        {
            Id = battleId,
            AttackerName = attackerName,
            DefenderName = defenderName,
            AttackerHp = Math.Max(1, attackerHp),
            DefenderHp = Math.Max(1, defenderHp),
            AttackerAttack = Math.Max(1, attackerAttack),
            DefenderAttack = Math.Max(1, defenderAttack)
        };
        Battles[battleId] = battle;
        battle.BattleLog.Add($"战斗开始！{attackerName} ←→ {defenderName}");
        return battleId;
    }

    /// <summary>执行一轮攻击；isAttacker=true 表示攻击方行动。</summary>
    public static string Attack(string battleId, bool isAttacker)
    {
        if (!Battles.TryGetValue(battleId, out var battle))
            return JsonSerializer.Serialize(new { error = "战斗不存在" });

        if (battle.Status == "ended")
            return JsonSerializer.Serialize(new { error = "战斗已结束" });

        int damage;
        if (isAttacker)
        {
            damage = Math.Max(1, battle.AttackerAttack - Random.Shared.Next(3));
            battle.DefenderHp = Math.Max(0, battle.DefenderHp - damage);
            battle.BattleLog.Add($"{battle.AttackerName} 攻击 {battle.DefenderName}，造成 {damage} 点伤害！");
        }
        else
        {
            damage = Math.Max(1, battle.DefenderAttack - Random.Shared.Next(3));
            battle.AttackerHp = Math.Max(0, battle.AttackerHp - damage);
            battle.BattleLog.Add($"{battle.DefenderName} 攻击 {battle.AttackerName}，造成 {damage} 点伤害！");
        }

        battle.IsAttackerTurn = !battle.IsAttackerTurn;
        battle.Round++;

        if (battle.AttackerHp <= 0 || battle.DefenderHp <= 0)
        {
            battle.Status = "ended";
            var winner = battle.AttackerHp > 0 ? battle.AttackerName : battle.DefenderName;
            battle.BattleLog.Add($"战斗结束！{winner} 获胜！");
        }

        return SerializeBattle(battle);
    }

    /// <summary>获取战斗状态。返回完整 JSON（含战斗日志）。</summary>
    public static string GetBattle(string battleId)
    {
        if (!Battles.TryGetValue(battleId, out var battle))
            return JsonSerializer.Serialize(new { error = "战斗不存在" });
        return SerializeBattle(battle);
    }

    /// <summary>删除战斗。</summary>
    public static bool DeleteBattle(string battleId) => Battles.TryRemove(battleId, out _);

    /// <summary>列出所有进行中的战斗 id。</summary>
    public static List<string> ListBattles() => Battles.Keys.ToList();

    private static string SerializeBattle(BattleData b) => JsonSerializer.Serialize(new
    {
        id = b.Id,
        attackerName = b.AttackerName,
        defenderName = b.DefenderName,
        attackerHp = b.AttackerHp,
        defenderHp = b.DefenderHp,
        round = b.Round,
        isAttackerTurn = b.IsAttackerTurn,
        status = b.Status,
        log = b.BattleLog
    });
}