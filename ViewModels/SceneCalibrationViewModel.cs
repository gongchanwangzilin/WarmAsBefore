using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Graphics;
using WarmAsBefore.Drawables;
using WarmAsBefore.Models;
using WarmAsBefore.Services;
using LineGroup = WarmAsBefore.Drawables.SceneCalibrationDrawable.LineGroup;

namespace WarmAsBefore.ViewModels;

/// <summary>标定页当前的操作目标（决定"在空白处拖拽"是干什么）。</summary>
public enum DrawTarget
{
    /// <summary>画落脚几何（点选模式的点 / 线性模式的线）。</summary>
    Landing,
    /// <summary>画缩放标定几何（双线模式的两条线 / 圆球模式的球）。</summary>
    Scale,
    /// <summary>平移视图。</summary>
    Pan
}

/// <summary>
/// 标定页 ViewModel：在一张背景原图上标出「落脚点」与「缩放依据」。
///
/// 交互总则（无模态状态机，靠命中优先级 + 双击区分）：
///   拖空白 = 按当前目标新建（画线 / 加球 / 加点）
///   拖顶点/球心/落脚点 = 移动；拖球缘 = 改半径
///   双击顶点/球/落脚点 = 删除；双击线身 = 在该处插入折点（把直线掰弯）
///   右键 = 撤销最后一处几何
///
/// 世界坐标全程用**原图像素**，只在落盘时归一化——像素坐标让球半径与 8px 命中容差都直观。
/// </summary>
public sealed partial class SceneCalibrationViewModel : ObservableObject
{
    private readonly MapService _maps;
    private readonly SceneCalibrationService _cal;

    public SceneCalibrationDrawable Drawable { get; } = new();

    private MapScene? _scene;
    private ScenePlacement _draft = new();
    private string? _imageAbs;

    // ---- 交互状态 ----
    private Action? _invalidate;
    private bool _panning;
    private PointF _panLast;
    private (LineGroup Group, int Line, int Pt) _dragVertex = (LineGroup.None, -1, -1);
    private int _dragSphere = -1;
    private bool _draggingRadius;
    private int _dragPoint = -1;
    private NormPoint? _pendingLineStart;
    private PointF _pendingLineStartScreen;
    private DateTime _lastTapAt = DateTime.MinValue;
    private PointF _lastTapPos;
    private long _rightTick;          // WinUI 右键时间戳
    private bool _finished;

    // ---- 可绑定状态 ----
    [ObservableProperty] private string _statusText = "拖空白处开始标定";
    [ObservableProperty] private DrawTarget _target = DrawTarget.Landing;
    [ObservableProperty] private string _missingText = "";
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private bool _canComplete;
    [ObservableProperty] private bool _hasMissing;
    [ObservableProperty] private string _sceneName = "";
    [ObservableProperty] private string _imageInfo = "";

    public SceneCalibrationViewModel(MapService maps, SceneCalibrationService cal)
    {
        _maps = maps;
        _cal = cal;
        Drawable.Placement = _draft;
    }

    public void AttachInvalidate(Action invalidate) => _invalidate = invalidate;
    private void Touch() => _invalidate?.Invoke();

    /// <summary>屏幕像素的命中容差换算到世界坐标（世界 = 原图像素）。</summary>
    private double HitTol => 10.0 / Math.Max(Drawable.Scale, 0.0001);

    public bool IsPointLanding => _draft.Landing == LandingKinds.Point;
    public bool IsFullLanding => _draft.Landing == LandingKinds.Full;
    public bool IsLinearLanding => _draft.Landing == LandingKinds.Linear;
    public bool IsDualLine => _draft.ScaleMethod == ScaleMethods.DualLine;
    public bool IsSpheres => _draft.ScaleMethod == ScaleMethods.Spheres;

    public string LandingChip(string kind) => _draft.Landing == kind ? "● " + PlacementMath.LandingName(kind) : PlacementMath.LandingName(kind);
    public string ScaleChip(string method) => _draft.ScaleMethod == method ? "● " + PlacementMath.ScaleName(method) : PlacementMath.ScaleName(method);
    public string PointChipLabel => LandingChip(LandingKinds.Point);
    public string FullChipLabel => LandingChip(LandingKinds.Full);
    public string LinearChipLabel => LandingChip(LandingKinds.Linear);
    public string DualLineChipLabel => ScaleChip(ScaleMethods.DualLine);
    public string SphereChipLabel => ScaleChip(ScaleMethods.Spheres);

    /// <summary>落脚点与缩放方式都是"线"时才需要用户手动切换操作目标，否则自动定。</summary>
    public bool ShowTargetSwitch => IsLinearLanding && IsDualLine;
    public bool IsTargetLanding => Target == DrawTarget.Landing;
    public bool IsTargetScale => Target == DrawTarget.Scale;
    public bool IsTargetPan => Target == DrawTarget.Pan;
    public string TargetLandingLabel => IsTargetLanding ? "● 画落脚线" : "画落脚线";
    public string TargetScaleLabel => IsTargetScale ? "● 画缩放线" : "画缩放线";
    public string TargetPanLabel => IsTargetPan ? "● 平移" : "平移";

    // ============ 载入 ============

    public void Load(MapScene scene, string? imageAbs, int imgW, int imgH, double? targetAspect)
    {
        _scene = scene;
        _imageAbs = imageAbs;
        // 从已有标定续做（改一个不满意的点，不必从零重画）
        _draft = scene.Placement?.Clone() ?? new ScenePlacement();
        _draft.Landing = string.IsNullOrEmpty(_draft.Landing) ? LandingKinds.Point : _draft.Landing;
        _draft.ScaleMethod = string.IsNullOrEmpty(_draft.ScaleMethod) ? ScaleMethods.Spheres : _draft.ScaleMethod;

        SceneName = string.IsNullOrWhiteSpace(scene.Name) ? "未命名场景" : scene.Name;
        Drawable.Placement = _draft;
        Drawable.ImagePath = imageAbs;
        Drawable.ImageWidth = imgW;
        Drawable.ImageHeight = imgH;
        Drawable.TargetAspect = targetAspect;
        Drawable.ResetImage();
        ImageInfo = imgW > 0 && imgH > 0 ? $"原图 {imgW}×{imgH}" : "原图尺寸未知（视频背景可能如此）";

        // 根据已有数据推断最合理的初始操作目标
        Target = IsPointLanding || IsLinearLanding ? DrawTarget.Landing : DrawTarget.Scale;
        if (IsLinearLanding && IsDualLine)
            Target = _draft.Lines.Count == 0 ? DrawTarget.Landing : DrawTarget.Scale;

        Drawable.EnsureImageLoaded(Touch);
        Refresh();
    }

    public void OnViewportChanged(double w, double h)
    {
        Drawable.FitToViewport(w, h);
        Touch();
    }

    // ============ 输入（由页面转发；坐标是屏幕像素）============

    /// <summary>WinUI 右键按压时同步标记，抑制同一次按压被 Touch 当成左键操作。</summary>
    public void MarkRightPress() => _rightTick = Environment.TickCount64;
    private bool IsRightEcho => Environment.TickCount64 - _rightTick < 60;

    public void CanvasStart(PointF screen)
    {
        if (IsRightEcho || _draft.Landing is null) return;

        var world = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
        var tol = HitTol;

        var isDouble = (DateTime.UtcNow - _lastTapAt).TotalMilliseconds < 350 && ScreenNear(screen, _lastTapPos, 24);
        _lastTapAt = DateTime.UtcNow;
        _lastTapPos = screen;

        // 命中优先级：折线顶点 → 球心 → 球缘 → 线身（双击插点）→ 落脚点 → 空白
        var v = Drawable.HitVertex(world, tol);
        if (v.Group != LineGroup.None)
        {
            if (isDouble) { RemoveVertex(v); Touch(); return; }
            _dragVertex = v;
            Touch();
            return;
        }

        var sc = Drawable.HitSphereCenter(world, tol);
        if (sc >= 0)
        {
            if (isDouble) { RemoveSphere(sc); Touch(); return; }
            _dragSphere = sc;
            _draggingRadius = false;
            Touch();
            return;
        }

        var sr = Drawable.HitSphereRim(world, tol);
        if (sr >= 0)
        {
            _dragSphere = sr;
            _draggingRadius = true;
            Touch();
            return;
        }

        var lb = Drawable.HitLineBody(world, tol);
        if (lb.Group != LineGroup.None)
        {
            // 必须在命中线身时就结束本次按下：否则会继续往下落到"空白"分支，
            // 在已有线上又新建一条线。
            if (isDouble) InsertVertex(lb, world);
            Touch();
            return;
        }

        var pi = Drawable.HitPoint(world, tol);
        if (pi >= 0)
        {
            if (isDouble) { RemovePoint(pi); Touch(); return; }
            _dragPoint = pi;
            Touch();
            return;
        }

        // 空白
        switch (Target)
        {
            case DrawTarget.Pan:
                _panning = true;
                _panLast = screen;
                break;

            case DrawTarget.Landing:
                if (IsPointLanding) AddPoint(world);
                else if (IsLinearLanding) BeginLine(LineGroup.Landing, world, screen);
                break;

            case DrawTarget.Scale:
                if (IsDualLine)
                {
                    if (_draft.ScaleLines.Count >= 2)
                    {
                        StatusText = "双线已有 2 条。双击线身可插点掰弯，右键撤销重画";
                        break;
                    }
                    BeginLine(LineGroup.Scale, world, screen);
                }
                else AddSphere(world);
                break;
        }
        Touch();
    }

    public void CanvasDrag(PointF screen)
    {
        if (IsRightEcho) return;
        var world = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));

        if (_panning)
        {
            Drawable.OffsetX += screen.X - _panLast.X;
            Drawable.OffsetY += screen.Y - _panLast.Y;
            _panLast = screen;
            Touch();
            return;
        }

        if (_dragVertex.Group != LineGroup.None)
        {
            SetVertex(_dragVertex, Drawable.ToNormalized(world));
            Touch();
            return;
        }

        if (_dragSphere >= 0 && _dragSphere < _draft.Spheres.Count)
        {
            var s = _draft.Spheres[_dragSphere];
            if (_draggingRadius)
            {
                var r = Math.Abs(world.X - s.X * Drawable.ImageWidth) ;
                // 半径按"半径 ÷ 原图宽"归一化；用球心到指针的欧氏距离更符合直觉
                var c = new Point(s.X * Drawable.ImageWidth, s.Y * Drawable.ImageHeight);
                var dx = world.X - c.X;
                var dy = world.Y - c.Y;
                r = Math.Sqrt(dx * dx + dy * dy);
                s.R = Math.Clamp(Drawable.ImageWidth > 0 ? r / Drawable.ImageWidth : s.R, 0.005, 0.6);
            }
            else
            {
                var n = Drawable.ToNormalized(world);
                s.X = Math.Clamp(n.X, -0.02, 1.02);
                s.Y = Math.Clamp(n.Y, -0.02, 1.02);
            }
            Refresh();
            Touch();
            return;
        }

        if (_dragPoint >= 0 && _dragPoint < _draft.Points.Count)
        {
            var n = Drawable.ToNormalized(world);
            _draft.Points[_dragPoint].X = Math.Clamp(n.X, -0.02, 1.02);
            _draft.Points[_dragPoint].Y = Math.Clamp(n.Y, -0.02, 1.02);
            Refresh();
            Touch();
            return;
        }

        if (_pendingLineStart is not null)
        {
            Drawable.LinePreview = (ToPixel(_pendingLineStart), world);
            Touch();
        }
    }

    public void CanvasEnd(PointF screen)
    {
        if (IsRightEcho) return;
        _panning = false;
        _dragVertex = (LineGroup.None, -1, -1);
        _dragSphere = -1;
        _draggingRadius = false;
        _dragPoint = -1;

        if (_pendingLineStart is not null)
        {
            var startScreen = _pendingLineStartScreen;
            var start = _pendingLineStart;
            _pendingLineStart = null;
            Drawable.LinePreview = null;

            // 只是点了一下（没拖动）就不新建线，避免误触产生退化的两点线
            if (!ScreenNear(screen, startScreen, 12))
            {
                var end = Drawable.ToNormalized(Drawable.ScreenToWorld(new Point(screen.X, screen.Y)));
                var line = new NormLine { Points = { start, ClampNorm(end) } };
                if (_currentGroup == LineGroup.Landing) _draft.Lines.Add(line);
                else _draft.ScaleLines.Add(line);
                StatusText = _currentGroup == LineGroup.Landing ? "已加一条落脚线" : "已加一条缩放线";
            }
        }
        Refresh();
        Touch();
    }

    /// <summary>滚轮缩放（屏幕点为锚）。</summary>
    public void Zoom(double delta, PointF screen)
    {
        if (Drawable.Scale <= 0) return;
        SetScaleAt(Drawable.Scale * (delta > 0 ? 1.15 : 1 / 1.15), screen);
    }

    /// <summary>直接设定缩放倍数（捏合手势用连续值，不适合走 Zoom 的固定步进）。</summary>
    public void SetScaleAt(double newScale, PointF screen)
    {
        var oldScale = Drawable.Scale;
        if (oldScale <= 0) return;
        newScale = Math.Clamp(newScale, 0.05, 6.0);
        if (Math.Abs(newScale - oldScale) < 0.0001) return;
        // 保持锚点指向的世界坐标原地不动
        var wx = (screen.X - Drawable.OffsetX) / oldScale;
        var wy = (screen.Y - Drawable.OffsetY) / oldScale;
        Drawable.OffsetX = screen.X - wx * newScale;
        Drawable.OffsetY = screen.Y - wy * newScale;
        Drawable.Scale = newScale;
        Touch();
    }

    /// <summary>右键 = 撤销最后一处几何（后进的先撤）。</summary>
    public void RightClick()
    {
        if (_draft.Spheres.Count > 0) { _draft.Spheres.RemoveAt(_draft.Spheres.Count - 1); StatusText = "已撤销一个圆球"; }
        else if (_draft.ScaleLines.Count > 0) { _draft.ScaleLines.RemoveAt(_draft.ScaleLines.Count - 1); StatusText = "已撤销一条缩放线"; }
        else if (_draft.Lines.Count > 0) { _draft.Lines.RemoveAt(_draft.Lines.Count - 1); StatusText = "已撤销一条落脚线"; }
        else if (_draft.Points.Count > 0) { _draft.Points.RemoveAt(_draft.Points.Count - 1); StatusText = "已撤销一个落脚点"; }
        else StatusText = "没有可撤销的内容";
        Refresh();
        Touch();
    }

    // ============ 几何操作 ============

    private LineGroup _currentGroup = LineGroup.Landing;

    private void BeginLine(LineGroup group, Point world, PointF screen)
    {
        _currentGroup = group;
        _pendingLineStart = Drawable.ToNormalized(world);
        _pendingLineStartScreen = screen;
        Drawable.LinePreview = (world, world);
    }

    private List<NormLine> GroupLines(LineGroup g) => g == LineGroup.Landing ? _draft.Lines : _draft.ScaleLines;

    private void SetVertex((LineGroup Group, int Line, int Pt) v, NormPoint n)
    {
        var lines = GroupLines(v.Group);
        if (v.Line < 0 || v.Line >= lines.Count) return;
        var pts = lines[v.Line].Points;
        if (v.Pt < 0 || v.Pt >= pts.Count) return;
        pts[v.Pt].X = Math.Clamp(n.X, -0.02, 1.02);
        pts[v.Pt].Y = Math.Clamp(n.Y, -0.02, 1.02);
        Refresh();
    }

    private void RemoveVertex((LineGroup Group, int Line, int Pt) v)
    {
        var lines = GroupLines(v.Group);
        if (v.Line < 0 || v.Line >= lines.Count) return;
        var pts = lines[v.Line].Points;
        if (pts.Count <= 2) { lines.RemoveAt(v.Line); StatusText = "线太短，已整条删除"; }
        else if (v.Pt >= 0 && v.Pt < pts.Count) { pts.RemoveAt(v.Pt); StatusText = "已删除一个折点"; }
        Refresh();
    }

    /// <summary>双击线身：在该处插入一个折点，之后就能拖它把直线掰弯（公路通常是弯的）。</summary>
    private void InsertVertex((LineGroup Group, int Line) lb, Point world)
    {
        var lines = GroupLines(lb.Group);
        if (lb.Line < 0 || lb.Line >= lines.Count) return;
        var pts = lines[lb.Line].Points;
        if (pts.Count == 0) return;
        var n = Drawable.ToNormalized(world);

        // 插到最近的那一段之后，保证顺序仍沿线的走向
        var bestIdx = pts.Count - 1;
        var bestD = double.MaxValue;
        for (int i = 1; i < pts.Count; i++)
        {
            var mx = (pts[i - 1].X + pts[i].X) / 2;
            var my = (pts[i - 1].Y + pts[i].Y) / 2;
            var d = (n.X - mx) * (n.X - mx) + (n.Y - my) * (n.Y - my);
            if (d < bestD) { bestD = d; bestIdx = i; }
        }
        pts.Insert(bestIdx, ClampNorm(n));
        StatusText = "已插入折点，拖它可把线掰弯";
        Refresh();
    }

    private void AddPoint(Point world)
    {
        var n = Drawable.ToNormalized(world);
        _draft.Points.Add(ClampNorm(n));
        StatusText = $"已加落脚点（{_draft.Points.Count}）";
        Refresh();
    }

    private void RemovePoint(int idx)
    {
        if (idx >= 0 && idx < _draft.Points.Count) _draft.Points.RemoveAt(idx);
        Refresh();
    }

    private void AddSphere(Point world)
    {
        var n = Drawable.ToNormalized(world);
        // 默认半径取图宽的 6%，大致是个"能看出远近"的起点，用户再拖球缘调
        _draft.Spheres.Add(new NormSphere { X = Clamp01(n.X), Y = Clamp01(n.Y), R = 0.06 });
        StatusText = $"已加示例圆球（{_draft.Spheres.Count}）——拖球心移动，拖球缘改大小";
        Refresh();
    }

    private void RemoveSphere(int idx)
    {
        if (idx >= 0 && idx < _draft.Spheres.Count) _draft.Spheres.RemoveAt(idx);
        Refresh();
    }

    private Point ToPixel(NormPoint n) => new(n.X * Drawable.ImageWidth, n.Y * Drawable.ImageHeight);
    private static NormPoint ClampNorm(NormPoint n) => new(Math.Clamp(n.X, -0.02, 1.02), Math.Clamp(n.Y, -0.02, 1.02));
    private static double Clamp01(double v) => Math.Clamp(v, 0, 1);

    private static bool ScreenNear(PointF a, PointF b, double d)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy <= d * d;
    }

    // ============ 模式切换命令 ============

    [RelayCommand]
    private void SetLanding(string kind)
    {
        if (_draft.Landing == kind) return;
        _draft.Landing = kind;
        // 切类型不清空已画的数据——用户可能只是想换个模式看看，切回来数据还在
        Target = kind == LandingKinds.Linear ? DrawTarget.Landing
               : _draft.ScaleMethod == ScaleMethods.DualLine ? DrawTarget.Scale : DrawTarget.Scale;
        StatusText = $"落脚点类型：{PlacementMath.LandingName(kind)}";
        Refresh();
        Touch();
    }

    [RelayCommand]
    private void SetScaleMethod(string method)
    {
        if (_draft.ScaleMethod == method) return;
        _draft.ScaleMethod = method;
        Target = method == ScaleMethods.DualLine && IsLinearLanding ? Target : DrawTarget.Scale;
        StatusText = $"缩放标定方式：{PlacementMath.ScaleName(method)}";
        Refresh();
        Touch();
    }

    [RelayCommand]
    private void SetTarget(string target)
    {
        Target = target switch
        {
            "landing" => DrawTarget.Landing,
            "scale" => DrawTarget.Scale,
            _ => DrawTarget.Pan
        };
        StatusText = Target switch
        {
            DrawTarget.Landing => "拖空白处画落脚线",
            DrawTarget.Scale => "拖空白处画缩放线",
            _ => "拖空白处平移视图"
        };
        Refresh();
        Touch();
    }

    /// <summary>反向画的第二条缩放线手动翻转（自动配对失灵时的兜底）。</summary>
    [RelayCommand]
    private void FlipSecondScaleLine()
    {
        if (_draft.ScaleLines.Count != 2) { StatusText = "需要恰好两条缩放线才能翻转"; return; }
        _draft.ScaleLines[1].Points.Reverse();
        StatusText = "已翻转第二条缩放线";
        Refresh();
        Touch();
    }

    [RelayCommand]
    private void FitView()
    {
        Drawable.FitToViewport(Drawable.ViewportWidth, Drawable.ViewportHeight);
        Touch();
    }

    [RelayCommand]
    private void ClearAll()
    {
        _draft.Points.Clear();
        _draft.Lines.Clear();
        _draft.ScaleLines.Clear();
        _draft.Spheres.Clear();
        StatusText = "已清空所有标定几何";
        Refresh();
        Touch();
    }

    // ============ 输出与收尾 ============

    /// <summary>重算三行强制输出与缺项提示。</summary>
    private void Refresh()
    {
        SummaryText = PlacementMath.Describe(_draft);

        var (ok, missing) = PlacementMath.Validate(_draft);
        CanComplete = ok;
        HasMissing = !ok;
        MissingText = ok ? "" : "还差：" + string.Join("\n", missing.Select((m, i) => $"{i + 1}. {m}"));
        OnPropertyChanged(nameof(HasMissing));

        OnPropertyChanged(nameof(PointChipLabel));
        OnPropertyChanged(nameof(FullChipLabel));
        OnPropertyChanged(nameof(LinearChipLabel));
        OnPropertyChanged(nameof(DualLineChipLabel));
        OnPropertyChanged(nameof(SphereChipLabel));
        OnPropertyChanged(nameof(ShowTargetSwitch));
        OnPropertyChanged(nameof(IsTargetLanding));
        OnPropertyChanged(nameof(IsTargetScale));
        OnPropertyChanged(nameof(IsTargetPan));
        OnPropertyChanged(nameof(TargetLandingLabel));
        OnPropertyChanged(nameof(TargetScaleLabel));
        OnPropertyChanged(nameof(TargetPanLabel));
        OnPropertyChanged(nameof(IsPointLanding));
        OnPropertyChanged(nameof(IsFullLanding));
        OnPropertyChanged(nameof(IsLinearLanding));
        OnPropertyChanged(nameof(IsDualLine));
        OnPropertyChanged(nameof(IsSpheres));
    }

    /// <summary>完成标定：写回场景并落盘。返回是否成功（数据不完整时不该走到这里）。</summary>
    public async Task<bool> TryCommitAsync()
    {
        var (ok, missing) = PlacementMath.Validate(_draft);
        if (!ok)
        {
            MissingText = "还差：" + string.Join("\n", missing.Select((m, i) => $"{i + 1}. {m}"));
            StatusText = "标定还不完整，无法完成";
            return false;
        }
        if (_scene is null) return false;

        _draft.SourceBackground = _scene.Background;
        _draft.SourceStamp = MapService.StampOf(_imageAbs);
        _draft.SourceWidth = Drawable.ImageWidth;
        _draft.SourceHeight = Drawable.ImageHeight;
        _draft.CalibratedAt = DateTime.UtcNow;
        _draft.Note = PlacementMath.Describe(_draft);
        _scene.Placement = _draft;

        await _maps.SaveAsync();
        _maps.NotifyChanged();
        App.WriteLog("标定完成 " + _scene.Name + "：" + _draft.Note);
        return true;
    }

    public void Finish(bool ok)
    {
        if (_finished) return;
        _finished = true;
        _cal.Finish(ok);
    }

    public void MarkFinished() => _finished = true;
    public bool IsFinished => _finished;
}
