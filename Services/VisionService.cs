using System.Text;
using System.Text.Json;

namespace WarmAsBefore.Services;

/// <summary>
/// #13 识图服务：用「独立配置的识图模型」看图说话。
/// 走 OpenAI 兼容的 vision 消息格式（content 数组 + image_url base64）。
/// 主要用于 #12 表情包自动描述；其它看图需求也可复用。
/// </summary>
public sealed class VisionService
{
    private readonly SettingsManager _settings;

    public VisionService(SettingsManager settings) => _settings = settings;

    /// <summary>识图端点是否配置完整（三项都填）。</summary>
    public bool IsConfigured
    {
        get
        {
            var s = _settings.Current;
            return !string.IsNullOrWhiteSpace(s.VisionUrl)
                && !string.IsNullOrWhiteSpace(s.VisionKey)
                && !string.IsNullOrWhiteSpace(s.VisionModel);
        }
    }

    /// <summary>
    /// 看图，返回一段描述。未配置 / 失败返回 null（调用方自行提示）。
    /// </summary>
    public async Task<string?> DescribeImageAsync(string imagePath, string prompt)
    {
        var s = _settings.Current;
        if (!IsConfigured) return null;
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(imagePath);
            var b64 = Convert.ToBase64String(bytes);
            var mime = GuessMime(imagePath);
            var payload = new
            {
                model = s.VisionModel.Trim(),
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new { type = "image_url", image_url = new { url = $"data:{mime};base64,{b64}" } }
                        }
                    }
                },
                max_tokens = 200,
                temperature = 0.5
            };
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + s.VisionKey.Trim());
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await http.PostAsync(s.VisionUrl.Trim(), content);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                App.WriteLog("VisionService HTTP " + (int)resp.StatusCode + ": " + body);
                return null;
            }
            using var doc = JsonDocument.Parse(body);
            var text = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (Exception ex)
        {
            App.WriteLog("VisionService.Describe -> " + ex.Message);
            return null;
        }
    }

    private static string GuessMime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        _ => "image/jpeg"
    };
}