using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Sandbox;

/// <summary>
/// 沙箱策略门面：把敏感词过滤、信任名单、加密落盘串成工具执行链的一环。
///
/// 隔离约定（不重蹈 DeepSeek Harness 的安全漏洞）：
///  · 外部工具参数/输出都过敏感词过滤；命中「已配置密钥指纹」即阻断或脱敏；
///  · 插件/工具默认无法动「模型相关数据」（AiKey / 对话历史）；
///    需用户在设置中把该工具加入信任名单（按路径哈希）才放开；
///  · 信任名单 / 审计 / 敏感配置全部 AES-GCM 加密落盘 + HMAC-SHA256 防篡改。
/// </summary>
public sealed class SandboxPolicy
{
    private readonly SensitiveFilter _filter = new();
    private readonly TrustStore _trust;
    private readonly CryptoStore _crypto;
    private readonly AuditLog _audit;
    private readonly StorageProvider _store;

    /// <summary>命中敏感数据时是否阻断（默认阻断；设为 false 仅脱敏放行）。</summary>
    public bool BlockOnHit { get; set; } = true;

    public TrustStore Trust => _trust;
    public CryptoStore Crypto => _crypto;
    public AuditLog Audit => _audit;

    public SandboxPolicy(StorageProvider store)
    {
        _store = store;
        var root = Path.Combine(store.Root, "..");
        _trust = new TrustStore(root);
        _crypto = new CryptoStore(root);
        _audit = new AuditLog(root);
        // 后台刷新密钥指纹。绝不 sync-over-async（GetAwaiter().GetResult()），
        // 那在 WinUI 同步上下文里会死锁 → 界面卡死。
        _ = RefreshKeyFingerprintsAsync();
    }

    /// <summary>
    /// 刷新动态密钥指纹（从 UserSettings 读取已配置的 ApiKey / 密钥）。
    /// 纯异步，调用方 fire-and-forget，绝不阻塞 UI 线程。
    /// </summary>
    public async Task RefreshKeyFingerprintsAsync()
    {
        try
        {
            var raw = await _store.LoadRawAsync("settings");
            if (string.IsNullOrWhiteSpace(raw)) return;
            var s = JsonDocument.Parse(raw).RootElement.Deserialize<UserSettings>();
            if (s is null) return;
            _filter.SetDynamicFingerprints(new[]
            {
                s.AiKey, s.VoiceApiKey, s.ChessApiKey,
                s.QqAppSecret, s.WechatAppSecret, s.WechatToken
            });
        }
        catch { /* 指纹刷新失败不影响主流程 */ }
    }

    /// <summary>同步版本（仅在确实需要密钥指纹的调用方使用，如设置页手动触发；UI 线程不要调）。</summary>
    public void RefreshKeyFingerprints()
    {
        try
        {
            // 非阻塞路径：直接用同步 IO 读 settings 文件（不走 async，避免 sync-over-async）
            var path = System.IO.Path.Combine(_store.Root, "..", "settings.json");
            if (!System.IO.File.Exists(path))
                path = System.IO.Path.Combine(_store.Root, "settings.json");
            if (!System.IO.File.Exists(path)) return;
            var raw = System.IO.File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(raw)) return;
            var s = JsonDocument.Parse(raw).RootElement.Deserialize<UserSettings>();
            if (s is null) return;
            _filter.SetDynamicFingerprints(new[]
            {
                s.AiKey, s.VoiceApiKey, s.ChessApiKey,
                s.QqAppSecret, s.WechatAppSecret, s.WechatToken
            });
        }
        catch { /* 指纹刷新失败不影响主流程 */ }
    }

    /// <summary>
    /// 工具执行前网关：校验参数中是否携带受保护数据。
    /// 返回 (放行?, 脱敏后参数, 阻断原因?)。
    /// 未信任外部工具携带密钥指纹 → 阻断 + 审计；信任工具 → 脱敏放行 + 审计。
    /// </summary>
    public async Task<(bool Allowed, string MaskedArgs, string? BlockReason)>
    GateInputAsync(string toolName, string toolPath, string argsJson, bool isExternal)
    {
        var scan = _filter.Scan(argsJson ?? "");
        if (scan.HitCategories.Count == 0)
            return (true, argsJson ?? "", null);

        var trusted = _trust.IsTrusted(toolName, toolPath);
        var detail = "敏感命中: " + string.Join("; ", scan.HitCategories);

        if (!trusted)
        {
            await _audit.RecordAsync(BlockOnHit ? "tool-input-blocked" : "tool-input-sanitized",
                toolName, detail, consented: false);
            if (BlockOnHit)
                return (false, scan.MaskedText, $"未信任工具 {toolName} 携带受保护数据，已阻断（在设置→工具安全中放开信任）");
        }
        else
        {
            await _audit.RecordAsync("tool-input-sanitized", toolName, detail, consented: true);
        }

        return (true, scan.MaskedText, null);
    }

    /// <summary>工具输出网关：脱敏返回（命中动态指纹打码 + 审计）。</summary>
    public string GateOutput(string toolName, string output)
    {
        var scan = _filter.Scan(output ?? "");
        if (scan.HitCategories.Count > 0)
            _ = _audit.RecordAsync("tool-output-sanitized", toolName,
                "敏感命中: " + string.Join("; ", scan.HitCategories), consented: true);
        return scan.MaskedText;
    }

    /// <summary>模型数据访问门：仅信任工具可读取模型相关数据（如 AiKey 摘要）。</summary>
    public bool CanAccessModelData(string toolName, string toolPath)
        => _trust.IsTrusted(toolName, toolPath);

    /// <summary>把一条敏感配置值加密落盘（AES-GCM + HMAC 防篡改）。</summary>
    public void StoreSecret(string keyName, string value)
        => _crypto.EncryptAndStore(keyName + ".json",
            JsonSerializer.Serialize(new { value, ts = DateTime.Now }));

    /// <summary>读取加密配置；篡改/缺失返回 null。</summary>
    public string? LoadSecret(string keyName) => _crypto.TryRead(keyName + ".json");
}
