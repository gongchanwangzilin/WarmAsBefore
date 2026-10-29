using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.Drawables;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Modules.Automation;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

public sealed partial class PhoneViewModel : ObservableObject, IQueryAttributable
{
    private readonly SettingsManager _settings;
    private readonly Modules.RealChat.OfficialChatBridge _bridge;
    private readonly Modules.RealWorld.TimeProvider _time;
    private readonly GameEngine _engine;

    [ObservableProperty] private string _timeLabel = "";
    [ObservableProperty] private string _weatherText = "晴天";
    [ObservableProperty] private string _statusTime = "";
    [ObservableProperty] private string _weChatLabel = "微信";
    [ObservableProperty] private string _statusLine = "温暖如初 · 手机";
    /// <summary>战斗系统在设置里开着时，手机桌面才给「战斗」入口——入口不该比功能多。</summary>
    [ObservableProperty] private bool _showBattle;
    /// <summary>这扇手机是不是自己一扇独立窗（独立窗的「返回」= 关窗，不是翻主窗口的栈）。</summary>
    [ObservableProperty] private bool _inWindow;

    public PhoneViewModel(SettingsManager settings,
        Modules.RealChat.OfficialChatBridge bridge, Modules.RealWorld.TimeProvider time, GameEngine engine)
    {
        _settings = settings;
        _bridge = bridge;
        _time = time;
        _engine = engine;
        RefreshStatus();
        _bridge.StatusChanged += _ => MainThread.BeginInvokeOnMainThread(RefreshStatus);
        _settings.Applied += RefreshStatus;
    }

    public void OnAppearing()
    {
        var info = _time.Now();
        TimeLabel = info.Now.ToString("HH:mm");
        StatusTime = $"{info.Now:MM-dd} {info.Period switch { "morning" => "上午", "noon" => "中午", "afternoon" => "下午", "evening" => "傍晚", _ => "晚上" }}";
        RefreshStatus();
    }

    /// <summary>微信图标下的小字：真微信未开启时显示「本地」。</summary>
    private void RefreshStatus()
    {
        var real = _settings.Current.WechatEnabled
            && _bridge.Channels.Length > 1 && _bridge.Channels[1].IsRunning;
        WeChatLabel = real ? "微信 · 真" : "微信 · 本地";
        var s = _bridge.Status;
        StatusLine = s is null or "未开启" ? "温暖如初 · 手机" : $"温暖如初 · {s}";
        ShowBattle = _settings.Current.BattleEnabled;
    }

    [RelayCommand]
    private async Task OpenChat() => await SafeNav("chat");

    /// <summary>QQ 壳：与微信壳同构的青蓝换皮会话（多论坛、无礼物/支付）。</summary>
    [RelayCommand]
    private async Task OpenQq() => await SafeNav("qqchat");

    [RelayCommand]
    private async Task OpenMap()
    {
        if (!_engine.MapUnlocked)
        {
            Toast.Warn("这一局没有绑定世界书，所以没有地图。想解锁：游戏菜单 → 本局设定 → 选一份世界书。");
            return;
        }
        await SafeNav("map");
    }

    [RelayCommand]
    private async Task OpenGallery() => await SafeNav("gallery");

    [RelayCommand]
    private async Task OpenLibrary() => await SafeNav("roster");

    [RelayCommand]
    private async Task OpenMaterials() => await SafeNav("materials");

    /// <summary>点名：人类主动点名角色，指定初始位置，强制进入对话剧情。</summary>
    [RelayCommand]
    private async Task OpenForceCast() => await SafeNav("forcecast");

    /// <summary>场景工坊：条目 / 模式（点击式切换，无需拖拽）。</summary>
    [RelayCommand]
    private async Task OpenSceneWorkshop() => await SafeNav("scenelibrary");

    [RelayCommand]
    private async Task OpenShop() => await SafeNav("shop");

    /// <summary>世界书：从小说/设定里生成世界观条目（桌面顶栏之外，手机也要能进）。</summary>
    [RelayCommand]
    private async Task OpenWorldbook() => await SafeNav("worldbook");

    /// <summary>换装：给当前角色挑一套立绘/服饰。</summary>
    [RelayCommand]
    private async Task OpenOutfit() => await SafeNav("outfit");

    [RelayCommand]
    private async Task OpenBattle() => await SafeNav("battle");

    [RelayCommand]
    private async Task OpenGame(string? kind)
    {
        var k = kind ?? "gobang";
        await SafeNav($"game?kind={k}");
    }

    [RelayCommand]
    private async Task Back()
    {
        if (InWindow) { CloseOwnWindow(); return; }
        await SafeNav("..");
    }

    /// <summary>
    /// 导航统一兜底：async void 链上任何异常都会崩 WinUI，此处拦截记录。
    /// 独立窗里点图标仍然导航主窗口（只有一整套 Shell 路由），所以先把主窗口抬到前面。
    /// </summary>
    private async Task SafeNav(string route)
    {
        try
        {
            if (InWindow) WindowTopmost.BringToFront();
            await Shell.Current.GoToAsync(route);
        }
        catch (Exception ex)
        {
            App.WriteLog($"PhoneViewModel.SafeNav({route}) -> {ex.Message}");
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("inWindow", out var v) && v?.ToString() == "1") InWindow = true;
    }

    private void CloseOwnWindow()
    {
        var app = Application.Current;
        var win = app?.Windows.FirstOrDefault(w => ReferenceEquals(w.Page?.BindingContext, this));
        if (win is not null) app!.CloseWindow(win);
    }
}
