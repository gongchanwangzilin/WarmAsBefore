using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 立绘标定的存取与判定：素材按 <see cref="PortraitCanvas.Spec"/> 逻辑画布标定一次，
/// 之后同一份素材在任何场景都按这份标定上场。
///
/// 键用素材的存储相对路径（与 SpriteMap 中的值一致），所以同一张图在角色库、
/// 素材库与游戏里看到的是同一份标定。
/// </summary>
public sealed class PortraitCalibrationService
{
    private const string StoreKey = "portrait_calibrations";

    private readonly StorageProvider _store;
    private readonly object _gate = new();
    private Dictionary<string, PortraitCalibration> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public PortraitCalibrationService(StorageProvider store) => _store = store;

    private async Task EnsureLoadedAsync()
    {
        lock (_gate)
        {
            if (_loaded) return;
            _loaded = true;
        }
        try
        {
            var saved = await _store.Load<Dictionary<string, PortraitCalibration>>(StoreKey);
            lock (_gate)
            {
                if (saved is not null) _cache = new Dictionary<string, PortraitCalibration>(saved, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("PortraitCalibration.Load -> " + ex.Message);
        }
    }

    public async Task<PortraitCalibration?> GetAsync(string spritePath)
    {
        if (string.IsNullOrWhiteSpace(spritePath)) return null;
        await EnsureLoadedAsync();
        lock (_gate) return _cache.TryGetValue(spritePath, out var c) ? c : null;
    }

    /// <summary>同步取值（渲染路径用）：未加载完时返回 null，由调用方按「未标定」处理。</summary>
    public PortraitCalibration? Get(string spritePath)
    {
        if (string.IsNullOrWhiteSpace(spritePath)) return null;
        lock (_gate) return _cache.TryGetValue(spritePath, out var c) ? c : null;
    }

    public async Task SaveAsync(string spritePath, PortraitCalibration cal)
    {
        if (string.IsNullOrWhiteSpace(spritePath)) return;
        await EnsureLoadedAsync();
        lock (_gate)
        {
            _cache[spritePath] = cal;
            _ = _store.Save(StoreKey, _cache);
        }
    }

    public async Task ClearAsync(string spritePath)
    {
        if (string.IsNullOrWhiteSpace(spritePath)) return;
        await EnsureLoadedAsync();
        lock (_gate)
        {
            if (_cache.Remove(spritePath)) _ = _store.Save(StoreKey, _cache);
        }
    }

    public async Task<int> CalibratedCountAsync()
    {
        await EnsureLoadedAsync();
        lock (_gate) return _cache.Count;
    }

    /// <summary>
    /// 判断这份素材是否需要进调试界面。
    /// 尺寸读不出来、或像素比例不是 9:16 的，都算「不合规」，必须标定后才能上场。
    /// </summary>
    public static (bool Needs, string Reason) Inspect(string? absPath, int srcW, int srcH)
    {
        if (string.IsNullOrWhiteSpace(absPath) || srcW <= 0 || srcH <= 0)
            return (false, "");   // 无立绘/读不出尺寸：不是标定问题，按「没有立绘」处理
        if (PortraitCalibrationMath.IsStandardCanvas(srcW, srcH)) return (false, "");
        return (true, $"素材为 {srcW}×{srcH}，不是 {PortraitCanvas.Spec} 的逻辑画布，需要先标定。");
    }

    /// <summary>校验某份素材是否可上场（未标定/标定不合法都给出原因）。</summary>
    public async Task<List<string>> ProblemsAsync(string spritePath, int srcW, int srcH)
    {
        var (needs, reason) = Inspect(spritePath, srcW, srcH);
        if (!needs) return new List<string>();
        var cal = await GetAsync(spritePath);
        if (cal is null) return new List<string> { reason };
        var problems = PortraitCalibrationMath.Validate(cal, srcW, srcH);
        return problems.Count > 0 ? problems : new List<string>();
    }
}
