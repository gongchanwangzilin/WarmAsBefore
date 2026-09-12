using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Modules.Battle;

/// <summary>
/// 把驱动的「战斗状态 JSON」解析成 UI 可直接绑定的模型。
/// 契约要求每次响应都携带 ended；缺失或解析失败一律视为 ended，
/// 防止游戏停留在战斗页面。
/// </summary>
public static class BattleStateParser
{
    /// <summary>从驱动响应中解析出战斗状态。</summary>
    public static BattleStateUi Parse(string json)
    {
        var ui = new BattleStateUi { Ended = true, StatusText = "战斗已结束 - 驱动异常" };
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var root = doc.RootElement;

            ui.BattleId = Str(root, "battle_id");
            ui.Winner = Str(root, "winner");
            ui.Round = Math.Max(1, Int(root, "round", 1));
            ui.PlayerTurn = root.TryGetProperty("player_turn", out var pt)
                ? pt.ValueKind == JsonValueKind.True : true;
            ui.Ended = root.TryGetProperty("ended", out var e)
                ? e.ValueKind == JsonValueKind.True : true;   // 反向结束数据缺失 → 视为已结束
            ui.StatusText = Str(root, "status_text")?.Length > 0 ? Str(root, "status_text") : "";

            if (root.TryGetProperty("log", out var log) && log.ValueKind == JsonValueKind.Array)
                ui.Log = log.EnumerateArray().Select(l => l.ValueKind == JsonValueKind.String ? l.GetString() ?? "" : l.ToString()).Where(s => s.Length > 0).ToList();

            ui.Players = ExpFromArray(root, "players", true);
            ui.Enemies = ExpFromArray(root, "enemies", false);

            // 未显式说明谁行动时：回合数守恒。
            if (ui.StatusText.Length == 0)
                ui.StatusText = ui.Ended
                    ? $"战斗结束{(ui.Winner.Length > 0 ? $" - {ui.Winner} 获胜" : "")}"
                    : (ui.PlayerTurn ? "选择你的角色行动" : "敌方回合...");
        }
        catch (Exception)
        {
            ui.Ended = true;
            ui.StatusText = "战斗已结束 - 驱动数据异常";
            ui.Log = new List<string> { "（驱动返回的数据无法解析，战斗中止）" };
        }
        return ui;
    }

    private static List<BattleCharacter> ExpFromArray(JsonElement root, string name, bool isPlayer)
    {
        var list = new List<BattleCharacter>();
        if (!root.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var it in arr.EnumerateArray())
        {
            var bp = new BattleCharacter
            {
                Id = Str(it, "id") is { Length: > 0 } idi ? idi : Guid.NewGuid().ToString(),
                Name = Str(it, "name") ?? "角色",
                MaxHp = Math.Max(1, Int(it, "max_hp", 100)),
                CurrentHp = Math.Max(0, Int(it, "hp", 100)),
                MaxMana = Math.Max(0, Int(it, "max_mp", 50)),
                CurrentMana = Math.Max(0, Int(it, "mp", 50)),
                Attack = Math.Max(0, Int(it, "attack", 15)),
                Defense = Math.Max(0, Int(it, "defense", 5)),
                IsPlayer = isPlayer,
                AvatarEmoji = Str(it, "emoji") ?? "👤",
                AvatarImage = Str(it, "image") ?? ""
            };

            if (it.TryGetProperty("skills", out var sk) && sk.ValueKind == JsonValueKind.Array)
            {
                bp.Skills = sk.EnumerateArray().Select(s => new SkillData
                {
                    Name = Str(s, "name") ?? "技能",
                    Description = Str(s, "description") ?? "",
                    Damage = Int(s, "damage", 10),
                    ManaCost = Int(s, "mp_cost", 0),
                    IsHeal = s.TryGetProperty("is_heal", out var h) && h.ValueKind == JsonValueKind.True,
                    HealAmount = Int(s, "heal", 0),
                    Type = Str(s, "type") ?? "attack"
                }).ToList();
            }
            list.Add(bp);
        }
        return list;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string name, int fallback) =>
        e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : fallback;
}

/// <summary>驱动状态 → UI 模型。</summary>
public sealed class BattleStateUi
{
    public string BattleId { get; set; } = "";
    public List<BattleCharacter> Players { get; set; } = new();
    public List<BattleCharacter> Enemies { get; set; } = new();
    public List<string> Log { get; set; } = new();
    public int Round { get; set; } = 1;
    public bool PlayerTurn { get; set; } = true;
    public bool Ended { get; set; }
    public string Winner { get; set; } = "";
    public string StatusText { get; set; } = "";
}