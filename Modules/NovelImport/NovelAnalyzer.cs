using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.ApiManager;

namespace WarmAsBefore.Modules.NovelImport;

public sealed class NovelAnalyzer
{
    private readonly ApiGateway _api;

    public NovelAnalyzer(ApiGateway api) => _api = api;

    public async Task<ScenarioResult?> Analyze(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        var text = await File.ReadAllTextAsync(filePath);
        return await AnalyzeText(text);
    }

    public async Task<ScenarioResult?> AnalyzeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var msgs = new List<ChatMessage>
        {
            new() { Role = "system", Content = "分析以下文本，提取标题、摘要、角色、关键事件和分支选项。返回JSON。" },
            new() { Role = "user", Content = text.Length > 6000 ? text[..6000] : text }
        };
        var reply = await _api.Chat(msgs);
        if (reply is null) return null;
        try
        {
            return JsonSerializer.Deserialize<ScenarioResult>(reply, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });
        }
        catch { return null; }
    }
}