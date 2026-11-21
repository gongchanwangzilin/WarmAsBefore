using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Models;
using WarmAsBefore.Services;
using System.Collections.ObjectModel;

namespace WarmAsBefore.ViewModels;

public sealed partial class SettingsViewModel
{
    public ObservableCollection<SettingsSectionRow> SectionRows { get; } = new();

    /// <summary>按当前设置值刷新每个分类的「当前值」摘要（首页一眼能看到关键状态）。</summary>
    private void RefreshSectionSummaries()
    {
        foreach (var row in SectionRows)
        {
            row.Summary = row.Key switch
            {
                "appearance" => $"{(_liquidOn ? "液态玻璃" : _glassOn ? "毛玻璃" : _frostOn ? "磨砂" : "纯色")} · {ThemeDisplayName()}",
                "sound" => $"BGM {BgmLevel:P0} · 音效 {SfxLevel:P0}",
                "ai" => $"{AiProviders.Count(v => v.Enabled)} 家厂商 · {(SmartSavingEnabled ? "省钱开" : "省钱关")} · 超时 {AiTimeoutSeconds}s",
                "memory" => $"回想 {WindowText(MemoryWindowDays)} · 上下文 {_contextTokenLimitDisplay}",
                "voice" => $"{(TtsEnabled ? "朗读开" : "朗读关")} · {(SttEnabled ? "识别开" : "识别关")} · {TtsEngine}",
                "interface" => $"{ScreenOrientationDisplay} · {ChatStyleDisplay} · 难度 {GameDifficultyDisplayOf(_gameDifficulty)}",
                "platform" => $"桌宠闲置 {(PetIdleMinutes == 0 ? "关" : PetIdleMinutes + " 分")} · {(PreferDiscreteGpu ? "独显" : "自动显卡")}",
                "integrations" => $"{(QqBotEnabled ? "QQ 开" : "QQ 关")} · {(WechatEnabled ? "微信开" : "微信关")} · MCP {(McpEnabled ? "开" : "关")}",
                "life" => $"{(NotificationsEnabled ? "通知开" : "通知关")} · {(string.IsNullOrWhiteSpace(WeatherCity) ? "未设城市" : WeatherCity)}{(WeatherSource == Modules.RealWorld.WeatherSources.Custom ? "（自定义源）" : "")}",
                "data" => $"{MaterialsSummary()} · 能力包 {(string.IsNullOrWhiteSpace(SkillsSummary) ? "未装" : "已装")}",
                _ => row.Desc
            };
        }
    }

    private string ThemeDisplayName() => (_themeName ?? "classic") switch
    {
        "sakura" => "樱花粉",
        "bamboo" => "翠竹绿",
        "mist" => "晨雾蓝灰",
        "endfield" => "终末地",
        _ => "经典"
    };

    /// <summary>把「天」讲成人话：不足一天说小时，一年上下说月，免得用户看到 0.25 天要心算。</summary>
    private static string WindowText(double days) => days switch
    {
        <= 0 => "不限",
        < 1 => $"{days * 24:0.#} 小时",
        < 30 => $"{days:0.#} 天",
        _ => $"{days / 30:0.#} 个月"
    };

    private string MaterialsSummary()
    {
        try { return "素材库"; }
        catch { return "素材库"; }
    }

    /// <summary>建分类条目（首次进设置页时一次）。</summary>
    private void BuildSectionRows()
    {
        if (SectionRows.Count > 0) { RefreshSectionSummaries(); return; }
        foreach (var sec in SettingsCatalog.All)
        {
            SectionRows.Add(new SettingsSectionRow
            {
                Key = sec.Key, Icon = sec.Icon, Title = sec.Title, Desc = sec.Desc, Keywords = sec.Keywords
            });
        }
        RefreshSectionSummaries();
    }

    /// <summary>按搜索词过滤分类条目（同时把卡片级过滤也应用上，子页里才不会显示无关卡片）。</summary>
    private void ApplySearch(string? query)
    {
        Filter.Apply(query);
        var q = (query ?? "").Trim().ToLowerInvariant();
        foreach (var row in SectionRows)
        {
            row.Visible = q.Length == 0
                || row.Keywords.Any(k => k.Contains(q, StringComparison.OrdinalIgnoreCase) || q.Contains(k, StringComparison.OrdinalIgnoreCase))
                || row.Title.Contains(q, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>首页出现时准备：建分类条目并刷新摘要。</summary>
    public void PrepareHome()
    {
        BuildSectionRows();
        RefreshSectionSummaries();
        ApplySearch(SettingsSearchText);
    }

    /// <summary>进入某个分类子页。</summary>
    [RelayCommand]
    private async Task OpenSection(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        await Shell.Current.GoToAsync($"settingssection?key={Uri.EscapeDataString(key)}");
    }


    // ============ 设置搜索 ============

    /// <summary>卡片级过滤：搜索时只留下命中的卡片（设置页太长，找不到项是常态）。</summary>
    public SettingsCardFilter Filter { get; } = new();

    [ObservableProperty] private string _settingsSearchText = "";

    private System.Threading.Timer? _searchTimer;

    /// <summary>
    /// 搜索防抖 250ms：每敲一个字就切一遍全部卡片的可见性会让长页面反复重排，
    /// 打字时就会一卡一卡；等手停一下再过滤，观感顺得多。
    /// </summary>
    partial void OnSettingsSearchTextChanged(string value)
    {
        lock (_persistLock)
        {
            _searchTimer ??= new System.Threading.Timer(
                _ => Helpers.Ui.Post(() => ApplySearch(SettingsSearchText)), null,
                Timeout.Infinite, Timeout.Infinite);
            _searchTimer.Change(250, Timeout.Infinite);
        }
    }

    /// <summary>清空搜索，恢复全部分类。</summary>
    [RelayCommand]
    private void ClearSettingsSearch()
    {
        SettingsSearchText = "";
        ApplySearch("");   // 清空立即生效，不用等防抖
    }

}
