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
    // ==================== 画布交互（页面转发，屏幕坐标） ====================

    public void CanvasStart(PointF screen)
    {
        var world = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));

        // 右键菜单打开时：先响应菜单点击（菜单面板为屏幕坐标，直接用屏幕点命中）
        if (Drawable.Menu is not null)
        {
            // 抑制回声：WinUI 右键打开菜单后，同一按压还会触发一次 Touch Start；
            // 若与菜单打开时间过近（<150ms），视为同动作回声，不当作“点菜单”，避免菜单闪关。
            if ((DateTime.Now - _menuOpenedAt).TotalMilliseconds < 150)
            {
                _menuOpenedAt = DateTime.MinValue;
                Touch();
                return;
            }
            var idx = Drawable.HitMenu(new Point(screen.X, screen.Y));
            var items = Drawable.Menu.Items;
            var item = idx >= 0 && idx < items.Count ? items[idx] : default;
            var kind = Drawable.Menu.TargetKind;
            var targetId = Drawable.Menu.TargetId;
            Drawable.Menu = null;
            Touch();
            if (item.Cmd is not null) _ = DispatchAsync(kind, targetId, item.Cmd);
            return;
        }

        // 精准回声闸：右键的同一输入批次（毫秒级）内，Touch Started 不进入任何交互分支。
        // 60ms 窄窗口：吞掉回声的同时不影响真实拖拽（拖拽的按下与右键相隔 >60ms，否则人做不到）。
        if (IsPointerEcho)
        {
            Touch();
            return;
        }

        // 圈地进行中：按住拖动仍然平移看图，纯点击才是落一个角（结算在 CanvasEnd）。
        if (_plotRing is not null)
        {
            _panning = true;
            _panStartScreen = screen;
            _panLastScreen = screen;
            Touch();
            return;
        }

        if (Mode == ModeKind.Doodle)
        {
            if ((Drawable.LinkStartNode is not null || Drawable.LinkStartTransit is not null)
                && (Drawable.HitCard(world) is not null || Drawable.HitTransit(world) is not null))
            {
                FinishDoodle(world);
                return;
            }
            StartDoodleOrFinish(world);
            return;
        }

        if (Mode == ModeKind.Link)
        {
            LinkClick(world);
            return;
        }

        var card = Drawable.HitCard(world);
        if (card is not null)
        {
            if (Mode == ModeKind.View)
            {
                _ = GoToSceneAsync(card);
                return;
            }
            // 编辑模式：选中 + 可拖
            SelectedNode = card;
            Drawable.SelectedEdgeId = null;
            _dragCard = card;
            _dragLastWorld = world;
            StatusText = "";
            Touch();
            return;
        }

        var transit = Drawable.HitTransit(world);
        if (transit is not null)
        {
            if (Mode == ModeKind.View) { StatusText = $"临时节点「{transit.Name}」：仅作路径端点"; return; }
            _dragTransit = transit;
            _dragLastWorld = world;
            Drawable.SelectedEdgeId = null;
            Touch();
            return;
        }

        var edge = Drawable.HitEdge(world);
        if (edge is not null && Mode == ModeKind.Edit)
        {
            Drawable.SelectedEdgeId = edge.Id;
            SelectedNode = null;
            var wp = Drawable.HitWaypoint(edge, world);
            if (wp >= 0)
            {
                _dragEdge = edge;
                _dragWaypoint = wp;
                _dragLastWorld = world;
            }
            Touch();
            return;
        }

        // 空白：取消选择 + 预备（静置=框选；立即拖动=平移）
        SelectedNode = null;
        Drawable.SelectedNode = null;
        Drawable.SelectedEdgeId = null;
        if (Mode == ModeKind.Edit)
        {
            // 地点框拖拽（一环套一环）：命中任意地点框（含空框）→ 整体移动该地点及全部后代
            // 点卡片优先拖卡片；点框内空白 = 整框拖；点框外空白 = 平移/框选
            if (Drawable.HitLocationFrame(world) is { } loc
                && Drawable.LocationFrame(loc) is { } f
                && world.X >= f.X && world.X <= f.X + f.Width && world.Y >= f.Y && world.Y <= f.Y + f.Height)
            {
                _dragLocId = loc.Id;
                _dragLastWorld = world;
                StatusText = "";
                Touch();
                return;
            }
            _marqueeStartWorld = world;
            _marqueeStartScreen = screen;
            Drawable.MarqueeRect = null;
            _marqueePending = true;
            _marqueeMode = false;
            _panning = false;
            var stamp = ++_pressStamp;
            _ = TryEnterMarqueeAsync(stamp);
            Touch();
            return;
        }
        _panning = true;
        _panStartScreen = screen;
        _panLastScreen = screen;
        Touch();
    }

    public void CanvasDrag(PointF screen)
    {
        if (Mode == ModeKind.Doodle)
        {
            // 涂鸦模式：按住拖动时不断追加折点；非按下拖动实际由 Start/End 驱动
            var w = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
            if (Drawable.LinkStartNode is not null || Drawable.LinkStartTransit is not null)
            {
                var last = Drawable.LinkPreview.Count > 0 ? Drawable.LinkPreview[^1] : WorldPointOfStart();
                if (Math.Abs(w.X - last.X) > 6 || Math.Abs(w.Y - last.Y) > 6)
                    Drawable.LinkPreview.Add(w);
            }
            Drawable.Mouse = new Point(screen.X, screen.Y);
            Touch();
            return;
        }
        if (Mode == ModeKind.Link)
        {
            Drawable.Mouse = new Point(screen.X, screen.Y);
            Touch();
            return;
        }
        if (_dragLocId is not null)
        {
            var w = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
            TranslateSubtree(_dragLocId, w.X - _dragLastWorld.X, w.Y - _dragLastWorld.Y);
            _dragLastWorld = w;
            SyncNodesInSubtree(_dragLocId);
            Touch();
            return;
        }
        if (_dragCard is not null)
        {
            var w = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
            _dragCard.X = _dragCard.X + w.X - _dragLastWorld.X;
            _dragCard.Y = _dragCard.Y + w.Y - _dragLastWorld.Y;
            _dragLastWorld = w;
            Touch();
            return;
        }
        if (_dragTransit is not null)
        {
            var w = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
            _dragTransit.X = _dragTransit.X + w.X - _dragLastWorld.X;
            _dragTransit.Y = _dragTransit.Y + w.Y - _dragLastWorld.Y;
            _dragLastWorld = w;
            Touch();
            return;
        }
        if (_dragEdge is not null && _dragWaypoint >= 0)
        {
            var w = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
            var wp = _dragEdge.Waypoints[_dragWaypoint];
            wp.X = w.X;
            wp.Y = w.Y;
            Touch();
            return;
        }
        if (_marqueeMode)
        {
            // 框选模式：更新矩形
            var w = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
            var x0 = Math.Min(_marqueeStartWorld.X, w.X);
            var y0 = Math.Min(_marqueeStartWorld.Y, w.Y);
            Drawable.MarqueeRect = new Microsoft.Maui.Graphics.RectF((float)x0, (float)y0,
                (float)Math.Abs(w.X - _marqueeStartWorld.X), (float)Math.Abs(w.Y - _marqueeStartWorld.Y));
            Touch();
            return;
        }
        if (_marqueePending)
        {
            // 尚未进入框选：移动即取消候补 → 恢复平移
            if (Math.Abs(screen.X - _marqueeStartScreen.X) > 6 || Math.Abs(screen.Y - _marqueeStartScreen.Y) > 6)
            {
                _marqueePending = false;
                _panning = true;
                _panLastScreen = screen;
            }
            else
            {
                Touch();
                return;
            }
        }
        if (_panning)
        {
            Drawable.OffsetX += screen.X - _panLastScreen.X;
            Drawable.OffsetY += screen.Y - _panLastScreen.Y;
            _panLastScreen = screen;
            Touch();
        }
    }

    public void CanvasEnd(PointF screen)
    {
        // 右键回声：同一按压的 Touch End 不当作“纯点击”/结算框选，避免清空已有框选
        if (IsPointerEcho)
        {
            Touch();
            return;
        }
        if (_plotRing is not null)
        {
            var dragged = Math.Abs(screen.X - _panStartScreen.X) > 6 || Math.Abs(screen.Y - _panStartScreen.Y) > 6;
            _panning = false;
            if (dragged) { Touch(); return; }          // 那是挪图，不是画角
            PlotVertex(Drawable.ScreenToWorld(new Point(screen.X, screen.Y)), screen);
            return;
        }
        if (Mode == ModeKind.Doodle)
        {
            var endW = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
            FinishDoodle(endW);
            return;
        }
        // 浏览模式点了一下「街」（按的位置基本没动就松手）：点按 = 街上弹菜单，拖拽 = 平移，互不打扰
        if (Mode == ModeKind.View && _panning
            && Math.Abs(screen.X - _panStartScreen.X) < 6 && Math.Abs(screen.Y - _panStartScreen.Y) < 6)
        {
            _panning = false;
            var world = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
            var tapEdge = Drawable.HitEdge(world);
            if (tapEdge is not null) _ = ShowStreetActionsAsync(tapEdge, world);
            else if (Drawable.HitRegion(world) is { } tapRegion) OpenRegionMenu(tapRegion, world);
            Touch();
            return;
        }
        if (_marqueeMode)
        {
            // 结算框选：命中矩形相交的地点，高亮
            Drawable.MarqueeSelected.Clear();
            if (Drawable.MarqueeRect is { } rect && rect.Width > 1 && rect.Height > 1)
            {
                foreach (var loc in Drawable.Locations ?? new List<MapLocation>())
                    if (Drawable.LocationFrame(loc) is { } f
                        && f.X < rect.Right && f.X + f.Width > rect.Left
                        && f.Y < rect.Bottom && f.Y + f.Height > rect.Top)
                        Drawable.MarqueeSelected.Add(loc.Id);
                StatusText = Drawable.MarqueeSelected.Count == 0
                    ? "框选未命中任何地点"
                    : $"已框选 {Drawable.MarqueeSelected.Count} 个地点，右键可新建场景/自动归属";
            }
            else
            {
                StatusText = "";
            }
            Drawable.MarqueeRect = null;
            _marqueeMode = false;
            _marqueePending = false;
            Touch();
            return;
        }
        if (_marqueePending)
        {
            // 纯点击：仅取消选择（编辑模式下点在地块上算「选中这块地」，好让人看清自己指的是哪一片）
            _marqueePending = false;
            Drawable.MarqueeSelected.Clear();
            if (Mode == ModeKind.Edit)
                Drawable.SelectedRegionId = Drawable.HitRegion(
                    Drawable.ScreenToWorld(new Point(screen.X, screen.Y)))?.Id;
            _audio.PlayAssigned("left");
            Touch();
            return;
        }
        if (_dragCard is not null)
        {
            _dragCard.Scene.X = _dragCard.X;
            _dragCard.Scene.Y = _dragCard.Y;
            _ = _maps.SaveAsync();
        }
        else if (_dragTransit is not null)
        {
            _dragTransit.Transit.X = _dragTransit.X;
            _dragTransit.Transit.Y = _dragTransit.Y;
            _ = _maps.SaveAsync();
        }
        else if (_dragEdge is not null)
        {
            _ = _maps.SaveAsync();
        }
        _dragCard = null;
        _dragTransit = null;
        _dragEdge = null;
        _dragWaypoint = -1;
        if (_dragLocId is not null)
        {
            _ = _maps.SaveAsync();
            _dragLocId = null;
        }
        _panning = false;
        _marqueePending = false;
        _marqueeMode = false;
        if (Mode == ModeKind.Link) Drawable.Mouse = null;
        // 松手这一下也要落一帧：重绘现在是合并到 16ms 后再画的，
        // 终态（连线预览的鼠标点收掉、拖拽后的位置）必须有人叫醒画布，不能停在半路。
        Touch();
    }

    /// <summary>空白按下后静置 0.6s：若仍未移动、未变平移，则进入框选模式。</summary>
    private async Task TryEnterMarqueeAsync(int stamp)
    {
        try
        {
            await Task.Delay(600);
            if (stamp != _pressStamp) return;   // 期间有过新的按下/新模式，作废
            if (Mode == ModeKind.Edit && _marqueePending && !_panning && !_marqueeMode)
            {
                _marqueeMode = true;
                Drawable.MarqueeRect = new Microsoft.Maui.Graphics.RectF(
                    (float)_marqueeStartWorld.X, (float)_marqueeStartWorld.Y, 0, 0);
                StatusText = "框选中：拖动选择地点；松开完成";
                Touch();
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("MapViewModel.TryEnterMarquee -> " + ex);
        }
    }

    /// <summary>滚轮缩放（屏幕点为锚）。</summary>
    public void Zoom(double delta, PointF screen)
    {
        var oldScale = Drawable.Scale;
        var factor = delta > 0 ? 1.15 : 1 / 1.15;
        var newScale = Math.Clamp(oldScale * factor, 0.3, 2.5);
        if (Math.Abs(newScale - oldScale) < 0.001) return;
        // 保持鼠标指向的世界点不动
        var wx = (screen.X - Drawable.OffsetX) / oldScale;
        var wy = (screen.Y - Drawable.OffsetY) / oldScale;
        Drawable.OffsetX = screen.X - wx * newScale;
        Drawable.OffsetY = screen.Y - wy * newScale;
        Drawable.Scale = newScale;
        Touch();
    }

    // ==================== 连线逻辑 ====================

    private void LinkClick(Point world)
    {
        var startNode = Drawable.LinkStartNode;
        var startTransit = Drawable.LinkStartTransit;
        if (startNode is null && startTransit is null)
        {
            var card = Drawable.HitCard(world);
            if (card is not null)
            {
                Drawable.LinkStartNode = card;
                StatusText = $"起点：{card.SceneName} —— 点击目标卡片/临时节点；点空白=加折点；右键=取消";
            }
            else if (Drawable.HitTransit(world) is { } tr)
            {
                Drawable.LinkStartTransit = tr;
                StatusText = $"起点：临时节点「{tr.Name}」 —— 点击目标卡片/临时节点；点空白=加折点；右键=取消";
            }
            else
            {
                StatusText = "请先点击一张卡片或一个临时节点作为起点";
            }
            Touch();
            return;
        }

        var endCard = Drawable.HitCard(world);
        var endTransit = Drawable.HitTransit(world);
        var endId = endCard?.Id ?? endTransit?.Id;
        var startId = startNode?.Id ?? startTransit!.Id;
        if (endId is null)
        {
            // 空白：加点折点
            Drawable.LinkPreview.Add(world);
            StatusText = $"已添加折点（共 {Drawable.LinkPreview.Count} 个），点击目标卡片/节点完成连线";
            Touch();
            return;
        }
        if (endId == startId)
        {
            CancelLink();
            StatusText = "已取消";
            Touch();
            return;
        }

        var existed = _maps.Map.Edges.Any(e =>
            (e.A == startId && e.B == endId) || (e.A == endId && e.B == startId));
        if (existed)
        {
            CancelLink();
            StatusText = "这两个端点已有连线（右键可管理它）";
            Touch();
            return;
        }

        var edge = new MapEdge
        {
            A = startId,
            B = endId,
            Kind = Drawable.LinkCurve ? "curve" : "line"
        };
        foreach (var p in Drawable.LinkPreview) edge.Waypoints.Add(new MapWaypoint { X = p.X, Y = p.Y });
        _maps.Map.Edges.Add(edge);
        _ = _maps.SaveAsync();
        var endName = endCard?.SceneName ?? endTransit!.Name;
        StatusText = $"已连线：{Drawable.LinkStartNode?.SceneName ?? Drawable.LinkStartTransit!.Name} ↔ {endName}"
                     + (edge.Waypoints.Count > 0 ? $"（{edge.Waypoints.Count} 个中继点）" : "");
        CancelLink();
        Refresh();
    }

    // ==================== 涂鸦模式 ====================

    private Point WorldPointOfStart()
    {
        if (Drawable.LinkStartNode is { } n) return new Point(n.X, n.Y);
        if (Drawable.LinkStartTransit is { } t) return new Point(t.X, t.Y);
        return default;
    }

    /// <summary>涂鸦起点：点击卡片/临时节点=吸附；点空白=自动创建临时节点作为端点。</summary>
    private void StartDoodleOrFinish(Point world)
    {
        if (Drawable.LinkStartNode is null && Drawable.LinkStartTransit is null)
        {
            var card = Drawable.HitCard(world);
            if (card is not null)
            {
                Drawable.LinkStartNode = card;
                StatusText = $"涂鸦起点：{card.SceneName} —— 按住拖动画线，松手完成；点空白可再次吸附";
            }
            else if (Drawable.HitTransit(world) is { } tr)
            {
                Drawable.LinkStartTransit = tr;
                StatusText = $"涂鸦起点：临时节点「{tr.Name}」 —— 按住拖动画线，松手完成";
            }
            else
            {
                var transit = _maps.AddTransit(world.X, world.Y);
                Drawable.LinkStartTransit = new MapTransitNode(transit);
                Refresh();
                StatusText = $"已自动创建端点「{transit.Name}」—— 按住拖动涂鸦，松手完成";
            }
            Drawable.LinkPreview.Clear();
            Touch();
            return;
        }

        // 已有点：点在卡片/临时节点上 = 直接完成（吸附终点）
        var endCard = Drawable.HitCard(world);
        var endTransit = Drawable.HitTransit(world);
        if (endCard is not null || endTransit is not null)
            FinishDoodle(world);
        else
            StatusText = "按住拖动涂鸦，或点击卡片/临时节点吸附终点；右键=取消";
        Touch();
    }

    /// <summary>结束涂鸦：自动补齐端点临时节点并生成连线。</summary>
    private void FinishDoodle(Point releaseWorld)
    {
        var startNode = Drawable.LinkStartNode;
        var startTransit = Drawable.LinkStartTransit;
        if (startNode is null && startTransit is null) return;

        // 追加松手点作为末尾折点
        if (Drawable.LinkPreview.Count == 0
            || Math.Abs(releaseWorld.X - Drawable.LinkPreview[^1].X) > 4
            || Math.Abs(releaseWorld.Y - Drawable.LinkPreview[^1].Y) > 4)
            Drawable.LinkPreview.Add(releaseWorld);

        var startId = startNode?.Id ?? startTransit!.Id;
        var pts = Drawable.LinkPreview.ToList();
        // 终点：最后一个折点吸附卡片/临时节点，否则自动创建临时节点
        var endWorld = pts.Count > 0 ? pts[^1] : WorldPointOfStart();
        var endCard = Drawable.HitCard(endWorld);
        var endTransit = Drawable.HitTransit(endWorld);
        string endId;
        string endName;
        if (endCard is not null)
        {
            endId = endCard.Id;
            endName = endCard.SceneName;
        }
        else if (endTransit is not null)
        {
            endId = endTransit.Id;
            endName = endTransit.Name;
        }
        else
        {
            var t = _maps.AddTransit(endWorld.X, endWorld.Y);
            endId = t.Id;
            endName = t.Name;
        }

        if (endId == startId)
        {
            // 空画（未动、也未吸附到目标）：回收自动创建的临时节点
            if (pts.Count == 0 && endCard is null && endTransit is null && startTransit is not null)
                _maps.RemoveTransit(startId);
            CancelLink();
            StatusText = "涂鸦未形成有效的线，已取消";
            Refresh();
            return;
        }
        if (_maps.Map.Edges.Any(e => (e.A == startId && e.B == endId) || (e.A == endId && e.B == startId)))
        {
            CancelLink();
            StatusText = "这两个端点已有连线，请选其他目标";
            Refresh();
            return;
        }

        var edge = new MapEdge { A = startId, B = endId, Kind = "line" };
        // 涂鸦：只显示整条总长一个标记（TotalLabel=true），不做逐段拆分
        // 原始点很密：道格拉斯-普克抽稀只保留明显拐弯处，线条干净
        var simp = SimplifyPoints(pts, 12);
        foreach (var p in simp) edge.Waypoints.Add(new MapWaypoint { X = p.X, Y = p.Y });
        edge.TotalLabel = true;
        _maps.Map.Edges.Add(edge);
        _ = _maps.SaveAsync();
        var startName = startNode?.SceneName ?? startTransit!.Name;
        StatusText = $"涂鸦完成：{startName} ↔ {endName}（总长 {_maps.DisplayLengthMeters(edge):F0} 米）";
        CancelLink();
        Refresh();
    }

    /// <summary>Ramer–Douglas–Peucker 抽稀：保留明显拐弯点，去掉近似直线上的密集采样。</summary>
    private static List<Point> SimplifyPoints(List<Point> pts, double tolerance)
    {
        if (pts.Count <= 2) return pts.ToList();

        double PerpDist(Point a, Point b, Point c)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var l2 = dx * dx + dy * dy;
            if (l2 < 1e-9) return Math.Sqrt(Math.Pow(c.X - a.X, 2) + Math.Pow(c.Y - a.Y, 2));
            var t = ((c.X - a.X) * dx + (c.Y - a.Y) * dy) / l2;
            t = Math.Clamp(t, 0, 1);
            var px = a.X + dx * t;
            var py = a.Y + dy * t;
            return Math.Sqrt(Math.Pow(c.X - px, 2) + Math.Pow(c.Y - py, 2));
        }

        void Rdp(int first, int last, List<bool> keep)
        {
            if (last <= first + 1) return;
            var maxD = 0.0;
            var maxI = first;
            for (var i = first + 1; i < last; i++)
            {
                var d = PerpDist(pts[first], pts[last], pts[i]);
                if (d > maxD) { maxD = d; maxI = i; }
            }
            if (maxD > tolerance)
            {
                keep[maxI] = true;
                Rdp(first, maxI, keep);
                Rdp(maxI, last, keep);
            }
        }

        var keep = new List<bool>(new bool[pts.Count]);
        keep[0] = true;
        keep[^1] = true;
        Rdp(0, pts.Count - 1, keep);
        var out_ = new List<Point>();
        for (var i = 0; i < pts.Count; i++)
            if (keep[i]) out_.Add(pts[i]);
        return out_;
    }
}
