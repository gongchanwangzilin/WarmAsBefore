using System.Text;
using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.ApiManager;

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

        // v1.60 能力路由：先从轮换池里挑声明了 SupportsVision 的端点（按 Priority），
        // 没有看图的就退到声明了 SupportsOcr 的端点（OCR 兜底也能看图说话）。
        var routed = ResolveViaGateway(e => e.SupportsVision)
                     ?? ResolveViaGateway(e => e.SupportsOcr);
        if (routed is not null) return routed.Value;

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
    /// 从 ApiGateway 的轮换池按能力挑一个可用端点（Url/Key/Model 三项齐全）。
    /// 能力路由在无匹配时会回退全部端点，所以这里再判一次 capability，绝不误用不识图的端点。
    /// 任何异常都吞掉并回退 null，让上层继续走旧的厂商池/全局模型路径。
    /// </summary>
    private static (string Url, string Key, string Model)? ResolveViaGateway(Func<AiEndpoint, bool> capability)
    {
        try
        {
            var api = WarmAsBefore.Modules.Tools.ServicesHelper.GetServices()
                ?.GetService(typeof(ApiGateway)) as ApiGateway;
            if (api is null) return null;
            foreach (var e in api.CandidatesForCapability(capability))
            {
                if (e is null || !capability(e)) continue;   // 排除「无匹配回退」塞进来的不具备该能力者
                if (string.IsNullOrWhiteSpace(e.Url) || string.IsNullOrWhiteSpace(e.Key)
                    || string.IsNullOrWhiteSpace(e.Model)) continue;
                // 轮换池里的 Url 是厂商根地址（如 .../v1），vision 请求要补成 chat/completions。
                return (BuildVisionChatUrl(e.Url), e.Key.Trim(), e.Model.Trim());
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("VisionService.ResolveViaGateway -> " + ex.Message);
        }
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

    /// <summary>
    /// OCR：用声明了 SupportsOcr 的端点，靠视觉提示词把图里的文字读出来（不引入新 SDK）。
    /// 没有这样的端点时明确失败（返回 null 并写日志），不偷偷改用别的模型冒充 OCR 结果。
    /// </summary>
    public async Task<string?> OcrAsync(Stream image, string mime = "image/png")
    {
        if (image is null) return null;
        var target = ResolveViaGateway(e => e.SupportsOcr);
        if (target is null)
        {
            App.WriteLog("VisionService.Ocr: 没有声明 SupportsOcr 的端点，明确失败");
            return null;
        }
        try
        {
            using var ms = new MemoryStream();
            await image.CopyToAsync(ms);
            var bytes = ms.ToArray();
            if (bytes.Length == 0) return null;
            var b64 = Convert.ToBase64String(bytes);
            const string prompt =
                "你是一个精确的 OCR 引擎。请原样输出图片中出现的所有文字，保持原有的换行与阅读顺序；"
                + "不要解释、不要总结、不要补充图片里没有的内容。若图中没有任何文字，只输出：无文字。";
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
                max_tokens = 2000,
                temperature = 0
            };
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + target.Value.Key);
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await http.PostAsync(target.Value.Url, content);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                App.WriteLog("VisionService.Ocr HTTP " + (int)resp.StatusCode + ": " + body);
                return null;
            }
            using var doc = JsonDocument.Parse(body);
            var text = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            if (string.IsNullOrWhiteSpace(text)) return null;
            var trimmed = text.Trim();
            return string.Equals(trimmed, "无文字", StringComparison.Ordinal) ? null : trimmed;
        }
        catch (Exception ex)
        {
            App.WriteLog("VisionService.Ocr -> " + ex.Message);
            return null;
        }
    }

    /// <summary>把厂商根地址补成 chat/completions（已是完整地址则原样返回）。</summary>
    private static string BuildVisionChatUrl(string raw)
    {
        var url = raw.Trim().TrimEnd('/');
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return url;
        return url + "/chat/completions";
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