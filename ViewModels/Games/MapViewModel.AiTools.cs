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
    // ============ AI 三钮共用工具 ============

    /// <summary>
    /// 让 AI 看场景背景：解析绝对路径、把「无图 / 未配置 / 空回话」统一 Toast 后返回 null，
    /// 调用方直接收工即可。
    /// </summary>
    private async Task<string?> AiLookAsync(MapScene scene, string prompt, int maxTokens, string busyLabel)
    {
        var abs = _maps.ResolveBackground(scene);
        if (string.IsNullOrWhiteSpace(abs))
        {
            Toast.Info("这个场景还没有背景图，AI 没东西可看。");
            return null;
        }
        if (!_vision.IsConfigured)
        {
            Toast.Info("识图模型还没配置：请到设置里填好「识图端点 / 密钥 / 模型名」三项。");
            return null;
        }
        StatusText = busyLabel;
        var text = await _vision.DescribeImageAsync(abs, prompt, maxTokens);
        if (string.IsNullOrWhiteSpace(text))
        {
            StatusText = "";
            Toast.Info("AI 没回话：识图配置可能不通，或这次请求失败了。");
            return null;
        }
        return text.Trim();
    }

    /// <summary>从 AI 回话里抠出 {"x":…,"y":…}：畸形/越界/null 一律拒绝，合法值钳到 2%..98%。</summary>
    private static (double X, double Y)? ParseJsonXy(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.IndexOf('{');
        var e = text.LastIndexOf('}');
        if (s < 0 || e <= s) return null;
        try
        {
            using var doc = JsonDocument.Parse(text.Substring(s, e - s + 1));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("x", out var xv) || xv.ValueKind != JsonValueKind.Number
                || !root.TryGetProperty("y", out var yv) || yv.ValueKind != JsonValueKind.Number) return null;
            var x = xv.GetDouble();
            var y = yv.GetDouble();
            if (!double.IsFinite(x) || !double.IsFinite(y)) return null;
            return (Math.Clamp(x, 0.02, 0.98), Math.Clamp(y, 0.02, 0.98));
        }
        catch { return null; }
    }

    /// <summary>从 AI 回话里抠出 {"ratio":…}：钳到 PlacementMath 的 0.1~0.9。</summary>
    private static double? ParseJsonRatio(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.IndexOf('{');
        var e = text.LastIndexOf('}');
        if (s < 0 || e <= s) return null;
        try
        {
            using var doc = JsonDocument.Parse(text.Substring(s, e - s + 1));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("ratio", out var rv) || rv.ValueKind != JsonValueKind.Number) return null;
            var r = rv.GetDouble();
            return double.IsFinite(r) ? Math.Clamp(r, 0.1, 0.9) : null;
        }
        catch { return null; }
    }

    /// <summary>Toast 里塞 AI 原话用：压成一行、截到 160 字，避免弹窗变成小说。</summary>
    private static string OneLine(string? s)
    {
        var t = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return t.Length <= 160 ? t : t[..157] + "…";
    }

    private async Task DispatchLocAsync(string locId, string cmd)
    {
        switch (cmd)
        {
            case "underfoot":
            {
                // 大片区（校区/公园）常整片盖在楼框底下：右键点中楼时，也得够得着脚下一块地。
                var under = Drawable.HitRegion(_menuWorld);
                if (under is null)
                {
                    StatusText = "这个点脚下没有地块";
                    return;
                }
                OpenRegionMenu(under, _menuWorld);
                Touch();
                break;
            }
            case "renloc":
            {
                var loc = _maps.Map.Locations.FirstOrDefault(l => l.Id == locId);
                if (loc is null) return;
                var name = await Shell.Current.DisplayPromptAsync("重命名地点", "地点（建筑/区域）的名字？", "确定", "取消",
                    initialValue: loc.Name);
                if (string.IsNullOrWhiteSpace(name)) return;
                loc.Name = name.Trim();
                await _maps.SaveAsync();
                StatusText = $"地点已重命名：{name.Trim()}";
                Refresh();
                break;
            }
            case "newscene":
            {
                var loc = _maps.Map.Locations.FirstOrDefault(l => l.Id == locId);
                if (loc is null) return;
                var sname = await Shell.Current.DisplayPromptAsync("新场景", "此地点下新场景的名字？", "确定", "取消");
                if (string.IsNullOrWhiteSpace(sname)) return;
                var scene = _maps.AddScene(loc.Id, sname.Trim());
                scene.X = _menuWorld.X;
                scene.Y = _menuWorld.Y;
                await _maps.SaveAsync();
                Drawable.MarqueeSelected.Clear();
                StatusText = $"已在「{loc.Name}」中新建场景：{sname.Trim()}";
                Refresh();
                break;
            }
            case "newchild":
            {
                var parent = _maps.Map.Locations.FirstOrDefault(l => l.Id == locId);
                if (parent is null) return;
                var cname = await Shell.Current.DisplayPromptAsync("新建子地点",
                    $"「{parent.Name}」下的子地点名字？", "确定", "取消");
                if (string.IsNullOrWhiteSpace(cname)) return;
                var child = _maps.AddLocation(cname.Trim(), parent.Id);
                // 落点若在父框外，锚点自动收进父框内（一环套一环：子框必须由父框包住）
                var pf = Drawable.LocationFrame(parent);
                if (pf is { } pfv)
                {
                    var cx = _menuWorld.X < pfv.Left || _menuWorld.X > pfv.Right
                        ? pfv.Center.X : _menuWorld.X;
                    var cy = _menuWorld.Y < pfv.Top || _menuWorld.Y > pfv.Bottom
                        ? pfv.Center.Y : _menuWorld.Y;
                    child.X = cx;
                    child.Y = cy;
                }
                else
                {
                    child.X = _menuWorld.X;
                    child.Y = _menuWorld.Y;
                }
                ResolveLocationOverlaps();
                await _maps.SaveAsync();
                StatusText = $"已添加子地点：{cname.Trim()}（在「{parent.Name}」内）——可右键其内部新建场景";
                Refresh();
                break;
            }
            case "floors":
            case "basement":
            {
                var loc = _maps.Map.Locations.FirstOrDefault(l => l.Id == locId);
                if (loc is null) return;
                var up = cmd == "floors";
                var input = await Shell.Current.DisplayPromptAsync(up ? "地上层数" : "地下层数",
                    $"「{loc.Name}」{(up ? "地上有几层？（0/1 = 不分层）" : "地下有几层？（0 = 没有）")}",
                    "确定", "取消",
                    initialValue: (up ? loc.Floors : loc.BasementFloors).ToString(), keyboard: Keyboard.Numeric);
                if (input is null) return;
                if (!int.TryParse(input.Trim(), out var n) || n < 0) { StatusText = "无效的层数"; return; }
                if (up) loc.Floors = n; else loc.BasementFloors = n;
                await _maps.SaveAsync();
                StatusText = $"「{loc.Name}」{(up ? $"地上 {n} 层" : $"地下 {n} 层")}";
                Refresh();
                break;
            }
            case "height":
            {
                var loc = _maps.Map.Locations.FirstOrDefault(l => l.Id == locId);
                if (loc is null) return;
                var input = await Shell.Current.DisplayPromptAsync("层高（米）",
                    $"「{loc.Name}」一层楼按几米算？（3D 视图按它挤楼；默认 3）",
                    "确定", "取消",
                    initialValue: loc.HeightPerFloor.ToString("0.##"), keyboard: Keyboard.Numeric);
                if (input is null) return;
                var norm = input.Trim().Replace(',', '.');
                if (!double.TryParse(norm, NumberStyles.Any, CultureInfo.InvariantCulture, out var h) || h < 0.5 || h > 50)
                {
                    StatusText = "层高得在 0.5 到 50 米之间";
                    return;
                }
                loc.HeightPerFloor = Math.Round(h, 2);
                await _maps.SaveAsync();
                StatusText = $"「{loc.Name}」层高 {loc.HeightPerFloor:0.##} 米";
                Refresh();
                break;
            }
            case "address":
            {
                var loc = _maps.Map.Locations.FirstOrDefault(l => l.Id == locId);
                if (loc is null) return;
                var addr = await Shell.Current.DisplayPromptAsync("地址",
                    $"「{loc.Name}」的门牌地址（留空清除）", "确定", "取消", initialValue: loc.Address);
                if (addr is null) return;
                loc.Address = addr.Trim();
                await _maps.SaveAsync();
                StatusText = string.IsNullOrEmpty(loc.Address) ? "已清除地址" : $"地址：{loc.Address}";
                Refresh();
                break;
            }
            case "delloc":
            {
                var loc = _maps.Map.Locations.FirstOrDefault(l => l.Id == locId);
                if (loc is null) return;
                var subCount = _maps.Map.Locations.Count(l => l.ParentId == loc.Id);
                var ok = await Shell.Current.DisplayAlert("删除地点",
                    $"确定删除「{loc.Name}」吗？其全部场景、连线{(subCount > 0 ? $"及 {subCount} 个子地点" : "")}将一并删除。", "删除", "取消");
                if (!ok) return;
                _maps.RemoveLocation(loc.Id);
                StatusText = $"已删除地点「{loc.Name}」";
                Refresh();
                break;
            }
            case "stats":
                break;
            default:
                break;
        }
    }

    private async Task DispatchBlankAsync(string cmd)
    {
        switch (cmd)
        {
            case "newloc":
            {
                var name = await Shell.Current.DisplayPromptAsync("新建地点", "地点（建筑）的名字？", "确定", "取消");
                if (string.IsNullOrWhiteSpace(name)) return;
                var loc = _maps.AddLocation(name.Trim());
                loc.X = _menuWorld.X;
                loc.Y = _menuWorld.Y;
                await _maps.SaveAsync();
                StatusText = $"已添加地点：{name.Trim()}（可右键空白继续新建场景）";
                Refresh();
                break;
            }
            case "create-map":
                await Shell.Current.GoToAsync("mapcreate");
                break;
            case "newregion":
                await StartRegionPlotAsync();
                break;
            case "newscene":
            {
                var map = _maps.Map;
                if (map.Locations.Count == 0)
                {
                    StatusText = "请先新建一个地点";
                    break;
                }
                MapLocation target;
                // 框选唯一地点优先（右键空白但已框选时）
                var boxed = Drawable.MarqueeSelected
                    .Select(id => map.Locations.FirstOrDefault(l => l.Id == id))
                    .Where(l => l is not null)
                    .Select(l => l!)
                    .ToList();
                if (boxed.Count == 1) target = boxed[0];
                else if (boxed.Count > 1)
                {
                    // 多处框选时也直接弹选择器（与 pickloc 一致）
                    var pick = await Shell.Current.DisplayActionSheet("框选地点中选择场景归属", "取消", null,
                        boxed.Select(l => l.Name).ToArray());
                    if (string.IsNullOrWhiteSpace(pick)) break;
                    target = boxed.FirstOrDefault(l => l.Name == pick) ?? boxed[0];
                }
                else if (map.Locations.Count == 1) target = map.Locations[0];
                else
                {
                    var pick = await Shell.Current.DisplayActionSheet("新场景属于哪个地点？", "取消", null,
                        map.Locations.Select(l => l.Name).ToArray());
                    if (string.IsNullOrWhiteSpace(pick)) break;
                    target = map.Locations.FirstOrDefault(l => l.Name == pick) ?? map.Locations[0];
                }
                var sname = await Shell.Current.DisplayPromptAsync("新场景", "场景的名字？", "确定", "取消");
                if (string.IsNullOrWhiteSpace(sname)) return;
                var scene = _maps.AddScene(target.Id, sname.Trim());
                scene.X = _menuWorld.X;
                scene.Y = _menuWorld.Y;
                await _maps.SaveAsync();
                StatusText = $"已添加场景：{sname.Trim()}（拖拽可摆放位置），右键可设背景图";
                Refresh();
                break;
            }
                        case "pickloc":
            {
                // 多选框选：在框选地点中选一个归属，然后新建场景
                var picked = Drawable.MarqueeSelected
                    .Select(id => _maps.Map.Locations.FirstOrDefault(l => l.Id == id))
                    .Where(l => l is not null)
                    .ToList();
                if (picked.Count == 0) { StatusText = "框选地点已失效，请重新框选"; break; }
                MapLocation target;
                if (picked.Count == 1) target = picked[0]!;
                else
                {
                    var pick = await Shell.Current.DisplayActionSheet("框选地点中选择场景归属", "取消", null,
                        picked.Select(l => l!.Name).ToArray());
                    if (string.IsNullOrWhiteSpace(pick)) break;
                    target = picked.FirstOrDefault(l => l!.Name == pick) ?? picked[0]!;
                }
                var sname = await Shell.Current.DisplayPromptAsync("新场景", $"「{target.Name}」下场景的名字？", "确定", "取消");
                if (string.IsNullOrWhiteSpace(sname)) return;
                var scene = _maps.AddScene(target.Id, sname.Trim());
                scene.X = _menuWorld.X;
                scene.Y = _menuWorld.Y;
                await _maps.SaveAsync();
                Drawable.MarqueeSelected.Clear();
                StatusText = $"已添加场景：{sname.Trim()}（归属「{target.Name}」）";
                Refresh();
                break;
            }
            case "newtransit":
            {
                var transit = _maps.AddTransit(_menuWorld.X, _menuWorld.Y);
                await _maps.SaveAsync();
                StatusText = $"已添加临时节点「{transit.Name}」—— 连线模式中可作为端点";
                Refresh();
                break;
            }
            case "nestnew":
            case "nestinto":
            {
                await NestSelectedLocationsAsync(cmd == "nestnew");
                break;
            }
            default:
                break;
        }
    }

    /// <summary>批量嵌套：把当前框选的地点整体归入新建（或已有）父地点。</summary>
    private async Task NestSelectedLocationsAsync(bool createParent)
    {
        var map = _maps.Map;
        var selected = Drawable.MarqueeSelected
            .Select(id => map.Locations.FirstOrDefault(l => l.Id == id))
            .Where(l => l is not null)
            .Select(l => l!)
            .ToList();
        if (selected.Count == 0)
        {
            StatusText = "框选地点已失效，请重新框选";
            return;
        }

        // 目标父地点：新建 or 从已有地点中挑选（排除被框选者及其父链，避免循环嵌套）
        MapLocation? parent;
        if (createParent)
        {
            var pname = await Shell.Current.DisplayPromptAsync("新建父地点", "用来收纳这批地点的父地点名字？", "确定", "取消",
                initialValue: $"{selected[0].Name}群");
            if (string.IsNullOrWhiteSpace(pname)) return;
            parent = _maps.AddLocation(pname.Trim());
            parent.X = _menuWorld.X;
            parent.Y = _menuWorld.Y;
        }
        else
        {
            var selectedIds = selected.Select(l => l.Id).ToHashSet();
            var candidates = map.Locations.Where(l => !selectedIds.Contains(l.Id)).ToList();
            if (candidates.Count == 0)
            {
                StatusText = "没有可归入的已有地点（请先新建地点，或框选其它地点）";
                return;
            }
            var pick = await Shell.Current.DisplayActionSheet("归入哪个已有地点？", "取消", null,
                candidates.Select(l => l.Name).ToArray());
            // 「取消」返回的是按钮文本；按名字找不到就当没选，不能退回 candidates[0]，
            // 否则点取消等于把这一片地点塞进列表里的第一个地点。
            if (string.IsNullOrWhiteSpace(pick) || pick == "取消") return;
            parent = candidates.FirstOrDefault(l => l.Name == pick);
        }
        if (parent is null) return;

        var selfChain = new HashSet<string>();
        for (var p = parent; p is not null; p = map.Locations.FirstOrDefault(l => l.Id == p.ParentId))
            selfChain.Add(p.Id);

        // 记下每个子地点子树当前的世界包围盒（含其全部后代卡片）
        var placements = new List<(MapLocation Loc, RectF Box)>();
        foreach (var loc in selected)
        {
            if (selfChain.Contains(loc.Id)) continue; // 跳过父链上的自己/祖先
            loc.ParentId = parent.Id;
            if (Drawable.LocationFrame(loc) is { } f) placements.Add((loc, f));
        }

        // —— 旧方案（网格平移）已废弃，被下方 ResolveLocationOverlaps 的碰撞推开取代 ——
        // 子地点仍停原位，相交/过近的地方由碰撞检测统一推开，保证全局不重合。

await _maps.SaveAsync();
        Drawable.MarqueeSelected.Clear();
        // 碰撞检测推开：本批归入的新子框若与其它框重叠，整体让开
        ResolveLocationOverlaps();
        await _maps.SaveAsync();
        StatusText = $"已把 {placements.Count} 个地点归入「{parent.Name}」——重叠自动推开";
        Refresh();
    }

    /// <summary>是否 self 是 target 的自身或子孙（用于跳过父子/祖孙重叠判定）。</summary>
    private bool IsSelfOrDescendant(string self, string target)
    {
        var map = _maps.Map;
        for (var cur = target; !string.IsNullOrEmpty(cur); cur = map.Locations.FirstOrDefault(l => l.Id == cur)?.ParentId ?? "")
            if (cur == self) return true;
        return false;
    }

    /// <summary>全局地点框碰撞检测：任何非父子关系的地点框相交或间距过小时，
    /// 沿移动量较小的轴把后者（含其子树全部卡片）推开；迭代至无重叠或达到上限。
    /// 全程在模型层计算框（读 MapScene.X/Y 真实坐标），每轮基于最新位置重算，铺开后可收敛。</summary>
    private void ResolveLocationOverlaps()
    {
        var map = _maps.Map;
        if (map.Locations.Count < 2) return;
        const float minGap = 44;          // 框间最小间距（世界坐标）
        const int maxPass = 40;           // 每轮重算所有框，最多迭代次数

        for (var pass = 0; pass < maxPass; pass++)
        {
            var moved = false;
            // —— 关键：每轮都从模型重算框（TranslateSubtree 已改模型坐标，下一轮必然拿到新位置）——
            var frames = map.Locations
                .Select(l => (Loc: l, R: ModelBox(l)))
                .ToList();
            for (var i = 0; i < frames.Count && !moved; i++)
            {
                for (var j = i + 1; j < frames.Count && !moved; j++)
                {
                    var (a, ra) = frames[i];
                    var (b, rb) = frames[j];
                    if (IsSelfOrDescendant(a.Id, b.Id) || IsSelfOrDescendant(b.Id, a.Id)) continue; // 父子天然嵌套
                    // 重叠/过近的横纵方向所需推开量
                    var dx = Math.Min(ra.Right, rb.Right) - Math.Max(ra.Left, rb.Left) + minGap;
                    var dy = Math.Min(ra.Bottom, rb.Bottom) - Math.Max(ra.Top, rb.Top) + minGap;
                    if (dx > 0 && dy > 0)
                    {
                        // 选最便宜的方向推开 b：水平往远离 a 中心的方向，垂直同理
                        var pushRight = rb.Center.X >= ra.Center.X;
                        var pushDown = rb.Center.Y >= ra.Center.Y;
                        if (dx <= dy) TranslateSubtree(b.Id, pushRight ? dx : -dx, 0);
                        else TranslateSubtree(b.Id, 0, pushDown ? dy : -dy);
                        moved = true;
                    }
                }
            }
            if (!moved) break;
        }
    }

    /// <summary>模型层地点框（世界坐标，含留白）：递归聚合该地点及其全部后代地点的场景卡片（含空后代锚点框）。
    /// 与 Drawable.LocationFrame 保持一致——父框必须几何包住空子框（一环套一环）。</summary>
    private Microsoft.Maui.Graphics.RectF ModelBox(MapLocation loc)
    {
        var map = _maps.Map;
        // 该地点 + 全部后代地点 id
        var ids = new List<string> { loc.Id };
        bool added;
        do
        {
            added = false;
            foreach (var l in map.Locations)
                if (!string.IsNullOrEmpty(l.ParentId) && !ids.Contains(l.Id) && ids.Contains(l.ParentId))
                {
                    ids.Add(l.Id);
                    added = true;
                }
        } while (added);

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var found = false;
        foreach (var l in map.Locations)
        {
            if (!ids.Contains(l.Id)) continue;
            foreach (var s in l.Scenes)
            {
                if (s.X < 0 || s.Y < 0) continue; // 未摆放的卡片不参与
                minX = Math.Min(minX, s.X);
                minY = Math.Min(minY, s.Y);
                maxX = Math.Max(maxX, s.X + MapCanvasDrawable.CardW);
                maxY = Math.Max(maxY, s.Y + MapCanvasDrawable.CardH);
                found = true;
            }
        }
        // 空后代（无卡片）也要计入父框包围盒（与绘制一致）
        const double emptyW = 230, emptyH = 100;
        foreach (var l in map.Locations)
        {
            if (!ids.Contains(l.Id)) continue;
            var hasCard = l.Scenes.Count > 0;
            if (hasCard) continue;
            var a = new Point(l.X < 0 ? 0 : l.X, l.Y < 0 ? 0 : l.Y);
            minX = Math.Min(minX, a.X - emptyW / 2);
            minY = Math.Min(minY, a.Y - emptyH / 2);
            maxX = Math.Max(maxX, a.X + emptyW / 2);
            maxY = Math.Max(maxY, a.Y + emptyH / 2);
            found = true;
        }
        // 完全空的地点：以锚点画最小框
        if (!found)
        {
            var anchor = new Point(loc.X < 0 ? 0 : loc.X, loc.Y < 0 ? 0 : loc.Y);
            return new Microsoft.Maui.Graphics.RectF((float)(anchor.X - emptyW / 2), (float)(anchor.Y - emptyH / 2),
                (float)emptyW, (float)emptyH);
        }
        // 与 Drawable.LocationFrame 保持一致：父框顶部多留标题带，避免父子框上沿重合
        const double pad = 56, title = 44;
        var extraHead = ids.Count > 1 ? title + pad : 0;
        return new Microsoft.Maui.Graphics.RectF((float)(minX - pad), (float)(minY - pad - title - extraHead),
            (float)(maxX - minX + pad * 2), (float)(maxY - minY + pad * 2 + title + extraHead));
    }

    /// <summary>地点子树（含全部后代地点）中的卡片同步到画布 node 副本（拖拽整框时实时跟随）。</summary>
    private void SyncNodesInSubtree(string rootId)
    {
        var ids = new HashSet<string> { rootId };
        var map = _maps.Map;
        bool added;
        do
        {
            added = false;
            foreach (var l in map.Locations)
                if (!string.IsNullOrEmpty(l.ParentId) && !ids.Contains(l.Id) && ids.Contains(l.ParentId))
                {
                    ids.Add(l.Id);
                    added = true;
                }
        } while (added);
        foreach (var n in Drawable.Nodes ?? new List<MapCanvasNode>())
            if (ids.Contains(n.Location.Id))
            {
                n.X = n.Scene.X;
                n.Y = n.Scene.Y;
            }
    }

    /// <summary>把某地点及其全部后代地点（含所有卡片位置、锚点）整体平移 dx,dy。</summary>
    private void TranslateSubtree(string rootId, double dx, double dy)
    {
        var map = _maps.Map;
        // 找子树全部地点 id（含自身）
        var ids = new List<string> { rootId };
        bool added;
        do
        {
            added = false;
            foreach (var l in map.Locations)
                if (!string.IsNullOrEmpty(l.ParentId) && !ids.Contains(l.Id) && ids.Contains(l.ParentId))
                {
                    ids.Add(l.Id);
                    added = true;
                }
        } while (added);

        // 平移地点锚点（空地点以锚点定位）
        foreach (var l in map.Locations)
            if (ids.Contains(l.Id))
            {
                l.X += dx;
                l.Y += dy;
                // 地点下所有场景卡片跟随平移
                foreach (var s in l.Scenes)
                {
                    s.X += dx;
                    s.Y += dy;
                }
            }
    }

    /// <summary>计算连线当前应显示的长度文本（用于详情）。</summary>
    private string LengthText(MapEdge e)
    {
        var m = _maps.DisplayLengthMeters(e);
        return m <= 0 ? "虚线（未丈量）" : $"~{(int)m} 米";
    }

    /// <summary>走一遍这条路要多久（按 长度 ÷ 限速；户型内/未丈量这类不计时）。</summary>
    private string RoadTimeText(MapEdge e)
    {
        var sec = _maps.TraversalSeconds(e);
        if (sec <= 0) return "不计时";
        return sec < 90 ? $"{sec:F0} 秒" : $"{sec / 60:F1} 分钟";
    }

    private async Task PickSceneBackgroundAsync(MapScene scene)
    {
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = $"为「{scene?.Name}」选择背景图/视频",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".mp4", ".mov", ".m4v", ".webm" } },
                    { DevicePlatform.Android, new[] { "image/*", "video/*" } },
                    { DevicePlatform.iOS, new[] { "public.image", "public.movie" } }
                })
            });
            if (pick is null || scene is null) return;

            // 换图 = 旧标定作废，随后必须重新标定；用户中途放弃则整张图回滚，
            // 不留下"换了图但没标定"的半吊子状态。
            var oldBg = scene.Background;
            var oldPlacement = scene.Placement;
            var oldFree = scene.FreePlacement;
            var rel = _maps.ImportBackgroundForScene(pick.FullPath, scene);
            if (rel is null) { StatusText = "背景复制失败"; return; }
            scene.Background = rel;
            await _maps.SaveAsync();

            if (!await FinalizeBackgroundAsync(scene, "背景图"))
            {
                scene.Background = oldBg;
                scene.Placement = oldPlacement;
                scene.FreePlacement = oldFree;
                await SaveAfterRollbackAsync(scene);
                return;
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("MapViewModel.PickBackground -> " + ex);
        }
    }

    /// <summary>从素材库（背景库）选择背景应用到场景：直接复制素材文件进 maps/ 目录。</summary>
    private async Task PickBackgroundFromLibraryAsync(MapScene scene)
    {
        try
        {
            if (scene is null) return;
            await _materials.EnsureLoadedAsync();
            var items = _materials.BackgroundItems;
            if (items.Count == 0)
            {
                Toast.Info("背景素材库是空的，可先到「素材库」页导入。");
                return;
            }
            var picked = await Shell.Current.DisplayActionSheet($"为「{scene.Name}」选择背景",
                "取消", null, items.Select(b => b.Name).ToArray());
            if (picked is null || picked == "取消") return;
            var item = items.FirstOrDefault(b => b.Name == picked);
            if (item is null) return;
            var abs = _materials.ResolveAbs(item.RelPath);
            if (!File.Exists(abs)) { StatusText = "背景文件缺失"; return; }
            var oldBg = scene.Background;
            var oldPlacement = scene.Placement;
            var oldFree = scene.FreePlacement;
            var rel = _maps.ImportBackgroundForScene(abs, scene);
            if (rel is null) { StatusText = "背景复制失败"; return; }
            scene.Background = rel;
            await _maps.SaveAsync();

            if (!await FinalizeBackgroundAsync(scene, $"素材库背景「{item.Name}」"))
            {
                scene.Background = oldBg;
                scene.Placement = oldPlacement;
                scene.FreePlacement = oldFree;
                await SaveAfterRollbackAsync(scene);
                return;
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("MapViewModel.PickBgFromLibrary -> " + ex);
        }
    }

    // ============ 背景落位的收尾：规定站位 / 不规定直接铺 ============

    /// <summary>
    /// 导入背景后问一句「规定不规定」：规定 = 走标定闸（老流程，不定时回滚）；不规定 = 先落
    /// <see cref="MapScene.FreePlacement"/> 直接铺，人物默认落脚。返回 false = 用户取消，调方回滚。
    /// </summary>
    private async Task<bool> FinalizeBackgroundAsync(MapScene scene, string sourceLabel)
    {
        scene.FreePlacement = false;   // 档位以本轮为准：先按老流程，选了不规定再翻
        var pick = await Shell.Current.DisplayActionSheet("这间屋要不要规定站位？", null, null,
            "规定角色（走标定）", "不规定（直接铺 · 自由站位）");
        var free = pick is "不规定（直接铺 · 自由站位）";
        scene.FreePlacement = free;

        if (free)
        {
            await _maps.SaveAsync();
            StatusText = $"已铺「{scene.Name}」{sourceLabel}：不规定站位，人物按默认落脚。右键「站位」可切换。";
            return true;
        }

        if (await _cal.EnsureCalibratedAsync(scene, SceneCalibrationService.DefaultTargetAspect))
        {
            await _maps.SaveAsync();
            StatusText = $"已应用「{scene.Name}」{sourceLabel}并完成标定（规定站位）。";
            return true;
        }
        StatusText = "已取消，背景未更换";
        return false;
    }

    /// <summary>取消导入时的回滚收盘。</summary>
    private async Task SaveAfterRollbackAsync(MapScene scene)
    {
        await _maps.SaveAsync();
    }
}
