namespace WarmAsBefore.Models;

/// <summary>
/// 一张流程图里的一条规则：什么时候看（触发）→ 条件成不成立 → 依次做什么。
/// 条件是一棵树（全满足 / 任一 / 非 / 叶子），叶子读的是「现在」的世界：状态、红绿灯、
/// 时间、太阳、好感、所在场景。天气没有独立数据源，要判天气就把「天气」当成一条自定义状态。
/// 全部字段带默认值：老存档没有 flows 这一节也要能反序列化。
/// </summary>
public sealed class FlowRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>看哪种时机（见 <see cref="FlowTriggers"/>）。</summary>
    public string Trigger { get; set; } = FlowTriggers.SceneEnter;
    /// <summary>这个时机的限定对象：状态名 / 路名 / 秒数 / 时刻 / 阈值 / 场景名；空 = 不限。</summary>
    public string TriggerKey { get; set; } = "";

    /// <summary>条件树根；null = 只要触发就做。</summary>
    public FlowCondition? Condition { get; set; }
    public List<FlowAction> Actions { get; set; } = new();

    /// <summary>同一条件在这段时间里只做一次（秒）；0 = 不限。</summary>
    public int CooldownSeconds { get; set; }

    public string Note { get; set; } = "";

    /// <summary>上次真的执行是什么时候（内存里记，不落盘——重启后冷却重新算，宁可多做一次）。</summary>
    [System.Text.Json.Serialization.JsonIgnore] public DateTime LastFiredAt { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public bool IsValid =>
        Name.Trim().Length > 0 && Actions.Count > 0;
}

/// <summary>什么时候看。§9 那张触发源清单能落到服务上的都在这里，落不上的（天气）走「自定义状态」。</summary>
public static class FlowTriggers
{
    public const string SceneEnter = "scene_enter";   // 进入场景 / 换楼
    public const string StateChange = "state_change"; // 自定义状态变化（TriggerKey=状态名，空=任意）
    public const string Tick = "tick";                // 定时器（TriggerKey=秒数）
    public const string Manual = "manual";            // 主界面按钮手点
    public const string TrafficLight = "traffic_light"; // 红绿灯变灯（TriggerKey=路名）
    public const string Time = "time";                // 时刻到了（TriggerKey=HH:mm）
    public const string Sun = "sun";                  // 太阳跨档：天亮 / 黄昏 / 黑夜
    public const string Affection = "affection";      // 好感跨过阈值（TriggerKey=数字）
    public const string AiCall = "ai_call";           // AI 回完一句话

    public static string Display(string? t) => t switch
    {
        StateChange => "状态变化",
        Tick => "定时",
        Manual => "手动",
        TrafficLight => "红绿灯变化",
        Time => "到点",
        Sun => "太阳换档",
        Affection => "好感跨越",
        AiCall => "AI 说完话",
        _ => "进场景",
    };
}

/// <summary>条件的连接方式。叶子节点不用它。</summary>
public static class FlowOps
{
    public const string All = "all";
    public const string Any = "any";
    public const string Not = "not";
    public const string Leaf = "leaf";
}

/// <summary>叶子条件读什么。</summary>
public static class FlowConditionKinds
{
    public const string State = "state";         // Key=状态名, Value=要等的值
    public const string Traffic = "traffic";     // Key=路名, Value=green|yellow|red
    public const string Time = "time";           // Value=HH:mm-HH:mm（跨零点也行）
    public const string Sun = "sun";             // Value=day|dusk|night
    public const string Affection = "affection"; // Value=>=40 之类
    public const string Scene = "scene";         // Value=场景名

    public static string Display(string? kind) => kind switch
    {
        Traffic => "红绿灯",
        Time => "时间",
        Sun => "太阳",
        Affection => "好感",
        Scene => "所在场景",
        _ => "状态",
    };
}

/// <summary>
/// 条件树的一个节点。Op=all/any 时看 Children；Op=not 时否定第一个孩子；
/// Op=leaf 时读 Kind/Key/Value。这样「与/或/多层 if」都能写出来，也都能一句句读回去。
/// </summary>
public sealed class FlowCondition
{
    public string Op { get; set; } = FlowOps.Leaf;
    public List<FlowCondition> Children { get; set; } = new();
    public string Kind { get; set; } = FlowConditionKinds.State;
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";

    public static FlowCondition Leaf(string kind, string key, string value) =>
        new() { Op = FlowOps.Leaf, Kind = kind, Key = key, Value = value };

    public static FlowCondition All(params FlowCondition[] children) =>
        new() { Op = FlowOps.All, Children = children.ToList() };

    public static FlowCondition Any(params FlowCondition[] children) =>
        new() { Op = FlowOps.Any, Children = children.ToList() };

    public static FlowCondition Not(FlowCondition child) =>
        new() { Op = FlowOps.Not, Children = new List<FlowCondition> { child } };

    /// <summary>读得出来的一句话：给编辑器、给 AI、给用户看的是同一句。</summary>
    public string Describe()
    {
        switch (Op)
        {
            case FlowOps.All:
                if (Children.Count == 0) return "总是";
                return string.Join(" 且 ", Children.Select(c => Wrap(c.Describe())));
            case FlowOps.Any:
                if (Children.Count == 0) return "从不";
                return string.Join(" 或 ", Children.Select(c => Wrap(c.Describe())));
            case FlowOps.Not:
                return Children.Count == 0 ? "从不" : "不满足「" + Children[0].Describe() + "」";
        }
        return Kind switch
        {
            FlowConditionKinds.Traffic => $"{Key}的红绿灯是{SignalKinds.Display(Value)}",
            FlowConditionKinds.Time => $"时间在 {Value} 之间",
            FlowConditionKinds.Sun => $"现在是{SunPhases.Display(Value)}",
            FlowConditionKinds.Affection => $"好感 {Value}",
            FlowConditionKinds.Scene => $"身在「{Value}」",
            _ => Value.Length == 0 ? $"{Key}有值" : $"{Key} 是「{Value}」",
        };
    }

    private static string Wrap(string text) => text.Contains(' ') ? "（" + text + "）" : text;
}

/// <summary>动作做什么。</summary>
public static class FlowActionKinds
{
    public const string SetState = "set_state";     // Key=状态名, Value=目标值
    public const string ToggleState = "toggle_state"; // Key=状态名
    public const string MoveScene = "move_scene";   // Value=场景名
    public const string Notify = "notify";          // Value=一句提示
    public const string RunFlow = "run_flow";       // Value=另一条规则的名字

    public static string Display(string? kind) => kind switch
    {
        SetState => "改状态",
        ToggleState => "切状态",
        MoveScene => "去场景",
        Notify => "提示",
        RunFlow => "跑另一条",
        _ => "动作",
    };
}

public sealed class FlowAction
{
    public string Kind { get; set; } = FlowActionKinds.SetState;
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";

    public string Describe() => Kind switch
    {
        FlowActionKinds.ToggleState => $"把「{Key}」切到下一个值",
        FlowActionKinds.MoveScene => $"去「{Value}」",
        FlowActionKinds.Notify => $"说一句：{Value}",
        FlowActionKinds.RunFlow => $"接着跑「{Value}」",
        _ => $"把「{Key}」改成「{Value}」",
    };
}

/// <summary>太阳档位：按太阳高度角分，不是拍脑袋按钟点。</summary>
public static class SunPhases
{
    public const string Day = "day";
    public const string Dusk = "dusk";
    public const string Night = "night";

    public static string Display(string? phase) => phase switch
    {
        Dusk => "黄昏",
        Night => "黑天",
        _ => "白天",
    };
}
