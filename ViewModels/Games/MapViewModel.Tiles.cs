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
    // ==================== 从别处（小地图）带着「要看哪一格」进来 ====================

    private bool _measured;
    private string? _pendingFocus;

    /// <summary>
    /// 定位到某个场景：分层视图先切到它那一层（不然摆过去也是被淡掉的一张），
    /// 再选中卡片、按当前缩放把它钉进视口中心。
    ///
    /// 页面是「构造 → 读盘 → 量尺寸」，跳转参数在量尺寸之前就到，所以这里不硬摆：
    /// 缺哪一样就先记着 Id，等 Refresh（节点表齐）或 SetViewport（视口齐）再补落。
    /// </summary>
    public void FocusScene(string sceneId)
    {
        _pendingFocus = sceneId;
        TryLandPending();
    }

    private void TryLandPending()
    {
        if (_pendingFocus is not { } id) return;
        if (!_measured || Nodes.Count == 0) return;
        _pendingFocus = null;
        var scene = _maps.Map.SceneById(id);
        var node = Nodes.FirstOrDefault(n => n.Id == id);
        if (scene is null || node is null) return;
        var locId = _maps.Map.LocationIdOfScene(id);
        if (_maps.Map.HasFloors(locId))
        {
            Drawable.FloorLocationId = locId;
            Drawable.FloorFilter = scene.Floor;
        }
        // 选中卡片顺带把楼层条挪到它所属的楼（见 OnSelectedNodeChanged），所以摆位次之。
        SelectedNode = node;
        LandFloor(node.X + MapCanvasDrawable.CardW / 2, node.Y + MapCanvasDrawable.CardH / 2, Drawable.Scale);
        Touch();
    }

    /// <summary>楼层条跟着「最后盯着的那栋楼」走：选中的卡片所属地点，或右键点中的建筑框。</summary>
    private void UpdateFloorBar()
    {
        var map = _maps.Map;
        var loc = _floorBarLocId is null ? null : map.LocationById(_floorBarLocId);
        if (loc is null || !map.HasFloors(loc.Id))
        {
            HasFloorBar = false;
            FloorChips.Clear();
            // 楼没了 / 不分层：分层视图跟着退，不然画布一直淡着别的层
            if (Drawable.FloorLocationId is not null && Drawable.FloorLocationId == _floorBarLocId)
            {
                Drawable.FloorLocationId = null;
                Drawable.FloorFilter = null;
            }
            return;
        }
        FloorBarTitle = loc.Name;
        HasFloorBar = true;
        var active = Drawable.FloorLocationId == loc.Id;
        var current = active ? Drawable.FloorFilter : (int?)null;
        FloorChips.Clear();
        FloorChips.Add(new FloorChip(this, "全部", null, isAll: true));
        foreach (var f in map.FloorsOf(loc.Id))
            FloorChips.Add(new FloorChip(this, MapDefinition.FloorLabel(f), f, isAll: false));
        foreach (var c in FloorChips)
            c.IsCurrent = c.IsAll ? !active : active && c.Floor == current;
    }

    internal void TapFloor(FloorChip chip)
    {
        // 点「全部」或再点当前层 = 退出分层视图
        var exit = chip.IsAll ||
                   (_floorBarLocId is not null && Drawable.FloorLocationId == _floorBarLocId
                    && Drawable.FloorFilter == chip.Floor);
        _ = SwitchFloorAsync(exit ? null : chip.Floor);
    }

    /// <summary>
    /// 切层：把「换楼层」演成「楼在往上/往下抬」——同一条 tween 里缩放与位移同时走：
    /// 中途抬远一档看清整层，同时把目标层摆向视口中心，末段落回原缩放。
    /// 只平移会被驳回（看不清过程），先缩放再平移两段式也驳回过（动作散）。
    /// </summary>
    private async Task SwitchFloorAsync(int? floor)
    {
        try
        {
            var locId = _floorBarLocId;
            if (locId is null) return;
            if (floor is null)
            {
                // 退出分层：影子层本来就在原位，不需要「落」这一下
                Drawable.FloorLocationId = null;
                Drawable.FloorFilter = null;
                UpdateFloorBar();
                Touch();
                return;
            }
            var box = FloorBounds(locId, floor);
            Drawable.FloorLocationId = locId;
            Drawable.FloorFilter = floor;
            UpdateFloorBar();
            Touch();
            if (box is { } b) await AnimateFloorAsync(b);   // 目标层没房间：只切状态，不演动画
        }
        catch (Exception ex)
        {
            App.WriteLog("MapViewModel.SwitchFloor -> " + ex);
            StopFloorAnim();
        }
    }

    /// <summary>目标层上全部卡片的包围盒（世界坐标）；一层都没摆就返回 null。</summary>
    private Microsoft.Maui.Graphics.RectF? FloorBounds(string locId, int? floor)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var found = false;
        foreach (var s in _maps.Map.ScenesOnFloor(locId, floor))
        {
            if (s.X < 0 || s.Y < 0) continue;
            minX = Math.Min(minX, s.X);
            minY = Math.Min(minY, s.Y);
            maxX = Math.Max(maxX, s.X + MapCanvasDrawable.CardW);
            maxY = Math.Max(maxY, s.Y + MapCanvasDrawable.CardH);
            found = true;
        }
        return found ? new Microsoft.Maui.Graphics.RectF((float)minX, (float)minY,
            (float)(maxX - minX), (float)(maxY - minY)) : null;
    }

    private async Task AnimateFloorAsync(Microsoft.Maui.Graphics.RectF box)
    {
        StopFloorAnim();
        var ms = MotionTokens.Ms(MotionTokens.PageSweepMs);   // 时长只有一个来源：过场档 440ms
        if (ms <= 0)
        {
            LandFloor(box.Center.X, box.Center.Y, Drawable.Scale);
            Touch();
            return;
        }
        _floorScale0 = Drawable.Scale;
        _floorFromWX = (_viewW / 2 - Drawable.OffsetX) / _floorScale0;
        _floorFromWY = (_viewH / 2 - Drawable.OffsetY) / _floorScale0;
        _floorToWX = box.Center.X;
        _floorToWY = box.Center.Y;
        _floorAnimMs = ms;
        _floorAnimStart = DateTime.UtcNow;
        _floorDone = new TaskCompletionSource();
        _floorClock = Application.Current?.Dispatcher.CreateTimer();
        if (_floorClock is null)
        {
            _floorDone = null;
            LandFloor(_floorToWX, _floorToWY, _floorScale0);
            Touch();
            return;
        }
        _floorClock.Interval = TimeSpan.FromMilliseconds(MotionTokens.FrameMs);
        _floorClock.Tick += OnFloorAnimTick;
        _floorClock.Start();
        await _floorDone.Task;
    }

    private void OnFloorAnimTick(object? sender, EventArgs e)
    {
        var t = (DateTime.UtcNow - _floorAnimStart).TotalMilliseconds / _floorAnimMs;
        if (t >= 1)
        {
            StopFloorAnim();
            LandFloor(_floorToWX, _floorToWY, _floorScale0);
            Touch();
            return;
        }
        // 位移走 Mech（机器在定位，不是软材料回弹），缩放走同一进度上的 sin 抬升：
        // 起终点都落回原缩放、中段最远。拉太远就看不出是从哪层抬上来的，太近又读不出「抬」。
        var c = Math.Clamp(t, 0, 1);
        var p = MotionTokens.Mech(c);
        var lift = Math.Sin(Math.PI * c);
        var scale = _floorScale0 * (1 - 0.18 * lift);
        var wx = _floorFromWX + (_floorToWX - _floorFromWX) * p;
        var wy = _floorFromWY + (_floorToWY - _floorFromWY) * p;
        Drawable.Scale = scale;
        Drawable.OffsetX = _viewW / 2 - wx * scale;
        Drawable.OffsetY = _viewH / 2 - wy * scale;
        Touch();
    }

    private void LandFloor(double worldX, double worldY, double scale)
    {
        Drawable.Scale = scale;
        Drawable.OffsetX = _viewW / 2 - worldX * scale;
        Drawable.OffsetY = _viewH / 2 - worldY * scale;
    }

    private void StopFloorAnim()
    {
        if (_floorClock is not null)
        {
            _floorClock.Stop();
            _floorClock.Tick -= OnFloorAnimTick;
            _floorClock = null;
        }
        var done = _floorDone;
        _floorDone = null;
        done?.TrySetResult();
    }

    /// <summary>右键（屏幕坐标）→ 上下文菜单。</summary>
    public void RightClick(PointF screen)
    {
        var world = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
        _menuOpenedAt = DateTime.Now;   // 标记本次右键将打开菜单，抑制随后的 Touch 回声
        _audio.PlayAssigned("right");
        if (Drawable.Menu is not null) { Drawable.Menu = null; Touch(); return; }
        if ((Mode == ModeKind.Link || Mode == ModeKind.Doodle)
            && (Drawable.LinkStartNode is not null || Drawable.LinkStartTransit is not null))
        {
            CancelLink();
            StatusText = "已取消连线/涂鸦";
            Touch();
            return;
        }
        // 圈地进行中：右键 = 收边（角还不够多就只是取消，围不出第二块地）。
        if (_plotRing is not null)
        {
            if (_plotRing.Count >= PlotMinCorners) _ = FinishRegionPlotAsync();
            else CancelRegionPlot("角还不够，已取消圈地");
            Touch();
            return;
        }

        var card = Drawable.HitCard(world);
        if (card is not null)
        {
            _floorBarLocId = card.Location.Id;
            UpdateFloorBar();
            Drawable.Menu = new ContextMenuState
            {
                Pos = world,
                Title = card.SceneName,
                TargetKind = "scene",
                TargetId = card.Id,
                Items = new()
                {
                    ("出发", "go"),
                    ("✎ 编辑名称", "rename"),
                    ("背景图（素材库）", "bg-lib"),
                    ("背景图（电脑导入）", "bg"),
                    ("标定这张背景", "cal"),
                    ("AI 出简介", "ai-note"),
                    ($"AI 选站位：{(card.Scene.Placement?.Landing == LandingKinds.Point && PlacementMath.Validate(card.Scene.Placement).Ok ? "挪锚点" : "先标定")}", "ai-anchor"),
                    ("AI 画比例", "ai-ratio"),
                    ($"站位：{(card.Scene.FreePlacement ? "不规定（自由铺）" : "规定（走标定）")}", "stand"),
                    ("清除背景", "bg-clear"),
                    ($"所在楼层：{(card.Scene.Floor is null ? "不分层" : MapDefinition.FloorLabel(card.Scene.Floor))}", "floor"),
                    ($"户型：{(string.IsNullOrWhiteSpace(card.Scene.Unit) ? "未分户" : card.Scene.Unit)}", "unit"),
                    ($"{(card.Scene.Indoor ? "改为室外" : "改为室内")}", "indoor"),
                    ("备注", "note"),
                    ("ℹ 详细信息", "info"),
                    ("删除", "del")
                }
            };
            Touch();
            return;
        }

        var transit = Drawable.HitTransit(world);
        if (transit is not null)
        {
            Drawable.Menu = new ContextMenuState
            {
                Pos = world,
                Title = transit.Name,
                TargetKind = "transit",
                TargetId = transit.Id,
                Items = new()
                {
                    ("✎ 编辑名称", "edit"),
                    ($"这是什么路口：{JunctionKinds.Display(transit.Transit.Junction)}", "junction"),
                    ("备注", "note"),
                    ("ℹ 详细信息", "info"),
                    ("删除", "del")
                }
            };
            Touch();
            return;
        }

        var edge = Drawable.HitEdge(world);
        if (edge is not null)
        {
            _menuWorld = world;   // 「由此进入 3D」要用点按位置（最近采样点在附近）
            Drawable.SelectedEdgeId = edge.Id;
            var roadTitle = string.IsNullOrWhiteSpace(edge.RoadName)
                ? RoadKinds.Display(edge.Road)
                : $"{edge.RoadName}（{RoadKinds.Display(edge.Road)}）";
            var edgeItems = new List<(string Label, string Cmd)>
            {
                ("由此进入 3D", "enter3d"),
                ($"这条路是：{RoadKinds.Display(edge.Road)}", "road"),
                ("命名这条路", "roadname"),
                ($"◀ 单行道：{(edge.OneWay ? "通" : "双向")}", "oneway"),
                ($"红绿灯：{(edge.TrafficLight ? "有" : "无")}", "light"),
                ($"收费站：{(edge.Toll ? $"过路 {edge.TollFee:F0} 元" : "无")}", "toll"),
                ($"限速：{(edge.SpeedKmh > 0 ? $"{edge.SpeedKmh:F0} km/h" : $"按类别 ≈{RoadKinds.DefaultSpeedKmh(edge.Road):F0}")}", "speed"),
                ("编辑长度", "length"),
                ($"线型: {edge.Kind}", "kind"),
                ("备注", "note"),
                ("ℹ 详细信息", "info"),
                ("删除", "del")
            };
            if (edge.TrafficLight)
            {
                // 挂了灯才问得到这三样：现在什么色（含倒计时）、一轮多长、绿灯放行哪一侧。
                edgeItems.Insert(5, ($"现在：{LightNowText(edge)}（顺箭头那侧）", "signal-now"));
                edgeItems.Insert(6, ($"⏱ 灯周期：{TrafficSignalService.CycleText(edge)}", "signal-cycle"));
                edgeItems.Insert(7, ($"绿灯朝向：{SignalPhases.Display(edge.SignalPhase)}", "signal-phase"));
            }
            Drawable.Menu = new ContextMenuState
            {
                Pos = world,
                Title = roadTitle,
                TargetKind = "edge",
                TargetId = edge.Id,
                Items = edgeItems
            };
            Touch();
            return;
        }

        // 空白：新建。目标地点优先级：右键落点命中的最深框 → 框选命中的地点（仅落点不在任何框内时兜底）
        _menuWorld = world;
        var hitLoc = Drawable.HitLocationFrame(world);
        var marqueeLoc = hitLoc is null && Drawable.MarqueeSelected.Count == 1
            ? _maps.Map.Locations.FirstOrDefault(l => Drawable.MarqueeSelected.Contains(l.Id))
            : null;
        var inLoc = hitLoc ?? marqueeLoc;
        var under = Drawable.HitRegion(world);
        if (inLoc is null && under is not null)
        {
            // 脚下是一块地（公园/湖面/航道）却没有楼框：先给这块地的操作，新建三项照旧塞在同一张菜单里。
            OpenRegionMenu(under, world);
            Touch();
            return;
        }
        var menuItems = new List<(string Label, string Cmd)>
        {
            ("新建地点", "newloc"),
            ($"圈一块地（{RegionKinds.Display(RegionKinds.Green)}/水面/航道…）", "newregion"),
            ("创建地图…", "create-map")
        };
        if (inLoc is not null)
        {
            if (under is not null) menuItems.Add(($"脚下的地块：{NameOf(under)}", "underfoot"));
            // 明确告知归属：框选时以框选地点为归属，右键落点不再覆盖
            var titlePart = marqueeLoc is not null ? "（框选）" : "";
            menuItems.Add(("新建场景" + titlePart, "newscene"));
            menuItems.Add(("新建子地点", "newchild"));
            menuItems.Add(("◉ 新建临时节点", "newtransit"));
            menuItems.Add(("✎ 重命名此地", "renloc"));
            menuItems.Add(("地上层数…", "floors"));
            menuItems.Add(("地下层数…", "basement"));
            menuItems.Add(("层高（米）…", "height"));
            menuItems.Add(("地址…", "address"));
            menuItems.Add(("删除此地点", "delloc"));
        }
        else
        {
            menuItems.Add(("新建场景", "newscene"));
            menuItems.Add(("◉ 新建临时节点", "newtransit"));
        }
        // 批量嵌套：只要有框选，就提供“批量归入”入口（新建或已有父地点）
        if (Drawable.MarqueeSelected.Count > 0)
        {
            menuItems.Add(($"批量归入新建地点（{Drawable.MarqueeSelected.Count}）", "nestnew"));
            menuItems.Add(($"批量归入已有地点（{Drawable.MarqueeSelected.Count}）", "nestinto"));
        }
        // 右键点中的这栋楼接管楼层条（不分层时楼层条自己会收起）
        if (inLoc is not null)
        {
            _floorBarLocId = inLoc.Id;
            UpdateFloorBar();
        }
        Drawable.Menu = new ContextMenuState
        {
            Pos = world,
            Title = inLoc is not null ? $"{(inLoc.Name)}" : "新建…",
            TargetKind = inLoc is not null ? "loc" : "blank",
            TargetId = inLoc?.Id ?? "",
            Items = menuItems
        };
        Touch();
    }

    // ==================== 地块（绿地 / 水面 / 航道 / 广场 / 分区） ====================

    /// <summary>围得成形了吗：面要三个角，航道这类细长一条两个角就够。</summary>
    private int PlotMinCorners => RegionKinds.IsLinear(_plotKind) ? 2 : 3;

    private static string NameOf(MapRegion r) =>
        string.IsNullOrWhiteSpace(r.Name) ? RegionKinds.Display(r.Kind) : $"{r.Name}·{RegionKinds.Display(r.Kind)}";

    /// <summary>这块地多大：面报面积、线报长度，都按地图自己的比例尺（1 单位=几米）换算。</summary>
    private string RegionSizeText(MapRegion r)
    {
        var mpu = _maps.Map.MetersPerUnit;
        var pts = r.Points;
        if (RegionKinds.IsLinear(r.Kind))
        {
            double len = 0;
            for (var i = 0; i + 1 < pts.Count; i++)
                len += Math.Sqrt(Math.Pow(pts[i + 1].X - pts[i].X, 2) + Math.Pow(pts[i + 1].Y - pts[i].Y, 2));
            return $"长约 {len * mpu:F0} 米";
        }
        double sum = 0;
        for (var i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return $"约 {Math.Abs(sum) / 2 * mpu * mpu:F0} 平方米";
    }

    /// <summary>地块的操作菜单：桌面右键、手机点按、地点菜单里的「脚下的地块」都走这一份。</summary>
    private void OpenRegionMenu(MapRegion r, Point world)
    {
        Drawable.SelectedRegionId = r.Id;
        var size = RegionSizeText(r);
        var items = new List<(string Label, string Cmd)>
        {
            ($"这是什么地：{RegionKinds.Display(r.Kind)}", "rkind"),
            ($"名字：{(string.IsNullOrWhiteSpace(r.Name) ? "还没取名" : r.Name)}", "rname"),
            ($"大小：{size}", "info"),
            ("备注", "note"),
            ("在这里新建地点", "newloc"),
            ("新建场景", "newscene"),
            ("删除这块地", "del")
        };
        if (!string.IsNullOrWhiteSpace(r.Note)) items.Insert(3, ($"现有备注：{r.Note}", "note"));
        Drawable.Menu = new ContextMenuState
        {
            Pos = world,
            Title = NameOf(r),
            TargetKind = "region",
            TargetId = r.Id,
            Items = items
        };
    }

    /// <summary>起手圈地：先问是什么、叫什么，第一角就落在右键那一点，之后逐个角点下去。</summary>
    private async Task StartRegionPlotAsync()
    {
        var kinds = new[]
        {
            RegionKinds.Green, RegionKinds.Water, RegionKinds.Route, RegionKinds.Plaza, RegionKinds.District
        };
        var labels = kinds.Select(RegionKinds.Display).ToArray();
        var pick = await Shell.Current.DisplayActionSheet("这块地是什么？", "取消", null, labels);
        var at = Array.IndexOf(labels, pick);
        if (at < 0) return;
        _plotKind = kinds[at];
        var name = await Shell.Current.DisplayPromptAsync($"圈一块{RegionKinds.Display(_plotKind)}",
            "这块地的名字？（可留空）", "开始圈", "取消");
        if (name is null) return;                       // 按了取消：一块地也没圈
        _plotName = name.Trim();
        _plotRing = new List<Point> { _menuWorld };
        SyncRegionDraft();
        StatusText = $"圈一块{RegionKinds.Display(_plotKind)}：第一角已落在右键处——继续点着加角，点回第一个角（或右键）收边";
    }

    /// <summary>落一个角；点回第一个角（12 屏幕像素内）就收边。</summary>
    private void PlotVertex(Point world, PointF screen)
    {
        if (_plotRing is not { } ring) return;
        var head = Drawable.WorldToScreen(ring[0]);
        if (ring.Count >= PlotMinCorners
            && Math.Abs(head.X - screen.X) < 12 && Math.Abs(head.Y - screen.Y) < 12)
        {
            _ = FinishRegionPlotAsync();
            return;
        }
        if (ring.Count >= 1)
        {
            var prev = Drawable.WorldToScreen(ring[^1]);
            if (Math.Abs(prev.X - screen.X) < 4 && Math.Abs(prev.Y - screen.Y) < 4) return;   // 手抖连点：同一个角不记两遍
        }
        ring.Add(world);
        SyncRegionDraft();
        StatusText = ring.Count < PlotMinCorners
            ? $"已落 {ring.Count} 个角——还差一点，围不成一块地"
            : $"已落 {ring.Count} 个角——点回第一个角或右键收边";
        Touch();
    }

    private void SyncRegionDraft()
    {
        Drawable.RegionDraft.Clear();
        if (_plotRing is { } ring) Drawable.RegionDraft.AddRange(ring);
    }

    private void CancelRegionPlot(string why)
    {
        _plotRing = null;
        SyncRegionDraft();
        StatusText = why;
    }

    /// <summary>收边：角点存进地图、落盘，并把这块地选上，接着就能改名改类别。</summary>
    private async Task FinishRegionPlotAsync()
    {
        var ring = _plotRing;
        if (ring is null) return;
        if (ring.Count < PlotMinCorners)
        {
            CancelRegionPlot("角还不够，已取消圈地");
            return;
        }
        _plotRing = null;
        var region = new MapRegion { Kind = _plotKind, Name = _plotName };
        foreach (var p in ring) region.Points.Add(new MapWaypoint { X = p.X, Y = p.Y });
        _maps.Map.Regions.Add(region);
        await _maps.SaveAsync();
        SyncRegionDraft();
        Drawable.SelectedRegionId = region.Id;
        StatusText = $"已圈出{RegionKinds.Display(region.Kind)}：{NameOf(region)}，{RegionSizeText(region)}";
        Refresh();
    }

    private async Task DispatchRegionAsync(string id, string cmd)
    {
        var region = _maps.Map.RegionById(id);
        if (region is null)
        {
            StatusText = "这块地已不在图上";
            return;
        }
        switch (cmd)
        {
            case "newloc":
            case "newscene":
            case "newtransit":
                await DispatchBlankAsync(cmd);          // 地块菜单里也备着新建：别让一块公园挡住正常的建地点流程
                break;
            case "rkind":
            {
                var kinds = new[]
                {
                    RegionKinds.Green, RegionKinds.Water, RegionKinds.Route, RegionKinds.Plaza, RegionKinds.District
                };
                var labels = kinds.Select(k => (k == region.Kind ? "● " : "") + RegionKinds.Display(k)).ToArray();
                var pick = await Shell.Current.DisplayActionSheet("这块地是什么？", "取消", null, labels);
                var at = Array.IndexOf(labels, pick);
                if (at < 0) return;
                region.Kind = kinds[at];
                await _maps.SaveAsync();
                StatusText = $"这块地改为：{RegionKinds.Display(region.Kind)}";
                Refresh();
                break;
            }
            case "rname":
            {
                var name = await Shell.Current.DisplayPromptAsync("这块地的名字", "叫什么？（留空 = 不取名）",
                    "确定", "取消", initialValue: region.Name);
                if (name is null) return;
                region.Name = name.Trim();
                await _maps.SaveAsync();
                StatusText = $"地块名字：{NameOf(region)}";
                Refresh();
                break;
            }
            case "note":
            {
                var note = await Shell.Current.DisplayPromptAsync("地块备注", "这块地有什么特别的？",
                    "确定", "取消", initialValue: region.Note);
                if (note is null) return;
                region.Note = note.Trim();
                await _maps.SaveAsync();
                StatusText = "已存下这块地的备注";
                Refresh();
                break;
            }
            case "info":
                await Shell.Current.DisplayAlert("地块信息",
                    $"{NameOf(region)}\n{RegionSizeText(region)}｜{region.Points.Count} 个角\n备注：{(string.IsNullOrWhiteSpace(region.Note) ? "无" : region.Note)}",
                    "知道了");
                break;
            case "del":
            {
                var ok = await Shell.Current.DisplayAlert("删除地块",
                    $"删掉「{NameOf(region)}」？路和楼都不受影响，只是这块地的底色没了。", "删除", "留着");
                if (!ok) return;
                _maps.Map.Regions.Remove(region);
                Drawable.SelectedRegionId = null;
                await _maps.SaveAsync();
                StatusText = $"已删掉{RegionKinds.Display(region.Kind)}：{region.Name}";
                Refresh();
                break;
            }
        }
    }
}
