using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 头像裁剪。取景框是正方形，图按 cover 铺满后再乘 <see cref="Zoom"/>；
/// 拖动只改图心相对框心的位移（像素），落盘时换成「框边长的比例」交给服务。
///
/// 归一化偏移不按图宽算、按框边长算：视图这边只认框，服务那边只认 min(宽,高)，
/// 两边各自乘自己的尺度，预览和成品才会落在同一块脸上。
/// </summary>
public sealed partial class AvatarCropViewModel : ObservableObject
{
    private readonly AvatarCropService _crop;
    private readonly CharacterLibrary _chars;

    private string _characterId = "";
    private string _sourcePath = "";
    private string? _previewPath;
    private int _pxW, _pxH;
    private double _frameSide;
    private bool _loaded;
    private bool _closed;

    private double _pinchStartZoom = 1;
    private double _pinchStartOffsetX, _pinchStartOffsetY;
    private double _pinchOriginX, _pinchOriginY;

    [ObservableProperty] private string _title = "裁剪头像";
    [ObservableProperty] private string _characterName = "TA";
    [ObservableProperty] private string _sourceInfo = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasImage;
    [ObservableProperty] private ImageSource? _previewSource;

    [ObservableProperty] private double _zoom = 1;
    [ObservableProperty] private string _zoomLabel = "1.0×";
    [ObservableProperty] private double _offsetX, _offsetY;
    [ObservableProperty] private double _baseWidth = 1, _baseHeight = 1;

    [ObservableProperty] private bool _showGrid = true;
    [ObservableProperty] private string _gridLabel = "隐藏网格";

    public AvatarCropViewModel(AvatarCropService crop, CharacterLibrary chars)
    {
        _crop = crop;
        _chars = chars;
    }

    public bool CanConfirm => HasImage && !IsBusy;

    public async Task LoadAsync(string characterId, string sourcePath)
    {
        if (_loaded) return;
        _loaded = true;
        _characterId = characterId ?? "";
        _sourcePath = sourcePath ?? "";

        IsBusy = true;
        Status = "正在读取这张图…";
        try
        {
            var data = (await _chars.ListAsync()).FirstOrDefault(c => c.Profile.Id == _characterId);
            CharacterName = data?.Profile.Name ?? "TA";
            Title = $"给「{CharacterName}」裁一张头像";

            var preview = await _crop.CreatePreviewAsync(_sourcePath);
            if (preview is null)
            {
                Status = "这张图打不开。换一张 png / jpg / webp 再试。";
                return;
            }

            _previewPath = preview.FilePath;
            _pxW = preview.Width;
            _pxH = preview.Height;
            PreviewSource = ImageSource.FromFile(preview.FilePath);
            SourceInfo = $"原图 {preview.Width}×{preview.Height} · 成品正方形";
            HasImage = true;
            RebuildBase();
            ResetView();
            Status = "拖动取景 · 滚轮 / 滑杆 / 双指缩放";
        }
        catch (Exception ex)
        {
            App.WriteLog("AvatarCrop.Load -> " + ex);
            Status = "读图失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>页面量出可用边长后调用：取景框变，cover 的底与可拖范围跟着变。</summary>
    public void SetFrameSize(double side)
    {
        if (side <= 0 || Math.Abs(side - _frameSide) < 0.5) return;
        _frameSide = side;
        RebuildBase();
        ClampOffsets();
    }

    private void RebuildBase()
    {
        if (_pxW <= 0 || _pxH <= 0 || _frameSide <= 0) return;
        var fit = _frameSide / Math.Min(_pxW, _pxH);
        BaseWidth = _pxW * fit;
        BaseHeight = _pxH * fit;
    }

    [RelayCommand]
    private void ResetView()
    {
        Zoom = 1;
        OffsetX = 0;
        OffsetY = 0;
        ZoomLabel = "1.0×";
        Status = "已回到整张图";
    }

    [RelayCommand]
    private void ToggleGrid()
    {
        ShowGrid = !ShowGrid;
        GridLabel = ShowGrid ? "隐藏网格" : "显示网格";
    }

    // ── 手势（页面转发，坐标都是框内像素）────────────────────────────────────

    public void PanBy(double dx, double dy)
    {
        if (!HasImage) return;
        OffsetX += dx;
        OffsetY += dy;
        ClampOffsets();
    }

    /// <summary>滚轮 / 滑杆：锚点在框内，缩放时锚点下面的那一块脸不动。</summary>
    public void ZoomBy(double factor, double anchorX, double anchorY)
    {
        if (!HasImage || _frameSide <= 0) return;
        var target = Math.Clamp(Zoom * factor, 1, 6);
        if (Math.Abs(target - Zoom) < 0.0001) return;

        var ax = anchorX - _frameSide / 2;
        var ay = anchorY - _frameSide / 2;
        var k = target / Zoom;
        OffsetX = ax - (ax - OffsetX) * k;
        OffsetY = ay - (ay - OffsetY) * k;
        Zoom = target;
        ZoomLabel = $"{target:0.0}×";
        ClampOffsets();
    }

    public void BeginPinch(double originX, double originY)
    {
        _pinchStartZoom = Zoom;
        _pinchStartOffsetX = OffsetX;
        _pinchStartOffsetY = OffsetY;
        _pinchOriginX = originX;
        _pinchOriginY = originY;
    }

    public void UpdatePinch(double relativeScale)
    {
        if (!HasImage || _frameSide <= 0 || _pinchStartZoom <= 0) return;
        var target = Math.Clamp(_pinchStartZoom * relativeScale, 1, 6);
        var ax = _frameSide * _pinchOriginX - _frameSide / 2;
        var ay = _frameSide * _pinchOriginY - _frameSide / 2;
        var k = target / _pinchStartZoom;
        OffsetX = ax - (ax - _pinchStartOffsetX) * k;
        OffsetY = ay - (ay - _pinchStartOffsetY) * k;
        Zoom = target;
        ZoomLabel = $"{target:0.0}×";
        ClampOffsets();
    }

    private void ClampOffsets()
    {
        if (_frameSide <= 0) return;
        var maxX = Math.Max(0, (BaseWidth * Zoom - _frameSide) / 2);
        var maxY = Math.Max(0, (BaseHeight * Zoom - _frameSide) / 2);
        OffsetX = Math.Clamp(OffsetX, -maxX, maxX);
        OffsetY = Math.Clamp(OffsetY, -maxY, maxY);
    }

    partial void OnZoomChanged(double value)
    {
        // Slider 直接写 Zoom：超过框的位移要当场收回来，不能留到确认时再处理
        var v = Math.Clamp(value, 1, 6);
        if (Math.Abs(v - value) > 0.0001)
        {
            Zoom = v;
            return;
        }
        ZoomLabel = $"{v:0.0}×";
        ClampOffsets();
    }

    partial void OnHasImageChanged(bool value) => OnPropertyChanged(nameof(CanConfirm));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanConfirm));

    // ── 收尾 ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Confirm()
    {
        if (!CanConfirm || _closed) return;
        if (_frameSide <= 0)
        {
            Status = "取景框还没量好，稍等一下再确认。";
            return;
        }
        IsBusy = true;
        Status = "正在生成头像…";
        try
        {
            // 手机不必存 1024：头像在界面上最大也就一百多点，边长小一档省盘也省内存
            var phone = DeviceInfo.Idiom == DeviceIdiom.Phone || DeviceInfo.Idiom == DeviceIdiom.Tablet;
            var edge = phone ? AvatarCropService.DefaultOutputEdge : 768;
            var req = new AvatarCropRequest(
                _characterId, _sourcePath, Zoom,
                OffsetX / _frameSide, OffsetY / _frameSide, edge);
            var result = await _crop.CropAsync(req);
            if (!result.Ok)
            {
                Status = result.Message;
                return;
            }

            _closed = true;
            _crop.Finish(result.AvatarRelPath);
            await Shell.Current.GoToAsync($"..?avatar={Uri.EscapeDataString(result.AvatarRelPath)}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Cancel()
    {
        if (_closed) return;
        _closed = true;
        _crop.Finish(null);
        if (Shell.Current is not null) await Shell.Current.GoToAsync("..");
    }

    /// <summary>页面离开时把临时预览清掉；<paramref name="closed"/> 为 false 说明是被系统顶掉的，也要收尾。</summary>
    public void Cleanup()
    {
        _crop.ReleasePreview(_previewPath);
        _previewPath = null;
        if (!_closed)
        {
            _closed = true;
            _crop.Finish(null);
        }
    }
}
