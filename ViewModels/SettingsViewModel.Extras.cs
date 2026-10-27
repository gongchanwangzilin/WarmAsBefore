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
    };
}
