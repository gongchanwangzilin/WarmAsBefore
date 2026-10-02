using System.IO.Compression;
using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.DataPack;

public sealed class PackImporter
{
    private readonly StorageProvider _store;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public PackImporter(StorageProvider store) => _store = store;

    public async Task<bool> Import(string zipPath, IProgress<string>? progress = null)
    {
        if (!File.Exists(zipPath)) return false;

        var tmp = Path.Combine(Path.GetTempPath(), $"pack_{Guid.NewGuid():N}");

        try
        {
            return await Task.Run(async () =>
            {
                Directory.CreateDirectory(tmp);

                progress?.Report("解压中…");
                ZipFile.ExtractToDirectory(zipPath, tmp, overwriteFiles: true);
                var info = await ReadManifest(tmp);
                if (info is null) return false;

                var assets = Path.Combine(_store.Root, "assets");
                progress?.Report("复制角色…");
                CopyDir(Path.Combine(tmp, "角色"), Path.Combine(assets, "characters"));
                progress?.Report("复制背景…");
                CopyDir(Path.Combine(tmp, "背景"), Path.Combine(assets, "backgrounds"));
                progress?.Report("复制音频…");
                CopyDir(Path.Combine(tmp, "音频"), Path.Combine(assets, "audio"));
                progress?.Report("复制CG…");
                CopyDir(Path.Combine(tmp, "CG"), Path.Combine(assets, "cg"));

                await _store.Save($"pack_{info.Name}", info);
                return true;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Pack] {ex.Message}");
            return false;
        }
        finally
        {
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true); } catch { }
        }
    }

    public async Task<PackInfo?> Peek(string zipPath)
    {
        if (!File.Exists(zipPath)) return null;
        using var z = ZipFile.OpenRead(zipPath);
        var e = z.GetEntry("manifest.json");
        if (e is null) return null;
        using var r = new StreamReader(e.Open());
        return JsonSerializer.Deserialize<PackInfo>(await r.ReadToEndAsync(), Json);
    }

    private static async Task<PackInfo?> ReadManifest(string dir)
    {
        var p = Path.Combine(dir, "manifest.json");
        if (!File.Exists(p)) return null;
        return JsonSerializer.Deserialize<PackInfo>(await File.ReadAllTextAsync(p), Json);
    }

    private static void CopyDir(string src, string dst)
    {
        if (!Directory.Exists(src)) return;
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, f);
            var dest = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(f, dest, overwrite: true);
        }
    }

    public static void PrintGuide()
    {
        Console.WriteLine(@"
=== 数据包格式 ===
角色包.zip
├── manifest.json          { name, version, author, description }
├── 角色/<名>/<服装>/<表情>.png
├── 背景/<地点>_<时间>_<季节>.png
├── 音频/背景音乐(名).mp3
└── CG/<名>/<CG文件名> + cg_data.json

表情名直接给AI识别，含TURN标记朝左");
    }
}