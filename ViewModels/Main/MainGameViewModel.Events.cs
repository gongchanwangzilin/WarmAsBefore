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
    // ==================== 单例服务事件挂/摘（审计 A4） ====================
    // VM 是 Transient，而 Map/SceneDirector/Settings/Automation/Blackout 都是 Singleton：
    // 订阅后不解绑，每进出一次主界面就叠一份，表现为「切一次场景触发多次 ApplyScene」
    // 与内存持续上涨。页面 OnAppearing 调 Attach、OnDisappearing 调 Detach。
    private bool _runtimeEventsAttached;
    private bool _mapEventsAttached;

    private void OnBlackoutChanged(bool on, string reason) =>
        Helpers.Ui.Post(() => { IsBlackout = on; BlackoutReason = reason; });

    private void HookMapEvents()
    {
        if (_mapEventsAttached) return;
        _mapEventsAttached = true;
        _map.SceneChanged += OnMapSceneChanged;
        _map.WalkSegmentStarted += OnWalkSegmentStarted;
        _map.WalkRouteCompleted += OnWalkRouteCompleted;
        _map.MapChanged += OnMapChanged;
    }

    private void UnhookMapEvents()
    {
        if (!_mapEventsAttached) return;
        _mapEventsAttached = false;
        _map.SceneChanged -= OnMapSceneChanged;
        _map.WalkSegmentStarted -= OnWalkSegmentStarted;
        _map.WalkRouteCompleted -= OnWalkRouteCompleted;
        _map.MapChanged -= OnMapChanged;
    }

    /// <summary>页面进场调用；重复调用无副作用。</summary>
    public void AttachRuntimeEvents()
    {
        // 进主界面（含从设置页修好 API 后返回）：解除 API 错误对被动 AI 的封停。
        Services.Core.BugReporter.Reset("AI");
        if (_runtimeEventsAttached) return;
        _runtimeEventsAttached = true;
        if (_map.IsLoaded) HookMapEvents();
        _sceneDirector.ModeApplied += OnSceneModeApplied;
        _sceneDirector.WeatherEventTriggered += OnWeatherEvent;
        _settings.Applied += SyncWeatherEventConfig;
        _auto.GreetingReady += OnGreet;
        _blackout.Changed += OnBlackoutChanged;
        // 统一背景配置变化 → 同步场景/音效到 VM（服务内部已用 Ui.Post 切回 UI 线程）。
        // 订阅必须走 Attach/Detach（审计 A4）：VM 是 Transient、BackgroundService 是 Singleton，
        // 放构造里会每进出一次主界面叠一份，这里随页面进出场挂摘。
        _background.Changed += OnBackgroundChanged;
        // API 错误独立通道：ChatEngine 把端点失败走这里，不再伪装成 AI 台词。
        // 收到后红色 Toast + 封停被动 AI（不再走 BugReporter 分析窗），错误本身绝不进聊天气泡。
        _chat.ErrorOccurred += OnApiError;
        // 测试版总开关可能在设置页里改过：那时本 VM 已随离场退订 Changed，收不到通知。
        // 进场重订阅后必须主动重读一次当前配置，否则天气氛围层的 IsVisible 绑定会停在旧值
        // （雨层由页面 OnAppearing 自己同步，底层场景与音效靠这里跟上）。同步是幂等的。
        SyncSceneFromBackground(_background.Current);
        _clock?.Start();
    }

    /// <summary>页面离场调用；把 Singleton 上的订阅全部摘掉，界面时钟一并停走。</summary>
    public void DetachRuntimeEvents()
    {
        if (!_runtimeEventsAttached) return;
        _runtimeEventsAttached = false;
        UnhookMapEvents();
        _sceneDirector.ModeApplied -= OnSceneModeApplied;
        _sceneDirector.WeatherEventTriggered -= OnWeatherEvent;
        _settings.Applied -= SyncWeatherEventConfig;
        _auto.GreetingReady -= OnGreet;
        _blackout.Changed -= OnBlackoutChanged;
        _background.Changed -= OnBackgroundChanged;
        _chat.ErrorOccurred -= OnApiError;
        _clock?.Stop();
    }

    /// <summary>
    /// API 调用失败：红色 Toast 立即告知，并封停「AI」子系统，暂停一切被动 AI 调用
    /// （主动对话 / 心跳 AI / 后台生成；各调用点须检查 BugReporter.IsStopped("AI") 跳过），
    /// 直到下次进存档 / 切存档 / 新建角色 / 用户重新指定 AI 时 Reset("AI")。
    /// 这是可恢复的请求级错误，不再走 BugReporter.Report 异常管线（不弹分析窗、不写 bugs.log）；
    /// 真正的非 API 异常仍照常由各子系统 Report。message 是网关编码的错误文本，绝不写进会话或记忆。
    /// </summary>
    private void OnApiError(string charId, string message)
    {
        // 先立即封停，再弹提示：即便 UI 线程正忙，后台的被动 AI 也不会继续排队重试/烧 token。
        Services.Core.BugReporter.Stop("AI");
        Helpers.Ui.Post(() =>
        {
            try
            {
                var who = _engine.Roster.TryGetValue(charId, out var ch) ? ch.Profile.Name : "角色";
                Controls.Toast.Error($"AI 暂时连不上（{who}）：{ShortApiError(message)}。请检查 API 设置，重新进存档或换一条端点后恢复。");
            }
            catch (Exception ex) { App.WriteLog("MainGameViewModel.OnApiError -> " + ex.Message); }
        });
    }

    /// <summary>识别网关的错误串：这些前缀是 ApiGateway/ChatEngine 对失败的编码，不是 AI 台词。</summary>
    private static bool IsApiErrorText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.TrimStart();
        return t.StartsWith("[API错误", StringComparison.Ordinal)
            || t.StartsWith("[网络连接失败", StringComparison.Ordinal)
            || t.StartsWith("[请求超时", StringComparison.Ordinal)
            || t.StartsWith("[请求排队超时", StringComparison.Ordinal);
    }

    /// <summary>错误文本截短给 Toast（分析窗里保留完整原文）。</summary>
    private static string ShortApiError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "未知错误";
        var one = message.Replace("\r", " ").Replace("\n", " ").Trim();
        return one.Length <= 60 ? one : one[..60] + "…";
    }

    /// <summary>统一背景配置变化回调：服务已在 UI 线程发事件，这里再做一次 Post 兜底线程纪律。</summary>
    private void OnBackgroundChanged(BackgroundConfig cfg)
        => Helpers.Ui.Post(() => SyncSceneFromBackground(cfg));
}
