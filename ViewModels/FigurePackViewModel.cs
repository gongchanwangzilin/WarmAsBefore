using System.Collections.ObjectModel;
using System.IO.Compression;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Dispatching;
using WarmAsBefore.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.Outfit;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 形象包管理：列出 assets/figures 下可读的包，导入/删除 zip，并把包绑定给某个角色。
/// 绑定写的是 CharacterData.FigurePackId（init-only，所以整份 with 后走 UpdateAsync）。
/// 解绑/包损坏/开关关闭时，主界面与工作室都会自动回到旧的 SpriteMap 立绘。
/// </summary>
public sealed partial class FigurePackViewModel : ObservableObject
{
    private readonly FigurePackService _figures;
    private readonly CharacterLibrary _library;
    private readonly GameEngine _engine;
    private readonly SettingsManager _settings;
    private readonly OutfitAssetPipeline _pipeline;
    private readonly ImageGenerationService _images;

    private List<FigurePackInfo> _infos = new();
    private List<CharacterData> _characters = new();
    private string _wantedCharacterId = "";
    private string _wantedPackId = "";
    private string _wantedMotionId = "";
    private string _previewPackId = "";
    private FigurePack? _previewPack;
    private bool _suppressPreview;
    private IDispatcherTimer? _previewTimer;
    private System.Diagnostics.Stopwatch? _previewClock;
    private double _previewElapsedMs;
    private double _previewDistance;
    private double _previewStartPhase;
    private int _previewFrame = -1;
    private int _previewRenderVersion;
    private bool _previewRendering;

    public ObservableCollection<FigurePackRow> Packs { get; } = new();
    public ObservableCollection<FigurePackCharacter> Characters { get; } = new();
    public ObservableCollection<FigurePackOption> PreviewOutfits { get; } = new();
    public ObservableCollection<FigurePackOption> PreviewEmotions { get; } = new();
    public ObservableCollection<FigurePackMotionRow> Motions { get; } = new();

    /// <summary>切片时给新部件的槽位。列表和 figure.json 的 slot 字段同一套名字。</summary>
    public IReadOnlyList<string> Slots { get; } = new[]
    {
        "base", "hair", "top", "bottom", "shoes", "accessory"
    };

    [ObservableProperty] private FigurePackCharacter? _selectedCharacter;
    [ObservableProperty] private string _currentPackLabel = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _hasPacks;

    // ---- 预览 / 动作 ----
    [ObservableProperty] private string _previewTitle = "";
    [ObservableProperty] private ImageSource? _previewSource;
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private FigurePackOption? _selectedPreviewOutfit;
    [ObservableProperty] private FigurePackOption? _selectedPreviewEmotion;
    [ObservableProperty] private FigurePackMotionRow? _selectedMotion;
    [ObservableProperty] private bool _previewPlaying;
    [ObservableProperty] private string _previewStatus = "";

    // ---- AI 素材流水线 ----
    [ObservableProperty] private string _generatePackId = "";
    [ObservableProperty] private string _generateName = "";
    [ObservableProperty] private string _generatePrompt = "";
    [ObservableProperty] private string _sliceRows = "1";
    [ObservableProperty] private string _sliceColumns = "4";
    [ObservableProperty] private string _sliceSlot = "top";
    [ObservableProperty] private string _motionId = "walk";
    [ObservableProperty] private string _motionFps = "8";
    [ObservableProperty] private string _pipelineStatus = "";
    [ObservableProperty] private bool _pipelineBusy;

    public bool HasVideoExtractor => OutfitAssetPipeline.HasVideoFrameExtractor;
    public bool CanPlayMotion => SelectedMotion is { IsStatic: false };
    public string PreviewPlayLabel => PreviewPlaying ? "停止" : "播放";
    public bool CanPreview => _previewPack is not null && HasPreview;

    public FigurePackViewModel(FigurePackService figures, CharacterLibrary library,
        GameEngine engine, SettingsManager settings, OutfitAssetPipeline pipeline,
        ImageGenerationService images)
    {
        _figures = figures;
        _library = library;
        _engine = engine;
        _settings = settings;
        _pipeline = pipeline;
        _images = images;
    }

    /// <summary>从工作室带过来的角色 id；页面第一次装载时用它预选。</summary>
    public void SelectCharacter(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id)) _wantedCharacterId = id;
    }

    public async Task LoadAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            var wanted = !string.IsNullOrWhiteSpace(_wantedCharacterId)
                ? _wantedCharacterId
                : SelectedCharacter?.Id ?? _engine.State.CharacterId;

            _characters = (await _library.ListAsync()).ToList();
            Characters.Clear();
            foreach (var ch in _characters)
                Characters.Add(new FigurePackCharacter
                {
                    Id = ch.Profile.Id,
                    Name = CharacterMask.DisplayName(ch, _settings.Current.MaskUnintroducedNames),
                    FigurePackId = ch.FigurePackId,
                });

            SelectedCharacter = Characters.FirstOrDefault(c => c.Id == wanted)
                                ?? Characters.FirstOrDefault();
            _wantedCharacterId = "";

            _infos = (await _figures.ListPacksAsync()).ToList();
            RebuildRows();
            if (string.IsNullOrWhiteSpace(Status))
                Status = _infos.Count == 0
                    ? "还没有形象包。把 zip 拖进来（或点上面的按钮）就能开始用新版着装。"
                    : $"共 {_infos.Count} 个形象包；绑定后主界面与工作室会一起换成立绘合成。";

            var wantedPack = !string.IsNullOrWhiteSpace(_wantedPackId) ? _wantedPackId : _previewPackId;
            _wantedPackId = "";
            var row = Packs.FirstOrDefault(p => string.Equals(p.Id, wantedPack, StringComparison.OrdinalIgnoreCase))
                      ?? Packs.FirstOrDefault();
            if (row is not null)
                await LoadPreviewPackAsync(row);
            else
                ClearPreview();
        }
        catch (Exception ex)
        {
            Status = "读取失败：" + ex.Message;
            App.WriteLog("FigurePackViewModel.Load -> " + ex);
        }
        finally
        {
            Busy = false;
        }
    }

    partial void OnSelectedCharacterChanged(FigurePackCharacter? value)
    {
        RebuildRows();
        OnPropertyChanged(nameof(HasCharacter));
        OnPropertyChanged(nameof(CanManage));
    }

    public bool HasCharacter => SelectedCharacter is not null;
    public bool CanManage => !Busy && SelectedCharacter is not null;

    private void RebuildRows()
    {
        Packs.Clear();
        var boundId = SelectedCharacter?.FigurePackId ?? "";
        foreach (var info in _infos)
        {
            Packs.Add(new FigurePackRow(info)
            {
                IsBound = string.Equals(info.Id, boundId, StringComparison.OrdinalIgnoreCase),
                IsSelected = string.Equals(info.Id, _previewPackId, StringComparison.OrdinalIgnoreCase),
            });
        }
        HasPacks = Packs.Count > 0;

        if (string.IsNullOrWhiteSpace(boundId))
        {
            CurrentPackLabel = SelectedCharacter is null
                ? "先选一个角色。"
                : "这个角色没有绑定形象包，正在使用旧版立绘素材。";
        }
        else
        {
            var name = _infos.FirstOrDefault(i =>
                string.Equals(i.Id, boundId, StringComparison.OrdinalIgnoreCase))?.Name ?? boundId;
            CurrentPackLabel = SelectedCharacter is null
                ? $"形象包：{name}"
                : $"{SelectedCharacter.Name} 正在使用「{name}」。";
        }
    }

    private async Task<CharacterData?> FindCharacterAsync(string id)
    {
        var list = await _library.ListAsync();
        return list.FirstOrDefault(c => string.Equals(c.Profile.Id, id, StringComparison.Ordinal));
    }

    // ── 预览：选一个包，先看静态合成，再逐个播动作 ─────────────────────

    [RelayCommand]
    private async Task SelectPack(FigurePackRow? row)
    {
        if (row is null || Busy) return;
        await LoadPreviewPackAsync(row);
    }

    private async Task LoadPreviewPackAsync(FigurePackRow row)
    {
        StopPreview();
        _previewRenderVersion++;
        _previewPackId = row.Id;
        foreach (var item in Packs)
            item.IsSelected = ReferenceEquals(item, row);

        PreviewTitle = row.Name;
        PreviewStatus = "正在读取形象包…";
        var pack = await _figures.LoadAsync(row.Id);
        if (pack is null)
        {
            ClearPreview();
            PreviewStatus = "这个形象包的 figure.json 读不出来；主界面与工作室会自动回退旧版立绘。";
            return;
        }

        _previewPack = pack;
        OnPropertyChanged(nameof(CanPreview));
        _suppressPreview = true;
        try
        {
            PreviewOutfits.Clear();
            foreach (var outfit in pack.Outfits)
            {
                if (string.IsNullOrWhiteSpace(outfit.Id)) continue;
                PreviewOutfits.Add(new FigurePackOption(
                    outfit.Id, string.IsNullOrWhiteSpace(outfit.Name) ? outfit.Id : outfit.Name));
            }
            if (PreviewOutfits.Count == 0)
                PreviewOutfits.Add(new FigurePackOption("default", "默认服装"));

            PreviewEmotions.Clear();
            var emotions = pack.Parts
                .SelectMany(p => p.EmotionTextures.Keys)
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!emotions.Contains("normal", StringComparer.OrdinalIgnoreCase))
                emotions.Insert(0, "normal");
            foreach (var emotion in emotions)
                PreviewEmotions.Add(new FigurePackOption(emotion, emotion));

            Motions.Clear();
            Motions.Add(FigurePackMotionRow.Static());
            foreach (var motion in pack.Motions)
            {
                if (motion is null || string.IsNullOrWhiteSpace(motion.Id)) continue;
                Motions.Add(FigurePackMotionRow.From(motion));
            }

            var wantedOutfit = PreviewOutfits.FirstOrDefault(o =>
                string.Equals(o.Id, SelectedPreviewOutfit?.Id, StringComparison.OrdinalIgnoreCase));
            SelectedPreviewOutfit = wantedOutfit ?? PreviewOutfits[0];
            SelectedPreviewEmotion = PreviewEmotions.FirstOrDefault(e =>
                string.Equals(e.Id, SelectedPreviewEmotion?.Id, StringComparison.OrdinalIgnoreCase))
                ?? PreviewEmotions[0];

            // 空字符串是「没有指定」；Static 行的 Id 也是空串，不能拿它去撞，
            // 否则默认会停在静态立绘、播放键永远是灰的。
            var wantedMotion = string.IsNullOrWhiteSpace(_wantedMotionId)
                ? null
                : Motions.FirstOrDefault(m =>
                    string.Equals(m.Id, _wantedMotionId, StringComparison.OrdinalIgnoreCase));
            _wantedMotionId = "";
            SelectedMotion = wantedMotion
                             ?? Motions.FirstOrDefault(m => m.IsWalk)
                             ?? Motions[0];
        }
        finally
        {
            _suppressPreview = false;
        }

        GeneratePackId = pack.Id;
        GenerateName = pack.Name;
        PreviewStatus = pack.Motions.Count == 0
            ? "这个包还没有动作，先看静态合成；导入走路帧后主界面才会动起来。"
            : $"动作 {pack.Motions.Count} 个；播放前会按包里的帧率取样。";
        await RenderPreviewAsync(0);
    }

    private void ClearPreview()
    {
        StopPreview();
        _previewRenderVersion++;
        _previewPack = null;
        _previewPackId = "";
        PreviewTitle = "";
        PreviewSource = null;
        HasPreview = false;
        PreviewStatus = "";
        Motions.Clear();
        PreviewOutfits.Clear();
        PreviewEmotions.Clear();
        SelectedPreviewOutfit = null;
        SelectedPreviewEmotion = null;
        SelectedMotion = null;
        OnPropertyChanged(nameof(CanPreview));
    }

    /// <summary>页面离开时由 code-behind 调用，停掉动作预览，避免后台继续合成。</summary>
    public void StopPlayback() => StopPreview();

    partial void OnSelectedPreviewOutfitChanged(FigurePackOption? value)
    {
        if (_suppressPreview) return;
        _ = RenderPreviewAsync(0);
    }

    partial void OnSelectedPreviewEmotionChanged(FigurePackOption? value)
    {
        if (_suppressPreview) return;
        _ = RenderPreviewAsync(0);
    }

    partial void OnSelectedMotionChanged(FigurePackMotionRow? value)
    {
        StopPreview();
        OnPropertyChanged(nameof(CanPlayMotion));
        if (_suppressPreview) return;
        _ = RenderPreviewAsync(0);
    }

    partial void OnPreviewPlayingChanged(bool value)
    {
        OnPropertyChanged(nameof(PreviewPlayLabel));
    }

    partial void OnHasPreviewChanged(bool value) => OnPropertyChanged(nameof(CanPreview));

    /// <summary>从当前服装/表情/动作取一帧。慢请求回来时用版本号丢弃，防止旧图盖新图。</summary>
    private async Task RenderPreviewAsync(int frameIndex, bool bumpVersion = true)
    {
        if (_previewPack is null) { HasPreview = false; return; }
        if (bumpVersion) _previewRenderVersion++;
        var version = _previewRenderVersion;

        var outfit = SelectedPreviewOutfit?.Id ?? "";
        var emotion = SelectedPreviewEmotion?.Id ?? "normal";
        var motion = SelectedMotion;
        string? path;
        if (motion is null || motion.IsStatic)
        {
            path = await _figures.RenderAsync(_previewPackId, outfit, "", emotion, 540, 960);
        }
        else
        {
            path = await _figures.RenderMotionAsync(
                _previewPackId, outfit, "", motion.Id, frameIndex, 540, 960);
        }

        if (version != _previewRenderVersion) return;
        if (string.IsNullOrWhiteSpace(path))
        {
            HasPreview = false;
            PreviewStatus = "这一帧没有合成成功；检查 figure.json 里的贴图路径是否都在包内。";
            return;
        }

        HasPreview = true;
        PreviewSource = ImageSource.FromFile(path);
        OnPropertyChanged(nameof(CanPreview));
    }

    [RelayCommand]
    private async Task PlayMotion()
    {
        if (SelectedMotion is null || SelectedMotion.IsStatic)
        {
            Toast.Info("先在动作下拉里选一个动作。");
            return;
        }
        if (_previewPack is null) return;
        if (PreviewPlaying)
        {
            StopPreview();
            return;
        }

        _previewElapsedMs = 0;
        _previewDistance = 0;
        _previewStartPhase = 0;
        _previewFrame = -1;
        _previewClock = System.Diagnostics.Stopwatch.StartNew();
        PreviewPlaying = true;

        if (_previewTimer is null)
        {
            _previewTimer = Application.Current?.Dispatcher.CreateTimer();
            if (_previewTimer is null)
            {
                PreviewPlaying = false;
                PreviewStatus = "当前界面没有可用的调度器，动作预览开不了。";
                return;
            }
            _previewTimer.Interval = TimeSpan.FromMilliseconds(33);
            _previewTimer.Tick += (_, _) => _ = TickPreviewAsync();
        }
        _previewTimer.Start();
        await TickPreviewAsync();
    }

    private async Task TickPreviewAsync()
    {
        if (!PreviewPlaying || _previewPack is null || SelectedMotion is not { IsStatic: false } motion)
            return;
        if (_previewRendering || _previewClock is null) return;

        _previewRendering = true;
        try
        {
            var now = _previewClock.Elapsed.TotalMilliseconds;
            var dt = Math.Clamp(now - _previewElapsedMs, 0, 120);
            _previewElapsedMs = now;

            int frame;
            if (FigureMotionPlayback.IsWalk(motion.Motion))
            {
                // 与主界面同一套位移驱动：预览里的步幅按预览图高度换算。
                var bodyHeight = 260.0;
                var step = Math.Max(20, bodyHeight * 0.38);
                var speed = Math.Max(30, bodyHeight * 0.9);
                _previewDistance += speed * dt / 1000.0;
                var phase = WalkCycle.PhaseForDistance(
                    _previewDistance, step, WalkCycle.DefaultStepsPerCycle, _previewStartPhase);
                frame = WalkCycle.FrameIndexAt(phase, motion.FrameCount);
            }
            else
            {
                frame = FigureMotionPlayback.FrameIndexAtElapsedMs(motion.Motion, now);
            }

            if (frame != _previewFrame)
            {
                _previewFrame = frame;
                await RenderPreviewAsync(frame);
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("FigurePackViewModel.PreviewTick -> " + ex);
            StopPreview();
        }
        finally
        {
            _previewRendering = false;
        }
    }

    private void StopPreview()
    {
        _previewTimer?.Stop();
        _previewClock?.Stop();
        _previewClock = null;
        if (PreviewPlaying) PreviewPlaying = false;
    }

    // ── AI 素材流水线：生图 → 切图 → 动作录入 ─────────────────────────

    [RelayCommand]
    private async Task GenerateParts()
    {
        if (PipelineBusy) return;
        var id = (GeneratePackId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            PipelineStatus = "先填一个包 id（例如 figure_maid），或点上面的包选中后再生成。";
            return;
        }
        if (!FigurePackService.IsValidPackId(id))
        {
            PipelineStatus = "包 id 不合法：不能带路径分隔符、..、首尾点或非法文件名字符。";
            return;
        }
        if (!_images.Enabled)
        {
            PipelineStatus = "画面生成没有开启（设置 → AI → 画面生成）。";
            return;
        }
        if (string.IsNullOrWhiteSpace(GeneratePrompt))
        {
            PipelineStatus = "先写一句素材描述，例如：同一位少女的头发、上衣、裙子、鞋子分件。";
            return;
        }
        if (!int.TryParse(SliceRows, out var rows) || rows is < 1 or > 8 ||
            !int.TryParse(SliceColumns, out var columns) || columns is < 1 or > 8)
        {
            PipelineStatus = "行数/列数要在 1–8 之间。";
            return;
        }

        var slot = string.IsNullOrWhiteSpace(SliceSlot) ? "accessory" : SliceSlot;
        PipelineBusy = true;
        PipelineStatus = "正在让生图模型画素材表，并按网格切图…";
        try
        {
            var result = await _pipeline.GeneratePartsAsync(
                id,
                GeneratePrompt,
                new OutfitSliceOptions
                {
                    Rows = rows,
                    Columns = columns,
                    DefaultSlot = slot,
                    PackName = string.IsNullOrWhiteSpace(GenerateName) ? id : GenerateName.Trim(),
                },
                null,
                "1024x1024");

            PipelineStatus = result.Message;
            if (result.Success)
            {
                _figures.Invalidate(id);
                _wantedPackId = id;
                _wantedMotionId = "";
                Status = "AI 素材已写入「" + id + "」。";
                Toast.Success(result.Message);
                await LoadAsync();
            }
            else
            {
                Toast.Warn(result.Message);
            }
        }
        catch (Exception ex)
        {
            PipelineStatus = "生成失败：" + ex.Message;
            Toast.Error(PipelineStatus);
            App.WriteLog("FigurePackViewModel.GenerateParts -> " + ex);
        }
        finally
        {
            PipelineBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportFrames()
    {
        if (PipelineBusy) return;
        if (_previewPack is null)
        {
            Toast.Info("先在「已安装」里点一个包，动作会导入到那个包。");
            return;
        }
        var picks = await FilePicker.Default.PickMultipleAsync(new PickOptions
        {
            PickerTitle = "选择动作帧（按文件名顺序）",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" } },
                { DevicePlatform.Android, new[] { "image/*" } },
                { DevicePlatform.iOS, new[] { "public.image" } }
            })
        });
        var files = picks?.Where(p => p is not null).Select(p => p.FullPath).ToList();
        if (files is null || files.Count == 0) return;

        await ImportMotionCoreAsync(
            (id, fps, loop) => _pipeline.ImportMotionFromFramesAsync(
                _previewPackId, id, files, (int)Math.Round(1000.0 / fps), loop));
    }

    [RelayCommand]
    private async Task ImportVideo()
    {
        if (PipelineBusy) return;
        if (_previewPack is null)
        {
            Toast.Info("先在「已安装」里点一个包，动作会导入到那个包。");
            return;
        }
        if (!HasVideoExtractor)
        {
            Toast.Warn("本机没找到 ffmpeg，视频抽帧不可用；把视频拆成 PNG 帧再导入即可。");
            return;
        }
        var pick = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "选择透明背景视频",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, new[] { ".mp4", ".mov", ".webm", ".mkv" } },
                { DevicePlatform.Android, new[] { "video/*" } },
                { DevicePlatform.iOS, new[] { "public.movie" } }
            })
        });
        if (pick is null) return;

        await ImportMotionCoreAsync(
            (id, fps, loop) => _pipeline.ImportMotionFromVideoAsync(
                _previewPackId, id, pick.FullPath, fps, loop));
    }

    private async Task ImportMotionCoreAsync(
        Func<string, double, bool, Task<MotionImportResult>> import)
    {
        var id = string.IsNullOrWhiteSpace(MotionId) ? "walk" : MotionId.Trim();
        if (!double.TryParse(MotionFps, out var fps) || fps is < 1 or > 30)
        {
            PipelineStatus = "动作帧率要在 1–30 之间。";
            return;
        }

        PipelineBusy = true;
        PipelineStatus = "正在导入动作…";
        try
        {
            var result = await import(id, fps, true);
            PipelineStatus = result.Message;
            if (!result.Success)
            {
                Toast.Warn(result.Message);
                return;
            }

            _figures.Invalidate(_previewPackId);
            _wantedPackId = _previewPackId;
            _wantedMotionId = result.MotionId;
            Status = result.Message;
            Toast.Success(result.Message);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            PipelineStatus = "动作导入失败：" + ex.Message;
            Toast.Error(PipelineStatus);
            App.WriteLog("FigurePackViewModel.ImportMotion -> " + ex);
        }
        finally
        {
            PipelineBusy = false;
        }
    }

    [RelayCommand]
    private async Task Bind(FigurePackRow? row)
    {
        if (row is null || SelectedCharacter is null) return;
        try
        {
            var ch = await FindCharacterAsync(SelectedCharacter.Id);
            if (ch is null) { Toast.Error("找不到这个角色。"); return; }
            if (string.Equals(ch.FigurePackId, row.Id, StringComparison.OrdinalIgnoreCase))
            {
                Toast.Info("这个形象包已经绑在这个角色身上了。");
                return;
            }
            if (!await _library.UpdateAsync(ch with { FigurePackId = row.Id }))
            {
                Toast.Error("绑定失败：角色资料没能写进盘。");
                return;
            }
            _wantedCharacterId = ch.Profile.Id;
            Toast.Success($"已把「{row.Name}」绑给 {SelectedCharacter.Name}。");
            Status = "绑定已保存。回到主界面或工作室就能看到合成立绘。";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Toast.Error("绑定失败：" + ex.Message);
            App.WriteLog("FigurePackViewModel.Bind -> " + ex);
        }
    }

    [RelayCommand]
    private async Task Unbind(FigurePackRow? row)
    {
        if (SelectedCharacter is null) return;
        try
        {
            var ch = await FindCharacterAsync(SelectedCharacter.Id);
            if (ch is null) { Toast.Error("找不到这个角色。"); return; }
            if (string.IsNullOrWhiteSpace(ch.FigurePackId))
            {
                Toast.Info("这个角色本来就没有绑定形象包。");
                return;
            }
            if (!await _library.UpdateAsync(ch with { FigurePackId = "" }))
            {
                Toast.Error("解绑失败：角色资料没能写进盘。");
                return;
            }
            _wantedCharacterId = ch.Profile.Id;
            Toast.Info("已解绑，回到旧版立绘素材。");
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Toast.Error("解绑失败：" + ex.Message);
            App.WriteLog("FigurePackViewModel.Unbind -> " + ex);
        }
    }

    [RelayCommand]
    private async Task Import()
    {
        if (Busy) return;
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择形象包 zip",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".zip" } },
                    { DevicePlatform.Android, new[] { "application/zip" } },
                    { DevicePlatform.iOS, new[] { "public.zip-archive" } }
                })
            });
            if (pick is null) return;

            Busy = true;
            Status = "正在导入形象包…";
            var (id, temp, error) = await ExtractAsync(pick.FullPath);
            if (id is null || temp is null)
            {
                Status = error ?? "导入失败。";
                Toast.Error(Status);
                return;
            }

            var target = Path.Combine(_figures.FiguresRoot, id);
            if (Directory.Exists(target)
                && !await Shell.Current.DisplayAlert("覆盖形象包",
                    $"「{id}」已经存在。用这个 zip 覆盖它吗？覆盖后旧包的贴图会被替换。",
                    "覆盖", "取消"))
            {
                SafeDelete(temp);
                Status = "已取消导入。";
                return;
            }

            try
            {
                Directory.CreateDirectory(_figures.FiguresRoot);
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.Move(temp, target);
            }
            catch (Exception ex)
            {
                SafeDelete(temp);
                throw new IOException("写入形象包目录失败：" + ex.Message, ex);
            }

            _figures.Invalidate(id);
            _wantedCharacterId = SelectedCharacter?.Id ?? "";
            Status = $"形象包「{id}」导入完成。";
            Toast.Success(Status);
            Busy = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Status = "导入失败：" + ex.Message;
            Toast.Error("导入失败：" + ex.Message);
            App.WriteLog("FigurePackViewModel.Import -> " + ex);
        }
        finally
        {
            if (Busy) Busy = false;
        }
    }

    /// <summary>
    /// 解压到 figures 根下的临时目录，返回 (包 id, 临时目录)。所有路径先展开再校验前缀，
    /// 防止 zip 里带 ../ 或绝对路径把文件写到包目录外面。
    /// </summary>
    private async Task<(string? Id, string? Temp, string? Error)> ExtractAsync(string zipPath)
    {
        await Task.Yield();
        var root = _figures.FiguresRoot;
        Directory.CreateDirectory(root);

        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > 5000) return (null, null, "zip 里的文件太多，已拒绝导入。");
        long total = 0;
        foreach (var e in archive.Entries) total += Math.Max(0, e.Length);
        if (total > 512L * 1024 * 1024) return (null, null, "zip 解压后超过 512MB，已拒绝导入。");

        var figureEntries = archive.Entries
            .Where(e => string.Equals(Path.GetFileName(e.FullName), "figure.json", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (figureEntries.Count == 0)
            return (null, null, "这个 zip 里没有 figure.json，不是形象包。");

        var prefix = figureEntries
            .OrderBy(e => e.FullName.Count(c => c == '/'))
            .ThenBy(e => e.FullName.Length)
            .First()
            .FullName;
        prefix = prefix[..^"figure.json".Length].TrimEnd('/', '\\');

        var id = !string.IsNullOrWhiteSpace(prefix) && !prefix.Contains('/') && !prefix.Contains('\\')
            ? prefix.Trim()
            : Path.GetFileNameWithoutExtension(zipPath).Trim();
        id = SanitizePackId(id);
        if (!FigurePackService.IsValidPackId(id))
            id = "figure_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

        var temp = Path.Combine(root, ".import-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(temp);
        var tempFull = Path.GetFullPath(temp) + Path.DirectorySeparatorChar;
        try
        {
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // 目录项
                var full = entry.FullName.Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(prefix)
                    && !full.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
                    continue;

                var rel = string.IsNullOrWhiteSpace(prefix)
                    ? full
                    : full[(prefix.Length + 1)..];
                if (string.IsNullOrWhiteSpace(rel) || rel.Contains("..", StringComparison.Ordinal))
                    continue;

                var dest = Path.GetFullPath(Path.Combine(temp, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!dest.StartsWith(tempFull, StringComparison.OrdinalIgnoreCase)) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, true);
            }

            if (!File.Exists(Path.Combine(temp, "figure.json")))
                throw new InvalidDataException("zip 里的 figure.json 不在可识别的包根目录。");
            return (id, temp, null);
        }
        catch (Exception ex)
        {
            SafeDelete(temp);
            return (null, null, "解压失败：" + ex.Message);
        }
    }

    private static string SanitizePackId(string raw)
    {
        var name = (raw ?? "").Trim();
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim('.', ' ');
        return name.Length > 80 ? name[..80] : name;
    }

    private static void SafeDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch (Exception ex) { App.WriteLog("FigurePackViewModel.SafeDelete -> " + ex.Message); }
    }

    [RelayCommand]
    private async Task Delete(FigurePackRow? row)
    {
        if (row is null) return;
        try
        {
            var users = _characters
                .Where(c => string.Equals(c.FigurePackId, row.Id, StringComparison.OrdinalIgnoreCase))
                .Select(c => CharacterMask.DisplayName(c, _settings.Current.MaskUnintroducedNames))
                .ToList();
            var extra = users.Count == 0
                ? ""
                : $"\n\n正在使用它的角色：{string.Join("、", users.Take(6))}"
                  + (users.Count > 6 ? $" 等 {users.Count} 位" : "")
                  + "。删除后他们会自动回到旧版立绘。";
            if (!await Shell.Current.DisplayAlert("删除形象包",
                    $"确定删除「{row.Name}」及其全部贴图与缓存吗？这个操作不可恢复。{extra}",
                    "删除", "取消"))
                return;

            var dir = _figures.PackDirectory(row.Id);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                Directory.Delete(dir, true);
            _figures.Invalidate(row.Id);
            _wantedCharacterId = SelectedCharacter?.Id ?? "";
            Status = $"已删除形象包「{row.Name}」。";
            Toast.Success(Status);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Toast.Error("删除失败：" + ex.Message);
            App.WriteLog("FigurePackViewModel.Delete -> " + ex);
        }
    }

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");
}

/// <summary>形象包列表行：IsBound 只影响按钮文案与禁用态，重载时整行重建。</summary>
public sealed partial class FigurePackRow : ObservableObject
{
    public FigurePackRow(FigurePackInfo info)
    {
        Id = info.Id;
        Name = string.IsNullOrWhiteSpace(info.Name) ? info.Id : info.Name;
        FolderPath = info.FolderPath;
        Detail = $"部件 {info.PartCount} · 服装 {info.OutfitCount} · 配色 {info.SchemeCount} · 动作 {info.MotionCount}";
    }

    public string Id { get; }
    public string Name { get; }
    public string FolderPath { get; }
    public string Detail { get; }
    public bool IsBound { get; set; }
    public bool CanBind => !IsBound;
    public string BindLabel => IsBound ? "已绑定" : "绑定给 TA";
    [ObservableProperty] private bool _isSelected;
}

/// <summary>服装/表情下拉项：Id 传给合成器，Label 给人看。</summary>
public sealed class FigurePackOption
{
    public FigurePackOption(string id, string label)
    {
        Id = id;
        Label = string.IsNullOrWhiteSpace(label) ? id : label;
    }

    public string Id { get; }
    public string Label { get; }
    public override string ToString() => Label;
}

/// <summary>动作下拉项。第一项是「静态立绘」，用来在不播动作时检查服装与表情。</summary>
public sealed class FigurePackMotionRow
{
    private FigurePackMotionRow(string id, string label, int frameCount, FigureMotion? motion)
    {
        Id = id;
        Label = label;
        FrameCount = frameCount;
        Motion = motion;
    }

    public string Id { get; }
    public string Label { get; }
    public int FrameCount { get; }
    public FigureMotion? Motion { get; }
    public bool IsStatic => Motion is null;
    public bool IsWalk => FigureMotionPlayback.IsWalk(Motion);
    public override string ToString() => Label;

    public static FigurePackMotionRow Static() => new("", "静态立绘", 0, null);

    public static FigurePackMotionRow From(FigureMotion motion)
    {
        var label = string.IsNullOrWhiteSpace(motion.Name) ? motion.Id : motion.Name;
        var loop = motion.Loop ? "循环" : "单次";
        return new FigurePackMotionRow(
            motion.Id, $"{label} · {motion.Frames.Count} 帧 · {loop}", motion.Frames.Count, motion);
    }
}

/// <summary>形象包页的角色下拉项：名字按掩名规则显示，绑定状态写在副标题里。</summary>
public sealed class FigurePackCharacter
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string FigurePackId { get; init; } = "";
    public string Label => string.IsNullOrEmpty(FigurePackId) ? Name : Name + " · 已绑形象包";
    public override string ToString() => Label;
}
