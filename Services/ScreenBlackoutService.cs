using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 黑屏：AI 可以选择把画面黑掉（适合某些时刻——比如房间里关了灯、不想被看见、话题太私密），
/// 但**黑屏不代表不能对话**：输入与聊天面板照常可用，只是舞台什么都不显示。
///
/// 状态挂在存档上（<see cref="GameState.Blackout"/>），所以读档后仍然保持；用户也可以手动取消。
/// </summary>
public sealed class ScreenBlackoutService
{
    private readonly GameEngine _engine;

    public ScreenBlackoutService(GameEngine engine) => _engine = engine;

    /// <summary>黑屏状态变化时触发（true = 进入黑屏）。</summary>
    public event Action<bool, string>? Changed;

    public bool IsBlack
    {
        get => _engine.State.Blackout;
        set => Set(value, "");
    }

    public string Reason => _engine.State.BlackoutReason;

    /// <summary>设置黑屏。<paramref name="reason"/> 是给用户看的说明（AI 可以留空）。</summary>
    public void Set(bool black, string reason = "")
    {
        var before = _engine.State.Blackout;
        _engine.State.Blackout = black;
        _engine.State.BlackoutReason = black ? (reason ?? "").Trim() : "";
        if (before != black || black) Changed?.Invoke(black, _engine.State.BlackoutReason);
    }

    /// <summary>把存档里的黑屏状态同步给界面（读档后调用）。</summary>
    public void SyncFromState() => Changed?.Invoke(_engine.State.Blackout, _engine.State.BlackoutReason);
}
