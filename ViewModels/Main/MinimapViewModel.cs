using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Helpers;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 右上角小地图的界面模型：换场景就重取一份材，展开/收起只改一个布尔。
///
/// 它只听 MapService 的两个事件（<see cref="MapService.SceneChanged"/> 走到新场景、
/// <see cref="MapService.MapChanged"/> 这张图被换过），由页面在进出场时挂/摘——
/// 主界面那个 VM 是 transient，反复进存档会攒下订阅，这里别再跟着学。
/// </summary>
public sealed partial class MinimapViewModel : ObservableObject
{
    private readonly MapService _maps;
    private readonly MinimapService _minimap;

    /// <summary>列表最多列这么多行：面板再长也不该把半个舞台盖住，剩下的进大地图看。</summary>
    private const int ListRows = 7;

    [ObservableProperty] private MinimapPlan? _plan;
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _subline = "";
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _listOnly;
    [ObservableProperty] private bool _hasPois;

    /// <summary>这一局没绑世界书就没有地图，整块小地图（连方片）都不该露出来。</summary>
    [ObservableProperty] private bool _visible;

    public ObservableCollection<MinimapPoi> Pois { get; } = new();

    /// <summary>方片态：这一局有地图、且人把它收了。面板与方片是同一块地方的两种活法，只留一个。</summary>
    public bool ChipVisible => Visible && !IsExpanded;

    /// <summary>展开态：整块面板（含标题、画布、地点列表）。</summary>
    public bool PanelVisible => Visible && IsExpanded;

    public MinimapViewModel(MapService maps, MinimapService minimap, bool expandedByDefault)
    {
        _maps = maps;
        _minimap = minimap;
        // 记住人上次拧的样子；没拧过的才按平台给默认（电脑摊开看，手机先收成一枚方片）。
        _isExpanded = Microsoft.Maui.Storage.Preferences.Default.Get("game.minimap.expanded", expandedByDefault);
    }

    /// <summary>页面进场时挂上（重复挂不叠加：先摘再挂）。</summary>
    public void Attach()
    {
        Detach();
        _maps.SceneChanged += OnSceneChanged;
        _maps.MapChanged += OnMapChanged;
        _maps.GeometryInvalidated += OnGeometryInvalidated;
        Rebuild();
    }

    public void Detach()
    {
        _maps.SceneChanged -= OnSceneChanged;
        _maps.MapChanged -= OnMapChanged;
        _maps.GeometryInvalidated -= OnGeometryInvalidated;
    }

    private void OnSceneChanged(MapScene _) => RequestRebuild();
    private void OnMapChanged() => RequestRebuild();
    private void OnGeometryInvalidated() => RequestRebuild();

    private bool _pending;

    /// <summary>
    /// 同一拍里连着来几个事件（改一次图会同时发 GeometryInvalidated 与 MapChanged）就只重取一次：
    /// 取材要跑一遍全图 Dijkstra，排队三次是纯浪费。
    /// </summary>
    private void RequestRebuild()
    {
        if (_pending) return;
        _pending = true;
        Ui.Post(() =>
        {
            _pending = false;
            Rebuild();
        });
    }

    /// <summary>重取一份材（调用方已在主线程上）。</summary>
    private void Rebuild()
    {
        var plan = _minimap.BuildFor(_maps.CurrentSceneId);
        Pois.Clear();
        if (plan is not null)
            foreach (var p in plan.Pois.Take(ListRows))
                Pois.Add(p);
        Plan = plan;
        Headline = plan?.Headline ?? "";
        Subline = plan?.Subline ?? "";
        ListOnly = plan is null || !plan.HasCanvas;
        HasPois = Pois.Count > 0;
    }

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        Microsoft.Maui.Storage.Preferences.Default.Set("game.minimap.expanded", value);
        NotifyForm();
    }

    partial void OnVisibleChanged(bool value)
    {
        NotifyForm();
        // 进场时地图往往还没加载（Attach 那一拍取材只会拿到空），解锁后再补一次：
        // 不补就得等第一次换场景才画得出东西，用户看到的是永远一张白纸。
        if (value) RequestRebuild();
    }

    /// <summary>方片/面板谁露出：这两个计算属性没有自己的赋值口，改完形参得手动喊一声。</summary>
    private void NotifyForm()
    {
        OnPropertyChanged(nameof(ChipVisible));
        OnPropertyChanged(nameof(PanelVisible));
    }

    /// <summary>点方片：展开（收起态下画布是透明的，点按都归这枚方片）。</summary>
    [RelayCommand]
    private void Expand() => IsExpanded = true;

    /// <summary>列表那一行（或画布上的一个点）：进大地图并定位到它。</summary>
    [RelayCommand]
    private async Task OpenPoi(string? sceneId)
    {
        if (string.IsNullOrEmpty(sceneId)) return;
        await Shell.Current.GoToAsync($"map?sceneId={Uri.EscapeDataString(sceneId)}");
    }

    /// <summary>面板顶上的「大地图」：不带定位，就是原来那颗地图钮。</summary>
    [RelayCommand]
    private async Task OpenMap() => await Shell.Current.GoToAsync("map");
}
