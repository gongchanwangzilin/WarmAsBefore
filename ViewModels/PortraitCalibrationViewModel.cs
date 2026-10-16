using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 立绘标定：把一份素材映射到 1080×1920 的逻辑画布上。
///
/// 预览与游戏用的是同一套算法，所以在这里调好，回到游戏里就是看到的样子。
/// 校验由 <see cref="PortraitCalibrationMath.Validate"/> 统一给出，不合法不允许保存。
/// </summary>
public sealed partial class PortraitCalibrationViewModel : ObservableObject, IQueryAttributable
{
    private readonly StorageProvider _store;
    private readonly PortraitCalibrationService _cal;

    private string _key = "";
    private int _srcW, _srcH;
    private bool _loading;

    public PortraitCalibrationViewModel(StorageProvider store, PortraitCalibrationService cal)
    {
        _store = store;
        _cal = cal;
    }

    /// <summary>预览框的像素尺寸（宽高比固定 9:16，与逻辑画布一致）。</summary>
    private const double FrameW = 270;
    private const double FrameH = FrameW / (PortraitCanvas.Width / PortraitCanvas.Height);

    [ObservableProperty] private string _absPath = "";
    [ObservableProperty] private string _title = "立绘标定";

    public string SourceLabel => _srcW > 0 ? $"{_srcW}×{_srcH}" : "读取中…";

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

    // ---- 预览变换（与游戏同算法） ----
    [ObservableProperty] private double _previewW = 100;
    [ObservableProperty] private double _previewH = 178;
    [ObservableProperty] private double _previewX;
    [ObservableProperty] private double _previewY;
    [ObservableProperty] private double _previewScaleX = 1;
    [ObservableProperty] private double _previewScaleY = 1;
    [ObservableProperty] private double _previewRotation;
    /// <summary>裁剪后的可视区域在预览里的位置（用于把「被裁掉的部分」也画出来做参照）。</summary>
    [ObservableProperty] private string _status = "";

    [ObservableProperty] private string _problemsText = "";
    partial void OnProblemsTextChanged(string value) => OnPropertyChanged(nameof(HasProblems));
    public bool HasProblems => !string.IsNullOrEmpty(ProblemsText);

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
                // 首次进入：按标准画布给出起点——比例合规的素材直接可用，不合规的从 1.0 开始调
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

    private void Recalc()
    {
        var (ew, eh) = PortraitCalibrationMath.EffectiveSize(_srcW, _srcH, Snapshot());
        var (bw, bh) = PortraitCalibrationMath.CanvasBox(_srcW, _srcH, Snapshot());
        var fit = Math.Min(FrameW / (ew <= 0 ? 1 : ew), FrameH / (eh <= 0 ? 1 : eh));
        var baseW = (ew <= 0 ? 1 : ew) * fit;
        var baseH = (eh <= 0 ? 1 : eh) * fit;

        PreviewW = baseW;
        PreviewH = baseH;
        // ScaleX/ScaleY 直接吸收翻转与缩放（MAUI 里同时绑 Scale 与 ScaleX 会互相覆盖）
        PreviewScaleX = (FlipHorizontal ? -1 : 1) * Scale;
        PreviewScaleY = (FlipVertical ? -1 : 1) * Scale;
        PreviewRotation = Rotation;
        PreviewX = OffsetX * FrameW;
        // 与游戏一致：脚底线要压在基准线上，所以按「显示高度」下移 (1 - FootLine)
        var shownH = baseH * Scale;
        PreviewY = OffsetY * FrameH + shownH * (1 - FootLine);
        Status = $"有效尺寸 {ew:0}×{eh:0} · 显示 {baseW * Scale:0}×{shownH:0}（画布 {bw:0}×{bh:0}）";

        var problems = PortraitCalibrationMath.Validate(Snapshot(), _srcW, _srcH);
        ProblemsText = problems.Count == 0 ? "" : string.Join("\n", problems.Select(p => "· " + p));
        SaveCommand.NotifyCanExecuteChanged();
    }

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
        await Shell.Current.DisplayAlert("立绘标定", "已保存。回到游戏即可看到效果。", "好");
    }

    [RelayCommand]
    private async Task ClearCalibration()
    {
        await _cal.ClearAsync(_key);
        await Shell.Current.DisplayAlert("立绘标定", "已清除这份标定，该素材恢复为未标定状态。", "好");
        Reset();
    }
}
