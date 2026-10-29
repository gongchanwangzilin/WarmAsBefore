using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;


public sealed class StorageProvider
{
    private readonly string _root;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _createdDirs = new(StringComparer.OrdinalIgnoreCase);

    public StorageProvider()
    {
        _root = Path.Combine(FileSystem.AppDataDirectory, "WarmAsBefore");
        Directory.CreateDirectory(_root);
        _createdDirs[_root] = 0;
    }

    public string Root => _root;

    /// <summary>任一 JSON 落盘或删除后触发（参数是存储键）。云存档靠它收集待上传的键。</summary>
    public event Action<string>? Changed;

    public async Task<T?> Load<T>(string key) where T : class
    {
        var path = KeyPath(key);
        if (!File.Exists(path)) return null;
        var json = await ReadWithRetryAsync(path);
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            BackupCorrupt(path);
            throw;
        }
    }

    /// <summary>读取原始 JSON 文本（供需要分段/多次反序列化的调用方使用，避免 Dictionary&lt;object&gt; 双重序列化）。</summary>
    public async Task<string?> LoadRawAsync(string key)
    {
        var path = KeyPath(key);
        if (!File.Exists(path)) return null;
        return await ReadWithRetryAsync(path);
    }

    /// <summary>
    /// 云同步与杀毒常常只占用文件几十毫秒，直接失败会让调用方把「读不到」当成「没有数据」。
    /// 所以先重试一次，真的读不出来再抛。
    /// </summary>
    private static async Task<string> ReadWithRetryAsync(string path)
    {
        try
        {
            return await File.ReadAllTextAsync(path);
        }
        catch (IOException)
        {
            await Task.Delay(80);
            return await File.ReadAllTextAsync(path);
        }
    }

    /// <summary>
    /// 内容解析不了就留一份带时间戳的原件再抛。调用方一律按「这份数据没有」继续跑，
    /// 下一次保存会整表覆盖——没有这份备份，用户的东西就真的没了。
    /// </summary>
    private static void BackupCorrupt(string path)
    {
        try
        {
            // 后缀留在最后（characters.corrupt-时间戳.json）：这份备份是要给用户手工救回来的，
            // 名字里还认得出是 JSON，编辑器与资源管理器才不会把它当成无关文件。
            var bak = Path.Combine(Path.GetDirectoryName(path) ?? "",
                Path.GetFileNameWithoutExtension(path) + ".corrupt-"
                + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + Path.GetExtension(path));
            if (!File.Exists(bak)) File.Copy(path, bak);
            App.WriteLog("StorageProvider: 文件读坏了，已备份原件 → " + bak);
        }
        catch (Exception ex)
        {
            App.WriteLog("StorageProvider.BackupCorrupt -> " + ex.Message);
        }
    }

    /// <summary>
    /// 这个键能不能安全地落在数据目录里。
    /// 远端云清单的键、数据包与角色 id 都是外部输入，它们不该有能力把文件写到数据目录外面。
    /// </summary>
    public static bool IsSafeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        // 冒号是盘符/备用数据流的写法：Windows 文件名里本来就不能有，留着只会被路径语义捡走。
        if (key.Contains(':')) return false;
        if (Path.IsPathRooted(key)) return false;
        foreach (var seg in key.Split('/', '\\'))
            if (seg == ".." || seg == ".") return false;
        return true;
    }

    public async Task Save<T>(string key, T data) where T : class
    {
        var json = JsonSerializer.Serialize(data, WriteOptions);
        await WriteAtomicAsync(key, json);
        Changed?.Invoke(key);
    }

    /// <summary>原样写回一段 JSON（云存档把远端文件落到本地，避免反序列化再序列化改变内容）。</summary>
    public async Task WriteRawAsync(string key, string json)
    {
        await WriteAtomicAsync(key, json);
        Changed?.Invoke(key);
    }

    /// <summary>
    /// 同目录临时文件 + 原子替换：另一个线程（云存档合并）同时在读时，
    /// 最多读到旧文件或新文件，绝不会读到写了一半的 JSON。
    /// </summary>
    private async Task WriteAtomicAsync(string key, string json)
    {
        var path = KeyPath(key);
        var dir = Path.GetDirectoryName(path);
        // TryAdd 只在首次插入时返回 true：同一目录只建一次，省掉每次 Save 的 CreateDirectory 系统调用
        if (dir is not null && _createdDirs.TryAdd(dir, 0))
            Directory.CreateDirectory(dir);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tmp, json);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(tmp, path, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 5)
                {
                    await Task.Delay(30 * (attempt + 1));   // 杀毒/索引器短暂占用文件
                }
            }
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>存储键对应的绝对路径（同步工具与调试用）；越界的键返回空串，让调用处的 File.Exists 自然为 false。</summary>
    public string PathOf(string key) => IsSafeKey(key) ? KeyPath(key) : "";

    /// <summary>中文原样写出去（默认会把「妈妈」转成 \u5988\u5988，人就没法直接读这些文件了）。</summary>
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public void Delete(string key)
    {
        var p = KeyPath(key);
        if (File.Exists(p)) File.Delete(p);
        Changed?.Invoke(key);
    }

    public bool Exists(string key) => IsSafeKey(key) && File.Exists(KeyPath(key));

    private string KeyPath(string key)
    {
        // 硬边界：键必须是数据目录内的相对路径。少这一道，一个 "../settings" 就能把文件写到目录外面。
        if (!IsSafeKey(key)) throw new ArgumentException("存储键不合法（要相对路径，且不含 . / ..）：" + key);
        return Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar) + ".json");
    }
}
