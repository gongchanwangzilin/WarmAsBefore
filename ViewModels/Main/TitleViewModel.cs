using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Helpers;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

public sealed partial class TitleViewModel : ObservableObject
{
    private readonly GameEngine _engine;
    private readonly SettingsManager _settings;
    private readonly DesignSystem.Theme.ThemeManager _theme;
    private readonly SaveManager _save;
    private readonly CharacterLibrary _library;
    private readonly NovelLibrary _novels;

    [ObservableProperty] private bool _hasSave;
    [ObservableProperty] private string _startLabel = "";

    // 「上次的存档」预览（主页空着的下半屏）：有存档才显示。
    [ObservableProperty] private bool _hasRecent;
    [ObservableProperty] private string _recentName = "";
    [ObservableProperty] private string _recentScene = "";
    [ObservableProperty] private string _recentMeta = "";

        [ObservableProperty] private bool _menuLeft = true;
        [ObservableProperty] private string _themeEffectName = "";
        [ObservableProperty] private string _themeColorName = "经典";
        [ObservableProperty] private int _menuColumn;
        [ObservableProperty] private int _decorColumn = 1;
        [ObservableProperty] private bool _showDisclaimer = true;
        /// <summary>
        /// 启动卡。一趟进程只放一次：标题页每次导航都是新建的（DI 里是 Transient），
        /// 而「启动」按进程算，不按页面实例算 —— 从设置页返回时再放一遍开机动画就成了故障。
        /// </summary>
        [ObservableProperty] private bool _showIntro = !Helpers.IntroCard.HasPlayed;

    public TitleViewModel(GameEngine engine, SettingsManager settings,
        DesignSystem.Theme.ThemeManager theme, SaveManager save,
        CharacterLibrary library, NovelLibrary novels)
    {
        _engine = engine;
        _settings = settings;
        _theme = theme;
        _save = save;
        _library = library;
        _novels = novels;
        ApplySettings();
        _settings.Applied += ApplySettings;
        _theme.Changed += ApplySettings;
        // 语言切换时刷新 VM 侧本地化文本
        LocalizationService.Current.PropertyChanged += (_, _) => MainThread.BeginInvokeOnMainThread(() =>
        {
            ApplySettings();
            RefreshStateCommand.Execute(null);
        });
        // 首次启动免责声明 + 非玩具声明：已确认过则不再弹。
        // 用 Preferences 作为权威来源：settings.json 是「整份快照覆盖写」，
        // 任何一个用旧快照落盘的路径都会把确认标记擦掉（用户实测每次启动都弹就是这个原因）。
        ShowDisclaimer = !IsDisclaimerAcknowledged();
        RefreshStateCommand.Execute(null);
    }

    /// <summary>是否已确认过免责声明（Preferences 优先，兼容旧的设置字段）。</summary>
    private bool IsDisclaimerAcknowledged()
    {
        try
        {
            if (Preferences.Get(DisclaimerPrefKey, false)) return true;
            return _settings.Current.DisclaimerAcknowledged;
        }
        catch (Exception ex)
        {
            App.WriteLog("Title: IsDisclaimerAcknowledged EX -> " + ex.Message);
            return _settings.Current.DisclaimerAcknowledged;
        }
    }

    private const string DisclaimerPrefKey = "disclaimer_ack";

    [RelayCommand]
    private async Task AcknowledgeDisclaimer()
    {
        ShowDisclaimer = false;
        try
        {
            // 先落到不会被覆盖的地方，再同步设置字段（best effort）
            Preferences.Set(DisclaimerPrefKey, true);
            _settings.Apply(_settings.Current with { DisclaimerAcknowledged = true });
            await _settings.Persist();
        }
        catch (Exception ex) { App.WriteLog("Title: AcknowledgeDisclaimer EX -> " + ex); }

        OpenSetupIfPending();
    }

    /// <summary>
    /// 没走过首次配置的人，在这里被交给配置页。两个入口共用这一条：
    /// 启动卡收掉时（条款早就确认过的老用户），和按下「同意并继续」时（真正的第一次）。
    /// </summary>
    public void OpenSetupIfPending()
    {
        try
        {
            if (ShowDisclaimer) return;                                  // 先谈条款，再谈配置
            if (Preferences.Get(SetupViewModel.DonePrefKey, false)) return;
            _ = Shell.Current.GoToAsync("setup");
        }
        catch (Exception ex) { App.WriteLog("Title: OpenSetup EX -> " + ex.Message); }
    }

    /// <summary>不同意免责声明：直接退出应用（用户明确要求「不同意则退出」）。</summary>
    [RelayCommand]
    private void DeclineDisclaimer()
    {
        try
        {
#if ANDROID
            Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
#else
            Application.Current?.Quit();
#endif
        }
        catch (Exception ex)
        {
            App.WriteLog("Title: DeclineDisclaimer EX -> " + ex.Message);
        }
    }

    /// <summary>有存档 → 「再续情缘」（读最后一次游玩的存档直接继续）；无存档 → 「创建新欢」（新开一局）。</summary>
    [RelayCommand]
    private async Task RefreshState()
    {
        try
        {
            var slots = await _save.List();
            HasSave = slots.Count > 0;
            StartLabel = HasSave
                ? LocalizationService.Current["Title_Continue"]
                : LocalizationService.Current["Title_Start"];

            // 空着的大片留白放「上次的存档」：点它就是继续，和那颗胶囊同一个去处，
            // 但先让人看见「上一次是谁、在哪儿」——标题页不该只有一个按钮。
            var last = slots.OrderByDescending(s => s.SavedAt).FirstOrDefault();
            if (last is not null)
            {
                HasRecent = true;
                RecentName = string.IsNullOrWhiteSpace(last.Label) ? last.Character : last.Label;
                RecentScene = last.Scene;
                RecentMeta = $"{last.SavedAt.ToLocalTime():MM 月 dd 日 HH:mm} · {last.PlayMinutes} 分钟";
            }
            else
            {
                HasRecent = false;
            }
        }
        catch (Exception ex) { App.WriteLog("Title: RefreshState EX -> " + ex); }
    }

    private void ApplySettings()
    {
        MenuLeft = _settings.Current.MenuSide != "right";
        MenuColumn = MenuLeft ? 0 : 1;
        DecorColumn = MenuLeft ? 1 : 0;
        // 名字取自世界本身，不在这里另立一套叫法：设置里那个外观下拉框用的就是同一个 Display，
        // 两处不同名就成了「设置里叫柔和渐变，首页上叫毛玻璃」。
        // 只发材质名：这一串在页面上是「配色 · 风格」读数的右半，前面还有一道分隔符兜着，
        // 自己再带一个「当前主题 ·」就成了「当前 · 晨雾蓝灰 · 当前主题 · 液态玻璃」。
        ThemeEffectName = DesignSystem.Worlds.StyleWorldTokens.Display(_theme.World);
        ThemeColorName = DesignSystem.Theme.ThemeManager.ThemeDisplay(_theme.ThemeName);
    }

    [RelayCommand]
    private async Task StartGame()
    {
        App.WriteLog("Title: StartGame clicked, HasSave=" + HasSave);
        // 进去要用的名册先读起来，但不挡路：验证过场没了，导航不再等这段预载。
        _ = Prefetch(Gate.Universe);
        // 有存档：再续情缘——直接读最后一次游玩的存档继续；无存档：创建新欢（新开一局）
        if (HasSave)
        {
            try
            {
                if (await _save.LoadLatest())
                {
                    await Shell.Current.GoToAsync("main");
                    return;
                }
            }
            catch (Exception ex) { App.WriteLog("Title: LoadLatest EX -> " + ex); }
            // 读不到最后一局就交给存档页。这里直连、不再走 OpenGate：
            // 刚念完一串「载入模拟宇宙」，接着又念一串「挂载存档库」，读起来像程序没听懂那一句。
            try { await Shell.Current.GoToAsync("save"); }
            catch (Exception ex) { App.WriteLog("Title: StartGame->save EX -> " + ex); }
            return;
        }
        try
        {
            await Shell.Current.GoToAsync("select");
            App.WriteLog("Title: StartGame navigation ok");
        }
        catch (Exception ex) { App.WriteLog("Title: StartGame EX -> " + ex); }
    }

    [RelayCommand]
    private Task OpenSave() => OpenGate(Gate.Saves, "save");

    [RelayCommand]
    private Task OpenSettings() => OpenGate(Gate.Settings, "settings");

    /// <summary>平行宇宙：进入已导入的小说世界选择页。</summary>
    [RelayCommand]
    private Task OpenNovels() => OpenGate(Gate.Novels, "novelselect");

    /// <summary>角色库：管理所有角色，AI 可调用其他角色，也可让 AI 改写人设。</summary>
    [RelayCommand]
    private Task OpenRoster() => OpenGate(Gate.Roster, "roster");

    /// <summary>一扇门共用这一条：先记一笔日志，再去预载，然后立刻走——不再有等人看完的过场。</summary>
    private async Task OpenGate(Gate gate, string route)
    {
        App.WriteLog($"Title: open {route} clicked");
        _ = Prefetch(gate);
        try
        {
            await Shell.Current.GoToAsync(route);
            App.WriteLog($"Title: open {route} nav ok");
        }
        catch (Exception ex) { App.WriteLog($"Title: open {route} EX -> " + ex); }
    }

    /// <summary>
    /// 下一页要用的名册先读起来。导航不等它 —— 它只是让磁盘先转，
    /// 读失败也不该影响「门已经开了」这件事。
    /// </summary>
    private async Task Prefetch(Gate gate)
    {
        try
        {
            switch (gate)
            {
                case Gate.Universe:
                case Gate.Saves:
                case Gate.Roster:
                    await _library.ListAsync();
                    break;
                case Gate.Novels:
                    await _novels.ListAsync();
                    break;
            }
        }
        catch (Exception ex) { App.WriteLog("Title: Prefetch EX -> " + ex.Message); }
    }

    /// <summary>预载要看哪一扇门。只为「读什么」而存在，和界面上的入口一一对应。</summary>
    private enum Gate { Universe, Saves, Settings, Novels, Roster }

    [RelayCommand]
    private async Task ExitGame()
    {
        App.WriteLog("Title: ExitGame clicked");
        Application.Current?.Quit();
    }
}
