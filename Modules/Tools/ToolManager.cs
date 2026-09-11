using System.Collections.Concurrent;
using System.Text.Json;
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
    private int _rpcId;
    private bool _disposed;

    public ToolManager(RuntimeManager runtimes)
    {
        _runtimes = runtimes;
        RegisterSystemTools();
        Modules.Battle.BattleTools.Register(this);
        ScanExternalTools();
        _ = StartHarnessLoopAsync();
    }

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
                        WorkDir = dir
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
                return await handler(argsJson ?? "");

            // 外部工具：先确保运行时可用
            var resolved = await _runtimes.ResolveRuntimeAsync(tool.Language);
            if (resolved is null)
                return JsonSerializer.Serialize(new { error = $"{tool.RuntimeNeeded}。可在设置页 -> 工具模式中「一键下载」或手动配置路径" });

            var session = await GetSessionAsync(tool, resolved.Value.Exe, resolved.Value.Args);
            object? parameters = null;
            if (!string.IsNullOrWhiteSpace(argsJson))
            {
                try { parameters = JsonDocument.Parse(argsJson).RootElement; }
                catch { parameters = argsJson; }
            }
            return await session.CallAsync(parameters);
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
        });
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
}