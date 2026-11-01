using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Tools;

/// <summary>
/// 流程图工具：AI 能把「天黑了就把门口那盏灯打开」直接写成规则，也能读回现在有哪几条。
/// 条件与动作都是结构化的小对象（见 FlowCondition / FlowAction），写入前一律校验；
/// 写得半懂不懂就退回来问清楚，不往存档里塞半截规则。
/// </summary>
public sealed partial class ToolManager
{
    private static readonly JsonSerializerOptions FlowJson = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private void RegisterFlowTools()
    {
        RegisterBuiltin("flow_list",
            "列出现在所有的流程图规则：什么时候看、条件是什么、成立后做什么（用人话写的）。"
            + "想知道某条规则具体怎么触发、或者要改它之前，先用它。",
            _ =>
            {
                var flow = GetFlowService();
                if (flow is null) return Task.FromResult(Fail("流程图服务不可用"));
                var list = flow.Map.Flows.Select(r => new
                {
                    id = r.Id,
                    name = r.Name,
                    enabled = r.Enabled,
                    trigger = FlowTriggers.Display(r.Trigger),
                    trigger_key = r.TriggerKey,
                    when = r.Condition?.Describe() ?? "只要触发就做",
                    then = r.Actions.Select(a => a.Describe()).ToList(),
                    cooldown_seconds = r.CooldownSeconds,
                    valid = r.IsValid,
                }).ToList();
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    ok = true,
                    count = list.Count,
                    rules = list,
                    hint = list.Count == 0
                        ? "还没有规则。用 flow_save 写一条：name / trigger / condition / actions。"
                        : "改一条就同名再存一次（按 name 覆盖）；flow_run 可以当场试跑。",
                }));
            });

        RegisterBuiltin("flow_save",
            "新建或覆盖一条流程图规则（按 name 认；同名就是改它）。"
            + "trigger 取 scene_enter / state_change / tick / manual / traffic_light / time / sun / affection / ai_call。"
            + "trigger_key：state_change 填状态名（空=任意）、tick 填秒数、traffic_light 填路名、time 填 HH:mm、"
            + "sun 填 day/dusk/night、affection 填阈值（如 40）。"
            + "condition 是条件树 JSON：{\"op\":\"all|any|not|leaf\",\"children\":[...],\"kind\":\"state|traffic|time|sun|affection|scene\",\"key\":\"\",\"value\":\"\"}；不填=只要触发就做。"
            + "actions 是数组：[{\"kind\":\"set_state|toggle_state|move_scene|notify|run_flow\",\"key\":\"\",\"value\":\"\"}]。"
            + "动作里要改的状态还不存在时，会在当前场景顺手建一个（values 给「开/关」这类）。",
            async args =>
            {
                var flow = GetFlowService();
                if (flow is null) return Fail("流程图服务不可用");
                var name = GetString(args, "name", "").Trim();
                if (name.Length == 0) return Fail("需要 name（规则名，也是以后改它的凭据）");

                var trigger = GetString(args, "trigger", FlowTriggers.Manual).Trim().ToLowerInvariant();
                if (!IsKnownTrigger(trigger))
                    return Fail("trigger 不认识：" + trigger + "。可用：" + KnownTriggers());

                FlowCondition? condition = null;
                var rawCondition = GetRaw(args, "condition");
                if (rawCondition.Length > 0 && rawCondition != "null")
                {
                    try { condition = JsonSerializer.Deserialize<FlowCondition>(rawCondition, FlowJson); }
                    catch (Exception ex) { return Fail("condition 不是合法的条件树：" + ex.Message); }
                    if (condition is not null && !ConditionIsValid(condition))
                        return Fail("condition 里有不认识的 op/kind，或叶子少了 key/value。");
                }

                var rawActions = GetRaw(args, "actions");
                if (rawActions.Length == 0) return Fail("需要 actions（至少一个动作）");
                List<FlowAction>? actions;
                try { actions = JsonSerializer.Deserialize<List<FlowAction>>(rawActions, FlowJson); }
                catch (Exception ex) { return Fail("actions 不是合法数组：" + ex.Message); }
                if (actions is null || actions.Count == 0) return Fail("actions 至少给一个");
                foreach (var a in actions)
                {
                    if (!IsKnownAction(a.Kind)) return Fail("动作不认识：" + a.Kind);
                    if (a.Kind is FlowActionKinds.SetState or FlowActionKinds.ToggleState && a.Key.Trim().Length == 0)
                        return Fail("改状态的动作要给 key（状态名）");
                    if (a.Kind is FlowActionKinds.MoveScene or FlowActionKinds.Notify or FlowActionKinds.RunFlow
                        && a.Value.Trim().Length == 0)
                        return Fail(FlowActionKinds.Display(a.Kind) + " 要给 value");
                }

                // 动作要改的状态不存在就先建：不然规则存下去也永远改不动东西
                foreach (var a in actions)
                {
                    if (a.Kind is not (FlowActionKinds.SetState or FlowActionKinds.ToggleState)) continue;
                    if (flow.Map.States.Any(s => s.Key == a.Key)) continue;
                    var values = a.Kind == FlowActionKinds.SetState && a.Value.Trim().Length > 0
                        ? new[] { a.Value.Trim(), "关" }
                        : new[] { "开", "关" };
                    await GetStateService()!.EnsureAsync(a.Key, values);
                }

                var existing = flow.Map.Flows.FirstOrDefault(f => f.Name == name);
                var rule = existing ?? new FlowRule { Name = name };
                rule.Name = name;
                rule.Trigger = trigger;
                rule.TriggerKey = GetString(args, "trigger_key", "").Trim();
                rule.Condition = condition;
                rule.Actions = actions;
                rule.CooldownSeconds = Math.Clamp(GetInt(args, "cooldown_seconds", 0), 0, 86400);
                rule.Note = GetString(args, "note", rule.Note);
                if (GetBool(args, "enabled", true)) rule.Enabled = true;
                if (existing is null) flow.Map.Flows.Add(rule);

                await flow.SaveAsync();
                flow.EnsureTicker();
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    created = existing is null,
                    id = rule.Id,
                    name = rule.Name,
                    trigger = FlowTriggers.Display(rule.Trigger),
                    when = rule.Condition?.Describe() ?? "只要触发就做",
                    then = rule.Actions.Select(a => a.Describe()).ToList(),
                    message = existing is null ? "规则已写好。" : "规则已改好。",
                });
            });

        RegisterBuiltin("flow_delete",
            "删掉一条流程图规则（按 name）。",
            async args =>
            {
                var flow = GetFlowService();
                if (flow is null) return Fail("流程图服务不可用");
                var name = GetString(args, "name", "").Trim();
                var rule = flow.Map.FlowByName(name);
                if (rule is null) return Fail("没有这条规则：" + name);
                flow.Map.Flows.Remove(rule);
                await flow.SaveAsync();
                flow.EnsureTicker();
                return JsonSerializer.Serialize(new { ok = true, removed = rule.Name });
            });

        RegisterBuiltin("flow_run",
            "当场跑一条流程图规则（不管它写的什么触发时机），用来看效果。条件不成立就什么也不做。",
            async args =>
            {
                var flow = GetFlowService();
                if (flow is null) return Fail("流程图服务不可用");
                var name = GetString(args, "name", "").Trim();
                if (name.Length == 0) return Fail("需要 name（规则名或 id）");
                var rule = flow.Map.FlowById(name) ?? flow.Map.FlowByName(name);
                if (rule is null) return Fail("没有这条规则：" + name);
                var passed = rule.Condition is null || flow.Evaluate(rule.Condition);
                if (!passed)
                    return JsonSerializer.Serialize(new { ok = false, ran = false, when = rule.Condition?.Describe(), message = "条件不成立，什么也没做。" });
                var ok = await flow.RunRuleAsync(rule.Id);
                return JsonSerializer.Serialize(new
                {
                    ok,
                    ran = ok,
                    name = rule.Name,
                    then = rule.Actions.Select(a => a.Describe()).ToList(),
                });
            });
    }

    private static FlowService? GetFlowService()
        => ServicesHelper.GetServices()?.GetService(typeof(FlowService)) as FlowService;

    private static bool IsKnownTrigger(string t) => t is FlowTriggers.SceneEnter or FlowTriggers.StateChange
        or FlowTriggers.Tick or FlowTriggers.Manual or FlowTriggers.TrafficLight or FlowTriggers.Time
        or FlowTriggers.Sun or FlowTriggers.Affection or FlowTriggers.AiCall;

    private static string KnownTriggers() => string.Join(" / ", new[]
    {
        FlowTriggers.SceneEnter, FlowTriggers.StateChange, FlowTriggers.Tick, FlowTriggers.Manual,
        FlowTriggers.TrafficLight, FlowTriggers.Time, FlowTriggers.Sun, FlowTriggers.Affection, FlowTriggers.AiCall,
    });

    private static bool IsKnownAction(string kind) => kind is FlowActionKinds.SetState or FlowActionKinds.ToggleState
        or FlowActionKinds.MoveScene or FlowActionKinds.Notify or FlowActionKinds.RunFlow;

    private static bool ConditionIsValid(FlowCondition c)
    {
        switch (c.Op)
        {
            case FlowOps.All:
            case FlowOps.Any:
                return c.Children.Count > 0 && c.Children.All(ConditionIsValid);
            case FlowOps.Not:
                return c.Children.Count > 0 && ConditionIsValid(c.Children[0]);
            case FlowOps.Leaf:
                return c.Kind is FlowConditionKinds.State or FlowConditionKinds.Traffic
                        or FlowConditionKinds.Time or FlowConditionKinds.Sun
                        or FlowConditionKinds.Affection or FlowConditionKinds.Scene
                       && c.Value.Trim().Length > 0;
        }
        return false;
    }

    /// <summary>取原始 JSON 片段（对象/数组原样给反序列化器，字符串就当它是 JSON 文本）。</summary>
    private static string GetRaw(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(key, out var v)) return "";
            return v.ValueKind switch
            {
                JsonValueKind.String => (v.GetString() ?? "").Trim(),
                JsonValueKind.Null or JsonValueKind.Undefined => "",
                _ => v.GetRawText(),
            };
        }
        catch { return ""; }
    }
}
