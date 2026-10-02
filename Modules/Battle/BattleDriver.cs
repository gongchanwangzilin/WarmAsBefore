using System.Text.Json;

namespace WarmAsBefore.Modules.Battle;

/// <summary>
/// 可替换战斗系统的驱动契约。
///
/// 应用（开局时）向驱动提供三样数据（见 <see cref="BattleBootData"/>）：
///   人物 / 上下文 / 日期；
/// 驱动负责演算战斗，且【每一个】响应都必须携带 ended （反向代表结束的数据），
/// 否则应用会判定驱动异常并按结束处理，避免游戏停留在战斗页面无法退出。
/// </summary>
public interface IBattleDriver : IDisposable
{
    /// <summary>驱动名（外部清单名 / 内置名）。</summary>
    string Name { get; }

    /// <summary>驱动子进程是否存活（内置驱动恒为 true）。</summary>
    bool IsAlive { get; }

    /// <summary>发起一次战斗调用（battle_start / battle_action / battle_advance）。
    /// 返回值必须为「战斗状态」JSON，且必须包含 ended:true|false。</summary>
    Task<string> CallAsync(string method, string paramsJson);
}

/// <summary>应用提供给战斗驱动的数据（battle_start 的 params.game）。</summary>
public sealed record BattleBootData
{
    /// <summary>人物：全部登场角色（含 id/名字/好感/信任/精力/情绪/所在地）。</summary>
    public List<BattleBootCharacter> Characters { get; init; } = new();

    /// <summary>上下文：场景 / 天气 / 季节 / 剧情标记等。</summary>
    public string Context { get; init; } = "";

    /// <summary>日期：游戏内日期 + 星期 + 节假日 + 实时时间。</summary>
    public string Date { get; init; } = "";

    /// <summary>应用发起 battle_start 的完整 params JSON（由调用方序列化）。</summary>
    public string ToParamsJson(string battleId) => JsonSerializer.Serialize(new
    {
        battle_id = battleId,
        game = new
        {
            characters = Characters.Select(c => new
            {
                id = c.Id,
                name = c.Name,
                affection = c.Affection,
                trust = c.Trust,
                energy = c.Energy,
                emotion = c.Emotion,
                location = c.Location
            }),
            context = Context,
            date = Date
        }
    });
}

/// <summary>提供给战斗驱动的一名角色。</summary>
public sealed record BattleBootCharacter
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Affection { get; init; }
    public int Trust { get; init; }
    public int Energy { get; init; } = 100;
    public string Emotion { get; init; } = "normal";
    public string Location { get; init; } = "home";
}

/// <summary>
/// 外部战斗驱动清单 battle.json 的结构。
/// （与工具模式 tool.json 同款：language=python|java，entry 默认 battle.py / battle.jar）
/// </summary>
public sealed class ExternalBattleManifest
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Language { get; set; } = "python";
    public string Entry { get; set; } = "";
}