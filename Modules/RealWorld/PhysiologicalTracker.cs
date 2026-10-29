using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Modules.RealWorld;


public sealed class PhysiologicalTracker
{
    private int _day;
    private bool _tracked = true;
    private int _cycleLen = 28;
    private int _periodLen = 5;

    /// <summary>按当前角色的性别下发规则：没有周期的性别直接把 Tracked 关掉，
    /// 下面三个读数（Phase / InPeriod / Label）全部退回「无」，谁拿它去拼界面或提示词都不会冒出经期。</summary>
    public void SetCycle(CycleRule rule)
    {
        _tracked = rule.Enabled;
        _cycleLen = Math.Clamp(rule.CycleLength, 7, 60);
        _periodLen = Math.Clamp(rule.PeriodLength, 1, _cycleLen);
    }

    public bool Tracked => _tracked;
    public int Day { get => _day; set => _day = value % _cycleLen; }
    public string Phase => !_tracked ? "normal" : _day switch
    {
        _ when _day < _periodLen => "period",
        _ when _day < 14 => "follicular",
        _ when _day < 16 => "ovulation",
        _ => "luteal"
    };
    public bool InPeriod => _tracked && _day < _periodLen;
    public string Label => !_tracked ? "" : Phase switch
    {
        "period" => "经期",
        "follicular" => "卵泡期",
        "ovulation" => "排卵期",
        "luteal" => "黄体期",
        _ => "正常"
    };
}
