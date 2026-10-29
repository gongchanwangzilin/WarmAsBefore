using WarmAsBefore.Models;
using WarmAsBefore.Modules.RealWorld;

namespace WarmAsBefore.Services;

/// <summary>
/// 身体感受：冷、热、渴、饿、想念。不是让 AI 报数值，而是让它真的「有身体」。
///
/// 数值跟着真实时间缓慢推移，并参考所在地天气；对话、拥抱、吃饭喝水会改变它。
/// 状态存在 <see cref="GameState.Body"/> 里，跟着存档走。
/// 关闭「身体感受」后完全不出现在提示词里，数值也不再变化。
/// </summary>
public sealed class PhysiologyService
{
    private readonly GameEngine _engine;
    private readonly SettingsManager _settings;
    private readonly WeatherProvider _weather;
    private readonly object _gate = new();

    private double _celsius = 22;
    private DateTime _weatherAt = DateTime.MinValue;
    private bool _weatherBusy;

    public PhysiologyService(GameEngine engine, SettingsManager settings, WeatherProvider weather)
    {
        _engine = engine;
        _settings = settings;
        _weather = weather;
    }

    private PhysiologyState Body => _engine.State.Body;

    /// <summary>按真实时间结算一次。每轮对话前、打开手机时都会调，重复调用无害。</summary>
    public void Tick()
    {
        if (!_settings.Current.PhysiologyEnabled) return;
        lock (_gate)
        {
            var b = Body;
            var now = DateTime.UtcNow;
            var hours = Math.Clamp((now - b.UpdatedAt).TotalHours, 0, 24 * 14);
            if (hours < 0.02) return;

            var target = TargetWarmth();
            var pull = Math.Min(1, hours * 0.35);
            b.Warmth = Math.Clamp(b.Warmth + (target - b.Warmth) * pull, 0, 1);
            b.Thirst = Math.Clamp(b.Thirst + 0.030 * hours, 0, 1);
            b.Hunger = Math.Clamp(b.Hunger + 0.022 * hours, 0, 1);
            // 想念涨得比饥渴慢，但夜晚上涨更快一点——人安静下来才更容易想人
            var night = DateTime.Now.Hour is >= 21 or < 6 ? 1.5 : 1.0;
            b.Longing = Math.Clamp(b.Longing + 0.014 * hours * night, 0, 1);
            b.UpdatedAt = now;

            var st = _engine.ActiveCharacter?.State;
            if (st is not null) st.LastSeen = now;
        }
    }

    private double TargetWarmth()
    {
        var c = _celsius;
        var target = c switch
        {
            <= 0 => 0.12,
            <= 6 => 0.22,
            <= 12 => 0.34,
            <= 18 => 0.45,
            <= 24 => 0.52,
            <= 28 => 0.65,
            <= 33 => 0.78,
            _ => 0.9
        };
        return target;
    }

    /// <summary>天气每隔一阵刷新一次，失败就沿用上次的气温。</summary>
    public async Task RefreshWeatherAsync(bool force = false)
    {
        if (!_settings.Current.PhysiologyEnabled) return;
        if (!force && DateTime.UtcNow - _weatherAt < TimeSpan.FromMinutes(30)) return;
        lock (_gate)
        {
            if (_weatherBusy) return;
            _weatherBusy = true;
        }
        try
        {
            var reading = await _weather.Fetch();
            if (reading is not null)
            {
                lock (_gate) { _celsius = reading.Celsius; _weatherAt = DateTime.UtcNow; }
            }
        }
        catch (Exception ex) { App.WriteLog("Physiology.Weather -> " + ex.Message); }
        finally { lock (_gate) _weatherBusy = false; }
    }

    /// <summary>对话后：想念回落，被抱/被关心时体温感受更接近舒适。</summary>
    public void OnTalk(bool intimate = false)
    {
        if (!_settings.Current.PhysiologyEnabled) return;
        lock (_gate)
        {
            var b = Body;
            b.Longing = Math.Clamp(b.Longing - (intimate ? 0.20 : 0.12), 0, 1);
            if (intimate) b.Warmth = Math.Clamp(b.Warmth + 0.04, 0, 1);
            b.UpdatedAt = DateTime.UtcNow;
        }
    }

    public void Eat() => Change(b => b.Hunger = Math.Clamp(b.Hunger - 0.45, 0, 1));
    public void Drink() => Change(b => b.Thirst = Math.Clamp(b.Thirst - 0.55, 0, 1));
    public void CoolDown() => Change(b => b.Warmth = Math.Clamp(b.Warmth - 0.25, 0, 1));
    public void WarmUp() => Change(b => b.Warmth = Math.Clamp(b.Warmth + 0.25, 0, 1));

    private void Change(Action<PhysiologyState> change)
    {
        lock (_gate)
        {
            change(Body);
            Body.UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>给提示词的一段：只给感受与倾向，不给数字。</summary>
    public string PromptText()
    {
        if (!_settings.Current.PhysiologyEnabled) return "";
        Tick();
        string desc;
        lock (_gate) desc = Body.Describe();
        var extra = "";
        lock (_gate)
        {
            var b = Body;
            if (b.Thirst > 0.65) extra = "你会下意识地想去倒水，或者说话时嗓子发干。";
            else if (b.Hunger > 0.65) extra = "你有点心不在焉，胃里空得让人分神。";
            else if (b.Warmth < 0.3) extra = "你会缩一缩肩，想找件外套或靠近暖和的地方。";
            else if (b.Warmth > 0.75) extra = "你会扇一扇风，想喝点凉的。";
            else if (b.Longing > 0.6) extra = "你比平时更想靠近对方，回话会软一点。";
        }
        if (string.IsNullOrEmpty(desc) && string.IsNullOrEmpty(extra)) return "";
        return "【身体感受】" + desc + extra
             + " 用动作和语气自然带出来，不要直接报数值，也不要每句都提。";
    }

    /// <summary>给工具读的结构化状态。</summary>
    public object Snapshot()
    {
        Tick();
        lock (_gate)
        {
            var b = Body;
            return new
            {
                warmth = Math.Round(b.Warmth, 2),
                thirst = Math.Round(b.Thirst, 2),
                hunger = Math.Round(b.Hunger, 2),
                longing = Math.Round(b.Longing, 2),
                feels = b.Describe(),
                celsius = Math.Round(_celsius, 1)
            };
        }
    }
}
