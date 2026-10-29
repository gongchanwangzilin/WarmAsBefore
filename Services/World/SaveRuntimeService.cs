using System.Text.RegularExpressions;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Modules.RealChat;

namespace WarmAsBefore.Services;

/// <summary>
/// 存档运行时：存档一打开，所有内部角色都在后台继续活着。
///
/// 每个角色按自己的时钟推演在做什么（本地、免费），偶尔用自己的模型写一条生活记忆，
/// 会自己下棋/玩小游戏，也会（在用户打开「主动发消息」后）通过 QQ/微信找你。
///
/// 调度只有一条工作线程：AI 调用永远串行，所有角色共用每小时预算，按角色错峰冷却；
/// 深夜只做本地推演。任何错误只记日志——后台绝不该拖垮前台。
/// </summary>
public sealed class SaveRuntimeService
{
    private readonly SettingsManager _settings;
    private readonly GameEngine _engine;
    private readonly ChatEngine _chat;
    private readonly MemoryVault _memory;
    private readonly OfficialChatBridge _bridge;
    private readonly WeChatService _wechat;

    private readonly object _gate = new();
    private readonly List<DateTime> _aiLog = new();
    private readonly Dictionary<string, DateTime> _nextAi = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _nextGame = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _nextMessage = new(StringComparer.Ordinal);

    private CancellationTokenSource? _cts;
    private bool _running;
    private string _saveId = "";

    /// <summary>一次最多推演多少个角色：预算与线程都有限，超出部分按名册顺序截断。</summary>
    private const int MaxCast = 12;

    private static readonly Regex MachineMarkers = new(@"【[^】]*】", RegexOptions.Compiled);
    private static readonly Regex PetDirective = new(@"\{\{\s*pet\s*:[^}]*\}\}", RegexOptions.Compiled);

    private static readonly string[] Games = { "象棋", "五子棋", "围棋", "跳棋" };

    public SaveRuntimeService(SettingsManager settings, GameEngine engine, ChatEngine chat,
        MemoryVault memory, OfficialChatBridge bridge, WeChatService wechat)
    {
        _settings = settings;
        _engine = engine;
        _chat = chat;
        _memory = memory;
        _bridge = bridge;
        _wechat = wechat;
    }

    public bool Running => _running;

    /// <summary>在场状态变化（后台线程触发，订阅方自己回主线程）。</summary>
    public event Action? Changed;

    /// <summary>某个角色此刻在做什么（设置页展示用）。</summary>
    public sealed record PresenceRow(string CharacterId, string Name, string Activity, string Place, DateTime At);

    /// <summary>当前存档里所有正在运行的角色（按名字排序）。</summary>
    public IReadOnlyList<PresenceRow> Snapshot()
    {
        try
        {
            lock (_gate)
            {
                return _engine.State.Presence
                    .Select(kv =>
                    {
                        var name = _engine.Roster.TryGetValue(kv.Key, out var c) && !string.IsNullOrWhiteSpace(c.Profile.Name)
                            ? c.Profile.Name
                            : kv.Key;
                        return new PresenceRow(kv.Key, name, kv.Value.Activity, kv.Value.Place, kv.Value.UpdatedAt.ToLocalTime());
                    })
                    .OrderBy(r => r.Name, StringComparer.CurrentCulture)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveRuntime.Snapshot -> " + ex.Message);
            return Array.Empty<PresenceRow>();
        }
    }

    /// <summary>存档打开后调用（MainGameViewModel.Begin）。重复调用无效果。</summary>
    public void Start()
    {
        if (_running) return;
        if (!_settings.Current.SaveRuntimeEnabled) return;
        _running = true;
        _saveId = _engine.CurrentSaveId;
        _cts = new CancellationTokenSource();
        _ = LoopAsync(_cts.Token);
        // 先把这一档的角色登记进 Presence：之后 tick 只改字段不改表结构，
        // 免得自动存档恰好序列化到一半时遇到「集合已修改」。
        _ = WarmCastAsync();
        App.WriteLog("SaveRuntime: 已启动（存档 " + (_saveId.Length == 0 ? "未命名" : _saveId) + "）");
    }

    private async Task WarmCastAsync()
    {
        try
        {
            var cast = await CastAsync();
            lock (_gate)
                foreach (var ch in cast)
                    _engine.State.Presence.TryAdd(ch.Profile.Id, new CharacterPresence());
        }
        catch (Exception ex) { App.WriteLog("SaveRuntime.Warm -> " + ex.Message); }
    }

    /// <summary>回标题/换档前调用：先停再让存档落盘，角色们的最后状态才写得进去。</summary>
    public void Stop()
    {
        if (!_running) return;
        _running = false;
        try { _cts?.Cancel(); } catch { }
        _cts = null;
        App.WriteLog("SaveRuntime: 已停止");
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        // 进场过场与首屏加载优先：后台晚十几秒再开始，不和它们抢线程。
        try { await Task.Delay(TimeSpan.FromSeconds(12), ct); }
        catch (OperationCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { await TickAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { App.WriteLog("SaveRuntime.Tick -> " + ex); }

            try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (!_running) return;
        var s = _settings.Current;
        if (!s.SaveRuntimeEnabled) { Stop(); return; }
        // 换档/回标题保护：新档由它自己的 Begin 重新启动。
        if (!string.IsNullOrEmpty(_saveId) && !string.Equals(_saveId, _engine.CurrentSaveId, StringComparison.Ordinal))
        {
            Stop();
            return;
        }

        var cast = await CastAsync();
        if (cast.Count == 0) return;

        var now = DateTime.Now;
        var quiet = s.SaveRuntimeQuietHours && (now.Hour >= 23 || now.Hour < 7);
        var budget = Math.Max(1, s.SaveRuntimeHourlyBudget);
        var aiMinutes = Math.Clamp(s.SaveRuntimeAiMinutes, 10, 120);
        var changed = false;

        foreach (var ch in cast)
        {
            if (ct.IsCancellationRequested || !_running) return;
            var id = ch.Profile.Id;

            CharacterPresence p;
            lock (_gate)
            {
                if (!_engine.State.Presence.TryGetValue(id, out var found))
                {
                    found = new CharacterPresence();
                    _engine.State.Presence[id] = found;
                }
                p = found;
            }

            // 1) 本地推演：按时间换活动，不花钱、永远可跑。
            if (RollActivity(ch, p, now)) changed = true;

            // 2) 自己下棋/玩小游戏：本地结算，不经过模型。
            if (s.SaveRuntimePlayGames && !quiet && Due(_nextGame, id, now, 40, 100))
            {
                await PlayGameAsync(ch, p, now);
                changed = true;
            }

            // 3) AI 生活回合：用自己的模型写一条生活记忆（串行 + 小时预算 + 按角色冷却）。
            if (s.SaveRuntimeAiEnabled && !quiet && _chat.HasEndpoint
                && Due(_nextAi, id, now, aiMinutes, aiMinutes * 2)
                && TakeBudget(budget))
            {
                if (await LifeTurnAsync(ch, p, now)) changed = true;
            }

            // 4) 主动消息：只由通道绑定的角色发（QQ/微信各一个身份，不能冒名）。
            if (s.SaveRuntimeProactive && !quiet && IsOutboundCharacter(id)
                && Due(_nextMessage, id, now, 120, 240))
            {
                if (await ProactiveAsync(ch, p, now)) changed = true;
            }
        }

        if (changed) Changed?.Invoke();
    }

    /// <summary>
    /// 这个存档的「内部角色」：主角 + 存档白名单 + 已经跑起来的角色 + 微信常驻好友。
    /// 不用整本角色库——那是所有存档共用的一份名册，不该每个档都全员开机。
    /// </summary>
    private async Task<List<CharacterData>> CastAsync()
    {
        var list = new List<CharacterData>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(CharacterData? c)
        {
            if (c is null || string.IsNullOrEmpty(c.Profile.Id)) return;
            if (list.Count >= MaxCast) return;
            if (seen.Add(c.Profile.Id)) list.Add(c);
        }

        Add(_engine.ActiveCharacter);
        if (_engine.State.AllowedCharacters.Count > 0)
            foreach (var id in _engine.State.AllowedCharacters)
                if (_engine.Roster.TryGetValue(id, out var c)) Add(c);
        // 本存档里被点名的配角（性欲系统那批）也是「这一档的人」，一起跑。
        foreach (var id in _engine.State.LibidoCharacters)
            if (_engine.Roster.TryGetValue(id, out var c)) Add(c);
        List<string> started;
        lock (_gate) started = _engine.State.Presence.Keys.ToList();
        foreach (var id in started)
            if (_engine.Roster.TryGetValue(id, out var c)) Add(c);

        try
        {
            foreach (var contact in await _wechat.ListAsync())
                if (contact.IsResident && _engine.Roster.TryGetValue(contact.CharacterId, out var c)) Add(c);
        }
        catch (Exception ex) { App.WriteLog("SaveRuntime.Cast(wechat) -> " + ex.Message); }

        return list;
    }

    /// <summary>按角色错峰：第一次抽一个未来时间，到点后再抽下一次。返回 true 表示这次轮到它。</summary>
    private static bool Due(Dictionary<string, DateTime> table, string id, DateTime now, int minMinutes, int maxMinutes)
    {
        if (!table.TryGetValue(id, out var next))
        {
            // 第一次不等满一个周期：打开存档几分钟内就该看见角色们真的在动。
            var lo = Math.Min(minMinutes, 4);
            var hi = Math.Min(maxMinutes, 12);
            table[id] = now.AddMinutes(Random.Shared.Next(lo, Math.Max(lo + 1, hi)));
            return false;
        }
        if (now < next) return false;
        table[id] = now.AddMinutes(Random.Shared.Next(minMinutes, Math.Max(minMinutes + 1, maxMinutes)));
        return true;
    }

    /// <summary>每小时总预算（所有角色合计）：超了就这一小时不再问模型。</summary>
    private bool TakeBudget(int perHour)
    {
        lock (_gate)
        {
            _aiLog.RemoveAll(t => t < DateTime.Now.AddHours(-1));
            if (_aiLog.Count >= perHour) return false;
            _aiLog.Add(DateTime.Now);
            return true;
        }
    }

    /// <summary>换活动（20~40 分钟一次）。返回是否有变化。</summary>
    private bool RollActivity(CharacterData ch, CharacterPresence p, DateTime now)
    {
        if (!string.IsNullOrWhiteSpace(p.Activity) && now - p.UpdatedAt.ToLocalTime() < TimeSpan.FromMinutes(20)) return false;
        var (activity, place) = PickActivity(ch, now);
        p.Activity = activity;
        p.Place = place;
        p.UpdatedAt = DateTime.UtcNow;
        return true;
    }

    private static (string Activity, string Place) PickActivity(CharacterData ch, DateTime now)
    {
        var h = now.Hour;
        string[] pool = h switch
        {
            >= 0 and < 6 => new[] { "睡着了", "睡得很沉", "翻了个身又睡过去" },
            < 9 => new[] { "刚醒，赖在床上", "在洗漱", "在弄早饭", "在拉开窗帘" },
            < 12 => new[] { "在看书", "在整理房间", "在写点东西", "在听歌", "在窗边发呆" },
            < 14 => new[] { "在吃午饭", "在收拾厨房", "在阳台晒一会儿太阳" },
            < 18 => new[] { "在忙自己的事", "在画点什么", "在研究新东西", "在翻手机" },
            < 20 => new[] { "在做晚饭", "在吃饭", "在附近散步" },
            < 23 => new[] { "在放松", "在看剧", "在棋盘前琢磨", "在记今天的事" },
            _ => new[] { "准备睡了", "在洗漱", "靠在床头看手机" }
        };
        // 同一个角色在同一时刻固定落在同一项：看起来像习惯，而不是每次刷新都换个人。
        var i = (int)((uint)StringComparer.Ordinal.GetHashCode(ch.Profile.Id + h) % (uint)pool.Length);
        return (pool[i], "家");
    }

    private async Task<bool> LifeTurnAsync(CharacterData ch, CharacterPresence p, DateTime now)
    {
        try
        {
            var prompt = $"现在是 {now:HH:mm}，你在{p.Place}，{p.Activity}。" +
                         "写下此刻的一小段生活：你在做什么、看到或想到了什么，四十个字以内。" +
                         "不要对任何人说话，不要称呼用户，不要写旁白说明。";
            var reply = await _chat.SendAmbient(ch.Profile.Id, prompt);
            if (string.IsNullOrWhiteSpace(reply)) return false;
            var clean = Clean(reply);
            if (clean.Length == 0) return false;
            if (clean.Length > 80) clean = clean[..80];
            p.Activity = clean;
            p.UpdatedAt = DateTime.UtcNow;
            p.LastAiAt = DateTime.UtcNow;
            p.AiRounds++;
            await _memory.StoreAmbient(ch.Profile.Id, clean, "life");
            App.WriteLog($"SaveRuntime.Life({ch.Profile.Name}) -> {clean}");
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveRuntime.LifeTurn -> " + ex);
            return false;
        }
    }

    private async Task PlayGameAsync(CharacterData ch, CharacterPresence p, DateTime now)
    {
        try
        {
            var game = Games[(int)((uint)StringComparer.Ordinal.GetHashCode(ch.Profile.Id + now.Date.ToString("yyyyMMdd"))
                                   % (uint)Games.Length)];
            var won = Random.Shared.Next(100) < 45;
            p.Games++;
            p.LastGameAt = DateTime.UtcNow;
            p.Activity = $"刚自己下完一局{game}，{(won ? "赢了" : "输了")}";
            p.UpdatedAt = DateTime.UtcNow;
            await _memory.StoreAmbient(ch.Profile.Id, $"自己下了一局{game}，{(won ? "赢了" : "输了")}", "game");
        }
        catch (Exception ex) { App.WriteLog("SaveRuntime.PlayGame -> " + ex.Message); }
    }

    /// <summary>能主动发消息的角色：QQ/微信绑定的角色或当前主角。一个通道只有一个身份，别人不能冒名。</summary>
    private bool IsOutboundCharacter(string id)
    {
        var s = _settings.Current;
        return string.Equals(id, s.QqBoundCharacterId, StringComparison.Ordinal)
               || string.Equals(id, s.WechatBoundCharacterId, StringComparison.Ordinal)
               || string.Equals(id, _engine.State.CharacterId, StringComparison.Ordinal);
    }

    private async Task<bool> ProactiveAsync(CharacterData ch, CharacterPresence p, DateTime now)
    {
        try
        {
            if (!_bridge.CanReachUser(ch.Profile.Id)) return false;
            var reply = await _chat.SendAmbient(ch.Profile.Id,
                $"现在是 {now:HH:mm}，你{p.Activity}，忽然想给手机那头的人发条消息。" +
                "写一条不超过三十个字的短消息，直接是正文，不要引号，不要解释。");
            if (string.IsNullOrWhiteSpace(reply)) return false;
            var text = Clean(reply);
            if (text.Length == 0) return false;
            if (text.Length > 60) text = text[..60];
            var result = _bridge.QueueOutbound(text, ch.Profile.Id);
            p.LastMessageAt = DateTime.UtcNow;
            p.UpdatedAt = DateTime.UtcNow;
            App.WriteLog($"SaveRuntime.Proactive({ch.Profile.Name}) -> {result}：{text}");
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveRuntime.Proactive -> " + ex);
            return false;
        }
    }

    /// <summary>去掉动作/状态标记与桌宠指令：生活记忆里只留人话。</summary>
    private static string Clean(string text)
    {
        var s = PetDirective.Replace(text, "");
        s = MachineMarkers.Replace(s, "");
        return s.Trim().Trim('"', '“', '”', '\'');
    }
}
