namespace WarmAsBefore.Modules.Plugin
{
    /// <summary>
    /// 战斗数据模型
    /// </summary>
    public class BattleData
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public int AttackerHp { get; set; } = 100;
        public int DefenderHp { get; set; } = 100;
        public int AttackerAttack { get; set; } = 15;
        public int DefenderAttack { get; set; } = 15;
        public int Round { get; set; } = 1;
        public bool IsAttackerTurn { get; set; } = true;
        public string Status { get; set; } = "start"; // start, fighting, ended
        public List<string> BattleLog { get; set; } = new();
    }
}
