using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Tools;

/// <summary>
/// 自定义状态工具：AI 读得出「现在灯是开还是关」，也改得动——流程图那边读的是同一份值。
/// 名字与值都不翻译：用户怎么起的（「客厅的灯」），AI 就照那个名字说。
/// </summary>
public sealed partial class ToolManager
{
    private void RegisterStateTools()
    {
        RegisterBuiltin("state_read",
            "读取当前可见的自定义状态（灯开/灯关这类开关），按「楼层 > 场景 > 全局」就近取。"
            + "不给 key 就列出现在能看到的全部；给 key 只读那一条，并说明它是哪个作用域的。"
            + "判断场景该不该发生什么之前，先读一眼当前值。",
            args =>
            {
                var state = GetStateService();
                if (state is null) return Task.FromResult(Fail("状态服务不可用"));
                var key = GetString(args, "key", "").Trim();
                if (key.Length > 0)
                {
                    var def = state.Resolve(key);
                    if (def is null) return Task.FromResult(Fail($"现在看不到「{key}」：可能不在它所在的场景/楼层，也可能是名字不对。"));
                    return Task.FromResult(JsonSerializer.Serialize(new
                    {
                        ok = true,
                        key = def.Key,
                        value = def.Value,
                        values = def.Values,
                        scope = StateScopes.Display(def.Scope),
                        scene = state.Map.SceneById(def.SceneId)?.Name ?? "",
                        note = def.Note,
                    }));
                }
                var list = state.Visible().Select(d => new
                {
                    key = d.Key,
                    value = d.Value,
                    values = d.Values,
                    scope = StateScopes.Display(d.Scope),
                    show_button = d.ShowButton,
                }).ToList();
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    ok = true,
                    where = state.Scene is { } s ? s.Name : "",
                    count = list.Count,
                    states = list,
                    hint = list.Count == 0
                        ? "现在一个自定义状态都没有。想用就直接 state_set 新建一个（给 values，如「开/关」）。"
                        : "改值用 state_set；改了之后流程图与主界面按钮都会立刻跟着变。",
                }));
            },
            new List<ToolParameter>
            {
                new() { Name = "key", Description = "状态名（如「客厅的灯」）；不给就列全部", Required = false }
            });

        RegisterBuiltin("state_set",
            "改一个自定义状态的值（开关类：开/关、锁/解锁）。"
            + "action=set 按 value 改；action=toggle 走到下一个值。"
            + "这条状态还不存在时，给 values（如「开/关」或数组）就会当场新建，作用域默认挂在你现在所在的场景。"
            + "改完值会落盘，流程图与主界面按钮随之更新。",
            async args =>
            {
                var state = GetStateService();
                if (state is null) return Fail("状态服务不可用");
                var key = GetString(args, "key", "").Trim();
                if (key.Length == 0) return Fail("需要 key（状态名）");

                var action = GetString(args, "action", "set").Trim().ToLowerInvariant();
                var def = state.Resolve(key);

                if (def is null)
                {
                    var values = StateValuesFrom(args);
                    if (values.Count == 0)
                        return Fail($"现在没有「{key}」。要新建就同时给 values（例如「开/关」）。");
                    def = await state.EnsureAsync(key, values);
                    if (def is null) return Fail("新建状态失败");
                    if (action != "toggle")
                    {
                        var first = GetString(args, "value", "").Trim();
                        if (first.Length > 0 && def.Values.Contains(first))
                        {
                            await state.SetAsync(key, first);
                            return JsonSerializer.Serialize(new { ok = true, created = true, key, value = first, values = def.Values });
                        }
                    }
                    return JsonSerializer.Serialize(new
                    {
                        ok = true,
                        created = true,
                        key,
                        value = def.Value,
                        values = def.Values,
                        scope = StateScopes.Display(def.Scope),
                    });
                }

                if (action == "toggle")
                {
                    var next = await state.ToggleAsync(key);
                    return JsonSerializer.Serialize(new { ok = next.Length > 0, key, value = next, values = def.Values });
                }

                var value = GetString(args, "value", "").Trim();
                if (value.Length == 0) return Fail("需要 value（要改成的那个值）");
                if (!def.Values.Contains(value))
                    return Fail($"「{key}」只能是：{string.Join(" / ", def.Values)}");
                var previous = def.Value;
                var changed = await state.SetAsync(key, value);
                return JsonSerializer.Serialize(new { ok = changed, key, value, previous, values = def.Values });
            },
            new List<ToolParameter>
            {
                new() { Name = "key", Description = "状态名（如「客厅的灯」）", Required = true },
                new() { Name = "action", Description = "set（按 value 改）/ toggle（走到下一个）", Required = false },
                new() { Name = "value", Description = "要改成的新值；action=set 时必给", Required = false },
                new() { Name = "values", Description = "新建时给可选值，数组或「开/关」", Required = false }
            });
    }

    private static StateService? GetStateService()
        => ServicesHelper.GetServices()?.GetService(typeof(StateService)) as StateService;

    private static string Fail(string message) => JsonSerializer.Serialize(new { ok = false, message });

    /// <summary>可选值：认 JSON 数组，也认「开/关」「开、关」这种人写的串。</summary>
    private static List<string> StateValuesFrom(string? json)
    {
        if (TryGetStringList(json, "values", out var list) && list is not null && list.Count > 0)
            return list;
        var text = GetString(json, "values", "");
        return text.Split(new[] { '/', '、', '，', ',', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
