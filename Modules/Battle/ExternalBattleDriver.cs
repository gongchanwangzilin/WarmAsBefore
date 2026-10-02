using System.Diagnostics;
using System.Text.Json;
using WarmAsBefore.Modules.Tools;

namespace WarmAsBefore.Modules.Battle;

/// <summary>
/// 外部战斗驱动：从 {root}/battles/{driver}/battle.json 拉起 Python/Java 子进程，
/// 通过 stdin/stdout 按 JSON-RPC 2.0 通信（与工具模式同协议）。
/// 方法：battle_start / battle_action / battle_advance；
/// 每次响应必须携带 ended:true|false，缺失视为已结束（防卡页）。
/// </summary>
public sealed class ExternalBattleDriver : IBattleDriver
{
    private readonly Process _process;
    private readonly StreamWriter _input;
    private readonly StreamReader _output;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _nextId;
    private bool _disposed;

    public string Name { get; }
    public bool IsAlive { get { try { return !_process.HasExited; } catch { return false; } } }

    private ExternalBattleDriver(Process process, StreamWriter input, StreamReader output, string name)
    {
        _process = process;
        _input = input;
        _output = output;
        Name = name;
    }

    /// <summary>解析并拉起一个外部战斗驱动；运行时不可用返回 null。</summary>
    public static async Task<ExternalBattleDriver?> StartAsync(
        ExternalBattleManifest manifest, string workDir, RuntimeManager runtimes)
    {
        ToolLanguage language = manifest.Language?.ToLowerInvariant() switch
        {
            "java" => ToolLanguage.Java,
            _ => ToolLanguage.Python
        };
        var runtime = await runtimes.ResolveRuntimeAsync(language);
        if (runtime is null) return null;

        try
        {
            var args = runtime.Value.Args;
            var entry = string.IsNullOrWhiteSpace(manifest.Entry)
                ? (language == ToolLanguage.Java ? "battle.jar" : "battle.py")
                : manifest.Entry;
            if (language == ToolLanguage.Python)
                args = (args + " \"" + Path.Combine(workDir, entry) + "\"").Trim();
            else
                args = "-jar \"" + Path.Combine(workDir, entry) + "\"";

            var psi = new ProcessStartInfo
            {
                FileName = runtime.Value.Exe,
                Arguments = args,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workDir
            };
            var p = Process.Start(psi);
            if (p is null) return null;
            return new ExternalBattleDriver(p, p.StandardInput, p.StandardOutput, manifest.Name);
        }
        catch (Exception ex)
        {
            App.WriteLog("ExternalBattleDriver.Start -> " + ex);
            return null;
        }
    }

    /// <summary>发起一次 JSON-RPC 调用（带方法名），返回响应的 JSON 文本。</summary>
    public async Task<string> CallAsync(string method, string paramsJson)
    {
        await _lock.WaitAsync();
        try
        {
            if (!IsAlive)
                return JsonSerializer.Serialize(new { error = "战斗驱动已退出", ended = true, winner = "" });

            var id = Interlocked.Increment(ref _nextId);
            object? payload;
            try
            {
                payload = JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(paramsJson) ? "{}" : paramsJson);
            }
            catch
            {
                payload = null;
            }

            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                w.WriteString("jsonrpc", "2.0");
                w.WriteNumber("id", id);
                w.WriteString("method", method);
                w.WritePropertyName("params");
                if (payload is JsonElement je) je.WriteTo(w);
                else { w.WriteStartObject(); w.WriteEndObject(); }
                w.WriteEndObject();
            }
            var line = System.Text.Encoding.UTF8.GetString(ms.ToArray());
            await _input.WriteLineAsync(line);
            await _input.FlushAsync();

            var response = await ReadResponseLineAsync(id);
            if (response is null)
                return JsonSerializer.Serialize(new { error = "战斗驱动无响应", ended = true, winner = "" });

            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                var msg = err.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                    ? m.GetString() : "未知错误";
                return JsonSerializer.Serialize(new { error = msg, ended = true, winner = "" });
            }
            // 契约要求 ended 必须存在；缺失则按结束处理（防卡页）。
            if (!doc.RootElement.TryGetProperty("ended", out _))
            {
                using var ms2 = new MemoryStream();
                using (var w2 = new Utf8JsonWriter(ms2))
                {
                    w2.WriteStartObject();
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        prop.WriteTo(w2);
                    }
                    w2.WriteBoolean("ended", true);
                    w2.WriteString("winner", "");
                    w2.WriteEndObject();
                }
                return System.Text.Encoding.UTF8.GetString(ms2.ToArray());
            }
            return response;
        }
        finally
        {
            _lock.Release();
        }
    }

    private Task<string?> ReadResponseLineAsync(int id) => ReadResponseLineInternalAsync(id);

    private async Task<string?> ReadResponseLineInternalAsync(int id)
    {
        var timeout = Task.Delay(TimeSpan.FromSeconds(30));
        var read = _output.ReadLineAsync();
        var done = await Task.WhenAny(read, timeout);
        if (done == timeout)
        {
            try { _process.Kill(entireProcessTree: true); } catch { }
            return JsonSerializer.Serialize(new { error = "战斗驱动超时（30s）" });
        }
        var text = await read;
        if (string.IsNullOrWhiteSpace(text))
            return JsonSerializer.Serialize(new { error = "战斗驱动无响应（EOF）" });
        return text;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (IsAlive) _process.Kill(entireProcessTree: true); } catch { }
        try { _process.Dispose(); } catch { }
        _lock.Dispose();
    }
}