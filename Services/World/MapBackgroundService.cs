using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>一次背景生成的人话结果。</summary>
public sealed record MapBgResult(bool Ok, string Message, string? RelPath = null)
{
    public static MapBgResult Fail(string message) => new(false, message);
}

/// <summary>一批背景生成的结算。</summary>
public sealed record MapBgBatchResult(int Done, int Failed, int Total, string? Message = null)
{
    public bool AllOk => Failed == 0 && Done > 0;
}

/// <summary>
/// 地图背景：给真实城市导入后那片「只有形状、没有画面」的空壳补图。
///
/// 两条通道：一张大地图的底图，和逐个场景的补充。素材一律走 <see cref="ImageGenerationService"/>
/// 现生成，或用户自己拍照导进来的文件 —— 不碰任何游戏自带/受版权保护的素材库，
/// 那是别处（素材包）的账，不该由地图创建这条路顺手拿过来。
///
/// 一次批量有上限（<see cref="MaxBatch"/>）：出图是最慢、最贵的一步，
/// 城市级地图动辄几百个场景，一口气全跑就是把手机钉在风扇上十几分钟。
/// </summary>
public sealed class MapBackgroundService
{
    /// <summary>一批最多生成几张。</summary>
    public const int MaxBatch = 12;

    /// <summary>底图在 maps/ 下的固定名字（不属于任何场景的那一张）。</summary>
    private const string BackdropName = "backdrop";

    private readonly MapService _maps;
    private readonly ImageGenerationService _images;
    private readonly StorageProvider _store;

    public MapBackgroundService(MapService maps, ImageGenerationService images, StorageProvider store)
    {
        _maps = maps;
        _images = images;
        _store = store;
    }

    /// <summary>当前地图里还缺背景的场景数（页面拿它说「还有 47 个场景没画面」）。</summary>
    public int CountMissing(MapDefinition map) => map.AllScenes.Count(s => string.IsNullOrWhiteSpace(s.Background));

    public bool Enabled => _images.Enabled;

    /// <summary>
    /// 大地图的底图：按城市与要素构成组织提示词，生成一张俯瞰的街区底图，
    /// 落到 maps/ 下并返回相对路径；同时挂到出生场景上 ——
    /// MapDefinition 自己没有整图底图层，出生场景是这张底图现在唯一会被看见的地方。
    /// </summary>
    public async Task<MapBgResult> GenerateBackdropAsync(MapDefinition map, string? subject, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(map);
        ct.ThrowIfCancellationRequested();
        var roads = map.Edges.Count;
        var places = map.Locations.Count;
        var where = FirstNonEmpty(subject, map.City, map.Name, "一座中国城市");
        var prompt = $"大地图底图：{where}，"
            + $"约 {places} 处建筑与店铺、{roads} 段道路的真实街区俯瞰，"
            + "白天、柔和的自然光，暖色调，街道与楼块层次清楚，"
            + "原创手绘与写实之间的质感，不要标注文字，不要 UI，不要参照任何现有游戏或地图产品的美术资源";
        var (path, message) = await _images.GenerateAsync(prompt, BackdropName, "1536x1024").ConfigureAwait(false);
        if (path is null) return MapBgResult.Fail(message);

        var rel = CopyToMaps(path, BackdropName);
        if (rel is null) return MapBgResult.Fail("底图已生成，但没能落到地图目录里（检查一下磁盘空间）。");
        var start = map.SceneById(map.StartSceneId) ?? map.AllScenes.FirstOrDefault();
        if (start is not null) ApplyTo(start, rel);
        await _maps.SaveAsync().ConfigureAwait(false);
        _maps.NotifyChanged();
        return new MapBgResult(true,
            start is null ? $"底图已存好：{rel}" : $"底图已生成，并铺在出生场景「{start.Name}」上。",
            rel);
    }

    /// <summary>给一个场景补一张背景（名字、真实店名、门牌都会进提示词）。</summary>
    public async Task<MapBgResult> GenerateForSceneAsync(MapDefinition map, MapScene scene, string? hint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ct.ThrowIfCancellationRequested();
        var (path, message) = await _images.GenerateAsync(BuildPrompt(map, scene, hint), "bg_" + scene.Id, "1536x1024")
            .ConfigureAwait(false);
        if (path is null) return MapBgResult.Fail(message);
        var rel = _maps.ImportBackgroundForScene(path, scene);
        if (rel is null) return MapBgResult.Fail("背景已经生成，但复制到地图目录时失败了。");
        scene.Background = rel;
        // 换图 = 旧标定作废：落脚点是绑在「哪一张图」上的，换一张就不成立了。
        scene.Placement = null;
        return new MapBgResult(true, $"「{scene.Name}」的背景补好了。", rel);
    }

    /// <summary>
    /// 一批：给还缺背景的场景逐个补图，最多 <see cref="MaxBatch"/> 张，全程可取消。
    /// 取消落在两张之间 —— 单次出图本身在 ImageGenerationService 里跑，中途掐了会留下半张请求。
    /// </summary>
    public async Task<MapBgBatchResult> FillMissingAsync(MapDefinition map, int limit,
        IProgress<string>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(map);
        var targets = map.AllScenes
            .Where(s => string.IsNullOrWhiteSpace(s.Background))
            .Take(Math.Clamp(limit, 1, MaxBatch))
            .ToList();
        if (targets.Count == 0) return new MapBgBatchResult(0, 0, 0, "这张图的场景都已经有了背景。");

        var done = 0;
        var failed = 0;
        string? firstError = null;
        foreach (var scene in targets)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"正在生成 {done + failed + 1}/{targets.Count}：{scene.Name}");
            var r = await GenerateForSceneAsync(map, scene, null, ct).ConfigureAwait(false);
            if (r.Ok) done++;
            else
            {
                failed++;
                firstError ??= r.Message;
                // 接口没配 / 没开：再往下跑也是同样一句话重复十二遍，直接收手。
                if (r.Message.Contains("还没有填写", StringComparison.Ordinal)
                    || r.Message.Contains("没有开启", StringComparison.Ordinal))
                    return new MapBgBatchResult(done, failed, targets.Count, r.Message);
            }
        }

        await _maps.SaveAsync().ConfigureAwait(false);
        _maps.NotifyChanged();
        var note = failed == 0
            ? $"补好了 {done} 个场景的背景。"
            : $"补好 {done} 个，失败 {failed} 个：{firstError}";
        return new MapBgBatchResult(done, failed, targets.Count, note);
    }

    /// <summary>场景 → 提示词。真实店名与门牌是这里最值钱的信息：有了它，AI 才知道该画一间什么样的店。</summary>
    private static string BuildPrompt(MapDefinition map, MapScene scene, string? hint)
    {
        var owner = map.Locations.FirstOrDefault(l => l.Scenes.Any(s => s.Id == scene.Id));
        var real = FirstNonEmpty(owner?.RealName, owner?.Name, scene.Name);
        var bits = new List<string> { "场景背景" };
        if (map.City.Length > 0) bits.Add("城市：" + map.City);
        if (real.Length > 0) bits.Add("地点：" + real);
        if (owner?.Address is { Length: > 0 } addr) bits.Add("门牌：" + addr);
        if (owner is { Floors: > 1 }) bits.Add($"这是一栋 {owner.Floors} 层的楼");
        if (hint is { Length: > 0 }) bits.Add(hint.Trim());
        bits.Add("横向构图");
        bits.Add("画面里不要出现人物");
        bits.Add("不要出现任何文字与水印");
        bits.Add("原创场景，不要参照现有游戏或影视的美术资源");
        return string.Join("，", bits);
    }

    private void ApplyTo(MapScene scene, string rel)
    {
        scene.Background = rel;
        scene.Placement = null;
    }

    /// <summary>把生成好的底图复制进 maps/（与 MapService.ImportBackground 同一套目录约定），返回相对路径。</summary>
    private string? CopyToMaps(string srcPath, string name)
    {
        try
        {
            var dir = Path.Combine(_store.Root, "maps");
            Directory.CreateDirectory(dir);
            var ext = Path.GetExtension(srcPath);
            if (string.IsNullOrEmpty(ext)) ext = ".png";
            var dest = Path.Combine(dir, name + ext);
            File.Copy(srcPath, dest, true);
            return $"maps/{name}{ext}";
        }
        catch (Exception ex)
        {
            App.WriteLog("MapBackgroundService.CopyToMaps -> " + ex);
            return null;
        }
    }

    private static string FirstNonEmpty(params string?[] vs)
    {
        foreach (var v in vs) if (!string.IsNullOrWhiteSpace(v)) return v!.Trim();
        return "";
    }
}
