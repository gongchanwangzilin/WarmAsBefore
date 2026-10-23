using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WarmAsBefore.Services;

/// <summary>
/// 画面生成：接 OpenAI 兼容的 /images/generations（也接受只返回 url 的服务）。
/// 用在装扮、场景背景、角色立绘与地图背景上——生成的是「素材」，不是屏幕上的临时图，
/// 所以统一落到存储根目录的 generated/ 下并返回绝对路径，后续由各业务自己复制/登记。
/// 未启用或没配密钥时明确失败，不偷偷给一张占位图。
/// </summary>
public sealed class ImageGenerationService
{
    private readonly StorageProvider _store;
    private readonly SettingsManager _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(150) };

    public ImageGenerationService(StorageProvider store, SettingsManager settings)
    {
        _store = store;
        _settings = settings;
    }

    public string Dir
    {
        get
        {
            var dir = Path.Combine(_store.Root, "generated");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public bool Enabled => _settings.Current.ImageGenEnabled
                           && !string.IsNullOrWhiteSpace(_settings.Current.ImageGenUrl)
                           && !string.IsNullOrWhiteSpace(_settings.Current.ImageGenKey);

    /// <summary>生成一张图，返回绝对路径；失败给 null 与一句人话。</summary>
    public async Task<(string? Path, string Message)> GenerateAsync(string prompt, string tag = "image", string? size = null)
    {
        var s = _settings.Current;
        if (!s.ImageGenEnabled) return (null, "画面生成没有开启（设置 → AI → 画面生成）。");
        if (string.IsNullOrWhiteSpace(s.ImageGenUrl)) return (null, "还没有填写画面生成的接口地址。");
        if (string.IsNullOrWhiteSpace(s.ImageGenKey)) return (null, "还没有填写画面生成的 API Key。");
        if (string.IsNullOrWhiteSpace(prompt)) return (null, "画面描述是空的。");

        var url = s.ImageGenUrl.Trim().TrimEnd('/');
        if (!url.EndsWith("/images/generations", StringComparison.OrdinalIgnoreCase))
            url += "/images/generations";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.ImageGenKey.Trim());
            var body = JsonSerializer.Serialize(new
            {
                model = string.IsNullOrWhiteSpace(s.ImageGenModel) ? "gpt-image-1" : s.ImageGenModel.Trim(),
                prompt,
                n = 1,
                size = string.IsNullOrWhiteSpace(size) ? (string.IsNullOrWhiteSpace(s.ImageGenSize) ? "1024x1024" : s.ImageGenSize) : size,
                response_format = "b64_json"
            });
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await _http.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                var brief = text.Length > 300 ? text[..300] + "…" : text;
                return (null, $"画面生成失败（HTTP {(int)resp.StatusCode}）：{brief}");
            }

            using var doc = JsonDocument.Parse(text);
            var item = doc.RootElement.TryGetProperty("data", out var arr) && arr.ValueKind == JsonValueKind.Array && arr.GetArrayLength() > 0
                ? arr[0]
                : default;
            if (item.ValueKind != JsonValueKind.Object) return (null, "画面生成接口没有返回图片。");

            var safeTag = string.Concat((tag ?? "image").Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
            if (safeTag.Length == 0) safeTag = "image";
            var path = Path.Combine(Dir, $"{safeTag}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}.png");

            if (item.TryGetProperty("b64_json", out var b64) && b64.ValueKind == JsonValueKind.String)
            {
                var bytes = Convert.FromBase64String(b64.GetString() ?? "");
                await File.WriteAllBytesAsync(path, bytes);
                return (path, "已生成画面。");
            }
            if (item.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
            {
                var remote = u.GetString() ?? "";
                if (remote.Length == 0) return (null, "画面生成接口返回了空地址。");
                var bytes = await _http.GetByteArrayAsync(remote);
                await File.WriteAllBytesAsync(path, bytes);
                return (path, "已生成画面。");
            }
            return (null, "画面生成接口的返回里没有 b64_json 或 url。");
        }
        catch (Exception ex)
        {
            App.WriteLog("ImageGeneration -> " + ex.Message);
            return (null, "画面生成出错：" + ex.Message);
        }
    }
}
