using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>平行宇宙：小说导入后生成的世界。持久化到 novels 列表。</summary>
public sealed class NovelLibrary
{
    private readonly StorageProvider _store;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private List<NovelEntry> _cache = new();
    private bool _loaded;

    public NovelLibrary(StorageProvider store) => _store = store;

    public async Task<List<NovelEntry>> ListAsync()
    {
        await EnsureLoaded();
        return _cache.OrderByDescending(n => n.ImportedAt).ToList();
    }

    public async Task<NovelEntry?> GetAsync(string id)
    {
        await EnsureLoaded();
        return _cache.FirstOrDefault(n => n.Id == id);
    }

    public async Task<NovelEntry> AddAsync(ScenarioResult result)
    {
        await EnsureLoaded();
        var entry = new NovelEntry
        {
            Id = Guid.NewGuid().ToString("N")[..10],
            Title = string.IsNullOrWhiteSpace(result.Title) ? "未命名世界" : result.Title.Trim(),
            Summary = result.Summary,
            Characters = result.Characters,
            Branches = result.Branches
        };
        _cache.Add(entry);
        await Save();
        return entry;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        await EnsureLoaded();
        var removed = _cache.RemoveAll(n => n.Id == id) > 0;
        if (removed) await Save();
        return removed;
    }

    private async Task EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var saved = await _store.Load<List<NovelEntry>>("novels");
            if (saved is not null) _cache = saved;
        }
        catch (Exception ex)
        {
            App.WriteLog("NovelLibrary.Load -> " + ex);
        }
    }

    private Task Save() => _store.Save("novels", _cache);
}

public sealed record NovelEntry
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";
    public List<string> Characters { get; init; } = new();
    public List<PlotBranch> Branches { get; init; } = new();
    public DateTime ImportedAt { get; init; } = DateTime.Now;
}
