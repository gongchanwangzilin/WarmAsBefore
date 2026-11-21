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
    // ==================== 菜单命令 ====================

    private async Task DispatchAsync(string kind, string targetId, string cmd)
    {
        try
        {
            switch (kind)
            {
                case "scene":
                    await DispatchSceneAsync(targetId, cmd);
                    break;
                case "transit":
                    await DispatchTransitAsync(targetId, cmd);
                    break;
                case "edge":
                    await DispatchEdgeAsync(targetId, cmd);
                    break;
                case "loc":
                    await DispatchLocAsync(targetId, cmd);
                    break;
                case "region":
                    await DispatchRegionAsync(targetId, cmd);
                    break;
                default:
                    await DispatchBlankAsync(cmd);
                    break;
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("MapViewModel.Dispatch -> " + ex);
        }
    }

    private async Task DispatchSceneAsync(string id, string cmd)
    {
        var scene = _maps.Map.SceneById(id);
        switch (cmd)
        {
            case "go":
            {
                if (scene is null) return;
                var note = await _maps.MoveToAsync(scene.Id);
                if (!string.IsNullOrWhiteSpace(note)) StatusText = note.Trim('（', '）');
                Refresh();
                break;
            }
            case "rename":
            {
                if (scene is null) return;
                var name = await Shell.Current.DisplayPromptAsync("编辑名称", "新的场景名称？", "确定", "取消",
                    initialValue: scene.Name);
                if (string.IsNullOrWhiteSpace(name)) return;
                scene.Name = name.Trim();
                await _maps.SaveAsync();
                StatusText = $"已重命名：{name.Trim()}";
                Refresh();
                break;
            }
            case "bg":
                await PickSceneBackgroundAsync(scene);
                Refresh();
                break;
            case "bg-lib":
                await PickBackgroundFromLibraryAsync(scene);
                Refresh();
                break;
            case "cal":
            {
                if (scene is null) return;
                if (_maps.ResolveBackground(scene) is null)
                {
                    Toast.Info("这个场景还没有背景图，先选一张再来标定。");
                    return;
                }
                // 已有标定的场景也能重标：改一个不满意的落脚点，不必重新导入背景
                var ok = await _cal.EnsureCalibratedAsync(scene,
                    SceneCalibrationService.DefaultTargetAspect, force: true);
                StatusText = ok ? $"已完成「{scene.Name}」的背景标定" : "已取消标定";
                Refresh();
                break;
            }
            case "stand":
            {
                if (scene is null) return;
                scene.FreePlacement = !scene.FreePlacement;
                await _maps.SaveAsync();
                if (scene.FreePlacement)
                {
                    StatusText = $"「{scene.Name}」改为不规定站位：背景直接铺，人物按默认落脚。";
                }
                else
                {
                    var calibrated = PlacementMath.Validate(scene.Placement).Ok;
                    StatusText = calibrated
                        ? $"「{scene.Name}」改回规定站位（用已有标定）。"
                        : $"「{scene.Name}」改回规定站位：还没标定，进入时会先请你去标。";
                }
                Refresh();
                break;
            }
            case "bg-clear":
                if (scene is not null)
                {
                    scene.Background = "";
                    await _maps.SaveAsync();
                    StatusText = $"已清除「{scene.Name}」背景";
                    Refresh();
                }
                break;
            case "note":
                if (scene is null) return;
                var note2 = await Shell.Current.DisplayPromptAsync("备注", "为这个场景添加备注（可留空清除）", "确定", "取消",
                    initialValue: scene.Note);
                if (note2 is null) return;
                scene.Note = note2.Trim();
                await _maps.SaveAsync();
                StatusText = string.IsNullOrWhiteSpace(scene.Note) ? "已清除备注" : "已保存备注";
                Refresh();
                break;
            case "ai-note":     // AI 出简介：看图 → 预填 → 确认写「备注」
            {
                if (scene is null) return;
                var desc = await AiLookAsync(scene,
                    "这是一张游戏场景的背景图。用中文写 1~3 句让玩家有画面感的场景简介（会直接当这条场景的备注用）："
                    + "说清这是什么地方、光线/时间、一两个突出的细节。不要寒暄、不要 Markdown、不要列条目，直接给正文。",
                    260, "AI 正在看图…");
                if (desc is null) return;   // AiLookAsync 里已 Toast 兜底
                var note3 = await Shell.Current.DisplayPromptAsync("AI 出简介",
                    "AI 看图写好了，可改可弃（「写入」后存进备注）：", "写入", "放弃",
                    initialValue: desc, maxLength: 400);
                if (note3 is null) { StatusText = ""; return; }
                scene.Note = note3.Trim();
                await _maps.SaveAsync();
                StatusText = string.IsNullOrWhiteSpace(scene.Note) ? "已清空备注" : "AI 简介已写入备注";
                Refresh();
                break;
            }
            case "ai-anchor":   // AI 选站位：合法「点选」标定上挪锚点
            {
                if (scene is null) return;
                if (scene.Placement is not { } pl
                    || pl.Landing != LandingKinds.Point
                    || !PlacementMath.Validate(pl).Ok)
                {
                    Toast.Info("AI 选站位要在已完成的「点选」标定上挪锚点：先用 标定这张背景（缩放标定是前提）。");
                    return;
                }
                var raw = await AiLookAsync(scene,
                    "这是一张游戏场景的背景图，图里将要站一个真人（比例真实）。"
                    + "请在图内选一个真实可站立的落脚位置：地面/地板上，不遮门窗、不在桌面或楼顶。"
                    + "用归一化坐标表达：x 从左到右 0~1，y 从上到下 0~1（站得越靠画面下方，y 越大）。"
                    + "只输出一个 JSON：{\"x\":0.5,\"y\":0.75}，不要任何别的文字。", 120, "AI 正在选站位…");
                if (raw is null) return;
                var spot = ParseJsonXy(raw);
                if (spot is null)
                {
                    StatusText = "";
                    Toast.Info("AI 给的不是可解析的坐标 JSON：" + OneLine(raw));
                    return;
                }
                var ok = await Shell.Current.DisplayAlert("AI 选站位",
                    $"AI 建议把落脚点挪到 ({spot.Value.X:0.00}, {spot.Value.Y:0.00})（图内归一化坐标，左上角为原点）。\n\n"
                    + "挪动只改站位锚点，缩放标定保持不变。要挪过去吗？",
                    "挪过去", "算了");
                if (!ok) { StatusText = ""; return; }
                var old = pl.Points[0];
                pl.Points[0] = new NormPoint(spot.Value.X, spot.Value.Y);
                var (anchorOk, anchorMissing) = PlacementMath.Validate(pl);
                if (!anchorOk)
                {
                    pl.Points[0] = old;   // 不过闸就还原，不留半吊子状态
                    StatusText = "";
                    Toast.Info("AI 给的位置没过标定校验：" + string.Join("；", anchorMissing.Take(2)));
                    return;
                }
                await _maps.SaveAsync();
                StatusText = $"AI 已把「{scene.Name}」的锚点挪到 ({spot.Value.X:0.00}, {spot.Value.Y:0.00})";
                Refresh();
                break;
            }
            case "ai-ratio":    // AI 画比例：改 RefHeightRatio + 重过校验
            {
                if (scene is null) return;
                if (scene.Placement is not { } pl2 || !PlacementMath.Validate(pl2).Ok)
                {
                    Toast.Info("AI 画比例要在已完成标定的场景上改：先用 标定这张背景。");
                    return;
                }
                var raw2 = await AiLookAsync(scene,
                    "这是一张游戏场景的背景图。若让一个真人（比如主角）站到图里合理可站立的地面上（考虑透视深度），"
                    + "他/她的身高大约占整张图片高度的比例是多少？ratio = 身高 ÷ 图片高度，取值 0.1~0.9："
                    + "远景路面上约 0.1~0.25，中景房间约 0.3~0.55，凑近镜头最高 0.9。"
                    + "只输出一个 JSON：{\"ratio\":0.4}，不要任何别的文字。", 120, "AI 正在估比例…");
                if (raw2 is null) return;
                var ratio = ParseJsonRatio(raw2);
                if (ratio is null)
                {
                    StatusText = "";
                    Toast.Info("AI 给的不是可解析的比例 JSON：" + OneLine(raw2));
                    return;
                }
                var okRatio = await Shell.Current.DisplayAlert("AI 画比例",
                    $"AI 估出人物身高约占图高 {ratio:0.00}（参考身高占比 0.1~0.9）。\n\n"
                    + "写回后人物的整体缩放会随之变化。要用这个比例吗？",
                    "用这个", "算了");
                if (!okRatio) { StatusText = ""; return; }
                var oldRatio = pl2.RefHeightRatio;
                pl2.RefHeightRatio = ratio.Value;
                var (ratioOk, ratioMissing) = PlacementMath.Validate(pl2);
                if (!ratioOk)
                {
                    pl2.RefHeightRatio = oldRatio;
                    StatusText = "";
                    Toast.Info("AI 给的比例没过标定校验：" + string.Join("；", ratioMissing.Take(2)));
                    return;
                }
                await _maps.SaveAsync();
                StatusText = $"AI 已把「{scene.Name}」的参考身高占比改为 {ratio:0.00}";
                Refresh();
                break;
            }
            case "floor":
            {
                if (scene is null) return;
                var locId = _maps.Map.LocationIdOfScene(scene.Id);
                var cand = _maps.Map.FloorsOf(locId).Where(f => f is not null).ToList();
                if (scene.Floor is { } cur && !cand.Contains(cur)) cand.Add(cur);
                if (cand.Count == 0)
                {
                    Toast.Info("这栋楼还没设层数：先右键建筑框，填「地上/地下层数」。");
                    return;
                }
                var labels = cand.Select(f => MapDefinition.FloorLabel(f)).Append("不分层（清空）").ToArray();
                var pick = await Shell.Current.DisplayActionSheet($"「{scene.Name}」在哪一层？", "取消", null, labels);
                if (string.IsNullOrWhiteSpace(pick) || pick == "取消") return;
                var at = Array.IndexOf(labels, pick);
                scene.Floor = at >= 0 && at < cand.Count ? cand[at] : null;
                await _maps.SaveAsync();
                StatusText = $"「{scene.Name}」{(scene.Floor is null ? "不再分层" : $"在 {MapDefinition.FloorLabel(scene.Floor)}")}";
                Refresh();
                break;
            }
            case "unit":
            {
                if (scene is null) return;
                var unit = await Shell.Current.DisplayPromptAsync("户型名",
                    "这一户/这一间的名字（留空 = 不分户）", "确定", "取消", initialValue: scene.Unit);
                if (unit is null) return;
                scene.Unit = unit.Trim();
                await _maps.SaveAsync();
                StatusText = string.IsNullOrEmpty(scene.Unit) ? "已清除户型名" : $"户型：{scene.Unit}";
                Refresh();
                break;
            }
            case "indoor":
            {
                if (scene is null) return;
                scene.Indoor = !scene.Indoor;
                await _maps.SaveAsync();
                StatusText = $"「{scene.Name}」改为{(scene.Indoor ? "室内" : "室外")}";
                Refresh();
                break;
            }
            case "info":
                if (scene is null) return;
                await Shell.Current.DisplayAlert("场景信息",
                    $"场景：{scene.Name}\n地点：{_maps.Map.LocationNameOf(scene.Id)}\n背景：{(string.IsNullOrWhiteSpace(scene.Background) ? "无" : "✓")}\n" +
                    $"站位：{(scene.FreePlacement ? "不规定（自由铺 · 默认屏底中央）" : PlacementMath.Validate(scene.Placement).Ok ? "规定（已标定）" : "规定（待标定）")}\n" +
                    $"楼层：{(scene.Floor is null ? "不分层" : MapDefinition.FloorLabel(scene.Floor))} · {(scene.Indoor ? "室内" : "室外")}" +
                    $"{(string.IsNullOrWhiteSpace(scene.Unit) ? "" : $" · 户型 {scene.Unit}")}\n备注：{(string.IsNullOrWhiteSpace(scene.Note) ? "无" : scene.Note)}",
                    "知道了");
                break;
            case "del":
            {
                if (scene is null) return;
                var ok = await Shell.Current.DisplayAlert("删除场景",
                    $"确定删除「{scene.Name}」吗？其连线也会一并删除。", "删除", "取消");
                if (!ok) return;
                _maps.RemoveScene(scene.Id);
                StatusText = "已删除场景";
                Refresh();
                break;
            }
        }
    }

    private async Task DispatchTransitAsync(string id, string cmd)
    {
        var transit = _maps.Map.TransitById(id);
        switch (cmd)
        {
            case "edit":
            {
                if (transit is null) return;
                var name = await Shell.Current.DisplayPromptAsync("编辑名称", "临时节点的名称？", "确定", "取消",
                    initialValue: transit.Name);
                if (string.IsNullOrWhiteSpace(name)) return;
                transit.Name = name.Trim();
                await _maps.SaveAsync();
                StatusText = $"已重命名：{name.Trim()}";
                Refresh();
                break;
            }
            case "junction":
            {
                if (transit is null) return;
                var kinds = new[]
                {
                    JunctionKinds.Crossing, JunctionKinds.Junction, JunctionKinds.Roundabout,
                    JunctionKinds.Toll, JunctionKinds.Stop, JunctionKinds.Poi
                };
                var labels = kinds
                    .Select(k => (k == transit.Junction ? "● " : "") + JunctionKinds.Display(k))
                    .Append("普通端点（清空）").ToArray();
                var pick = await Shell.Current.DisplayActionSheet($"「{transit.Name}」是什么路口？", "取消", null, labels);
                if (string.IsNullOrWhiteSpace(pick) || pick == "取消") return;
                var at = Array.IndexOf(labels, pick);
                transit.Junction = at >= 0 && at < kinds.Length ? kinds[at] : "";
                await _maps.SaveAsync();
                StatusText = $"路口改为：{JunctionKinds.Display(transit.Junction)}";
                Refresh();
                break;
            }
            case "note":
                if (transit is null) return;
                var note = await Shell.Current.DisplayPromptAsync("编辑备注", "备注内容（可留空清除）", "确定", "取消",
                    initialValue: transit.Note);
                if (note is null) return;
                transit.Note = note.Trim();
                await _maps.SaveAsync();
                Refresh();
                break;
            case "info":
                if (transit is null) return;
                await Shell.Current.DisplayAlert("临时节点信息",
                    $"名称：{transit.Name}\n路口：{JunctionKinds.Display(transit.Junction)}\n" +
                    $"坐标：({transit.X:F0}, {transit.Y:F0})\n备注：{(string.IsNullOrWhiteSpace(transit.Note) ? "无" : transit.Note)}",
                    "知道了");
                break;
            case "del":
            {
                if (transit is null) return;
                var ok = await Shell.Current.DisplayAlert("删除临时节点",
                    $"确定删除「{transit.Name}」吗？其连线也会一并删除。", "删除", "取消");
                if (!ok) return;
                _maps.RemoveTransit(transit.Id);
                StatusText = "已删除临时节点";
                Refresh();
                break;
            }
        }
    }

    private async Task DispatchEdgeAsync(string id, string cmd)
    {
        var edge = _maps.Map.Edges.FirstOrDefault(e => e.Id == id);
        switch (cmd)
        {
            case "enter3d":
                if (edge is null) return;
                await Enter3dFromStreetAsync(edge, _menuWorld);
                break;
            case "length":
            {
                if (edge is null) return;
                var current = edge.Length < 0 ? "" : edge.Length.ToString("F0");
                var input = await Shell.Current.DisplayPromptAsync("编辑长度（米）",
                    "填入长度（米）；留空=按路径自动计算；填 0=虚线（不计算）", "确定", "取消",
                    initialValue: current, keyboard: Keyboard.Numeric);
                if (input is null) return;
                if (string.IsNullOrWhiteSpace(input)) { edge.Length = -1; }
                else if (double.TryParse(input.Trim(), out var len)) edge.Length = Math.Max(0, len);
                else StatusText = "无效的数值";
                await _maps.SaveAsync();
                StatusText = edge.Length == 0 ? "该连线将显示为虚线" :
                    edge.Length < 0 ? "该连线使用自动计算长度" : $"该连线长度为 {edge.Length:F0} 米";
                Refresh();
                break;
            }
            case "kind":
            {
                if (edge is null) return;
                edge.Kind = edge.Kind == "line" ? "curve" : "line";
                await _maps.SaveAsync();
                StatusText = $"线型已切换为：{(edge.Kind == "line" ? "直线" : "曲线")}";
                Refresh();
                break;
            }
            case "road":
            {
                if (edge is null) return;
                var kinds = new[]
                {
                    RoadKinds.Highway, RoadKinds.Trunk, RoadKinds.County, RoadKinds.Rural, RoadKinds.Street,
                    RoadKinds.Lane, RoadKinds.Walkway,
                    RoadKinds.Steps, RoadKinds.Bridge, RoadKinds.Tunnel, RoadKinds.Waterway, RoadKinds.Interior
                };
                var labels = kinds.Select(k => (k == edge.Road ? "● " : "") + RoadKinds.Display(k)).ToArray();
                var pick = await Shell.Current.DisplayActionSheet("这条路是什么路？", "取消", null, labels);
                if (string.IsNullOrWhiteSpace(pick) || pick == "取消") return;
                var at = Array.IndexOf(labels, pick);
                if (at < 0) return;
                edge.Road = kinds[at];
                await _maps.SaveAsync();
                StatusText = $"这条路改为：{RoadKinds.Display(edge.Road)}";
                Refresh();
                break;
            }
            case "roadname":
            {
                if (edge is null) return;
                // 路名直接改（长江路），不跟着端点名字走
                var rn = await Shell.Current.DisplayPromptAsync("命名这条路",
                    "路名（例：长江路）。留空 = 无名巷道。", "确定", "取消", initialValue: edge.RoadName);
                if (rn is null) return;
                edge.RoadName = rn.Trim();
                await _maps.SaveAsync();
                StatusText = string.IsNullOrEmpty(edge.RoadName) ? "已清除路名" : $"已命名：{edge.RoadName}";
                Refresh();
                break;
            }
            case "oneway":
                if (edge is null) return;
                edge.OneWay = !edge.OneWay;
                await _maps.SaveAsync();
                StatusText = edge.OneWay ? "已设为单行（反向不通）" : "已改回双向";
                Refresh();
                break;
            case "light":
                if (edge is null) return;
                edge.TrafficLight = !edge.TrafficLight;
                await _maps.SaveAsync();
                StatusText = edge.TrafficLight
                    ? $"这条路挂上了红绿灯：{TrafficSignalService.CycleText(edge)}（右键再点「红绿灯」可改周期与朝向）"
                    : "已取消红绿灯";
                Refresh();
                break;
            case "signal-now":
            {
                if (edge is null) return;
                // 顺箭头这侧走服务（开了实时就问高德，问不到自己退回本地）；倒着那侧没有实时报，只按周期算
                var fwd = await _signals.CurrentAsync(edge, true);
                var rev = SignalClock.Now(edge, false);
                await Shell.Current.DisplayAlert("这盏灯现在",
                    $"顺着箭头（A→B）：{fwd.Display}\n倒着走（B→A）：{rev.Display}\n"
                    + $"一轮：{TrafficSignalService.CycleText(edge)}｜绿灯放行：{SignalPhases.Display(edge.SignalPhase)}\n"
                    + $"这一眼是谁算的：{fwd.Source}", "知道了");
                break;
            }
            case "signal-cycle":
            {
                if (edge is null) return;
                var v = await Shell.Current.DisplayPromptAsync("红绿灯周期",
                    "一轮多少秒？想连绿、黄一起定就三个数隔开写，如「60 26 4」。留空 = 不改。",
                    "确定", "取消",
                    initialValue: (edge.SignalCycle > 0 ? edge.SignalCycle : SignalClock.DefaultCycle).ToString());
                if (string.IsNullOrWhiteSpace(v)) return;
                var bits = v.Split(new[] { ' ', ',', '，', '/', ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (bits.Length == 0
                    || !int.TryParse(bits[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cycle)
                    || cycle < 6)
                {
                    StatusText = "周期至少 6 秒，否则灯还没看清就换色了";
                    return;
                }
                edge.SignalCycle = cycle;
                if (bits.Length > 1 && int.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g))
                    edge.SignalGreen = Math.Clamp(g, 1, cycle - 2);
                if (bits.Length > 2 && int.TryParse(bits[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
                    edge.SignalYellow = Math.Clamp(y, 1, Math.Max(1, cycle - edge.SignalGreen - 1));
                await _maps.SaveAsync();
                StatusText = $"这条路现在：{TrafficSignalService.CycleText(edge)}";
                Refresh();
                break;
            }
            case "signal-phase":
            {
                if (edge is null) return;
                var phases = new[] { SignalPhases.Forward, SignalPhases.Reverse, SignalPhases.Both };
                var labels = phases.Select(p => (p == edge.SignalPhase ? "● " : "") + SignalPhases.Display(p)).ToArray();
                var pick = await Shell.Current.DisplayActionSheet("绿灯放行哪一侧？", "取消", null, labels);
                var at = Array.IndexOf(labels, pick);
                if (at < 0) return;
                edge.SignalPhase = phases[at];
                await _maps.SaveAsync();
                StatusText = $"绿灯朝向改为：{SignalPhases.Display(edge.SignalPhase)}";
                Refresh();
                break;
            }
            case "toll":
            {
                if (edge is null) return;
                if (!edge.Toll)
                {
                    var fee = await Shell.Current.DisplayPromptAsync("设收费站",
                        "过路费多少钱（元）？留空 = 0（免费站）", "确定", "取消",
                        initialValue: edge.TollFee > 0 ? edge.TollFee.ToString("F0") : "", keyboard: Keyboard.Numeric);
                    if (fee is null) return;   // 取消 = 整个操作不生效
                    edge.Toll = true;
                    edge.TollFee = double.TryParse(fee.Trim(), out var f) && f > 0 ? f : 0;
                }
                else
                {
                    edge.Toll = false;
                    edge.TollFee = 0;
                }
                await _maps.SaveAsync();
                StatusText = edge.Toll ? $"收费站：{edge.TollFee:F0} 元" : "已撤掉收费站";
                Refresh();
                break;
            }
            case "speed":
            {
                if (edge is null) return;
                var cur = edge.SpeedKmh > 0 ? edge.SpeedKmh.ToString("F0") : "";
                var input = await Shell.Current.DisplayPromptAsync("限速（km/h）",
                    $"填限速；留空/0 = 按类别默认（{RoadKinds.Display(edge.Road)} ≈ {RoadKinds.DefaultSpeedKmh(edge.Road):F0}）",
                    "确定", "取消", initialValue: cur, keyboard: Keyboard.Numeric);
                if (input is null) return;
                if (string.IsNullOrWhiteSpace(input)) edge.SpeedKmh = 0;
                else if (double.TryParse(input.Trim(), out var kmh)) edge.SpeedKmh = Math.Max(0, kmh);
                else { StatusText = "无效的数值"; return; }
                await _maps.SaveAsync();
                StatusText = edge.SpeedKmh > 0 ? $"限速 {edge.SpeedKmh:F0} km/h" : "限速回到类别默认";
                Refresh();
                break;
            }
            case "note":
                if (edge is null) return;
                var note = await Shell.Current.DisplayPromptAsync("编辑备注", "连线备注（可留空清除）", "确定", "取消",
                    initialValue: edge.Note);
                if (note is null) return;
                edge.Note = note.Trim();
                await _maps.SaveAsync();
                Refresh();
                break;
            case "info":
                if (edge is null) return;
                var aName = _maps.Map.PointNameById(edge.A) ?? edge.A;
                var bName = _maps.Map.PointNameById(edge.B) ?? edge.B;
                var mark = string.Join(" · ",
                    new[] { edge.OneWay ? "单行" : null, edge.TrafficLight ? "红绿灯" : null,
                            edge.Toll ? $"收费 {edge.TollFee:F0} 元" : null }.Where(x => x is not null));
                await Shell.Current.DisplayAlert("连线信息",
                    $"从：{aName}\n到：{bName}\n道路：{RoadKinds.Display(edge.Road)}" +
                    $"{(string.IsNullOrWhiteSpace(edge.RoadName) ? "" : " · " + edge.RoadName)}\n" +
                    $"线型：{(edge.Kind == "line" ? "直线" : "曲线")}\n折点：{edge.Waypoints.Count}\n长度：{LengthText(edge)}\n" +
                    $"限速：{(edge.SpeedKmh > 0 ? $"{edge.SpeedKmh:F0} km/h" : $"按类别 ≈{RoadKinds.DefaultSpeedKmh(edge.Road):F0}")}\n" +
                    $"通行：约 {RoadTimeText(edge)}\n标记：{(string.IsNullOrEmpty(mark) ? "无" : mark)}\n" +
                    $"备注：{(string.IsNullOrWhiteSpace(edge.Note) ? "无" : edge.Note)}",
                    "知道了");
                break;
            case "del":
            {
                if (edge is null) return;
                var ok = await Shell.Current.DisplayAlert("删除连线",
                    $"确定删除「{_maps.Map.PointNameById(edge.A) ?? edge.A} ↔ {_maps.Map.PointNameById(edge.B) ?? edge.B}」吗？", "删除", "取消");
                if (!ok) return;
                _maps.RemoveEdge(edge.A, edge.B);
                StatusText = "已删除连线";
                Refresh();
                break;
            }
        }
    }

    // ============ 街面 3D 进入（点按动作表 / 右键菜单共用） ============

    /// <summary>浏览模式点一下街道：弹一份小菜单，问要不要从这里进 3D。</summary>
    private async Task ShowStreetActionsAsync(MapEdge edge, Point world)
    {
        var title = string.IsNullOrWhiteSpace(edge.RoadName)
            ? RoadKinds.Display(edge.Road)
            : $"{edge.RoadName}（{RoadKinds.Display(edge.Road)}）";
        var pick = await Shell.Current.DisplayActionSheet(title, "取消", null, "由此进入 3D");
        if (string.IsNullOrWhiteSpace(pick) || pick == "取消") return;
        await Enter3dFromStreetAsync(edge, world);
    }

    /// <summary>
    /// 街面 3D：取边上离点击最近的采样点，沿路的切向摆机位（单位→米、弧度）后跳 3D 页。
    /// 户型内/楼梯这类「楼层间的线」没有 3D 地面可站，直接劝退。
    /// </summary>
    private async Task Enter3dFromStreetAsync(MapEdge edge, Point world)
    {
        var kind = string.IsNullOrEmpty(edge.Road) ? RoadKinds.Street : edge.Road;
        if (kind is RoadKinds.Interior or RoadKinds.Steps)
        {
            StatusText = "房型内/楼梯的连线不进 3D，站到真正的街道上再进。";
            return;
        }
        var pts = Drawable.SampleEdge(edge);   // 画布单位
        if (pts.Count < 2) { StatusText = "这条路太短，进不去"; return; }

        var best = 0;
        var bestD = double.MaxValue;
        for (var i = 0; i < pts.Count; i++)
        {
            var dx = pts[i].X - world.X;
            var dy = pts[i].Y - world.Y;
            var d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = i; }
        }
        var i0 = Math.Max(0, best - 1);
        var i1 = Math.Min(pts.Count - 1, best + 1);
        var tx = pts[i1].X - pts[i0].X;
        var ty = pts[i1].Y - pts[i0].Y;
        if (Math.Abs(tx) < 1e-9 && Math.Abs(ty) < 1e-9) { StatusText = "这条路太短，进不去"; return; }

        // 相机 forward = (sin yaw, -cos yaw)（世界 y 向下、北 = -y）；沿街走向-initial 朝前
        var yaw = Math.Atan2(tx, -ty);
        var mpu = _maps.Map.MetersPerUnit > 0 ? _maps.Map.MetersPerUnit : 0.5;
        var mx = (pts[best].X * mpu).ToString("0.####", CultureInfo.InvariantCulture);
        var my = (pts[best].Y * mpu).ToString("0.####", CultureInfo.InvariantCulture);
        var ydeg = (yaw * 180 / Math.PI).ToString("0.##", CultureInfo.InvariantCulture);
        StatusText = "已按街面视角进入 3D（返回就回到 2D 编辑器）";
        await Shell.Current.GoToAsync($"minimal3d?x={mx}&y={my}&yaw={ydeg}");
    }
}
