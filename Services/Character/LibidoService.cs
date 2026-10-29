using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 性欲系统：只对用户在存档里点名的角色生效（主角与指定配角），其余角色完全不参与。
///
/// 优先级是硬规则：<b>设定 &gt; 周期</b>。
/// 一旦用户（或角色设定）把值写死（<see cref="CharacterState.LibidoLock"/>），周期就不再影响它；
/// 没写死时，才用「基础值 + 周期调制」得出此刻的有效值。周期只做调制，不写回基础值——
/// 否则每调用一次就叠加一次，同一天里看三次会得到三个数字。
/// </summary>
public sealed class LibidoService
{
    /// <summary>该角色在这份存档里是否参与：总开关 + 存档内的点名清单。</summary>
    public static bool Participates(GameState? state, string? charId, UserSettings? settings)
    {
        if (state is null || settings is null || !settings.LibidoSystemEnabled) return false;
        if (string.IsNullOrEmpty(charId)) return false;
        return state.LibidoCharacters.Contains(charId);
    }

    /// <summary>此刻的有效性欲（0~100）。</summary>
    public static int Effective(CharacterState st, UserSettings settings, GameState? state = null, string? charId = null)
    {
        if (st is null) return 0;
        if (st.LibidoLock is { } locked) return Math.Clamp(locked, 0, 100);
        var influence = state is null || charId is null || !Participates(state, charId, settings)
            ? 0
            : Math.Clamp(settings.LibidoCycleInfluence, 0, 1);
        var value = st.Libido + CycleOffset(st.Cycle.Phase) * influence;
        return Math.Clamp((int)Math.Round(value), 0, 100);
    }

    /// <summary>
    /// AI 判定的增量。设定锁着的时候不动——这是「设定大于一切」的直接体现，
    /// 不是忽略 AI，而是用户已经说了这一项不由情境决定。
    /// </summary>
    public static bool ApplyDelta(CharacterState st, int delta, UserSettings settings,
        GameState? state = null, string? charId = null)
    {
        if (st is null || delta == 0) return false;
        if (st.LibidoLock is not null) return false;
        if (state is not null && charId is not null && !Participates(state, charId, settings)) return false;
        var next = Math.Clamp(st.Libido + delta, 0, 100);
        if (next == st.Libido) return false;
        st.Libido = next;
        return true;
    }

    /// <summary>周期对性欲的抬升/压低量（未乘影响强度）。经期不适会压低，排卵期抬高最明显。</summary>
    public static int CycleOffset(string? phase) => (phase ?? "").ToLowerInvariant() switch
    {
        "period" => -12,
        "follicular" => 4,
        "ovulation" => 18,
        "luteal" => 8,
        _ => 0,
    };

    /// <summary>给提示词用的一行描述；不参与时返回空串，调用方负责不把它拼进去。</summary>
    public static string Describe(CharacterState st, UserSettings settings, GameState? state, string charId)
    {
        if (!Participates(state, charId, settings)) return "";
        var value = Effective(st, settings, state, charId);
        var lockNote = st.LibidoLock is not null ? "（设定固定）" : "";
        return $"性欲 {value}/100{lockNote}";
    }
}
