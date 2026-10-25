using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 角色工作室页：列表页那一行点进来就是这一屏。
///
/// 这页只负责三件事，「怎么改」全在面板和它的 VM 里：
/// · 把谁请进来（id / 新建草稿），并把「这一屏有多宽」交给面板去排版；
/// · 按「关闭」或系统返回键离开时，改动还没落盘就问一句——离开一页比点掉一个弹窗容易得多；
/// · 新建的人真的进了名册、且是从「开始新的一局」那条路进来的，就照旧直接开一局。
/// </summary>
[QueryProperty(nameof(TargetId), "id")]
[QueryProperty(nameof(NewDraft), "new")]
[QueryProperty(nameof(StartAfter), "start")]
public partial class CharacterStudioPage : ContentPage
{
    private readonly CharacterStudioViewModel _vm;
    private readonly Services.GameEngine _engine;
    private readonly Modules.SaveSystem.SaveManager _save;
    private bool _loaded;
    private bool _narrow;
    private bool _narrowSet;
    private bool _startAfterCreate;
    private bool _leaving;

    public CharacterStudioPage(CharacterStudioViewModel vm, Services.GameEngine engine,
        Modules.SaveSystem.SaveManager save)
    {
        InitializeComponent();
        _vm = vm;
        _engine = engine;
        _save = save;
        BindingContext = vm;
        Studio.BindingContext = vm;
        vm.CloseRequested = CloseAsync;
        vm.Created = id => _ = StartRunAsync(id);
    }

    public string TargetId { get; set; } = "";
    public string NewDraft { get; set; } = "";
    public string StartAfter { get; set; } = "";

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;

        _startAfterCreate = StartAfter == "1";
        StartAfter = "";

        if (NewDraft == "1")
        {
            NewDraft = "";
            _vm.OpenNew();
        }
        else if (!await _vm.OpenAsync(TargetId))
        {
            // 这个人不在盘上了（被删了、id 传错了）。留一张空工作室只会让人以为程序坏了，
            // 回列表去：那里读得见「现在到底有谁」。
            await Shell.Current.GoToAsync("..");
            return;
        }
        await Studio.OpenAsync();
    }

    /// <summary>
    /// 分档读的是这一屏量出来的宽度，不是设备类型：桌面上把窗口拖窄时，
    /// 左边那条 336px 的立绘会把右边的表单挤成一列字，这时它要的是竖排 —— 和手机同一套。
    /// 只在真的跨档那一次改方向：SizeChanged 每一像素都来一次，排版整屏重算不能跟着抖。
    /// </summary>
    private void OnStudioResized(object? sender, EventArgs e)
    {
        if (Studio.Width <= 0) return;
        // 352 的舞台之外，右栏至少还要放得下一整行字段；700 时右栏只剩 348，
        // 标签和输入框会被挤成一列字，所以宁可早一点转成上下排。
        var narrow = Studio.Width < 820;
        if (_narrowSet && narrow == _narrow) return;
        _narrow = narrow;
        _narrowSet = true;
        Studio.SetNarrow(narrow);
    }

    private async Task CloseAsync()
    {
        if (_leaving) return;
        if (_vm.IsDirty)
        {
            var leave = await Shell.Current.DisplayAlert("改动还没保存",
                $"「{_vm.DisplayName}」这一屏上有没保存的改动。\n离开就会丢掉它们，盘上还是原来那一版。",
                "丢掉并离开", "继续改");
            if (!leave) return;
        }
        _leaving = true;
        await Studio.CloseAsync();
        await Shell.Current.GoToAsync("..");
    }

    /// <summary>
    /// 草稿真的进了名册，且这一趟是「建好就开一局」：照旧先 Boot 再 SetCharacter——
    /// 顺序反了会把刚写进去的角色清掉（GameEngine.Boot 重置整份状态）。
    /// </summary>
    private async Task StartRunAsync(string id)
    {
        if (!_startAfterCreate) return;
        _startAfterCreate = false;
        _engine.Boot();
        _engine.SetCharacter(id);
        if (_engine.ActiveCharacter is { } fresh) Services.CharacterLibrary.RandomizeSpriteState(fresh);
        _save.NewRun();
        _leaving = true;
        await Shell.Current.GoToAsync("main");
    }

    protected override bool OnBackButtonPressed()
    {
        if (!_vm.IsDirty) return false;
        _ = CloseAsync();
        return true;
    }
}
