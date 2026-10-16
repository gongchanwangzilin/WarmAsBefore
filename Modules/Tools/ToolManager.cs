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
    private readonly SkillLibrary? _skills;
    private int _rpcId;
    private bool _disposed;

    public ToolManager(RuntimeManager runtimes, Modules.Sandbox.SandboxPolicy? injectedSandbox = null,
        SkillLibrary? skills = null)
    {
        _runtimes = runtimes;
        _skills = skills;
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
    private static bool IsChatTool(ToolDefinition t)
    {
        if (t.IsExternal || ChatExcludedTools.Contains(t.Name)) return false;
        // 战斗系统在设置里被关闭时，battle_* 工具整体不提供给 AI（连"能看见但不能用"都不留，
        // 否则模型会反复尝试调用被拒的工具，浪费轮次）。
        if (t.Name.StartsWith("battle_", StringComparison.Ordinal) && !BattleSystemEnabled()) return false;
        return true;
    }

    /// <summary>读设置里的战斗总开关；设置服务不可用时默认可用，避免误伤。</summary>
    private static bool BattleSystemEnabled()
    {
        try
        {
            var settings = ServicesHelper.GetServices()?.GetService(typeof(Services.SettingsManager))
                as Services.SettingsManager;
            return settings?.Current.BattleEnabled ?? true;
        }
        catch { return true; }
    }

    /// <summary>应用内聊天可见的工具名集合：ChatEngine 用它兜住"模型凭空报了一个工具名"的情况。</summary>
    public HashSet<string> ChatToolNames() =>
        ListTools().Where(IsChatTool).Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

    // ============ 工具完整性检查（#2） ============

    /// <summary>
    /// 期望存在的基础工具集（名字 + 用途）。完整性检查据此核对「该有的工具是不是都在」。
    /// 新增内置工具时同步补到这里，否则检查会漏报。
    /// 注意：战斗工具在设置里被关闭时不参与检查（那是按配置隐藏，不是缺失）。
    /// </summary>
    private static readonly (string Name, string Purpose)[] ExpectedTools =
    {
        ("get_state", "读取当前游戏状态"),
        ("send_message", "向角色发送消息"),
        ("set_sprite", "设置立绘位置/透明度"),
        ("animate", "播放立绘动画"),
        ("get_scene_options", "列出当前可用场景选项"),
        ("set_scene_option", "应用一个场景选项"),
        ("add_wechat", "把角色加为微信好友"),
        ("list_wechat_contacts", "列出微信好友"),
        ("search_memory", "在对话记忆库中搜索过去"),
        ("tag_memory", "给记得的事打标签"),
        ("search_by_tags", "按标签或标签组合检索记忆"),
        ("list_skills", "列出可用的能力包"),
        ("read_skill", "读取能力包的说明书"),
        ("set_blackout", "把画面黑掉或恢复（仍可对话）"),
        ("send_to_user", "给用户手机真的发一条消息（默认需确认）"),
        ("list_actors", "列出地图实体位置"),
        ("pull_actor", "拉拽实体到场景"),
        ("battle_start", "发起战斗"),
        ("battle_attack", "战斗攻击"),
        ("battle_status", "战斗状态"),
        ("battle_end", "结束战斗"),
        ("battle_list", "列出战斗")
    };

    /// <summary>单个工具的问题描述（缺失 / 描述为空 / schema 解析失败 / 重复注册）。</summary>
    public sealed record ToolIssue(string Tool, string Severity, string Detail);

    /// <summary>完整性检查结果。</summary>
    public sealed record ToolIntegrityReport(
        int Total,
        int ChatVisible,
        int External,
        List<string> Missing,
        List<ToolIssue> Issues,
        bool Healthy)
    {
        /// <summary>一行摘要（可直接显示/写日志）。</summary>
        public string Summary()
        {
            if (Healthy)
                return $"工具完整性正常：共 {Total} 个（聊天可见 {ChatVisible}，外部 {External}）。";
            var parts = new List<string>();
            if (Missing.Count > 0) parts.Add($"缺失 {Missing.Count} 个：{string.Join("、", Missing)}");
            if (Issues.Count > 0) parts.Add($"问题 {Issues.Count} 条：" + string.Join("；", Issues.Select(i => $"{i.Tool}({i.Severity}:{i.Detail})")));
            return $"工具完整性问题：共 {Total} 个。" + string.Join(" ", parts);
        }
    }

    /// <summary>
    /// 真正核验工具清单：期望工具是否齐全、描述是否为空、参数 schema 是否能被解析成合法 JSON Schema、
    /// 聊天工具集合是否可构建（命中不该暴露的工具则报问题）。返回结构化报告，健康与否一眼可见。
    /// </summary>
    public ToolIntegrityReport VerifyIntegrity()
    {
        var tools = ListTools();
        var byName = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var issues = new List<ToolIssue>();
        var missing = new List<string>();
        var battleOn = BattleSystemEnabled();

        foreach (var (name, purpose) in ExpectedTools)
        {
            // 战斗工具在关闭时被有意隐藏，不算缺失。
            var isBattle = name.StartsWith("battle_", StringComparison.Ordinal);
            if (isBattle && !battleOn) continue;
            if (!byName.ContainsKey(name)) missing.Add($"{name}（{purpose}）");
        }

        foreach (var t in tools)
        {
            if (string.IsNullOrWhiteSpace(t.Name))
                issues.Add(new ToolIssue("(空名)", "error", "工具名为空"));
            if (string.IsNullOrWhiteSpace(t.Description))
                issues.Add(new ToolIssue(t.Name, "warn", "描述为空，模型看不到该工具的作用"));

            // 参数 schema 必须能解析成对象（否则模型请求会带坏 tools 数组）。
            if (!string.IsNullOrWhiteSpace(t.ParametersSchema))
            {
                try { _ = ParseSchemaNode(t.ParametersSchema, null); }
                catch (Exception ex) { issues.Add(new ToolIssue(t.Name, "error", $"参数 schema 解析失败：{ex.Message}")); }
            }
            // 声明了参数却没给 schema 时，检查每个参数的 name/description 是否完整。
            foreach (var p in t.Parameters)
            {
                if (string.IsNullOrWhiteSpace(p.Name))
                    issues.Add(new ToolIssue(t.Name, "warn", "有参数未命名"));
                else if (string.IsNullOrWhiteSpace(p.Description))
                    issues.Add(new ToolIssue(t.Name, "warn", $"参数 {p.Name} 无描述"));
            }
        }

        // 可构建聊天工具表 = 聊天链路不会被坏数据打断。
        HashSet<string> chatNames;
        try { chatNames = ChatToolNames(); }
        catch (Exception ex)
        {
            chatNames = new HashSet<string>(StringComparer.Ordinal);
            issues.Add(new ToolIssue("(chat)", "error", $"聊天工具集构建失败：{ex.Message}"));
        }
        // 不该暴露给聊天的工具若混进来，也报出来。
        foreach (var bad in ChatExcludedTools)
            if (chatNames.Contains(bad))
                issues.Add(new ToolIssue(bad, "error", "本应排除却出现在聊天可见工具里"));

        var healthy = missing.Count == 0 && issues.All(i => i.Severity != "error");
        return new ToolIntegrityReport(
            Total: tools.Count,
            ChatVisible: chatNames.Count,
            External: tools.Count(t => t.IsExternal),
            Missing: missing,
            Issues: issues,
            Healthy: healthy);
    }

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

            // Skills：启用了可执行入口的 skill 目录也按外部工具注册（说明书那部分走 read_skill）
            var skillsDir = SkillLibrary.SkillsDir;
            if (_skills is not null && Directory.Exists(skillsDir))
            {
                var enabled = _skills.EnabledAsync().GetAwaiter().GetResult();
                foreach (var skill in enabled.Where(s => s.HasExecutable))
                {
                    var manifestPath = Path.Combine(skill.Dir, "tool.json");
                    try
                    {
                        var manifest = JsonSerializer.Deserialize<ExternalToolManifest>(File.ReadAllText(manifestPath));
                        if (manifest is null) continue;
                        var language = manifest.Language?.ToLowerInvariant() switch
                        {
                            "python" => ToolLanguage.Python,
                            "java" => ToolLanguage.Java,
                            _ => ToolLanguage.Builtin
                        };
                        if (language == ToolLanguage.Builtin) continue;
                        var toolName = string.IsNullOrWhiteSpace(manifest.Name) ? $"skill_{skill.Name}" : manifest.Name.Trim();
                        _tools[toolName] = new ToolDefinition
                        {
                            Name = toolName,
                            Description = string.IsNullOrWhiteSpace(manifest.Description) ? skill.Description : manifest.Description!,
                            Language = language,
                            Entry = string.IsNullOrWhiteSpace(manifest.Entry)
                                ? (language == ToolLanguage.Java ? "tool.jar" : "main.py")
                                : manifest.Entry,
                            WorkDir = skill.Dir,
                            ParametersSchema = manifest.Schema,
                            OutputSchema = manifest.OutputSchema
                        };
                    }
                    catch (Exception ex)
                    {
                        App.WriteLog("ToolManager.ScanSkills(" + skill.Name + ") -> " + ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("ToolManager.ScanExternalTools -> " + ex);
        }
    }

    /// <summary>
    /// 人设里的 skill 清单（只有名字与用途）：渐进加载——正文等 AI 主动 read_skill 时才进上下文。
    /// </summary>
    public string SkillHintText => _skillHint;
    private volatile string _skillHint = "";

    /// <summary>刷新 skill 清单缓存（启动时与导入/删除/启停之后调用）。</summary>
    public async Task RefreshSkillHintAsync()
    {
        if (_skills is null) { _skillHint = ""; return; }
        try { _skillHint = await _skills.HintTextAsync(); }
        catch (Exception ex) { App.WriteLog("ToolManager.RefreshSkillHint -> " + ex.Message); }
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

        // 加微信：把剧情里用过的角色库角色变成微信常驻好友（加上后才出现在微信联系人里）
        RegisterBuiltin("add_wechat",
            "把一个角色加为微信好友。角色必须已经出现在角色库中（即剧情里真的用过这个角色）。"
            + "加成功后该角色会成为常驻好友，出现在手机的微信联系人列表里，随时可以点进去聊天。"
            + "重复添加同一个角色是安全的（幂等），不会报错。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                if (services is null) return JsonSerializer.Serialize(new { ok = false, message = "服务不可用" });
                var wechat = services.GetService(typeof(Services.WeChatService)) as Services.WeChatService;
                if (wechat is null) return JsonSerializer.Serialize(new { ok = false, message = "WeChatService 不可用" });
                var name = (args ?? "").Trim();
                if (name.Length == 0)
                    return JsonSerializer.Serialize(new { ok = false, message = "缺少参数 character（要加微信的角色名或 Id）" });

                var (ok, message, added) = await wechat.AddContactAsync(name);
                return JsonSerializer.Serialize(new { ok, message, added });
            },
            new List<ToolParameter>
            {
                new() { Name = "character", Description = "角色名或角色 Id（需已在角色库中出现过）", Required = true }
            });

        // 查微信好友：AI 想知道自己微信里现在有哪些人，先调用本工具，不要凭记忆猜
        RegisterBuiltin("list_wechat_contacts",
            "列出当前已经在微信里的常驻好友（用 add_wechat 加过的角色）。"
            + "在回应「谁在微信里」这类问题前先调用它，避免凭记忆编造。",
            async _ =>
            {
                var services = ServicesHelper.GetServices();
                if (services is null) return JsonSerializer.Serialize(new { ok = false, message = "服务不可用" });
                var wechat = services.GetService(typeof(Services.WeChatService)) as Services.WeChatService;
                if (wechat is null) return JsonSerializer.Serialize(new { ok = false, message = "WeChatService 不可用" });

                var context = await wechat.BuildContactsContextAsync();
                var contacts = await wechat.ListAsync();
                return JsonSerializer.Serialize(new
                {
                    count = contacts.Count,
                    ids = contacts.Select(c => c.CharacterId).ToList(),
                    context
                });
            });

        // 搜记忆：在对话记忆库里按关键词 / 日期找过去的对话——想不起来就查，不要凭空编造
        RegisterBuiltin("search_memory",
            "在你们的对话记忆库里搜索过去（对话与好感记录都存着）。"
            + "可以按关键词模糊搜，也可以按日期搜；日期支持具体日期（如 2026-10-01）或"
            + "「今天 / 昨天 / 前天 / 最近」这样的相对说法。"
            + "当对方问起过去的事、某天聊了什么、某个话题时调用本工具；不要凭记忆编造过去。"
            + "返回两部分：items 是记录（近的在前、同一天内随机）、human 是对方脑子里还记得的事"
            + "（重要的事不容易忘，久的会模糊、可能想不起来）。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                if (services is null) return JsonSerializer.Serialize(new { ok = false, message = "服务不可用" });
                var memory = services.GetService(typeof(Modules.AiChat.MemoryVault)) as Modules.AiChat.MemoryVault;
                if (memory is null) return JsonSerializer.Serialize(new { ok = false, message = "MemoryVault 不可用" });
                var engine = services.GetService(typeof(GameEngine)) as GameEngine;
                var charId = engine?.State.CharacterId ?? "";
                if (string.IsNullOrEmpty(charId)) charId = "小雨";

                string keyword = "", dateFrom = "", dateTo = "";
                var limit = 10;
                try
                {
                    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(args) ? "{}" : args);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("keyword", out var k) && k.ValueKind == JsonValueKind.String)
                        keyword = (k.GetString() ?? "").Trim();
                    if (root.TryGetProperty("date_from", out var df) && df.ValueKind == JsonValueKind.String)
                        dateFrom = (df.GetString() ?? "").Trim();
                    if (root.TryGetProperty("date_to", out var dt) && dt.ValueKind == JsonValueKind.String)
                        dateTo = (dt.GetString() ?? "").Trim();
                    if (root.TryGetProperty("limit", out var lm))
                    {
                        if (lm.ValueKind == JsonValueKind.Number && lm.TryGetInt32(out var n))
                            limit = Math.Clamp(n, 1, 30);
                        else if (lm.ValueKind == JsonValueKind.String && int.TryParse(lm.GetString(), out var n2))
                            limit = Math.Clamp(n2, 1, 30);
                    }
                }
                catch (Exception ex)
                {
                    App.WriteLog("ToolManager.search_memory parse -> " + ex.Message);
                }

                if (keyword.Length == 0 && dateFrom.Length == 0 && dateTo.Length == 0)
                    return JsonSerializer.Serialize(new { ok = false, message = "请至少给出关键词（keyword）或日期（date_from / date_to）中的一项。" });

                // 相对日期也算得出来——AI 不一定知道今天几号，但它会说「昨天」。
                DateTime? ParseDay(string s, bool isEnd)
                {
                    if (string.IsNullOrWhiteSpace(s)) return null;
                    var t = s.Trim();
                    var today = DateTime.Now.Date;
                    if (t is "今天" or "今日") return isEnd ? today.AddDays(1).AddSeconds(-1) : today;
                    if (t is "昨天" or "昨日") { var d = today.AddDays(-1); return isEnd ? d.AddDays(1).AddSeconds(-1) : d; }
                    if (t is "前天") { var d = today.AddDays(-2); return isEnd ? d.AddDays(1).AddSeconds(-1) : d; }
                    if (t is "最近" or "近期") return isEnd ? today.AddDays(1).AddSeconds(-1) : today.AddDays(-7);
                    if (DateTime.TryParse(t, out var d3)) return isEnd ? d3.Date.AddDays(1).AddSeconds(-1) : d3;
                    return null;
                }

                var from = ParseDay(dateFrom, isEnd: false);
                var to = ParseDay(dateTo, isEnd: true);

                var all = await memory.All(charId);
                IEnumerable<MemoryEntry> hits = all;
                // 联想检索：先用词义关系网把关键词扩散成一组相关词（想到「咖啡」也该想起「加班」）
                var relatedTerms = new List<string>();
                var semantic = services.GetService(typeof(SemanticNetworkService)) as SemanticNetworkService;
                if (semantic is not null && keyword.Length > 0)
                {
                    try
                    {
                        var expanded = await semantic.ExpandAsync(charId, new[] { keyword }, 8, 1);
                        relatedTerms = expanded.Select(x => x.Term)
                            .Where(t => !string.Equals(t, keyword, StringComparison.OrdinalIgnoreCase))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                    }
                    catch (Exception ex) { App.WriteLog("ToolManager.search_memory expand -> " + ex.Message); }
                }
                if (keyword.Length > 0)
                {
                    hits = hits.Where(m => (m.Content ?? "").Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        || (m.Keywords is not null && m.Keywords.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        || relatedTerms.Any(t => (m.Content ?? "").Contains(t, StringComparison.OrdinalIgnoreCase)));
                }
                if (from is not null) hits = hits.Where(m => m.At.ToLocalTime() >= from.Value);
                if (to is not null) hits = hits.Where(m => m.At.ToLocalTime() <= to.Value);

                // 由近到远排序，但完全随机：按「天」为主序（近期在前），同一天内随机打乱。
                // 人回忆时正是这样——大致记得先后，具体哪句先想起来是随机的。
                var list = hits
                    .GroupBy(m => m.At.ToLocalTime().Date)
                    .OrderByDescending(g => g.Key)
                    .SelectMany(g => g.OrderBy(_ => Random.Shared.Next()))
                    .Take(limit)
                    .ToList();
                if (list.Count == 0)
                {
                    var humanOnly = await BuildHumanRecallAsync(services, charId, keyword);
                    if (humanOnly.Count == 0)
                        return JsonSerializer.Serialize(new { ok = true, count = 0, message = "没有找到相关记忆。可以说「记不太清了」，不要编造。" });
                    return JsonSerializer.Serialize(new
                    {
                        ok = true,
                        count = humanOnly.Count,
                        character = charId,
                        human = humanOnly,
                        note = "以上是对方脑子里还记得的事；用自然的口气引用，不要逐条念。"
                    });
                }

                var items = list.Select(m =>
                {
                    var c = m.Content ?? "";
                    return new
                    {
                        time = m.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                        type = m.Category == "affection" ? "好感" : "对话",
                        text = c.Length > 120 ? c[..120] + "…" : c
                    };
                }).ToList();
                var human = await BuildHumanRecallAsync(services, charId, keyword);
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    count = items.Count + human.Count,
                    character = charId,
                    items,
                    human,
                    related = relatedTerms.Take(6),
                    note = "items 是翻日记能找到的记录（近的在前、同一天内随机）；"
                         + "human 是对方脑子里还记得的事（重要的不容易忘，久的会模糊）；"
                         + "related 是顺着记忆里的联系想起来的近义词。"
                         + "回答时用自然的口气引用，不要逐条念。"
                });
            },
            new List<ToolParameter>
            {
                new() { Name = "keyword", Description = "关键词（模糊匹配对话内容与备注）；可与日期组合，可留空", Required = false },
                new() { Name = "date_from", Description = "起始日期（含），如 2026-10-01，或「昨天」「最近」", Required = false },
                new() { Name = "date_to", Description = "结束日期（含当天），同上格式", Required = false },
                new() { Name = "limit", Description = "返回条数上限，默认 10，最大 30", Required = false }
            });

        // 检索记忆时同时给出「对方脑子里还记得的」（人类记忆层），
        // 而不是只把日记记录倒出来。
        static async Task<List<object>> BuildHumanRecallAsync(IServiceProvider services, string charId, string keyword)
        {
            var result = new List<object>();
            var human = services.GetService(typeof(HumanMemoryService)) as HumanMemoryService;
            if (human is null) return result;
            var settings = services.GetService(typeof(SettingsManager)) as SettingsManager;
            var window = settings?.Current.MemoryWindowDays ?? HumanMemoryMath.DefaultWindowDays;
            var randomness = settings?.Current.MemoryRecallRandomness ?? HumanMemoryMath.DefaultRandomness;

            var entries = await human.RecallAsync(charId, window, randomness, 8);
            foreach (var e in entries)
            {
                if (!string.IsNullOrWhiteSpace(keyword)
                    && !(e.Text ?? "").Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    && !e.Tags.Any(t => t.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                    continue;
                result.Add(new
                {
                    time = e.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd"),
                    type = e.Kind switch
                    {
                        HumanMemoryKind.Image => "照片",
                        HumanMemoryKind.Important => "重要",
                        HumanMemoryKind.Abstraction => "印象",
                        _ => "事件"
                    },
                    recalled = e.RecallCount,
                    text = e.Text
                });
            }
            return result;
        }

        // 打标签：给自己的记忆做分类（人 / 对话 / 事件 / 地点 / 物）。
        // 标签会落到人类记忆上，之后可以按标签或标签组合检索，标签本身也能被回想。
        RegisterBuiltin("tag_memory",
            "给你记得的事打标签，用于以后按标签回忆。标签可以指向人、一段对话、一件事、一个地方或一件东西。"
            + "例如把「雨夜那次谈心」标成「谈心、雨夜、脆弱」，之后问起「我们聊过什么心里话」就能顺着标签想起来。"
            + "一次最多 6 个标签，每个不超过 12 个字。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                if (services is null) return JsonSerializer.Serialize(new { ok = false, message = "服务不可用" });
                var tags = services.GetService(typeof(MemoryTagService)) as MemoryTagService;
                if (tags is null) return JsonSerializer.Serialize(new { ok = false, message = "标签服务不可用" });
                var engine = services.GetService(typeof(GameEngine)) as GameEngine;
                var charId = engine?.State.CharacterId ?? "";
                if (string.IsNullOrEmpty(charId)) charId = "小雨";

                string raw = "", subject = "event", target = "";
                var importance = 0.5;
                try
                {
                    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(args) ? "{}" : args);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("tags", out var t))
                    {
                        raw = t.ValueKind == JsonValueKind.Array
                            ? string.Join("、", t.EnumerateArray().Select(x => x.GetString() ?? ""))
                            : (t.GetString() ?? "");
                    }
                    if (root.TryGetProperty("subject", out var s) && s.ValueKind == JsonValueKind.String)
                        subject = (s.GetString() ?? "event").Trim().ToLowerInvariant();
                    if (root.TryGetProperty("target", out var tg) && tg.ValueKind == JsonValueKind.String)
                        target = (tg.GetString() ?? "").Trim();
                    if (root.TryGetProperty("importance", out var im) && im.ValueKind == JsonValueKind.Number)
                        importance = Math.Clamp(im.GetDouble(), 0.05, 1.0);
                }
                catch (Exception ex) { App.WriteLog("ToolManager.tag_memory parse -> " + ex.Message); }

                var list = raw.Split(new[] { '、', ',', '，', ';', '；', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (list.Count == 0)
                    return JsonSerializer.Serialize(new { ok = false, message = "请给出至少一个标签（tags）。" });

                var subjectKind = subject is "person" or "talk" or "event" or "place" or "thing" ? subject : "event";
                var applied = await tags.ApplyAsync(charId, list, subjectKind, target, importance);
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    applied,
                    subject = subjectKind,
                    message = applied == 0 ? "没有可用的标签。" : $"已记下 {applied} 个标签。"
                });
            },
            new List<ToolParameter>
            {
                new() { Name = "tags", Description = "标签，逗号或顿号分隔，一次最多 6 个", Required = true },
                new() { Name = "subject", Description = "标签指向什么：person/talk/event/place/thing，默认 event", Required = false },
                new() { Name = "target", Description = "标签指向的那件事/那段话（留空则用标签自身记一条）", Required = false },
                new() { Name = "importance", Description = "这件事的重要性 0~1，默认 0.5", Required = false }
            });

        // 按标签（可组合）检索：这是「分类查」的入口，和按关键词/日期的 search_memory 互补。
        RegisterBuiltin("search_by_tags",
            "按你打过的标签检索记忆，支持标签组合。当对方问「我们聊过哪些心里话」「关于下雨的事」这类"
            + "带分类意味的问题时用它。match_all=true（默认）要求同时具备全部标签，false 表示命中任一即可。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                if (services is null) return JsonSerializer.Serialize(new { ok = false, message = "服务不可用" });
                var tags = services.GetService(typeof(MemoryTagService)) as MemoryTagService;
                if (tags is null) return JsonSerializer.Serialize(new { ok = false, message = "标签服务不可用" });
                var engine = services.GetService(typeof(GameEngine)) as GameEngine;
                var charId = engine?.State.CharacterId ?? "";
                if (string.IsNullOrEmpty(charId)) charId = "小雨";

                var raw = "";
                var matchAll = true;
                var limit = 10;
                try
                {
                    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(args) ? "{}" : args);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("tags", out var t))
                    {
                        raw = t.ValueKind == JsonValueKind.Array
                            ? string.Join("、", t.EnumerateArray().Select(x => x.GetString() ?? ""))
                            : (t.GetString() ?? "");
                    }
                    if (root.TryGetProperty("match_all", out var ma))
                        matchAll = ma.ValueKind != JsonValueKind.False;
                    if (root.TryGetProperty("limit", out var lm) && lm.ValueKind == JsonValueKind.Number
                        && lm.TryGetInt32(out var n))
                        limit = Math.Clamp(n, 1, 20);
                }
                catch (Exception ex) { App.WriteLog("ToolManager.search_by_tags parse -> " + ex.Message); }

                var list = raw.Split(new[] { '、', ',', '，', ';', '；', '|' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                if (list.Count == 0)
                    return JsonSerializer.Serialize(new { ok = false, message = "请给出至少一个标签（tags）。" });

                var hits = await tags.SearchAsync(charId, list, matchAll, limit);
                if (hits.Count == 0)
                {
                    // 没命中就说清有什么标签可用，别让 AI 编造
                    var available = await tags.ListAsync(charId);
                    return JsonSerializer.Serialize(new
                    {
                        ok = true,
                        count = 0,
                        known_tags = available.Take(20).Select(x => x.Name),
                        message = "没有带这些标签的记忆。可以照实说「记不太清」，或改用可用的标签再查一次。"
                    });
                }
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    count = hits.Count,
                    items = hits.Select(h => new
                    {
                        time = h.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd"),
                        kind = h.Kind.ToString(),
                        tags = h.Tags,
                        text = h.Text
                    }),
                    note = "用自然的口气引用，不要逐条念。"
                });
            },
            new List<ToolParameter>
            {
                new() { Name = "tags", Description = "标签，逗号或顿号分隔，可组合", Required = true },
                new() { Name = "match_all", Description = "true=同时具备全部标签（默认），false=命中任一", Required = false },
                new() { Name = "limit", Description = "返回条数上限，默认 10，最大 20", Required = false }
            });

        // ===== Skills（能力包）=====
        RegisterBuiltin("list_skills",
            "列出已安装并可用的能力包（skill），包含名字与用途。不确定自己会什么时先用它看一眼。",
            async _ =>
            {
                var services = ServicesHelper.GetServices();
                var skills = services?.GetService(typeof(SkillLibrary)) as SkillLibrary;
                if (skills is null) return JsonSerializer.Serialize(new { ok = true, count = 0, message = "没有安装能力包。" });
                var list = await skills.EnabledAsync();
                if (list.Count == 0) return JsonSerializer.Serialize(new { ok = true, count = 0, message = "没有安装能力包。" });
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    count = list.Count,
                    skills = list.Select(s => new
                    {
                        name = s.Name,
                        title = s.Title,
                        purpose = s.Description,
                        runnable = s.HasExecutable
                    }),
                    note = "要用某个能力时，先调 read_skill 读它的说明书。"
                });
            },
            new List<ToolParameter>());

        RegisterBuiltin("read_skill",
            "读取某个能力包（skill）的说明书正文。按说明书做，不要凭名字猜它的用法。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                var skills = services?.GetService(typeof(SkillLibrary)) as SkillLibrary;
                if (skills is null) return JsonSerializer.Serialize(new { ok = false, message = "能力包服务不可用。" });

                var name = "";
                try
                {
                    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(args) ? "{}" : args);
                    if (doc.RootElement.TryGetProperty("name", out var n)) name = (n.GetString() ?? "").Trim();
                }
                catch (Exception ex) { App.WriteLog("ToolManager.read_skill parse -> " + ex.Message); }
                if (name.Length == 0) return JsonSerializer.Serialize(new { ok = false, message = "请给出能力包名字（name）。" });

                var body = skills.ReadInstructions(name);
                if (string.IsNullOrWhiteSpace(body))
                    return JsonSerializer.Serialize(new { ok = false, message = $"「{name}」没有说明书（可能只有可执行入口，直接用它的工具即可）。" });
                return JsonSerializer.Serialize(new { ok = true, name, instructions = body });
            },
            new List<ToolParameter>
            {
                new() { Name = "name", Description = "能力包名字（见 list_skills 返回）", Required = true }
            });

        // 黑屏：AI 可以主动把画面黑掉（关灯/私密时刻），但黑屏不影响继续对话
        RegisterBuiltin("set_blackout",
            "把画面黑掉或恢复。适合某些时刻：房间里关了灯、不想让对方看见画面、话题太私密。"
            + "黑屏不代表不能说话——你依然可以正常回复，对方也依然能看见你的文字。"
            + "不要滥用：日常对话保持画面可见。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                var blackout = services?.GetService(typeof(ScreenBlackoutService)) as ScreenBlackoutService;
                if (blackout is null) return JsonSerializer.Serialize(new { ok = false, message = "黑屏服务不可用" });

                var on = true;
                var reason = "";
                try
                {
                    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(args) ? "{}" : args);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("on", out var o))
                    {
                        on = o.ValueKind switch
                        {
                            JsonValueKind.False => false,
                            JsonValueKind.String => !string.Equals(o.GetString(), "false", StringComparison.OrdinalIgnoreCase),
                            _ => true
                        };
                    }
                    if (root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String)
                        reason = (r.GetString() ?? "").Trim();
                }
                catch (Exception ex) { App.WriteLog("ToolManager.set_blackout parse -> " + ex.Message); }

                blackout.Set(on, reason);
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    blackout = on,
                    message = on
                        ? "画面已黑掉（对方仍能和你说话）。" + (reason.Length > 0 ? $"说明：{reason}" : "")
                        : "画面已恢复。"
                });
            },
            new List<ToolParameter>
            {
                new() { Name = "on", Description = "true=黑屏，false=恢复，默认 true", Required = false },
                new() { Name = "reason", Description = "简短说明为什么黑屏（会给用户看），可留空", Required = false }
            });

        // 给用户真的发一条消息到手机（QQ 桥）：让「虚拟世界」能伸手够到现实
        RegisterBuiltin("send_to_user",
            "给用户真的发一条消息到他的手机上（QQ 私聊），不是游戏里的对话。"
            + "只有确实想越过屏幕找到他时才用：约好的时间到了、他很久没消息你想叫他、或你答应了要给他发消息。"
            + "默认要等用户确认后才会真的发出去，所以你发完要如实告诉他「我发了，记得确认」。"
            + "一小时内的条数有上限，别连着发。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                var bridge = services?.GetService(typeof(Modules.RealChat.OfficialChatBridge)) as Modules.RealChat.OfficialChatBridge;
                if (bridge is null) return JsonSerializer.Serialize(new { ok = false, message = "外接通道服务不可用。" });

                var text = "";
                try
                {
                    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(args) ? "{}" : args);
                    if (doc.RootElement.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        text = (t.GetString() ?? "").Trim();
                }
                catch (Exception ex) { App.WriteLog("ToolManager.send_to_user parse -> " + ex.Message); }

                if (text.Length == 0)
                    return JsonSerializer.Serialize(new { ok = false, message = "请给出要发的内容（text）。" });
                if (text.Length > 500) text = text[..500];

                var result = bridge.QueueOutbound(text);
                return JsonSerializer.Serialize(new { ok = true, message = result });
            },
            new List<ToolParameter>
            {
                new() { Name = "text", Description = "要发到对方手机上的消息正文", Required = true }
            });

        // 看谁在哪：多实体地图上，用户和每个 AI 角色各自的位置
        RegisterBuiltin("list_actors",
            "列出地图上所有实体（用户 + 各角色）当前所在的场景。"
            + "想拉人/带人之前先看这个，确认谁和谁在同一场景。",
            async _ =>
            {
                var services = ServicesHelper.GetServices();
                if (services is null) return JsonSerializer.Serialize(new { ok = false, message = "服务不可用" });
                var actors = services.GetService(typeof(Services.MapActorService)) as Services.MapActorService;
                if (actors is null) return JsonSerializer.Serialize(new { ok = false, message = "MapActorService 不可用" });
                var list = actors.Actors.Select(a => new
                {
                    id = a.Id,
                    name = a.Name,
                    isUser = a.IsUser,
                    scene = a.SceneId
                }).ToList();
                return JsonSerializer.Serialize(new { count = list.Count, actors = list });
            });

        // 拉人：AI 把用户/其他 AI 拉到另一个场景（多 AI 拉同一目标由服务仲裁）
        RegisterBuiltin("pull_actor",
            "把某个实体（用户或另一个角色）拉到指定场景。"
            + "需要一次拉多个不同目标时，用批量形式：传 targets 数组（每项 {target, scene}），puller 是发起者。"
            + "若同一目标同时被多个方向拉拽，服务会仲裁：方向一致则跟随，方向不同则目标不动，"
            + "并在返回里说明是谁在拉、为什么没动；不可达/不同场景/没指定目的地会被拒绝并列在 rejected 里。"
            + "另有一种「自己走」的形式：传 move_self=true（+scene），让 puller 自己走到 scene；"
            + "此时用 carry_user=true/false 决定这次行动是否带上用户（用户不在同一处就带不走，返回里会说明）。"
            + "无论拉拽还是自己走，事件都会广播给同场景的在场者（broadcast）。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                if (services is null) return JsonSerializer.Serialize(new { ok = false, message = "服务不可用" });
                var actors = services.GetService(typeof(Services.MapActorService)) as Services.MapActorService;
                if (actors is null) return JsonSerializer.Serialize(new { ok = false, message = "MapActorService 不可用" });
                var map = services.GetService(typeof(MapService)) as MapService;

                string puller = "", target = "", scene = "";
                bool moveSelf = false, carryUser = false;
                var batch = new List<(string Target, string Scene)>();
                try
                {
                    using var doc = JsonDocument.Parse(args ?? "{}");
                    var root = doc.RootElement;
                    if (root.TryGetProperty("puller", out var p)) puller = p.GetString() ?? "";
                    if (root.TryGetProperty("target", out var t)) target = t.GetString() ?? "";
                    if (root.TryGetProperty("scene", out var s)) scene = s.GetString() ?? "";
                    if (root.TryGetProperty("move_self", out var ms)
                        && (ms.ValueKind == JsonValueKind.True || ms.ValueKind == JsonValueKind.False))
                        moveSelf = ms.GetBoolean();
                    if (root.TryGetProperty("carry_user", out var cu)
                        && (cu.ValueKind == JsonValueKind.True || cu.ValueKind == JsonValueKind.False))
                        carryUser = cu.GetBoolean();
                    // 批量形式：targets 是对象数组
                    if (root.TryGetProperty("targets", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in arr.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.Object) continue;
                            var bt = item.TryGetProperty("target", out var e1) ? e1.GetString() ?? "" : "";
                            var bs = item.TryGetProperty("scene", out var e2) ? e2.GetString() ?? "" : "";
                            if (bt.Length > 0 && bs.Length > 0) batch.Add((bt, bs));
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.WriteLog("ToolManager.pull_actor parse -> " + ex.Message);
                }

                // 场景名 → Id
                string ResolveScene(string name)
                {
                    if (map is null) return name;
                    var sc = map.Map.SceneById(name) ?? map.Map.SceneByName(name);
                    return sc is null ? name : sc.Id;
                }

                // 自己走（可选带用户）：move_self=true 时走这里
                if (moveSelf && scene.Length > 0)
                {
                    var me = puller.Length == 0 ? "ai" : puller;
                    var (note, carried, bc) = actors.MoveWithUser(me, ResolveScene(scene), carryUser);
                    return JsonSerializer.Serialize(new
                    {
                        ok = note.Length > 0,
                        move_self = true,
                        carried_user = carried,
                        note,
                        broadcast = bc
                    });
                }

                // 批量：一次拉多个不同目标，各自独立仲裁
                if (batch.Count > 0)
                {
                    var reqs = batch.Select(b => new PullRequest
                    {
                        PullerId = puller.Length == 0 ? "ai" : puller,
                        PullerName = puller,
                        TargetId = b.Target,
                        TargetSceneId = ResolveScene(b.Scene)
                    }).ToList();
                    var many = await actors.PullManyAsync(reqs);
                    var results = many.Select(kv => new
                    {
                        target = kv.Key,
                        moved = kv.Value.Moved,
                        final_scene = kv.Value.FinalSceneId,
                        shake_suppressed = kv.Value.ShakeSuppressed,
                        rejected = kv.Value.Rejected,
                        message = kv.Value.Message
                    }).ToList();
                    return JsonSerializer.Serialize(new { ok = true, count = results.Count, results });
                }

                if (target.Length == 0 || scene.Length == 0)
                    return JsonSerializer.Serialize(new { ok = false, message = "需要参数 puller、target、scene，或用 targets 数组批量拉拽，或 move_self=true + scene 自己走" });

                var sceneId = ResolveScene(scene);
                var result = await actors.PullAsync(puller.Length == 0 ? "ai" : puller,
                    puller, target, sceneId);
                return JsonSerializer.Serialize(new
                {
                    moved = result.Moved,
                    final_scene = result.FinalSceneId,
                    shake_suppressed = result.ShakeSuppressed,
                    pulled_by = result.Pulls.Select(r => r.PullerName).ToList(),
                    rejected = result.Rejected,
                    message = result.Message
                });
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