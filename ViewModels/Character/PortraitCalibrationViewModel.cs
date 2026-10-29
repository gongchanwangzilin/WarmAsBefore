using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 立绘标定：把一份素材映射到 1080×1920 的逻辑画布上。
///
/// 预览与游戏**同一套公式**，包括背景 AspectFill 的裁切映射与落脚点锚定：
/// 游戏怎么算，这里就怎么算，所以在这一页调好，回到游戏里就是看到的样子
/// （差别只有舞台尺寸——预览用的是固定 9:16 画框，游戏用实际舞台尺寸）。
///
/// 校验由 <see cref="PortraitCalibrationMath.Validate"/> 统一给出，不合法不允许保存。
/// </summary>
public sealed partial class PortraitCalibrationViewModel : ObservableObject, IQueryAttributable
{
    private readonly StorageProvider _store;
    private readonly PortraitCalibrationService _cal;
    private readonly PortraitNormalizer _normalizer;
    private readonly CharacterLibrary _library;
    private readonly GameEngine _engine;
    private readonly MapService _map;

    private string _key = "";
    private int _srcW, _srcH;
    private bool _loading;
    private ScenePlacement? _placement;
    private (int W, int H) _bgSize;

    public PortraitCalibrationViewModel(StorageProvider store, PortraitCalibrationService cal,
        PortraitNormalizer normalizer, CharacterLibrary library, GameEngine engine, MapService map)
    {
        _store = store;
        _cal = cal;
        _normalizer = normalizer;
        _library = library;
        _engine = engine;
        _map = map;
    }

    /// <summary>预览框：与逻辑画布同比例（9:16）的手机舞台尺寸。</summary>
    private const double FrameW = 270;
    private const double FrameH = FrameW / (PortraitCanvas.Width / PortraitCanvas.Height);

    /// <summary>游戏里未标定时用的落脚点（归一化）：DesiredNormalized 的 y 恒为 0.9。</summary>
    private const double DefaultAnchorY = 0.9;

    [ObservableProperty] private string _absPath = "";
    [ObservableProperty] private string _bgAbsPath = "";
    [ObservableProperty] private string _title = "立绘标定";

    public string SourceLabel => _srcW > 0 ? $"{_srcW}×{_srcH}" : "读取中…";
    public bool HasBackground => !string.IsNullOrEmpty(BgAbsPath);

    [ObservableProperty] private bool _flipHorizontal;
    [ObservableProperty] private bool _flipVertical;
    [ObservableProperty] private int _rotation;
    [ObservableProperty] private double _scale = 1.0;
    [ObservableProperty] private double _offsetX;
    [ObservableProperty] private double _offsetY;
    [ObservableProperty] private double _footLine = 1.0;

    partial void OnFlipHorizontalChanged(bool value) => Recalc();
    partial void OnFlipVerticalChanged(bool value) => Recalc();
    partial void OnRotationChanged(int value) => Recalc();
    partial void OnScaleChanged(double value) => Recalc();
    partial void OnOffsetXChanged(double value) => Recalc();
    partial void OnOffsetYChanged(double value) => Recalc();
    partial void OnFootLineChanged(double value) => Recalc();

    // ---- 预览变换（与游戏同公式） ----
    [ObservableProperty] private double _previewW = 100;
    [ObservableProperty] private double _previewH = 178;
    [ObservableProperty] private double _previewX;
    [ObservableProperty] private double _previewY;
    [ObservableProperty] private double _previewScaleX = 1;
    [ObservableProperty] private double _previewScaleY = 1;
    [ObservableProperty] private double _previewRotation;
    /// <summary>脚底基准线在预览框内的 Y（像素）：与游戏算出的锚点完全一致。</summary>
    [ObservableProperty] private double _groundLineY = 216;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _anchorNote = "";

    [ObservableProperty] private string _problemsText = "";
    partial void OnProblemsTextChanged(string value) => OnPropertyChanged(nameof(HasProblems));
    public bool HasProblems => !string.IsNullOrEmpty(ProblemsText);

    [ObservableProperty] private string _normalizeNote = "";

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("key", out var v) && v is string k && !string.IsNullOrWhiteSpace(k))
            _key = Uri.UnescapeDataString(k);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            await LoadSceneContextAsync();
            AbsPath = ResolveAbs(_key);
            var (w, h) = await ImageProbe.SizeOfAsync(string.IsNullOrEmpty(AbsPath) || !File.Exists(AbsPath) ? null : AbsPath);
            _srcW = w;
            _srcH = h;
            Title = $"立绘标定 · {Path.GetFileName(_key)}";

            var existing = await _cal.GetAsync(_key);
            if (existing is not null)
            {
                FlipHorizontal = existing.FlipHorizontal;
                FlipVertical = existing.FlipVertical;
                Rotation = existing.Rotation;
                Scale = existing.Scale;
                OffsetX = existing.OffsetX;
                OffsetY = existing.OffsetY;
                FootLine = existing.FootLine;
            }
            else
            {
                Rotation = 0;
                Scale = 1.0;
                OffsetX = 0;
                OffsetY = 0;
                FootLine = 1.0;
                FlipHorizontal = false;
                FlipVertical = false;
            }
            OnPropertyChanged(nameof(SourceLabel));
            Recalc();
        }
        catch (Exception ex)
        {
            App.WriteLog("PortraitCalibrationVM.Load -> " + ex.Message);
            Status = "读取素材失败：" + ex.Message;
        }
        finally { _loading = false; }
    }

    /// <summary>取当前场景的背景与标定：预览要用它们复现游戏的 AspectFill 映射与落脚点。</summary>
    private async Task LoadSceneContextAsync()
    {
        try
        {
            var scene = _map.Map.SceneById(_engine.State.Location);
            _placement = scene?.Placement;
            var bg = scene is null ? null : _map.ResolveBackground(scene);
            if (!string.IsNullOrEmpty(bg) && !File.Exists(bg)) bg = null;
            BgAbsPath = bg ?? "";
            OnPropertyChanged(nameof(HasBackground));
            _bgSize = await ImageProbe.SizeOfAsync(bg);
            AnchorNote = _placement is not null && PlacementMath.Validate(_placement).Ok
                ? "落脚点来自当前场景的标定数据"
                : $"当前场景没有可用标定，按游戏默认居中 + 落脚点 {DefaultAnchorY:0.0}（画面 90% 高度处）";
        }
        catch (Exception ex)
        {
            App.WriteLog("PortraitCalibrationVM.Scene -> " + ex.Message);
            _placement = null;
            _bgSize = (0, 0);
            AnchorNote = "取场景信息失败，按默认落脚点预览";
        }
    }

    /// <summary>把 SpriteMap 里的存储相对路径解析成绝对路径（与游戏侧同样的候选顺序）。</summary>
    private string ResolveAbs(string rel)
    {
        if (string.IsNullOrWhiteSpace(rel)) return "";
        if (Path.IsPathRooted(rel) && File.Exists(rel)) return rel;
        var candidates = new List<string>
        {
            Path.Combine(_store.Root, rel),
            Path.Combine(_store.Root, "assets", rel),
            Path.Combine(App.RootDirectory, rel)
        };
        if (rel.StartsWith("assets/", StringComparison.Ordinal))
            candidates.Add(Path.Combine(_store.Root, rel["assets/".Length..]));
        return candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p)) ?? "";
    }

    private PortraitCalibration Snapshot() => new()
    {
        Calibrated = true,
        SourceWidth = _srcW,
        SourceHeight = _srcH,
        FlipHorizontal = FlipHorizontal,
        FlipVertical = FlipVertical,
        Rotation = Rotation,
        Scale = Scale,
        OffsetX = OffsetX,
        OffsetY = OffsetY,
        FootLine = FootLine
    };

    /// <summary>
    /// 与 <c>MainGameViewModel.UpdateSpriteTransform</c> 同一套换算，只是舞台换成预览框：
    /// AspectFit 基准 → 标定缩放 → 背景 AspectFill 映射出锚点 → 脚底线压到锚点 → 叠加偏移。
    /// </summary>
    private void Recalc()
    {
        var cal = Snapshot();
        var (effW, effH) = PortraitCalibrationMath.EffectiveSize(_srcW, _srcH, cal);
        var sw = effW > 0 ? effW : 400.0;
        var sh = effH > 0 ? effH : 640.0;

        var stageW = FrameW;
        var stageH = FrameH;
        var baseW = sw * Math.Min(stageW / sw, stageH / sh);
        var baseH = sh * Math.Min(stageW / sw, stageH / sh);

        PreviewW = baseW;
        PreviewH = baseH;
        PreviewRotation = Rotation;
        PreviewScaleX = (FlipHorizontal ? -1 : 1) * Scale;
        PreviewScaleY = (FlipVertical ? -1 : 1) * Scale;

        // 背景 AspectFill 在舞台上实际铺成多大（游戏用同一式子）
        var iw = _bgSize.W > 0 ? _bgSize.W : 16.0;
        var ih = _bgSize.H > 0 ? _bgSize.H : 9.0;
        var s = Math.Max(stageW / iw, stageH / ih);
        var dw = iw * s;
        var dh = ih * s;
        var ox = (stageW - dw) / 2;
        var oy = (stageH - dh) / 2;

        // 落脚位置：有可用标定就按标定解，否则与游戏一样居中显示
        double anchorXpx, anchorYpx, scale;
        if (_placement is not null && PlacementMath.Validate(_placement).Ok)
        {
            var desired = CenterDesired();
            var (anchor, k) = PlacementMath.ResolveAnchor(_placement, desired);
            anchorXpx = ox + anchor.X * dw;
            anchorYpx = oy + anchor.Y * dh;
            var visibleH = stageH * _placement.RefHeightRatio * k;
            scale = Math.Clamp(visibleH / (baseH <= 0 ? 1 : baseH), 0.02, 8.0);
        }
        else
        {
            anchorXpx = ox + 0.5 * dw;
            anchorYpx = oy + DefaultAnchorY * dh;
            scale = 1.0;
        }

        var finalScale = Math.Clamp(scale * Scale, 0.02, 8.0);
        // 与游戏一致：脚底线要压在锚点上，所以按显示高度下移 (1 - FootLine)
        PreviewY = anchorYpx - stageH / 2 - baseH * finalScale / 2
                   + baseH * finalScale * (1 - FootLine)
                   + OffsetY * stageH;
        PreviewX = anchorXpx - stageW / 2 + OffsetX * stageW;

        GroundLineY = Math.Clamp(anchorYpx, 0, stageH);
        PreviewScaleX = (FlipHorizontal ? -1 : 1) * finalScale;
        PreviewScaleY = (FlipVertical ? -1 : 1) * finalScale;

        Status = $"有效尺寸 {effW:0}×{effH:0} · 显示 {baseW * finalScale:0}×{baseH * finalScale:0} · 缩放 {finalScale:0.00}";
        var problems = PortraitCalibrationMath.Validate(cal, _srcW, _srcH);
        ProblemsText = problems.Count == 0 ? "" : string.Join("\n", problems.Select(p => "· " + p));
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 预览按「居中」落脚：游戏里 left/center/right 是运行时立绘位置设置决定的，
    /// 标定页不碰那个选项，所以固定用居中，与游戏默认值一致。
    /// </summary>
    private static NormPoint CenterDesired() => new(0.5, DefaultAnchorY);

    private bool CanSave() => PortraitCalibrationMath.Validate(Snapshot(), _srcW, _srcH).Count == 0;

    [RelayCommand] private void ToggleFlipHorizontal() => FlipHorizontal = !FlipHorizontal;
    [RelayCommand] private void ToggleFlipVertical() => FlipVertical = !FlipVertical;
    [RelayCommand] private void RotateRight() => Rotation = (Rotation + 90) % 360;
    [RelayCommand] private void ScaleUp() => Scale = Math.Min(PortraitCalibrationMath.MaxScale, Math.Round(Scale + 0.05, 2));
    [RelayCommand] private void ScaleDown() => Scale = Math.Max(PortraitCalibrationMath.MinScale, Math.Round(Scale - 0.05, 2));
    [RelayCommand] private void NudgeLeft() => OffsetX = Math.Round(OffsetX - 0.01, 3);
    [RelayCommand] private void NudgeRight() => OffsetX = Math.Round(OffsetX + 0.01, 3);
    [RelayCommand] private void NudgeUp() => OffsetY = Math.Round(OffsetY - 0.01, 3);
    [RelayCommand] private void NudgeDown() => OffsetY = Math.Round(OffsetY + 0.01, 3);
    /// <summary>脚底线往上抬：素材底部有留白、脚在画布内部时用。</summary>
    [RelayCommand] private void FootUp() => FootLine = Math.Round(Math.Max(0.5, FootLine - 0.01), 3);
    [RelayCommand] private void FootDown() => FootLine = Math.Round(Math.Min(1.0, FootLine + 0.01), 3);

    [RelayCommand]
    private void Reset()
    {
        FlipHorizontal = false;
        FlipVertical = false;
        Rotation = 0;
        Scale = 1.0;
        OffsetX = 0;
        OffsetY = 0;
        FootLine = 1.0;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        await _cal.SaveAsync(_key, Snapshot());
        Status = "已保存：这张立绘现在按标定结果上场。";
        Toast.Success("已保存。回到游戏即可看到效果。");
    }

    [RelayCommand]
    private async Task ClearCalibration()
    {
        await _cal.ClearAsync(_key);
        Toast.Info("已清除这份标定，该素材恢复为未标定状态。");
        Reset();
    }

    /// <summary>
    /// 规范化：真的重采样出一个 1080×1920 的标准画布文件，并把 SpriteMap 指过去。
    /// 与「标定」不同，这一步改的是文件本身——素材歪得离谱时用它更省事。
    /// </summary>
    [RelayCommand]
    private async Task Normalize()
    {
        try
        {
            if (string.IsNullOrEmpty(AbsPath) || !File.Exists(AbsPath))
            {
                NormalizeNote = "找不到源文件，无法重采样。";
                return;
            }
            var (rel, message) = await _normalizer.NormalizeAsync(AbsPath, _key, FlipHorizontal, Rotation);
            NormalizeNote = message;
            if (rel is null) return;

            var (ok, replaceMsg) = await _library.ReplaceSpritePathAsync(_key, rel);
            NormalizeNote = message + " " + replaceMsg;
            if (ok)
            {
                _key = rel;                    // 之后的标定都记在新文件上
                await _cal.ClearAsync(rel);    // 标准画布不再需要旧标定
                Reset();
                await LoadAsync();
            }
            Toast.Success(NormalizeNote);
        }
        catch (Exception ex)
        {
            App.WriteLog("PortraitCalibrationVM.Normalize -> " + ex);
            NormalizeNote = "规范化失败：" + ex.Message;
        }
    }
}
