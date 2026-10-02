using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WarmAsBefore.Modules.Sandbox;

/// <summary>
/// 信任名单：控制哪些外部工具允许访问「模型相关数据」（如当前 AiKey、对话历史摘要等）。
/// 默认所有外部工具都无法读取模型数据；用户在设置页把工具加入信任后，
/// 该工具调用 get_state / 工具参数注入时才允许携带密钥指纹。
///
/// 实现：trust.jsonl（每行一个工具条目），SHA-256 内容哈希防篡改。
/// </summary>
public sealed class TrustStore
{
    private readonly string _file;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private byte[] _integrityHash = Array.Empty<byte>();

    public TrustStore(string rootDir)
    {
        _file = Path.Combine(rootDir, ".sandbox", "trust.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        LoadIntegrity();
    }

    private void LoadIntegrity()
    {
        try
        {
            var side = _file + ".sha256";
            if (File.Exists(side))
                _integrityHash = File.ReadAllBytes(side);
        }
        catch { _integrityHash = Array.Empty<byte>(); }
    }

    /// <summary>是否篡改过（内容哈希不匹配时返回 true，调用方应清空信任）。</summary>
    public bool IsTampered()
    {
        try
        {
            if (!File.Exists(_file)) return false;
            var content = File.ReadAllBytes(_file);
            var hash = SHA256.HashData(content);
            return !_integrityHash.SequenceEqual(hash);
        }
        catch { return true; }
    }

    private void RecomputeIntegrity()
    {
        try
        {
            if (!File.Exists(_file))
            {
                _integrityHash = Array.Empty<byte>();
                File.WriteAllBytes(_file + ".sha256", _integrityHash);
                return;
            }
            var hash = SHA256.HashData(File.ReadAllBytes(_file));
            _integrityHash = hash;
            File.WriteAllBytes(_file + ".sha256", hash);
        }
        catch { }
    }

    public List<TrustEntry> All()
    {
        var list = new List<TrustEntry>();
        try
        {
            if (!File.Exists(_file)) return list;
            foreach (var line in File.ReadLines(_file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var t = JsonDocument.Parse(line).RootElement.Deserialize<TrustEntry>();
                if (t is not null) list.Add(t);
            }
        }
        catch { }
        return list;
    }

    public bool IsTrusted(string toolName, string toolPath)
    {
        var all = All();
        return all.Any(e =>
            string.Equals(e.ToolName, toolName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormalizePath(e.ToolPath), NormalizePath(toolPath), StringComparison.OrdinalIgnoreCase));
    }

    public void Add(string toolName, string toolPath, string note)
    {
        _lock.Wait();
        try
        {
            var entry = new TrustEntry(toolName, toolPath, DateTime.Now, note);
            File.AppendAllText(_file, JsonSerializer.Serialize(entry) + "\n");
            RecomputeIntegrity();
        }
        finally { _lock.Release(); }
    }

    public void Remove(string toolName, string toolPath)
    {
        _lock.Wait();
        try
        {
            var remaining = All()
                .Where(e => !(
                    string.Equals(e.ToolName, toolName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(NormalizePath(e.ToolPath), NormalizePath(toolPath), StringComparison.OrdinalIgnoreCase)))
                .ToList();
            File.WriteAllLines(_file, remaining.Select(e => JsonSerializer.Serialize(e)));
            RecomputeIntegrity();
        }
        finally { _lock.Release(); }
    }

    private static string NormalizePath(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        var full = Path.GetFullPath(p);
        return full.TrimEnd(Path.DirectorySeparatorChar).Replace('\\', '/');
    }
}

/// <summary>信任名单条目：工具名 + 目录路径 + 加入时间 + 备注。</summary>
public sealed record TrustEntry(string ToolName, string ToolPath, DateTime AddedAt, string Note);
