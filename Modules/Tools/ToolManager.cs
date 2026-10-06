using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Tools;

/// <summary>与旧 PluginManager 一致的服务取用入口。</summary>
internal static class ServicesHelper
{
    public static IServiceProvider? GetServices() =>
        Application.Current?.Handler?.MauiContext?.Services;
}

/// <summary>
/// 工具管理器：内置工具（战斗、系统）与外部 Java/Python 工具的统一入口。
///
/// 外部工具直接以 JSON-RPC 2.0（stdin/stdout）与主进程通信，Java 与 Python 使用同一套协议，
/// 不分成两个独立区域。工具的 JavaScript 风格 schema 用 ToolDefinition 描述。
/// </summary>
public sealed class ToolManager : IDisposable
{
    private readonly RuntimeManager _runtimes;
    private readonly ConcurrentDictionary<string, ToolDefinition> _tools = new();
    private readonly ConcurrentDictionary<string, Func<string, Task<string>>> _builtins = new();
    private readonly ConcurrentDictionary<string, ToolSession> _sessions = new();
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly Modules.Sandbox.SandboxPolicy _sandbox;
    private int _rpcId;
    private bool _disposed;

    public ToolManager(RuntimeManager runtimes, Modules.Sandbox.SandboxPolicy? injectedSandbox = null)
    {
        _runtimes = runtimes;
        // 优先使用 DI 注入的共享 SandboxPolicy（设置页保存后指纹刷新对所有调用方生效）；
        // 未注入时（非 DI 路径）才自建，保证兼容。
        _sandbox = injectedSandbox ?? new Modules.Sandbox.SandboxPolicy(new StorageProvider());
        RegisterSystemTools();
        Modules.Battle.BattleTools.Register(this);
        ScanExternalTools();
#if ANDROID
        // Android APK 没有宿主 stdin，Harness JSON-RPC 循环只在桌面端监听
#else
        _ = StartHarnessLoopAsync();
#endif
    }

    /// <summary>
    /// 工具沙箱策略（敏感词过滤 + 信任名单 + 加密落盘）。
    /// 设置页 / 工具管理器通过此入口配置密钥指纹、信任与加密策略。
    /// </summary>
    public Modules.Sandbox.SandboxPolicy Sandbox => _sandbox;

    /// <summary>工具目录：{root}/tools/{工具名}/，每个工具目录含 tool.json 清单。</summary>
    public static string ToolsDir => Path.Combine(App.RootDirectory, "tools");

    // ============ 注册 / 发现 ============

    /// <summary>注册内置工具（进程内 C# 实现）。</summary>
    public void RegisterBuiltin(string name, string description, Func<string, Task<string>> handler, List<ToolParameter>? parameters = null)
    {
        _tools[name] = new ToolDefinition
        {
            Name = name,
            Description = description,
            Language = ToolLanguage.Builtin,
            Parameters = parameters ?? new List<ToolParameter>()
        };
        _builtins[name] = handler;
    }

    public List<ToolDefinition> ListTools() =>
        _tools.Values.OrderBy(t => t.Name).ToList();

    public ToolDefinition? Find(string name) =>
        _tools.TryGetValue(name, out var t) ? t : null;

    /// <summary>
    /// 以 DeepSeek Harness / OpenAI tools 契约格式输出全部工具的 schema。
    /// 数组元素：{ name, description, parameters: JSONSchema, output?: JSONSchema }，
    /// 可直接作为模型请求的 tools 字段注入（DeepSeek Harness defineTool 的模型面投影）。
    /// </summary>
    public string ListSchemas()
    {
        var tools = ListTools().Select(t => new
        {
            name = t.Name,
            description = t.Description,
            parameters = t.ParametersSchema,
            output = t.OutputSchema
        });
        return JsonSerializer.Serialize(tools);
    }

    /// <summary>供外部注入的完整 schema JSON 数组（含 parameters 的 JSONSchema 对象）。</summary>
    public List<object> BuildHarnessSchemas()
    {
        var list = new List<object>();
        foreach (var t in ListTools())
        {
            var obj = new Dictionary<string, object>
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["parameters"] = ParseSchemaNode(t.ParametersSchema, t.Parameters)
            };
            if (!string.IsNullOrWhiteSpace(t.OutputSchema))
                obj["output"] = ParseSchemaNode(t.OutputSchema, null);
            list.Add(obj);
        }
        return list;
    }

    // ============ 应用内聊天可见的工具（OpenAI function calling） ============

    /// <summary>
    /// 不暴露给应用内聊天模型的内置工具：
    ///  · send_message —— 它内部调用 MainGameViewModel.SendMessageCommand → ChatEngine.Send，
    ///    在聊天回合里调用就是重入：会话历史交错、请求翻倍，还可能一层层套下去；
    ///  · battle_* —— 服务外部 Harness/MCP 的独立战斗状态（见 BattleSystem 注释，与战斗页的
    ///    BuiltinBattleDriver 互不相干）。聊天里调用只会产生玩家在战斗页看不到的"影子战斗"，
    ///    对陪伴剧情没有意义，还白烧 token。
    /// </summary>
    private static readonly HashSet<string> ChatExcludedTools = new(StringComparer.Ordinal)
    {
        "send_message",
        "battle_start", "battle_attack", "battle_status", "battle_end", "battle_list"
    };

    /// <summary>某个工具是否可以暴露给应用内聊天：只放内置工具（外部工具要解析运行时、最长可挂 120s，聊天回合扛不住）。</summary>
    private static bool IsChatTool(ToolDefinition t) =>
        !t.IsExternal && !ChatExcludedTools.Contains(t.Name);

    /// <summary>应用内聊天可见的工具名集合：ChatEngine 用它兜住"模型凭空报了一个工具名"的情况。</summary>
    public HashSet<string> ChatToolNames() =>
        ListTools().Where(IsChatTool).Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// 应用内聊天要注入请求的 tools 数组，OpenAI function calling 线格式：
    /// <c>[{ "type":"function", "function":{ name, description, parameters } }]</c>。
    /// 只输出 name/description/parameters —— Harness 契约里的 output 扩展字段会被严格端点拒收。
    /// 没有可用工具时返回空列表（调用方据此完全不发 tools 字段）。
    /// </summary>
    public List<object> BuildChatTools()
    {
        var list = new List<object>();
        foreach (var t in ListTools().Where(IsChatTool))
        {
            list.Add(new Dictionary<string, object>
            {
                ["type"] = "function",
                ["function"] = new Dictionary<string, object>
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = ParseSchemaNode(t.ParametersSchema, t.Parameters)
                }
            });
        }
        return list;
    }

    /// <summary>
    /// 契约是"一个裸参数字符串"的内置工具。只有这几个工具在收到单参数 JSON 时需要剥壳：
    /// 它们的参数本来就是裸串（外部驱动一直这么调，如 "sink"、"客厅/夜灯"），而模型会按 schema
    /// 传 <c>{"action":"sink"}</c>。像 battle_status / battle_end 那种参数本身就是 JSON 对象的
    /// 工具必须原样透传 —— 剥了壳它们的解析器会直接报错，那是外部驱动正在用的契约。
    /// </summary>
    private static readonly HashSet<string> RawStringArgTools = new(StringComparer.Ordinal)
    {
        "set_sprite", "animate", "set_scene_option"
    };

    /// <summary>
    /// 内置工具的参数归一化：把 RawStringArgTools 的单参数 JSON 还原成裸串，两种调用方都成立，
    /// 工具实现一行都不用改。其余情况（多参数、非字符串值）原样透传，工具自己会报错。
    /// </summary>
    private static string NormalizeBuiltinArgs(ToolDefinition tool, string? argsJson)
    {
        var raw = (argsJson ?? "").Trim();
        if (raw.Length == 0 || raw[0] != '{') return raw;
        if (!RawStringArgTools.Contains(tool.Name)) return raw;
        if (tool.Parameters.Count != 1 || !tool.Parameters[0].Required) return raw;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return raw;
            var props = doc.RootElement.EnumerateObject().ToList();
            if (props.Count != 1) return raw;
            var v = props[0].Value;
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
            if (v.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                return v.GetRawText();
            return raw;
        }
        catch (Exception ex)
        {
            App.WriteLog("ToolManager.NormalizeBuiltinArgs -> " + ex.Message);
            return raw;
        }
    }

    /// <summary>
    /// 把 schema 字符串解析为 JsonNode；空或非法则按 Parameters 生成扁平 schema。
    /// 返回类型是 JsonNode（不是 JsonNode?）：它永远给得出一份有效 schema，
    /// 调用方把它放进 object 字典时不必再为空值辩护。
    /// </summary>
    private static JsonNode ParseSchemaNode(string? schemaJson, List<ToolParameter>? parameters)
    {
        if (!string.IsNullOrWhiteSpace(schemaJson))
        {
            try
            {
                var parsed = JsonNode.Parse(schemaJson);
                if (parsed is not null) return parsed;
            }
            catch { }
        }
        if (parameters is not null && parameters.Count > 0)
        {
            var props = new JsonObject();
            var required = new JsonArray();
            foreach (var p in parameters)
            {
                props[p.Name] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = p.Description
                };
                if (p.Required) required.Add(p.Name);
            }
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = props,
                ["required"] = required,
                ["additionalProperties"] = false
            };
        }
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
            ["required"] = new JsonArray(),
            ["additionalProperties"] = false
        };
    }

    /// <summary>扫描 {root}/tools/ 下的外部工具目录（tool.json 清单）。</summary>
    private void ScanExternalTools()
    {
        try
        {
            if (!Directory.Exists(ToolsDir)) return;
            foreach (var dir in Directory.GetDirectories(ToolsDir))
            {
                var manifestPath = Path.Combine(dir, "tool.json");
                if (!Directory.Exists(dir) || !File.Exists(manifestPath)) continue;
                try
                {
                    var manifest = JsonSerializer.Deserialize<ExternalToolManifest>(File.ReadAllText(manifestPath));
                    if (manifest is null || string.IsNullOrWhiteSpace(manifest.Name)) continue;
                    var language = manifest.Language?.ToLowerInvariant() switch
                    {
                        "python" => ToolLanguage.Python,
                        "java" => ToolLanguage.Java,
                        _ => ToolLanguage.Builtin
                    };
                    if (language == ToolLanguage.Builtin) continue;
                    _tools[manifest.Name.Trim()] = new ToolDefinition
                    {
                        Name = manifest.Name.Trim(),
                        Description = manifest.Description ?? "",
                        Language = language,
                        Entry = string.IsNullOrWhiteSpace(manifest.Entry) ? (language == ToolLanguage.Java ? "tool.jar" : "main.py") : manifest.Entry,
                        WorkDir = dir,
                        ParametersSchema = manifest.Schema,
                        OutputSchema = manifest.OutputSchema
                    };
                }
                catch (Exception ex)
                {
                    App.WriteLog("ToolManager.ScanExternalTools(" + dir + ") -> " + ex);
                }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("ToolManager.ScanExternalTools -> " + ex);
        }
    }

    // ============ 执行 ============

    /// <summary>按工具名执行工具。argsJson 为 JSON 参数字符串或空。</summary>
    public async Task<string> ExecuteAsync(string name, string argsJson)
    {
        if (!_tools.TryGetValue(name, out var tool))
            return JsonSerializer.Serialize(new { error = $"未知工具：{name}，可用工具：{string.Join(", ", _tools.Keys.OrderBy(k => k))}" });

        try
        {
            if (tool.IsExternal is false && _builtins.TryGetValue(name, out var handler))
                return await handler(NormalizeBuiltinArgs(tool, argsJson));

            // 外部工具：沙箱网关（敏感词过滤 + 信任名单 + 防篡改审计）
            var toolPath = tool.WorkDir;
            var gate = await _sandbox.GateInputAsync(name, toolPath, argsJson ?? "", isExternal: true);
            if (!gate.Allowed)
                return JsonSerializer.Serialize(new { error = gate.BlockReason });

            // 确保运行时可用
            var resolved = await _runtimes.ResolveRuntimeAsync(tool.Language);
            if (resolved is null)
                return JsonSerializer.Serialize(new { error = $"{tool.RuntimeNeeded}。可在设置页 -> 工具模式中「一键下载」或手动配置路径" });

            var session = await GetSessionAsync(tool, resolved.Value.Exe, resolved.Value.Args);
            object? parameters = null;
            if (!string.IsNullOrWhiteSpace(gate.MaskedArgs))
            {
                try { parameters = JsonDocument.Parse(gate.MaskedArgs).RootElement; }
                catch { parameters = gate.MaskedArgs; }
            }
            var raw = await session.CallAsync(parameters);
            return _sandbox.GateOutput(name, raw);
        }
        catch (Exception ex)
        {
            App.WriteLog("ToolManager.ExecuteAsync(" + name + ") -> " + ex);
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    private async Task<ToolSession> GetSessionAsync(ToolDefinition tool, string exe, string args)
    {
        await _sessionLock.WaitAsync();
        try
        {
            // 崩溃后自动重启
            if (_sessions.TryGetValue(tool.Name, out var s) && s?.IsAlive == true)
                return s;
            _sessions.TryRemove(tool.Name, out _);
            var fresh = ToolSession.Start(tool, exe, args);
            if (fresh is null)
                throw new InvalidOperationException($"无法启动外部工具进程（{exe}）");
            _sessions[tool.Name] = fresh;
            return fresh;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    // ============ 内置系统工具（原插件系统默认能力） ============

    private void RegisterSystemTools()
    {
        // 获取游戏状态
        RegisterBuiltin("get_state", "获取当前游戏状态（角色、好感、精力、所在地点）", async args =>
        {
            var services = ServicesHelper.GetServices();
            if (services is null) return JsonSerializer.Serialize(new { error = "无法获取服务" });
            var engine = services.GetService(typeof(GameEngine)) as GameEngine;
            if (engine is null) return JsonSerializer.Serialize(new { error = "GameEngine 不可用" });
            var vm = services.GetService(typeof(ViewModels.MainGameViewModel)) as ViewModels.MainGameViewModel;
            var charData = engine.Roster.TryGetValue(engine.State.CharacterId, out var ch) ? ch : null;
            return JsonSerializer.Serialize(new
            {
                character = engine.State.CharacterId,
                characterName = vm?.CharacterName ?? "未知",
                affection = charData?.State.Affection ?? 0,
                trust = charData?.State.Trust ?? 0,
                energy = charData?.State.Energy ?? 100,
                location = engine.State.Location,
                isSpeaking = vm?.IsSpeaking,
                messageCount = vm?.Messages.Count ?? 0
            });
        });

        // 发送消息到主界面聊天
        RegisterBuiltin("send_message", "向游戏里的角色发送一条消息", async args =>
        {
            var services = ServicesHelper.GetServices();
            if (services is null) return "错误：服务不可用";
            var vm = services.GetService(typeof(ViewModels.MainGameViewModel)) as ViewModels.MainGameViewModel;
            if (vm is null) return "错误：MainGameViewModel 不可用";
            var text = args?.Trim() ?? "";
            if (string.IsNullOrEmpty(text)) return "错误：消息为空";
            vm.InputText = text;
            await vm.SendMessageCommand.ExecuteAsync(null);
            return "消息已发送";
        });

        // 设置立绘（位置/透明度/坐标）
        // 声明 spec 参数是为了让模型知道要传什么：内置工具的契约仍是裸字符串，
        // 单参数 JSON 由 NormalizeBuiltinArgs 还原成裸串（外部驱动照旧直接传 "left"）。
        RegisterBuiltin("set_sprite", "设置角色立绘：参数为 \"位置[,x,y]\" 或 \"opacity:0.5\" 形式", async args =>
        {
            var services = ServicesHelper.GetServices();
            if (services is null) return "错误：服务不可用";
            var vm = services.GetService(typeof(ViewModels.MainGameViewModel)) as ViewModels.MainGameViewModel;
            if (vm is null) return "错误：MainGameViewModel 不可用";
            var arg = args?.Trim() ?? "";
            try
            {
                if (arg.StartsWith("opacity:", StringComparison.OrdinalIgnoreCase))
                {
                    var o = Math.Clamp(double.Parse(arg["opacity:".Length..].Trim()), 0, 1);
                    vm.SpriteOpacity = o;
                    return $"立绘透明度已设置为 {o}";
                }
                var parts = arg.Split(',');
                if (parts.Length >= 1)
                {
                    var pos = parts[0].Trim().ToLower();
                    vm.SpritePosition = pos switch { "left" => "left", "right" => "right", _ => "center" };
                }
                if (parts.Length >= 2 && double.TryParse(parts[1].Trim(), out var x)) vm.SpriteX = x;
                if (parts.Length >= 3 && double.TryParse(parts[2].Trim(), out var y)) vm.SpriteY = y;
                return "立绘已设置";
            }
            catch (Exception ex)
            {
                return $"错误：{ex.Message}";
            }
        },
            new List<ToolParameter>
            {
                new() { Name = "spec", Description = "位置[,x,y]，如 \"left\"、\"right\"、\"left,0.1,0.2\"；或用 \"opacity:0.5\" 设透明度", Required = true }
            });

        // 播放动画
        RegisterBuiltin("animate", "播放立绘动画：sink(下沉) / jump(跳跃) / shake(颤抖)", async args =>
        {
            var services = ServicesHelper.GetServices();
            if (services is null) return "错误：服务不可用";
            var vm = services.GetService(typeof(ViewModels.MainGameViewModel)) as ViewModels.MainGameViewModel;
            if (vm is null) return "错误：MainGameViewModel 不可用";
            var action = (args ?? "").Trim().ToLower();
            if (action == "sink") { vm.SinkAnimationAsync(); return "下沉动画已开始"; }
            if (action == "jump") { vm.JumpAnimationAsync(); return "跳跃动画已开始"; }
            if (action == "shake") { vm.ShakeAnimationAsync(); return "颤抖动画已开始"; }
            return $"未知动画：{action}，可用: sink, jump, shake";
        },
            new List<ToolParameter>
            {
                new() { Name = "action", Description = "sink=下沉 / jump=跳跃 / shake=颤抖", Required = true }
            });

        // 场景选项列表：AI 必须先取表再选（选项随场景/时段变化，没选项或看不出变化都会说明）
        RegisterBuiltin("get_scene_options",
            "查看当前场景此刻可用的场景/氛围选项（含「能否切换」「切换后画面是否看得出变化」）。"
            + "切换场景前必须先调用本工具拿到选项名，不要凭想象直接切换。",
            _ =>
            {
                var director = ResolveSceneDirector();
                if (director is null)
                    return Task.FromResult(JsonSerializer.Serialize(new { error = "场景导演不可用" }));
                return Task.FromResult(JsonSerializer.Serialize(BuildSceneOptionsPayload(director)));
            });

        // 应用场景选项：返回值必须带上「是否真的产生了视觉变化」，否则 AI 会以为自己成功了
        RegisterBuiltin("set_scene_option",
            "应用一个场景选项，参数 spec 取自 get_scene_options 的 spec 字段（形如「客厅/夜灯」或「客厅」）。"
            + "返回 visible_change=false 表示已切换但画面看不出变化（例如白天开灯、或本来就是该状态），"
            + "此时要如实说明，不要宣称画面已经变了，也不要反复重试。"
            + "选了一个不在生效时段内的选项（get_scene_options 的 out_of_window）也会照切，"
            + "但 message 里会注明它不符合当前时段——把这句话转达给用户，不要隐瞒。",
            async args =>
            {
                var director = ResolveSceneDirector();
                if (director is null)
                    return JsonSerializer.Serialize(new { error = "场景导演不可用" });
                var spec = ExtractSceneSpec(args);
                if (spec.Length == 0)
                    return JsonSerializer.Serialize(new { error = "缺少参数 spec（请先调用 get_scene_options 取选项名）" });

                var result = await director.ApplySpecAsync(spec, Modules.Scene.SceneApplySource.Ai);
                var opt = result.Option;
                return JsonSerializer.Serialize(new
                {
                    status = result.Status,
                    applied = result.Applied,
                    visible_change = result.VisibleChange,
                    spec,
                    resolved = opt?.Spec,
                    name = opt?.Title,
                    message = result.Message,
                    dim = opt is null ? (double?)null : director.ActiveDim
                });
            },
            new List<ToolParameter>
            {
                new() { Name = "spec", Description = "选项名，取自 get_scene_options 的 spec 字段", Required = true }
            });
    }

    /// <summary>取场景导演（与 get_state 等内置工具一致，走服务定位而不是构造注入）。</summary>
    private static Modules.Scene.SceneDirector? ResolveSceneDirector()
    {
        var services = ServicesHelper.GetServices();
        return services?.GetService(typeof(Modules.Scene.SceneDirector)) as Modules.Scene.SceneDirector;
    }

    /// <summary>get_scene_options 的返回体：可用选项 + 不可用选项（附理由）+ 当前生效态。</summary>
    private static object BuildSceneOptionsPayload(Modules.Scene.SceneDirector director)
    {
        var now = DateTime.Now;
        var options = director.BuildOptions(now);
        var available = options.Where(o => o.Available).ToList();
        var blocked = options.Where(o => !o.Available).ToList();
        var active = director.Active;
        return new
        {
            current_time = now.ToString("HH:mm"),
            active = active is null ? null : new
            {
                spec = director.ActiveMode is null ? active.Name : $"{active.Name}/{director.ActiveMode.Name}",
                name = director.ActiveMode is null ? active.Name : $"{active.Name} · {director.ActiveMode.Name}",
                source = director.Source.ToString(),
                dim = Math.Round(director.ActiveDim, 2)
            },
            available_count = available.Count,
            options = available.Select(o => new
            {
                spec = o.Spec,
                name = o.Title,
                already_active = o.AlreadyActive,
                visible_change = o.VisibleChange,
                change = o.ChangeDetail,
                condition = o.AvailableReason
            }).ToList(),
            // 这些不是"不可用"，而是"不在它自己的生效时段内"。仍然可以切，
            // 但结果里的 message 会如实告诉你它此刻通常不该生效。
            out_of_window = blocked.Select(o => new { spec = o.Spec, reason = o.UnavailableReason }).ToList(),
            note = available.Count == 0
                ? "此刻没有任何选项处于自己的生效时段内。若用户明确要求，你仍可挑一个切；结果会说明它不符合当前时段。"
                : "只能从 options 里挑 spec。visible_change=false 的选项切换后画面不会变（例如白天开灯），如实说明即可。"
        };
    }

    /// <summary>
    /// 取 set_scene_option 的 spec 参数：既接受 {"spec":"..."} 的 JSON，也接受直接传选项名。
    /// </summary>
    private static string ExtractSceneSpec(string? argsJson)
    {
        var raw = (argsJson ?? "").Trim();
        if (raw.Length == 0) return "";
        if (raw[0] is '{')
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("spec", out var s) && s.ValueKind == JsonValueKind.String)
                    return s.GetString()?.Trim() ?? "";
            }
            catch (Exception ex)
            {
                App.WriteLog("ToolManager.ExtractSceneSpec -> " + ex.Message);
            }
            return "";
        }
        return raw.Trim('"', '\'').Trim();
    }

    // ============ 外部 Harness 服务循环（JSON-RPC over app stdin/stdout） ============

    /// <summary>
    /// 与外部驱动（Harness）的通信循环：从应用标准输入读 JSON-RPC，执行后写回标准输出。
    /// 可用方法：tools/list 列出全部工具；工具名直接调用（params 为工具参数）。
    /// </summary>
    private async Task StartHarnessLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested && Console.In is not null)
            {
                string? line = await Console.In.ReadLineAsync();
                if (line is null)
                {
                    // null = 标准输入已关闭（桌面端没有控制台时就是这种情况）。原来这里 continue 会
                    // 立刻再次读到 null，空转烧满一个核；EOF 意味着宿主已走，直接退出循环。
                    App.WriteLog("ToolManager.HarnessLoop：标准输入已关闭，停止工具服务循环");
                    break;
                }
                if (string.IsNullOrWhiteSpace(line)) continue;
                var response = await ProcessRpcAsync(line);
                if (Console.Out is null) continue;
                Console.Out.WriteLine(response);
                Console.Out.Flush();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            App.WriteLog("ToolManager.HarnessLoop -> " + ex);
        }
    }

    private async Task<string> ProcessRpcAsync(string line)
    {
        var id = Interlocked.Increment(ref _rpcId);
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var method = root.TryGetProperty("method", out var m) ? m.GetString() ?? "" : "";
            var methodId = root.TryGetProperty("id", out var i) ? i.ToString() : id.ToString();
            var parameters = root.TryGetProperty("params", out var p) ? p : default;

            if (method == "tools.list" || method == "list_tools")
            {
                var list = ListTools().Select(t => new
                {
                    name = t.Name,
                    description = t.Description,
                    language = t.Language.ToString(),
                    parameters = t.Parameters.Select(pm => new { pm.Name, pm.Description, pm.Required })
                });
                return RpcResult(methodId, JsonSerializer.SerializeToElement(list));
            }

            if (method == "tools.schema" || method == "list_schemas")
            {
                // DeepSeek Harness / OpenAI tools 契约：可注入模型请求 tools 字段的 schema 数组
                var schemaJson = ListSchemas();
                return RpcResult(methodId, JsonDocument.Parse(schemaJson).RootElement.Clone());
            }

            if (method.StartsWith("tool.", StringComparison.Ordinal) || _tools.ContainsKey(method))
            {
                var toolName = method.StartsWith("tool.", StringComparison.Ordinal) ? method["tool.".Length..] : method;
                var argsJson = parameters.ValueKind == JsonValueKind.Undefined ? "" : parameters.GetRawText();
                var result = await ExecuteAsync(toolName, argsJson);
                return RpcResult(methodId, JsonSerializer.SerializeToElement(result));
            }

            return RpcResult(methodId, JsonSerializer.SerializeToElement(new { error = $"未知方法：{method}" }));
        }
        catch (Exception ex)
        {
            return RpcResult(id.ToString(), JsonSerializer.SerializeToElement(new { error = ex.Message }));
        }
    }

    private static string RpcResult(string id, JsonElement result)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            w.WriteString("id", id);
            w.WritePropertyName("result");
            result.WriteTo(w);
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        foreach (var s in _sessions.Values) s.Dispose();
        _sessions.Clear();
    }
}

/// <summary>外部工具清单 tool.json 的结构。</summary>
public sealed class ExternalToolManifest
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>python 或 java。</summary>
    public string Language { get; set; } = "python";
    public string Entry { get; set; } = "";
    /// <summary>可选：参数 JSON Schema（DeepSeek Harness 契约）；缺省时由内置参数列表生成。</summary>
    public string? Schema { get; set; }
    /// <summary>可选：输出 JSON Schema。</summary>
    public string? OutputSchema { get; set; }
}