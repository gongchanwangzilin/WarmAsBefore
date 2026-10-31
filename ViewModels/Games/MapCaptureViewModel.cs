using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 取景会话：规定开始地点 → 走路随拍。一张照一个点——
/// 街道照落「某条边 + 弧长位置」（同一街道不同位置可各拍一张），室内照必须落
/// 「楼层 + 户型（+ 门）」，不匹配的绑定在 MapService.AddPhotoAsync 的一致性校验里被拦。
/// 空间绑定靠选单不靠画布点按：手机上一个下拉 + 一根位置滑杆比在画布上戳点稳得多，
/// 画布点按留给以后的升级。
/// </summary>
public sealed partial class MapCaptureViewModel : ObservableObject
{
    public MapCaptureViewModel(MapService maps)
    {
        _maps = maps;
        // 取景只对手机开放（相机在这儿）；电脑端进这页是「照片导入」的脸。
        CaptureAvailable = DeviceInfo.Platform == DevicePlatform.Android;
    }

    private readonly MapService _maps;

    /// <summary>取景（相机）是不是这台设备的事；电脑端这页只摆导入。</summary>
    public bool CaptureAvailable { get; }
    public bool ShowCaptureUnavailable => !CaptureAvailable;

    // ============ 会话：开始地点 ============

    public ObservableCollection<string> StartOptions { get; } = new();
    [ObservableProperty] private string _startPick = "";
    [ObservableProperty] private bool _sessionActive;
    [ObservableProperty] private string _sessionTitle = "还没出发";

    // ============ 本张的绑定 ============

    /// <summary>true = 街道照；false = 室内照。</summary>
    [ObservableProperty] private bool _streetMode = true;

    public ObservableCollection<StreetOption> Streets { get; } = new();
    [ObservableProperty] private StreetOption? _selectedStreet;
    [ObservableProperty] private double _streetT = 0.5;

    public ObservableCollection<string> BuildingOptions { get; } = new();
    [ObservableProperty] private string _buildingPick = "";
    [ObservableProperty] private string _buildingId = "";

    public ObservableCollection<string> FloorOptions { get; } = new();
    [ObservableProperty] private string _floorPick = "";
    [ObservableProperty] private int? _floorValue;

    public ObservableCollection<RoomOption> Rooms { get; } = new();
    [ObservableProperty] private RoomOption? _selectedRoom;

    public ObservableCollection<string> DoorOptions { get; } = new();
    [ObservableProperty] private string _doorPick = "";
    [ObservableProperty] private string _doorId = "";

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _busy;

    /// <summary>没照片时那行提示。Reload 收口时翻牌。</summary>
    public bool PhotosEmpty => Photos.Count == 0;

    // 模式二选钮的点亮底：与用量页同一套 token 口径（资源里有就用，没有退手写的暖色）。
    private static Color Token(string key, string fallback)
    {
        try { return Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : Color.FromArgb(fallback); }
        catch { return Color.FromArgb(fallback); }
    }

    private static readonly Color ChipOn = Token("AccentWarm", "#F5D8B8");

    public Color StreetModeBg => StreetMode ? ChipOn : Colors.Transparent;
    public Color IndoorModeBg => StreetMode ? Colors.Transparent : ChipOn;
    public bool IndoorModeVisible => !StreetMode;
    public bool CanCapture => !Busy;

    partial void OnStreetModeChanged(bool value)
    {
        OnPropertyChanged(nameof(StreetModeBg));
        OnPropertyChanged(nameof(IndoorModeBg));
        OnPropertyChanged(nameof(IndoorModeVisible));
    }

    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanCapture));

    [RelayCommand]
    private void SetStreetMode(string? mode) => StreetMode = mode != "indoor";

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");

    // ============ 已拍的 ============

    public ObservableCollection<PhotoRow> Photos { get; } = new();

    public sealed record StreetOption(string Id, string Display)
    {
        public override string ToString() => Display;
    }

    public sealed record RoomOption(string SceneId, string Display, int? Floor, string Unit, string BuildingName)
    {
        public override string ToString() => Display;
    }

    public sealed class PhotoRow
    {
        public string Id { get; init; } = "";
        public int Order { get; init; }
        public string Thumb { get; init; } = "";
        public string Line { get; init; } = "";
        public string Tag { get; init; } = "";
    }

    // ============ 生命周期 ============

    [RelayCommand]
    private async Task Appearing()
    {
        try
        {
            await _maps.InitializeAsync();
            RebuildPick();
            await ReloadPhotosAsync();
        }
        catch (Exception ex) { App.WriteLog("MapCapture.Appearing -> " + ex.Message); }
    }

    private void RebuildPick()
    {
        var map = _maps.Map;

        // 开始地点：全场景（含不分层的第一张），不给空名
        StartOptions.Clear();
        foreach (var s in map.AllScenes)
            StartOptions.Add(s.Name.Length == 0 ? "未命名场景" : s.Name);

        // 街道：所有连线（户型内的不算街面），显示用「路名 / 端点—端点」
        Streets.Clear();
        foreach (var e in map.Edges)
        {
            if (string.Equals(e.Road, RoadKinds.Interior, StringComparison.Ordinal)) continue;
            var label = !string.IsNullOrWhiteSpace(e.RoadName)
                ? $"{e.RoadName}（{RoadKinds.Display(e.Road)}）"
                : $"{map.PointNameById(e.A) ?? "一端"} — {map.PointNameById(e.B) ?? "另一端"}（{RoadKinds.Display(e.Road)}）";
            if (map.Edges.Count(x => x.Road == e.Road && x.RoadName == e.RoadName && x.RoadName != "") > 1)
                label += $" · {map.LocationNameOf(e.A)}/{map.LocationNameOf(e.B)}";
            Streets.Add(new StreetOption(e.Id, label));
        }

        BuildingOptions.Clear();
        foreach (var l in map.Locations.Where(l => l.Scenes.Count > 0))
            BuildingOptions.Add(l.Name);

        SyncIndoorPickers();
    }

    partial void OnBuildingPickChanged(string value)
    {
        var map = _maps.Map;
        _buildingId = map.Locations.FirstOrDefault(l => l.Name == BuildingPick)?.Id ?? "";
        rebuildFloors();
        SyncIndoorPickers();
    }

    partial void OnFloorPickChanged(string value)
    {
        _floorValue = tryFloorKey(FloorPick);
        SyncIndoorPickers();
    }

    partial void OnSelectedRoomChanged(RoomOption? value)
    {
        // 门：可空（拍户型本身）+ 连到这个户型的所有门
        DoorOptions.Clear();
        DoorId = "";
        if (value?.SceneId is { } sid)
        {
            DoorOptions.Add("户型本身");
            foreach (var d in _maps.Map.DoorsOpeningTo(sid))
                DoorOptions.Add(string.IsNullOrWhiteSpace(d.Name) ? "无名门" : d.Name);
            _doorIds.Clear();
            _doorIds.Add("");   // 户型本身 = 门为空
            foreach (var d in _maps.Map.DoorsOpeningTo(sid))
                _doorIds.Add(d.Id);
        }
        if (DoorOptions.Count > 0) DoorPick = DoorOptions[0];
    }

    partial void OnDoorPickChanged(string value)
    {
        var i = DoorOptions.IndexOf(value);
        DoorId = i >= 0 && i < _doorIds.Count ? _doorIds[i] : "";
    }

    private readonly List<string> _doorIds = new();
    private readonly List<int?> _floorKeys = new();

    private void rebuildFloors()
    {
        FloorOptions.Clear();
        _floorKeys.Clear();
        _floorValue = null;
        var map = _maps.Map;
        var loc = map.LocationById(_buildingId);
        if (loc is null) return;
        foreach (var f in map.FloorsOf(loc.Id))
        {
            _floorKeys.Add(f);
            FloorOptions.Add(MapDefinition.FloorLabel(f));
        }
        if (FloorOptions.Count > 0) FloorPick = FloorOptions[0];
    }

    private int? tryFloorKey(string label)
    {
        var i = FloorOptions.IndexOf(label);
        return i >= 0 && i < _floorKeys.Count ? _floorKeys[i] : null;
    }

    /// <summary>楼层定了 → 摆这一层的户型（场景）。楼层变化重摆，选中的户型失效。</summary>
    private void SyncIndoorPickers()
    {
        var map = _maps.Map;
        Rooms.Clear();
        SelectedRoom = null;
        var floor = _floorValue ?? tryFloorKey(FloorPick);
        var loc = map.LocationById(_buildingId);
        if (loc is null || floor is null)
        {
            // 没选楼栋/楼层时给「不分层」的场景兜底，让室内档随时可拍
            floor = null;
            foreach (var s in loc?.Scenes.Where(s => s.Floor is null) ?? Enumerable.Empty<MapScene>())
                Rooms.Add(new RoomOption(s.Id, s.Name.Length == 0 ? "未命名户型" : s.Name, s.Floor, s.Unit, loc?.Name ?? ""));
        }
        else
        {
            foreach (var s in loc.Scenes.Where(s => s.Floor == floor))
                Rooms.Add(new RoomOption(s.Id, string.IsNullOrWhiteSpace(s.Unit) ? s.Name : $"{s.Unit} · {s.Name}", s.Floor, s.Unit, loc.Name));
        }
        if (Rooms.Count > 0) SelectedRoom = Rooms[0];
    }

    // ============ 会话 ============

    /// <summary>规定开始地点：session 的锚点。开始地点本身不产生照片，照片跟着脚步走。</summary>
    [RelayCommand]
    private void StartSession()
    {
        if (string.IsNullOrWhiteSpace(StartPick))
        {
            StatusText = "先选一个出发的场景。";
            return;
        }
        SessionActive = true;
        SessionTitle = $"从「{StartPick}」出发";
        StatusText = "开始拍吧：每到一个位置给它一张。";
    }

    [RelayCommand]
    private void StopSession()
    {
        SessionActive = false;
        SessionTitle = "还没出发";
        StatusText = "会话结束。下次进来照片还在。";
    }

    // ============ 拍照 ============

    private const int MaxPhotosPerSession = 200;

    [RelayCommand]
    private async Task CaptureAsync()
    {
        if (Busy) return;
        if (!SessionActive) { StatusText = "先规定开始地点，再开始拍。"; return; }
        if (_maps.Map.Photos.Count >= MaxPhotosPerSession) { StatusText = "照片攒到 200 张了，先删几张再拍。"; return; }

        // 电脑端不取景（产品口径）： 这页只在手机露出，但命令级再拦一道，
        // 免得桌面真机调试时误开 Windows 相机 UI 走错通路。
        if (DeviceInfo.Platform != DevicePlatform.Android)
        {
            StatusText = "取景是手机端的事：电脑端请把照片传过来再「导入」。";
            return;
        }
        if (!StreetMode && SelectedRoom is null)
        {
            StatusText = "室内照要先选楼栋、楼层和户型，再拍。";
            return;
        }

        try
        {
            var camera = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (camera != PermissionStatus.Granted)
                camera = await Permissions.RequestAsync<Permissions.Camera>();
            if (camera != PermissionStatus.Granted)
            {
                StatusText = "没有相机权限，拍不了。去系统设置里放行。";
                return;
            }

            Busy = true;
            var shot = await MediaPicker.CapturePhotoAsync(new MediaPickerOptions { Title = "取景" });
            if (shot is null) { StatusText = ""; return; }

            var (ok, msg) = await _maps.AddPhotoAsync(shot.FullPath, p =>
            {
                p.Source = "mobile";
                if (StreetMode)
                {
                    p.Binding = PhotoBindings.Street;
                    p.StreetId = SelectedStreet?.Id ?? "";
                    p.StreetT = Math.Clamp(StreetT, 0, 1);
                }
                else
                {
                    var room = SelectedRoom;
                    p.Binding = PhotoBindings.Indoor;
                    p.Floor = room?.Floor;
                    p.Unit = room?.Unit ?? "";
                    p.TargetSceneId = room?.SceneId ?? "";
                    p.DoorRef = DoorId;
                }
            });

            if (!ok) { StatusText = msg; return; }
            StatusText = "拍好了。街面同一处可以多拍几张，室内一次一层一户。";
            await ReloadPhotosAsync();
        }
        catch (Exception ex)
        {
            App.WriteLog("MapCapture.Capture -> " + ex);
            StatusText = "拍的时候出了点岔子：" + ex.Message;
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task DeleteAsync(PhotoRow? row)
    {
        if (row is null) return;
        var ok = await Shell.Current.DisplayAlert("删这张照片", "删了就取不回来（文件一并删除）。确认？", "删", "留着");
        if (!ok) return;
        _maps.RemovePhoto(row.Id);
        if (SelectedPhoto?.Id == row.Id) SelectedPhoto = null;
        await ReloadPhotosAsync();
    }

    // ============ 电脑端导入：照片先落账（未绑），选中后把绑定记上去 ============

    /// <summary>当前选中的照片（逐张补绑就靠它）。null = 绑定选择器送「下一张拍摄的」。</summary>
    [ObservableProperty] private PhotoRow? _selectedPhoto;

    public string BindingTargetText => SelectedPhoto is null
        ? "这组绑定给谁：新拍的那张"
        : $"这组绑定记到：第 {SelectedPhoto.Order} 张（{SelectedPhoto.Tag}）";

    partial void OnSelectedPhotoChanged(PhotoRow? value) => OnPropertyChanged(nameof(BindingTargetText));

    [RelayCommand]
    private void SelectPhoto(PhotoRow? row)
        => SelectedPhoto = row is null || row.Id == SelectedPhoto?.Id ? null : row;

    /// <summary>把面板上这组绑定记到选中那张。车道照选街道、室内照选楼层户型，记上才算数。</summary>
    [RelayCommand]
    private async Task ApplyBindingAsync()
    {
        if (SelectedPhoto is null) { StatusText = "先点一张照片的「选」，再记绑定。"; return; }
        if (!StreetMode && SelectedRoom is null) { StatusText = "室内要先选楼栋、楼层和户型。"; return; }

        var err = await _maps.RebindPhotoAsync(SelectedPhoto.Id, p =>
        {
            if (StreetMode)
            {
                p.Binding = PhotoBindings.Street;
                p.StreetId = SelectedStreet?.Id ?? "";
                p.StreetT = Math.Clamp(StreetT, 0, 1);
            }
            else
            {
                var room = SelectedRoom;
                p.Binding = PhotoBindings.Indoor;
                p.Floor = room?.Floor;
                p.Unit = room?.Unit ?? "";
                p.TargetSceneId = room?.SceneId ?? "";
                p.DoorRef = DoorId;
            }
        });
        if (err is not null) { StatusText = err; return; }
        StatusText = "绑定记上了。同一处多张也行——街上不同点各来一张。";
        await ReloadPhotosAsync();
    }

    /// <summary>电脑/手机通用：从文件选若干张进「未绑」区，逐张再补绑定。</summary>
    [RelayCommand]
    private async Task ImportPhotosAsync()
    {
        try
        {
            var picks = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "选要导入的照片（可多选）",
                FileTypes = FilePickerFileType.Images
            });
            if (picks is null) return;

            Busy = true;
            var n = 0; string? fail = null;
            foreach (var f in picks)
            {
                var (ok, msg) = await _maps.AddPhotoAsync(f.FullPath, p =>
                {
                    p.Source = "manual";
                    p.Binding = "";   // 先落账，之后逐张补绑
                });
                if (ok) n++;
                else fail ??= msg;
            }
            StatusText = fail is null
                ? $"导入 {n} 张，全是「未绑」态：选中一张 → 挑绑定 → 记上。"
                : $"导入 {n} 张；有 1 条失败：{fail}";
            await ReloadPhotosAsync();
        }
        catch (Exception ex)
        {
            App.WriteLog("MapCapture.Import -> " + ex);
            StatusText = "导入时出了岔子：" + ex.Message;
        }
        finally { Busy = false; }
    }

    /// <summary>把手机拍的整包（照片+绑定+顺序）搬去另一台机器。</summary>
    [RelayCommand]
    private async Task ExportBundleAsync()
        => StatusText = await _maps.ExportCaptureBundleAsync();

    /// <summary>开一个取景包：照片与门按 Id 合并进当前这张图，不整张换图。</summary>
    [RelayCommand]
    private async Task ImportBundleAsync()
    {
        StatusText = await _maps.ImportCaptureBundleAsync();
        await ReloadPhotosAsync();
    }

    private async Task ReloadPhotosAsync()
    {
        try
        {
            Photos.Clear();
            foreach (var p in _maps.Map.Photos.OrderBy(x => x.Order).ToList())
            {
                var full = _maps.ResolvePhotoPath(p.RelPath);
                var bindingLabel = p.IsIndoor ? "室内"
                    : string.Equals(p.Binding, PhotoBindings.Street, StringComparison.Ordinal) ? "街道"
                    : "未绑";
                Photos.Add(new PhotoRow
                {
                    Id = p.Id,
                    Order = p.Order,
                    Thumb = full ?? "",
                    Tag = bindingLabel,
                    Line = DescribeBinding(p),
                });
            }
            OnPropertyChanged(nameof(PhotosEmpty));
        }
        catch (Exception ex) { App.WriteLog("MapCapture.Photos -> " + ex.Message); }
    }

    private string DescribeBinding(ScenePhoto p)
    {
        if (string.IsNullOrWhiteSpace(p.Binding))
            return "待绑定：点「选」选中这张，再挑绑定记上。";
        var map = _maps.Map;
        if (p.IsIndoor)
        {
            var scene = map.SceneById(p.TargetSceneId);
            var door = string.IsNullOrWhiteSpace(p.DoorRef) ? "" : map.DoorById(p.DoorRef)?.Name;
            var unitLabel = string.IsNullOrWhiteSpace(p.Unit) ? scene?.Name ?? "未选户型" : p.Unit;
            return $"{MapDefinition.FloorLabel(p.Floor)} · {unitLabel}"
                 + (string.IsNullOrWhiteSpace(door) ? "" : $" · 过「{door}」");
        }
        var edge = map.EdgeById(p.StreetId);
        var name = edge is null ? "未绑街道"
            : (!string.IsNullOrWhiteSpace(edge.RoadName) ? edge.RoadName
               : $"{map.PointNameById(edge.A) ?? "一端"} — {map.PointNameById(edge.B) ?? "另一端"}");
        return $"{name} · 位置 {p.StreetT:P0}";
    }
}
