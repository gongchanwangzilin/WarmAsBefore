using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Showcase;

/// <summary>
/// 展示案存储：脚本列表持久化到 {root}/showcase/scripts.json，
/// 编写过程中导入的临时素材复制到 {root}/showcase/materials/{scriptId}/。
/// </summary>
public sealed class ShowcaseStore
{
    private readonly StorageProvider _store;
    private const string Key = "showcase/scripts";

    public ShowcaseStore(StorageProvider store) => _store = store;

    /// <summary>素材根目录：{root}/showcase/materials。</summary>
    public static string MaterialsRoot => Path.Combine(App.RootDirectory, "showcase", "materials");

    public string MaterialsDir(string scriptId) => Path.Combine(MaterialsRoot, scriptId);

    public async Task<List<ShowcaseScript>> LoadAsync()
    {
        try
        {
            var list = await _store.Load<List<ShowcaseScript>>(Key);
            return list ?? new List<ShowcaseScript>();
        }
        catch (Exception ex)
        {
            App.WriteLog("ShowcaseStore.Load -> " + ex);
            return new List<ShowcaseScript>();
        }
    }

    public async Task SaveAllAsync(List<ShowcaseScript> scripts) => await _store.Save(Key, scripts);

    public async Task<ShowcaseScript?> GetAsync(string id)
    {
        var all = await LoadAsync();
        return all.FirstOrDefault(s => s.Id == id);
    }

    /// <summary>新增或更新展示案，并立即落盘。</summary>
    public async Task<bool> UpsertAsync(ShowcaseScript script)
    {
        try
        {
            script.UpdatedAt = DateTime.UtcNow;
            var all = await LoadAsync();
            var idx = all.FindIndex(s => s.Id == script.Id);
            if (idx >= 0) all[idx] = script;
            else all.Add(script);
            await SaveAllAsync(all);
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("ShowcaseStore.Upsert -> " + ex);
            return false;
        }
    }

    public async Task<bool> DeleteAsync(string id)
    {
        try
        {
            var all = await LoadAsync();
            var removed = all.RemoveAll(s => s.Id == id) > 0;
            if (removed) await SaveAllAsync(all);
            var matDir = MaterialsDir(id);
            if (Directory.Exists(matDir)) Directory.Delete(matDir, recursive: true);
            return removed;
        }
        catch (Exception ex)
        {
            App.WriteLog("ShowcaseStore.Delete -> " + ex);
            return false;
        }
    }

    /// <summary>把临时素材（一般是图片）复制到该展示案素材目录，返回相对根目录的路径。</summary>
    public async Task<string?> ImportMaterialAsync(string scriptId, string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath)) return null;
            var dir = MaterialsDir(scriptId);
            Directory.CreateDirectory(dir);
            var ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
            var dest = Path.Combine(dir, $"{Guid.NewGuid():N}{ext}");
            await Task.Run(() => File.Copy(sourcePath, dest, overwrite: true));
            return $"showcase/materials/{scriptId}/{Path.GetFileName(dest)}";
        }
        catch (Exception ex)
        {
            App.WriteLog("ShowcaseStore.ImportMaterial -> " + ex);
            return null;
        }
    }

    /// <summary>把相对根目录的素材路径解析为磁盘绝对路径。</summary>
    public static string FullPath(string rel)
    {
        if (string.IsNullOrWhiteSpace(rel)) return "";
        return Path.IsPathRooted(rel) ? rel : Path.Combine(App.RootDirectory, rel);
    }
}