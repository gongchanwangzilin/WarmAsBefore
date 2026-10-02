using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Cg;

/// <summary>
/// CG 收藏册存储：目录来自 {root}/cg/catalog.json（外部可自由编辑改写），
/// 解锁状态持久化在应用数据（key: cg_unlocked）。
/// 触发协议：AI 回复中出现【CG:文件名】即解锁并全屏播放该 CG。
/// </summary>
public sealed class CgStore
{
    private readonly StorageProvider _store;
    private List<CgRecord> _catalog = new();
    private HashSet<string> _unlocked = new();

    public CgStore(StorageProvider store)
    {
        _store = store;
        _ = LoadAsync();
    }

    public IReadOnlyList<CgRecord> Catalog => _catalog;
    public IReadOnlyCollection<string> Unlocked => _unlocked;
    public event Action? Changed;

    public static string CgRoot => Path.Combine(App.RootDirectory, "cg");

    private async Task LoadAsync()
    {
        try
        {
            var catalogPath = Path.Combine(CgRoot, "catalog.json");
            if (File.Exists(catalogPath))
            {
                var cat = JsonSerializer.Deserialize<List<CgRecord>>(await File.ReadAllTextAsync(catalogPath));
                if (cat is not null) _catalog = cat;
            }
            var unlocked = await _store.Load<List<string>>("cg_unlocked");
            if (unlocked is not null && unlocked.Count > 0) _unlocked = new HashSet<string>(unlocked);
        }
        catch (Exception ex)
        {
            App.WriteLog("CgStore.Load -> " + ex);
        }
    }

    /// <summary>重新扫描目录与解锁状态（回忆录打开收藏册时调用）。</summary>
    public async Task RefreshAsync()
    {
        await LoadAsync();
        Changed?.Invoke();
    }

    public bool IsUnlocked(string file) => _unlocked.Contains(Norm(file));

    public List<CgRecord> AllUnlocked() =>
        _catalog.Where(c => _unlocked.Contains(Norm(c.File))).ToList();

    /// <summary>解锁一张 CG（幂等）；返回落地后的记录（含标题）。</summary>
    public async Task<CgRecord?> UnlockAsync(string file, string? title = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(file)) return null;
            var id = Norm(file);
            if (!_unlocked.Contains(id))
            {
                _unlocked.Add(id);
                await _store.Save("cg_unlocked", _unlocked.ToList());
            }
            var record = _catalog.FirstOrDefault(c => Norm(c.File) == id);
            if (record is null && !string.IsNullOrWhiteSpace(title))
            {
                record = new CgRecord
                {
                    Id = Guid.NewGuid().ToString("N")[..12],
                    Title = title,
                    File = file,
                    Unlocked = true
                };
                _catalog.Add(record);
            }
            if (record is not null) record.Unlocked = true;
            Changed?.Invoke();
            return record;
        }
        catch (Exception ex)
        {
            App.WriteLog("CgStore.Unlock -> " + ex);
            return null;
        }
    }

    /// <summary>把 CG 文件解析为可显示路径（cg 目录优先，其次 assets/根目录/绝对路径）。</summary>
    public string? ResolvePath(CgRecord cg)
    {
        if (cg is null || string.IsNullOrWhiteSpace(cg.File)) return null;
        var rel = Norm(cg.File);
        string[] roots =
        {
            CgRoot,
            Path.Combine(App.RootDirectory),
            Path.Combine(FileSystem.AppDataDirectory, "WarmAsBefore"),
            App.RootDirectory,
            Directory.GetCurrentDirectory()
        };
        foreach (var root in roots)
        {
            foreach (var candidate in new[]
            {
                Path.Combine(root, rel),
                Path.Combine(root, "assets", rel)
            })
            {
                try { if (File.Exists(candidate)) return candidate; } catch { }
            }
        }
        try { if (File.Exists(cg.File)) return cg.File; } catch { }
        return null;
    }

    private static string Norm(string file) => (file ?? "").Trim().Replace('\\', '/');
}

/// <summary>CG 全屏播放页的载荷（由触发方写入，页内读取）。</summary>
public sealed class CgViewPayload
{
    public string ImagePath { get; set; } = "";
    public string Title { get; set; } = "";
    public bool HasPayload { get; set; }
}