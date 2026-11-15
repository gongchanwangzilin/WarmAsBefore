using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Graphics.Platform;
using SkiaSharp;
using WarmAsBefore.Helpers;
using WarmAsBefore.Services;

namespace WarmAsBefore.Controls;

/// <summary>
/// 能放 GIF / 动图 WebP 的 Image。
///
/// 解码走 SkiaSharp 的 SKCodec：动图先在后台一次性预解码全部帧（crop/缩放烘焙进帧图，
/// 帧数 / 累计字节双上限，超限回落静态首帧），之后播放循环每帧只对 GraphicsView
/// 换引用 + Invalidate —— 零大对象堆分配、零 Measure 失效，不再逐帧换 Image.Source。
/// 文件只有一帧（或解码失败）时退回普通 Image，不额外占一颗定时器。
/// 平铺（tile）在 MAUI 里没有 TileBrush，这里改用 GraphicsView 反复画同一张图；
/// 动图的 tile 帧同样走预解码 + 换引用，Windows 与 Android 都是同一套 Skia 绘制路径。
///
/// 页面消失 / 控件隐藏 / 从树上卸下都会停表；重新出现时从第一帧重播，
/// 不偷偷在后台烧 CPU。预解码帧数组由 UI 线程统一持有/释放，drawable 只借引用；
/// 所有 SKBitmap / SKCodec / SKImage 都在帧处理完立刻释放。
/// </summary>
public sealed class AnimatedImage : ContentView
{
    private const int MaxFrameEdge = 2560;
    private const int MaxLoopFrames = 36;                   // 预解码帧数硬上限：超过就放弃循环，回落静态首帧
    private const long MaxFrameBytes = 192L * 1024 * 1024;  // 预解码累计字节硬上限：同上
    private static readonly string[] VideoExts = { ".mp4", ".mov", ".m4v", ".webm" };

    private readonly Image _image;
    private readonly GraphicsView _tileView;
    private readonly TileDrawable _tileDrawable = new();
    private readonly GraphicsView _playerView;              // 动图帧播放器：播放期间只换引用 + Invalidate，不碰布局
    private readonly FramePlayerDrawable _playerDrawable = new();
    private FrameSet? _frames;                              // 预解码帧数组：只在 UI 线程上挂账/释放
    private CancellationTokenSource? _playCts;
    private int _loadToken;
    private bool _loaded;
    private bool _paused;
    private volatile bool _playingAllowed;
    private bool _suppressReload;
    private Page? _page;

    public AnimatedImage()
    {
        _image = new Image
        {
            Aspect = Aspect.AspectFill,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        _tileView = new GraphicsView
        {
            IsVisible = false,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Drawable = _tileDrawable
        };
        _playerView = new GraphicsView
        {
            IsVisible = false,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Drawable = _playerDrawable
        };
        Content = new Grid
        {
            IsClippedToBounds = true,
            Children = { _image, _tileView, _playerView }
        };

        Loaded += (_, _) =>
        {
            _loaded = true;
            HookPage();
            Play();
        };
        Unloaded += (_, _) =>
        {
            _loaded = false;
            _paused = true;
            _playingAllowed = false;
            StopLoop();
            _image.Source = null;
            _tileDrawable.SetImage(null);
            ReleaseFrames();          // 预解码帧数组连同两个 drawable 里的借用引用一起收走
            _playerView.IsVisible = false;
            UnhookPage();
        };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(IsVisible)) return;
            if (IsVisible) Play();
            else Pause();
        };
    }

    /// <summary>背景文件绝对路径。空 = 什么都不画。</summary>
    public static readonly BindableProperty PathProperty = BindableProperty.Create(
        nameof(Path), typeof(string), typeof(AnimatedImage), "",
        propertyChanged: OnRenderInputChanged);

    public string Path
    {
        get => (string)GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    /// <summary>fill / fit / stretch / tile / crop，语义与 <see cref="CharacterBackgroundService"/> 一致。</summary>
    public static readonly BindableProperty FitProperty = BindableProperty.Create(
        nameof(Fit), typeof(string), typeof(AnimatedImage), "fill",
        propertyChanged: OnRenderInputChanged);

    public string Fit
    {
        get => (string)GetValue(FitProperty);
        set => SetValue(FitProperty, value);
    }

    public static readonly BindableProperty CropXProperty = BindableProperty.Create(
        nameof(CropX), typeof(double), typeof(AnimatedImage), 0d,
        propertyChanged: OnRenderInputChanged);

    public double CropX
    {
        get => (double)GetValue(CropXProperty);
        set => SetValue(CropXProperty, value);
    }

    public static readonly BindableProperty CropYProperty = BindableProperty.Create(
        nameof(CropY), typeof(double), typeof(AnimatedImage), 0d,
        propertyChanged: OnRenderInputChanged);

    public double CropY
    {
        get => (double)GetValue(CropYProperty);
        set => SetValue(CropYProperty, value);
    }

    public static readonly BindableProperty CropWProperty = BindableProperty.Create(
        nameof(CropW), typeof(double), typeof(AnimatedImage), 1d,
        propertyChanged: OnRenderInputChanged);

    public double CropW
    {
        get => (double)GetValue(CropWProperty);
        set => SetValue(CropWProperty, value);
    }

    public static readonly BindableProperty CropHProperty = BindableProperty.Create(
        nameof(CropH), typeof(double), typeof(AnimatedImage), 1d,
        propertyChanged: OnRenderInputChanged);

    public double CropH
    {
        get => (double)GetValue(CropHProperty);
        set => SetValue(CropHProperty, value);
    }

    /// <summary>GIF/WebP 是否循环；false 时播完停在最后一帧。</summary>
    public static readonly BindableProperty IsLoopingProperty = BindableProperty.Create(
        nameof(IsLooping), typeof(bool), typeof(AnimatedImage), true);

    public bool IsLooping
    {
        get => (bool)GetValue(IsLoopingProperty);
        set => SetValue(IsLoopingProperty, value);
    }

    /// <summary>给外层交叉淡入层设置缩放方式（tile 时由 GraphicsView 接管，这里不影响）。</summary>
    public Aspect ImageAspect
    {
        get => _image.Aspect;
        set => _image.Aspect = value;
    }

    /// <summary>一次设置路径/适配/裁剪并只重载一次（避免四条属性各触发一遍解码）。</summary>
    public void Configure(string path, string fit, double cropX, double cropY, double cropW, double cropH)
    {
        _suppressReload = true;
        Path = path;
        Fit = CharacterBackgroundService.NormalizeFit(fit);
        CropX = cropX;
        CropY = cropY;
        CropW = cropW;
        CropH = cropH;
        _suppressReload = false;
        Reload();
    }

    public void Play()
    {
        _paused = false;
        _playingAllowed = _loaded && IsVisible;
        if (_playingAllowed) Reload();
    }

    /// <summary>停表但保留当前帧（页面离场 / 控件隐藏时用）。</summary>
    public void Pause()
    {
        _paused = true;
        _playingAllowed = false;
        StopLoop();
    }

    private static void OnRenderInputChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is AnimatedImage image && !image._suppressReload)
            image.Reload();
    }

    private void Reload()
    {
        StopLoop();
        _loadToken++;
        var token = _loadToken;
        _image.Source = null;
        _tileDrawable.SetImage(null);   // 先按老路径清静态 tile 图（借用帧不会被它误释放）
        ReleaseFrames();                // 再收预解码帧数组 + 两个 drawable 里的借用引用
        _playerView.IsVisible = false;  // 确认要播动图时再翻起来（见 RunFramesAsync 的交接）

        var path = Path;
        var fit = CharacterBackgroundService.NormalizeFit(Fit);
        var isTile = fit == "tile";
        _image.IsVisible = !isTile;
        _tileView.IsVisible = isTile;
        _playerDrawable.Fit = fit;      // fit 在装载时定一次，播放期间不变
        _image.Aspect = fit switch
        {
            "fit" => Aspect.AspectFit,
            "stretch" => Aspect.Fill,
            _ => Aspect.AspectFill
        };

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        if (VideoExts.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant())) return; // 视频由页面上的 MediaElement 接
        _ = LoadAsync(path, fit, token);
    }

    private async Task LoadAsync(string path, string fit, int token)
    {
        var frameCount = await Task.Run(() => ProbeFrameCount(path));
        if (token != _loadToken) return;

        if (frameCount <= 1)
        {
            await RenderStaticAsync(path, fit, token);
            return;
        }

        if (!(_loaded && IsVisible && !_paused))
        {
            // 停着的时候至少把第一帧摆上，别留一块空白。
            await RenderFirstFrameAsync(path, fit, token);
            return;
        }
        StartLoop(path, fit, token);
    }

    private void StartLoop(string path, string fit, int token)
    {
        StopLoop();
        var cts = new CancellationTokenSource();
        _playCts = cts;
        _playingAllowed = true;
        _ = Task.Run(() => RunFramesAsync(path, fit, token, cts.Token));
    }

    private void StopLoop()
    {
        _playCts?.Cancel();
        _playCts?.Dispose();
        _playCts = null;
    }

    private async Task RenderStaticAsync(string path, string fit, int token)
    {
        try
        {
            if (fit is not ("crop" or "tile"))
            {
                if (token == _loadToken)
                    await Ui.PostAsync(() =>
                    {
                        if (token == _loadToken) _image.Source = ImageSource.FromFile(path);
                        return Task.CompletedTask;
                    });
                return;
            }

            var bytes = await Task.Run(() =>
            {
                using var bmp = SKBitmap.Decode(path);
                return bmp is null ? null : EncodeFrame(bmp, fit, CropX, CropY, CropW, CropH);
            });
            if (bytes is null || token != _loadToken) return;
            await Ui.PostAsync(() => { ApplyFrame(bytes, token); return Task.CompletedTask; });
        }
        catch (Exception ex)
        {
            App.WriteLog("AnimatedImage.RenderStatic -> " + ex.Message);
        }
    }

    private async Task RenderFirstFrameAsync(string path, string fit, int token)
    {
        var bytes = await Task.Run(() =>
        {
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream);
            if (codec is null) return null;
            using var bmp = DecodeFrame(codec, 0, new SKBitmap(
                new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul)));
            return bmp is null ? null : EncodeFrame(bmp, fit, CropX, CropY, CropW, CropH);
        });
        if (bytes is null || token != _loadToken) return;
        await Ui.PostAsync(() => { ApplyFrame(bytes, token); return Task.CompletedTask; });
    }

    private async Task RunFramesAsync(string path, string fit, int token, CancellationToken cancel)
    {
        try
        {
            // 本方法整段跑在线程池上（StartLoop 里 Task.Run 进来）：
            // 先一次性预解码全部帧，之后播放循环每帧只剩「换引用 + Invalidate + Task.Delay」。
            var set = PredecodeFrames(path, fit, token, cancel);

            if (set is null || !set.Complete)
            {
                // 帧数 / 累计字节超预算或中途失效：残阵送回 UI 线程清账
                //（drawable 里可能还挂着第 0 帧的借用引用），再回落静态首帧。
                if (set is not null)
                {
                    Ui.Post(() =>
                    {
                        if (token == _loadToken)
                        {
                            _playerDrawable.SetFrame(null);
                            _tileDrawable.SetFrame(null);
                            if (!_tileView.IsVisible)
                            {
                                _playerView.IsVisible = false;
                                _image.IsVisible = true;
                            }
                        }
                        set.Dispose();
                    });
                }
                await RenderFirstFrameAsync(path, fit, token);
                return;
            }

            var frames = set.Frames;
            var delays = set.Delays;
            var count = frames.Length;
            var isTile = fit == "tile";

            // 交接：帧数组挂到 UI 线程统一持有/释放，第 0 帧立即上屏
            //（预解码途中可能已先摆过一次，这里幂等重设）。
            Ui.Post(() =>
            {
                if (token != _loadToken || !_playingAllowed) { set.Dispose(); return; }
                _frames = set;
                if (isTile)
                {
                    _tileDrawable.SetFrame(frames[0]);
                    _tileView.Invalidate();
                }
                else
                {
                    _image.IsVisible = false;
                    _playerView.IsVisible = true;
                    _playerDrawable.SetFrame(frames[0]);
                    _playerView.Invalidate();
                }
            });

            // 播放循环：按原帧延迟节奏推进（clamp 已在预解码时做完），
            // 每帧只对 drawable 换引用 + Invalidate —— 换引用不触碰布局，杜绝 Measure 失效。
            var index = 0;
            while (!cancel.IsCancellationRequested && token == _loadToken && _playingAllowed)
            {
                if (!IsLooping && index == count - 1) break; // 播完停在最后一帧
                var delayMs = delays[index];
                index = (index + 1) % count;
                await Task.Delay(delayMs, cancel);

                var frame = frames[index];
                Ui.Post(() =>
                {
                    if (token != _loadToken || !_playingAllowed) return;
                    if (isTile)
                    {
                        _tileDrawable.SetFrame(frame);
                        _tileView.Invalidate();
                    }
                    else
                    {
                        _playerDrawable.SetFrame(frame);
                        _playerView.Invalidate();
                    }
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.WriteLog("AnimatedImage.RunFrames -> " + ex.Message);
        }
    }

    /// <summary>
    /// 后台一次性预解码全部帧：逐帧 GetPixels → EncodeFrame（crop / ≤2560 缩放烘焙）→
    /// PlatformImage。返回 null 仅当连 codec 都打不开；Complete = false 表示超预算或中途失效，
    /// 已解码的残余帧仍带回来，由调用方送回 UI 线程统一释放。
    /// </summary>
    private FrameSet? PredecodeFrames(string path, string fit, int token, CancellationToken cancel)
    {
        FrameSet? set = null;
        try
        {
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream);
            if (codec is null || codec.FrameCount <= 1) return null;

            var count = codec.FrameCount;
            set = new FrameSet(count);
            if (count > MaxLoopFrames) return set;

            var info = codec.Info;
            using var scratch = new SKBitmap(new SKImageInfo(
                info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            var frameInfo = codec.FrameInfo;
            var frames = set.Frames;
            var delays = set.Delays;
            long totalBytes = 0;

            for (var i = 0; i < count; i++)
            {
                if (cancel.IsCancellationRequested || token != _loadToken) return set;

                var options = i == 0 ? new SKCodecOptions(0) : new SKCodecOptions(i, i - 1);
                var result = codec.GetPixels(scratch.Info, scratch.GetPixels(), options);
                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) return set;

                var bytes = EncodeFrame(scratch, fit, CropX, CropY, CropW, CropH);
                if (bytes is null) return set;

                totalBytes += bytes.Length;
                if (totalBytes > MaxFrameBytes) return set;

                var delayMs = i < frameInfo.Length ? frameInfo[i].Duration : 100;
                delays[i] = Math.Clamp(delayMs <= 0 ? 100 : delayMs, 20, 2000);

                Microsoft.Maui.Graphics.IImage? img;
                try { img = PlatformImage.FromStream(new MemoryStream(bytes)); }
                catch (Exception ex)
                {
                    App.WriteLog("AnimatedImage.FromStream -> " + ex.Message);
                    return set;
                }
                frames[i] = img;

                if (i == 0)
                {
                    // 第 0 帧解码完就先摆上：预解码剩余帧期间不留空白
                    //（若最终超预算，上面的回落路径会把位子再让给静态首帧）。
                    var tile = fit == "tile";
                    Ui.Post(() =>
                    {
                        if (token != _loadToken || !_playingAllowed) return;
                        if (tile)
                        {
                            _tileDrawable.SetFrame(img);
                            _tileView.Invalidate();
                        }
                        else
                        {
                            _image.IsVisible = false;
                            _playerView.IsVisible = true;
                            _playerDrawable.SetFrame(img);
                            _playerView.Invalidate();
                        }
                    });
                }
            }

            set.Complete = true;
            return set;
        }
        catch (Exception ex)
        {
            App.WriteLog("AnimatedImage.Predecode -> " + ex.Message);
            return set;
        }
    }

    /// <summary>释放预解码帧数组：先把 drawable 里的借用引用摘掉（Draw 不再碰已释放的帧），再统一 Dispose。</summary>
    private void ReleaseFrames()
    {
        var set = _frames;
        _frames = null;
        _playerDrawable.SetFrame(null);
        _tileDrawable.SetFrame(null);
        set?.Dispose();
    }

    /// <summary>预解码帧数组与其每帧延迟；Complete = false 表示装载中途放弃，残余帧须整体释放。</summary>
    private sealed class FrameSet : IDisposable
    {
        public readonly Microsoft.Maui.Graphics.IImage?[] Frames;
        public readonly int[] Delays;
        public bool Complete;

        public FrameSet(int count)
        {
            Frames = new Microsoft.Maui.Graphics.IImage?[count];
            Delays = new int[count];
        }

        public void Dispose()
        {
            foreach (var frame in Frames)
            {
                if (frame is null) continue;
                try { (frame as IDisposable)?.Dispose(); }
                catch { }
            }
            Array.Clear(Frames);
        }
    }

    private void ApplyFrame(byte[] bytes, int token)
    {
        if (token != _loadToken || bytes.Length == 0) return;
        if (_tileView.IsVisible)
        {
            try
            {
                _tileDrawable.SetImage(PlatformImage.FromStream(new MemoryStream(bytes)));
                _tileView.Invalidate();
            }
            catch (Exception ex) { App.WriteLog("AnimatedImage.Tile -> " + ex.Message); }
        }
        else
        {
            _image.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
        }
    }

    private static int ProbeFrameCount(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream);
            return codec?.FrameCount ?? 1;
        }
        catch
        {
            return 1;
        }
    }

    private static SKBitmap? DecodeFrame(SKCodec codec, int index, SKBitmap target)
    {
        var options = index == 0 ? new SKCodecOptions(0) : new SKCodecOptions(index, index - 1);
        var result = codec.GetPixels(target.Info, target.GetPixels(), options);
        return result is SKCodecResult.Success or SKCodecResult.IncompleteInput ? target : null;
    }

    /// <summary>
    /// 把一帧按 fit 处理成 PNG 字节。裁剪与缩小都在这里做，输出永远不在 UI 线程上算。
    /// 缩到 2560 是为了 4K 背景不把内存和 PNG 编码拖垮；平铺时同一张图反复用，也省一份大底。
    /// </summary>
    private static byte[]? EncodeFrame(SKBitmap source, string fit, double cropX, double cropY, double cropW, double cropH)
    {
        try
        {
            var srcRect = new SKRectI(0, 0, source.Width, source.Height);
            if (fit == "crop")
            {
                var x = (int)Math.Round(Math.Clamp(cropX, 0, 0.999) * source.Width);
                var y = (int)Math.Round(Math.Clamp(cropY, 0, 0.999) * source.Height);
                var w = (int)Math.Round(Math.Clamp(cropW, 0.001, 1) * source.Width);
                var h = (int)Math.Round(Math.Clamp(cropH, 0.001, 1) * source.Height);
                w = Math.Max(1, Math.Min(w, source.Width - x));
                h = Math.Max(1, Math.Min(h, source.Height - y));
                srcRect = new SKRectI(x, y, x + w, y + h);
            }

            var scale = Math.Min(1.0, (double)MaxFrameEdge / Math.Max(srcRect.Width, srcRect.Height));
            var dstW = Math.Max(1, (int)Math.Round(srcRect.Width * scale));
            var dstH = Math.Max(1, (int)Math.Round(srcRect.Height * scale));

            using var output = new SKBitmap(new SKImageInfo(dstW, dstH, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(output))
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.Medium, IsAntialias = true })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(source, srcRect, new SKRect(0, 0, dstW, dstH), paint);
            }
            using var image = SKImage.FromBitmap(output);
            using var data = image.Encode(SKEncodedImageFormat.Png, 92);
            if (data is null) return null;
            using var ms = new MemoryStream();
            data.SaveTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private void HookPage()
    {
        var page = FindPage();
        if (ReferenceEquals(page, _page)) return;
        UnhookPage();
        _page = page;
        if (_page is null) return;
        _page.Disappearing += OnPageDisappearing;
        _page.Appearing += OnPageAppearing;
    }

    private void UnhookPage()
    {
        if (_page is null) return;
        _page.Disappearing -= OnPageDisappearing;
        _page.Appearing -= OnPageAppearing;
        _page = null;
    }

    private void OnPageDisappearing(object? sender, EventArgs e) => Pause();

    private void OnPageAppearing(object? sender, EventArgs e)
    {
        if (IsVisible) Play();
    }

    private Page? FindPage()
    {
        for (var p = Parent; p is not null; p = p.Parent)
            if (p is Page page) return page;
        return null;
    }
}

/// <summary>平铺：一帧图按原尺寸横竖重复，画布裁剪由 GraphicsView 负责。</summary>
internal sealed class TileDrawable : IDrawable
{
    private Microsoft.Maui.Graphics.IImage? _image;
    private bool _borrowed; // 当前引用是预解码帧数组借来的：换走时不自行 Dispose，由所有者统一释放

    public void SetImage(Microsoft.Maui.Graphics.IImage? image)
    {
        var old = _image;
        var oldBorrowed = _borrowed;
        _image = image;
        _borrowed = false;
        if (!oldBorrowed) (old as IDisposable)?.Dispose();
    }

    /// <summary>借用预解码帧：只换引用，不释放旧图（生命周期归帧数组所有者）。</summary>
    public void SetFrame(Microsoft.Maui.Graphics.IImage? frame)
    {
        _image = frame;
        _borrowed = true;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var image = _image;
        if (image is null || image.Width <= 0 || image.Height <= 0) return;
        canvas.SaveState();
        canvas.ClipRectangle(dirtyRect);
        for (var y = dirtyRect.Top; y < dirtyRect.Bottom; y += image.Height)
        {
            for (var x = dirtyRect.Left; x < dirtyRect.Right; x += image.Width)
                canvas.DrawImage(image, x, y, image.Width, image.Height);
        }
        canvas.RestoreState();
    }
}

/// <summary>
/// 动图帧播放器：持当前帧引用（SetFrame 只换引用不释放——帧数组由 AnimatedImage 统一
/// 管理生命周期），Draw 按 fit 语义画进画布：fit = 等比完整显示（居中 letterbox）、
/// stretch = 拉伸填满、其余（fill / crop）= 等比裁切填满。crop 已在预解码时烘焙进帧图，
/// 这里只做缩放适配。
/// </summary>
internal sealed class FramePlayerDrawable : IDrawable
{
    private Microsoft.Maui.Graphics.IImage? _frame;

    /// <summary>fit 语义；装载时设置一次（见 AnimatedImage.Reload），播放期间不变。</summary>
    public string Fit { get; set; } = "fill";

    public void SetFrame(Microsoft.Maui.Graphics.IImage? frame) => _frame = frame;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var frame = _frame;
        if (frame is null || frame.Width <= 0 || frame.Height <= 0) return;

        var scaleX = dirtyRect.Width / frame.Width;
        var scaleY = dirtyRect.Height / frame.Height;
        float dx, dy, dw, dh;
        if (Fit == "fit")
        {
            // 等比完整显示，居中 letterbox。
            var scale = Math.Min(scaleX, scaleY);
            dw = frame.Width * scale;
            dh = frame.Height * scale;
            dx = dirtyRect.Left + (dirtyRect.Width - dw) / 2f;
            dy = dirtyRect.Top + (dirtyRect.Height - dh) / 2f;
        }
        else if (Fit == "stretch")
        {
            // 拉伸填满。
            dx = dirtyRect.Left;
            dy = dirtyRect.Top;
            dw = dirtyRect.Width;
            dh = dirtyRect.Height;
        }
        else
        {
            // fill / crop：等比裁切填满。
            var scale = Math.Max(scaleX, scaleY);
            dw = frame.Width * scale;
            dh = frame.Height * scale;
            dx = dirtyRect.Left + (dirtyRect.Width - dw) / 2f;
            dy = dirtyRect.Top + (dirtyRect.Height - dh) / 2f;
        }

        canvas.SaveState();
        canvas.ClipRectangle(dirtyRect);
        canvas.DrawImage(frame, dx, dy, dw, dh);
        canvas.RestoreState();
    }
}

/// <summary>
/// 舞台角色背景的两层执行器：新旧两层图片各占一颗 AnimatedImage，
/// 切换时 220ms 同时淡入淡出；视频只有一颗 MediaElement，图片↔视频同为交叉淡入，
/// 视频↔视频则先收再起（同一颗元素没法同时放两条片）。
/// 页面不直接碰这些控件，切背景只调 <see cref="ApplyAsync"/>。
/// </summary>
public sealed class CharacterBackdropPresenter
{
    private const int FadeMs = 220;
    private readonly AnimatedImage _a;
    private readonly AnimatedImage _b;
    private readonly Layout _videoHost;
    private MediaElement? _video;
    private bool _frontA = true;
    private bool _videoActive;
    private string _videoPath = "";
    private string _videoFit = "";
    private int _generation;

    public CharacterBackdropPresenter(AnimatedImage a, AnimatedImage b, Layout videoHost)
    {
        _a = a;
        _b = b;
        _videoHost = videoHost;
    }

    /// <summary>
    /// 真到要播视频才建 MediaElement：大多数角色背景是静态图，
    /// 提前建一颗播放器只会白白多占一个解码器。
    /// </summary>
    private MediaElement EnsureVideo()
    {
        if (_video is not null) return _video;
        var video = new MediaElement
        {
            IsVisible = false,
            Opacity = 0,
            Aspect = Aspect.AspectFill,
            ShouldAutoPlay = false,
            ShouldLoopPlayback = true,
            ShouldMute = true,
            InputTransparent = true,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        video.MediaFailed += (_, _) =>
        {
            // 编解码不支持时宁可退回纯色，也不要留一块黑屏硬塞在那里。
            _videoActive = false;
            video.IsVisible = false;
            video.Opacity = 0;
        };
        _videoHost.Children.Add(video);
        _video = video;
        return video;
    }

    /// <summary>新背景；path 为空 = 收掉角色背景，露出场景底色。</summary>
    public async Task ApplyAsync(string? path, string fit, double cropX, double cropY,
        double cropW, double cropH, bool isVideo, bool instant = false)
    {
        var generation = ++_generation;
        if (string.IsNullOrWhiteSpace(path))
        {
            await ClearAsync(generation, instant);
            return;
        }
        if (isVideo)
        {
            await ShowVideoAsync(path, fit, generation, instant);
            return;
        }
        await ShowImageAsync(path, fit, cropX, cropY, cropW, cropH, generation, instant);
    }

    public void Pause()
    {
        try { _video?.Pause(); } catch { }
    }

    public void Resume()
    {
        if (!_videoActive || _video is null || _video.Source is null) return;
        try
        {
            _video.IsVisible = true;
            _video.Play();
        }
        catch { }
    }

    private async Task ClearAsync(int generation, bool instant)
    {
        if (instant)
        {
            ResetLayers();
            return;
        }
        var tasks = new List<Task>();
        if (_videoActive && _video is not null) tasks.Add(_video.FadeTo(0, FadeMs, Easing.CubicIn));
        if (_a.IsVisible) tasks.Add(_a.FadeTo(0, FadeMs, Easing.CubicIn));
        if (_b.IsVisible) tasks.Add(_b.FadeTo(0, FadeMs, Easing.CubicIn));
        if (tasks.Count > 0) await Task.WhenAll(tasks);
        if (generation != _generation) return;
        ResetLayers();
    }

    private async Task ShowImageAsync(string path, string fit, double cropX, double cropY,
        double cropW, double cropH, int generation, bool instant)
    {
        ApplyImageAspect(fit);

        var target = _frontA ? _b : _a;
        var old = _frontA ? _a : _b;
        target.IsVisible = true;
        target.Opacity = instant ? 1 : 0;
        target.Configure(path, fit, cropX, cropY, cropW, cropH);

        if (instant)
        {
            old.IsVisible = false;
            old.Opacity = 0;
            old.Path = "";
            StopVideo();
            _frontA = !_frontA;
            return;
        }

        var tasks = new List<Task> { target.FadeTo(1, FadeMs, Easing.CubicOut) };
        if (old.IsVisible) tasks.Add(old.FadeTo(0, FadeMs, Easing.CubicIn));
        if (_videoActive && _video is not null) tasks.Add(_video.FadeTo(0, FadeMs, Easing.CubicIn));
        await Task.WhenAll(tasks);
        if (generation != _generation) return;

        old.IsVisible = false;
        old.Opacity = 0;
        old.Path = "";
        if (_videoActive) StopVideo();
        _frontA = !_frontA;
    }

    private async Task ShowVideoAsync(string path, string fit, int generation, bool instant)
    {
        var video = EnsureVideo();
        var aspect = fit switch
        {
            "fit" => Aspect.AspectFit,
            "stretch" => Aspect.Fill,
            _ => Aspect.AspectFill
        };

        if (_videoActive && _videoPath == path && _videoFit == fit)
        {
            if (!instant && video.Opacity < 1)
                await video.FadeTo(1, FadeMs, Easing.CubicOut);
            video.Opacity = 1;
            return;
        }

        if (instant)
        {
            ResetImages();
            SetVideoSource(path, fit, aspect);
            video.IsVisible = true;
            video.Opacity = 1;
            video.Play();
            _videoActive = true;
            _videoPath = path;
            _videoFit = fit;
            return;
        }

        if (_videoActive)
        {
            // 视频→视频：一颗 MediaElement 没法叠着放，先收再起，两段各 110ms，整体仍在一次呼吸里。
            await video.FadeTo(0, 110, Easing.CubicIn);
            if (generation != _generation) return;
            SetVideoSource(path, fit, aspect);
            video.Opacity = 0;
            video.IsVisible = true;
            video.Play();
            await video.FadeTo(1, 110, Easing.CubicOut);
            if (generation != _generation) return;
        }
        else
        {
            SetVideoSource(path, fit, aspect);
            video.Opacity = 0;
            video.IsVisible = true;
            video.Play();
            var tasks = new List<Task> { video.FadeTo(1, FadeMs, Easing.CubicOut) };
            if (_a.IsVisible) tasks.Add(_a.FadeTo(0, FadeMs, Easing.CubicIn));
            if (_b.IsVisible) tasks.Add(_b.FadeTo(0, FadeMs, Easing.CubicIn));
            await Task.WhenAll(tasks);
            if (generation != _generation) return;
            ResetImages();
        }

        _videoActive = true;
        _videoPath = path;
        _videoFit = fit;
    }

    private void SetVideoSource(string path, string fit, Aspect aspect)
    {
        var video = EnsureVideo();
        try { video.Pause(); } catch { }
        video.Aspect = aspect;
        video.Source = MediaSource.FromFile(path);
        _videoFit = fit;
    }

    private void ApplyImageAspect(string fit)
    {
        var aspect = fit switch
        {
            "fit" => Aspect.AspectFit,
            "stretch" => Aspect.Fill,
            _ => Aspect.AspectFill
        };
        _a.ImageAspect = aspect;
        _b.ImageAspect = aspect;
    }

    private void StopVideo()
    {
        if (_video is null) return;
        try { _video.Pause(); } catch { }
        _video.IsVisible = false;
        _video.Opacity = 0;
        _video.Source = null;
        _videoActive = false;
        _videoPath = "";
        _videoFit = "";
    }

    private void ResetImages()
    {
        _a.IsVisible = false;
        _a.Opacity = 0;
        _a.Path = "";
        _b.IsVisible = false;
        _b.Opacity = 0;
        _b.Path = "";
    }

    private void ResetLayers()
    {
        ResetImages();
        StopVideo();
    }
}
