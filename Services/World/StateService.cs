using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 自定义状态的读写口：值按「楼层 &gt; 场景 &gt; 全局」就近取，同名不同层各存各的。
/// 数据就挂在 <see cref="MapDefinition.States"/> 上，落盘跟地图同一支笔（MapService.SaveAsync），
/// 所以换存档就跟着换、老存档没有这一节也照常读。落盘不走 MapChanged，
/// 免得编辑器里点一下按钮就把整张图重排一遍。
/// </summary>
public sealed class StateService
{
    private readonly MapService _maps;

    public StateService(MapService maps) => _maps = maps;

    /// <summary>值变了（给的是状态的 Key）；UI 与流程图都在这里等。增删改也发一次。</summary>
    public event Action<string>? ValueChanged;

    public MapDefinition Map => _maps.Map;

    /// <summary>此刻站在哪个场景、哪栋楼的第几层——就近解析全靠它。</summary>
    public MapScene? Scene => _maps.CurrentScene;
    public MapLocation? Location => Scene is { } s ? _maps.Map.LocationOfScene(s.Id) : null;
    public int? Floor => Scene?.Floor;

    /// <summary>
    /// 此刻能看见的状态：全局 + 这个场景 + 这栋楼的这一层。近的排前面，
    /// 这样按钮排出来就是「这层专用的」在最左；同一把 Key 只露最近的那一条（就近顶替）。
    /// </summary>
    public IReadOnlyList<CustomStateDef> Visible()
    {
        var list = new List<CustomStateDef>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var def in ContextDefs())
        {
            if (!def.IsValid || !seen.Add(def.Key)) continue;
            list.Add(def);
        }
        return list;
    }

    /// <summary>全部状态（不限当前所在），给编辑器与 AI 报表用。</summary>
    public IReadOnlyList<CustomStateDef> All() => Map.States;

    /// <summary>
    /// 按 Key 找最近的那一条：本楼层 &gt; 本场景 &gt; 全局。
    /// <paramref name="anywhere"/> = true 时找不到就退到「全图第一个同名」，给 AI 在别处也能读到。
    /// </summary>
    public CustomStateDef? Resolve(string key, bool anywhere = false)
    {
        key = (key ?? "").Trim();
        if (key.Length == 0) return null;
        foreach (var def in ContextDefs())
            if (string.Equals(def.Key, key, StringComparison.Ordinal)) return def;
        return anywhere
            ? Map.States.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal))
            : null;
    }

    /// <summary>现在这个值；找不到就返回兜底。</summary>
    public string Get(string key, string fallback = "")
        => Resolve(key)?.Value ?? fallback;

    /// <summary>「灯」是不是「开」。判断不落盘、不联网，流程图每帧问也不心疼。</summary>
    public bool Is(string key, string value)
        => string.Equals(Resolve(key)?.Value, value, StringComparison.Ordinal);

    /// <summary>改值并落盘；给的值不在可选清单里就不改（返回 false）。</summary>
    public async Task<bool> SetAsync(string key, string value)
    {
        var def = Resolve(key);
        if (def is null || !def.Values.Contains(value)) return false;
        if (def.Current == value) return true;
        def.Current = value;
        await _maps.SaveAsync();
        ValueChanged?.Invoke(def.Key);
        return true;
    }

    /// <summary>点一下走到下一个值并落盘；返回改完之后的值。</summary>
    public async Task<string> ToggleAsync(string key)
    {
        var def = Resolve(key);
        if (def is null) return "";
        var next = def.Next();
        def.Current = next;
        await _maps.SaveAsync();
        ValueChanged?.Invoke(def.Key);
        return next;
    }

    /// <summary>
    /// 没有就新建一条（AI 想要「开/关」这类状态时用），有就直接返回。
    /// 同名已经在更近的作用域里，就听更近的那条——不在远处再造一个同名坑。
    /// </summary>
    public async Task<CustomStateDef?> EnsureAsync(string key, IEnumerable<string> values, string? scope = null)
    {
        key = (key ?? "").Trim();
        if (key.Length == 0) return null;
        var existing = Resolve(key);
        if (existing is not null) return existing;

        var def = new CustomStateDef
        {
            Key = key,
            Values = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct().ToList(),
            Scope = scope ?? StateScopes.Scene,
            SceneId = Scene?.Id ?? "",
            LocationId = Location?.Id ?? "",
            Floor = Floor,
        };
        if (def.Values.Count == 0) def.Values.Add("开");
        def.Current = def.Values[0];
        Map.States.Add(def);
        await _maps.SaveAsync();
        ValueChanged?.Invoke(key);
        return def;
    }

    public async Task<bool> RemoveAsync(string id)
    {
        var def = Map.StateById(id);
        if (def is null) return false;
        Map.States.Remove(def);
        await _maps.SaveAsync();
        ValueChanged?.Invoke(def.Key);
        return true;
    }

    /// <summary>给 AI 的一段说明：现在有哪些状态、各是什么值。没状态时返回空串。</summary>
    public string PromptText()
    {
        var list = Visible();
        if (list.Count == 0) return "";
        var parts = list.Select(s => $"{s.Key}＝{s.Value}（{StateScopes.Display(s.Scope)}）");
        return "当前可见状态：" + string.Join("；", parts) + "。";
    }

    /// <summary>近到远：本层 → 本场景 → 全局。</summary>
    private IEnumerable<CustomStateDef> ContextDefs()
    {
        var scene = Scene;
        var loc = Location;
        var floor = scene?.Floor;

        if (loc is not null && floor is not null)
        {
            foreach (var d in Map.States)
                if (d.Scope == StateScopes.Floor && d.LocationId == loc.Id && d.Floor == floor)
                    yield return d;
        }
        if (scene is not null)
        {
            foreach (var d in Map.States)
                if (d.Scope == StateScopes.Scene && d.SceneId == scene.Id)
                    yield return d;
        }
        foreach (var d in Map.States)
            if (d.Scope == StateScopes.Global)
                yield return d;
    }
}
