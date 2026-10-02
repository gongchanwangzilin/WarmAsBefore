namespace WarmAsBefore.Modules.Battle;

/// <summary>
/// 回合制战斗数据模型（正式战斗模块，非插件）。
/// </summary>
public sealed class BattleData
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AttackerName { get; set; } = "攻击方";
    public string DefenderName { get; set; } = "防御方";
    public int AttackerHp { get; set; } = 100;
    public int DefenderHp { get; set; } = 100;
    public int AttackerAttack { get; set; } = 15;
    public int DefenderAttack { get; set; } = 15;
    public int Round { get; set; } = 1;
    public bool IsAttackerTurn { get; set; } = true;
    public string Status { get; set; } = "start"; // start, fighting, ended
    public List<string> BattleLog { get; set; } = new();
}