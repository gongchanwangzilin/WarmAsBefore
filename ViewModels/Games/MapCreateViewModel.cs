using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// AI 地图创建：地名 / 经纬度 → 检索 → 定范围 → 先预览数一数 → 导入到游戏 → 再让 AI 补背景。
///
/// 这一页只做「拿一张图」这一段，编辑留给地图页，所以这里的按钮都是真会联网、
/// 真会花钱的步骤：每一颗都跟着一句实打实的进度，没有事的转圈一律不放。
/// 预览算过的那一份留着直接用，导入不再重复拉一次 Overpass（那台免费服务器不该被我们敲两遍）。
/// </summary>
public sealed partial class MapCreateViewModel : ObservableObject
{
    private readonly MapService _maps;
    private readonly MapImportService _importer;
    private readonly MapBackgroundService _backgrounds;

    private CancellationTokenSource? _cts;
    /// <summary>上一次「预览」算出来的整张图：导入时命中同一参数就直接用它。</summary>
    private MapBuildResult? _preview;
    private string _previewKey = "";
    private int _bgStep;

    public ObservableCollection<MapHitRow> Hits { get; } = new();
    public ObservableCollection<MapPresetChip> Presets { get; } = new();
    public IReadOnlyList<string> BatchChoices { get; } = new[] { "3 张", "6 张", "12 张" };

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _busyLabel = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private MapHitRow? _selectedRow;
    [ObservableProperty] private double _radiusMeters = 500;
    [ObservableProperty] private bool _useHitBoundingBox;
    [ObservableProperty] private string _previewText = "";
    [ObservableProperty] private string _previewWarning = "";
    [ObservableProperty] private string _sourceNote = "";
    [ObservableProperty] private string _amapKeyInput = "";
    [ObservableProperty] private bool _hasAmapKey;
    [ObservableProperty] private string _backgroundSummary = "";
    [ObservableProperty] private string _backdropNote = "";
    [ObservableProperty] private bool _hasMap;
    [ObservableProperty] private bool _imagesEnabled = true;
    [ObservableProperty] private string _batchChoice = "6 张";
    [ObservableProperty] private bool _isFillingBackground;
    [ObservableProperty] private double _bgProgress;
    [ObservableProperty] private string _bgProgressText = "";

    public MapCreateViewModel(MapService maps, MapImportService importer, MapBackgroundService backgrounds)
    {
        _maps = maps;
        _importer = importer;
        _backgrounds = backgrounds;
        foreach (var (label, radius) in MapImportOptions.Presets)
            Presets.Add(new MapPresetChip(label, radius));
        SyncPresets();
    }

    // ==================== 派生显示 ====================

    public bool HasSelection => SelectedRow is not null;
    public bool SelectedHasBox => SelectedRow?.Hit.HasBoundingBox == true;
    public bool HasPreviewText => PreviewText.Length > 0;
    public bool HasPreviewWarning => PreviewWarning.Length > 0;
    public bool HasBackdropNote => BackdropNote.Length > 0;
    public bool HasSourceNote => SourceNote.Length > 0;

    /// <summary>出图那两颗钮：没开画面生成时按下去也只是白等，所以直接灰着。</summary>
    public bool CanRunAi => ImagesEnabled && !IsBusy;

    /// <summary>滑块说的是半径，用户念的是「这一片有多大」，所以两句都给。</summary>
    public string RadiusLabel
    {
        get
        {
            var span = RadiusMeters * 2;
            var text = span >= 1000
                ? (span / 1000).ToString("0.##", CultureInfo.InvariantCulture) + " 公里宽的一整片"
                : RadiusMeters.ToString("0", CultureInfo.InvariantCulture) + " 米为半径的一小片";
            return "取中心点周围 " + text;
        }
    }

    public string AmapKeyState => HasAmapKey
        ? "已填 Key：检索走高德，道路与建筑详情仍取自开放街道图。"
        : "没填 Key 也能用：检索退回开放街道图，只是国内的中文店名与小马路会少一些。";

    // ==================== 进页面 ====================

    public async Task LoadAsync()
    {
        AmapKeyInput = _importer.AmapKey;
        HasAmapKey = _importer.HasAmapKey;
        SourceNote = _importer.SourceNote;
        ImagesEnabled = _backgrounds.Enabled;
        try { await _maps.InitializeAsync(); }
        catch (Exception ex) { App.WriteLog("MapCreateViewModel.LoadAsync -> " + ex); }
        RefreshMapState();
        if (Status.Length == 0)
            Status = "输入城市、区县、街道，或者直接填经纬度（例：120.15,30.27）。";
    }

    /// <summary>地图现在的状况：还缺几张背景，AI 出图开没开。</summary>
    private void RefreshMapState()
    {
        var map = _maps.Map;
        var scenes = map.AllScenes.Count();
        HasMap = scenes > 0;
        var missing = _backgrounds.CountMissing(map);
        BackgroundSummary = scenes == 0
            ? "手上还没有地图：先检索一片真实城市导进来，背景才有着落。"
            : $"{map.Name} · {scenes} 个场景，其中 {missing} 个还没有背景。"
              + (missing > 0 && !_backgrounds.Enabled
                  ? "（画面生成没开启：到 设置 → AI → 画面生成 填好接口地址与 Key。）" : "");
    }

    // ==================== 参数 ====================

    partial void OnSelectedRowChanged(MapHitRow? value)
    {
        foreach (var row in Hits) row.IsSelected = ReferenceEquals(row, value);
        UseHitBoundingBox = value?.Hit.HasBoundingBox == true;
        InvalidatePreview();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedHasBox));
    }

    partial void OnRadiusMetersChanged(double value)
    {
        SyncPresets();
        OnPropertyChanged(nameof(RadiusLabel));
        InvalidatePreview();
    }

    partial void OnUseHitBoundingBoxChanged(bool value) => InvalidatePreview();

    partial void OnPreviewTextChanged(string value) => OnPropertyChanged(nameof(HasPreviewText));

    partial void OnPreviewWarningChanged(string value) => OnPropertyChanged(nameof(HasPreviewWarning));

    partial void OnBackdropNoteChanged(string value) => OnPropertyChanged(nameof(HasBackdropNote));

    partial void OnSourceNoteChanged(string value) => OnPropertyChanged(nameof(HasSourceNote));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanRunAi));

    partial void OnImagesEnabledChanged(bool value) => OnPropertyChanged(nameof(CanRunAi));

    private void SyncPresets()
    {
        foreach (var chip in Presets)
            chip.IsCurrent = Math.Abs(chip.Radius - RadiusMeters) < 1;
    }

    [RelayCommand]
    private void ApplyPreset(double radius) => RadiusMeters = radius;

    /// <summary>点一行结果就是「以这里为中心」。</summary>
    [RelayCommand]
    private void SelectHit(MapHitRow? row)
    {
        if (row is null) return;
        SelectedRow = row;
        Status = $"中心：{row.Hit.Title} · 来源{row.Hit.SourceLabel}。定好范围再预览。";
    }

    private void InvalidatePreview()
    {
        _preview = null;
        _previewKey = "";
        PreviewText = "";
        PreviewWarning = "";
    }

    private string KeyFor(MapSearchHit hit) =>
        $"{hit.Longitude.ToString("0.000000", CultureInfo.InvariantCulture)},"
        + $"{hit.Latitude.ToString("0.000000", CultureInfo.InvariantCulture)}"
        + $"|{RadiusMeters.ToString("0", CultureInfo.InvariantCulture)}"
        + $"|{(UseHitBoundingBox && hit.HasBoundingBox ? "box" : "r")}";

    private MapImportOptions CurrentOptions() => new()
    {
        RadiusMeters = RadiusMeters,
        UseHitBoundingBox = UseHitBoundingBox && (SelectedRow?.Hit.HasBoundingBox ?? false),
    };

    /// <summary>预览与导入共用：参数没变就不敲第二次服务器。</summary>
    private async Task<MapBuildResult> EnsureBuiltAsync(MapSearchHit hit, CancellationToken ct)
    {
        var key = KeyFor(hit);
        if (_preview is not null && _previewKey == key) return _preview;
        var built = await _importer.FetchAsync(hit, CurrentOptions(), ct);
        _preview = built;
        _previewKey = key;
        WritePreview(built);
        return built;
    }

    private void WritePreview(MapBuildResult built)
    {
        PreviewText = $"这一片实际取到：{built.Roads} 段道路 · {built.Buildings} 栋建筑 · "
            + $"{built.NamedPlaces} 个带名字的店铺或地点 · {built.Junctions} 个路口节点\n"
            + $"{built.ScaleText} · 覆盖 {built.Extent}";
        var bits = new List<string>();
        if (built.Truncated) bits.Add(built.Warning);
        if (built.Buildings == 0 && built.Roads == 0)
            bits.Add("这一片在开放街道图上几乎是空的：换个更中心的地点，或把半径调大一档。");
        else if (built.Buildings == 0)
            bits.Add("只拿到道路，没有建筑轮廓 —— 那一带的楼还没人画进开放街道图（国内小城市常见）。"
                + "地图能用，只是画面得靠 AI 补。");
        PreviewWarning = string.Join("\n", bits);
    }

    // ==================== 三条主路 ====================

    [RelayCommand]
    private async Task SearchAsync()
    {
        var text = Query.Trim();
        if (text.Length == 0)
        {
            Status = "先输入点什么：城市、区县、街道，或者「120.15,30.27」这样的经纬度。";
            return;
        }
        // 顶部那条 Key 改了就直接用：省得用户以为还要多点一颗「保存」。
        if (!string.Equals(_importer.AmapKey, AmapKeyInput.Trim(), StringComparison.Ordinal))
            SaveKeyCore(quiet: true);

        await RunAsync($"正在检索「{text}」…", async ct =>
        {
            var hits = await _importer.SearchAsync(text, ct);
            Hits.Clear();
            SelectedRow = null;
            foreach (var h in hits) Hits.Add(new MapHitRow(h));
            if (hits.Count == 0)
            {
                Status = "两边都没找到「" + text + "」：写全一点试试（「杭州市上城区」比「上城区」准）。";
                return;
            }
            if (hits[0].Source == "coord")
                Status = "按经纬度定位好了，直接预览就行。";
            else
                Status = $"找到 {hits.Count} 处，点一处定范围。";
            // 高德挂了、OSM 顶上：这话只有页面该说，服务层不出声。
            if (_importer.LastSearchNote.Length > 0)
            {
                Status = _importer.LastSearchNote;
                Toast.Warn(_importer.LastSearchNote);
            }
            SelectedRow = Hits[0];
        });
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        var hit = SelectedRow?.Hit;
        if (hit is null)
        {
            Toast.Info("先在结果里点一处。");
            return;
        }
        await RunAsync("正在数这一片的道路与建筑…", async ct =>
        {
            var built = await EnsureBuiltAsync(hit, ct);
            Status = "预览好了：确认数量与范围再导入，导入这一步不会再联网。";
        });
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var hit = SelectedRow?.Hit;
        if (hit is null)
        {
            Toast.Info("先在结果里点一处。");
            return;
        }
        await RunAsync("正在取这一片的道路与建筑…", async ct =>
        {
            var built = await EnsureBuiltAsync(hit, ct);
            if (built.Map.AllScenes.Count() == 0)
            {
                Status = "这一片一个建筑或店铺都没有，导进游戏也是一张空图：换个地点或把半径调大。";
                Toast.Warn(Status);
                return;
            }
            Status = "正在落到游戏里…";
            var msg = await _maps.AdoptAsync(built.Map, ct);
            Status = msg;
            if (msg.StartsWith("已导入", StringComparison.Ordinal)) Toast.Success(msg);
            else Toast.Warn(msg);
            InvalidatePreview();
            ImagesEnabled = _backgrounds.Enabled;
            RefreshMapState();
        });
    }

    /// <summary>人工导入：用户自己准备的地图 JSON（只有形状与路名，背景同样要靠实地取材或 AI 补）。</summary>
    [RelayCommand]
    private async Task ImportJsonAsync()
    {
        await RunAsync("正在读那个文件…", async ct =>
        {
            var msg = await _maps.ImportAsync(ct);
            Status = msg;
            if (msg.StartsWith("已导入", StringComparison.Ordinal)) Toast.Success(msg);
            else if (!msg.StartsWith("已取消", StringComparison.Ordinal)) Toast.Warn(msg);
            InvalidatePreview();
            RefreshMapState();
        });
    }

    [RelayCommand]
    private void Cancel()
    {
        if (_cts is null) return;
        _cts.Cancel();
        Status = "正在收尾…";
    }

    // ==================== 背景 ====================

    /// <summary>批量补缺失背景：一次最多 12 张，进度按真张数走，两张之间可取消。</summary>
    [RelayCommand]
    private async Task FillBackgroundAsync()
    {
        var map = _maps.Map;
        var missing = _backgrounds.CountMissing(map);
        if (missing == 0)
        {
            Toast.Info("这张图的场景都已经有了背景。");
            return;
        }
        if (!_backgrounds.Enabled)
        {
            Toast.Warn("画面生成没有开启。请到 设置 → AI → 画面生成 填接口地址与 API Key。");
            return;
        }
        var limit = Math.Min(BatchSize, Math.Min(missing, MapBackgroundService.MaxBatch));
        await RunAsync($"正在补前 {limit} 张背景（共缺 {missing} 个）…", async ct =>
        {
            var progress = new Progress<string>(s =>
            {
                _bgStep++;
                BgProgressText = s;
                BgProgress = Math.Clamp((double)_bgStep / limit, 0, 1);
            });
            var r = await _backgrounds.FillMissingAsync(map, limit, progress, ct);
            Status = r.Message ?? "补背景结束了。";
            if (r.AllOk) Toast.Success(Status);
            else if (r.Done > 0) Toast.Warn(Status);
            else Toast.Error(Status);
            RefreshMapState();
        }, backgroundPhase: true);
    }

    /// <summary>整张地图的底图：MapDefinition 自己没有全图底图层，所以它落在 maps/ 下并铺到出生场景。</summary>
    [RelayCommand]
    private async Task BackdropAsync()
    {
        var map = _maps.Map;
        if (map.AllScenes.Count() == 0)
        {
            Toast.Info("手上还没有地图：先检索一片真实城市导进来，底图才有地方铺。");
            return;
        }
        if (!_backgrounds.Enabled)
        {
            Toast.Warn("画面生成没有开启。请到 设置 → AI → 画面生成 填接口地址与 API Key。");
            return;
        }
        await RunAsync("正在生成大地图的底图…", async ct =>
        {
            var subject = SelectedRow?.Hit.Title ?? Query.Trim();
            var r = await _backgrounds.GenerateBackdropAsync(map, subject.Length > 0 ? subject : null, ct);
            BackdropNote = r.Message;
            Status = r.Message;
            if (r.Ok) Toast.Success(r.Message);
            else Toast.Error(r.Message);
            RefreshMapState();
        });
    }

    private int BatchSize => BatchChoice switch
    {
        "3 张" => 3,
        "12 张" => 12,
        _ => 6,
    };

    // ==================== 高德 Key ====================

    [RelayCommand]
    private void SaveKey() => SaveKeyCore(quiet: false);

    private void SaveKeyCore(bool quiet)
    {
        _importer.AmapKey = AmapKeyInput;
        HasAmapKey = _importer.HasAmapKey;
        SourceNote = _importer.SourceNote;
        OnPropertyChanged(nameof(AmapKeyState));
        if (quiet) return;
        Toast.Info(HasAmapKey
            ? "已存下高德 Key：下一次检索改走高德。"
            : "已清掉 Key，检索继续走开放街道图。");
    }

    [RelayCommand]
    private async Task GoBackAsync() => await Shell.Current.GoToAsync("..");

    // ==================== 跑一发的统一外壳 ====================

    /// <summary>
    /// 长任务都走这里：占住按钮、给一句真在做什么的话、失败按原因说人话，取消只认这一颗。
    /// </summary>
    private async Task RunAsync(string busyLabel, Func<CancellationToken, Task> work, bool backgroundPhase = false)
    {
        if (IsBusy)
        {
            Toast.Info(BusyLabel.Length > 0 ? BusyLabel + "，还没完 —— 想中断就点「取消」。" : "上一次还在跑。");
            return;
        }
        var cts = new CancellationTokenSource();
        _cts = cts;
        IsBusy = true;
        BusyLabel = busyLabel;
        Status = busyLabel;
        IsFillingBackground = backgroundPhase;
        if (backgroundPhase)
        {
            _bgStep = 0;
            BgProgress = 0;
            BgProgressText = busyLabel;
        }
        try
        {
            await work(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Status = cts.IsCancellationRequested
                ? "已经停下来：取到一半的不算数，重来一次就行。"
                : "等太久（约 20 秒）对方没回话。稍后再试，或把半径调小一档。";
        }
        catch (Exception ex)
        {
            var msg = Explain(ex);
            Status = msg;
            Toast.Error(msg);
            App.WriteLog("MapCreateViewModel: " + ex);
        }
        finally
        {
            IsBusy = false;
            BusyLabel = "";
            IsFillingBackground = false;
            BgProgressText = "";
            BgProgress = 0;
            _cts = null;
            cts.Dispose();
        }
    }

    /// <summary>服务层抛的都是写好的人话；剩下那些（断网、超时）才需要在这里现编。</summary>
    private static string Explain(Exception ex) => ex switch
    {
        InvalidOperationException invalid when invalid.Message.Length > 0 => invalid.Message,
        TaskCanceledException => "等太久（约 20 秒）对方没回话：稍后再试，或把半径调小一档。",
        OperationCanceledException => "已经停下来。",
        HttpRequestException http => "连不上检索服务：" + (http.InnerException?.Message ?? http.Message)
            + "（检查一下网络；国内直连开放街道图常常不通，填上高德 Key 至少检索这一步能用。）",
        _ => "没成功：" + ex.Message,
    };
}

/// <summary>检索结果的一行：包一层只为了「选中」这件事有个地方挂。</summary>
public sealed partial class MapHitRow : ObservableObject
{
    public MapHitRow(MapSearchHit hit) => Hit = hit;

    public MapSearchHit Hit { get; }
    public string Title => Hit.Title;
    public string Subtitle => Hit.Subtitle;
    public string SourceLabel => Hit.SourceLabel;

    [ObservableProperty] private bool _isSelected;
}

/// <summary>范围预设的一颗：点一下把半径设过去。</summary>
public sealed partial class MapPresetChip : ObservableObject
{
    public MapPresetChip(string label, double radius)
    {
        Label = label;
        Radius = radius;
    }

    public string Label { get; }
    public double Radius { get; }

    [ObservableProperty] private bool _isCurrent;
}
