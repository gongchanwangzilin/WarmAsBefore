using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 主界面小组件模式：顶栏 / 左栏显示什么、按什么顺序、每个按钮留不留。
/// 任何修改都立刻写回 <see cref="UserSettings.Widgets"/> 并落盘 —— 面板不设「保存」，
/// 用户关掉面板的那一刻就是生效的那一帧。
/// </summary>
public sealed partial class MainGameViewModel
{
    /// <summary>全部组件（按 Order 排序），改配置时以它为准。</summary>
    public ObservableCollection<WidgetItemVm> WidgetItems { get; } = new();
    /// <summary>实际画在顶栏/左栏里的可见组件。</summary>
    public ObservableCollection<WidgetItemVm> TopWidgets { get; } = new();
    public ObservableCollection<WidgetItemVm> LeftWidgets { get; } = new();
    /// <summary>手机顶栏只取前三项，其余收进组件面板，避免窄屏挤爆。</summary>
    public ObservableCollection<WidgetItemVm> PhoneTopWidgets { get; } = new();
    /// <summary>配置面板里的两列（包含被隐藏的项）。</summary>
    public ObservableCollection<WidgetItemVm> TopWidgetSettings { get; } = new();
    public ObservableCollection<WidgetItemVm> LeftWidgetSettings { get; } = new();
    /// <summary>顶栏按钮的勾选态。</summary>
    public ObservableCollection<TopBarButtonVm> TopBarButtonItems { get; } = new();

    [ObservableProperty] private bool _widgetModeEnabled;
    [ObservableProperty] private bool _isWidgetPanelVisible;
    private bool _syncingWidgets;

    public bool LegacyPanelsVisible => !WidgetModeEnabled && !HideDesktopPanels;
    public bool WidgetTopBarVisible => WidgetModeEnabled && !HideDesktopPanels;
    public bool WidgetLeftBarVisible => WidgetModeEnabled && !HideDesktopPanels;

    public bool HasLeftWidgets => LeftWidgets.Any(w => w.Shown);

    // 顶栏按钮的显隐（组件的可见性由 WidgetItemVm.Shown 负责）。
    public bool ShowSaveButton => IsTopButtonAllowed("save");
    public bool ShowMenuButton => IsTopButtonAllowed("menu");
    public bool ShowPhoneButton => IsTopButtonAllowed("phone");
    public bool ShowMapButton => IsTopButtonAllowed("map");
    public bool ShowAiButton => IsTopButtonAllowed("ai");
    public bool ShowHideButton => IsTopButtonAllowed("hide");

    private static readonly HashSet<string> WidgetValueSources = new(StringComparer.Ordinal)
    {
        nameof(CurrentTimeStr), nameof(DateLabel), nameof(WeatherDesc), nameof(LocationLabel),
        nameof(Energy), nameof(Affection), nameof(Trust), nameof(Balance), nameof(AffectionLevelLabel),
        nameof(BrainStatusText), nameof(BrainVisible), nameof(CharacterName), nameof(SpriteSource)
    };

    /// <summary>构造函数里调一次：把设置合并成完整清单，并接上「数值变了就刷新小组件」。</summary>
    private void InitWidgets()
    {
        WidgetModeEnabled = _settings.Current.WidgetModeEnabled;
        var merged = WidgetRegistry.Merge(_settings.Current.Widgets);

        _syncingWidgets = true;
        foreach (var pref in merged)
        {
            var def = WidgetRegistry.Find(pref.Id);
            if (def is null) continue;
            var item = new WidgetItemVm(def.Id, def.Title, def.Description)
            {
                Visible = pref.Visible,
                Slot = pref.Slot,
                Order = pref.Order,
                ValueFontSize = def.Id == "clock" ? 22 : 16
            };
            item.PropertyChanged += OnWidgetItemChanged;
            WidgetItems.Add(item);
        }
        _syncingWidgets = false;

        RebuildWidgetCollections();
        BuildTopBarButtons();
        RefreshWidgetValues();
        // 数值来源很散（时钟、天气、好感结算……），统一在这里听，省得每个赋值点都记得补一句。
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is { } name && WidgetValueSources.Contains(name)) RefreshWidgetValues();
        };
    }

    private void OnWidgetItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingWidgets) return;
        if (e.PropertyName == nameof(WidgetItemVm.Visible))
        {
            // 开关只影响「画出来的那一份」，配置面板里的两列不重排；
            // 否则 Switch 正在处理点击时把自己的行从列表里换掉，手感会突然断一下。
            RebuildVisibleWidgets();
            PersistWidgets();
            return;
        }
        if (e.PropertyName is not (nameof(WidgetItemVm.Slot) or nameof(WidgetItemVm.Order))) return;
        RebuildWidgetCollections();
        PersistWidgets();
    }

    private void RebuildWidgetCollections()
    {
        var ordered = WidgetItems
            .OrderBy(w => w.Order)
            .ThenBy(w => WidgetRegistry.DefaultOrderOf(w.Id))
            .ToList();

        _syncingWidgets = true;
        TopWidgets.Clear();
        LeftWidgets.Clear();
        PhoneTopWidgets.Clear();
        TopWidgetSettings.Clear();
        LeftWidgetSettings.Clear();

        foreach (var w in ordered)
        {
            if (w.Slot == "top")
            {
                TopWidgetSettings.Add(w);
                if (w.Visible) TopWidgets.Add(w);
            }
            else
            {
                LeftWidgetSettings.Add(w);
                if (w.Visible) LeftWidgets.Add(w);
            }
        }
        foreach (var w in TopWidgets.Take(3)) PhoneTopWidgets.Add(w);
        _syncingWidgets = false;

        OnPropertyChanged(nameof(HasLeftWidgets));
        RefreshWidgetValues();
    }

    private void RebuildVisibleWidgets()
    {
        var ordered = WidgetItems
            .OrderBy(w => w.Order)
            .ThenBy(w => WidgetRegistry.DefaultOrderOf(w.Id))
            .ToList();

        _syncingWidgets = true;
        TopWidgets.Clear();
        LeftWidgets.Clear();
        PhoneTopWidgets.Clear();
        foreach (var w in ordered)
        {
            if (!w.Visible) continue;
            (w.Slot == "top" ? TopWidgets : LeftWidgets).Add(w);
        }
        foreach (var w in TopWidgets.Take(3)) PhoneTopWidgets.Add(w);
        _syncingWidgets = false;

        OnPropertyChanged(nameof(HasLeftWidgets));
        RefreshWidgetValues();
    }

    private void RefreshWidgetValues()
    {
        foreach (var w in WidgetItems)
        {
            w.Shown = w.Visible && (w.Id != "brain"
                || (BrainVisible && !string.IsNullOrWhiteSpace(BrainStatusText)));
            switch (w.Id)
            {
                case "clock":
                    w.Value = string.IsNullOrWhiteSpace(CurrentTimeStr) ? "--:--" : CurrentTimeStr;
                    w.SubValue = "";
                    break;
                case "date":
                    w.Value = DateLabel;
                    w.SubValue = "";
                    break;
                case "weather":
                    w.Value = WeatherDesc;
                    w.SubValue = "";
                    break;
                case "location":
                    w.Value = LocationLabel;
                    w.SubValue = "";
                    break;
                case "affection":
                    w.Value = $"{Affection}/100";
                    w.SubValue = AffectionLevelLabel;
                    break;
                case "trust":
                    w.Value = $"{Trust}/100";
                    w.SubValue = "";
                    break;
                case "energy":
                    w.Value = $"{Energy}%";
                    w.SubValue = "";
                    break;
                case "balance":
                    w.Value = $"¤ {Balance}";
                    w.SubValue = "";
                    break;
                case "brain":
                    w.Value = BrainStatusText;
                    w.SubValue = "";
                    break;
                case "mood":
                    w.Value = CurrentMoodLabel();
                    w.SubValue = "";
                    break;
            }
        }
        OnPropertyChanged(nameof(HasLeftWidgets));
    }

    private string CurrentMoodLabel()
    {
        var raw = _char?.State.CurrentEmotion ?? "";
        return raw switch
        {
            "normal" or "" => "平静",
            "happy" or "joy" => "开心",
            "smile" => "微笑",
            "shy" => "害羞",
            "sad" => "难过",
            "angry" => "生气",
            "surprised" => "惊讶",
            "cry" => "想哭",
            "tired" => "有点累",
            _ => raw
        };
    }

    // ===== 配置面板命令 =====

    [RelayCommand]
    private void ToggleWidgetPanel() => IsWidgetPanelVisible = !IsWidgetPanelVisible;

    [RelayCommand]
    private void CloseWidgetPanel() => IsWidgetPanelVisible = false;

    [RelayCommand]
    private void ToggleWidget(WidgetItemVm? item)
    {
        if (item is null) return;
        item.Visible = !item.Visible;   // 由 OnWidgetItemChanged 统一重建 + 落盘
    }

    [RelayCommand]
    private void MoveWidgetUp(WidgetItemVm? item) => MoveWidget(item, -1);

    [RelayCommand]
    private void MoveWidgetDown(WidgetItemVm? item) => MoveWidget(item, 1);

    private void MoveWidget(WidgetItemVm? item, int direction)
    {
        if (item is null) return;
        var list = item.Slot == "top" ? TopWidgetSettings.ToList() : LeftWidgetSettings.ToList();
        var index = list.IndexOf(item);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= list.Count) return;
        (list[index], list[target]) = (list[target], list[index]);

        _syncingWidgets = true;
        for (var i = 0; i < list.Count; i++) list[i].Order = (i + 1) * 10;
        _syncingWidgets = false;
        RebuildWidgetCollections();
        PersistWidgets();
    }

    [RelayCommand]
    private void MoveWidgetSlot(WidgetItemVm? item)
    {
        if (item is null) return;
        var targetSlot = item.Slot == "top" ? "left" : "top";
        var peers = targetSlot == "top" ? TopWidgetSettings : LeftWidgetSettings;
        var maxOrder = peers.Count > 0 ? peers.Max(w => w.Order) : 0;

        _syncingWidgets = true;
        item.Slot = targetSlot;
        item.Order = maxOrder + 10;
        _syncingWidgets = false;
        RebuildWidgetCollections();
        PersistWidgets();
    }

    private void PersistWidgets()
    {
        var prefs = WidgetItems
            .OrderBy(w => w.Order)
            .ThenBy(w => WidgetRegistry.DefaultOrderOf(w.Id))
            .Select(w => new WidgetPref
            {
                Id = w.Id,
                Visible = w.Visible,
                Slot = w.Slot,
                Order = w.Order
            })
            .ToList();
        _settings.Apply(_settings.Current with { Widgets = prefs });
        _ = _settings.Persist();
    }

    // ===== 顶栏按钮 =====

    private void BuildTopBarButtons()
    {
        var allowed = new HashSet<string>(WidgetRegistry.AllowedTopButtons(_settings.Current.TopBarButtons),
            StringComparer.OrdinalIgnoreCase);
        TopBarButtonItems.Clear();
        foreach (var def in WidgetRegistry.TopButtons)
            TopBarButtonItems.Add(new TopBarButtonVm(def.Id, def.Label) { Visible = allowed.Contains(def.Id) });
        NotifyTopButtonVisibility();
    }

    private bool IsTopButtonAllowed(string id)
        => TopBarButtonItems.FirstOrDefault(b => b.Id == id)?.Visible ?? true;

    [RelayCommand]
    private void ToggleTopBarButton(TopBarButtonVm? button)
    {
        if (button is null) return;
        button.Visible = !button.Visible;
        var csv = WidgetRegistry.BuildTopButtons(TopBarButtonItems.Where(b => b.Visible).Select(b => b.Id));
        _settings.Apply(_settings.Current with { TopBarButtons = csv });
        _ = _settings.Persist();
        NotifyTopButtonVisibility();
    }

    private void NotifyTopButtonVisibility()
    {
        OnPropertyChanged(nameof(ShowSaveButton));
        OnPropertyChanged(nameof(ShowMenuButton));
        OnPropertyChanged(nameof(ShowPhoneButton));
        OnPropertyChanged(nameof(ShowMapButton));
        OnPropertyChanged(nameof(ShowAiButton));
        OnPropertyChanged(nameof(ShowHideButton));
    }

    partial void OnWidgetModeEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(LegacyPanelsVisible));
        OnPropertyChanged(nameof(WidgetTopBarVisible));
        OnPropertyChanged(nameof(WidgetLeftBarVisible));
        if (!value) IsWidgetPanelVisible = false;
    }

    /// <summary>设置页可能改了小组件总开关；回到主界面时补一次，别让用户等到下次启动。</summary>
    public void SyncWidgetModeFromSettings()
    {
        var enabled = _settings.Current.WidgetModeEnabled;
        if (WidgetModeEnabled != enabled) WidgetModeEnabled = enabled;
    }

    /// <summary>沉浸态/恢复时，顶栏与左栏的两种排版要一起换。</summary>
    private void NotifyPanelVisibility()
    {
        OnPropertyChanged(nameof(LegacyPanelsVisible));
        OnPropertyChanged(nameof(WidgetTopBarVisible));
        OnPropertyChanged(nameof(WidgetLeftBarVisible));
    }
}

/// <summary>配置面板/栏位里的一行小组件。Value 是主值，SubValue 是底下那行小字。</summary>
public sealed partial class WidgetItemVm : ObservableObject
{
    public WidgetItemVm(string id, string title, string description)
    {
        Id = id;
        _title = title;
        _desc = description;
    }

    public string Id { get; }

    [ObservableProperty] private string _title;
    [ObservableProperty] private string _desc;
    [ObservableProperty] private bool _visible = true;
    [ObservableProperty] private string _slot = "left";
    [ObservableProperty] private int _order;
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private string _subValue = "";
    [ObservableProperty] private bool _shown = true;
    [ObservableProperty] private double _valueFontSize = 16;

    public bool IsTop => Slot == "top";
    public bool IsLeft => Slot == "left";
    public string SlotLabel => IsTop ? "顶栏" : "左栏";
    public string SlotSwitchLabel => IsTop ? "移到左栏" : "移到顶栏";

    partial void OnSlotChanged(string value)
    {
        OnPropertyChanged(nameof(IsTop));
        OnPropertyChanged(nameof(IsLeft));
        OnPropertyChanged(nameof(SlotLabel));
        OnPropertyChanged(nameof(SlotSwitchLabel));
    }
}

/// <summary>顶栏按钮在配置面板里的勾选态。</summary>
public sealed partial class TopBarButtonVm : ObservableObject
{
    public TopBarButtonVm(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }
    public string Label { get; }

    [ObservableProperty] private bool _visible;
}
