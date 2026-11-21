using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Models;
using WarmAsBefore.Services;
using WarmAsBefore.Controls;
using System.Collections.ObjectModel;

namespace WarmAsBefore.ViewModels;

public sealed partial class SettingsViewModel
{
    // ============ 天气与生活：定位 / 选择城市 / 数据源 ============

    [ObservableProperty] private string _weatherCity = "";
    /// <summary>城市坐标：由「定位」或「选择城市」写入。为空表示只填过城市名，取天气时再地理编码。</summary>
    [ObservableProperty] private double? _weatherLatitude;
    [ObservableProperty] private double? _weatherLongitude;
    /// <summary>数据源代码（open-meteo / wttr / custom）：落盘用它，显示名只给 Picker 看。</summary>
    [ObservableProperty] private string _weatherSource = "open-meteo";
    [ObservableProperty] private Modules.RealWorld.WeatherSourceChoice _weatherSourceChoice = null!;
    [ObservableProperty] private string _weatherApiUrl = "";
    [ObservableProperty] private string _weatherApiKey = "";
    [ObservableProperty] private bool _isWeatherSourceCustom;

    /// <summary>城市选择面板（搜索 + 热门城市）是否展开。</summary>
    [ObservableProperty] private bool _cityChooserOpen;
    [ObservableProperty] private string _citySearchText = "";
    [ObservableProperty] private bool _isSearchingCity;
    [ObservableProperty] private bool _isLocatingCity;
    [ObservableProperty] private bool _hasCityResults;
    /// <summary>IP 估算出来的候选位置：不自动采用，等用户点「采用这个估算」。</summary>
    [ObservableProperty] private bool _hasPendingLocationEstimate;
    [ObservableProperty] private string _pendingLocationText = "";
    private Modules.RealWorld.LocationFix? _pendingEstimateFix;
    /// <summary>定位 / 选城市 / 测试天气的回显文字（不入库）。</summary>
    [ObservableProperty] private string _weatherStatusText = "";
    [ObservableProperty] private bool _hasWeatherStatus;

    public List<Modules.RealWorld.WeatherSourceChoice> WeatherSourceChoices { get; } = new()
    {
        new("open-meteo", "Open-Meteo（默认，免密钥）"),
        new("wttr", "wttr.in（免密钥）"),
        new("custom", "自定义（Open-Meteo 兼容）"),
    };

    /// <summary>内置热门城市：不联网也能点选。</summary>
    public IReadOnlyList<Modules.RealWorld.CityHit> PopularCities => Modules.RealWorld.CityCatalog.Popular;

    /// <summary>搜索联想结果（内置匹配 + 在线地理编码合并）。</summary>
    public ObservableCollection<Modules.RealWorld.CityHit> CityResults { get; } = new();

    public string WeatherCityDisplay => string.IsNullOrWhiteSpace(WeatherCity)
        ? LocalizationService.Current["Settings_CityUnset"] : WeatherCity;

    /// <summary>坐标回显：让「定到的是哪儿」看得见，也方便确认自定义源请求的坐标。</summary>
    public string WeatherCoordDisplay => WeatherLatitude is double la && WeatherLongitude is double lo
        ? $"{la:0.00}, {lo:0.00}" : "";

    private Modules.RealWorld.WeatherSourceChoice ChoiceOf(string code) =>
        WeatherSourceChoices.FirstOrDefault(c => c.Code == code) ?? WeatherSourceChoices[0];

    partial void OnWeatherSourceChoiceChanged(Modules.RealWorld.WeatherSourceChoice value)
    {
        if (value is null) return;
        // 写后备字段：赋属性会再触发一次本钩子，Picker 上就会看到两次「源已切换」
        _weatherSource = value.Code;
        IsWeatherSourceCustom = value.Code == Modules.RealWorld.WeatherSources.Custom;
        PersistSettings();
    }

    partial void OnWeatherApiUrlChanged(string value) => PersistSettings();
    partial void OnWeatherApiKeyChanged(string value) => PersistSettings();
    partial void OnWeatherStatusTextChanged(string value) => HasWeatherStatus = !string.IsNullOrWhiteSpace(value);

    /// <summary>展开/收起城市选择面板。</summary>
    [RelayCommand]
    private void ToggleCityChooser() => CityChooserOpen = !CityChooserOpen;

    /// <summary>点选一个城市：名字与坐标一起记下，之后取天气不再依赖地理编码。</summary>
    [RelayCommand]
    private void ChooseCity(Modules.RealWorld.CityHit? city)
    {
        if (city is null) return;
        WeatherCity = city.Name;
        WeatherLatitude = city.Latitude;
        WeatherLongitude = city.Longitude;
        CityChooserOpen = false;
        CitySearchText = "";
        CityResults.Clear();
        HasCityResults = false;
        WeatherStatusText = $"已选择 {city.Name}（{city.CoordText}）";
        PersistSettings();   // 城市名没变、只更新坐标时，属性钩子不会触发，这里补一次
    }

    /// <summary>
    /// 定位：先要设备定位（Android GPS / Windows 定位服务），失败再按公网 IP 估一个城市。
    /// IP 结果只作为「候选」展示，等用户确认才写入设置——IP 常常把漯河估成郑州，
    /// 静默采用等于每次打开都把人搬到隔壁市。设备定位成功则直接采用。
    /// </summary>
    [RelayCommand]
    private async Task LocateWeatherCity()
    {
        if (IsLocatingCity) return;
        IsLocatingCity = true;
        HasPendingLocationEstimate = false;
        _pendingEstimateFix = null;
        WeatherStatusText = "正在请求设备定位（手机 GPS / 电脑定位服务）…";
        try
        {
            var fix = await _cities.LocateDeviceAsync();
            if (fix is not null)
            {
                ApplyLocationFix(fix);
                WeatherStatusText = $"已按{fix.Origin}定位到 {fix.City.Name}（{fix.City.CoordText}）"
                    + AccuracyNote(fix);
                return;
            }

            var reason = string.IsNullOrWhiteSpace(_cities.LastDeviceError)
                ? "设备定位不可用" : _cities.LastDeviceError;
            WeatherStatusText = reason + "；正在用 IP 估算一个候选…";
            var estimate = await _cities.LocateByIpAsync();
            if (estimate is null)
            {
                WeatherStatusText = reason + "；IP 也没估出位置。请手动选择城市。";
                return;
            }
            // 不写设置：先把候选摆出来，让用户自己决定要不要采信。
            _pendingEstimateFix = estimate;
            PendingLocationText = $"IP 估算：{estimate.City.Name}（{estimate.City.CoordText}）。{reason}。"
                + "IP 只到城市级、经常偏几十公里——确认是这里再点「采用」。";
            HasPendingLocationEstimate = true;
            WeatherStatusText = "设备定位不可用，已按 IP 估算给出候选，需要你确认。";
            App.WriteLog($"SettingsViewModel.LocateWeatherCity -> 设备失败（{reason}），IP 候选 {estimate.City.Name}");
        }
        catch (Exception ex)
        {
            WeatherStatusText = "定位失败：" + ex.Message;
            App.WriteLog("SettingsViewModel.LocateWeatherCity -> " + ex);
        }
        finally
        {
            IsLocatingCity = false;
        }
    }

    /// <summary>采用 IP 候选：用户明确点了按钮才落到设置里。</summary>
    [RelayCommand]
    private void AdoptLocationEstimate()
    {
        if (_pendingEstimateFix is null) return;
        ApplyLocationFix(_pendingEstimateFix);
        WeatherStatusText = $"已采用 IP 估算位置 {_pendingEstimateFix.City.Name}"
            + "（如果不对，请重新定位或手动选城市）。";
        _pendingEstimateFix = null;
        HasPendingLocationEstimate = false;
        PendingLocationText = "";
        PersistSettings();
    }

    /// <summary>不要这个 IP 候选：清掉提示，位置保持原样。</summary>
    [RelayCommand]
    private void DismissLocationEstimate()
    {
        _pendingEstimateFix = null;
        HasPendingLocationEstimate = false;
        PendingLocationText = "";
        WeatherStatusText = "没有采用 IP 估算。可以手动选择城市，或到系统设置里允许定位后重试。";
    }

    private void ApplyLocationFix(Modules.RealWorld.LocationFix fix)
    {
        WeatherCity = fix.City.Name;
        WeatherLatitude = fix.City.Latitude;
        WeatherLongitude = fix.City.Longitude;
        CityChooserOpen = false;
        CitySearchText = "";
        CityResults.Clear();
        HasCityResults = false;
        PersistSettings();
    }

    private static string AccuracyNote(Modules.RealWorld.LocationFix fix) =>
        fix.AccuracyMeters is double a && a > 0 ? $" · 精度约 {a:0} 米" : "";

    /// <summary>按当前界面上的源与城市取一次真实天气：自定义源填完能立刻验证通不通。</summary>
    [RelayCommand]
    private async Task TestWeather()
    {
        WeatherStatusText = "正在取天气…";
        try
        {
            _weather.Configure(WeatherSource, WeatherApiUrl, WeatherApiKey);
            _weather.SetLocation(WeatherCity, WeatherLatitude, WeatherLongitude);
            // 这里测的是「用户设置的城市」，不受当前地图的天气覆盖影响。
            var r = await _weather.FetchForUserAsync();
            WeatherStatusText = r is null ? "这个源没返回天气数据" : $"{r.City}：{r.Description} {r.Celsius:0.#}°C";
        }
        catch (Exception ex)
        {
            WeatherStatusText = "取天气失败：" + ex.Message;
            App.WriteLog("SettingsViewModel.TestWeather -> " + ex);
        }
    }

    private System.Threading.Timer? _citySearchTimer;
    private readonly object _citySearchLock = new();
    private int _citySearchSeq;

    partial void OnCitySearchTextChanged(string value)
    {
        lock (_citySearchLock)
        {
            _citySearchTimer ??= new System.Threading.Timer(
                _ => Helpers.Ui.Post(RunCitySearch), null,
                Timeout.Infinite, Timeout.Infinite);
            _citySearchTimer.Change(250, Timeout.Infinite);
        }
    }

    private async void RunCitySearch()
    {
        try
        {
            var keyword = CitySearchText.Trim();
            var seq = ++_citySearchSeq;
            if (keyword.Length == 0)
            {
                CityResults.Clear();
                HasCityResults = false;
                return;
            }
            IsSearchingCity = true;
            try
            {
                var hits = await _cities.SearchAsync(keyword, WeatherSource, WeatherApiUrl);
                // 只让最后一次输入的结果落地：连打字时旧请求回得晚会把新结果盖掉
                if (seq != _citySearchSeq) return;
                CityResults.Clear();
                foreach (var h in hits) CityResults.Add(h);
                HasCityResults = CityResults.Count > 0;
            }
            catch (Exception ex)
            {
                App.WriteLog("SettingsViewModel.CitySearch -> " + ex);
            }
            finally
            {
                IsSearchingCity = false;
            }
        }
        catch (Exception ex) { App.WriteLog("Settings.RunCitySearch -> " + ex); }
    }

    // ============ 周期设置：按性别分开 ============
    //
    // 编辑的是「当前选中性别」的那一条规则，全部规则先在内存里攒着（_cycleRules），
    // 落盘时整体快照一次。不直接改 _settings.Current 的那本字典：切一次性别会连着触发
    // 三个字段变更，每个都去读盘再写盘，读到的还是上一轮的旧值，最后一次编辑会被吃掉。

    private readonly Dictionary<string, CycleRule> _cycleRules = new(StringComparer.Ordinal);
    private bool _loadingCycle;

    [ObservableProperty] private bool _cycleTracked;
    [ObservableProperty] private int _cycleLength = 28;
    [ObservableProperty] private int _periodLength = 5;
    [ObservableProperty] private string _cycleGender = "女";
    [ObservableProperty] private bool _showCycleAdvanced;

    public ObservableCollection<string> CycleGenderChoices { get; } = new() { "女", "男", "其他" };

    private void LoadCycleRules(UserSettings s)
    {
        _cycleRules.Clear();
        foreach (var (gender, rule) in s.CycleRules) _cycleRules[gender] = rule;
        foreach (var gender in _cycleRules.Keys)
            if (!CycleGenderChoices.Contains(gender)) CycleGenderChoices.Add(gender);

        // 后备字段直写，理由同天气那一段：构造期触发变更钩子会把旧设置又写回盘
        _cycleGender = CycleGenderChoices.FirstOrDefault(g => _cycleRules.ContainsKey(g)) ?? "女";
        var first = _cycleRules.TryGetValue(_cycleGender, out var r) ? r : CycleRule.DefaultFor(_cycleGender);
        _cycleTracked = first.Enabled;
        _cycleLength = first.CycleLength;
        _periodLength = first.PeriodLength;
    }

    partial void OnCycleGenderChanged(string value)
    {
        _loadingCycle = true;
        var rule = _cycleRules.TryGetValue(value, out var r) ? r : CycleRule.DefaultFor(value);
        CycleTracked = rule.Enabled;
        CycleLength = rule.CycleLength;
        PeriodLength = rule.PeriodLength;
        _loadingCycle = false;
    }

    partial void OnCycleTrackedChanged(bool value) => SaveCycleRule();
    partial void OnCycleLengthChanged(int value) => SaveCycleRule();
    partial void OnPeriodLengthChanged(int value) => SaveCycleRule();

    private void SaveCycleRule()
    {
        if (_loadingCycle) return;
        var g = (CycleGender ?? "").Trim();
        if (g.Length == 0) return;
        _cycleRules[g] = new CycleRule
        {
            Enabled = CycleTracked,
            CycleLength = Math.Clamp(CycleLength, 7, 60),
            PeriodLength = Math.Clamp(PeriodLength, 1, Math.Clamp(CycleLength, 7, 60)),
        };
        PersistSettings();
    }

    /// <summary>高级：给一个自定义性别单独立一条周期规则。</summary>
    [RelayCommand]
    private async Task AddCycleGenderAsync()
    {
        var input = await Shell.Current.DisplayPromptAsync("自定义性别", "这个性别的称呼（例如：机器人 / 双性 / 精灵）：",
            "添加", "取消", maxLength: 12);
        var g = (input ?? "").Trim();
        if (g.Length == 0) return;
        if (_cycleRules.ContainsKey(g)) { CycleGender = g; return; }
        _cycleRules[g] = CycleRule.DefaultFor(g);
        if (!CycleGenderChoices.Contains(g)) CycleGenderChoices.Add(g);
        CycleGender = g;
        PersistSettings();
    }


    public List<int> CycleChoices { get; } = Enumerable.Range(21, 15).ToList();
    public List<int> PeriodChoices { get; } = Enumerable.Range(3, 5).ToList();


    // ============ 金钱：自由增加 / 设为指定余额 ============

    [RelayCommand]
    private async Task AddMoney()
    {
        if (!int.TryParse((MoneyInput ?? "").Trim(), out var amount) || amount == 0)
        {
            Toast.Warn("输入一个整数金额（可负数，用于扣减）。");
            return;
        }
        await _shop.InitializeAsync();
        await _shop.AddCoinsAsync(amount);
        WalletText = _shop.WalletLabel;
    }

    [RelayCommand]
    private async Task SetMoney()
    {
        if (!int.TryParse((MoneyInput ?? "").Trim(), out var amount) || amount < 0)
        {
            Toast.Warn("输入一个不小于 0 的整数。");
            return;
        }
        await _shop.InitializeAsync();
        await _shop.SetCoinsAsync(amount);
        WalletText = _shop.WalletLabel;
    }

}
