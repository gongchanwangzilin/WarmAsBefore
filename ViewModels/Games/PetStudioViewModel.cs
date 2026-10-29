using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 「AI 制作桌宠」工作室：选角色 → 一键把立绘 Q 版化并逐状态生成一整套状态动画 →
/// 逐个出图时就地预览 → 完成后告知素材包已就绪。
///
/// 进度是真的：只由 <see cref="PetStudioService"/> 每出一帧回报一次驱动，绝不用定时器编一个假动画。
/// 生成可取消；单帧失败不整体失败（服务里已重试一次并跳过）。
/// </summary>
public sealed partial class PetStudioViewModel : ObservableObject
{
    private readonly PetStudioService _studio;
    private readonly CharacterLibrary _library;
    private readonly SettingsManager _settings;
    private readonly GameEngine _engine;

    private CancellationTokenSource? _cts;
    private string _wantedCharacterId = "";

    public ObservableCollection<PetStudioCharacterOption> Characters { get; } = new();
    public ObservableCollection<PetStudioStateRow> States { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private PetStudioCharacterOption? _selectedCharacter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private bool _busy;

    [ObservableProperty] private string _status = "";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _canCancel;
    [ObservableProperty] private double _progress;          // 0~1，真实帧进度
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _resultMessage = "";
    [ObservableProperty] private bool _packReady;
    [ObservableProperty] private string _walkCadenceText = "";
    [ObservableProperty] private string _imageHint = "";
    [ObservableProperty] private int _canvasHeight;

    private bool _imageReady;

    public bool CanGenerate => !_busy && SelectedCharacter is not null && _imageReady;
    public bool CanCancelRunning => _canCancel;

    /// <summary>角色库为空时给右侧列表一句提示（装载后手动通知）。</summary>
    public bool NoCharacters => Characters.Count == 0;

    public PetStudioViewModel(
        PetStudioService studio,
        CharacterLibrary library,
        SettingsManager settings,
        GameEngine engine)
    {
        _studio = studio;
        _library = library;
        _settings = settings;
        _engine = engine;
    }

    /// <summary>从别处（角色工作室）带过来的角色 id；第一次装载时预选它。</summary>
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
            _imageReady = ImagesReady();
            ImageHint = _imageReady
                ? ""
                : "画面生成还没开启（设置 → AI → 画面生成 里填好接口与密钥），现在没法出图。";

            var wanted = !string.IsNullOrWhiteSpace(_wantedCharacterId)
                ? _wantedCharacterId
                : SelectedCharacter?.Id ?? _engine.State.CharacterId;

            var options = new List<PetStudioCharacterOption>();
            foreach (var ch in await _library.ListAsync())
            {
                options.Add(new PetStudioCharacterOption
                {
                    Id = ch.Profile.Id,
                    Name = string.IsNullOrWhiteSpace(ch.Profile.Name) ? "未命名角色" : ch.Profile.Name.Trim(),
                    HasSprite = ch.SpriteMap.Count > 0,
                });
            }
            Characters.Clear();
            foreach (var opt in options) Characters.Add(opt);
            OnPropertyChanged(nameof(NoCharacters));
            var pick = options.FirstOrDefault(c => c.Id == wanted) ?? options.FirstOrDefault();
            SelectedCharacter = pick;   // 触发 OnSelectedCharacterChanged 统一点亮 IsSelected
            _wantedCharacterId = "";

            CanvasHeight = Math.Clamp(_settings.Current.PetStudioCanvasHeight, 96, 720);
            RefreshCadencePreview();
            await ShowExistingForAsync(SelectedCharacter?.Id ?? "");

            if (_imageReady)
                Status = "挑一个角色，点「一键生成桌宠动画」：代码会先算好走动的帧数与步频，再逐状态串行出图、每出一帧就点亮一行。";
        }
        catch (Exception ex)
        {
            Status = "读取失败：" + ex.Message;
            App.WriteLog("PetStudioVM.Load -> " + ex);
        }
        finally
        {
            Busy = false;
            OnPropertyChanged(nameof(CanGenerate));
        }
    }

    private bool ImagesReady()
    {
        var s = _settings.Current;
        return s.ImageGenEnabled
               && !string.IsNullOrWhiteSpace(s.ImageGenUrl)
               && !string.IsNullOrWhiteSpace(s.ImageGenKey);
    }

    private void RefreshCadencePreview()
    {
        var c = _studio.PlanWalkCadence();
        WalkCadenceText = $"走动循环：{c}（按步频算，非写死）";
    }

    partial void OnSelectedCharacterChanged(PetStudioCharacterOption? value)
    {
        OnPropertyChanged(nameof(CanGenerate));
        foreach (var opt in Characters)
            opt.IsSelected = ReferenceEquals(opt, value);
        _ = ShowExistingForAsync(value?.Id ?? "");
    }

    /// <summary>点角色行选它：生成中不允许改（否则进度会张冠李戴）。</summary>
    [RelayCommand]
    private void PickCharacter(PetStudioCharacterOption? option)
    {
        if (option is null || Busy) return;
        if (!ReferenceEquals(SelectedCharacter, option)) SelectedCharacter = option;
    }

    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanGenerate));
    partial void OnCanCancelChanged(bool value) => OnPropertyChanged(nameof(CanCancelRunning));

    private async Task ShowExistingForAsync(string charId)
    {
        if (string.IsNullOrWhiteSpace(charId))
        {
            States.Clear();
            PackReady = false;
            Progress = 0;
            ProgressText = "";
            return;
        }
        await ReflectPackAsync(await _studio.LoadPackAsync(charId));
    }

    /// <summary>把一个素材包的状态逐行填进列表：读得到缩略图就预览，读不到留空。</summary>
    private Task ReflectPackAsync(PetAnimationPack? pack)
    {
        States.Clear();
        if (pack is null || pack.States.Count == 0)
        {
            PackReady = false;
            Progress = 0;
            ProgressText = "";
            foreach (var def in PetStudioService.DefaultStates)
            {
                var frames = def.IsLocomotion ? _studio.PlanWalkCadence().Frames : Math.Max(1, def.FixedFrames);
                States.Add(new PetStudioStateRow { Id = def.Id, Name = def.Name, FrameTotal = frames });
            }
            return Task.CompletedTask;
        }

        foreach (var def in PetStudioService.DefaultStates)
        {
            var st = pack.GetState(def.Id);
            var row = new PetStudioStateRow
            {
                Id = def.Id,
                Name = def.Name,
                FrameTotal = def.IsLocomotion ? _studio.PlanWalkCadence().Frames : Math.Max(1, def.FixedFrames),
                FrameCount = st?.FrameCount ?? 0,
                Fps = st?.Fps ?? 0,
                Loop = st?.Loop ?? def.Loop,
                Done = st is { Frames.Count: > 0 },
            };
            States.Add(row);
            if (st is { Frames.Count: > 0 })
            {
                var abs = _studio.ResolveAbs(st.Frames[0]);
                if (File.Exists(abs)) row.Preview = ImageSource.FromFile(abs);
            }
        }
        PackReady = true;
        Progress = 1;
        ProgressText = $"已有素材包：{pack.States.Count(s => s.FrameCount > 0)} 个状态 · {pack.States.Sum(s => s.FrameCount)} 帧。";
        return Task.CompletedTask;
    }

    // ==================== 生成 ====================

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        var sel = SelectedCharacter;
        if (sel is null || Busy) return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Busy = true;
        CanCancel = true;
        PackReady = false;
        ResultMessage = "";
        Progress = 0;
        ProgressText = "";

        // 先把状态列表立起来（每行「待生成」），出帧时逐行点亮——排期看得见，进度才落得下。
        States.Clear();
        foreach (var def in PetStudioService.DefaultStates)
        {
            var frames = def.IsLocomotion ? _studio.PlanWalkCadence().Frames : Math.Max(1, def.FixedFrames);
            States.Add(new PetStudioStateRow { Id = def.Id, Name = def.Name, FrameTotal = frames });
        }
        RefreshCadencePreview();
        Status = $"正在为「{sel.Name}」生成桌宠动画…（随时可取消）";
        var progress = new Progress<PetStudioService.PetStudioProgress>(OnProgress);

        try
        {
            var result = await _studio.GenerateAsync(sel.Id, progress, token);
            ResultMessage = result.Message;
            if (result.Pack is not null && result.GeneratedStates > 0)
            {
                await ReflectPackAsync(result.Pack);
                Status = result.Cancelled ? "已取消，但画好的部分已存进素材包。" : "桌宠素材包已就绪。";
                // 她这会儿就站在桌面上的话，不用重进桌宠也能立刻换上新一套帧
                Services.PetService.ReloadAnimation();
            }
            else
            {
                Status = result.Message;
            }
        }
        catch (OperationCanceledException)
        {
            // 取消也把已经出来的帧如实呈现：服务逐帧落盘，重读一次就能看到半成品。
            await ReflectPackAsync(await _studio.LoadPackAsync(sel.Id));
            Services.PetService.ReloadAnimation();
            ResultMessage = "已取消生成。画好的状态都留在素材包里，随时可以接着补。";
            Status = ResultMessage;
        }
        catch (Exception ex)
        {
            Status = "生成出错：" + ex.Message;
            App.WriteLog("PetStudioVM.Generate -> " + ex);
        }
        finally
        {
            Busy = false;
            CanCancel = false;
            _cts?.Dispose();
            _cts = null;
            OnPropertyChanged(nameof(CanGenerate));
        }
    }

    private void OnProgress(PetStudioService.PetStudioProgress p)
    {
        Progress = Math.Clamp(p.Overall, 0, 1);
        ProgressText = $"{p.DoneCalls}/{p.TotalCalls} 帧 · 整体 {(p.Overall * 100):0}%";
        Status = p.Message;

        var row = States.FirstOrDefault(r => r.Id == p.StateId);
        if (row is null) return;
        row.FrameIndex = p.FrameIndex;
        row.FrameTotal = Math.Max(row.FrameTotal, p.FrameTotal);

        if (p.Failed) { row.FailedFrames++; return; }
        if (p.LastFrameRel is { Length: > 0 })
        {
            var abs = _studio.ResolveAbs(p.LastFrameRel);
            if (File.Exists(abs)) row.Preview = ImageSource.FromFile(abs);
            row.FrameCount = p.FrameIndex;   // 已出帧号即帧数（1 基恰好对上）
            row.Done = p.FrameIndex >= row.FrameTotal;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelRunning))]
    private void Cancel()
    {
        try { _cts?.Cancel(); } catch { }
        Status = "正在取消…（会把手上这一帧画完再停）";
    }

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");

    /// <summary>页面消失/切走时确保取消在跑的生成，不留后台任务。</summary>
    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
    }
}

/// <summary>工作室里可选的一位角色（做成 ObservableObject，让「当前选中」能在列表里点亮）。</summary>
public sealed partial class PetStudioCharacterOption : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool HasSprite { get; init; }
    public string Hint => HasSprite ? "有立绘 · 可 Q 版化" : "暂无立绘 · 也能凭设定生成";

    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// 一行的状态进度与预览：等待 / 出帧中 / 完成 / 缺帧。绑定到工作室右侧的状态列表。
/// 单独做成 ObservableObject 是为了让某一行出帧时只刷那一行。
/// </summary>
public sealed partial class PetStudioStateRow : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    [ObservableProperty] private int _frameCount;
    [ObservableProperty] private int _frameTotal;
    [ObservableProperty] private int _frameIndex;
    [ObservableProperty] private double _fps;
    [ObservableProperty] private bool _loop;
    [ObservableProperty] private bool _done;
    [ObservableProperty] private int _failedFrames;
    [ObservableProperty] private ImageSource? _preview;

    public bool HasPreview => Preview is not null;
    public string Meta => FrameTotal > 0 ? $"{FrameCount}/{FrameTotal} 帧" : "待生成";
    public string StateText => FailedFrames > 0
        ? $"出好 {FrameCount} 帧 · 缺 {FailedFrames} 帧"
        : Done ? (FrameTotal > 1 ? $"{FrameCount} 帧 · {(Loop ? "循环" : "一次性")}" : "已出图")
        : FrameIndex > 0 ? "出帧中…" : "待生成";

    partial void OnPreviewChanged(ImageSource? value) => OnPropertyChanged(nameof(HasPreview));
    partial void OnFrameCountChanged(int value)
    {
        OnPropertyChanged(nameof(Meta));
        OnPropertyChanged(nameof(StateText));
    }
    partial void OnFrameTotalChanged(int value) => OnPropertyChanged(nameof(Meta));
    partial void OnFailedFramesChanged(int value) => OnPropertyChanged(nameof(StateText));
    partial void OnDoneChanged(bool value) => OnPropertyChanged(nameof(StateText));
    partial void OnFrameIndexChanged(int value) => OnPropertyChanged(nameof(StateText));
}
