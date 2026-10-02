using WarmAsBefore.Models;
using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Automation;

/// <summary>
/// 每日日记（核心机制，不支持关闭）：每天自动把当日对话与好感时刻汇总成一篇日记。
/// 与回忆录的区别：回忆录 = 全部好感时刻 + 全部对话（可搜索）；日记 = 每日自动生成的总结。
/// 启动时立即补写当天日记，之后每分钟检查跨天（避免长时间挂机漏写）。
/// </summary>
public sealed class DailyDiaryWriter
{
    private readonly MemoryVault _memory;
    private readonly GameEngine _engine;
    private CancellationTokenSource? _cts;
    private DateTime _lastDate = DateTime.MinValue;

    public DailyDiaryWriter(MemoryVault memory, GameEngine engine)
    {
        _memory = memory;
        _engine = engine;
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _ = EnsureTodayAsync();            // 启动立即补写
        _ = WatchLoopAsync(_cts.Token);    // 跨天监听
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var today = DateTime.Now.Date;
                if (today != _lastDate)
                {
                    _lastDate = today;
                    await EnsureTodayAsync();
                }
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
            }
            catch (TaskCanceledException) { break; }
            catch (Exception ex) { App.WriteLog("DailyDiaryWriter.Loop -> " + ex); }
        }
    }

    /// <summary>补写当天日记：当天还没有日记且今天有对话/好感时，生成一篇总结。</summary>
    public async Task EnsureTodayAsync()
    {
        try
        {
            var charId = ResolveCharacter();
            var today = DateTime.Now.Date;
            var diary = await _memory.Diary(charId);
            var localDates = diary.Select(d => d.Date.ToLocalTime().Date).ToList();
            if (localDates.Contains(today)) return;

            var dayStart = today.ToUniversalTime();
            var dayEnd = today.AddDays(1).ToUniversalTime();
            var all = await _memory.All(charId);
            var dayEvents = all.Where(m => m.At >= dayStart && m.At < dayEnd).ToList();
            if (dayEvents.Count == 0) return;

            var dialogues = dayEvents.Where(m => m.Category == "dialogue").ToList();
            var affections = dayEvents.Where(m => m.Category == "affection").ToList();
            var summary = Compose(today, dialogues, affections);
            if (string.IsNullOrWhiteSpace(summary)) return;

            await _memory.WriteDiary(charId, summary, MoodOf(affections));
            App.WriteLog($"DailyDiaryWriter: {today:yyyy-MM-dd} 已生成日记（对话 {dialogues.Count}，好感 {affections.Count}）");
        }
        catch (Exception ex)
        {
            App.WriteLog("DailyDiaryWriter.EnsureTodayAsync -> " + ex);
        }
    }

    private static string Compose(DateTime day, List<MemoryEntry> dialogues, List<MemoryEntry> affections)
    {
        var lines = new List<string> { $"{day:M月d日} 的陪伴总结：" };
        if (dialogues.Count > 0)
            lines.Add($"· 今天和你聊了 {dialogues.Count} 次");
        if (affections.Count > 0)
        {
            var net = affections.Sum(a => a.Weight);
            var reasons = string.Join("、", affections.Select(a => a.Keywords).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct());
            lines.Add($"· 好感变化 {net:+0;-0;0}（{reasons}）");
        }
        var excerpts = dialogues.Take(3).Select(m =>
        {
            var t = m.Content;
            return t.Length > 40 ? t[..40] + "…" : t;
        });
        lines.Add("· 今日片段：" + string.Join("；", excerpts));
        return string.Join("\n", lines);
    }

    private static string MoodOf(List<MemoryEntry> affections)
    {
        var net = affections.Sum(a => a.Weight);
        return net switch
        {
            >= 5 => "温暖",
            > 0 => "愉快",
            < 0 => "低落",
            _ => "平静"
        };
    }

    private string ResolveCharacter()
    {
        var id = _engine.State.CharacterId;
        if (!string.IsNullOrEmpty(id)) return id;
        if (_engine.Roster.Count > 0) return _engine.Roster.Values.First().Profile.Id;
        return "小雨";
    }
}
