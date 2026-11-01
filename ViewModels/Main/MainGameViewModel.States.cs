using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 主界面那排「自定义状态」按钮：由状态定义自己长出来，值一变就重排，点一下走到下一个值。
/// 没人手动往这排里塞东西——所以换场景、换楼层时它自动换成那一处该看的状态。
/// </summary>
public sealed partial class MainGameViewModel
{
    private StateService _states = null!;

    public ObservableCollection<StateChipVm> StateButtons { get; } = new();

    public bool HasStateButtons => StateButtons.Count > 0;

    /// <summary>接上状态服务：值变（按钮点、AI 改、流程图改）都会把这一排重排一次。</summary>
    private void HookStates(StateService states)
    {
        _states = states;
        _states.ValueChanged += _ => Helpers.Ui.Post(RefreshStateButtons);
        RefreshStateButtons();
    }

    /// <summary>按当前场景/楼层重排；没开按钮的状态不出现在界面上，但流程图照样能用。</summary>
    public void RefreshStateButtons()
    {
        StateButtons.Clear();
        foreach (var def in _states.Visible())
        {
            if (!def.ShowButton) continue;
            StateButtons.Add(new StateChipVm { Id = def.Id, Key = def.Key, Label = $"{def.Key}：{def.Value}" });
        }
        // 「手动」触发的规则也长一颗钮：点了才跑，不跟变量挂钩
        foreach (var rule in _flows.Map.Flows)
        {
            if (!rule.Enabled || !rule.IsValid || rule.Trigger != FlowTriggers.Manual) continue;
            StateButtons.Add(new StateChipVm { Id = rule.Id, Key = rule.Name, Label = "▶ " + rule.Name, IsRule = true });
        }
        OnPropertyChanged(nameof(HasStateButtons));
    }

    [RelayCommand]
    private async Task ToggleState(StateChipVm? chip)
    {
        if (chip is null) return;
        if (chip.IsRule)
        {
            var ran = await _flows.RunRuleAsync(chip.Key);
            Toast.Info(ran ? $"已跑「{chip.Key}」" : $"「{chip.Key}」没跑成：条件不成立或触发太密。");
            return;
        }
        var value = await _states.ToggleAsync(chip.Key);
        if (value.Length == 0) return;
        Toast.Info($"{chip.Key} → {value}");
    }
}

/// <summary>一颗状态按钮：名字不动，标签跟着值变；或者是「手动」规则的一颗起跑钮。</summary>
public sealed class StateChipVm
{
    public string Id { get; set; } = "";
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public bool IsRule { get; set; }
}
