using System.IO;
using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.Worldbook;

namespace WarmAsBefore.Services;

/// <summary>
/// 世界树：一份份「这个世界是什么样」的设定，存档可以绑定其中一份。
///
/// 它和世界书是同一个东西，只是名字更直白：世界树是树，存档是树上结的一局。
/// 世界树随时能改（人写或 AI 写）；改完之后当前存档下一次初始化/重建记忆时生效。
/// 不绑定世界树的存档是普通酒馆——没有「发生在哪儿」，地图整块锁上。
/// </summary>
public sealed class WorldTreeService
{
    /// <summary>落盘键。旧版世界书是 worldbooks/*.json 散文件，首次读取时合并进来。</summary>
    private const string StoreName = "world_tree";

    private readonly StorageProvider _store;
    private readonly WorldbookGenerator _generator;
    private readonly object _gate = new();
    private List<WorldbookEntry>? _cache;
    private Task<List<WorldbookEntry>>? _loading;

    public WorldTreeService(StorageProvider store, WorldbookGenerator generator)
    {
        _store = store;
        _generator = generator;
    }

    public async Task<List<WorldbookEntry>> ListAsync()
    {
        await EnsureLoadedAsync();
        lock (_gate) return _cache!.OrderByDescending(w => w.CreatedAt).ToList();
    }

    public async Task<WorldbookEntry?> GetAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var list = await ListAsync();
        return list.FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.Ordinal));
    }

    public async Task SaveAsync(WorldbookEntry entry)
    {
        if (entry is null) return;
        await EnsureLoadedAsync();
        lock (_gate)
        {
            var idx = _cache!.FindIndex(w => string.Equals(w.Id, entry.Id, StringComparison.Ordinal));
            if (idx >= 0) _cache[idx] = entry;
            else _cache.Add(entry);
        }
        await PersistAsync();
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await EnsureLoadedAsync();
        bool removed;
        lock (_gate) removed = _cache!.RemoveAll(w => string.Equals(w.Id, id, StringComparison.Ordinal)) > 0;
        if (removed) await PersistAsync();
        // 旧世界书的散文件也要一起删：下次启动时迁移会把留下的那一份重新捡回来，
        // 用户刚删掉的世界就会「复活」。
        try
        {
            var legacy = Path.Combine(_store.Root, "worldbooks", id + ".json");
            if (File.Exists(legacy)) File.Delete(legacy);
        }
        catch (Exception ex) { App.WriteLog("WorldTree.DeleteLegacy -> " + ex.Message); }
        return removed;
    }

    /// <summary>AI 造一份世界树：先生成，再落盘，返回可直接绑定的一条。</summary>
    public async Task<WorldbookEntry> GenerateAsync(string description, WorldbookMode mode = WorldbookMode.TextOnly)
    {
        var result = await _generator.GenerateAsync(description, generateCover: false, mode);
        var entry = result.Worldbook;
        if (string.IsNullOrWhiteSpace(entry.Title))
            entry.Title = description.Length > 18 ? description[..18] : description;
        if (string.IsNullOrWhiteSpace(entry.Description))
            entry.Description = description.Trim();
        entry.UserDescription = description.Trim();
        entry.Mode = mode;
        await SaveAsync(entry);
        return entry;
    }

    /// <summary>AI 润色一份已有世界树：只改文字，不动 id / 创建时间，用户手写的角色名尽量保留。</summary>
    public async Task<(bool Ok, string Message)> PolishAsync(WorldbookEntry entry)
    {
        if (entry is null) return (false, "没有可润色的世界树。");
        var prompt =
            "你是世界观编辑。在不改变事实与人物关系的前提下，把下面这份设定写得更具体、更有生活质感，"
            + "少用形容词堆砌，多给可被剧情用上的细节。只输出 JSON，不要 markdown 代码块：\n"
            + "{\"title\":\"标题\",\"description\":\"世界观描述\","
            + "\"characters\":[{\"name\":\"角色名\",\"gender\":\"女/男/其他\",\"personality\":\"性格\",\"description\":\"角色简介\"}]}\n\n"
            + $"标题：{entry.Title}\n世界观：{entry.Description}\n"
            + "角色：" + string.Join("；", entry.Characters.Select(c =>
                $"{c.Name}（{c.Gender}，{c.Personality}）：{c.Description}"));
        try
        {
            var reply = await _generator.ChatAsync(new List<ChatMessage> { new() { Role = "user", Content = prompt } });
            var json = ExtractJson(reply);
            if (json is null) return (false, "AI 这次没有给出可解析的内容，原文未改动。");
            var parsed = JsonSerializer.Deserialize<WorldbookGenerator.WorldbookJson>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (parsed is null) return (false, "AI 返回的格式不对，原文未改动。");

            if (!string.IsNullOrWhiteSpace(parsed.Title)) entry.Title = parsed.Title.Trim();
            if (!string.IsNullOrWhiteSpace(parsed.Description)) entry.Description = parsed.Description.Trim();
            var fresh = (parsed.Characters ?? new()).Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();
            if (fresh.Count > 0)
            {
                // 同名角色保留原立绘路径，其余按 AI 新给的重写
                var old = entry.Characters.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
                entry.Characters.Clear();
                foreach (var c in fresh)
                {
                    old.TryGetValue(c.Name, out var prev);
                    entry.Characters.Add(new WorldbookCharacter
                    {
                        Name = c.Name.Trim(),
                        Gender = string.IsNullOrWhiteSpace(c.Gender) ? prev?.Gender ?? "女" : c.Gender,
                        Personality = c.Personality,
                        Description = c.Description,
                        HasSprite = prev?.HasSprite ?? c.HasSprite,
                        SpritePath = prev?.SpritePath ?? c.SpritePath ?? ""
                    });
                }
            }
            entry.AiPolished = true;
            await SaveAsync(entry);
            return (true, "已润色并保存。");
        }
        catch (Exception ex)
        {
            App.WriteLog("WorldTree.Polish -> " + ex);
            return (false, "润色失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 给记忆层用的一段世界概述：只写「这个世界里一直成立的事」，
    /// 不写具体剧情——剧情该由这一局的相处长出来。
    /// </summary>
    public async Task<string> DescribeForPromptAsync(string id)
    {
        var entry = await GetAsync(id);
        if (entry is null) return "";
        var sb = new System.Text.StringBuilder();
        sb.Append(entry.Title);
        if (!string.IsNullOrWhiteSpace(entry.Description)) sb.Append("：").Append(entry.Description.Trim());
        if (entry.Characters.Count > 0)
        {
            sb.Append("\n这个世界里的人：");
            sb.Append(string.Join("；", entry.Characters.Take(8).Select(c =>
                c.Name + (string.IsNullOrWhiteSpace(c.Personality) ? "" : "（" + c.Personality.Trim() + "）"))));
        }
        return sb.ToString().Trim();
    }

    /// <summary>世界树给人看的一句话摘要（卡列表用）。</summary>
    public static string SummaryOf(WorldbookEntry entry)
    {
        if (entry is null) return "";
        var text = string.IsNullOrWhiteSpace(entry.Description) ? entry.UserDescription : entry.Description;
        text = text.Trim().Replace('\n', ' ');
        return text.Length <= 80 ? text : text[..80] + "…";
    }

    private async Task EnsureLoadedAsync()
    {
        Task<List<WorldbookEntry>> loading;
        lock (_gate) loading = _loading ??= LoadAsync();
        await loading;
    }

    private async Task<List<WorldbookEntry>> LoadAsync()
    {
        var list = new List<WorldbookEntry>();
        try
        {
            var saved = await _store.Load<List<WorldbookEntry>>(StoreName);
            if (saved is not null) list.AddRange(saved);
        }
        catch (Exception ex)
        {
            App.WriteLog("WorldTree.Load -> " + ex.Message);
        }

        // 旧版世界书：worldbooks/<id>.json 散文件。搬进同一张表，原文件留着不删。
        try
        {
            var dir = Path.Combine(_store.Root, "worldbooks");
            if (Directory.Exists(dir))
            {
                var known = list.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var file in Directory.GetFiles(dir, "*.json"))
                {
                    try
                    {
                        var legacy = JsonSerializer.Deserialize<WorldbookEntry>(await File.ReadAllTextAsync(file));
                        if (legacy is null || string.IsNullOrWhiteSpace(legacy.Id) || !known.Add(legacy.Id)) continue;
                        list.Add(legacy);
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("WorldTree.MigrateLegacy -> " + ex.Message);
        }

        lock (_gate) _cache = list;
        if (list.Count > 0) await _store.Save(StoreName, list);
        return list;
    }

    private async Task PersistAsync()
    {
        List<WorldbookEntry> snapshot;
        lock (_gate) snapshot = (_cache ?? new()).ToList();
        await _store.Save(StoreName, snapshot);
    }

    private static string? ExtractJson(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : null;
    }
}
