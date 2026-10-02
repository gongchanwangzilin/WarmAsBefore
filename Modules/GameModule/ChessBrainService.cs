using System.Text;
using System.Text.Json;
using WarmAsBefore.Modules.ApiManager;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.GameModule;

/// <summary>
/// 云端棋力脑（可选接入）：把"当前棋盘 + 合法走子清单"发给 OpenAI 兼容下棋 API，
/// 让模型选出最优一步。全程后台预热，绝不阻塞棋盘渲染；失败/超时自动回退本地启发式 AI。
/// 未配置或未启用时返回 null（引擎纯本地下棋，不依赖云端）。
/// </summary>
public sealed class ChessBrainService
{
    private readonly ApiGateway _api;
    private readonly SettingsManager _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(7) };

    public ChessBrainService(ApiGateway api, SettingsManager settings)
    {
        _api = api;
        _settings = settings;
    }

    public bool Enabled
    {
        get
        {
            var s = _settings.Current;
            return s.ChessApiEnabled && !string.IsNullOrWhiteSpace(s.ChessApiUrl);
        }
    }

    public string EndpointLabel
    {
        get
        {
            var s = _settings.Current;
            if (!s.ChessApiEnabled) return "本地棋力（未接入云端）";
            if (string.IsNullOrWhiteSpace(s.ChessApiUrl)) return "已开启，待填 API 地址";
            return $"云端棋力：{s.ChessApiModel}";
        }
    }

    /// <summary>在该局的合法走子里请求云端择优。返回 null = 云端不可用/非法/超时 → 调用方用本地兜底。</summary>
    public async Task<(int fr, int fc, int tr, int tc)?> SuggestBestAsync(
        MiniGameEngine.GameKind kind, string boardText, IReadOnlyList<(int fr, int fc, int tr, int tc)> legal)
    {
        if (!Enabled || legal.Count == 0) return null;
        // 压缩表示：仅传合法走子下标，模型只需在候选里选最优，避免自由格式 JSON 解析失败
        var gameName = kind switch
        {
            MiniGameEngine.GameKind.Chess => "国际象棋",
            MiniGameEngine.GameKind.ChineseChess => "中国象棋",
            MiniGameEngine.GameKind.AnimalChess => "斗兽棋",
            _ => "棋类"
        };
        var list = string.Join("\n", legal.Select((m, i) => $"{i}: {m.fr},{m.fc}→{m.tr},{m.tc}"));
        var system = "你是棋力强劲的下棋引擎。根据棋盘局势，从候选走法中选择最优的一步。只输出候选编号（纯数字），不要任何其他文字。";
        var user = $"游戏：{gameName}\n棋盘：\n{boardText}\n候选走法（编号: 起点→终点）：\n{list}\n请只输出一个编号数字。";

        try
        {
            var s = _settings.Current;
            var body = new
            {
                model = string.IsNullOrWhiteSpace(s.ChessApiModel) ? "gpt-4o-mini" : s.ChessApiModel,
                messages = new[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user }
                },
                temperature = 0.2,
                max_tokens = 10
            };
            var json = JsonSerializer.Serialize(body);
            using var req = new HttpRequestMessage(HttpMethod.Post, s.ChessApiUrl.Trim());
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {s.ChessApiKey}");

            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadAsStringAsync();
            var content = JsonDocument.Parse(raw)
                .RootElement.GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            // 提取首个数字（可能带引号/空格）
            var numStr = new string(content.Where(char.IsDigit).Take(3).ToArray());
            if (int.TryParse(numStr, out var idx) && idx >= 0 && idx < legal.Count)
                return legal[idx];
        }
        catch
        {
            // 静默回退本地
        }
        return null;
    }
}