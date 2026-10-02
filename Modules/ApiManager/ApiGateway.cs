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

    public async Task<string?> Chat(List<ChatMessage> history, AiEndpoint? over = null)
    {
        var cfg = over ?? _cfg;
        // 确保 URL 格式正确：不包含 /v1 但需要 /chat/completions
        var url = cfg.Url.TrimEnd('/');
        // 如果以 /v1 结尾，替换为 /v1/chat/completions
        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            url = url[..^3] + "/v1/chat/completions";
        else if (!url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            url += "/chat/completions";
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
}