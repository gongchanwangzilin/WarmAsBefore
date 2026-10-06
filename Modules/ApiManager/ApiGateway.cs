using System.Net;
using System.Text;
using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Modules.ApiManager;

public sealed class ApiGateway
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private AiEndpoint _cfg = new();

    public void Configure(AiEndpoint cfg) => _cfg = cfg;

    /// <summary>获取 API 支持的所有模型列表。</summary>
    public async Task<List<string>?> ListModels()
    {
        var baseUrl = _cfg.Url.TrimEnd('/');
        // 去掉 /chat/completions 后缀
        if (baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl[..^"/chat/completions".Length];
        var modelsUrl = $"{baseUrl}/models";

        using var req = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_cfg.Key}");

        try
        {
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                var errRaw = await resp.Content.ReadAsStringAsync();
                try
                {
                    var errDoc = JsonDocument.Parse(errRaw);
                    if (errDoc.RootElement.TryGetProperty("error", out var err)
                        && err.TryGetProperty("message", out var msg))
                        return null; // 返回 null 表示认证或权限错误
                }
                catch { }
                return null;
            }

            var modelsRaw = await resp.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(modelsRaw);
            var models = new List<string>();
            foreach (var model in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                var id = model.GetProperty("id").GetString();
                if (!string.IsNullOrWhiteSpace(id))
                    models.Add(id);
            }
            return models;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Gateway.ListModels] {ex.Message}");
            return null;
        }
    }

    /// <summary>原始请求：调用方给定完整 body（含 model/messages/temperature），返回助手文本。失败返回 null。</summary>
    public async Task<string?> ChatRaw(object body)
    {
        var cfg = _cfg;
        var json = JsonSerializer.Serialize(body);
        using var req = new HttpRequestMessage(HttpMethod.Post, cfg.Url);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {cfg.Key}");

        try
        {
            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadAsStringAsync();
            return JsonDocument.Parse(raw)
                .RootElement.GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Gateway.ChatRaw] {ex.Message}");
            return null;
        }
    }

    /// <summary>把端点地址补全为 chat/completions 的完整 URL（Chat 与 ChatWithToolsAsync 共用）。</summary>
    private static string BuildChatUrl(AiEndpoint cfg)
    {
        // 确保 URL 格式正确：不包含 /v1 但需要 /chat/completions
        var url = cfg.Url.TrimEnd('/');
        // 如果以 /v1 结尾，替换为 /v1/chat/completions
        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            url = url[..^3] + "/v1/chat/completions";
        else if (!url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            url += "/chat/completions";
        return url;
    }

    public async Task<string?> Chat(List<ChatMessage> history, AiEndpoint? over = null)
    {
        var cfg = over ?? _cfg;
        var url = BuildChatUrl(cfg);
        var body = new
        {
            model = cfg.Model,
            messages = history.Select(m => new { role = m.Role, content = m.Content }),
            temperature = cfg.Temperature,
            max_tokens = cfg.MaxTokens
        };
        var json = JsonSerializer.Serialize(body);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {cfg.Key}");

        try
        {
            var resp = await _http.SendAsync(req);
            var raw = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                try
                {
                    var errDoc = JsonDocument.Parse(raw);
                    if (errDoc.RootElement.TryGetProperty("error", out var err)
                        && err.TryGetProperty("message", out var msg))
                        return $"[API错误 {resp.StatusCode}]: {msg}";
                }
                catch { }
                return $"[API错误 {resp.StatusCode}]";
            }

            return JsonDocument.Parse(raw)
                .RootElement.GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Gateway] {ex.Message}");
            if (ex is HttpRequestException && ex.InnerException is System.Net.Sockets.SocketException)
                return "[网络连接失败，请检查网络]";
            if (ex is TimeoutException)
                return "[请求超时，请检查网络或重试]";
            return $"[API错误: {ex.Message}]";
        }
    }

    // ============ 工具调用（OpenAI function calling） ============

    /// <summary>
    /// 带工具的一轮请求：返回完整 assistant 消息（content 与 tool_calls），而不是像 Chat() 那样只留文本。
    ///
    /// 兼容性约定：
    ///  · tools 为空 / null 时请求体与 Chat() 完全一致（不带 tools 字段），无工具时行为不变；
    ///  · 端点不认识 tools 字段（报 4xx 或在错误里点名 tool/function）时，自动去掉 tools 重试一次，
    ///    并在结果上标 ToolsRejected，让调用方后续本轮不再带 tools —— 聊天绝不能因此彻底不可用。
    /// </summary>
    public async Task<ChatTurn> ChatWithToolsAsync(List<ChatMessage> history, List<object>? tools, AiEndpoint? over = null)
    {
        var cfg = over ?? _cfg;
        var url = BuildChatUrl(cfg);

        var turn = await SendTurnAsync(url, cfg, history, tools);
        if (tools is not { Count: > 0 } || !turn.ToolRejected)
            return new ChatTurn { Content = turn.Content, ToolCalls = turn.ToolCalls, Error = turn.Error };

        // 去掉 tools 再试一次：绝大多数"不支持工具"的端点只是严格校验了未知字段
        App.WriteLog($"ApiGateway.ChatWithTools: 端点疑似不支持 tools（{turn.Error}），去掉 tools 重试");
        var retry = await SendTurnAsync(url, cfg, history, null);
        return new ChatTurn
        {
            Content = retry.Content,
            ToolCalls = retry.ToolCalls,
            Error = retry.Error,
            ToolsRejected = true
        };
    }

    /// <summary>单次 HTTP 往返：发起请求 + 解析出完整 assistant 消息。</summary>
    private async Task<TurnResult> SendTurnAsync(string url, AiEndpoint cfg, List<ChatMessage> history, List<object>? tools)
    {
        var json = JsonSerializer.Serialize(BuildBody(cfg, history, tools));
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {cfg.Key}");

        try
        {
            var resp = await _http.SendAsync(req);
            var raw = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                var detail = "";
                try
                {
                    var errDoc = JsonDocument.Parse(raw);
                    if (errDoc.RootElement.TryGetProperty("error", out var err)
                        && err.TryGetProperty("message", out var msg))
                        detail = msg.ToString();
                }
                catch { }
                var text = string.IsNullOrWhiteSpace(detail)
                    ? $"[API错误 {resp.StatusCode}]"
                    : $"[API错误 {resp.StatusCode}]: {detail}";
                return new TurnResult(null, null, text, LooksLikeToolRejection(resp.StatusCode, raw));
            }

            return ParseTurn(raw);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Gateway.ChatWithTools] {ex.Message}");
            var text = ex switch
            {
                HttpRequestException { InnerException: System.Net.Sockets.SocketException } => "[网络连接失败，请检查网络]",
                TimeoutException => "[请求超时，请检查网络或重试]",
                _ => $"[API错误: {ex.Message}]"
            };
            return new TurnResult(null, null, text, false);
        }
    }

    /// <summary>请求体：只有确实有工具时才带 tools 字段（无工具时与 Chat() 逐字段相同）。</summary>
    private static object BuildBody(AiEndpoint cfg, List<ChatMessage> history, List<object>? tools)
    {
        var messages = history.Select(Wire).ToList();
        if (tools is { Count: > 0 })
            return new
            {
                model = cfg.Model,
                messages,
                temperature = cfg.Temperature,
                max_tokens = cfg.MaxTokens,
                tools
            };
        return new
        {
            model = cfg.Model,
            messages,
            temperature = cfg.Temperature,
            max_tokens = cfg.MaxTokens
        };
    }

    /// <summary>
    /// 把 ChatMessage 投影成 OpenAI 线格式。带 tool_calls 的 assistant 必须原样回传（content 可为 null），
    /// role="tool" 的结果必须带 tool_call_id，否则端点会因"tool 消息没有对应的 tool_calls"拒收整段历史。
    /// </summary>
    private static object Wire(ChatMessage m)
    {
        if (m.ToolCalls is { Count: > 0 })
            return new
            {
                role = "assistant",
                content = string.IsNullOrEmpty(m.Content) ? null : m.Content,
                tool_calls = m.ToolCalls.Select(c => new
                {
                    id = c.Id,
                    type = "function",
                    function = new { name = c.Name, arguments = c.Arguments }
                }).ToList()
            };
        if (m.Role == "tool")
            return new { role = "tool", tool_call_id = m.ToolCallId ?? "", content = m.Content };
        return new { role = m.Role, content = m.Content };
    }

    /// <summary>
    /// 解析响应：content 可能为 null（模型只给工具调用时）或缺失，所以不能用 GetProperty 硬取。
    /// </summary>
    private static TurnResult ParseTurn(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                return new TurnResult(null, null, "[API错误: 响应里没有 choices]", false);

            if (!choices[0].TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
                return new TurnResult(null, null, "[API错误: 响应里没有 message]", false);

            string? content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;

            List<ToolCall>? calls = null;
            if (message.TryGetProperty("tool_calls", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var item in arr.EnumerateArray())
                {
                    i++;
                    if (item.ValueKind != JsonValueKind.Object) continue;   // 畸形的 tool_call 直接跳过，别让整轮解析失败
                    var fn = item.TryGetProperty("function", out var f) && f.ValueKind == JsonValueKind.Object ? f : default;
                    var name = fn.ValueKind == JsonValueKind.Object
                        && fn.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                        ? (n.GetString() ?? "").Trim() : "";
                    if (name.Length == 0) continue;   // 没有名字的调用无法执行，直接忽略
                    var argsText = fn.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.String
                        ? a.GetString() ?? "" : "";
                    // 少数端点不回 id；但 tool 结果必须回指一个 id，这里补一个稳定的
                    var id = item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                        ? (idEl.GetString() ?? "") : "";
                    (calls ??= new List<ToolCall>()).Add(new ToolCall
                    {
                        Id = string.IsNullOrWhiteSpace(id) ? $"call_{i}" : id,
                        Name = name,
                        Arguments = argsText
                    });
                }
            }

            return new TurnResult(content, calls, null, false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Gateway.Parse] {ex.Message}");
            return new TurnResult(null, null, $"[API错误: 响应解析失败 {ex.Message}]", false);
        }
    }

    /// <summary>
    /// 判断失败是否是"端点不认 tools 字段"。认证类错误（401/403）与限流不在此列 ——
    /// 重试也救不回来，白等一轮。
    /// </summary>
    private static bool LooksLikeToolRejection(HttpStatusCode status, string raw)
    {
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            or HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout)
            return false;
        // 错误体里点名 tools / function：任何状态码都当成不支持工具
        if (raw.Contains("tool", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("function", StringComparison.OrdinalIgnoreCase))
            return true;
        // 典型的"我不认识这个字段"类 4xx：去掉 tools 再试一次是安全的
        return status is HttpStatusCode.BadRequest or HttpStatusCode.NotFound
            or HttpStatusCode.MethodNotAllowed or HttpStatusCode.UnsupportedMediaType
            or HttpStatusCode.UnprocessableEntity;
    }

    /// <summary>单次往返的解析结果（含"是否需要去掉 tools 重试"这一内部判断）。</summary>
    private sealed record TurnResult(string? Content, List<ToolCall>? ToolCalls, string? Error, bool ToolRejected);
}

/// <summary>
/// 带工具的一轮调用结果：Content 为正文（可能是空串＝模型选择沉默，也可能是 null＝没给正文），
/// ToolCalls 非空表示模型要求先调用工具；Error 非空时沿用 Chat() 的 <c>[...</c> 错误文本约定。
/// </summary>
public sealed class ChatTurn
{
    public string? Content { get; init; }
    public List<ToolCall>? ToolCalls { get; init; }
    public string? Error { get; init; }

    /// <summary>端点不接受 tools 字段，本轮已自动降级为无工具调用。</summary>
    public bool ToolsRejected { get; init; }
}