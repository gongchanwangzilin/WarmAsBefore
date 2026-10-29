using WarmAsBefore.Models;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 设置页的新增选项（通知样式、界面组件、角色内容开关）。
/// 独立成部分类，只做「读写 UserSettings 上的新字段」这一件事：
/// 落盘仍走 PersistNow，所以这里不自己序列化，只把值挂回 s。
/// </summary>
public sealed partial class SettingsViewModel
{
    // ---- 通知 ----
    public static readonly string[] NotificationPositionKeys =
        { "top-right", "top-left", "bottom-right", "bottom-left", "top", "bottom", "center" };

    public List<string> NotificationPositionChoices { get; } = new()
        { "右上角（默认）", "左上角", "右下角", "左下角", "顶部居中", "底部居中", "屏幕中间" };

    private string _notificationPosition = "top-right";

    /// <summary>通知落点，存的是键（Toast 按它排版），界面显示中文。</summary>
    public string NotificationPosition
    {
        get => _notificationPosition;
        set
        {
            var key = NotificationPositionKeys.Contains(value) ? value : "top-right";
            if (_notificationPosition == key) return;
            _notificationPosition = key;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NotificationPositionDisplay));
            PersistSettings();
        }
    }

    public string NotificationPositionDisplay
    {
        get
        {
            var i = Array.IndexOf(NotificationPositionKeys, _notificationPosition);
            return i >= 0 && i < NotificationPositionChoices.Count ? NotificationPositionChoices[i] : NotificationPositionChoices[0];
        }
        set
        {
            var i = NotificationPositionChoices.IndexOf(value ?? "");
            NotificationPosition = i >= 0 ? NotificationPositionKeys[i] : "top-right";
        }
    }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private double _notificationSeconds = 4;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _toastEnabled = true;

    // ---- 界面与玩法 ----
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _widgetModeEnabled = true;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _outfitSystemNew = true;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _graphAutoSkipAnim = true;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private double _graphSkipAnimNodes = 240;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _saveCreationOobe = true;

    // ---- 角色与内容 ----
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _libidoSystemEnabled;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private double _libidoCycleInfluence = 0.5;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _maskUnintroducedNames = true;

    // ---- 存档运行时：存档一打开，所有内部角色继续活着 ----
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _saveRuntimeEnabled = true;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _saveRuntimeAiEnabled = true;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _saveRuntimePlayGames = true;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _saveRuntimeProactive;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _saveRuntimeQuietHours = true;

    private int _saveRuntimeAiMinutes = 20;
    private int _saveRuntimeHourlyBudget = 6;

    /// <summary>生活回合间隔（分钟，存的是数，界面显示中文）。</summary>
    public List<string> SaveRuntimeAiChoices { get; } = new()
        { "每 10 分钟", "每 15 分钟", "每 20 分钟", "每 30 分钟", "每 45 分钟", "每 60 分钟" };
    private static readonly int[] SaveRuntimeAiTable = { 10, 15, 20, 30, 45, 60 };

    public string SaveRuntimeAiDisplay
    {
        get
        {
            var i = Array.IndexOf(SaveRuntimeAiTable, _saveRuntimeAiMinutes);
            return SaveRuntimeAiChoices[i >= 0 ? i : 2];
        }
        set
        {
            var i = SaveRuntimeAiChoices.IndexOf(value ?? "");
            _saveRuntimeAiMinutes = i >= 0 ? SaveRuntimeAiTable[i] : 20;
            OnPropertyChanged();
            PersistSettings();
        }
    }

    /// <summary>每小时最多几个 AI 生活回合（所有角色共用）。</summary>
    public List<string> SaveRuntimeBudgetChoices { get; } = new()
        { "每小时 3 次", "每小时 6 次", "每小时 12 次", "每小时 20 次" };
    private static readonly int[] SaveRuntimeBudgetTable = { 3, 6, 12, 20 };

    public string SaveRuntimeBudgetDisplay
    {
        get
        {
            var i = Array.IndexOf(SaveRuntimeBudgetTable, _saveRuntimeHourlyBudget);
            return SaveRuntimeBudgetChoices[i >= 0 ? i : 1];
        }
        set
        {
            var i = SaveRuntimeBudgetChoices.IndexOf(value ?? "");
            _saveRuntimeHourlyBudget = i >= 0 ? SaveRuntimeBudgetTable[i] : 6;
            OnPropertyChanged();
            PersistSettings();
        }
    }

    /// <summary>在线角色此刻在做什么（设置页展示用；后台线程写的，点刷新才重读）。</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _runtimePresenceText = "";

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void RefreshRuntimePresence()
    {
        try
        {
            var rows = _runtime.Snapshot();
            var head = _runtime.Running ? "存档运行时正在跑" : "存档没开着，后台生活要等进存档才开始";
            if (rows.Count == 0)
            {
                RuntimePresenceText = head + "｜还没有角色动起来，进存档待一会儿再来看看。";
                return;
            }
            var lines = rows.Take(12).Select(r => $"· {r.Name}　{r.Activity}（{r.Place}）　{r.At:HH:mm}");
            RuntimePresenceText = head + "\n" + string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            RuntimePresenceText = "";
            App.WriteLog("Settings.RefreshRuntimePresence -> " + ex.Message);
        }
    }

    partial void OnNotificationSecondsChanged(double value) => PersistSettings();
    partial void OnToastEnabledChanged(bool value) => PersistSettings();
    partial void OnWidgetModeEnabledChanged(bool value) => PersistSettings();
    partial void OnOutfitSystemNewChanged(bool value) => PersistSettings();
    partial void OnGraphAutoSkipAnimChanged(bool value) => PersistSettings();
    partial void OnGraphSkipAnimNodesChanged(double value) => PersistSettings();
    partial void OnSaveCreationOobeChanged(bool value) => PersistSettings();
    partial void OnLibidoSystemEnabledChanged(bool value) => PersistSettings();
    partial void OnLibidoCycleInfluenceChanged(double value) => PersistSettings();
    partial void OnMaskUnintroducedNamesChanged(bool value) => PersistSettings();
    partial void OnSaveRuntimeEnabledChanged(bool value) => PersistSettings();
    partial void OnSaveRuntimeAiEnabledChanged(bool value) => PersistSettings();
    partial void OnSaveRuntimePlayGamesChanged(bool value) => PersistSettings();
    partial void OnSaveRuntimeProactiveChanged(bool value) => PersistSettings();
    partial void OnSaveRuntimeQuietHoursChanged(bool value) => PersistSettings();

    private void LoadExtraOptions(UserSettings s)
    {
        _notificationPosition = NotificationPositionKeys.Contains(s.NotificationPosition ?? "")
            ? s.NotificationPosition!
            : "top-right";
        _notificationSeconds = s.NotificationSeconds <= 0 ? 4 : Math.Clamp(s.NotificationSeconds, 1.5, 12);
        _toastEnabled = s.ToastEnabled;
        _widgetModeEnabled = s.WidgetModeEnabled;
        _outfitSystemNew = s.OutfitSystemNew;
        _graphAutoSkipAnim = s.GraphAutoSkipAnim;
        _graphSkipAnimNodes = s.GraphSkipAnimNodes <= 0 ? 240 : s.GraphSkipAnimNodes;
        _saveCreationOobe = s.SaveCreationOobe;
        _libidoSystemEnabled = s.LibidoSystemEnabled;
        _libidoCycleInfluence = Math.Clamp(s.LibidoCycleInfluence, 0, 1);
        _maskUnintroducedNames = s.MaskUnintroducedNames;
        _saveRuntimeEnabled = s.SaveRuntimeEnabled;
        _saveRuntimeAiEnabled = s.SaveRuntimeAiEnabled;
        _saveRuntimeAiMinutes = Math.Clamp(s.SaveRuntimeAiMinutes > 0 ? s.SaveRuntimeAiMinutes : 20, 10, 120);
        _saveRuntimePlayGames = s.SaveRuntimePlayGames;
        _saveRuntimeProactive = s.SaveRuntimeProactive;
        _saveRuntimeHourlyBudget = Math.Clamp(s.SaveRuntimeHourlyBudget > 0 ? s.SaveRuntimeHourlyBudget : 6, 1, 30);
        _saveRuntimeQuietHours = s.SaveRuntimeQuietHours;
    }

    private UserSettings ApplyExtraOptions(UserSettings s) => s with
    {
        NotificationPosition = _notificationPosition,
        NotificationSeconds = Math.Clamp(_notificationSeconds, 1.5, 12),
        ToastEnabled = _toastEnabled,
        WidgetModeEnabled = _widgetModeEnabled,
        OutfitSystemNew = _outfitSystemNew,
        GraphAutoSkipAnim = _graphAutoSkipAnim,
        GraphSkipAnimNodes = (int)Math.Clamp(_graphSkipAnimNodes, 0, 5000),
        SaveCreationOobe = _saveCreationOobe,
        LibidoSystemEnabled = _libidoSystemEnabled,
        LibidoCycleInfluence = Math.Clamp(_libidoCycleInfluence, 0, 1),
        MaskUnintroducedNames = _maskUnintroducedNames,
        SaveRuntimeEnabled = _saveRuntimeEnabled,
        SaveRuntimeAiEnabled = _saveRuntimeAiEnabled,
        SaveRuntimeAiMinutes = Math.Clamp(_saveRuntimeAiMinutes, 10, 120),
        SaveRuntimePlayGames = _saveRuntimePlayGames,
        SaveRuntimeProactive = _saveRuntimeProactive,
        SaveRuntimeHourlyBudget = Math.Clamp(_saveRuntimeHourlyBudget, 1, 30),
        SaveRuntimeQuietHours = _saveRuntimeQuietHours,
    };
}
