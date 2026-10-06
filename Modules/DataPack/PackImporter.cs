using System.IO.Compression;
using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.DataPack;

/// <summary>素材包导入结果：除成功与否外，还要把"哪些地图需要标定"和"sidecar 校验问题"带出来。</summary>
public sealed record PackImportResult(bool Ok, List<MapScene> PendingCalibration, List<string> Messages);

public sealed class PackImporter
{
    private readonly StorageProvider _store;
    private readonly MapService _map;
    private readonly SceneCalibrationService _cal;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };
    private static readonly string[] VideoExts = { ".mp4", ".mov", ".m4v", ".webm" };

    public PackImporter(StorageProvider store, MapService map, SceneCalibrationService cal)
    {
        _store = store;
        _map = map;
        _cal = cal;
    }

    /// <summary>兼容旧调用方：只关心成败。</summary>
    public async Task<bool> Import(string zipPath, IProgress<string>? progress = null)
        => (await ImportWithReport(zipPath, progress)).Ok;

    public async Task<PackImportResult> ImportWithReport(string zipPath, IProgress<string>? progress = null)
    {
        var pending = new List<MapScene>();
        var messages = new List<string>();

        if (!File.Exists(zipPath))
        {
            messages.Add("素材包文件不存在");
            return new PackImportResult(false, pending, messages);
        }

        var tmp = Path.Combine(Path.GetTempPath(), $"pack_{Guid.NewGuid():N}");

        try
        {
            PackInfo? info = null;
            var ok = await Task.Run(async () =>
            {
                Directory.CreateDirectory(tmp);

                progress?.Report("解压中…");
                ZipFile.ExtractToDirectory(zipPath, tmp, overwriteFiles: true);
                info = await ReadManifest(tmp);
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

            if (!ok || info is null)
            {
                messages.Add("素材包里没有可识别的 manifest.json");
                return new PackImportResult(false, pending, messages);
            }

            // 地图合并要探测图片原始尺寸，而图片解码必须在主线程（见 ImageProbe 的说明），
            // 所以这部分不能留在上面的 Task.Run 里。
            var (p, m) = await MainThread.InvokeOnMainThreadAsync(() => ImportMaps(tmp, info.Name, progress));
            pending.AddRange(p);
            messages.AddRange(m);

            if (pending.Count > 0)
            {
                await _map.SaveAsync();
                _map.NotifyChanged();
                // 入队由导入方在合适时机统一驱动，逐个被动弹标定页体验太差
                _cal.Enqueue(pending);
            }

            return new PackImportResult(true, pending, messages);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Pack] {ex.Message}");
            App.WriteLog("PackImporter.ImportWithReport -> " + ex);
            messages.Add("导入失败：" + ex.Message);
            return new PackImportResult(false, pending, messages);
        }
        finally
        {
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// 素材包里的地图。
    ///
    /// 目录约定（两种都支持）：
    ///   地图/&lt;地图名&gt;/&lt;场景名&gt;.png                 每个子目录 = 一个地点
    ///   地图/&lt;场景名&gt;.png                            扁平形式，归到「素材包·&lt;包名&gt;」地点下
    /// 可选 sidecar：同目录、与图片同名的 &lt;场景名&gt;.placement.json
    ///
    /// 素材包里的地图**必须走与手画完全相同的标定流程**：没有 sidecar、或 sidecar 校验不通过，
    /// 都进待标定队列，不能因为是"包里带的"就跳过。
    /// </summary>
    private (List<MapScene> Pending, List<string> Messages) ImportMaps(
        string packRoot, string packName, IProgress<string>? progress)
    {
        var pending = new List<MapScene>();
        var messages = new List<string>();
        var root = Path.Combine(packRoot, "地图");
        if (!Directory.Exists(root)) return (pending, messages);

        progress?.Report("复制地图…");

        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var e = Path.GetExtension(f).ToLowerInvariant();
                return ImageExts.Contains(e) || VideoExts.Contains(e);
            })
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        foreach (var file in files)
        {
            try
            {
                var rel = Path.GetRelativePath(root, file);
                var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var locationName = parts.Length >= 2 ? parts[0] : $"素材包·{packName}";
                var sceneName = Path.GetFileNameWithoutExtension(file);

                var loc = _map.EnsureLocation(locationName);
                var (finalName, existing) = ResolveSceneName(loc, sceneName, packName, messages);
                var scene = existing
                    ? loc.Scenes.First(s => s.Name == finalName)
                    : _map.AddScene(loc.Id, finalName);

                // 背景统一走 MapService.ImportBackground：这样 ResolveBackground / 视频缩略图
                // / 换图作废标定这一整条链路都自动适用，不需要为素材包再开一条通路
                var relBg = _map.ImportBackground(file, scene.Id);
                if (relBg is null)
                {
                    messages.Add($"地图「{sceneName}」的图片复制失败，已跳过");
                    continue;
                }
                scene.Background = relBg;
                scene.SourcePack = packName;
                scene.Placement = null;   // 先清空：下面只有通过校验的 sidecar 才能把它填回来

                var sidecar = PlacementValidator.SidecarPathFor(file);
                if (!File.Exists(sidecar))
                {
                    pending.Add(scene);
                    continue;
                }

                var isVideo = VideoExts.Contains(Path.GetExtension(file).ToLowerInvariant());
                var size = isVideo ? (0, 0) : ImageProbe.SizeOf(file);
                if (isVideo)
                    messages.Add($"「{sceneName}」是视频背景，无法核对声明的尺寸，坐标比例校验已跳过");

                var (placement, errors) = PlacementValidator.FromJson(
                    File.ReadAllText(sidecar), Path.GetFileName(file), size);

                if (placement is null)
                {
                    // 校验不通过绝不静默忽略：图照常导入，但标定留空并进队列，同时把原因说清楚
                    messages.AddRange(errors.Select(e => $"「{sceneName}」：{e}"));
                    pending.Add(scene);
                }
                else
                {
                    placement.SourceBackground = relBg;
                    placement.SourceStamp = MapService.StampOf(_map.ResolveBackground(scene));
                    scene.Placement = placement;
                }
            }
            catch (Exception ex)
            {
                App.WriteLog($"PackImporter.ImportMaps({file}) -> {ex}");
                messages.Add($"地图「{Path.GetFileNameWithoutExtension(file)}」导入出错：{ex.Message}");
            }
        }

        return (pending, messages);
    }

    /// <summary>
    /// 同名场景怎么处理。三级策略，核心是**绝不静默覆盖用户手工建的场景**。
    /// </summary>
    private static (string Name, bool Existing) ResolveSceneName(
        MapLocation loc, string sceneName, string packName, List<string> messages)
    {
        var same = loc.Scenes.FirstOrDefault(s => s.Name == sceneName);
        if (same is null) return (sceneName, false);

        // 同一个包重复导入 = 升级，直接覆盖
        if (same.SourcePack == packName) return (sceneName, true);

        // 不同来源（或手工创建的 SourcePack 为空）：另存一个，不动原来那个
        var from = string.IsNullOrEmpty(same.SourcePack) ? "手工创建" : $"素材包「{same.SourcePack}」";
        var idx = 2;
        string candidate;
        do { candidate = $"{sceneName} ({idx++})"; }
        while (loc.Scenes.Any(s => s.Name == candidate));

        messages.Add($"场景「{sceneName}」已存在（{from}），本次导入另存为「{candidate}」");
        return (candidate, false);
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
├── CG/<名>/<CG文件名> + cg_data.json
└── 地图/<地图名>/<场景名>.png
    └── <场景名>.placement.json   （可选：已标定好的落脚点与缩放，未提供则需要导入后手动标定）

表情名直接给AI识别，含TURN标记朝左");
    }
}
