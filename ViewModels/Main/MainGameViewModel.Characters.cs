using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Dispatching;
using WarmAsBefore.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Models.Background;
using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Modules.Automation;
using WarmAsBefore.Modules.Outfit;
using WarmAsBefore.Modules.RealWorld;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;
using RealTimeProvider = WarmAsBefore.Modules.RealWorld.TimeProvider;

namespace WarmAsBefore.ViewModels;

public sealed partial class MainGameViewModel
{
    // ============ 多角色位置：left / center / right ============
    [ObservableProperty] private string _spritePosition = "center";
    // ============ 说话者高亮 ============
    [ObservableProperty] private bool _isSpeaking;

    // ============ 多角色单场景：气泡式开关 + 沉默提示 ============
    [ObservableProperty] private bool _useBubbleChat;
    [ObservableProperty] private string _silenceHintText = "";

    // ---- 标定驱动的角色变换 ----
    private ScenePlacement? _placement;
    private (int W, int H) _bgImageSize;
    private (int W, int H) _spritePixels;
    private double _stageW, _stageH;
    private string? _spriteAbsPath;
    /// <summary>当前立绘在 SpriteMap 里的键（存储相对路径）：立绘标定按它存取。</summary>
    private string? _spriteKey;
    private PortraitCalibration? _portraitCal;
    /// <summary>标定算出的缩放（说话强调在此之上乘系数，而不是覆盖它）。</summary>
    private double _calibratedScale = 1.0;
    /// <summary>标定算出的基准位移；动画在它之上做偏移，动画结束回到它。</summary>
    private double _baseSpriteX, _baseSpriteY;

    // ---- 新版着装（形象包） ----
    /// <summary>当前角色是否真的走形象包：开关开着、绑了包、包能读出来，三者缺一就回退 SpriteMap。</summary>
    private bool _figureActive;
    private FigurePack? _figurePack;
    private List<string> _figureOutfits = new();
    private List<string> _figureEmotions = new();
    /// <summary>连续切表情时只认最后一次合成结果，防止慢的那一张盖掉新的一张。</summary>
    private int _figureRenderVersion;
    /// <summary>静态立绘的缓存路径：走路动画结束后回到这一张，不重新合成。</summary>
    private string? _figureStaticPath;
    /// <summary>行走动作的取消源；每次 IsWalking 切换都换一代，旧循环不会把旧帧贴回来。</summary>
    private CancellationTokenSource? _walkMotionCts;
    /// <summary>上一次采样到的行走相位（0~1）。从待机起步时沿用，避免跳回第 0 帧。</summary>
    private double _walkPhase;
    private int _walkFrameIndex = -1;
    /// <summary>
    /// 行走帧的 ImageSource 缓存（键 "{motion.Id}:{帧号}:{宽度}"）。
    /// 循环回到同一帧时回赋同一个实例：setter 的相等检查直接短路，不触发 PropertyChanged，
    /// 也就不会让 Image 换源走一遍 Measure 失效——这是 GC 风暴与布局环的共犯。每次行走开始时清空。
    /// </summary>
    private readonly System.Collections.Generic.Dictionary<string, Microsoft.Maui.Controls.ImageSource> _walkFrameCache = new();
    /// <summary>主界面行走帧的落盘尺寸；手机端降到 720×1280，避免多帧同时解码吃爆内存。</summary>
    private const int WalkFrameDesktopWidth = 1080;
    private const int WalkFramePhoneWidth = 720;

    [ObservableProperty] private double _spriteFitW = 260;
    [ObservableProperty] private double _spriteFitH = 420;

    /// <summary>水平翻转（-1 = 镜像）：方向不对的素材由标定决定，而不是靠文件名约定。</summary>
    [ObservableProperty] private double _spriteFlipScaleX = 1;

    /// <summary>立绘标定问题（非空时主界面顶部给出提示与「去标定」入口）。</summary>
    [ObservableProperty] private string _spriteCalibrationIssue = "";
    partial void OnSpriteCalibrationIssueChanged(string value)
        => OnPropertyChanged(nameof(HasSpriteCalibrationIssue));
    public bool HasSpriteCalibrationIssue => !string.IsNullOrEmpty(SpriteCalibrationIssue);

    /// <summary>承载背景与立绘的容器尺寸变化（页面 SizeChanged 转发）。</summary>
    public void OnStageSizeChanged(double w, double h, bool isPhone)
    {
        // 桌面版与手机版两条上报路径都汇到这里：统一挡一次非有限值。
        // NaN 与任何数比较都为 false，只写 <= 0 会把它放过去，随后 UpdateSpriteTransform
        // 会把 SpriteFitW/H / SpriteScale 算成 NaN 写回立绘的 WidthRequest / Scale。
        if (!double.IsFinite(w) || !double.IsFinite(h) || w <= 0 || h <= 0) return;
        _stageW = w;
        _stageH = h;
        _isPhoneStage = isPhone;
        UpdateSpriteTransform();
    }

    private bool _isPhoneStage = true;

    /// <summary>取地图场景背景图与立绘的原图像素尺寸，然后重算立绘变换。</summary>
    private async Task RefreshSpriteGeometryAsync(MapScene? scene)
    {
        var bgAbs = scene is null ? null : _map.ResolveBackground(scene);
        if (!string.IsNullOrEmpty(bgAbs) && MapService.IsVideoExt(Path.GetExtension(bgAbs)))
            bgAbs = scene is null ? null : _map.ThumbnailAbsFor(scene);
        await RefreshSpriteGeometryForAsync(bgAbs, scene?.Placement);
    }

    /// <summary>按"任意背景路径 + 标定数据"重算立绘变换（场景模式覆盖背景时走这条）。</summary>
    private async Task RefreshSpriteGeometryForAsync(string? bgAbs, ScenePlacement? placement)
    {
        _placement = placement;
        _bgImageSize = await ImageProbe.SizeOfAsync(
            !string.IsNullOrEmpty(bgAbs) && File.Exists(bgAbs) ? bgAbs : null);
        _spritePixels = await ImageProbe.SizeOfAsync(_spriteAbsPath);
        await RefreshPortraitCalibrationAsync();
        UpdateSpriteTransform();
    }

    /// <summary>
    /// 取当前立绘的标定，并判断它是否需要进调试界面。
    /// 不是 1080×1920 逻辑画布的素材会被标记出来（主界面给「去标定」入口），
    /// 但不会被静默拉伸——大小与底部对齐只认标定结果。
    /// </summary>
    private async Task RefreshPortraitCalibrationAsync()
    {
        var key = _spriteKey ?? _spriteAbsPath ?? "";
        _portraitCal = await _portraits.GetAsync(key);

        List<string> problems;
        try { problems = await _portraits.ProblemsAsync(key, _spritePixels.W, _spritePixels.H); }
        catch (Exception ex)
        {
            App.WriteLog("MainGameVM.PortraitCalibration -> " + ex.Message);
            problems = new List<string>();
        }
        SpriteCalibrationIssue = problems.Count > 0 ? problems[0] : "";
    }

    /// <summary>
    /// 场景模式换背景时同步换标定。
    ///
    /// 规格里这块是"可以选择沿用或单图微调"，两种都支持：
    ///   · <see cref="Models.SceneMode.Placement"/> 有值 = 这张模式背景自己标过（单图微调）；
    ///   · 为空 = 沿用所属库条目引用的地图场景的标定。
    /// </summary>
    private void ApplyModePlacement(Models.SceneLibraryEntry entry, Models.SceneMode? mode)
    {
        var placement = mode?.Placement;
        if (placement is null && !string.IsNullOrEmpty(entry.MapSceneRef))
            placement = _map.Map.SceneById(entry.MapSceneRef!)?.Placement;

        var bgAbs = mode?.Background is { Length: > 0 } mb ? mb : entry.Background;
        _ = RefreshSpriteGeometryForAsync(bgAbs, placement);
    }

    /// <summary>
    /// 由标定数据算出立绘的尺寸、缩放与位移。
    ///
    /// 关键点是背景用的是 AspectFill（会裁切），而标定坐标是**原图归一化**坐标，
    /// 所以必须先把归一化坐标映射到 AspectFill 之后的实际显示矩形上，
    /// 否则竖图放横屏时会整体偏掉。
    /// </summary>
    private void UpdateSpriteTransform()
    {
        if (_stageW <= 0 || _stageH <= 0) return;

        // 立绘基准尺寸：按「标定后的有效像素」AspectFit 进容器。
        // 用有效尺寸而不是原始像素，才能让 1080×1920 的画布语义对任何素材都成立
        // （裁剪修掉留白、旋转修正方向后，角色的实际占比才是对的）。
        var (effW, effH) = PortraitCalibrationMath.EffectiveSize(_spritePixels.W, _spritePixels.H, _portraitCal);
        var sw = effW > 0 ? effW : 400.0;
        var sh = effH > 0 ? effH : 640.0;
        var fit = Math.Min(_stageW / sw, _stageH / sh);
        var baseW = sw * fit;
        var baseH = sh * fit;
        // SpriteFitW/H 仍供手机端 XAML 绑定与行走动画步长使用；桌面立绘改由 AspectFit 按舞台自适应，
        // 不再写回布局测量。本方法只产出渲染变换值（Scale/Translation/Opacity/Flip），不参与父容器 Measure。
        SpriteFitW = baseW;
        SpriteFitH = baseH;

        // 没有可用标定：退回旧的居中显示，**不凭空推断位置与缩放**
        if (_placement is null || !PlacementMath.Validate(_placement).Ok)
        {
            _calibratedScale = 1.0;
            _baseSpriteX = 0;
            _baseSpriteY = 0;
            SpriteX = 0;
            SpriteY = 0;
            ApplyPortraitCalibrationOffsets(baseH);
            ApplySpeakingEmphasis();
            return;
        }

        var desired = DesiredNormalized(baseW);
        var (anchor, k) = PlacementMath.ResolveAnchor(_placement, desired);

        // 标定给出的 k 是"相对基准大小"，乘上参考身高占比与容器高得到目标像素高
        var visibleH = _stageH * _placement.RefHeightRatio * k;
        _calibratedScale = Math.Clamp(visibleH / baseH, 0.02, 8.0);
        ApplySpeakingEmphasis();

        // AspectFill 映射：算出原图在容器里实际被铺成了多大、偏移多少
        var iw = _bgImageSize.W > 0 ? _bgImageSize.W : 16.0;
        var ih = _bgImageSize.H > 0 ? _bgImageSize.H : 9.0;
        var s = Math.Max(_stageW / iw, _stageH / ih);
        var dw = iw * s;
        var dh = ih * s;
        var ox = (_stageW - dw) / 2;
        var oy = (_stageH - dh) / 2;

        _baseSpriteX = ox + anchor.X * dw - _stageW / 2;
        // 让"脚底"落在锚点上：立绘实际显示高度是 baseH * Scale
        _baseSpriteY = oy + anchor.Y * dh - _stageH / 2 - baseH * SpriteScale / 2;

        ApplyPortraitCalibrationOffsets(baseH);

        SpriteX = _baseSpriteX;
        SpriteY = _baseSpriteY;
    }

    /// <summary>
    /// 把立绘标定叠加到基准变换上：方向（镜像）、溢出幅度（缩放）、着色偏移、
    /// 以及「底部对齐」——脚底线不在图像底边时，把图整体下移，让脚踩在锚点上。
    /// </summary>
    private void ApplyPortraitCalibrationOffsets(double baseH)
    {
        SpriteFlipScaleX = 1;
        if (_portraitCal is not { Calibrated: true } pc) return;

        SpriteFlipScaleX = pc.FlipHorizontal ? -1 : 1;
        _calibratedScale = Math.Clamp(_calibratedScale * pc.Scale, 0.02, 8.0);
        ApplySpeakingEmphasis();

        // 底部对齐：脚底线归一化高度 footLine 处的像素要落在锚点上
        _baseSpriteY += baseH * SpriteScale * (1 - pc.FootLine);
        _baseSpriteX += pc.OffsetX * _stageW;
        _baseSpriteY += pc.OffsetY * _stageH;
    }

    /// <summary>把 left/center/right 换算成归一化落脚位置——按立绘宽度成比例，而不是写死 150px。</summary>
    private NormPoint DesiredNormalized(double baseW)
    {
        var iw = _bgImageSize.W > 0 ? _bgImageSize.W : 16.0;
        var ih = _bgImageSize.H > 0 ? _bgImageSize.H : 9.0;
        var dw = iw * Math.Max(_stageW / iw, _stageH / ih);
        var dir = SpritePosition switch { "left" => -1.0, "right" => 1.0, _ => 0.0 };
        var nx = dw > 0 ? 0.5 + dir * baseW * 1.05 / dw : 0.5;
        return new NormPoint(Math.Clamp(nx, 0.02, 0.98), 0.9);
    }

    /// <summary>说话强调：乘在标定缩放之上，而不是把它覆盖掉。</summary>
    private void ApplySpeakingEmphasis() => SpriteScale = _calibratedScale * (IsSpeaking ? 1.0 : 0.95);

    private void SetEmotion(string emotion)
    {
        if (_char is null) return;
        _currentEmotion = _figureActive
            ? (_figureEmotions.Contains(emotion, StringComparer.OrdinalIgnoreCase) ? emotion : _defaultEmotion)
            : (_char.SpriteMap.ContainsKey($"{_outfitKey}/{emotion}") ? emotion : _defaultEmotion);
        // 表情也要写回角色状态，随存档持久化；否则下次进来又回到初次的表情
        _char.State.CurrentOutfit = _outfitKey;
        _char.State.CurrentEmotion = _currentEmotion;
        ApplySprite();
    }

    /// <summary>
    /// 选图顺序：新版形象包（开关开着、绑了包、包能读出来）优先；否则走旧的 SpriteMap。
    /// 合成失败时同一次调用立刻回退旧路径，屏幕不会空着——旧版换装始终可用。
    /// </summary>
    private void ApplySprite()
    {
        if (_char is null || string.IsNullOrEmpty(_outfitKey)) { SpriteVisible = false; return; }
        if (_figureActive)
        {
            _ = ApplyFigurePackSpriteAsync();
            return;
        }
        // 新版分支已经失效时，别让旧的行走循环继续往舞台上贴合成帧。
        StopWalkMotion(restoreStatic: false);
        ApplySpriteMap();
    }

    private void ApplySpriteMap()
    {
        if (_char is null || string.IsNullOrEmpty(_outfitKey)) { SpriteVisible = false; return; }
        var key = $"{_outfitKey}/{_currentEmotion}";
        if (!_char.SpriteMap.TryGetValue(key, out var rel))
            rel = _char.SpriteMap.ContainsKey($"{_outfitKey}/{_defaultEmotion}")
                ? _char.SpriteMap[$"{_outfitKey}/{_defaultEmotion}"]
                : _char.SpriteMap.Values.FirstOrDefault();
        _spriteKey = rel;   // 立绘标定按 SpriteMap 的存储路径存取，与角色库/素材库共用同一份
        string? full = null;
        if (!string.IsNullOrEmpty(rel))
        {
            // 候选路径和工作室、好感动画共用同一份（SpritePathResolver）：
            // 各处各猜各的，就会出现「同一张图这一屏有、那一屏没有」。
            var candidates = Modules.Affection.SpritePathResolver.Candidates(rel, _store.Root);
            full = candidates
                .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                .FirstOrDefault();
            if (full is null)
                App.WriteLog($"ApplySprite: 未找到立绘 {rel}, 尝试了 {candidates.Count} 条路径");
        }
        if (string.IsNullOrEmpty(full)) { SpriteVisible = false; return; }
        // Ui.Post 在 UI 线程上同步执行，与原先两分支的行为一致。
        Helpers.Ui.Post(() => _ = ApplySpriteCoreAsync(full));
    }

    /// <summary>
    /// 读形象包的服装轴与表情轴。失败不抛：调用方据此回退 SpriteMap。
    /// 表情键来自部件的 EmotionTextures；包没给表情时至少保留 normal，合成器按基础表情出图。
    /// </summary>
    private async Task<bool> TryLoadFigurePackAsync(CharacterData ch)
    {
        try
        {
            if (FigureCompat.ShouldFallbackToSpriteMap(_settings, ch)) return false;
            var pack = await _figures.LoadAsync(ch.FigurePackId);
            if (pack is null) return false;

            _figurePack = pack;
            _figureOutfits = pack.Outfits
                .Select(o => o.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (_figureOutfits.Count == 0) _figureOutfits.Add("default");

            _figureEmotions = new List<string> { "normal" };
            foreach (var part in pack.Parts)
            {
                foreach (var emotion in part.EmotionTextures.Keys)
                {
                    if (string.IsNullOrWhiteSpace(emotion)) continue;
                    if (!_figureEmotions.Contains(emotion, StringComparer.OrdinalIgnoreCase))
                        _figureEmotions.Add(emotion);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGameVM.TryLoadFigurePack -> " + ex);
            return false;
        }
    }

    /// <summary>
    /// 新版着装出图：按当前服装/表情合成一张 1080×1920 的透明 PNG。
    /// 手机上同样走这张 PNG（不做透明视频解码）——合成一次，主界面、桌宠、工作室共用缓存。
    /// </summary>
    private async Task ApplyFigurePackSpriteAsync()
    {
        var ch = _char;
        if (ch is null || !_figureActive) return;
        var version = ++_figureRenderVersion;
        try
        {
            var outfit = _figureOutfits.FirstOrDefault(o =>
                string.Equals(o, _outfitKey, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(outfit) && _figurePack is { Outfits.Count: > 0 })
                outfit = _figurePack.Outfits[0].Id;

            var emotion = _figureEmotions.FirstOrDefault(e =>
                string.Equals(e, _currentEmotion, StringComparison.OrdinalIgnoreCase)) ?? "normal";

            var path = await _figures.RenderAsync(
                ch.FigurePackId, outfit ?? "", "", emotion, 1080, 1920);

            if (version != _figureRenderVersion
                || _char is null
                || !string.Equals(ch.Profile.Id, _char.Profile.Id, StringComparison.Ordinal))
                return;

            if (string.IsNullOrWhiteSpace(path))
            {
                App.WriteLog("MainGameVM.ApplyFigurePack: 合成失败，回退 SpriteMap -> " + ch.FigurePackId);
                _figureActive = false;
                StopWalkMotion(restoreStatic: false);
                ApplySpriteMap();
                return;
            }

            // 合成图本来就是 1080×1920 标准画布；用缓存路径做标定键，改素材后换新键，
            // 不会把旧图的偏移套到新图上。
            _figureStaticPath = path;
            _spriteKey = path;
            // 行走中换服装/表情：静态图先备好，再让行走循环用新参数重新取帧；
            // 不把当前行走帧盖成静态图，否则每换一次表情都会闪一下。
            if (IsWalking)
            {
                StartWalkMotion();
                return;
            }
            // Ui.PostAsync 在 UI 线程上就地执行并返回原 Task，与原先两分支的 await 行为一致。
            await Helpers.Ui.PostAsync(() => ApplySpriteCoreAsync(path));
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGameVM.ApplyFigurePack -> " + ex);
            if (version == _figureRenderVersion)
            {
                _figureActive = false;
                StopWalkMotion(restoreStatic: false);
                ApplySpriteMap();
            }
        }
    }

    private async Task ApplySpriteCoreAsync(string full)
    {
        SpriteLoadingProgress = 0.1;
        StatusText = "加载立绘...";
        await Task.Delay(50);
        try
        {
            SpriteSource = ImageSource.FromFile(full);
            SpriteVisible = true;
            _spriteAbsPath = full;
            SpriteOpacity = IsSpeaking ? 1.0 : 0.5;
            // 位置与缩放一律由标定算出来；没有标定时回退居中显示。
            // 这里不再有"-150/150"这类写死的偏移——那会让远近不同的场景全都错位。
            await RefreshSpriteGeometryAsync(_currentScene);
            SpriteLoadingProgress = 1.0;
            StatusText = "";
        }
        catch (Exception ex)
        {
            App.WriteLog($"ApplySpriteCoreAsync: {ex.Message}");
            SpriteLoadingProgress = 0;
            StatusText = "立绘加载失败";
        }
    }

    // ============ 新版着装：行走动作 ============

    /// <summary>
    /// 一段路线开始行走时切到 walk 动作；到达/取消时由 WalkRouteCompleted 收回静态立绘。
    /// 节拍仍由 <see cref="AnimateWalkAsync"/> 里的 WalkCycle 按距离推进。
    /// </summary>
    private void OnWalkSegmentStarted()
    {
        Helpers.Ui.Post(StartWalkMotion);
    }

    private void OnWalkRouteCompleted()
    {
        Helpers.Ui.Post(() => StopWalkMotion());
    }

    private void StartWalkMotion()
    {
        StopWalkMotion(restoreStatic: false);
        if (!_figureActive || _figurePack is null || _char is null) return;
        var motion = FigureMotionPlayback.FindWalk(_figurePack);
        if (motion?.Frames is not { Count: > 0 }) return;

        var cts = new CancellationTokenSource();
        _walkMotionCts = cts;
        _walkFrameIndex = -1;
        _ = AnimateWalkAsync(motion, cts.Token);
    }

    private void StopWalkMotion(bool restoreStatic = true)
    {
        var cts = _walkMotionCts;
        _walkMotionCts = null;
        if (cts is not null)
        {
            try { cts.Cancel(); } catch { }
            cts.Dispose();
        }
        _walkFrameIndex = -1;

        if (!restoreStatic || !_figureActive) return;
        var staticPath = _figureStaticPath;
        if (string.IsNullOrWhiteSpace(staticPath) || !File.Exists(staticPath)) return;
        // 静态立绘的单发赋值：统一走 Ui.Post，不再区分是否已在 UI 线程。
        Helpers.Ui.Post(() => SpriteSource = ImageSource.FromFile(staticPath));
    }

    /// <summary>
    /// 行走节拍的唯一驱动：每帧把「真实经过的时间 × 步行速度」换算成位移，
    /// 再用 <see cref="WalkCycle.PhaseForDistance"/> 求相位。这样帧号只跟走过的距离有关，
    /// 帧率抖动、变速、从待机起步都不会让脚在地上滑。
    /// </summary>
    private async Task AnimateWalkAsync(FigureMotion motion, CancellationToken ct)
    {
        try
        {
            var packId = _char?.FigurePackId ?? "";
            var outfit = _outfitKey ?? "";
            var frames = motion.Frames;
            if (string.IsNullOrWhiteSpace(packId) || frames.Count == 0) return;
            // 新一段行走开始：上一段的帧缓存作废（着装/表情可能已经换了）。
            _walkFrameCache.Clear();

            // 以角色显示高度作为「一个身长」：步长约 0.38 身长，速度约 0.9 身长/秒。
            // 参数集中在 WalkCycle 的单位里，改速度时不必去改播放器。
            var bodyHeight = Math.Max(120, SpriteFitH * Math.Max(0.1, SpriteScale));
            var stepLength = Math.Max(24, bodyHeight * 0.38);
            var speed = Math.Max(40, bodyHeight * 0.9);
            var startPhase = _walkPhase;
            var width = _isPhoneStage ? WalkFramePhoneWidth : WalkFrameDesktopWidth;
            var height = (int)Math.Round(width * 1920.0 / 1080.0);

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var lastSeconds = 0d;
            var distance = 0d;

            while (!ct.IsCancellationRequested)
            {
                var now = clock.Elapsed.TotalSeconds;
                var dt = Math.Clamp(now - lastSeconds, 0, 0.12);
                lastSeconds = now;
                distance += speed * dt;

                var phase = WalkCycle.PhaseForDistance(distance, stepLength, WalkCycle.DefaultStepsPerCycle, startPhase);
                _walkPhase = phase;
                var index = WalkCycle.FrameIndexAt(phase, frames.Count);
                if (index != _walkFrameIndex && !ct.IsCancellationRequested)
                {
                    _walkFrameIndex = index;
                    // 帧图只构建一次 ImageSource、之后全程复用：逐帧 FromFile 会不停分配大对象，
                    // 而且每次换新实例都会触发 Image 的 Measure 失效（GC 风暴 + 布局环的共犯）。
                    var key = $"{motion.Id}:{index}:{width}";
                    if (_walkFrameCache.TryGetValue(key, out var cached))
                    {
                        Helpers.Ui.Post(() =>
                        {
                            if (ct.IsCancellationRequested) return;
                            SpriteSource = cached;
                            SpriteVisible = true;
                        });
                    }
                    else
                    {
                        var path = await _figures.RenderMotionAsync(
                            packId, outfit, "", motion.Id, index, width, height).ConfigureAwait(false);
                        if (ct.IsCancellationRequested) return;
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            var src = ImageSource.FromFile(path);
                            _walkFrameCache[key] = src;
                            Helpers.Ui.Post(() =>
                            {
                                if (ct.IsCancellationRequested) return;
                                SpriteSource = src;
                                SpriteVisible = true;
                            });
                        }
                    }
                }

                await Task.Delay(33, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.WriteLog("MainGameVM.AnimateWalk -> " + ex);
        }
    }

    /// <summary>设置当前说话角色并更新立绘高亮。</summary>
    public void SetSpeakingCharacter(bool isSpeaking)
    {
        IsSpeaking = isSpeaking;
        if (SpriteVisible)
        {
            SpriteOpacity = isSpeaking ? 1.0 : 0.5;
            ApplySpeakingEmphasis();
        }
    }

    /// <summary>执行基础动作：下沉。位移叠加在标定算出的基准位置上，动画结束回到基准。</summary>
    public async Task SinkAnimationAsync()
    {
        SpriteY = _baseSpriteY + 15;
        await Task.Delay(300);
        SpriteY = _baseSpriteY;
    }

    /// <summary>执行基础动作：跳跃。</summary>
    public async Task JumpAnimationAsync()
    {
        SpriteY = _baseSpriteY - 30;
        await Task.Delay(400);
        SpriteY = _baseSpriteY;
    }

    /// <summary>执行基础动作：颤抖。</summary>
    public async Task ShakeAnimationAsync()
    {
        for (int i = 0; i < 5; i++)
        {
            SpriteX = _baseSpriteX + (i % 2 == 0 ? 3 : -3);
            await Task.Delay(50);
        }
        SpriteX = _baseSpriteX;
    }

    /// <summary>从回复文本中匹配表情：命中任一表情词（取最长）则切过去；否则按常用情绪词兜底。</summary>
    private string? ResolveEmotion(string text)
    {
        if (_char is null || string.IsNullOrEmpty(text)) return null;
        if (_figureActive)
        {
            var hit = _figureEmotions
                .Where(e => e.Length > 0 && text.Contains(e, StringComparison.Ordinal))
                .OrderByDescending(e => e.Length)
                .FirstOrDefault();
            if (hit is not null) return hit;
            foreach (var f in new[] { "开心", "高兴", "微笑", "害羞", "温柔", "平静", "惊讶", "伤心" })
            {
                var m = _figureEmotions.FirstOrDefault(e => e.Contains(f, StringComparison.Ordinal));
                if (m is not null) return m;
            }
            return null;
        }
        var best = "";
        foreach (var key in _char.SpriteMap.Keys)
        {
            var parts = key.Split('/');
            if (parts.Length != 2 || parts[0] != _outfitKey) continue;
            if (parts[1].Length > best.Length && text.Contains(parts[1], StringComparison.Ordinal))
                best = parts[1];
        }
        if (best.Length > 0) return best;
        foreach (var f in new[] { "开心", "高兴", "微笑", "害羞", "温柔", "平静", "惊讶", "伤心" })
        {
            var m = _char.SpriteMap.Keys.FirstOrDefault(k =>
                k.StartsWith(_outfitKey + "/", StringComparison.Ordinal) && k.Split('/')[1].Contains(f, StringComparison.Ordinal));
            if (m is not null) return m.Split('/')[1];
        }
        return null;
    }

    /// <summary>按提示词列表切表情：第一个命中的优先，全不中则随机挑一个当前服装已有的情绪。</summary>
    private void SetEmotionAny(params string[] hints)
    {
        if (_char is null) return;
        if (_figureActive)
        {
            foreach (var hint in hints)
            {
                var m = _figureEmotions.FirstOrDefault(e => e.Contains(hint, StringComparison.Ordinal));
                if (m is not null) { SetEmotion(m); return; }
            }
            SetEmotion(RandomEmotion() ?? _defaultEmotion);
            return;
        }
        foreach (var hint in hints)
        {
            var m = _char.SpriteMap.Keys.FirstOrDefault(k =>
                k.StartsWith(_outfitKey + "/", StringComparison.Ordinal) && k.Split('/')[1].Contains(hint, StringComparison.Ordinal));
            if (m is not null) { SetEmotion(m.Split('/')[1]); return; }
        }
        SetEmotion(RandomEmotion() ?? _defaultEmotion);
    }

    /// <summary>"随机立绘"：当情绪解析无果、不确定用哪个时，从当前服装的情绪里随机挑一个。</summary>
    private string? RandomEmotion()
    {
        if (_char is null || string.IsNullOrEmpty(_outfitKey)) return null;
        var emotions = _figureActive
            ? _figureEmotions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : _char.SpriteMap.Keys
                .Where(k => k.StartsWith(_outfitKey + "/", StringComparison.Ordinal))
                .Select(k => k.Split('/')[1])
                .Distinct()
                .ToArray();
        if (emotions.Length == 0) return null;
        return emotions[Random.Shared.Next(emotions.Length)];
    }
}
