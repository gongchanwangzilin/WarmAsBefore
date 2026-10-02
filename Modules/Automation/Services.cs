using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Automation;

public sealed class TaskOrchestrator
{
    private readonly Modules.AiChat.ChatEngine _ai;
    private readonly Modules.RealWorld.WeatherProvider _weather;
    private CancellationTokenSource? _cts;
    private bool _running;

    public bool Running => _running;
    public bool Enabled { get; set; } = true;
    public event Action<string>? GreetingReady;

    public TaskOrchestrator(Modules.AiChat.ChatEngine ai, Modules.RealWorld.WeatherProvider weather)
    {
        _ai = ai;
        _weather = weather;
    }

    public void Start()
    {
        if (!Enabled || _running) return;
        _running = true;
        _cts = new();
        _ = Loop(_cts.Token);
    }

    public void Stop()
    {
        _running = false;
        _cts?.Cancel();
        _cts = null;
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var time = DateTime.Now.Hour;
                var greeting = time switch
                {
                    < 12 => "早上好~",
                    < 14 => "中午好~",
                    < 18 => "下午好~",
                    _ => "晚上好~"
                };
                GreetingReady?.Invoke(greeting);
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
            }
            catch (TaskCanceledException) { break; }
            catch { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
        }
    }
}