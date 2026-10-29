using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 「全局设置 → 角色覆写」解析链。
/// 凡是可能需要按角色微调的参数都从这里取值，避免各处直接读 <see cref="UserSettings"/>
/// 而绕过角色级配置；未设置覆写时返回值与全局设置完全一致。
/// </summary>
public sealed class CharacterSettingsResolver
{
    /// <summary>回忆随机扰动的默认强度（0..1）。</summary>
    public const double DefaultRecallRandomness = HumanMemoryMath.DefaultRandomness;

    /// <summary>人类记忆调取窗口的默认天数（保留常量以兼容既有引用）。</summary>
    public const double DefaultMemoryWindowDays = HumanMemoryMath.DefaultWindowDays;

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
        var v = OverridesOf(characterId).MemoryWindowDays ?? _settings.Current.MemoryWindowDays;
        return v < 0 ? HumanMemoryMath.DefaultWindowDays : v;
    }

    public double MemoryRecallRandomness(string? characterId)
    {
        var v = OverridesOf(characterId).MemoryRecallRandomness ?? _settings.Current.MemoryRecallRandomness;
        return Math.Clamp(v, 0, 1);
    }

    /// <summary>
    /// 把全局端点配置按该角色覆写改写一份：只动可覆写的字段，密钥/地址/模型等一律沿用。
    /// 角色单独绑过模型时先换成它绑的那个端点（见 <see cref="BoundEndpoint"/>），覆写再叠在上面。
    /// </summary>
    public AiEndpoint EndpointFor(string? characterId, AiEndpoint baseCfg)
    {
        var cfg = BoundEndpoint(characterId) ?? baseCfg;
        var o = OverridesOf(characterId);
        if (o.IsEmpty) return cfg;
        return cfg with
        {
            MemoryTurns = o.MemoryTurns is { } mt ? Math.Clamp(mt, 1, 200) : cfg.MemoryTurns,
            ContextTokenLimit = o.ContextTokenLimit is { } tl && tl > 0 ? tl : cfg.ContextTokenLimit,
            Temperature = o.AiTemperature is { } t ? Math.Clamp(t, 0, 2) : cfg.Temperature
        };
    }

    /// <summary>
    /// 该角色绑定的模型显示名（「厂商 · 模型」）；没绑、或绑的厂商已经不可用时返回空串。
    /// 主界面右上角的「AI」用它来判断这次临时指定对 TA 到底生不生效。
    /// </summary>
    public string BoundModelLabel(string? characterId)
    {
        var e = BoundEndpoint(characterId);
        if (e is null) return "";
        return string.IsNullOrWhiteSpace(e.Provider) ? e.Model : $"{e.Provider} · {e.Model}";
    }

    /// <summary>
    /// 角色单独绑定的模型（设置里「这个角色用哪个 AI」）。
    /// 只绑模型不绑厂商时，在已启用的厂商里找第一款提供它的；找不到就当没绑，回到全局轮换。
    /// </summary>
    public AiEndpoint? BoundEndpoint(string? characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return null;
        if (!_engine.Roster.TryGetValue(characterId, out var ch)) return null;
        var bind = ch.Model;
        if (bind is null) return null;
        var providerId = (bind.ProviderId ?? "").Trim();
        var model = (bind.Model ?? "").Trim();
        if (providerId.Length == 0 && model.Length == 0) return null;

        var s = _settings.Current;
        var providers = (s.AiProviders ?? new List<AiProvider>())
            .Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Url))
            .ToList();
        if (providers.Count == 0) return null;

        AiProvider? provider = null;
        if (providerId.Length > 0)
        {
            provider = providers.FirstOrDefault(p => string.Equals(p.Id, providerId, StringComparison.Ordinal))
                       ?? providers.FirstOrDefault(p => string.Equals(p.Name, providerId, StringComparison.OrdinalIgnoreCase));
        }
        if (provider is null && model.Length > 0)
        {
            provider = providers.FirstOrDefault(p => (p.Models ?? new List<AiProviderModel>())
                .Any(m => m.Enabled && string.Equals(m.Model, model, StringComparison.OrdinalIgnoreCase)));
        }
        if (provider is null) return null;

        var keys = (provider.Keys ?? new List<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
        if (keys.Count == 0) return null;
        var picked = model.Length > 0
            ? model
            : (provider.Models ?? new List<AiProviderModel>()).FirstOrDefault(m => m.Enabled)?.Model ?? "";
        if (picked.Length == 0) return null;

        return new AiEndpoint
        {
            Provider = string.IsNullOrWhiteSpace(provider.Name) ? picked : provider.Name,
            Url = provider.Url.Trim(),
            Model = picked,
            Key = keys[0],
            KeyPool = keys,
            Temperature = s.AiTemperature,
            MaxTokens = s.AiMaxTokens,
            DeepThink = s.DeepThink || s.ComplexPlot,
            DeepModel = s.DeepModel,
            MemoryTurns = s.MemoryTurns,
            ContextTokenLimit = s.ContextTokenLimit > 0 ? s.ContextTokenLimit : 32000,
            TimeoutSeconds = provider.TimeoutSeconds
        };
    }
}
