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
        List<MemoryEntry>? pendingOld = null;
        string oldKey;
        lock (_gate)
        {
            // 旧档还有没落盘的记忆：先把它们写回**旧档**的键，再切缓存。
            // 否则防抖窗口内换档，旧档记忆会被写进新档文件（审计 P3）。
            if (_dirty) pendingOld = new List<MemoryEntry>(_cache);
            oldKey = _scopeKey;
            _cache = new();
            _loading = null;
            _scopeKey = _scope.Key("memories");
            _dirty = false;
            _pendingSinceFlush = 0;
        }
        lock (_flushGate) { _flushCts?.Cancel(); }

        if (pendingOld is not null)
        {
            _ = Task.Run(async () =>
            {
                try { await _store.Save(oldKey, pendingOld); }
                catch (Exception ex) { App.WriteLog("MemoryVault.FlushOldScope -> " + ex.Message); }
            });
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
        var flushNow = false;
        lock (_gate)
        {
            _cache.Add(entry);
            PruneAmbientLocked(_cache, entry.CharacterId);
            _dirty = true;
            _pendingSinceFlush++;
            flushNow = _pendingSinceFlush >= MaxPendingBeforeFlush;
        }

        // 写盘合并（审计 P1）：不再每条消息都整表序列化 + 整文件重写。
        // 内存立即可读，磁盘按 1.5s 防抖 / 32 条批量落盘，长会话从 O(n^2) 降到接近 O(n)。
        if (flushNow) await FlushAsync();
        else ScheduleFlush();
    }

    // ==================== 写盘合并（审计 P1） ====================
    private readonly object _flushGate = new();
    private CancellationTokenSource? _flushCts;
    private bool _dirty;
    private int _pendingSinceFlush;
    private const int FlushDelayMs = 1500;
    private const int MaxPendingBeforeFlush = 32;

    private void ScheduleFlush()
    {
        lock (_flushGate)
        {
            _flushCts?.Cancel();
            _flushCts = new CancellationTokenSource();
            var token = _flushCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(FlushDelayMs, token);
                    await FlushAsync();
                }
                catch (OperationCanceledException) { /* 被下一次 Store 重新排期，正常 */ }
                catch (Exception ex) { App.WriteLog("MemoryVault.ScheduleFlush -> " + ex.Message); }
            });
        }
    }

    /// <summary>把内存里的记忆落盘。换档、退出等对数据敏感的时刻可直接调用。</summary>
    public async Task FlushAsync()
    {
        List<MemoryEntry> snapshot;
        string scopeKey;
        lock (_gate)
        {
            if (!_dirty) return;
            // 快照与 scopeKey 必须在同一把锁里捕获，否则换档会把旧记忆写进新档（审计 P3）
            snapshot = new List<MemoryEntry>(_cache);
            scopeKey = _scopeKey;
            _dirty = false;
            _pendingSinceFlush = 0;
        }

        await _writeGate.WaitAsync();
        try
        {
            await _store.Save(scopeKey, snapshot);
        }
        catch (Exception ex)
        {
            // 写失败要把脏标记补回去，否则这批记忆就悄悄丢了
            lock (_gate) { _dirty = true; }
            App.WriteLog("MemoryVault.Flush -> " + ex.Message);
        }
        finally { _writeGate.Release(); }
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
        // 只扫当前角色，且只对这一小组排序、一次 RemoveAll 完事；
        // 原实现是每次 Store 都全表 Where + OrderBy，再逐条 Remove（每次 O(n)）。
        var ambient = new List<MemoryEntry>();
        foreach (var m in list)
            if (m.CharacterId == charId && AmbientCategories.Contains(m.Category))
                ambient.Add(m);
        if (ambient.Count <= AmbientCapPerCharacter) return;

        var doomed = ambient.OrderBy(m => m.At)
            .Take(ambient.Count - AmbientCapPerCharacter)
            .ToHashSet();
        list.RemoveAll(m => doomed.Contains(m));
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

    public async Task<List<MemoryEntry>> Recent(string charId, int n = 10)
    {
        // 必须真的等加载完：原来 fire-and-forget 后直接读缓存，
        // 启动后第一轮对话会拿到空记忆，用户感知就是「TA 把我忘了」（审计 P2/U2）。
        await EnsureLoadedAsync();
        lock (_gate)
            return _cache.Where(m => m.CharacterId == charId)
                .OrderByDescending(m => m.At).Take(n).ToList();
    }

    /// <summary>某个角色的全部记忆（可限定类别，如 affection / dialogue）。</summary>
    public async Task<List<MemoryEntry>> All(string charId, string? category = null)
    {
        await EnsureLoadedAsync();
        lock (_gate)
            return _cache.Where(m => m.CharacterId == charId
                    && (category is null || m.Category == category))
                .OrderByDescending(m => m.At).ToList();
    }

    public async Task<List<MemoryEntry>> Search(string charId, string query)
    {
        await EnsureLoadedAsync();
        List<MemoryEntry> source;
        lock (_gate)
        {
            source = _cache.Where(m => m.CharacterId == charId).ToList();
        }

        IEnumerable<MemoryEntry> hits = source;
        if (!string.IsNullOrWhiteSpace(query))
        {
            // 默认按纯文本匹配（审计 S7）：用户输入不再直接当正则，
            // 避免灾难性回溯卡死 UI，也避免每次搜索都动态编译一份常驻代码。
            // 确实要用正则的高级用法用 "re:" 前缀，并强制匹配超时。
            if (query.StartsWith("re:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var re = new Regex(query[3..], RegexOptions.IgnoreCase,
                        TimeSpan.FromMilliseconds(200));
                    hits = source.Where(m => SafeIsMatch(re, m.Content)
                        || (m.Keywords is not null && SafeIsMatch(re, m.Keywords)));
                }
                catch (ArgumentException ex)
                {
                    App.WriteLog("MemoryVault.Search: 正则无效 -> " + ex.Message);
                    hits = Array.Empty<MemoryEntry>();
                }
            }
            else
            {
                hits = source.Where(m => m.Content.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || (m.Keywords?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
            }
        }
        return hits.OrderByDescending(m => m.Weight).ThenByDescending(m => m.At).ToList();
    }

    private static bool SafeIsMatch(Regex re, string input)
    {
        try { return re.IsMatch(input); }
        catch (RegexMatchTimeoutException) { return false; }   // 回溯爆炸按「没匹配上」处理，不拖死界面
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
