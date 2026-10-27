using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Graphics.Platform;
using SkiaSharp;
using WarmAsBefore.Services;

namespace WarmAsBefore.Controls;

/// <summary>
/// 能放 GIF / 动图 WebP 的 Image。
///
/// 解码走 SkiaSharp 的 SKCodec：一次只留一帧的像素，按帧延迟换 ImageSource；
/// 文件只有一帧（或解码失败）时退回普通 Image，不额外占一颗定时器。
/// 平铺（tile）在 MAUI 里没有 TileBrush，这里改用 GraphicsView 反复画同一张图，
/// Windows 与 Android 都是同一套 Skia 绘制路径。
///
/// 页面消失 / 控件隐藏 / 从树上卸下都会停表；重新出现时从第一帧重播，
/// 不偷偷在后台烧 CPU。所有 SKBitmap / SKCodec / SKImage 都在帧处理完立刻释放。
/// </summary>
public sealed class AnimatedImage : ContentView
{
    private const int MaxFrameEdge = 2560;
    private static readonly string[] VideoExts = { ".mp4", ".mov", ".m4v", ".webm" };

    private readonly Image _image;
    private readonly GraphicsView _tileView;
    private readonly TileDrawable _tileDrawable = new();
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
        Content = new Grid
        {
            IsClippedToBounds = true,
            Children = { _image, _tileView }
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
        _tileDrawable.SetImage(null);

        var path = Path;
        var fit = CharacterBackgroundService.NormalizeFit(Fit);
        var isTile = fit == "tile";
        _image.IsVisible = !isTile;
        _tileView.IsVisible = isTile;
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
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (token == _loadToken) _image.Source = ImageSource.FromFile(path);
                    });
                return;
            }

            var bytes = await Task.Run(() =>
            {
                using var bmp = SKBitmap.Decode(path);
                return bmp is null ? null : EncodeFrame(bmp, fit, CropX, CropY, CropW, CropH);
            });
            if (bytes is null || token != _loadToken) return;
            await MainThread.InvokeOnMainThreadAsync(() => ApplyFrame(bytes, token));
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
        await MainThread.InvokeOnMainThreadAsync(() => ApplyFrame(bytes, token));
    }

    private async Task RunFramesAsync(string path, string fit, int token, CancellationToken cancel)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var codec = SKCodec.Create(stream);
            if (codec is null || codec.FrameCount <= 1) return;

            var info = codec.Info;
            using var scratch = new SKBitmap(new SKImageInfo(
                info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            var frames = codec.FrameInfo;
            var count = codec.FrameCount;
            var index = 0;

            while (!cancel.IsCancellationRequested && token == _loadToken)
            {
                var options = index == 0 ? new SKCodecOptions(0) : new SKCodecOptions(index, index - 1);
                var result = codec.GetPixels(scratch.Info, scratch.GetPixels(), options);
                if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) return;

                var bytes = EncodeFrame(scratch, fit, CropX, CropY, CropW, CropH);
                if (bytes is null) return;

                await MainThread.InvokeOnMainThreadAsync(() => ApplyFrame(bytes, token));
                if (token != _loadToken || !_playingAllowed) return;

                var delayMs = index < frames.Length ? frames[index].Duration : 100;
                delayMs = Math.Clamp(delayMs <= 0 ? 100 : delayMs, 20, 2000);
                if (!IsLooping && index == count - 1) return;

                index = (index + 1) % count;
                await Task.Delay(delayMs, cancel);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.WriteLog("AnimatedImage.RunFrames -> " + ex.Message);
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

    public void SetImage(Microsoft.Maui.Graphics.IImage? image)
    {
        var old = _image;
        _image = image;
        (old as IDisposable)?.Dispose();
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
