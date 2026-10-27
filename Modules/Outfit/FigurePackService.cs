using System.Collections.Concurrent;
using System.Text.Json;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Outfit;

/// <summary>形象包目录摘要：列表页只需要这些，不必把整包贴图都读一遍。</summary>
public sealed record FigurePackInfo
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string FolderPath { get; init; } = "";
    public int PartCount { get; init; }
    public int OutfitCount { get; init; }
    public int SchemeCount { get; init; }
    public int MotionCount { get; init; }
}

/// <summary>
/// 形象包服务：从 assets/figures/{packId}/figure.json 载入，按 mtime 缓存解析结果，
/// 并把渲染委托给 <see cref="FigureComposer"/>。
///
/// 约定：这里所有“读/画”入口都不把异常抛给 UI。包缺失、JSON 坏了、贴图缺了，
/// 统一写日志并返回 null/空列表，让换装页自然回退到旧版 SpriteMap。
/// </summary>
public sealed class FigurePackService
{
    private readonly StorageProvider _store;
    private readonly FigureComposer _composer;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    internal static readonly JsonSerializerOptions ReadJson = new()
    {
        // 形象包可能由外部工具/手写生成，camelCase、注释、尾逗号都尽量宽容。
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public FigurePackService(StorageProvider store) : this(store, new FigureComposer())
    {
    }

    public FigurePackService(StorageProvider store, FigureComposer composer)
    {
        _store = store;
        _composer = composer;
    }

    public string FiguresRoot => Path.Combine(_store.Root, "assets", "figures");

    /// <summary>包目录绝对路径；id 不合法时返回空串，调用方的 File.Exists 自然失败。</summary>
    public string PackDirectory(string packId)
        => IsValidPackId(packId) ? Path.Combine(FiguresRoot, packId) : "";

    /// <summary>实例版存在性判断：跟随当前 StorageProvider（云恢复/测试可换根目录）。</summary>
    public bool Exists(string? packId)
    {
        if (!IsValidPackId(packId)) return false;
        return File.Exists(Path.Combine(FiguresRoot, packId!, "figure.json"));
    }

    /// <summary>
    /// 静态版存在性判断，给只有 CharacterData 的兼容层用；与实例版同指应用数据根下的 assets/figures。
    /// </summary>
    public static bool PackExists(string? packId)
    {
        if (!IsValidPackId(packId)) return false;
        return File.Exists(Path.Combine(App.RootDirectory, "assets", "figures", packId!, "figure.json"));
    }

    public static bool IsValidPackId(string? packId)
    {
        if (string.IsNullOrWhiteSpace(packId)) return false;
        if (packId.Length > 80) return false;
        if (packId is "." or "..") return false;
        if (packId.Contains('/') || packId.Contains('\\')) return false;
        if (packId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        if (packId.StartsWith('.') || packId.EndsWith('.') || packId.EndsWith(' ')) return false;
        return true;
    }

    /// <summary>载入形象包；同一 mtime+长度的文件只解析一次。</summary>
    public async Task<FigurePack?> LoadAsync(string packId)
    {
        try
        {
            if (!IsValidPackId(packId))
            {
                App.WriteLog("FigurePackService.Load: 包 id 不合法 -> " + packId);
                return null;
            }

            var dir = Path.Combine(FiguresRoot, packId);
            var file = Path.Combine(dir, "figure.json");
            if (!File.Exists(file))
            {
                App.WriteLog("FigurePackService.Load: 形象包不存在 -> " + file);
                return null;
            }

            var stamp = FileStamp(file);
            if (_cache.TryGetValue(packId, out var hit) && hit.Stamp == stamp) return hit.Pack;

            var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
            var pack = JsonSerializer.Deserialize<FigurePack>(json, ReadJson);
            if (pack is null)
            {
                App.WriteLog("FigurePackService.Load: figure.json 内容为空 -> " + file);
                return null;
            }

            pack = Normalize(pack, packId);
            _cache[packId] = new CacheEntry(stamp, pack);
            return pack;
        }
        catch (JsonException ex)
        {
            App.WriteLog("FigurePackService.Load: figure.json 解析失败 " + packId + " -> " + ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            App.WriteLog("FigurePackService.Load -> " + ex);
            return null;
        }
    }

    /// <summary>列出全部可读形象包；坏包/空目录跳过，不影响其它包。</summary>
    public async Task<IReadOnlyList<FigurePackInfo>> ListPacksAsync()
    {
        var list = new List<FigurePackInfo>();
        try
        {
            if (!Directory.Exists(FiguresRoot)) return list;

            foreach (var dir in Directory.GetDirectories(FiguresRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var id = Path.GetFileName(dir);
                if (!IsValidPackId(id)) continue;
                if (!File.Exists(Path.Combine(dir, "figure.json"))) continue;

                var pack = await LoadAsync(id).ConfigureAwait(false);
                if (pack is null) continue;

                list.Add(new FigurePackInfo
                {
                    // 目录名才是服务/缓存的唯一键；JSON 里的 Id 即使被手改成别的，也不能让列表点进去找不到包。
                    Id = id,
                    Name = string.IsNullOrWhiteSpace(pack.Name) ? id : pack.Name,
                    FolderPath = dir,
                    PartCount = pack.Parts.Count,
                    OutfitCount = pack.Outfits.Count,
                    SchemeCount = pack.Schemes.Count,
                    MotionCount = pack.Motions.Count,
                });
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("FigurePackService.ListPacks -> " + ex);
        }
        return list;
    }

    public async Task<IReadOnlyList<FigureOutfit>> ListOutfitsAsync(string packId)
        => (await LoadAsync(packId).ConfigureAwait(false))?.Outfits ?? new List<FigureOutfit>();

    public async Task<IReadOnlyList<FigureScheme>> ListSchemesAsync(string packId)
        => (await LoadAsync(packId).ConfigureAwait(false))?.Schemes ?? new List<FigureScheme>();

    public async Task<IReadOnlyList<FigureMotion>> ListMotionsAsync(string packId)
        => (await LoadAsync(packId).ConfigureAwait(false))?.Motions ?? new List<FigureMotion>();

    /// <summary>
    /// 渲染某个表情/静态姿态，返回缓存 PNG 的绝对路径（可直接给 Image.Source）。
    /// 包缺失或合成失败返回 null；不抛异常。
    /// </summary>
    public async Task<string?> RenderAsync(
        string packId,
        string outfitId,
        string schemeId,
        string emotion,
        int width,
        int height)
    {
        try
        {
            var pack = await LoadAsync(packId).ConfigureAwait(false);
            if (pack is null) return null;

            LogMissingSelection(pack, outfitId, schemeId);

            var request = new FigureRenderRequest
            {
                OutfitId = outfitId,
                SchemeId = schemeId,
                Emotion = emotion,
                Width = width,
                Height = height,
            };

            var path = await _composer.RenderCachedAsync(pack, PackDirectory(packId), request).ConfigureAwait(false);
            if (path is null)
                App.WriteLog($"FigurePackService.Render: 合成失败 {packId}/{outfitId}/{schemeId}/{emotion}");
            return path;
        }
        catch (Exception ex)
        {
            App.WriteLog("FigurePackService.Render -> " + ex);
            return null;
        }
    }

    /// <summary>
    /// 渲染动作的某一帧。动作不存在时退回同参数的静态图（而不是黑屏），帧号自动循环。
    /// </summary>
    public async Task<string?> RenderMotionAsync(
        string packId,
        string outfitId,
        string schemeId,
        string motionId,
        int frameIndex,
        int width,
        int height)
    {
        try
        {
            var pack = await LoadAsync(packId).ConfigureAwait(false);
            if (pack is null) return null;

            var motion = pack.Motions.FirstOrDefault(m =>
                string.Equals(m.Id, motionId, StringComparison.OrdinalIgnoreCase));
            var frames = motion?.Frames.Where(f => f is not null).ToList() ?? new List<FigureMotionFrame>();

            LogMissingSelection(pack, outfitId, schemeId);

            var request = new FigureRenderRequest
            {
                OutfitId = outfitId,
                SchemeId = schemeId,
                MotionId = frames.Count > 0 ? motion!.Id : null,
                FrameIndex = frames.Count > 0 ? ((frameIndex % frames.Count) + frames.Count) % frames.Count : 0,
                Width = width,
                Height = height,
            };

            if (frames.Count == 0)
                App.WriteLog("FigurePackService.RenderMotion: 动作不存在或没有帧，回退静态图 -> " + motionId);

            var path = await _composer.RenderCachedAsync(pack, PackDirectory(packId), request).ConfigureAwait(false);
            if (path is null)
                App.WriteLog($"FigurePackService.RenderMotion: 合成失败 {packId}/{motionId}#{frameIndex}");
            return path;
        }
        catch (Exception ex)
        {
            App.WriteLog("FigurePackService.RenderMotion -> " + ex);
            return null;
        }
    }

    /// <summary>手工改过 figure.json 且不想等 mtime 刷新时调用；不删渲染缓存（缓存键自带素材指纹）。</summary>
    public void Invalidate(string? packId = null)
    {
        if (string.IsNullOrWhiteSpace(packId)) _cache.Clear();
        else _cache.TryRemove(packId, out _);
    }

    /// <summary>
    /// 把外部 JSON 规整成运行期形状：缺列表/缺画布尺寸/空 id 都补上默认值。
    /// 反序列化后的 null 列表在这里统一清掉，合成器与页面就不用到处判空。
    /// </summary>
    internal static FigurePack Normalize(FigurePack pack, string fallbackId)
    {
        var id = string.IsNullOrWhiteSpace(pack.Id) ? fallbackId : pack.Id.Trim();
        var parts = (pack.Parts ?? new List<FigurePart>())
            .Where(p => p is not null)
            .Select(p => p with
            {
                Id = p.Id ?? "",
                Texture = p.Texture ?? "",
                Slot = p.Slot ?? "",
                EmotionTextures = p.EmotionTextures ?? new Dictionary<string, string>(),
            })
            .ToList();

        var outfits = (pack.Outfits ?? new List<FigureOutfit>())
            .Where(o => o is not null)
            .Select(o => o with
            {
                Id = o.Id ?? "",
                Overrides = o.Overrides ?? new Dictionary<string, string>(),
                Tint = o.Tint ?? "",
            })
            .ToList();

        var schemes = (pack.Schemes ?? new List<FigureScheme>())
            .Where(s => s is not null)
            .Select(s => s with
            {
                Id = s.Id ?? "",
                SlotColors = s.SlotColors ?? new Dictionary<string, string>(),
            })
            .ToList();

        var motions = (pack.Motions ?? new List<FigureMotion>())
            .Where(m => m is not null)
            .Select(m => m with
            {
                Id = m.Id ?? "",
                Frames = (m.Frames ?? new List<FigureMotionFrame>())
                    .Where(f => f is not null)
                    .Select(f => f with
                    {
                        Texture = f.Texture ?? "",
                        PartOverrides = f.PartOverrides ?? new Dictionary<string, string>(),
                    })
                    .ToList(),
            })
            .ToList();

        return pack with
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(pack.Name) ? id : pack.Name,
            CanvasWidth = pack.CanvasWidth > 0 ? pack.CanvasWidth : 1080,
            CanvasHeight = pack.CanvasHeight > 0 ? pack.CanvasHeight : 1920,
            Parts = parts,
            Outfits = outfits,
            Schemes = schemes,
            Motions = motions,
        };
    }

    private static void LogMissingSelection(FigurePack pack, string? outfitId, string? schemeId)
    {
        if (!string.IsNullOrWhiteSpace(outfitId) &&
            !pack.Outfits.Any(o => string.Equals(o.Id, outfitId, StringComparison.OrdinalIgnoreCase)))
            App.WriteLog("FigurePackService: 服装不存在，按基础部件渲染 -> " + outfitId);

        if (!string.IsNullOrWhiteSpace(schemeId) &&
            !pack.Schemes.Any(s => string.Equals(s.Id, schemeId, StringComparison.OrdinalIgnoreCase)))
            App.WriteLog("FigurePackService: 配色不存在，按原色渲染 -> " + schemeId);
    }

    private static string FileStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.LastWriteTimeUtc.Ticks.ToString() + ":" + info.Length.ToString();
        }
        catch
        {
            return "0:0";
        }
    }

    private sealed record CacheEntry(string Stamp, FigurePack Pack);
}
