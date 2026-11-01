using System.Globalization;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 流程图的执行器：时机到了就挑出对应的规则，判一遍条件，成立就依次做动作。
///
/// 三条护栏：
/// 1. 一跳一预算——一次触发最多做 <see cref="MaxActionsPerRun"/> 个动作，「跑另一条」也在这个预算里；
/// 2. 环检测——同一条规则在这一次触发里已经跑过就不再进去，A→B→A 直接断掉；
/// 3. 冷却——规则自己写了几秒内只做一次，就真只做一次。
/// 另外，规则之间互相触发状态（A 改灯 → B 改门 → A 又改灯）时，两秒内超过上限就整体安静一会儿，
/// 死循环宁可少做，不许把界面卡死。
/// </summary>
public sealed class FlowService : IDisposable
{
    private const int MaxActionsPerRun = 64;
    private const int MaxRunsPerWindow = 24;
    private const double WindowSeconds = 2.0;
    private const double SuppressSeconds = 5.0;

    private readonly MapService _maps;
    private readonly StateService _states;
    private readonly TrafficSignalService _signals;
    private readonly GameEngine _engine;

    private Microsoft.Maui.Dispatching.IDispatcherTimer? _clock;
    // 轮询类的记忆：定时到没到、这一分钟看没看过、太阳/灯光上次是什么档
    private readonly Dictionary<string, DateTime> _nextTick = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _lastKey = new(StringComparer.Ordinal);
    private string _lastSunPhase = "";
    private int _lastAffection = -1;

    private DateTime _windowStart = DateTime.MinValue;
    private int _windowRuns;
    private DateTime _suppressedUntil = DateTime.MinValue;

    public FlowService(MapService maps, StateService states, TrafficSignalService signals, GameEngine engine)
    {
        _maps = maps;
        _states = states;
        _signals = signals;
        _engine = engine;
        _maps.SceneChanged += OnSceneChanged;
        _states.ValueChanged += OnStateChanged;
        EnsureTicker();
    }

    /// <summary>一条规则的动作全都做完了（给界面与日志看）。</summary>
    public event Action<FlowRule>? Fired;
    /// <summary>规则里要「说一句」时冒出来；由主界面接成 Toast。</summary>
    public event Action<string>? Notify;

    public MapDefinition Map => _maps.Map;

    /// <summary>规则表被改过（编辑器与 AI 都会发）：主界面那排「手动」钮要跟着重排。</summary>
    public event Action? RulesChanged;

    /// <summary>规则改了之后落盘（与编辑器同一个笔：只走 SaveAsync，不发 MapChanged）。</summary>
    public async Task SaveAsync()
    {
        await _maps.SaveAsync();
        RulesChanged?.Invoke();
    }

    // ==================== 触发 ====================

    /// <summary>跑一次某类触发（主界面按钮、AI 工具，以及内部各事件都从这里进）。</summary>
    public async Task RunAsync(string trigger, string triggerKey = "")
    {
        if (DateTime.UtcNow < _suppressedUntil) return;
        if (!TakeRunSlot()) return;
        // 服务可能在建界面之前就构造了（那时 Dispatcher 还没有）：每次真跑一遍时顺手补挂一次
        EnsureTicker();

        var rules = Map.Flows
            .Where(r => r.Enabled && r.IsValid && r.Trigger == trigger && MatchesKey(r, triggerKey))
            .ToList();
        if (rules.Count == 0) return;

        var scope = new RunScope();
        foreach (var rule in rules)
            await ExecuteAsync(rule, scope);
    }

    /// <summary>按名字手动跑一条（不管它写的什么触发时机）；给 AI 与手动按钮用。</summary>
    public async Task<bool> RunRuleAsync(string nameOrId)
    {
        var rule = Map.FlowById(nameOrId) ?? Map.FlowByName(nameOrId);
        if (rule is null || !rule.IsValid || DateTime.UtcNow < _suppressedUntil) return false;
        if (!TakeRunSlot()) return false;
        await ExecuteAsync(rule, new RunScope());
        return true;
    }

    /// <summary>两秒钟里跑的次数超过上限就停五秒：这不是正常节奏，是规则在互相点着。</summary>
    private bool TakeRunSlot()
    {
        var now = DateTime.UtcNow;
        if ((now - _windowStart).TotalSeconds > WindowSeconds)
        {
            _windowStart = now;
            _windowRuns = 0;
        }
        if (++_windowRuns <= MaxRunsPerWindow) return true;
        _suppressedUntil = now.AddSeconds(SuppressSeconds);
        _windowRuns = 0;
        App.WriteLog("FlowService -> 触发太密，暂停 5 秒（检查是不是两条规则在互相点着）");
        return false;
    }

    private sealed class RunScope
    {
        public int Left = MaxActionsPerRun;
        public readonly HashSet<string> Path = new(StringComparer.Ordinal);
    }

    private async Task ExecuteAsync(FlowRule rule, RunScope scope)
    {
        if (!rule.Enabled || !rule.IsValid) return;
        if (!scope.Path.Add(rule.Id)) return;                 // 环：这一次已经来过了
        try
        {
            if (rule.CooldownSeconds > 0 && rule.LastFiredAt != default
                && (DateTime.UtcNow - rule.LastFiredAt).TotalSeconds < rule.CooldownSeconds)
                return;
            if (rule.Condition is not null && !Evaluate(rule.Condition)) return;

            rule.LastFiredAt = DateTime.UtcNow;
            foreach (var action in rule.Actions)
            {
                if (scope.Left <= 0)
                {
                    App.WriteLog("FlowService -> 单次预算用完，后面的动作没做：" + rule.Name);
                    break;
                }
                scope.Left--;
                await ApplyAsync(action, scope);
            }
            Fired?.Invoke(rule);
        }
        catch (Exception ex)
        {
            App.WriteLog("FlowService." + rule.Name + " -> " + ex);
        }
    }

    private async Task ApplyAsync(FlowAction action, RunScope scope)
    {
        switch (action.Kind)
        {
            case FlowActionKinds.SetState:
                await _states.SetAsync(action.Key, action.Value);
                break;
            case FlowActionKinds.ToggleState:
                await _states.ToggleAsync(action.Key);
                break;
            case FlowActionKinds.MoveScene:
                if (!string.IsNullOrWhiteSpace(action.Value))
                    await _maps.MoveToAsync(action.Value);
                break;
            case FlowActionKinds.Notify:
                if (!string.IsNullOrWhiteSpace(action.Value)) Notify?.Invoke(action.Value);
                break;
            case FlowActionKinds.RunFlow:
            {
                var next = Map.FlowById(action.Value) ?? Map.FlowByName(action.Value);
                if (next is not null) await ExecuteAsync(next, scope);
                break;
            }
        }
    }

    // ==================== 条件 ====================

    /// <summary>判一棵条件树。读的全是「现在」，不落盘、不联网。</summary>
    public bool Evaluate(FlowCondition? condition)
    {
        if (condition is null) return true;
        switch (condition.Op)
        {
            case FlowOps.All:
                return condition.Children.All(Evaluate);
            case FlowOps.Any:
                return condition.Children.Any(Evaluate);
            case FlowOps.Not:
                return condition.Children.Count > 0 && !Evaluate(condition.Children[0]);
        }

        return condition.Kind switch
        {
            FlowConditionKinds.Traffic => TrafficIs(condition.Key, condition.Value),
            FlowConditionKinds.Time => InTimeBand(condition.Value, DateTime.Now),
            FlowConditionKinds.Sun => SunPhaseNow() is { } phase && phase == condition.Value,
            FlowConditionKinds.Affection => AffectionMatches(condition.Value),
            FlowConditionKinds.Scene => SceneIs(condition.Value),
            _ => string.Equals(_states.Get(condition.Key), condition.Value, StringComparison.Ordinal),
        };
    }

    private bool TrafficIs(string road, string color)
    {
        var name = (road ?? "").Trim();
        if (name.Length == 0) return false;
        var edge = Map.Edges.FirstOrDefault(e => e.TrafficLight
            && string.Equals(e.RoadName, name, StringComparison.Ordinal));
        if (edge is null) return false;
        var kind = _signals.Local(edge, true).Kind;
        return string.Equals(kind, (color ?? "").Trim(), StringComparison.Ordinal);
    }

    private static bool InTimeBand(string band, DateTime now)
    {
        var parts = (band ?? "").Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;
        if (!TimeOnly.TryParse(parts[0], CultureInfo.InvariantCulture, out var from)
            || !TimeOnly.TryParse(parts[1], CultureInfo.InvariantCulture, out var to)) return false;
        var t = TimeOnly.FromDateTime(now);
        return from <= to ? t >= from && t < to : t >= from || t < to;   // 跨零点
    }

    private bool AffectionMatches(string expr)
    {
        var affection = _engine.ActiveCharacter?.State.Affection ?? 0;
        var text = (expr ?? "").Trim().Replace(" ", "");
        if (text.Length == 0) return false;
        var op = text.StartsWith(">=", StringComparison.Ordinal) ? ">="
            : text.StartsWith("<=", StringComparison.Ordinal) ? "<="
            : text.StartsWith(">", StringComparison.Ordinal) ? ">"
            : text.StartsWith("<", StringComparison.Ordinal) ? "<"
            : "=";
        var num = text.TrimStart('>', '<', '=');
        if (!int.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return false;
        return op switch
        {
            ">=" => affection >= v,
            "<=" => affection <= v,
            ">" => affection > v,
            "<" => affection < v,
            _ => affection == v,
        };
    }

    private bool SceneIs(string name)
    {
        var scene = _maps.CurrentScene;
        if (scene is null) return false;
        return string.Equals(scene.Name, name, StringComparison.Ordinal)
            || string.Equals($"{_maps.Map.LocationNameOf(scene.Id)}·{scene.Name}", name, StringComparison.Ordinal);
    }

    /// <summary>
    /// 现在太阳在哪个档。按地图中心的经纬度与本地时刻算高度角（&gt;6° 白天，-6°~6° 黄昏，更低是黑天）。
    /// 这张图没有经纬度（手工图）就返回 null——不拿钟点凑一个假答案。
    /// </summary>
    public string? SunPhaseNow()
    {
        var lat = Map.CenterLatitude;
        var lng = Map.CenterLongitude;
        if (Math.Abs(lat) < 0.0001 && Math.Abs(lng) < 0.0001) return null;

        var now = DateTime.Now;
        var dayOfYear = now.DayOfYear;
        var decl = -23.44 * Math.Cos(2 * Math.PI * (dayOfYear + 10) / 365.0);      // 度
        var solarHour = now.Hour + now.Minute / 60.0;                              // 忽略时差：判白天与黄昏够用
        var hourAngle = (solarHour - 12) * 15;
        var latRad = lat * Math.PI / 180;
        var declRad = decl * Math.PI / 180;
        var haRad = hourAngle * Math.PI / 180;
        var sinAlt = Math.Sin(latRad) * Math.Sin(declRad)
                     + Math.Cos(latRad) * Math.Cos(declRad) * Math.Cos(haRad);
        var alt = Math.Asin(Math.Clamp(sinAlt, -1, 1)) * 180 / Math.PI;

        return alt > 6 ? SunPhases.Day : alt > -6 ? SunPhases.Dusk : SunPhases.Night;
    }

    // ==================== 轮询与接线 ====================

    private void OnSceneChanged(MapScene scene) => _ = RunAsync(FlowTriggers.SceneEnter, scene.Name);

    private void OnStateChanged(string key) => _ = RunAsync(FlowTriggers.StateChange, key);

    private static bool MatchesKey(FlowRule rule, string key)
        => rule.TriggerKey.Trim().Length == 0 || string.Equals(rule.TriggerKey.Trim(), key, StringComparison.Ordinal);

    /// <summary>有需要看时钟的规则才挂每秒一跳；一条都没有就不挂，别白耗。</summary>
    public void EnsureTicker()
    {
        var needed = Map.Flows.Any(r => r.Enabled && (r.Trigger is FlowTriggers.Tick
            or FlowTriggers.Time or FlowTriggers.Sun or FlowTriggers.Affection or FlowTriggers.TrafficLight));
        if (!needed)
        {
            _clock?.Stop();
            _clock = null;
            return;
        }
        if (_clock is not null) return;
        var clock = Microsoft.Maui.Controls.Application.Current?.Dispatcher.CreateTimer();
        if (clock is null) return;
        clock.Interval = TimeSpan.FromSeconds(1);
        clock.Tick += (_, _) => Poll();
        clock.Start();
        _clock = clock;
    }

    private void Poll()
    {
        var now = DateTime.Now;
        var rules = Map.Flows.ToList();
        if (rules.Count == 0) return;

        // 这一秒世界变了什么先算一次，再分给各条规则看——每条规则各算一遍会把「变没变」算丢。
        var sunPhase = SunPhaseNow() ?? "";
        var sunChanged = sunPhase.Length > 0 && sunPhase != _lastSunPhase;
        if (sunPhase.Length > 0) _lastSunPhase = sunPhase;

        var lightChanges = new List<(string Road, string Kind)>();
        foreach (var edge in Map.Edges)
        {
            if (!edge.TrafficLight || string.IsNullOrWhiteSpace(edge.RoadName)) continue;
            var kind = _signals.Local(edge, true).Kind;
            var k = "light:" + edge.Id;
            var prev = _lastKey.TryGetValue(k, out var p) ? p : "";
            _lastKey[k] = kind;
            if (prev.Length > 0 && prev != kind) lightChanges.Add((edge.RoadName, kind));
        }

        var affection = _engine.ActiveCharacter?.State.Affection ?? 0;
        var affectionBefore = _lastAffection;
        _lastAffection = affection;

        foreach (var rule in rules)
        {
            if (!rule.Enabled || !rule.IsValid) continue;
            switch (rule.Trigger)
            {
                case FlowTriggers.Tick:
                {
                    // 点对点点火：谁到期就跑谁。这里不能走 RunAsync(trigger,"")——
                    // 那会把所有同触发、且 TriggerKey 为空的规则一起拉下场，
                    // A 的 30 秒到点时 B 的 120 秒规则也会跟着跑一遍（B 自己的到期门只拦 Poll，拦不到执行）。
                    var seconds = int.TryParse(rule.TriggerKey.Trim(), out var s) && s > 0 ? s : 60;
                    var due = _nextTick.TryGetValue(rule.Id, out var at) ? at : now;
                    if (now < due) break;
                    _nextTick[rule.Id] = now.AddSeconds(seconds);
                    _ = RunRuleAsync(rule.Id);
                    break;
                }
                case FlowTriggers.Time:
                {
                    // 到点那一分钟里触发一次，错过不回头补
                    var hhmm = now.ToString("HH:mm", CultureInfo.InvariantCulture);
                    var k = "time:" + rule.Id;
                    var last = _lastKey.TryGetValue(k, out var l) ? l : "";
                    _lastKey[k] = hhmm;
                    if (string.Equals(rule.TriggerKey.Trim(), hhmm, StringComparison.Ordinal) && last != hhmm)
                        _ = RunAsync(FlowTriggers.Time, hhmm);
                    break;
                }
                case FlowTriggers.Sun:
                    if (sunChanged && MatchesKey(rule, sunPhase))
                        _ = RunAsync(FlowTriggers.Sun, sunPhase);
                    break;
                case FlowTriggers.Affection:
                {
                    // 同样点对点：好感跨过阈值那一刻跑这一条。
                    // 不能走 RunAsync(trigger, 当前值)——RunAsync 里还会用 TriggerKey 再筛一遍，
                    // 而这里 TriggerKey 是阈值（80）、传过去的是当下值（90），阈值规则会被哑音吞掉。
                    var threshold = int.TryParse(rule.TriggerKey.Trim(), out var t) ? t : int.MaxValue;
                    if (affectionBefore >= 0 && affectionBefore < threshold && affection >= threshold)
                        _ = RunRuleAsync(rule.Id);
                    break;
                }
                case FlowTriggers.TrafficLight:
                    foreach (var (road, _) in lightChanges)
                        if (MatchesKey(rule, road))
                            _ = RunAsync(FlowTriggers.TrafficLight, road);
                    break;
            }
        }
    }

    public void Dispose()
    {
        _maps.SceneChanged -= OnSceneChanged;
        _states.ValueChanged -= OnStateChanged;
        _clock?.Stop();
        _clock = null;
    }
}
