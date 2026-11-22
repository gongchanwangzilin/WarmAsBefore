using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Dispatching;
using WarmAsBefore.Controls;
using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Drawables;
using WarmAsBefore.Helpers;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

public sealed partial class MapViewModel : ObservableObject, IDisposable
{
    // ==================== 天气来源快捷开关 ====================

    /// <summary>把当前地图的天气来源写到顶栏按钮上（进页面与每次改完都刷一次）。</summary>
    private void RefreshWeatherModeLabel()
    {
        var mode = MapWeatherModes.Normalize(_maps.Map.WeatherMode);
        WeatherModeLabel = MapWeatherModes.Display(mode);
    }

    /// <summary>
    /// 顶栏快捷切换：不用回创建页也能改「这张图按哪儿算天气」。
    /// 自定义地点在这里顺手问一句地名；校验失败就什么都不存（创建页有更完整的说明）。
    /// </summary>
    [RelayCommand]
    private async Task PickWeatherModeAsync()
    {
        try
        {
            var map = _maps.Map;
            var labels = new[]
            {
                MapWeatherModes.Display(MapWeatherModes.User),
                MapWeatherModes.Display(MapWeatherModes.Map),
                MapWeatherModes.Display(MapWeatherModes.Custom),
            };
            var codes = new[] { MapWeatherModes.User, MapWeatherModes.Map, MapWeatherModes.Custom };
            var pick = await Shell.Current.DisplayActionSheet("这张图的天气按哪儿算", "取消", null, labels);
            if (string.IsNullOrWhiteSpace(pick) || pick == "取消") return;
            var idx = Array.IndexOf(labels, pick);
            if (idx < 0) return;
            var code = codes[idx];

            if (code == MapWeatherModes.Custom)
            {
                var city = await Shell.Current.DisplayPromptAsync("自定义地点天气",
                    "填一个城市名，主界面按那个地方算天气：", "保存", "取消", "例如：杭州市", maxLength: 40);
                if (string.IsNullOrWhiteSpace(city)) return;
                map.WeatherCity = city.Trim();
            }
            map.WeatherMode = code;
            await _maps.SaveAsync();
            _maps.NotifyChanged();
            RefreshWeatherModeLabel();
            Toast.Info(code switch
            {
                MapWeatherModes.Map => "地图天气改为「地图当地」：主界面按这张图的抓取位置查天气。",
                MapWeatherModes.Custom => $"地图天气按「{map.WeatherCity}」算。",
                _ => "地图天气改回「跟随用户」：主界面按你设置里的城市查天气。",
            });
        }
        catch (Exception ex)
        {
            App.WriteLog("MapViewModel.PickWeatherMode -> " + ex);
            Toast.Error("天气来源没改成：" + ex.Message);
        }
    }

    // ==================== 模式 ====================

    partial void OnModeChanged(ModeKind value)
    {
        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(IsLinkMode));
        OnPropertyChanged(nameof(IsDoodleMode));
        OnPropertyChanged(nameof(IsViewMode));
        OnPropertyChanged(nameof(ViewModeLabel));
        OnPropertyChanged(nameof(EditModeLabel));
        OnPropertyChanged(nameof(LinkModeLabel));
        OnPropertyChanged(nameof(DoodleModeLabel));
        // 浏览态是「地图」，编辑/连线/涂鸦才是「编辑器」：框与提示只在后者露出来。
        RefreshChrome();
    }

    [RelayCommand]
    private void SetMode(string mode)
    {
        if (mode == "view") Mode = ModeKind.View;
        else if (mode == "edit") Mode = ModeKind.Edit;
        else if (mode == "link") Mode = ModeKind.Link;
        else if (mode == "doodle") Mode = ModeKind.Doodle;
        // 切模式时清空框选残留（高亮/批量菜单只在编辑模式下有意义）
        Drawable.MarqueeSelected.Clear();
        Drawable.MarqueeRect = null;
        CancelLink();
        var wasPlotting = _plotRing is not null;
        if (wasPlotting) CancelRegionPlot("");           // 切模式就丢下手上的圈（没收边的角不存盘）
        // 模式/状态提示文本更新
        OnPropertyChanged(nameof(ViewModeLabel));
        OnPropertyChanged(nameof(EditModeLabel));
        OnPropertyChanged(nameof(LinkModeLabel));
        OnPropertyChanged(nameof(DoodleModeLabel));
        StatusText = Mode switch
        {
            ModeKind.View => "左键点击卡片 = 出发前往",
            ModeKind.Edit => "左键拖动卡片，右键=菜单（新建/删除/编辑/详情/备注）",
            ModeKind.Doodle => "涂鸦：按住拖动自由画线，松手自动补端点临时节点；点卡片/节点吸附",
            _ => "连线：先点卡片/临时节点设起点，点目标连线，点空白加折点"
        };
        if (wasPlotting) StatusText += "；刚才圈到一半的地已取消";
        PingStatusStrip();
        Touch();
    }

    /// <summary>连线线型切换（直线/曲线）。</summary>
    [RelayCommand]
    private void ToggleLinkKind()
    {
        Drawable.LinkCurve = !Drawable.LinkCurve;
        StatusText = Drawable.LinkCurve ? "当前连线的线型：贝塞尔曲线" : "当前连线的线型：直线";
        OnPropertyChanged(nameof(LinkKindText));
        Touch();
    }

    public string LinkKindText => Drawable.LinkCurve ? "曲线" : "直线";

    [RelayCommand]
    private async Task ExportMap() => StatusText = await _maps.ExportAsync();

    [RelayCommand]
    private async Task ImportMap()
    {
        var note = await _maps.ImportAsync();
        await _maps.InitializeAsync();
        Refresh();
        StatusText = note;
    }

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");

    /// <summary>创建地图入口：OSM/高德/AI 生成新地图在「创建地图」页做，本页只管已有的这张。</summary>
    [RelayCommand]
    private async Task CreateMap() => await Shell.Current.GoToAsync("mapcreate");

    /// <summary>取景入口：走出去拍照、把照片钉在地图坐标上（手机端专属，电脑端只能导入照片）。</summary>
    [RelayCommand]
    private async Task OpenCapture() => await Shell.Current.GoToAsync("mapcapture");

    /// <summary>3D 灰度视图：把这张图换个「站在街上」的读法（编辑器不动，还是 2D）。</summary>
    [RelayCommand]
    private async Task Open3d() => await Shell.Current.GoToAsync("minimal3d");

    /// <summary>流程图：什么时候看、条件成不成立、依次做什么。</summary>
    [RelayCommand]
    private async Task OpenFlows() => await Shell.Current.GoToAsync("flows");

    [RelayCommand]
    private async Task AddLocation()
    {
        var name = await Shell.Current.DisplayPromptAsync("新建地点", "地点（建筑）的名字？", "确定", "取消");
        if (string.IsNullOrWhiteSpace(name)) return;
        _maps.AddLocation(name.Trim());
        await _maps.SaveAsync();
        StatusText = $"已添加地点：{name.Trim()}";
        Refresh();
    }

    [RelayCommand]
    private async Task AddScene()
    {
        var map = _maps.Map;
        if (map.Locations.Count == 0)
        {
            StatusText = "请先新建一个地点";
            return;
        }
        MapLocation target;
        if (map.Locations.Count == 1) target = map.Locations[0];
        else
        {
            var pick = await Shell.Current.DisplayActionSheet("新场景属于哪个地点？", "取消", null,
                map.Locations.Select(l => l.Name).ToArray());
            if (string.IsNullOrWhiteSpace(pick)) return;
            target = map.Locations.FirstOrDefault(l => l.Name == pick) ?? map.Locations[0];
        }
        var name = await Shell.Current.DisplayPromptAsync("新场景", "场景的名字？", "确定", "取消");
        if (string.IsNullOrWhiteSpace(name)) return;
        var scene = _maps.AddScene(target.Id, name.Trim());
        scene.X = _menuWorld == default ? -1 : _menuWorld.X;
        scene.Y = _menuWorld == default ? -1 : _menuWorld.Y;
        await _maps.SaveAsync();
        StatusText = $"已添加场景：{name.Trim()}（拖拽可摆位，右键可设背景/备注）";
        await PickSceneBackgroundAsync(scene);
        Refresh();
    }

    /// <summary>从素材库为选中场景设背景（触屏常驻按钮；无选中场景时提示先选场景）。</summary>
    [RelayCommand]
    private async Task BackgroundFromLibrary()
    {
        var node = SelectedNode;
        if (node is null || node.Scene is null)
        {
            StatusText = "请先在地图上点选一个场景卡片，再点「背景（素材库）」";
            return;
        }
        await PickBackgroundFromLibraryAsync(node.Scene);
        Refresh();
    }

    /// <summary>
    /// AI 画背景：用一句描述为选中的场景生成一张背景。
    /// 生成后走与「素材库背景」相同的落地路径（复制进 maps/、保存、标定），
    /// 这样新旧背景在地图里没有两套行为。
    /// </summary>
    [RelayCommand]
    private async Task GenerateBackground()
    {
        var node = SelectedNode;
        var scene = node?.Scene ?? _maps.CurrentScene;
        if (scene is null)
        {
            StatusText = "请先在地图上点选一个场景卡片，再点「AI 画背景」";
            return;
        }
        if (!_images.Enabled)
        {
            Toast.Warn("画面生成没有开启。请到 设置 → AI → 画面生成 填接口地址与 API Key。");
            return;
        }
        var place = node?.LocationName ?? "";
        var prompt = await Shell.Current.DisplayPromptAsync(
            "AI 画背景",
            $"描述「{scene.Name}」此刻的画面（光线、时间、气氛）。",
            "生成", "取消", placeholder: "例如：傍晚的旧书店，暖黄灯光，窗外下着小雨");
        if (string.IsNullOrWhiteSpace(prompt)) return;

        StatusText = "正在生成背景…";
        try
        {
            var (path, message) = await _images.GenerateAsync(
                $"场景背景：{(place.Length > 0 ? place + " · " : "")}{scene.Name}，{prompt.Trim()}。横向构图，不要人物，不要文字",
                "bg_" + scene.Id, "1536x1024");
            if (path is null) { StatusText = message; return; }

            var oldBg = scene.Background;
            var oldPlacement = scene.Placement;
            var rel = _maps.ImportBackgroundForScene(path, scene);
            if (rel is null) { StatusText = "背景复制失败"; return; }
            scene.Background = rel;
            scene.Placement = null;
            await _maps.SaveAsync();

            if (!await _cal.EnsureCalibratedAsync(scene, SceneCalibrationService.DefaultTargetAspect))
            {
                scene.Background = oldBg;
                scene.Placement = oldPlacement;
                await _maps.SaveAsync();
                StatusText = "已取消，背景未更换";
                return;
            }
            StatusText = $"已为「{scene.Name}」生成并应用新背景";
            Refresh();
        }
        catch (Exception ex)
        {
            StatusText = "生成失败：" + ex.Message;
            App.WriteLog("MapViewModel.GenerateBackground -> " + ex);
        }
    }

    [RelayCommand]
    private async Task RemoveScene()
    {
        if (SelectedNode is null) return;
        var ok = await Shell.Current.DisplayAlert("删除场景",
            $"确定删除「{SelectedNode.SceneName}」吗？其连线也会一并删除。", "删除", "取消");
        if (!ok) return;
        _maps.RemoveScene(SelectedNode.Id);
        SelectedNode = null;
        Refresh();
        StatusText = "已删除场景";
    }

    partial void OnSelectedNodeChanged(MapCanvasNode? value)
    {
        Drawable.SelectedNode = value;
        // 楼层条跟着「最后盯着的那栋楼」走；点空白取消选择不收起（人可能还在切层）
        if (value is not null) _floorBarLocId = value.Location.Id;
        UpdateFloorBar();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedNodeLabel));
        // 点中一个场景标记 → 底部抽屉升起（名字 / 一句话 / 距离 / 两颗按钮）
        SyncSheetFromSelection();
        Touch();
    }

    private void CancelLink()
    {
        Drawable.LinkStartNode = null;
        Drawable.LinkStartTransit = null;
        Drawable.LinkPreview.Clear();
        Drawable.Mouse = null;
    }

    /// <summary>浏览模式下点击卡片：走最短路径前往该场景。</summary>
    private async Task GoToSceneAsync(MapCanvasNode card)
    {
        if (card is null) return;
        StatusText = $"前往「{card.SceneName}」…";
        try
        {
            var note = await _maps.MoveToAsync(card.Id);
            StatusText = string.IsNullOrWhiteSpace(note) ? "" : note.Trim('（', '）');
            Refresh();
        }
        catch (Exception ex)
        {
            App.WriteLog("MapViewModel.GoToScene -> " + ex);
            StatusText = "出发失败";
        }
    }

    // ==================== 楼层条与切层 ====================

    [ObservableProperty] private bool _hasFloorBar;
    [ObservableProperty] private string _floorBarTitle = "";
    /// <summary>楼层条的档位：顶层在上（FloorsOf 的次序），「全部」退出分层视图。</summary>
    public ObservableCollection<FloorChip> FloorChips { get; } = new();
    private string? _floorBarLocId;

    // 切层动画：一次 tween，缩放与位移同 tick 驱动
    private IDispatcherTimer? _floorClock;
    private TaskCompletionSource? _floorDone;
    private DateTime _floorAnimStart;
    private double _floorAnimMs = 1;
    private double _floorScale0, _floorFromWX, _floorFromWY, _floorToWX, _floorToWY;
    private double _viewW = 1200, _viewH = 720;

    /// <summary>页面转发的画布尺寸：切层动画要把目标层摆进视口中心，得知道中心在哪。</summary>
    public void SetViewport(double w, double h)
    {
        if (w > 40 && h > 40) { _viewW = w; _viewH = h; _measured = true; }
        RefreshScaleBar();   // 比例尺是按像素折的，视口一变就得重算
        TryLandPending();
    }
}
