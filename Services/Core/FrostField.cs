using SkiaSharp;

namespace WarmAsBefore.Services;

/// <summary>
/// 页面坐标 → 糊底像素的映射。糊底是一张按 **UniformToFill** 铺满窗口的图
/// （见 <see cref="AppBackgroundService"/> 的 ImageBrush），所以除了比例还有居中裁切留下的负偏移：
///   Scale    每 1 图像像素等于多少逻辑单位；
///   OffsetX/OffsetY  图像左上角在页面坐标里的位置（通常是负数，图比窗口宽/高）。
/// </summary>
public readonly record struct FrostMap(double Scale, double OffsetX, double OffsetY)
{
    public bool IsUsable => Scale > 1e-6;

    /// <summary>页面逻辑坐标 → 图像像素坐标。映射不可用时返回 <see cref="float.NaN"/>。</summary>
    public float X(double pageX) => IsUsable ? (float)((pageX - OffsetX) / Scale) : float.NaN;
    public float Y(double pageY) => IsUsable ? (float)((pageY - OffsetY) / Scale) : float.NaN;
    public float W(double pageW) => IsUsable ? (float)(pageW / Scale) : float.NaN;
    public float H(double pageH) => IsUsable ? (float)(pageH / Scale) : float.NaN;
}

/// <summary>
/// 糊底的**取样场**：把 <see cref="FrostImageService"/> 落盘的那张 PNG 解码一次，留在内存里给
/// 玻璃面按位置裁着用。
///
/// 为什么不让每个控件自己读文件：一屏上可能有十几块玻璃，每块各解码一张 960×1200 的 PNG
/// 就是十几份位图与十几次磁盘 IO。糊底只有一张，全站共用这一份。
///
/// 解码在后台线程上做一次，完成时 <see cref="Version"/> 加一 —— 玻璃面把版本号写进自己的缓存键，
/// 于是换壁纸/换主题后下一帧自然换成新的取样，不需要每个控件各自订阅事件。
/// </summary>
public static class FrostField
{
    private static readonly object Gate = new();

    private static SKBitmap? _bitmap;
    private static string _path = "";
    private static bool _loading;
    private static bool _hooked;
    private static int _version;

    /// <summary>取样内容变一次就加一。玻璃缓存键里必须带它。</summary>
    public static int Version { get { lock (Gate) return _version; } }

    /// <summary>解好码的糊底（只读共享，调用方**不许** Dispose）。没有糊底时返回 null。</summary>
    public static SKBitmap? Bitmap { get { lock (Gate) return _bitmap; } }

    /// <summary>
    /// 换了一张糊底（解码完成）。**在后台线程上触发** —— 订阅方自己切回 UI 线程。
    /// 玻璃面靠它把手里那张旧取样作废：版本号进了缓存键，但没人 Invalidate 的话，
    /// GraphicsView 根本不会再画一次，玻璃会一直停在上一张糊底上。
    /// </summary>
    public static event Action? Changed;

    /// <summary>挂上订阅并尝试解码当前糊底。幂等，任何线程都能调。</summary>
    public static void Ensure()
    {
        lock (Gate)
        {
            if (_hooked) return;
            _hooked = true;
        }
        FrostImageService.Ready += (_, _) => Load(FrostImageService.FilePath);
        Load(FrostImageService.FilePath);
    }

    /// <summary>页面尺寸 → 糊底的铺法。页面（或糊底）尺寸非法时返回不可用的映射。</summary>
    public static FrostMap Map(double pageWidth, double pageHeight)
    {
        var bmp = Bitmap;
        if (bmp is null || bmp.Width <= 0 || bmp.Height <= 0) return default;
        if (!double.IsFinite(pageWidth) || !double.IsFinite(pageHeight)) return default;
        if (pageWidth <= 0 || pageHeight <= 0) return default;

        // UniformToFill：取两轴里大的那个比例，多出来的一截被居中裁掉。
        var scale = Math.Max(pageWidth / bmp.Width, pageHeight / bmp.Height);
        return new FrostMap(scale, (pageWidth - bmp.Width * scale) * 0.5, (pageHeight - bmp.Height * scale) * 0.5);
    }

    /// <summary>
    /// 解码一张糊底。同一个路径已经加载就不再解；正在解时不重复发起。
    /// 失败只写日志 —— 上层的兜底是「画渐变」，不是「抛出去」。
    /// </summary>
    private static void Load(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        lock (Gate)
        {
            if (_loading || (path == _path && _bitmap is not null)) return;
            _loading = true;
        }
        _ = Task.Run(() =>
        {
            SKBitmap? bmp = null;
            try
            {
                using var codec = SKCodec.Create(path);
                if (codec is not null)
                    bmp = SKBitmap.Decode(codec, new SKImageInfo(
                        codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
                bmp ??= SKBitmap.Decode(path);
            }
            catch (Exception ex)
            {
                App.WriteLog("FrostField.Load -> " + ex.Message);
            }

            lock (Gate)
            {
                _loading = false;
                if (bmp is null) return;
                if (path == _path && _bitmap is not null) { bmp.Dispose(); return; }   // 期间别的调用已经装好了同一张
                var old = _bitmap;
                _bitmap = bmp;
                _path = path;
                _version++;
                old?.Dispose();
            }
            try { Changed?.Invoke(); }
            catch (Exception ex) { App.WriteLog("FrostField.Changed -> " + ex.Message); }
        });
    }
}
