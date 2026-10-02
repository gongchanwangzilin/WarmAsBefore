using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WarmAsBefore.Modules.Sandbox;

/// <summary>
/// 敏感词/敏感数据过滤器。
/// 规则：
///  1. 内置模式匹配（API key / 密钥 / 凭据类特征串，如 sk-、AKIA、LTAI、Bearer、password=、api_key= 等）。
///  2. 动态指纹：从设置中取已配置的 ApiKey / 密钥字段（AiKey、VoiceApiKey、WechatAppSecret 等），
///     把非空值加入指纹表；工具参数里出现任何指纹即命中。
/// 命中行为：默认阻断并返回脱敏提示；仅在用户对该工具「放开信任」后允许携带模型数据。
/// 不依赖 DeepSeek Harness 的未授权网络/文件系统暴露面 —— 只做内容级过滤。
/// </summary>
public sealed class SensitiveFilter
{
    /// <summary>命中后返回脱敏文本；同时给出命中的类别描述列表。</summary>
    public record ScanResult(string MaskedText, List<string> HitCategories);

    /// 内置敏感模式（大小写不敏感，匹配参数 JSON 原文）。
    private static readonly (string Label, Regex Re)[] BuiltinPatterns =
    {
        ("OpenAI 风格密钥", new Regex(@"sk-[A-Za-z0-9]{20,}", RegexOptions.Compiled)),
        ("Anthropic 风格密钥", new Regex(@"sk-ant-[A-Za-z0-9-]{20,}", RegexOptions.Compiled)),
        ("AWS Access Key", new Regex(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled)),
        ("阿里云 AccessKey", new Regex(@"LTAI[0-9A-Za-z]{12,20}", RegexOptions.Compiled)),
        ("GitHub Token", new Regex(@"gh[pousr]_[A-Za-z0-9]{20,}", RegexOptions.Compiled)),
        ("Bearer 凭据", new Regex(@"Bearer\s+[A-Za-z0-9._-]{20,}", RegexOptions.Compiled)),
        ("密码字段", new Regex(@"""?\b(password|passwd|pwd|secret)\b""?\s*[:=]\s*""[^""]{6,}", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        ("api_key 字段", new Regex(@"""?\bapi[_-]?key\b""?\s*[:=]\s*""[^""]{8,}", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
    };

    private readonly List<string> _dynamicFingerprints = new();

    public SensitiveFilter()
    {
    }

    /// <summary>把已配置的密钥值加入动态指纹表（仅保留非空值，每次更新前清空）。</summary>
    public void SetDynamicFingerprints(IEnumerable<string?> values)
    {
        _dynamicFingerprints.Clear();
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v) && v.Trim().Length >= 8)
                _dynamicFingerprints.Add(v.Trim());
        }
    }

    /// <summary>
    /// 扫描文本。返回脱敏后的文本与命中的类别描述。
    /// 动态指纹命中时脱敏为 [已配置密钥]，内置模式命中时脱敏为该模式前缀 + ***。
    /// </summary>
    public ScanResult Scan(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new ScanResult(text, new List<string>());

        var hits = new List<string>();
        var result = text;

        // 1. 动态指纹（优先级最高，精确匹配已配置的密钥全文）
        foreach (var fp in _dynamicFingerprints)
        {
            if (result.Contains(fp, StringComparison.Ordinal))
            {
                if (!hits.Contains("已配置的 API 密钥"))
                    hits.Add("已配置的 API 密钥");
                result = result.Replace(fp, "[已配置密钥]");
            }
        }

        // 2. 内置模式
        foreach (var (label, re) in BuiltinPatterns)
        {
            var matches = re.Matches(result);
            if (matches.Count > 0)
            {
                if (!hits.Contains(label))
                    hits.Add(label);
                result = re.Replace(result, m =>
                {
                    // 保留前 8 字符用于日志定位，其余打码
                    var raw = m.Value;
                    return raw.Length > 8 ? raw[..8] + "***" : "***";
                });
            }
        }

        return new ScanResult(result, hits);
    }

    public bool HasHits(string text)
    {
        var r = Scan(text);
        return r.HitCategories.Count > 0;
    }
}

/// <summary>
/// 审计日志：JSONL 追加写，记录工具调用/敏感命中/信任变更，含时间戳与 consented 标志。
/// 与 CryptoStore 同目录（.sandbox/audit.jsonl）。
/// </summary>
public sealed class AuditLog
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AuditLog(string rootDir)
    {
        var dir = Path.Combine(rootDir, ".sandbox");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "audit.jsonl");
    }

    public async Task RecordAsync(string action, string toolName, string detail, bool consented)
    {
        var entry = new
        {
            ts = DateTime.Now.ToString("o"),
            action,
            tool = toolName,
            detail,
            consented
        };
        var line = System.Text.Json.JsonSerializer.Serialize(entry);
        await _lock.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(_path, line + "\n");
        }
        finally
        {
            _lock.Release();
        }
    }
}
