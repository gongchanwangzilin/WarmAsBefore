using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Modules.Battle;

/// <summary>
/// 内置回合制战斗驱动（进程内实现）。
/// 当没有外部 battle.json 时使用，保证战斗功能随时可用。
/// 与外部驱动同契约：每次响应都带 ended:true|false。
/// </summary>
public sealed class BuiltinBattleDriver : IBattleDriver
{
    private readonly BattleState _battle = new();
    private readonly string _playerName;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = false };

    public string Name => "builtin";

    public bool IsAlive => true;

    public BuiltinBattleDriver(BattleBootData? boot)
    {
        _playerName = boot?.Characters.FirstOrDefault()?.Name ?? "璃茉";
        var affection = boot?.Characters.FirstOrDefault()?.Affection ?? 0;

        _battle.Players.Add(new BattleCharacter
        {
            Name = _playerName,
            MaxHp = 120 + Math.Clamp(affection / 2, 0, 80),
            CurrentHp = 120 + Math.Clamp(affection / 2, 0, 80),
            MaxMana = 50,
            CurrentMana = 50,
            Attack = 18,
            Defense = 6,
            IsPlayer = true,
            AvatarEmoji = "🌸",
            AvatarImage = "battle/sakura.png"
        });
        foreach (var s in new[]
        {
            new SkillData { Name = "普通斩击", Description = "认真的挥击", Damage = 15, ManaCost = 0, Type = "attack" },
            new SkillData { Name = "花舞连击", Description = "高速三连斩", Damage = 30, ManaCost = 18, Type = "attack" },
            new SkillData { Name = "绽樱治愈", Description = "恢复友方生命", IsHeal = true, HealAmount = 40, ManaCost = 12, Type = "heal" }
        })
            _battle.Players[0].Skills.Add(s);

        _battle.Enemies.Add(new BattleCharacter
        {
            Name = "狼人",
            MaxHp = 150, CurrentHp = 150, MaxMana = 30, CurrentMana = 30,
            Attack = 16, Defense = 4, IsPlayer = false, AvatarEmoji = "🐺", AvatarImage = "battle/wolf.png"
        });
        _battle.Enemies[0].Skills.Add(new SkillData { Name = "利爪", Description = "撕裂攻击", Damage = 12, ManaCost = 0, Type = "attack" });
        _battle.Enemies.Add(new BattleCharacter
        {
            Name = "暗精灵",
            MaxHp = 110, CurrentHp = 110, MaxMana = 40, CurrentMana = 40,
            Attack = 14, Defense = 2, IsPlayer = false, AvatarEmoji = "🧝", AvatarImage = "battle/dark_elf.png"
        });
        _battle.Enemies[1].Skills.Add(new SkillData { Name = "暗影箭", Description = "远程暗影伤害", Damage = 16, ManaCost = 0, Type = "attack" });
    }

    public Task<string> CallAsync(string method, string paramsJson)
    {
        try
        {
            string stateJson;
            switch (method)
            {
                case "battle_start":
                    stateJson = ApplyStart();
                    break;
                case "battle_action":
                    stateJson = ApplyAction(paramsJson);
                    break;
                case "battle_advance":
                    stateJson = ApplyEnemyTurn();
                    break;
                case "battle_status":
                    stateJson = Serialize();
                    break;
                default:
                    stateJson = JsonSerializer.Serialize(new { error = $"未知方法 {method}", ended = true, winner = "" });
                    break;
            }
            return Task.FromResult(stateJson);
        }
        catch (Exception ex)
        {
            App.WriteLog("BuiltinBattleDriver.CallAsync -> " + ex);
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                error = ex.Message,
                ended = true,
                winner = ""
            }));
        }
    }

    private string ApplyStart()
    {
        if (_battle.Status != "waiting" && _battle.Status != "ended")
            return JsonSerializer.Serialize(new { error = "战斗已在进行中", ended = false });

        if (_battle.Status == "ended")
        {
            PlayerReset();
            EnemyReset();
            _battle.Log.Clear();
        }

        _battle.Status = "player_turn";
        _battle.CurrentRound = 1;
        AddLog($"战斗开始！{_playerName} ←→ 狼人 & 暗精灵");
        return Serialize();
    }

    private void PlayerReset()
    {
        foreach (var p in _battle.Players) { p.CurrentHp = p.MaxHp; p.CurrentMana = p.MaxMana; }
    }

    private void EnemyReset()
    {
        foreach (var e in _battle.Enemies) { e.CurrentHp = e.MaxHp; e.CurrentMana = e.MaxMana; }
    }

    private string ApplyAction(string paramsJson)
    {
        if (_battle.Status == "ended")
            return JsonSerializer.Serialize(new { error = "战斗已结束", ended = true, winner = _battle.Winner, round = _battle.CurrentRound });

        if (_battle.Status != "player_turn")
            return Serialize(); // 非玩家回合，忽略输入

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(paramsJson) ? "{}" : paramsJson);
            var root = doc.RootElement;
            var action = Str(root, "action") ?? "attack_normal";
            var skillIndex = Int(root, "skill_index", 0);
            var targetIndex = Int(root, "target_index", 0);

            var player = _battle.Players.FirstOrDefault(p => p.CurrentHp > 0);
            if (player is null)
            {
                _battle.Status = "ended";
                _battle.Winner = "敌方";
                AddLog("我方全灭 - 战斗失败");
                return Serialize();
            }

            var aliveEnemies = _battle.Enemies.Where(e => e.CurrentHp > 0).ToList();
            if (aliveEnemies.Count == 0)
            {
                _battle.Status = "ended";
                _battle.Winner = _playerName;
                AddLog("敌方全灭 - 战斗胜利");
                return Serialize();
            }

            var target = aliveEnemies[Math.Clamp(targetIndex, 0, aliveEnemies.Count - 1)];

            if (action == "skill")
            {
                var skill = player.Skills[Math.Clamp(skillIndex, 0, player.Skills.Count - 1)];
                if (player.CurrentMana < skill.ManaCost)
                {
                    AddLog($"{player.Name} 魔力不足，无法使用「{skill.Name}」！");
                    return Serialize();
                }
                player.CurrentMana = Math.Max(0, player.CurrentMana - skill.ManaCost);

                if (skill.IsHeal)
                {
                    var healed = player.CurrentHp > 0 ? player : _battle.Players.OrderBy(p => p.CurrentHp).First(p => p.CurrentHp > 0);
                    var amount = skill.HealAmount;
                    healed.CurrentHp = Math.Min(healed.MaxHp, healed.CurrentHp + amount);
                    AddLog($"{player.Name} 使用「{skill.Name}」，{healed.Name} 恢复 {amount} HP！");
                }
                else
                {
                    var damage = DamageOf(player.Attack + skill.Damage, target.Defense);
                    target.CurrentHp = Math.Max(0, target.CurrentHp - damage);
                    AddLog($"{player.Name} 使用「{skill.Name}」，对 {target.Name} 造成 {damage} 点伤害！");
                }
            }
            else
            {
                var damage = DamageOf(player.Attack, target.Defense);
                target.CurrentHp = Math.Max(0, target.CurrentHp - damage);
                AddLog($"{player.Name} 攻击 {target.Name}，造成 {damage} 点伤害！");
            }

            CheckEndAfterPlayer();
            return Serialize();
        }
        catch (Exception ex)
        {
            AddLog("操作无效：" + ex.Message);
            return Serialize();
        }
    }

    private string ApplyEnemyTurn()
    {
        if (_battle.Status == "ended")
            return JsonSerializer.Serialize(new { error = "战斗已结束", ended = true, winner = _battle.Winner, round = _battle.CurrentRound });

        if (_battle.Status == "player_turn")
            return Serialize(); // 已回到玩家回合，无需推进

        _battle.Status = "enemy_turn";
        WaitForBattleSync(900);

        foreach (var enemy in _battle.Enemies.Where(e => e.CurrentHp > 0).ToList())
        {
            var alive = _battle.Players.Where(p => p.CurrentHp > 0).ToList();
            if (alive.Count == 0) break;
            var target = alive[new Random().Next(alive.Count)];
            var damage = DamageOf(enemy.Attack, target.Defense);
            target.CurrentHp = Math.Max(0, target.CurrentHp - damage);
            AddLog($"{enemy.Name} 攻击 {target.Name}，造成 {damage} 点伤害！");
            CheckEndAfterEnemyTurn();
            if (_battle.Status == "ended") return Serialize();
        }

        if (_battle.Status != "ended")
        {
            _battle.Status = "player_turn";
            _battle.CurrentRound++;
            AddLog($"—— 第 {_battle.CurrentRound} 回合 ——");
        }
        return Serialize();
    }

    /// <summary>外部驱动在敌方回合常需要「思考时间」；内置驱动同样等一拍，保持节奏一致。</summary>
    private void WaitForBattleSync(int ms) => Thread.Sleep(ms);

    private void CheckEndAfterPlayer()
    {
        if (_battle.Players.Any(p => p.CurrentHp > 0) && _battle.Enemies.All(e => e.CurrentHp <= 0))
        {
            _battle.Status = "ended";
            _battle.Winner = _playerName;
            AddLog($"战斗胜利！{_playerName} 一行清扫了敌人！");
            return;
        }
        if (!_battle.Players.Any(p => p.CurrentHp > 0))
        {
            _battle.Status = "ended";
            _battle.Winner = "敌方";
            AddLog("战斗失败 - 我方全灭…");
            return;
        }
        _battle.Status = "enemy_turn";
    }

    private void CheckEndAfterEnemyTurn()
    {
        if (_battle.Players.All(p => p.CurrentHp <= 0) || (_battle.Players.Any(p => p.CurrentHp > 0) && _battle.Enemies.All(e => e.CurrentHp <= 0)))
        {
            _battle.Status = "ended";
            _battle.Winner = _battle.Players.Any(p => p.CurrentHp > 0) ? _playerName : "敌方";
            AddLog($"{_battle.Winner} 获得胜利！");
        }
    }

    private int DamageOf(int attack, int defense) => Math.Max(1, attack - defense) + Random.Shared.Next(3);

    private void AddLog(string message) =>
        _battle.Log.Add(new BattleLogEntry { Round = _battle.CurrentRound, Action = message });

    private string Serialize() => JsonSerializer.Serialize(ToContract(), _json);

    private object ToContract() => new
    {
        battle_id = _battle.Id,
        round = _battle.CurrentRound,
        player_turn = _battle.Status == "player_turn",
        status_text = _battle.Status switch
        {
            "ended" => $"战斗结束{(_battle.Winner.Length > 0 ? $" - {_battle.Winner} 获胜" : "")}",
            "player_turn" => "选择你的角色行动",
            _ => "敌方回合..."
        },
        ended = _battle.Status == "ended",
        winner = _battle.Winner,
        players = _battle.Players.Select(PlayerContract),
        enemies = _battle.Enemies.Select(PlayerContract),
        log = _battle.Log.Select(l => l.Action)
    };

    private object PlayerContract(BattleCharacter c) => new
    {
        id = c.Id,
        name = c.Name,
        max_hp = c.MaxHp,
        hp = c.CurrentHp,
        max_mp = c.MaxMana,
        mp = c.CurrentMana,
        attack = c.Attack,
        defense = c.Defense,
        emoji = c.AvatarEmoji,
        image = c.AvatarImage,
        skills = c.Skills.Select(s => new
        {
            name = s.Name,
            description = s.Description,
            damage = s.Damage,
            mp_cost = s.ManaCost,
            is_heal = s.IsHeal,
            heal = s.IsHeal ? s.HealAmount : 0,
            type = s.Type
        })
    };

    public void Dispose() { }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string name, int fallback) =>
        e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : fallback;
}