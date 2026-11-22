using System;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.Drawables;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>图层抽屉里的一颗「地图来源」药丸。</summary>
public sealed partial class MapSourceChip : ObservableObject
{
    public string Id { get; }
    public string Label { get; }
    public Command TapCommand { get; }

    [ObservableProperty] private bool _isCurrent;

    internal MapSourceChip(MapViewModel owner, string id, string label)
    {
        Id = id;
        Label = label;
        TapCommand = new Command(() => owner.TapTileSource(id));
    }
}

/// <summary>
/// 底图子系统在 VM 这一侧：选来源、记设置、算锚点、把瓦片层挂到画布上、比例尺跟着纬度走。
///
/// 关键约定：底图与叠加层共用一套 Offset/Scale。所以这里只做「换一层底」和「按档位放宽缩放范围」，
/// 拖拽、框选、连线这些交互一个都不碰。
/// </summary>
public sealed partial class MapViewModel
{
    private TileService? _tiles;
    private SettingsManager? _settings;
    private readonly TileLayerDrawable _tileLayer = new();
    private MapTileSource _tileSource = MapTiles.Manual;
    private DateTime _tileStatusAt = DateTime.MinValue;
    private int _tileIdleTicks;

    /// <summary>图层抽屉里的来源药丸（手工绘制 / 高德路网 / 高德卫星 / OSM / 离线包）。</summary>
    public ObservableCollection<MapSourceChip> TileSourceChips { get; } = new();

    /// <summary>当前来源那句说明：抽屉里显示在药丸下面。</summary>
    [ObservableProperty] private string _tileSourceNote = MapTiles.Manual.Note;

    /// <summary>联网总闸的镜像（真正的值在设置里，这里只是给它一个可绑定的面）。</summary>
    [ObservableProperty] private bool _isOnlineTilesEnabled = true;

    /// <summary>程序回填开关时置位：别把人家的设置当成用户刚拨了一下又存回去。</summary>
    private bool _syncingOnlineTiles;

    /// <summary>抽屉里拨开关 = 直接改设置；设置页拨同一项也会走到这里（经由 Applied）。</summary>
    partial void OnIsOnlineTilesEnabledChanged(bool value)
    {
        if (_syncingOnlineTiles || _settings is null) return;
        if (_settings.Current.MapTileOnline == value) return;
        _settings.Apply(_settings.Current with { MapTileOnline = value });
        _ = _settings.Persist();
        RefreshTileStatus(force: true);
        Touch();
    }

    /// <summary>「z18 · 12 块 / 已到 9 块 · 缓存 128MB」这一行。</summary>
    [ObservableProperty] private string _tileStatusText = "";

    /// <summary>底图锚点归属：地图中心 / 默认落点。抽屉里给人一句交代。</summary>
    [ObservableProperty] private string _tileAnchorText = "";

    /// <summary>当前这张底图是不是真在画（手工绘制时为 false）。</summary>
    public bool IsTileLayerActive => _tileSource.HasTiles;

    public string TileSourceName => _tileSource.Name;

    /// <summary>页面装配完画布之后调一次：把瓦片层挂上去，并按设置恢复上次选的来源。</summary>
    internal void AttachTileLayer(TileService tiles, SettingsManager settings)
    {
        _tiles = tiles;
        _settings = settings;
        _tileLayer.Service = tiles;
        Drawable.Tiles = _tileLayer;
        tiles.TileArrived += OnTileArrived;

        BuildSourceChips();
        ApplyTileSource(MapTiles.ById(settings.Current.MapTileSource), persist: false, announce: false);
        SetOnlineMirror(settings.Current.MapTileOnline);
        settings.Applied += OnSettingsApplied;
    }

    private void OnSettingsApplied()
    {
        // 设置页里拨了总闸：这里跟着变，下一次取图就按新值走（TileService 每次请求都读设置）。
        SetOnlineMirror(_settings?.Current.MapTileOnline ?? true);
        // 设置页换了 .mbtiles：这里跟着换包，不用来回切一次底图才生效。
        if (_settings is { } s && _tiles is { } t)
        {
            var path = s.Current.MapTileMbTilesPath ?? "";
            if (!string.Equals(path, t.MbTilesPath, StringComparison.Ordinal)) t.ConfigureMbTiles(path);
        }
        RefreshTileNote();
        RefreshTileStatus(force: true);
    }

    private void SetOnlineMirror(bool value)
    {
        _syncingOnlineTiles = true;
        IsOnlineTilesEnabled = value;
        _syncingOnlineTiles = false;
    }

    private void OnTileArrived()
    {
        // 后台解出一块图：只催一次重绘，别在加载线程上碰 UI。
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Touch();
            RefreshTileStatus();
        });
    }

    private void BuildSourceChips()
    {
        if (TileSourceChips.Count > 0) return;
        foreach (var s in MapTiles.All)
            TileSourceChips.Add(new MapSourceChip(this, s.Id, s.Name));
    }

    internal void TapTileSource(string id)
    {
        ApplyTileSource(MapTiles.ById(id), persist: true, announce: true);
    }

    /// <summary>换底图：画布换层、药丸换高亮、设置落盘、缩放范围按档位重算。</summary>
    private void ApplyTileSource(MapTileSource source, bool persist, bool announce)
    {
        _tileSource = source;
        _tileLayer.Source = source;
        RefreshTileNote();
        foreach (var chip in TileSourceChips)
            chip.IsCurrent = string.Equals(chip.Id, source.Id, StringComparison.Ordinal);
        OnPropertyChanged(nameof(IsTileLayerActive));
        OnPropertyChanged(nameof(TileSourceName));

        RefreshTileAnchor();
        if (persist && _settings is not null)
        {
            var s = _settings.Current;
            if (!string.Equals(s.MapTileSource, source.Id, StringComparison.Ordinal))
            {
                _settings.Apply(s with { MapTileSource = source.Id });
                _ = _settings.Persist();
            }
        }

        // 切到瓦片底图：按档 3–19 重新收一遍缩放（0.3–2.5 那套是纸面画布的尺度，装不下全国视野）。
        ClampIntoScaleRange();
        RefreshTileStatus(force: true);
        Touch();

        if (announce)
        {
            if (!source.HasTiles)
                Toast.Info("回到手工绘制底图。");
            else if (source.Id == MapTiles.MbTilesId)
                Toast.Info(_tiles?.OfflinePackage.IsReady == true
                    ? $"底图：离线包 · {_tiles.OfflinePackage.Message}"
                    : "离线包还没就绪：" + (_tiles?.OfflinePackage.Message ?? "去设置里选一个 .mbtiles 文件。"));
            else if (!IsOnlineTilesEnabled && source.IsOnline)
                Toast.Info($"已切到{source.Name}；联网瓦片是关着的，只显示缓存里已有的瓦片。");
            else
                Toast.Info($"底图：{source.Name}");
        }
    }

    /// <summary>抽屉里那句说明：离线包要把文件状态一并讲清楚（没选 / 不在 / 读不出 / 就绪）。</summary>
    private void RefreshTileNote()
    {
        if (_tileSource.Id == MapTiles.MbTilesId && _tiles is { } t)
        {
            var status = t.OfflinePackage;
            TileSourceNote = _tileSource.Note + " " + status.Message;
            return;
        }
        TileSourceNote = _tileSource.Note;
    }

    /// <summary>锚点：有真实中心就用它，手绘图没有经纬度就落到默认城市（不改存档，只给底图一个落点）。</summary>
    private void RefreshTileAnchor()
    {
        var map = _maps.Map;
        _tileLayer.MetersPerUnit = map.MetersPerUnit;
        if (map.HasGeoAnchor)
        {
            _tileLayer.AnchorLat = map.CenterLatitude;
            _tileLayer.AnchorLon = map.CenterLongitude;
            TileAnchorText = "锚点：地图中心 "
                + map.CenterLatitude.ToString("0.0000", CultureInfo.InvariantCulture) + ", "
                + map.CenterLongitude.ToString("0.0000", CultureInfo.InvariantCulture);
        }
        else
        {
            _tileLayer.AnchorLat = MapTiles.DefaultAnchorLat;
            _tileLayer.AnchorLon = MapTiles.DefaultAnchorLon;
            TileAnchorText = "锚点：" + MapTiles.DefaultAnchorName
                + " —— 这张图没有经纬度，底图只当背景铺，不对应真实位置。";
        }
    }

    [RelayCommand]
    private void ToggleOnlineTiles()
    {
        IsOnlineTilesEnabled = !IsOnlineTilesEnabled;
        Toast.Info(IsOnlineTilesEnabled ? "已允许联网取瓦片。" : "已关闭联网：底图只用本地缓存。");
    }

    /// <summary>缩放范围：底图在用时按 z3–z19 折，手工绘制仍是原来的 0.3–2.5。</summary>
    internal (double Min, double Max) ScaleRange()
    {
        if (!IsTileLayerActive) return (0.3, 2.5);
        return MapProjection.ScaleRangeForZooms(_tileLayer.MetersPerUnit, _tileLayer.AnchorLat);
    }

    private void ClampIntoScaleRange()
    {
        var (min, max) = ScaleRange();
        Drawable.Scale = Math.Clamp(Drawable.Scale, min, max);
    }

    /// <summary>底图档位变了、视口动了：更新那行状态（限流 250ms，拖拽时别每帧算）。</summary>
    internal void RefreshTileStatus(bool force = false)
    {
        if (!IsTileLayerActive)
        {
            TileStatusText = "底图关着：编辑器的纸面画布。";
            return;
        }
        var now = DateTime.UtcNow;
        if (!force && (now - _tileStatusAt).TotalMilliseconds < 250) return;
        _tileStatusAt = now;

        var parts = new System.Collections.Generic.List<string>(6);
        var zoom = MapProjection.ZoomForCanvasScale(Drawable.Scale, _tileLayer.MetersPerUnit, _tileLayer.AnchorLat);
        var z = Math.Clamp((int)Math.Round(zoom), MapProjection.MinZoom, MapProjection.MaxZoom);
        parts.Add("z" + z.ToString(CultureInfo.InvariantCulture));
        if (_tileLayer.LastTiles > 0)
            parts.Add($"可见 {_tileLayer.LastTiles} 块 · 已到 {_tileLayer.LastReady} 块");
        if (_tiles is { } t)
        {
            var mb = t.DiskBytes / 1024 / 1024;
            parts.Add(t.MemoryCount > 0 ? $"缓存 {t.MemoryCount} 张 · {mb}MB" : $"缓存 0 张 · {mb}MB");
            parts.Add($"失败 {t.FailureCount}");
            parts.Add($"退避 {t.BackoffSeconds}s");
            if (_tileSource.Id == MapTiles.MbTilesId)
            {
                var status = t.OfflinePackage;
                parts.Add(status.IsReady ? $"离线包 {status.CacheCount} 块 · 缺 {status.Misses}" : status.Message);
            }
            if (!IsOnlineTilesEnabled && _tileSource.IsOnline) parts.Add("联网已关");
        }
        TileStatusText = string.Join(" · ", parts);
        KickTileIdleWork();
    }

    /// <summary>
    /// 停手之后再多画几帧：瓦片层每帧会顺手把视口外一屏的瓦片排进队列。
    /// 只排三帧（约 1 秒），够把周边铺上，又不会让画布一直空转。
    /// </summary>
    private void KickTileIdleWork()
    {
        if (!IsTileLayerActive || _tileIdleTicks > 0) return;
        _tileIdleTicks = 3;
        PumpTileIdleFrame();
    }

    private void PumpTileIdleFrame()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) { _tileIdleTicks = 0; return; }
        try
        {
            dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(320), () =>
            {
                if (_tileIdleTicks <= 0) return;
                _tileIdleTicks--;
                Touch();
                if (_tileIdleTicks > 0) PumpTileIdleFrame();
            });
        }
        catch { _tileIdleTicks = 0; }
    }

    /// <summary>
    /// 比例尺要跟着纬度走：底图在用时按当前视口中心的纬度算「一像素多少米」，
    /// 80° 纬度上 cos 只剩 0.17，继续用赤道换算会把 10 公里标成 58 公里。
    /// </summary>
    internal double ScaleBarMetersPerPixel()
    {
        if (!IsTileLayerActive) return Drawable.MetersPerUnit / Math.Max(0.05, Drawable.Scale);
        var (lat, _) = MapProjection.CanvasToGeo(
            (_viewW / 2 - Drawable.OffsetX) / Drawable.Scale,
            (_viewH / 2 - Drawable.OffsetY) / Drawable.Scale,
            _tileLayer.AnchorLat, _tileLayer.AnchorLon, Drawable.MetersPerUnit);
        return MapProjection.GroundMetersPerPixel(
            MapProjection.ZoomForCanvasScale(Drawable.Scale, _tileLayer.MetersPerUnit, _tileLayer.AnchorLat),
            lat);
    }

    /// <summary>图层抽屉打开时刷一遍：状态行、锚点说明、联网开关都对齐当前设置。</summary>
    internal void RefreshTileDrawer()
    {
        RefreshTileAnchor();
        RefreshTileNote();
        SetOnlineMirror(_settings?.Current.MapTileOnline ?? true);
        foreach (var chip in TileSourceChips)
            chip.IsCurrent = string.Equals(chip.Id, _tileSource.Id, StringComparison.Ordinal);
        RefreshTileStatus(force: true);
    }
}
