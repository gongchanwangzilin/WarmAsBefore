using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Graphics;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.DesignSystem.Worlds;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 首次配置里的一颗可选项。序号是它的身份（命令只交回一个 int），
/// 文字随语言走，选中态由外面管。
/// <see cref="Pick"/> 带着自己该叫谁：四组选项共用同一份行模板，命令就不用在 XAML 里写四遍。
/// </summary>
public sealed partial class SetupOption : ObservableObject
{
    public int Index { get; init; }

    /// <summary>不随语言变的真值：文化代码、世界名、色板键、分钟数。</summary>
    public string Value { get; init; } = "";

    /// <summary>点这一颗该执行什么。</summary>
    public ICommand? Pick { get; init; }

    /// <summary>色板预览：一条色卡。只给色板那组填，其余为空。</summary>
    public IReadOnlyList<Color> Swatches { get; init; } = [];
    public bool HasSwatches => Swatches.Count > 0;

    /// <summary>材质预览：一枚圆角样卡的圆角。只给材质那组填，为 0 表示不画。</summary>
    public double SampleRadius { get; init; }
    public bool HasSample => SampleRadius > 0;

    [ObservableProperty] private string _label = "";
    [ObservableProperty] private bool _selected;
}

/// <summary>
/// 首次配置（OOBE）：一屏一件事，选完立刻生效 —— 语言、材质、色板都是当场能看到结果的，
/// 这也是它和设置页的分工：设置页是「来调」，这里是「来选」。
///
/// 版式借 Windows 的那一套：地面上只浮一张卡，左上角一枚裸返回箭头，标题大而不粗，
/// 主行动是右下角一颗实心、它的左边一枚文字按钮，工具位钉在整页的最右下角。
/// 借的是骨架，不是内容 —— 这里没有微软账号，也没有 PIN 和隐私球。
///
/// 全部落点都是已有的设置字段，一个新字段都不加。
/// </summary>
public sealed partial class SetupViewModel : ObservableObject
{
    /// <summary>走没走过首次配置。和免责声明一样只认 Preferences：settings.json 是整份快照覆盖写。</summary>
    public const string DonePrefKey = "setup_done";

    private const int StepLang = 0, StepStyle = 1, StepReside = 2, StepAi = 3, StepDone = 4;

    private readonly SettingsManager _settings;
    private readonly ThemeManager _theme;
    private readonly int[] _steps;

    [ObservableProperty] private int _position;
    [ObservableProperty] private bool _atLang = true, _atStyle, _atReside, _atAi, _atDone;
    [ObservableProperty] private bool _canBack;
    [ObservableProperty] private string _stepEyebrow = "";
    [ObservableProperty] private string _nextLabel = "";

    [ObservableProperty] private string _aiUrl = "";
    [ObservableProperty] private string _aiKey = "";
    [ObservableProperty] private string _aiModel = "";

    public ObservableCollection<SetupOption> LangOptions { get; } = new();
    public ObservableCollection<SetupOption> WorldOptions { get; } = new();
    public ObservableCollection<SetupOption> PaletteOptions { get; } = new();
    public ObservableCollection<SetupOption> PetOptions { get; } = new();

    /// <summary>置顶和桌宠是 Win32 的事，在安卓上这一步整屏不存在（不是禁用，是不出现）。</summary>
    private readonly bool _hasResideStep;

    public SetupViewModel(SettingsManager settings, ThemeManager theme)
    {
        _settings = settings;
        _theme = theme;
        _hasResideStep = DeviceInfo.Platform == DevicePlatform.WinUI;

        _steps = _hasResideStep
            ? new[] { StepLang, StepStyle, StepReside, StepAi, StepDone }
            : new[] { StepLang, StepStyle, StepAi, StepDone };

        BuildOptions();
        ApplyCulture();
        LocalizationService.Current.PropertyChanged += (_, _) =>
            MainThread.BeginInvokeOnMainThread(ApplyCulture);
        SyncFromSettings();
    }

    private void BuildOptions()
    {
        // 语言用自称：简体中文在英文界面里也叫「简体中文」，翻成 "Simplified Chinese" 反而认不出。
        Add(LangOptions, "zh-CN", "简体中文", PickLangCommand);
        Add(LangOptions, "zh-TW", "繁體中文", PickLangCommand);
        Add(LangOptions, "en-US", "English", PickLangCommand);
        Add(LangOptions, "ja-JP", "日本語", PickLangCommand);

        Add(WorldOptions, "soft", "", PickWorldCommand, sampleRadius: 12);
        Add(WorldOptions, "minimal", "", PickWorldCommand, sampleRadius: 8);
        Add(WorldOptions, "gradient", "", PickWorldCommand, sampleRadius: 16);
        Add(WorldOptions, "liquid", "", PickWorldCommand, sampleRadius: 20);

        // 色板色值取自各 ColorPalette*.xaml：底 / 面 / 暖强调 / 主行动，四色一条。
        Add(PaletteOptions, "classic", "", PickPaletteCommand,
            swatches: new[] { "#F5F0E8", "#FDF8F0", "#F5D8B8", "#DCC7A0" });
        Add(PaletteOptions, "sakura", "", PickPaletteCommand,
            swatches: new[] { "#FBF1F3", "#FEF8F9", "#F6DCC6", "#ECA9BB" });
        Add(PaletteOptions, "bamboo", "", PickPaletteCommand,
            swatches: new[] { "#F0F5EC", "#F8FBF6", "#EDE0C8", "#BAD99F" });
        Add(PaletteOptions, "mist", "", PickPaletteCommand,
            swatches: new[] { "#EFF2F5", "#F7F9FB", "#E8E0D2", "#A9BDCE" });

        Add(PetOptions, "0", "", PickPetCommand);
        Add(PetOptions, "5", "", PickPetCommand);
        Add(PetOptions, "15", "", PickPetCommand);
        Add(PetOptions, "30", "", PickPetCommand);
    }

    private static void Add(ObservableCollection<SetupOption> list, string value, string label, ICommand pick,
        string[] swatches = null, double sampleRadius = 0)
    {
        var opt = new SetupOption
        {
            Index = list.Count,
            Value = value,
            Label = label,
            Pick = pick,
            SampleRadius = sampleRadius,
            Swatches = swatches is null ? [] : swatches.Select(Color.Parse).ToList(),
        };
        list.Add(opt);
    }

    /// <summary>四组选项的显示文字都从语言表取；换语言时整批重写一遍。</summary>
    private void ApplyCulture()
    {
        var i18n = LocalizationService.Current;
        for (var i = 0; i < WorldOptions.Count; i++)
            WorldOptions[i].Label = i18n["Setup_World_" + WordCap(WorldOptions[i].Value)];
        for (var i = 0; i < PaletteOptions.Count; i++)
            PaletteOptions[i].Label = i18n["Setup_Pal_" + WordCap(PaletteOptions[i].Value)];
        PetOptions[0].Label = i18n["Setup_Pet_Off"];
        for (var i = 1; i < PetOptions.Count; i++)
            PetOptions[i].Label = i18n["Setup_Pet_Min"].Replace("{0}", PetOptions[i].Value);
    }

    private static string WordCap(string key) => key.Length == 0 ? key : char.ToUpper(key[0]) + key[1..];

    /// <summary>把界面拧到磁盘上已有的值 —— 中途退出过一次的人，回来看到的应该是自己上次选的。</summary>
    private void SyncFromSettings()
    {
        var s = _settings.Current;
        Select(LangOptions, s.Lang);
        Select(WorldOptions, StyleWorldTokens.NameOf(StyleWorldTokens.FromName(s.StyleWorldName)));
        Select(PaletteOptions, s.ThemeName);
        Select(PetOptions, s.PetIdleMinutes.ToString());
        AiUrl = s.AiUrl;
        AiKey = s.AiKey;
        AiModel = s.AiModel;
        Topmost = s.AlwaysOnTop;
        Position = 0;
        OnPositionChanged(0);
    }

    private static void Select(ObservableCollection<SetupOption> list, string value)
    {
        var hit = list.FirstOrDefault(o => string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase));
        foreach (var o in list) o.Selected = ReferenceEquals(o, hit);
        if (hit is null && list.Count > 0) list[0].Selected = true;
    }

    partial void OnPositionChanged(int value)
    {
        var id = _steps[Math.Clamp(value, 0, _steps.Length - 1)];
        AtLang = id == StepLang;
        AtStyle = id == StepStyle;
        AtReside = id == StepReside;
        AtAi = id == StepAi;
        AtDone = id == StepDone;
        CanBack = value > 0;
        StepEyebrow = (value + 1) + " / " + _steps.Length;
        NextLabel = LocalizationService.Current[id == StepDone ? "Setup_Begin" : "Setup_Next"];
    }

    // ── 选一 ────────────────────────────────────────────────────────────────
    // 每一颗都是「立刻生效 + 顺手落盘」：这一步的效果要在这一步里看到，不该等到下一步。

    [RelayCommand]
    private void PickLang(int index)
    {
        var code = LangOptions[index].Value;
        Select(LangOptions, code);
        LocalizationService.Current.SetCulture(code);
        NextLabel = LocalizationService.Current[AtDone ? "Setup_Begin" : "Setup_Next"];
        _ = Save(s => s with { Lang = code });
    }

    [RelayCommand]
    private void PickWorld(int index)
    {
        var world = StyleWorldTokens.FromName(WorldOptions[index].Value);
        Select(WorldOptions, StyleWorldTokens.NameOf(world));
        _theme.World = world;                       // 材质当场换：整页的圆角、缝、影、字阶一起走
        var (liquid, glass, frost) = StyleWorldTokens.GlassFlags(world);
        _ = Save(s => s with
        {
            StyleWorldName = StyleWorldTokens.NameOf(world),
            LiquidEnabled = liquid, GlassEnabled = glass, FrostEnabled = frost
        });
    }

    [RelayCommand]
    private void PickPalette(int index)
    {
        var key = PaletteOptions[index].Value;
        Select(PaletteOptions, key);
        _theme.ThemeName = key;                     // 色板也当场换
        _ = Save(s => s with { ThemeName = key });
    }

    [RelayCommand]
    private void PickPet(int index)
    {
        var minutes = int.Parse(PetOptions[index].Value);
        Select(PetOptions, minutes.ToString());
        _ = Save(s => s with { PetIdleMinutes = minutes });
    }

    /// <summary>
    /// 置顶那颗开关。值存在设置快照里，这里只是它的一张脸：
    /// 两边各存一份的话，回到标题页就会发现开关和实际状态对不上。
    /// </summary>
    [ObservableProperty] private bool _topmost;

    partial void OnTopmostChanged(bool value)
    {
        if (_settings.Current.AlwaysOnTop == value) return;
        _ = Save(s => s with { AlwaysOnTop = value });
    }

    // ── 走 ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Next()
    {
        if (AtAi) await Save(s => s with
        {
            AiUrl = string.IsNullOrWhiteSpace(AiUrl) ? s.AiUrl : AiUrl.Trim(),
            AiKey = AiKey.Trim(),
            AiModel = string.IsNullOrWhiteSpace(AiModel) ? s.AiModel : AiModel.Trim()
        });
        if (Position < _steps.Length - 1) { Position++; return; }
        await Leave();
    }

    [RelayCommand]
    private void Back()
    {
        if (Position > 0) Position--;
    }

    /// <summary>右下角那枚工具位：整趟都不走了。已经选过的（语言、材质）都留着 —— 跳过不是撤销。</summary>
    [RelayCommand]
    private async Task SkipAll() => await Leave();

    /// <summary>最后一步的主行动：回到标题，再把人交给已有的「新建角色」。</summary>
    [RelayCommand]
    private async Task ToCreate()
    {
        MarkDone();
        await Shell.Current.GoToAsync("../");
        await Shell.Current.GoToAsync("select");
    }

    private async Task Leave()
    {
        MarkDone();
        await Shell.Current.GoToAsync("../");
    }

    private void MarkDone()
    {
        try { Preferences.Set(DonePrefKey, true); }
        catch (Exception ex) { App.WriteLog("Setup: MarkDone EX -> " + ex.Message); }
    }

    private async Task Save(Func<UserSettings, UserSettings> mutate)
    {
        try
        {
            _settings.Apply(mutate(_settings.Current));
            await _settings.Persist();
        }
        catch (Exception ex) { App.WriteLog("Setup: Save EX -> " + ex.Message); }
    }
}
