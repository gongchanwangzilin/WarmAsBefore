using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Dispatching;
using WarmAsBefore.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Models.Background;
using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Modules.Automation;
using WarmAsBefore.Modules.Outfit;
using WarmAsBefore.Modules.RealWorld;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;
using RealTimeProvider = WarmAsBefore.Modules.RealWorld.TimeProvider;

namespace WarmAsBefore.ViewModels;

public sealed partial class MainGameViewModel
{
    [ObservableProperty] private bool _weatherBackgroundEnabled = true;
    [ObservableProperty] private bool _weatherBackgroundVisible;
    [ObservableProperty] private double _weatherBackgroundOpacity;
    [ObservableProperty] private string _weatherBackgroundColor = "#00000000";
    // ===== 统一背景系统：场景种类 / 可见性 / 音效 / 闪电信号（由 BackgroundService.Changed 同步） =====
    [ObservableProperty] private WeatherSceneKind _sceneKind = WeatherSceneKind.Sunny;
    [ObservableProperty] private bool _weatherSceneVisible = false;
    [ObservableProperty] private bool _sceneAudioEnabled = true;
    [ObservableProperty] private double _sceneAudioLevel = 0.6;
    /// <summary>闪电信号：每次打雷自增，视图监听它触发一次场景层闪光（不透明度/缩放动画由视图决定）。</summary>
    [ObservableProperty] private int _lightningFlashSignal;

    // ===== 场景光照：由「当前时间 + 真实天气的能见度/云量」算出，暴露给页面驱动 WeatherSceneLayer.SetLighting =====
    /// <summary>光源水平方向：-1 偏左(东/上午) → 1 偏右(西/下午)。</summary>
    [ObservableProperty] private double _sceneLightDirX = -0.7;
    /// <summary>光源仰角：0 近地平线，1 正当天顶（正午最高，夜间月光低仰角）。</summary>
    [ObservableProperty] private double _sceneLightElevation = 0.5;
    /// <summary>光照强度 0..1：由能见度 + 云量决定，夜间再压暗。</summary>
    [ObservableProperty] private double _sceneLightIntensity = 0.7;
    /// <summary>光柱/折射等光效开关：总开关关闭时为 false（基础照明仍保留）。</summary>
    [ObservableProperty] private bool _sceneLightEffectsEnabled = true;
    /// <summary>光照总开关：由统一背景配置下发；关闭后只关光柱/折射、保留基础光。</summary>
    [ObservableProperty] private bool _sceneLightEnabled = true;

    partial void OnWeatherBackgroundEnabledChanged(bool value)
    {
        if (!value)
        {
            WeatherBackgroundVisible = false;
            WeatherBackgroundOpacity = 0;
            WeatherBackgroundColor = "#00000000";
            return;
        }

        ApplyWeatherBackground(WeatherDesc);
    }

    partial void OnWeatherDescChanged(string value)
    {
        // 旧层（底色/透明度）照常跟随真实天气，避免大回归。
        ApplyWeatherBackground(value);
        // 新层：默认「手动选的场景优先」，真实天气不覆盖用户在设置里选的场景。
        // 只有显式打开开关（暂未暴露 UI，留作扩展点）时才让真实天气推给统一背景系统。
        if (_weatherSceneFollowsRealWeather) _background.ApplyWeatherDescription(value);
    }

    /// <summary>
    /// 把统一配置里的场景种类 / 可见性 / 音效 / 光照开关同步到 VM 展示属性，
    /// 并把音效参数下发给 WeatherAudioService（启动与 Changed 订阅共用同一条路径）。
    /// </summary>
    private void SyncSceneFromBackground(BackgroundConfig cfg)
    {
        SceneKind = cfg.Scene;
        WeatherSceneVisible = cfg.ExperimentalEnabled && cfg.Mode == BackgroundMode.Weather;
        SceneAudioEnabled = cfg.AudioEnabled;
        SceneAudioLevel = cfg.AudioLevel;
        SceneLightEnabled = cfg.SceneLightEnabled;
        _weatherAudio.Apply(cfg.Scene, cfg.ExperimentalEnabled && cfg.AudioEnabled, cfg.AudioLevel);
    }

    /// <summary>
    /// 上一次应用过的天气来源指纹：mode|城市|坐标。
    /// NotifyChanged 不只天气设置会发（改标定、装素材包都会发），而每次应用都要联网拉一次天气，
    /// 所以先比指纹，没变就不重复请求、也不重复刷 SceneDirector 的天气门控。
    /// </summary>
    private string _mapWeatherKey = "";
    private bool _mapWeatherApplied;

    /// <summary>
    /// 把「这张图按哪儿算天气」告诉 <see cref="WeatherProvider"/>：
    /// user = 跟随用户（清掉覆盖）；map = 用抓取位置的城市 / 中心坐标；custom = 用户填的地点。
    /// 之后立刻重拉一次天气并刷新场景天气门控 —— 否则主界面会继续显示上一张图那座城市的天气。
    /// </summary>
    private void ApplyMapWeather()
    {
        try
        {
            if (!_map.IsLoaded) return;
            var map = _map.Map;
            var mode = MapWeatherModes.Normalize(map.WeatherMode);
            var city = mode == MapWeatherModes.Map
                ? (!string.IsNullOrWhiteSpace(map.WeatherCity) ? map.WeatherCity : map.City)
                : mode == MapWeatherModes.Custom ? map.WeatherCity : "";
            var key = string.Create(CultureInfo.InvariantCulture,
                $"{mode}|{city}|{map.CenterLatitude:0.######}|{map.CenterLongitude:0.######}");
            if (_mapWeatherApplied && key == _mapWeatherKey) return;
            _mapWeatherKey = key;
            _mapWeatherApplied = true;

            switch (mode)
            {
                case MapWeatherModes.Map:
                {
                    // 0,0 是「这张图没有抓取位置」的默认值，不能当坐标拿去查天气。
                    var hasCoord = Math.Abs(map.CenterLatitude) > 1e-6 && Math.Abs(map.CenterLongitude) > 1e-6;
                    _weather.SetMapWeather(MapWeatherModes.Map, city,
                        hasCoord ? map.CenterLatitude : null,
                        hasCoord ? map.CenterLongitude : null);
                    break;
                }
                case MapWeatherModes.Custom:
                    // 自定义地点只有名字：坐标交给 WeatherProvider 首次拉天气时自己地理编码。
                    _weather.SetMapWeather(MapWeatherModes.Custom, city, null, null);
                    break;
                default:
                    _weather.ClearMapWeather();
                    break;
            }

            App.WriteLog($"MainGame.ApplyMapWeather -> mode={mode}, override={_weather.HasMapWeatherOverride}, city={city}");
            _ = FetchWeather();
            _ = _sceneDirector.RefreshWeatherNowAsync();
        }
        catch (Exception ex)
        {
            // 天气是锦上添花：这里出错只记日志，绝不能把主界面拖垮。
            App.WriteLog("MainGame.ApplyMapWeather -> " + ex);
        }
    }

    /// <summary>把设置里的天气事件参数同步进 SceneDirector（设置变更与启动时各调一次）。</summary>
    private void SyncWeatherEventConfig()
    {
        var s = _settings.Current;
        _sceneDirector.SetWeatherEventConfig(new Modules.Scene.SceneDirector.WeatherEventConfig
        {
            LightningEnabled = s.LightningEnabled,
            LightningProbability = s.LightningProbability,
            RainEventEnabled = s.RainEventEnabled,
            RainProbability = s.RainProbability
        });
    }

    /// <summary>
    /// 天气事件回调（打雷/下雨，可能在 Timer 线程）：
    ///  - 打雷：若配了帧图则按序播放（播放期间叠图片），否则默认「屏幕白一下再慢慢恢复」；
    ///    同时按配置播放雷声；白闪时长与帧切换间隔取 WeatherEffectFrameMs。
    ///  - 下雨：播放雨声（若配了）；若配了雨帧则循环显示。
    /// 全程切到 UI 线程渲染，异常吞掉不影响主对话。
    /// </summary>
    private async void OnWeatherEvent(Modules.Scene.SceneDirector.WeatherEventKind kind, string? weather)
    {
        try
        {
            try
            {
                // 统一背景系统联动：打雷时播放雷声音效并自增闪电信号，交给视图层做场景闪光。
                // 下面原有的白闪/帧特效是独立机制，保留不动，两种表现并存。
                if (kind == Modules.Scene.SceneDirector.WeatherEventKind.Lightning)
                {
                    _weatherAudio.PlayThunder();
                    Helpers.Ui.Post(() => LightningFlashSignal++);
                }

                var s = _settings.Current;
                var frames = kind == Modules.Scene.SceneDirector.WeatherEventKind.Lightning
                    ? s.LightningFrames
                    : s.RainFrames;
                var sfx = kind == Modules.Scene.SceneDirector.WeatherEventKind.Lightning
                    ? s.LightningSfxPath
                    : s.RainSfxPath;
                var frameMs = Math.Clamp(s.WeatherEffectFrameMs, 30, 5000);

                // 该回调从 Timer 线程进来：原先的 BeginInvoke 在 WinUI 线程池上取不到 DispatcherQueue 会直接抛
                // （见 Helpers/Ui.cs 的实证记录），这里统一走 Ui.PostAsync 回 UI 线程。
                await Helpers.Ui.PostAsync(async () =>
                {
                    try
                    {
                        // 音效：优先取「音效库卡槽」（素材库长按 → 设为打雷音 / 雨声音）；没装填再回落设置里的路径。
                        // 两条都没有就不放音（画面照常闪）。
                        var slotKind = kind == Modules.Scene.SceneDirector.WeatherEventKind.Lightning ? "lightning" : "rain";
                        if (await _audio.HasAssignedAsync(slotKind))
                            _audio.PlayAssigned(slotKind);
                        else if (!string.IsNullOrWhiteSpace(sfx))
                            _audio.PlaySfxFile(sfx);

                        // 视觉：有帧图则按序切换；打雷且无帧图 → 默认白闪。
                        // 帧图一次性预建成 ImageSource 实例（装载失败的跳过），循环里只在这批缓存实例间轮换：
                        // 不再逐帧 FromFile —— 那会不停分配大对象，且每次换新实例都触发一次换源 Measure 失效。
                        var usable = frames.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
                        var sources = new List<ImageSource>();
                        foreach (var f in usable)
                        {
                            try { sources.Add(ImageSource.FromFile(f)); } catch { /* 故意忽略：单帧图加载失败就跳过这一帧 */ }
                        }
                        if (sources.Count > 0)
                        {
                            WeatherFxVisible = true;
                            var loops = kind == Modules.Scene.SceneDirector.WeatherEventKind.Lightning
                                ? 1 // 打雷只播一遍
                                : 3; // 下雨循环几轮（受雨声时长约束，简单起见固定 3 轮）
                            for (var loop = 0; loop < loops; loop++)
                            {
                                foreach (var src in sources)
                                {
                                    WeatherFxImage = src;
                                    await Task.Delay(frameMs);
                                }
                                if (kind == Modules.Scene.SceneDirector.WeatherEventKind.Lightning) break;
                            }
                            WeatherFxVisible = false;
                            WeatherFxImage = null;
                        }
                        else if (kind == Modules.Scene.SceneDirector.WeatherEventKind.Lightning)
                        {
                            // 默认表现：屏幕白一下，再慢慢恢复。
                            FlashOpacity = 1.0;
                            await Task.Delay(frameMs);
                            var steps = 12;
                            for (var i = steps; i >= 0; i--)
                            {
                                FlashOpacity = i / (double)steps * 0.9;
                                await Task.Delay(45);
                            }
                            FlashOpacity = 0;
                        }
                    }
                    catch (Exception ex) { App.WriteLog("MainGame.OnWeatherEvent(render) -> " + ex); }
                });
            }
            catch (Exception ex)
            {
                App.WriteLog("MainGame.OnWeatherEvent -> " + ex);
            }
        }
        catch (Exception ex) { App.WriteLog("MainGame.OnWeatherEvent -> " + ex); }
    }

    /// <summary>场景库模式被应用（地图/纯库场景）时渲染背景与 BGM。</summary>
    private void OnSceneModeApplied(Models.SceneLibraryEntry entry, Models.SceneMode? mode)
    {
        Helpers.Ui.Post(() =>
        {
            // 亮度遮罩跟着生效模式走；「早上开灯看不出变化」已由 SceneDirector 判定并写进给 AI 的回报里
            SceneDim = _sceneDirector.ActiveDim;
            var bg = mode?.Background is { Length: > 0 } mb ? mb : entry.Background;
            var color = mode?.BackgroundColor is { Length: > 0 } mc ? mc : entry.BackgroundColor;
            if (string.IsNullOrWhiteSpace(bg))
            {
                if (!string.IsNullOrEmpty(color)) SceneBg = color;
                // 纯库场景才清空背景。地图引用条目（entry.Background 为空）是"背景交给地图场景去画"，
                // 这里清空会把刚由 SceneChanged 渲染好的地图背景抹掉。
                if (string.IsNullOrEmpty(entry.MapSceneRef)) SceneBackdrop = null;
            }
            else
            {
                SceneBackdrop = ImageSource.FromFile(bg);
                if (!string.IsNullOrEmpty(color)) SceneBg = color;
            }
            // 背景换了，标定也得跟着换，否则角色会按上一张图的尺度站到错的位置
            ApplyModePlacement(entry, mode);
            // 场景库模式也可能把背景让回纯色：这时角色背景要顶上来，反之则继续让位。
            RefreshCharacterBackdrop();
        });
    }

    private void ApplyWeatherBackground(string? condition)
    {
        if (!WeatherBackgroundEnabled)
        {
            WeatherBackgroundVisible = false;
            WeatherBackgroundOpacity = 0;
            WeatherBackgroundColor = "#00000000";
            return;
        }

        var normalized = (condition ?? "").Trim();
        var tuple = normalized.ToLowerInvariant() switch
        {
            var c when c.Contains("storm") || c.Contains("雷") => ("#4a5674", 0.28),
            var c when c.Contains("rain") || c.Contains("阵雨") || c.Contains("雨") => ("#536a7d", 0.24),
            var c when c.Contains("snow") || c.Contains("雪") => ("#dfeaf7", 0.18),
            var c when c.Contains("fog") || c.Contains("雾") || c.Contains("mist") => ("#b5b1a8", 0.16),
            var c when c.Contains("cloud") || c.Contains("阴") || c.Contains("多云") => ("#8d99a4", 0.14),
            var c when c.Contains("sun") || c.Contains("晴") || c.Contains("clear") => ("#f7d791", 0.10),
            _ => ("#00000000", 0.0)
        };

        WeatherBackgroundColor = tuple.Item1;
        WeatherBackgroundOpacity = tuple.Item2;
        WeatherBackgroundVisible = tuple.Item2 > 0.01;
    }

    /// <summary>
    /// 按「当前时间 + 最近一次真实天气」算场景光照，赋给 4 个 ObservableProperty，
    /// 供页面订阅后驱动 WeatherSceneLayer.SetLighting(dirX, elevation, intensity, lightFxEnabled)。
    /// 时间定太阳/月亮方向与仰角，能见度 + 云量定强度；FetchWeather 可能在后台线程，赋值统一走 Ui.Post 回 UI 线程。
    /// </summary>
    private void ComputeLighting(WeatherReading? w)
    {
        Helpers.Ui.Post(() =>
        {
            if (!_background.Current.ExperimentalEnabled) return; // 测试版总开关关闭：光照参数不下发，避免空闲的属性变化驱动场景层。
            var hour = _time.Now().Now.Hour; // 0..23
            bool isNight = hour < 6 || hour > 18;

            // 时间定方向：白天太阳 6 点东升(仰角≈0) → 12 点正午最高(1) → 18 点西落(≈0)；夜间月光低仰角、偏右。
            double elevation = isNight
                ? 0.25
                : Math.Clamp(Math.Sin((hour - 6) / 12.0 * Math.PI), 0.0, 1.0);
            double dirX = isNight
                ? 0.5
                : Math.Clamp((hour - 12) / 6.0, -1.0, 1.0);

            // 能见度 + 云量定强度：能见度越高越亮(10000 米封顶、最低留 0.15)，云量 0..100 最多削弱 60%。
            double intensity;
            if (w is not null && w.VisibilityMeters >= 0)
            {
                double visFactor = Math.Clamp(w.VisibilityMeters / 10000.0, 0.15, 1.0);
                // 云量未知(-1)按无云处理。
                double cloudFactor = w.CloudCover >= 0
                    ? 1.0 - Math.Clamp(w.CloudCover, 0.0, 100.0) / 100.0 * 0.6
                    : 1.0;
                intensity = Math.Clamp(0.35 + 0.65 * visFactor * cloudFactor, 0.0, 1.0);
            }
            else
            {
                intensity = 0.6; // 无数据 / 能见度未知：兜底强度
            }
            if (isNight) intensity *= 0.35; // 夜间整体压暗

            // 总开关关掉时只关光柱/折射特效，基础照明保留（云仍受光）。
            SceneLightDirX = dirX;
            SceneLightElevation = elevation;
            SceneLightIntensity = Math.Clamp(intensity, 0.0, 1.0);
            SceneLightEffectsEnabled = SceneLightEnabled;
        });
    }

    private async Task FetchWeather()
    {
        var w = await _weather.Fetch();
        if (w is not null)
        {
            _lastWeather = w; // 缓存，供 UpdateTime 按时间重算时仍带上能见度/云量
            WeatherDesc = w.Description;
            // 天气读数本来就取到了温度与云量，只是以前只把一句话摆上 HUD：
            // 现在温度进 OUTDOOR 块、云量变成 WEATHER 块的进度线（取不到就 -1，不画）。
            WeatherTempText = double.IsFinite(w.Celsius) ? $"{Math.Round(w.Celsius):0}°" : "";
            CloudCoverPct = w.CloudCover >= 0 ? Math.Clamp(w.CloudCover / 100.0, 0, 1) : -1;
            ApplyWeatherBackground(w.Description);
            if (_weatherSceneFollowsRealWeather && _background.Current.ExperimentalEnabled) _background.ApplyWeatherDescription(w.Description);
            ComputeLighting(w); // 真天气驱动：按能见度 + 时间算光
        }
        else
        {
            _lastWeather = null;
            WeatherTempText = "";
            CloudCoverPct = -1;
            ApplyWeatherBackground(null);
            ComputeLighting(null); // 无数据也按时间算一次光
        }
    }
}
