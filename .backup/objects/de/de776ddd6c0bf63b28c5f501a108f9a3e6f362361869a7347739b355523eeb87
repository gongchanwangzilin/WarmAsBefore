using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WarmAsBefore.Modules.Plugin
{
    /// <summary>
    /// 简单的回合制战斗系统
    /// </summary>
    public static class BattleSystem
    {
        private static readonly Dictionary<string, BattleData> _battles = new();

        /// <summary>
        /// 开始新的战斗
        /// </summary>
        public static string StartBattle(string battleId, int attackerHp = 100, int defenderHp = 100, 
            int attackerAttack = 15, int defenderAttack = 15)
        {
            var battle = new BattleData
            {
                Id = battleId,
                AttackerHp = attackerHp,
                DefenderHp = defenderHp,
                AttackerAttack = attackerAttack,
                DefenderAttack = defenderAttack
            };
            _battles[battleId] = battle;
            battle.BattleLog.Add($"战斗开始！{battleId}");
            return battleId;
        }

        /// <summary>
        /// 执行一轮攻击
        /// </summary>
        public static string Attack(string battleId, bool isAttacker)
        {
            if (!_battles.TryGetValue(battleId, out var battle))
                return "错误：战斗不存在";

            if (battle.Status == "ended")
                return "错误：战斗已结束";

            int damage;
            string attackerName;
            string defenderName;

            if (isAttacker)
            {
                damage = Math.Max(1, battle.AttackerAttack - Random.Shared.Next(3));
                battle.DefenderHp = Math.Max(0, battle.DefenderHp - damage);
                attackerName = "攻击方";
                defenderName = "防御方";
                battle.BattleLog.Add($"{attackerName} 攻击 {defenderName}，造成 {damage} 点伤害！");
            }
            else
            {
                damage = Math.Max(1, battle.DefenderAttack - Random.Shared.Next(3));
                battle.AttackerHp = Math.Max(0, battle.AttackerHp - damage);
                attackerName = "防御方";
                defenderName = "攻击方";
                battle.BattleLog.Add($"{attackerName} 攻击 {defenderName}，造成 {damage} 点伤害！");
            }

            battle.IsAttackerTurn = !battle.IsAttackerTurn;
            battle.Round++;

            // 检查是否结束
            if (battle.AttackerHp <= 0 || battle.DefenderHp <= 0)
            {
                battle.Status = "ended";
                var winner = battle.AttackerHp > 0 ? "攻击方" : "防御方";
                battle.BattleLog.Add($"战斗结束！{winner}获胜！");
            }

            return SerializeBattle(battle);
        }

        /// <summary>
        /// 获取战斗状态
        /// </summary>
        public static string GetBattle(string battleId)
        {
            if (!_battles.TryGetValue(battleId, out var battle))
                return "错误：战斗不存在";
            return SerializeBattle(battle);
        }

        /// <summary>
        /// 删除战斗
        /// </summary>
        public static bool DeleteBattle(string battleId)
        {
            return _battles.Remove(battleId);
        }

        /// <summary>
        /// 列出所有战斗
        /// </summary>
        public static List<string> ListBattles()
        {
            return new List<string>(_battles.Keys);
        }

        private static string SerializeBattle(BattleData battle)
        {
            return $"{{\"id\":\"{battle.Id}\",\"attackerHp\":{battle.AttackerHp}," +
                   $"\"defenderHp\":{battle.DefenderHp},\"round\":{battle.Round}," +
                   $"\"isAttackerTurn\":{battle.IsAttackerTurn.ToString().ToLower()}," +
                   $"\"status\":\"{battle.Status}\"}}";
        }
    }
}
