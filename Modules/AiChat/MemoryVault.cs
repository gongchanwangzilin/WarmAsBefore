using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.ApplicationModel;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.ApiManager;
using WarmAsBefore.Modules.Tools;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.AiChat;


public sealed class MemoryVault
{
    private readonly StorageProvider _store;
    private readonly MemoryScope _scope;
    private readonly object _gate = new();
    /// <summary>落盘闸：整表写入必须一次一份。多角色后台同时写时，并发快照会互相覆盖。</summary>
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private List<MemoryEntry> _cache = new();
    private Task? _loading;
    private string _scopeKey = "memories";

    /// <summary>生活/小游戏这类后台记忆每个角色最多留多少条：它们会持续产生，绝不能无限堆积。</summary>
    public const int AmbientCapPerCharacter = 200;

    private static readonly string[] AmbientCategories = { "life", "game" };

    public MemoryVault(StorageProvider store, MemoryScope scope)
    {
        _store = store;
        _scope = scope;
        _scopeKey = scope.Key("memories");
        scope.Changed += OnScopeChanged;
    }

    /// <summary>换档：全量记忆库也是这个存档的，不能带着上一个存档的缓存继续写。</summary>
    private void OnScopeChanged()
    {
        lock (_gate)
        {
            _cache = new();
            _loading = null;
            _scopeKey = _scope.Key("memories");
        }
    }

    /// <summary>日记按作用域分文件；共用那份沿用旧的 <c>diary_&lt;角色&gt;.json</c> 命名。</summary>
    private string DiaryKey(string charId)
        => _scope.IsShared ? $"diary_{charId}" : $"diary/{_scope.Current}/{charId}";

    /// <summary>惰性加载：首次真正读写前 await，避免构造函数 fire-and-forget 造成「旧记忆还没读进来就被覆盖」的竞态。</summary>
    private Task EnsureLoadedAsync()
    {
        lock (_gate) return _loading ??= LoadAsync();
    }

    private async Task LoadAsync()
    {
        var key = _scopeKey;
        try
        {
            var saved = await _store.Load<List<MemoryEntry>>(key);
            lock (_gate)
            {
                if (saved is not null && saved.Count > 0 && key == _scopeKey) _cache = saved;
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("MemoryVault.LoadAll -> " + ex);
        }
    }

    public async Task Store(MemoryEntry entry)
    {
        await EnsureLoadedAsync();
        await _writeGate.WaitAsync();
        try
        {
            List<MemoryEntry> snapshot;
            lock (_gate)
            {
                _cache.Add(entry);
                PruneAmbientLocked(_cache, entry.CharacterId);
                // 快照整份列表再序列化，避免序列化到一半列表被另一线程修改
                snapshot = new List<MemoryEntry>(_cache);
            }
            await _store.Save(_scopeKey, snapshot);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// 后台生活/小游戏记忆：单独一个类别、有配额，混不进对话记忆的检索权重里。
    /// 文本由调用方压到一句话——后台不该把整段独白背下来。
    /// </summary>
    public Task StoreAmbient(string charId, string content, string category = "life", string? keywords = null, int weight = 1)
        => Store(new MemoryEntry
        {
            CharacterId = charId,
            Content = content,
            Category = category,
            Keywords = keywords,
            Weight = weight
        });

    /// <summary>只裁后台产生的类别，对话与好感记忆一条不动。</summary>
    private static void PruneAmbientLocked(List<MemoryEntry> list, string charId)
    {
        var ambient = list.Where(m => m.CharacterId == charId && AmbientCategories.Contains(m.Category)).ToList();
        if (ambient.Count <= AmbientCapPerCharacter) return;
        foreach (var old in ambient.OrderBy(m => m.At).Take(ambient.Count - AmbientCapPerCharacter))
            list.Remove(old);
    }

    /// <summary>记录好感提升瞬间（回忆录的「全部好感时刻」来源，随记忆持久化）。</summary>
    /// <summary>
    /// 人类记忆层的入口：好感时刻/重要事件也会进「脑子里记得的」那层。
    /// 未注入时只写全量记忆库，行为与从前一致。
    /// </summary>
    public HumanMemoryService? Human { get; set; }

    public Task LogAffection(string charId, int delta, string reason, string? imagePath = null)
    {
        if (Human is not null)
        {
            // 带截图的时刻按「照片式记忆」存，没有图的按重要事件存——
            // 人只记得住事件、印象、重要时刻和单张图片，不会把过程全背下来。
            var hasImage = !string.IsNullOrWhiteSpace(imagePath);
            var text = string.IsNullOrWhiteSpace(reason) ? "一件值得记住的事" : reason;
            var kind = hasImage ? HumanMemoryKind.Image : HumanMemoryKind.Important;
            var importance = hasImage ? 0.8 : Math.Clamp(0.5 + Math.Abs(delta) / 40.0, 0.5, 0.95);
            _ = Human.StoreAsync(charId, text, kind, importance, imagePath ?? "");
        }
        return Store(new MemoryEntry
        {
            CharacterId = charId,
            Content = $"{delta:+0;-0;0} 好感（{reason}）",
            Category = "affection",
            Keywords = reason,
            Weight = delta,
            ImagePath = imagePath
        });
    }

    public Task<List<MemoryEntry>> Recent(string charId, int n = 10)
    {
        _ = EnsureLoadedAsync();
        lock (_gate)
            return Task.FromResult(_cache.Where(m => m.CharacterId == charId)
                .OrderByDescending(m => m.At).Take(n).ToList());
    }

    /// <summary>某个角色的全部记忆（可限定类别，如 affection / dialogue）。</summary>
    public Task<List<MemoryEntry>> All(string charId, string? category = null)
    {
        _ = EnsureLoadedAsync();
        lock (_gate)
            return Task.FromResult(_cache.Where(m => m.CharacterId == charId
                    && (category is null || m.Category == category))
                .OrderByDescending(m => m.At).ToList());
    }

    public Task<List<MemoryEntry>> Search(string charId, string query)
    {
        _ = EnsureLoadedAsync();
        IEnumerable<MemoryEntry> hits;
        lock (_gate)
        {
            hits = _cache.Where(m => m.CharacterId == charId).AsEnumerable();
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            try
            {
                var re = new Regex(query, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                hits = hits.Where(m => re.IsMatch(m.Content) || (m.Keywords is not null && re.IsMatch(m.Keywords)));
            }
            catch
            {
                hits = hits.Where(m => m.Content.Contains(query, StringComparison.OrdinalIgnoreCase));
            }
        }
        return Task.FromResult(hits.OrderByDescending(m => m.Weight).ThenByDescending(m => m.At).ToList());
    }

    public async Task<List<DiaryNote>> Diary(string charId)
    {
        var all = await _store.Load<List<DiaryNote>>(DiaryKey(charId));
        return all ?? new();
    }

    public async Task WriteDiary(string charId, string content, string mood)
    {
        var d = await Diary(charId);
        d.Add(new DiaryNote { CharacterId = charId, Content = content, Mood = mood });
        await _store.Save(DiaryKey(charId), d);
    }
}
