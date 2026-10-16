using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 「全局设置 → 角色覆写」解析链。
/// 凡是可能需要按角色微调的参数都从这里取值，避免各处直接读 <see cref="UserSettings"/>
/// 而绕过角色级配置；未设置覆写时返回值与全局设置完全一致。
/// </summary>
public sealed class CharacterSettingsResolver
{
    /// <summary>人类记忆调取窗口的默认天数（可在设置或角色覆写里改；0 表示不限）。</summary>
    public const double DefaultMemoryWindowDays = 3;

    /// <summary>回忆随机扰动的默认强度（0..1）。</summary>
    public const double DefaultRecallRandomness = 0.25;

    private readonly SettingsManager _settings;
    private readonly GameEngine _engine;

    public CharacterSettingsResolver(SettingsManager settings, GameEngine engine)
    {
        _settings = settings;
        _engine = engine;
    }

    /// <summary>取某角色的覆写配置；角色不存在或无覆写时返回空的覆写对象。</summary>
    public CharacterOverrides OverridesOf(string? characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return new CharacterOverrides();
        return _engine.Roster.TryGetValue(characterId, out var ch) && ch.Overrides is not null
            ? ch.Overrides
            : new CharacterOverrides();
    }

    public int MemoryTurns(string? characterId)
    {
        var v = OverridesOf(characterId).MemoryTurns ?? _settings.Current.MemoryTurns;
        return Math.Clamp(v, 1, 200);
    }

    public int ContextTokenLimit(string? characterId)
    {
        var v = OverridesOf(characterId).ContextTokenLimit ?? _settings.Current.ContextTokenLimit;
        return v > 0 ? v : 32000;
    }

    public double AiTemperature(string? characterId)
    {
        var v = OverridesOf(characterId).AiTemperature ?? _settings.Current.AiTemperature;
        return Math.Clamp(v, 0, 2);
    }

    /// <summary>人类记忆调取窗口（天）。0 表示不限（能回忆到最早的记忆）。</summary>
    public double MemoryWindowDays(string? characterId)
    {
        var v = OverridesOf(characterId).MemoryWindowDays ?? DefaultMemoryWindowDays;
        return v < 0 ? 0 : v;
    }

    public double MemoryRecallRandomness(string? characterId)
    {
        var v = OverridesOf(characterId).MemoryRecallRandomness ?? DefaultRecallRandomness;
        return Math.Clamp(v, 0, 1);
    }

    /// <summary>
    /// 把全局端点配置按该角色覆写改写一份：只动可覆写的字段，密钥/地址/模型等一律沿用。
    /// </summary>
    public AiEndpoint EndpointFor(string? characterId, AiEndpoint baseCfg)
    {
        var o = OverridesOf(characterId);
        if (o.IsEmpty) return baseCfg;
        return baseCfg with
        {
            MemoryTurns = o.MemoryTurns is { } mt ? Math.Clamp(mt, 1, 200) : baseCfg.MemoryTurns,
            ContextTokenLimit = o.ContextTokenLimit is { } tl && tl > 0 ? tl : baseCfg.ContextTokenLimit,
            Temperature = o.AiTemperature is { } t ? Math.Clamp(t, 0, 2) : baseCfg.Temperature
        };
    }
}
