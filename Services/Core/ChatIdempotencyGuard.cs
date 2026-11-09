using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace WarmAsBefore.Services.Core;

/// <summary>
/// 聊天轮幂等：上下文中每句只留一次、同一回合只向 Web 提交一遍、断线重连/重试不重复提交。
/// turnKey 不含 sessionId（会话 ID），所以重连后仍能命中同一键。
/// </summary>
public sealed class ChatIdempotencyGuard
{
    private readonly ConcurrentDictionary<string, DateTime> _seen = new(StringComparer.Ordinal);

    /// <summary>回合键：对话 ID + 回合序号 + 内容哈希，跨重连稳定。</summary>
    public static string TurnKey(string conversationId, long turnSeq, string content)
    {
        string raw = (conversationId ?? "") + "\u0001" + turnSeq.ToString(System.Globalization.CultureInfo.InvariantCulture)
                     + "\u0001" + (content ?? "");
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>同一回合只放行一次；重复调用返回 false，调用方直接返回缓存结果、不再请求模型。</summary>
    public bool TryBegin(string turnKey)
    {
        if (string.IsNullOrEmpty(turnKey)) return false;
        return _seen.TryAdd(turnKey, DateTime.UtcNow);
    }

    /// <summary>上下文中每句哈希只保留一次，保持原有顺序。</summary>
    public static IReadOnlyList<string> DedupContext(IEnumerable<string> hashes)
    {
        if (hashes is null) return Array.Empty<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (string h in hashes)
        {
            if (string.IsNullOrEmpty(h)) continue;
            if (seen.Add(h)) result.Add(h);
        }
        return result;
    }

    /// <summary>清理过期键，避免长期运行内存增长。回合结束后保留 5 分钟足够覆盖重连重试。</summary>
    public void Purge(TimeSpan ttl)
    {
        DateTime cutoff = DateTime.UtcNow - ttl;
        foreach (var pair in _seen.ToArray())
        {
            if (pair.Value < cutoff) _seen.TryRemove(pair.Key, out _);
        }
    }
}
