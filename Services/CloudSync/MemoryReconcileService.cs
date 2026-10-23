using System.Text.Json;
using System.Text.Json.Nodes;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.ApiManager;

namespace WarmAsBefore.Services.CloudSync;

/// <summary>
/// 跨设备合并后的记忆整理：两端同时使用时，同一件事可能被各记一遍。
///
/// 不做自动「猜合并」——只让对话模型挑出几乎重复的条目序号，代码按序号删除。
/// 模型不参与改写内容，删下来的也只是重复项；没配模型、或整理失败时原样保留，
/// 云存档本身不受影响。每份文件每个会话最多整理一次，避免反复花钱。
/// </summary>
public sealed class MemoryReconcileService
{
    private const int MaxEntries = 120;
    private const int DigestEntries = 70;

    private readonly StorageProvider _store;
    private readonly SettingsManager _settings;
    private readonly ApiGateway _api;
    private readonly MemoryScope _scope;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _done = new(StringComparer.Ordinal);

    public MemoryReconcileService(StorageProvider store, SettingsManager settings, ApiGateway api, MemoryScope scope)
    {
        _store = store;
        _settings = settings;
        _api = api;
        _scope = scope;
    }

    /// <summary>整理这次跨设备合并过的记忆文件；返回一句给界面看的结果。</summary>
    public async Task<string> ReconcileAsync(IReadOnlyList<string> appliedKeys)
    {
        if (!_settings.Current.CloudReconcileMemory) return "";
        var keys = appliedKeys.Where(IsMemoryStore).Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count == 0) return "";
        if (!_api.HasAnyEndpoint) return "未配置对话模型，跳过记忆整理";
        if (!await _gate.WaitAsync(0)) return "上一次记忆整理还没结束";

        var removed = 0;
        var touched = 0;
        try
        {
            foreach (var key in keys)
            {
                lock (_done)
                {
                    if (!_done.Add(key)) continue;    // 本次运行已经整理过这份文件
                }
                try
                {
                    var n = await ReconcileOneAsync(key);
                    if (n > 0) { removed += n; touched++; }
                }
                catch (Exception ex)
                {
                    App.WriteLog("MemoryReconcile " + key + " -> " + ex.Message);
                }
            }
        }
        finally { _gate.Release(); }

        if (touched == 0) return "";
        _scope.InvalidateCaches();   // 各记忆服务手里的缓存已经旧了
        return $"记忆整理：去掉 {removed} 条双端重复";
    }

    private static bool IsMemoryStore(string key)
    {
        var top = key.Split('/')[0];
        return top is "human_memories" or "memories";
    }

    private async Task<int> ReconcileOneAsync(string key)
    {
        var raw = await _store.LoadRawAsync(key);
        if (string.IsNullOrWhiteSpace(raw)) return 0;

        JsonArray? array;
        try { array = JsonNode.Parse(raw) as JsonArray; }
        catch { return 0; }
        if (array is null || array.Count < 4 || array.Count > MaxEntries) return 0;

        var lines = new List<string>();
        for (var i = 0; i < array.Count && lines.Count < DigestEntries; i++)
        {
            if (array[i] is not JsonObject o) continue;
            var text = StringOf(o, "Text") ?? StringOf(o, "Content");
            if (string.IsNullOrWhiteSpace(text)) continue;
            var at = StringOf(o, "CreatedAt") ?? StringOf(o, "At") ?? "";
            var who = StringOf(o, "CharacterId") ?? "";
            if (at.Length > 19) at = at[..19];
            if (text.Length > 90) text = text[..90] + "…";
            lines.Add($"{i}. [{at}] {who} {text}");
        }
        if (lines.Count < 6) return 0;

        var prompt =
            "两台设备同时玩了同一个存档，记忆被合并到了一起。下面每一行是一条记忆（序号. 时间 角色 内容）。\n" +
            "请只找出「同一件事被记了两遍」或「一条是另一条的子集」的重复项，保留信息更完整的那一条；" +
            "时间不同的两次相似经历不算重复，不要删。\n" +
            "只输出 JSON，不要解释：{\"drop\":[要删除的序号,...]}；没有重复就输出 {\"drop\":[]}。\n\n" +
            string.Join("\n", lines);

        var reply = await _api.Chat(new List<ChatMessage>
        {
            new() { Role = "system", Content = "你是记忆整理器。只输出 JSON，不写任何说明。" },
            new() { Role = "user", Content = prompt }
        });
        if (string.IsNullOrWhiteSpace(reply)) return 0;

        var drop = ParseDrop(reply);
        if (drop.Count == 0) return 0;

        var removed = 0;
        foreach (var idx in drop.Distinct().OrderByDescending(x => x))
        {
            if (idx < 0 || idx >= array.Count) continue;
            array.RemoveAt(idx);
            removed++;
        }
        if (removed == 0) return 0;

        var json = array.ToJsonString(new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        await _store.WriteRawAsync(key, json);
        App.WriteLog($"MemoryReconcile: {key} 去掉 {removed} 条重复记忆");
        return removed;
    }

    private static List<int> ParseDrop(string reply)
    {
        var result = new List<int>();
        try
        {
            var start = reply.IndexOf('{');
            var end = reply.LastIndexOf('}');
            if (start < 0 || end <= start) return result;
            using var doc = JsonDocument.Parse(reply[start..(end + 1)]);
            if (!doc.RootElement.TryGetProperty("drop", out var drop) || drop.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var item in drop.EnumerateArray())
                if (item.TryGetInt32(out var idx)) result.Add(idx);
        }
        catch (Exception ex) { App.WriteLog("MemoryReconcile.Parse -> " + ex.Message); }
        return result;
    }

    private static string? StringOf(JsonObject o, string name)
    {
        try
        {
            var node = o[name];
            if (node is null) return null;
            return node is JsonValue v && v.TryGetValue<string>(out var s) ? s : node.ToJsonString();
        }
        catch { return null; }
    }
}
