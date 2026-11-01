using WarmAsBefore.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 流程图在主界面这一侧的接线：规则要说一句话就弹 Toast；AI 每说完一轮话给它一次时机。
/// 其余时机（进场景、状态变化、定时、红绿灯……）由 FlowService 自己盯着，VM 不掺和。
/// </summary>
public sealed partial class MainGameViewModel
{
    private FlowService _flows = null!;

    private void HookFlows(FlowService flows)
    {
        _flows = flows;
        _flows.Notify += message => Helpers.Ui.Post(() => Toast.Info(message));
        // 规则表变了（编辑器存在、AI 写了一条）：主界面那排「手动」钮跟着重排
        _flows.RulesChanged += () => Helpers.Ui.Post(RefreshStateButtons);
    }

    /// <summary>AI 说完一句话（这一轮收尾）后给流程图一次机会。</summary>
    private void FireAiCall() => _ = _flows.RunAsync(FlowTriggers.AiCall, "");
}
