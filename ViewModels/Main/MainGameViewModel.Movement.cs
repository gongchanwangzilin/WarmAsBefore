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
    // ---- 关闭 Galgame：WAB 只做我的世界的 AI / 通讯后端 ----
    [ObservableProperty] private bool _isBackendMode;
    [ObservableProperty] private string _backendStatusText = "";

    /// <summary>进主界面时刷新：设置可能在运行中被改过。</summary>
    public void RefreshBackendMode()
    {
        IsBackendMode = !_maidBridge.GalgameEnabled;
        BackendStatusText = _maidBridge.ConnectionSummary;
    }

    // ---- 标定闸门 ----
    /// <summary>当前场景没标定，界面被全屏遮罩拦住（不可关闭，必须选一个出口）。</summary>
    [ObservableProperty] private bool _isCalibrationBlocked;
    [ObservableProperty] private string _calibrationHint = "";
    /// <summary>用户选了「暂时用居中显示继续」：放行游玩，但界面常驻未标定警告。</summary>
    [ObservableProperty] private bool _isUncalibrated;
    [ObservableProperty] private string _uncalibratedHint = "";

    /// <summary>
    /// 标定闸门。
    ///
    /// 落到哪个位置、画多大，全靠标定数据算出来；没有标定就不能凭空生成角色位置，
    /// 所以这里弹一个不可关闭的遮罩。
    ///
    /// 但**必须留出口**：用户完全可能只是选错了一张图、想换一张，此时若不给路走就会被永久卡住。
    /// 三个出口分别是「去标定」「移除这张背景」「暂时用居中显示继续」。
    /// </summary>
    private void UpdateCalibrationGate(MapScene scene)
    {
        if (!_cal.NeedsCalibration(scene))
        {
            ClearCalibrationGate();
            return;
        }
        CalibrationHint = $"「{scene.Name}」的背景还没有标定，无法确定角色该站在哪、应该多大。\n\n"
                          + _cal.DescribeMissing(scene);
        IsCalibrationBlocked = true;
    }

    private void ClearCalibrationGate()
    {
        IsCalibrationBlocked = false;
        CalibrationHint = "";
        IsUncalibrated = false;
        UncalibratedHint = "";
    }

    /// <summary>出口①：去标定。标完回来重跑 ApplyScene 解除遮罩。</summary>
    [RelayCommand]
    private async Task GoCalibrateAsync()
    {
        if (_currentScene is null) return;
        var ok = await _cal.EnsureCalibratedAsync(_currentScene, SceneCalibrationService.DefaultTargetAspect);
        if (ok) ApplyScene(_currentScene);
    }

    /// <summary>
    /// 出口②：移除这张背景。场景退回纯色 —— 没有图也就不需要标定，
    /// 这是合法出口而不是"跳过标定"（跳过标定指的是带着图却不标）。
    /// </summary>
    [RelayCommand]
    private async Task RemoveSceneBackgroundAsync()
    {
        var scene = _currentScene;
        if (scene is null) return;
        scene.Background = "";
        scene.Placement = null;
        await _map.SaveAsync();
        _map.NotifyChanged();
        ApplyScene(scene);
    }

    /// <summary>出口③：暂时用居中显示继续玩。放行但常驻警告，直到补上标定。</summary>
    [RelayCommand]
    private void ContinueCentered()
    {
        IsCalibrationBlocked = false;
        IsUncalibrated = true;
        UncalibratedHint = $"「{_currentScene?.Name}」背景未标定，角色位置已回退为居中显示，点此补标定";
    }

    /// <summary>地图 overlay 里点场景直接走过去。</summary>
    [RelayCommand]
    private async Task WalkToScene(string sceneId)
    {
        if (string.IsNullOrEmpty(sceneId) || sceneId == _map.CurrentSceneId) return;
        try
        {
            IsWalking = true;
            var walk = await _map.MoveToAsync(sceneId);
            if (!string.IsNullOrWhiteSpace(walk)) AddMessage("assistant", walk);
        }
        catch (Exception ex) { App.WriteLog("MainGame.WalkToScene -> " + ex); }
        finally { IsWalking = false; }
    }

    // ============ 左右切换地点（#9） ============

    /// <summary>当前可切换的地点数量（不含当前所在）。为 0 时左右按键置灰。</summary>
    [ObservableProperty] private int _sceneSwitchCount;
    /// <summary>是否存在可切换的目标地点；false 时左右切换按键全部置灰。</summary>
    [ObservableProperty] private bool _hasSceneNeighbors;
    /// <summary>左键是否可用；无目标时为 false（置灰）。</summary>
    [ObservableProperty] private bool _canGoPrevScene;
    /// <summary>右键是否可用；无目标时为 false（置灰）。</summary>
    [ObservableProperty] private bool _canGoNextScene;
    /// <summary>切换按钮上显示的「上一个地点」名（用于提示，可空）。</summary>
    [ObservableProperty] private string _prevSceneLabel = "";
    /// <summary>切换按钮上显示的「下一个地点」名（用于提示，可空）。</summary>
    [ObservableProperty] private string _nextSceneLabel = "";

    /// <summary>当前在 SceneOptions 中的索引；找不到时取 0。</summary>
    private int CurrentSceneIndex()
    {
        var id = _map.CurrentSceneId;
        for (var i = 0; i < SceneOptions.Count; i++)
            if (SceneOptions[i].SceneId == id) return i;
        return SceneOptions.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// 刷新左右切换按钮的可用态与相邻地点名。呼据点：
    /// 场景列表变化、走到新地点、存档载入 —— 只要地点集合或当前位置变了就要重算，否则按钮状态会滞后。
    /// </summary>
    private void RefreshSceneSwitch()
    {
        try
        {
            var n = SceneOptions.Count;
            SceneSwitchCount = n;
            if (n <= 1)
            {
                // 只有一个（或零个）地点：没有可切的目标，左右都置灰。
                HasSceneNeighbors = false;
                CanGoPrevScene = false;
                CanGoNextScene = false;
                PrevSceneLabel = NextSceneLabel = "";
                return;
            }
            var idx = CurrentSceneIndex();
            if (idx < 0)
            {
                HasSceneNeighbors = false; CanGoPrevScene = false; CanGoNextScene = false;
                PrevSceneLabel = NextSceneLabel = ""; return;
            }
            // 循环切换：首尾相接，永远有相邻项（因为 n>1）。
            var prev = SceneOptions[(idx - 1 + n) % n];
            var next = SceneOptions[(idx + 1) % n];
            HasSceneNeighbors = true;
            CanGoPrevScene = true;
            CanGoNextScene = true;
            PrevSceneLabel = prev.Label;
            NextSceneLabel = next.Label;
        }
        catch (Exception ex) { App.WriteLog("MainGame.RefreshSceneSwitch -> " + ex); }
    }

    /// <summary>切换到上一个地点（首尾循环）。无目标时静默忽略。</summary>
    [RelayCommand]
    private async Task PrevSceneAsync()
    {
        var n = SceneOptions.Count;
        if (n <= 1) return;
        var idx = CurrentSceneIndex();
        if (idx < 0) return;
        await WalkToScene(SceneOptions[(idx - 1 + n) % n].SceneId);
    }

    /// <summary>切换到下一个地点（首尾循环）。无目标时静默忽略。</summary>
    [RelayCommand]
    private async Task NextSceneAsync()
    {
        var n = SceneOptions.Count;
        if (n <= 1) return;
        var idx = CurrentSceneIndex();
        if (idx < 0) return;
        await WalkToScene(SceneOptions[(idx + 1) % n].SceneId);
    }

    /// <summary>解析 AI 回复中的【移动:场景名】标记并执行移动，移动结果附在回复末尾。</summary>
    private async Task<string> ExecuteMoveMarkersAsync(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply) || !reply.Contains("【移动:", StringComparison.Ordinal))
            return reply;
        var result = reply;
        var notes = new List<string>();
        foreach (Match m in Regex.Matches(reply, @"【移动:([^】]+)】"))
        {
            var sceneName = m.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(sceneName)) continue;
            try
            {
                IsWalking = true;
                var walk = await ExecuteSingleMoveAsync(sceneName);
                if (!string.IsNullOrWhiteSpace(walk)) notes.Add(walk);
            }
            catch (Exception ex)
            {
                App.WriteLog("MainGame.ExecuteMoveMarkersAsync -> " + ex);
            }
            finally { IsWalking = false; }
        }
        result = Regex.Replace(result, @"【移动:[^】]+】", "").Trim();
        if (notes.Count > 0)
            result = $"{result}\n{string.Join("\n", notes)}";
        // 场景库【场景:条目/模式】标记（时间关灯 / AI 指令切换）
        var (cleanReply, sceneNote) = await _sceneDirector.ExecuteMarkersAsync(result);
        if (!string.IsNullOrEmpty(sceneNote))
            cleanReply = cleanReply + "\n" + sceneNote;
        return cleanReply;
    }

    /// <summary>
    /// 执行一次【移动:场景名】，返回移动过程描述（作为一段回答展示）。
    /// #9 行进扩展：
    ///  - 设置「AI 单独出行」关闭（默认）→ 维持旧行为：移动用户视角，AI 走到哪用户跟到哪。
    ///  - 设置「AI 单独出行」开启 → 只移动 AI 自己的 actor 位置，用户视角停原地；
    ///    随后若「允许跟上」，主界面出现「跟上」入口（FollowAiCommand），由用户决定是否跟上。
    /// </summary>
    private async Task<string?> ExecuteSingleMoveAsync(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return null;
        if (!_engine.MapUnlocked) return null;
        try
        {
            IsWalking = true;
            var alone = _settings.Current.AiTravelAlone;

            if (!alone)
            {
                // 旧行为：AI 带着用户一起走（移动用户视角）。
                var walk = await _map.MoveToAsync(sceneName);
                ClearFollowOffer();
                return walk;
            }

            // 新行为：AI 单独走。先把当前 AI 注册成 actor（若尚未注册），再单独移动它。
            var actorId = EnsureAiActor();
            var target = _map.Map.SceneById(sceneName) ?? _map.Map.SceneByName(sceneName);
            if (target is null) return null;

            var note = _mapActors.MoveActorToScene(actorId, target.Id);
            if (string.IsNullOrWhiteSpace(note))
            {
                // 无法单独移动（不可达等）：退回旧行为，保证 AI 仍能到达目的地。
                ClearFollowOffer();
                return await _map.MoveToAsync(sceneName);
            }
            // 成功单独出行：用户视角没动。若允许跟上且用户不在同一场景，亮出「跟上」入口。
            var userScene = _map.CurrentSceneId;
            if (_settings.Current.AllowFollowAi && userScene != target.Id)
            {
                PendingFollowSceneId = target.Id;
                PendingFollowSceneName = target.Name;
                CanFollowAi = true;
            }
            else
            {
                ClearFollowOffer();
            }
            return note;
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGame.ExecuteSingleMoveAsync -> " + ex);
            return null;
        }
        finally { IsWalking = false; }
    }

    // ============ 行进扩展：AI 单独出行 / 用户跟上（#9） ============

    /// <summary>「跟上」入口是否可用：AI 单独走到别处且用户没跟上时为 true。</summary>
    [ObservableProperty] private bool _canFollowAi;
    /// <summary>待跟上的目标场景名（按钮/提示用）。</summary>
    [ObservableProperty] private string _pendingFollowSceneName = "";
    /// <summary>待跟上的目标场景 Id（内部用）。</summary>
    private string PendingFollowSceneId { get; set; } = "";

    /// <summary>确保当前 AI 角色已在地图 actor 里注册，返回其 actor Id。</summary>
    private string EnsureAiActor()
    {
        var ch = _engine.ActiveCharacter;
        var id = ch?.Profile.Id;
        var name = ch?.Profile.Name ?? CharacterName;
        if (string.IsNullOrEmpty(id)) id = "ai_active";
        if (_mapActors.Get(id) is null)
            _mapActors.Register(id, name, isUser: false, sceneId: _map.CurrentSceneId);
        return id;
    }

    /// <summary>清掉「跟上」提示（AI 未单独出行 / 已跟上 / 用户已同场景时调用）。</summary>
    private void ClearFollowOffer()
    {
        CanFollowAi = false;
        PendingFollowSceneId = "";
        PendingFollowSceneName = "";
    }

    /// <summary>用户选择「跟上」AI：把用户视角移动到 AI 所在的场景。</summary>
    [RelayCommand]
    private async Task FollowAiAsync()
    {
        if (!CanFollowAi || string.IsNullOrEmpty(PendingFollowSceneId)) return;
        var sceneId = PendingFollowSceneId;
        try
        {
            IsWalking = true;
            var walk = await _map.MoveToAsync(sceneId);
            if (!string.IsNullOrWhiteSpace(walk)) AddMessage("assistant", walk);
        }
        catch (Exception ex) { App.WriteLog("MainGame.FollowAi -> " + ex); }
        finally
        {
            IsWalking = false;
            ClearFollowOffer();
        }
    }
}
