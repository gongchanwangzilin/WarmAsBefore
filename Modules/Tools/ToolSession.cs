using System.Collections;
using System.Diagnostics;
using System.Text.Json;

namespace WarmAsBefore.Modules.Tools;

/// <summary>
/// 工具子进程会话：把一个 Python/Java 工具作为长驻子进程拉起，
/// 通过 stdin/stdout 以 JSON-RPC 2.0（newline 分帧）通信。
/// 统一的协议让 Java 与 Python 工具完全可互换，不区分两个独立系统。
/// </summary>
public sealed class ToolSession : IDisposable
{
    private readonly Process _process;
    private readonly StreamWriter _input;
    private readonly StreamReader _output;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _nextId;
    private bool _disposed;

    private ToolSession(Process process, StreamWriter input, StreamReader output)
    {
        _process = process;
        _input = input;
        _output = output;
    }

    /// <summary>启动工具子进程。exe=解释器；args=解释器启动参数；脚本/JAR 由 entry 拼接。</summary>
    public static ToolSession? Start(ToolDefinition tool, string exe, string args)
    {
        try
        {
            var toolArgs = args;
            if (tool.Language == ToolLanguage.Python)
                toolArgs = (args + " \"" + Path.Combine(tool.WorkDir, tool.Entry) + "\"").Trim();
            else if (tool.Language == ToolLanguage.Java)
                toolArgs = "-jar \"" + Path.Combine(tool.WorkDir, tool.Entry) + "\"";

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = toolArgs,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tool.WorkDir
            };
            var p = Process.Start(psi);
            if (p is null) return null;
            return new ToolSession(p, p.StandardInput, p.StandardOutput);
        }
        catch (Exception ex)
        {
            App.WriteLog("ToolSession.Start -> " + ex);
            return null;
        }
    }

    public bool IsAlive
    {
        get { try { return !_process.HasExited; } catch { return false; } }
    }

    /// <summary>发起一次 JSON-RPC 调用，返回响应行（JSON 文本）。</summary>
    public async Task<string> CallAsync(object? parameters)
    {
        await _lock.WaitAsync();
        try
        {
            if (!IsAlive)
                throw new InvalidOperationException("工具进程已退出");

            var id = Interlocked.Increment(ref _nextId);
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                w.WriteString("jsonrpc", "2.0");
                w.WriteNumber("id", id);
                w.WriteString("method", "execute");
                w.WritePropertyName("params");
                if (parameters is null)
                {
                    w.WriteStartObject();
                    w.WriteEndObject();
                }
                else if (parameters is JsonElement je)
                {
                    je.WriteTo(w);
                }
                else
                {
                    JsonSerializer.Serialize(w, parameters, parameters.GetType());
                }
                w.WriteEndObject();
            }
            var line = System.Text.Encoding.UTF8.GetString(ms.ToArray());
            await _input.WriteLineAsync(line);
            await _input.FlushAsync();

            var response = await ReadResponseLineAsync(id);
            if (response is null)
                return JsonSerializer.Serialize(new { error = "工具无响应" });

            using var doc = JsonDocument.Parse(response);
            if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                var msg = err.TryGetProperty("message", out var m) ? m.GetString() : "未知错误";
                return JsonSerializer.Serialize(new { error = msg });
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
        var timeout = Task.Delay(TimeSpan.FromSeconds(120));
        var read = _output.ReadLineAsync();
        var done = await Task.WhenAny(read, timeout);
        if (done == timeout)
        {
            try { _process.Kill(entireProcessTree: true); } catch { }
            return JsonSerializer.Serialize(new { error = "工具调用超时（120s）" });
        }
        var text = await read;
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