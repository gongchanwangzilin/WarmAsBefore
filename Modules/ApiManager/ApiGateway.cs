using System.Text;
using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Modules.ApiManager;

public sealed class ApiGateway
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private AiEndpoint _cfg = new();

    public void Configure(AiEndpoint cfg) => _cfg = cfg;

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
        var body = new
        {
            model = cfg.Model,
            messages = history.Select(m => new { role = m.Role, content = m.Content }),
            temperature = cfg.Temperature,
            max_tokens = cfg.MaxTokens
        };
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
            System.Diagnostics.Debug.WriteLine($"[Gateway] {ex.Message}");
            return null;
        }
    }
}