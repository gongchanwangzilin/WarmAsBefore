using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 流程图编辑器：一条条规则列出来，用人话读（什么时候看、条件、做什么），点开就改。
/// 复杂条件树（多层与/或）交给 AI 用 flow_save 写；这里管「与」起来的一串常用条件，
/// 以及随手试跑、启停、删。所有改动落盘走 FlowService.SaveAsync，不碰 MapChanged。
/// </summary>
public sealed partial class FlowEditorViewModel : ObservableObject
{
    private readonly FlowService _flows;
    private readonly StateService _states;
    private readonly MapService _maps;

    public ObservableCollection<FlowRowVm> Rows { get; } = new();

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isEmpty = true;

    public FlowEditorViewModel(FlowService flows, StateService states, MapService maps)
    {
        _flows = flows;
        _states = states;
        _maps = maps;
        Refresh();
    }

    public void Refresh()
    {
        Rows.Clear();
        foreach (var rule in _flows.Map.Flows)
            Rows.Add(new FlowRowVm(rule));
        IsEmpty = Rows.Count == 0;
        Status = IsEmpty
            ? "还没有规则。点「＋ 新规则」写一条；多层与/或的条件可以让 AI 帮你写。"
            : $"共 {Rows.Count} 条规则，改完立刻生效（不用重启）。";
    }

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");

    // ==================== 增 / 改 ====================

    [RelayCommand]
    private async Task Add()
    {
        var name = await AskAsync("新规则", "给这条规则起个名字（例：天黑了开门口的灯）");
        if (name is null) return;
        var rule = await BuildAsync(new FlowRule { Name = name }, isNew: true);
        if (rule is null) return;

        _flows.Map.Flows.Add(rule);
        await SaveAndRefreshAsync("规则已写好。");
    }

    [RelayCommand]
    private async Task Edit(FlowRowVm? row)
    {
        if (row is null) return;
        // 先在一份草稿上问：问到一半取消，原规则一个字都别动
        var draft = new FlowRule
        {
            Id = row.Rule.Id,
            Name = row.Rule.Name,
            Enabled = row.Rule.Enabled,
            Trigger = row.Rule.Trigger,
            TriggerKey = row.Rule.TriggerKey,
            Condition = row.Rule.Condition,
            Actions = row.Rule.Actions,
            CooldownSeconds = row.Rule.CooldownSeconds,
            Note = row.Rule.Note,
        };
        var built = await BuildAsync(draft, isNew: false);
        if (built is null) return;
        var index = _flows.Map.Flows.IndexOf(row.Rule);
        if (index >= 0) _flows.Map.Flows[index] = built;
        await SaveAndRefreshAsync("规则已改好。");
    }

    /// <summary>把一条规则问全（触发 → 条件 → 动作）；中途取消返回 null，不动原规则。</summary>
    private async Task<FlowRule?> BuildAsync(FlowRule rule, bool isNew)
    {
        var triggerLabels = new[]
        {
            "进场景 / 换楼", "自定义状态变化", "定时", "手动",
            "红绿灯变化", "到点", "太阳换档", "好感跨越", "AI 说完话",
        };
        var triggerValues = new[]
        {
            FlowTriggers.SceneEnter, FlowTriggers.StateChange, FlowTriggers.Tick, FlowTriggers.Manual,
            FlowTriggers.TrafficLight, FlowTriggers.Time, FlowTriggers.Sun, FlowTriggers.Affection, FlowTriggers.AiCall,
        };
        var pick = await PickAsync("什么时候看？", triggerLabels);
        if (pick < 0) return null;
        rule.Trigger = triggerValues[pick];

        rule.TriggerKey = await AskTriggerKeyAsync(rule.Trigger);
        if (rule.TriggerKey is null) return null;

        var (conditionOk, condition) = await BuildConditionAsync(isNew ? null : rule.Condition);
        if (!conditionOk) return null;
        rule.Condition = condition;

        var actions = await BuildActionsAsync();
        if (actions is null) return null;
        rule.Actions = actions;
        if (rule.Name.Trim().Length == 0)
        {
            var name = await AskAsync("起个名字", "这条规则叫什么？");
            if (name is null) return null;
            rule.Name = name;
        }
        return rule;
    }

    private async Task<string?> AskTriggerKeyAsync(string trigger)
    {
        switch (trigger)
        {
            case FlowTriggers.StateChange:
                return await AskAsync("看哪个状态？", "状态名（例：客厅的灯）；留空 = 任何状态变化都看") ?? "";
            case FlowTriggers.Tick:
                return await AskAsync("多久一次？", "秒数（例：60）") ?? "";
            case FlowTriggers.TrafficLight:
                return await AskAsync("看哪条路？", "路名（要挂了红绿灯的那条）；留空 = 任何一条") ?? "";
            case FlowTriggers.Time:
            {
                var text = await AskAsync("几点？", "写 HH:mm（例：22:30）；到点那一分钟内触发一次");
                if (text is null) return null;
                if (!TimeOnly.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    Toast.Warn("时刻看不懂：写成 22:30 这样。");
                    return null;
                }
                return text;
            }
            case FlowTriggers.Sun:
            {
                var pick = await PickAsync("哪一档？", new[] { "白天", "黄昏", "黑天" });
                return pick < 0 ? null : pick switch { 1 => SunPhases.Dusk, 2 => SunPhases.Night, _ => SunPhases.Day };
            }
            case FlowTriggers.Affection:
                return await AskAsync("好感多少？", "阈值（例：40 = 首次到 40 时触发）") ?? "";
            default:
                return "";
        }
    }

    // ==================== 条件 ====================

    private async Task<(bool Ok, FlowCondition? Condition)> BuildConditionAsync(FlowCondition? existing)
    {
        var leaves = new List<FlowCondition>();
        if (existing is not null && existing.Op == FlowOps.All)
            leaves.AddRange(existing.Children);
        else if (existing is not null && existing.Op == FlowOps.Leaf)
            leaves.Add(existing);

        while (true)
        {
            var labels = new[] { "状态是某个值", "红绿灯", "时间在某个区间", "太阳档位", "好感比较", "所在场景" };
            var pick = await PickAsync(
                leaves.Count == 0 ? "要加条件吗？（不加 = 只要触发就做）" : $"已有 {leaves.Count} 条条件，还要加吗？",
                labels.Append("不再加，就这样").ToArray());
            if (pick < 0) return (false, null);
            if (pick >= labels.Length) break;

            FlowCondition? leaf = pick switch
            {
                0 => await AskStateLeafAsync(),
                1 => await AskTrafficLeafAsync(),
                2 => await AskTimeLeafAsync(),
                3 => await AskSunLeafAsync(),
                4 => await AskAffectionLeafAsync(),
                _ => await AskSceneLeafAsync(),
            };
            if (leaf is null) return (false, null);
            leaves.Add(leaf);
        }

        return (true, leaves.Count switch
        {
            0 => null,
            1 => leaves[0],
            _ => FlowCondition.All(leaves.ToArray()),
        });
    }

    private async Task<FlowCondition?> AskStateLeafAsync()
    {
        var key = await AskAsync("状态条件", "状态名（例：客厅的灯）");
        if (key is null) return null;
        var def = _states.Resolve(key, anywhere: true);
        var value = def is not null && def.Values.Count > 0
            ? await PickFromAsync("等于哪个值？", def.Values.ToArray())
            : await AskAsync("等于哪个值？", "值（这个状态还没建，先按名字写也行）");
        if (value is null) return null;
        return FlowCondition.Leaf(FlowConditionKinds.State, key, value);
    }

    private async Task<FlowCondition?> AskTrafficLeafAsync()
    {
        var roads = _maps.Map.Edges.Where(e => e.TrafficLight && !string.IsNullOrWhiteSpace(e.RoadName))
            .Select(e => e.RoadName).Distinct().ToArray();
        string? road;
        if (roads.Length > 0)
        {
            var pick = await PickAsync("哪条路的灯？", roads.Append("换个路名…").ToArray());
            if (pick < 0) return null;
            road = pick < roads.Length ? roads[pick] : await AskAsync("路名", "挂了红绿灯的路名");
            if (road is null) return null;
        }
        else
        {
            road = await AskAsync("哪条路的灯？", "这张图还没有挂灯的路，名字先写上也可以");
            if (road is null) return null;
        }
        var color = await PickAsync("等到什么色？", new[] { "绿灯", "黄灯", "红灯" });
        if (color < 0) return null;
        var kind = color switch { 0 => SignalKinds.Green, 1 => SignalKinds.Yellow, _ => SignalKinds.Red };
        return FlowCondition.Leaf(FlowConditionKinds.Traffic, road, kind);
    }

    private async Task<FlowCondition?> AskTimeLeafAsync()
    {
        var band = await AskAsync("时间区间", "写 HH:mm-HH:mm（例：18:00-06:00，跨零点也行）");
        return band is null ? null : FlowCondition.Leaf(FlowConditionKinds.Time, "", band);
    }

    private async Task<FlowCondition?> AskSunLeafAsync()
    {
        var pick = await PickAsync("太阳在哪一档？", new[] { "白天", "黄昏", "黑天" });
        if (pick < 0) return null;
        var phase = pick switch { 1 => SunPhases.Dusk, 2 => SunPhases.Night, _ => SunPhases.Day };
        if (_maps.Map.CenterLatitude == 0 && _maps.Map.CenterLongitude == 0)
            Toast.Info("这张图没有经纬度：太阳条件永远不成立，导入真实地图后才有用。");
        return FlowCondition.Leaf(FlowConditionKinds.Sun, "", phase);
    }

    private async Task<FlowCondition?> AskAffectionLeafAsync()
    {
        var expr = await AskAsync("好感条件", "例：>=40、<=10、=100");
        return expr is null ? null : FlowCondition.Leaf(FlowConditionKinds.Affection, "", expr.Replace(" ", ""));
    }

    private async Task<FlowCondition?> AskSceneLeafAsync()
    {
        var scenes = _maps.Map.AllScenes.Select(s => s.Name).Distinct().ToArray();
        string? scene;
        if (scenes.Length > 0)
        {
            var pick = await PickAsync("在哪个场景？", scenes.Append("换个名字…").ToArray());
            if (pick < 0) return null;
            scene = pick < scenes.Length ? scenes[pick] : await AskAsync("场景名", "写到场景名");
            if (scene is null) return null;
        }
        else
        {
            scene = await AskAsync("场景名", "这张图还没有场景，名字先写上也可以");
            if (scene is null) return null;
        }
        return FlowCondition.Leaf(FlowConditionKinds.Scene, "", scene);
    }

    // ==================== 动作 ====================

    private async Task<List<FlowAction>?> BuildActionsAsync()
    {
        var actions = new List<FlowAction>();
        while (true)
        {
            var labels = new[] { "把状态改成某个值", "切换状态", "去某个场景", "提示一句", "跑另一条规则" };
            var pick = await PickAsync(
                actions.Count == 0 ? "然后做什么？" : $"已有 {actions.Count} 个动作，还要加吗？",
                labels.Append("就这些，保存").ToArray());
            if (pick < 0) return null;
            if (pick >= labels.Length) break;

            FlowAction? action = pick switch
            {
                0 => await AskSetStateActionAsync(),
                1 => await AskToggleStateActionAsync(),
                2 => await AskMoveSceneActionAsync(),
                3 => await AskNotifyActionAsync(),
                _ => await AskRunFlowActionAsync(),
            };
            if (action is null) return null;
            actions.Add(action);
        }
        return actions.Count == 0 ? null : actions;
    }

    private async Task<FlowAction?> AskSetStateActionAsync()
    {
        var key = await AskAsync("改哪个状态？", "状态名（例：客厅的灯）");
        if (key is null) return null;
        var def = _states.Resolve(key, anywhere: true);
        var value = def is not null && def.Values.Count > 0
            ? await PickFromAsync("改成哪个值？", def.Values.ToArray())
            : await AskAsync("改成哪个值？", "值（例：开）");
        if (value is null) return null;
        if (def is null) await _states.EnsureAsync(key, new[] { value, "关" });
        return new FlowAction { Kind = FlowActionKinds.SetState, Key = key, Value = value };
    }

    private async Task<FlowAction?> AskToggleStateActionAsync()
    {
        var key = await AskAsync("切哪个状态？", "状态名（例：门口的门）");
        return key is null ? null : new FlowAction { Kind = FlowActionKinds.ToggleState, Key = key };
    }

    private async Task<FlowAction?> AskMoveSceneActionAsync()
    {
        var scenes = _maps.Map.AllScenes.Select(s => s.Name).Distinct().ToArray();
        string? scene;
        if (scenes.Length > 0)
        {
            var pick = await PickAsync("去哪个场景？", scenes.Append("换个名字…").ToArray());
            if (pick < 0) return null;
            scene = pick < scenes.Length ? scenes[pick] : await AskAsync("场景名", "写到场景名");
            if (scene is null) return null;
        }
        else
        {
            scene = await AskAsync("场景名", "写到场景名");
            if (scene is null) return null;
        }
        return new FlowAction { Kind = FlowActionKinds.MoveScene, Value = scene };
    }

    private async Task<FlowAction?> AskNotifyActionAsync()
    {
        var text = await AskAsync("提示什么？", "一句话（会以 Toast 弹出来）");
        return text is null ? null : new FlowAction { Kind = FlowActionKinds.Notify, Value = text };
    }

    private async Task<FlowAction?> AskRunFlowActionAsync()
    {
        var names = _flows.Map.Flows.Select(f => f.Name).Where(n => n.Length > 0).Distinct().ToArray();
        string? name;
        if (names.Length > 0)
        {
            var pick = await PickAsync("跑哪条？", names);
            if (pick < 0) return null;
            name = names[pick];
        }
        else
        {
            Toast.Info("还没有别的规则可跑。");
            return null;
        }
        return new FlowAction { Kind = FlowActionKinds.RunFlow, Value = name };
    }

    // ==================== 行操作 ====================

    [RelayCommand]
    private async Task Delete(FlowRowVm? row)
    {
        if (row is null) return;
        if (!await Shell.Current.DisplayAlert("删掉这条规则？", row.Rule.Name, "删掉", "算了")) return;
        _flows.Map.Flows.Remove(row.Rule);
        await SaveAndRefreshAsync("已删掉。");
    }

    [RelayCommand]
    private async Task Toggle(FlowRowVm? row)
    {
        if (row is null) return;
        row.Rule.Enabled = !row.Rule.Enabled;
        await SaveAndRefreshAsync(row.Rule.Enabled ? "已启用。" : "已停用：这条规则不再自动触发，手动跑仍可用。");
    }

    [RelayCommand]
    private async Task Test(FlowRowVm? row)
    {
        if (row is null) return;
        var passed = row.Rule.Condition is null || _flows.Evaluate(row.Rule.Condition);
        if (!passed)
        {
            Status = $"「{row.Rule.Name}」的条件现在不成立：什么也没做。";
            return;
        }
        var ok = await _flows.RunRuleAsync(row.Rule.Id);
        Status = ok ? $"「{row.Rule.Name}」跑完了。" : "没有跑成：规则可能没写全，或刚触发得太密被临时拦下了。";
    }

    private async Task SaveAndRefreshAsync(string message)
    {
        await _flows.SaveAsync();
        _flows.EnsureTicker();
        Refresh();
        Status = message;
    }

    // ==================== 对话框小工具 ====================

    /// <summary>单行输入；取消返回 null，留空返回 ""（调用方自己判要不要空）。</summary>
    private static async Task<string?> AskAsync(string title, string message, string initial = "")
    {
        var text = await Shell.Current.DisplayPromptAsync(title, message, "确定", "取消", initialValue: initial);
        return text?.Trim();
    }

    /// <summary>选项表；取消返回 -1。</summary>
    private static async Task<int> PickAsync(string title, string[] options)
    {
        var pick = await Shell.Current.DisplayActionSheet(title, "取消", null, options);
        if (pick is null) return -1;
        var index = Array.IndexOf(options, pick);
        return index;
    }

    private static async Task<string?> PickFromAsync(string title, string[] options)
    {
        var index = await PickAsync(title, options);
        return index < 0 ? null : options[index];
    }
}

/// <summary>列表里的一行：把规则翻成三行人话。</summary>
public sealed class FlowRowVm
{
    public FlowRowVm(FlowRule rule) => Rule = rule;

    public FlowRule Rule { get; }

    public string Name => Rule.Name;
    public bool Enabled => Rule.Enabled;
    public string EnabledLabel => Rule.Enabled ? "● 启用" : "○ 停用";
    public string TriggerText => Rule.TriggerKey.Trim().Length == 0
        ? FlowTriggers.Display(Rule.Trigger)
        : $"{FlowTriggers.Display(Rule.Trigger)}（{Rule.TriggerKey}）";
    public string WhenText => "当 " + (Rule.Condition?.Describe() ?? "只要触发");
    public string ThenText => "那么 " + string.Join("；", Rule.Actions.Select(a => a.Describe()));
}
