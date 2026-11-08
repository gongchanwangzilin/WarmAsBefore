using System.Text;
using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// #13 识图服务：用「独立配置的识图模型」看图说话；没配识图端点时按顺序回退到
/// 标注了「能看图」的厂商模型，再回退到全局大模型——不让「看一眼」因为少填一项就整个不可用。
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
    /// 现在到底能不能识图：独立识图端点配好了，或者有一条标了「能看图」的厂商模型，
    /// 或者至少有一个全局大模型可以兜底（用户要求：没配识图模型时直接用大模型看）。
    /// </summary>
    public bool Available => ResolveTarget() is not null;

    /// <summary>这次会用谁来看图，给设置页/日志一句人话。</summary>
    public string TargetDescription
    {
        get
        {
            var s = _settings.Current;
            if (IsConfigured) return "独立识图端点 · " + s.VisionModel.Trim();
            var t = ResolveTarget();
            return t is null ? "没有可用的识图模型" : "回退到 " + t.Value.Model;
        }
    }

    /// <summary>
    /// 识图目标的解析顺序：
    /// 1) 设置里独立配的识图端点；
    /// 2) 厂商池里标注了 SupportsVision 的启用模型（按厂商顺序，第一家先用）；
    /// 3) 全局对话端点的大模型——没有识图模型也不该让「看一眼」整个不可用。
    /// </summary>
    private (string Url, string Key, string Model)? ResolveTarget()
    {
        var s = _settings.Current;
        if (IsConfigured)
            return (s.VisionUrl.Trim(), s.VisionKey.Trim(), s.VisionModel.Trim());

        foreach (var v in s.AiProviders ?? new List<AiProvider>())
        {
            if (!v.Enabled || string.IsNullOrWhiteSpace(v.Url)) continue;
            var key = (v.Keys ?? new List<string>()).FirstOrDefault(k => !string.IsNullOrWhiteSpace(k));
            if (string.IsNullOrWhiteSpace(key)) continue;
            var m = (v.Models ?? new List<AiProviderModel>())
                .FirstOrDefault(x => x.Enabled && x.SupportsVision && !string.IsNullOrWhiteSpace(x.Model));
            if (m is not null) return (v.Url.Trim(), key.Trim(), m.Model.Trim());
        }

        if (!string.IsNullOrWhiteSpace(s.AiUrl) && !string.IsNullOrWhiteSpace(s.AiKey)
            && !string.IsNullOrWhiteSpace(s.AiModel))
            return (s.AiUrl.Trim(), s.AiKey.Trim(), s.AiModel.Trim());
        return null;
    }

    /// <summary>
    /// 看图，返回一段描述。未配置 / 失败返回 null（调用方自行提示）。
    /// </summary>
    public async Task<string?> DescribeImageAsync(string imagePath, string prompt, int maxTokens = 200)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return null;
        return await DescribeBytesAsync(await File.ReadAllBytesAsync(imagePath), GuessMime(imagePath), prompt, maxTokens);
    }

    /// <summary>
    /// 图不在盘上、手上只有字节时的入口（Minecraft 桥的画面就是从 socket 上来的 PNG）。
    /// </summary>
    public async Task<string?> DescribeBytesAsync(byte[]? bytes, string mime, string prompt, int maxTokens = 200)
    {
        var target = ResolveTarget();
        if (target is null) return null;
        if (bytes is null || bytes.Length == 0) return null;
        try
        {
            var b64 = Convert.ToBase64String(bytes);
            var payload = new
            {
                model = target.Value.Model,
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new { type = "image_url", image_url = new { url = $"data:{(string.IsNullOrWhiteSpace(mime) ? "image/png" : mime.Trim())};base64,{b64}" } }
                        }
                    }
                },
                max_tokens = Math.Clamp(maxTokens, 32, 4096),
                temperature = 0.5
            };
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + target.Value.Key);
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await http.PostAsync(target.Value.Url, content);
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