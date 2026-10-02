using System.Text.Json;
using WarmAsBefore.Modules.ApiManager;
using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.GameModule;

/// <summary>
/// 小游戏技能追踪（熟练度 + 认真度，驱动 LLM 难度决策）：
/// - 熟练度：按棋类累加（胜负都加，赢了加更多），长时间不玩会随时间衰减（每天 -1，下限 0）。
/// - 认真度：由大语言模型设定的 1..10 整数，表示小雨这场对局有多认真，持久化保存。
/// - 难度：由 LLM 根据「世界书（角色人设）+ 记忆（最近对话）+ 表现（熟练度/战绩）」灵活决定；
///   不同棋类难度可以不同，对局中可被 LLM 重新评判即时换难度；
///   云端不可用时回退本地经验规则（按熟练度+认真度映射难度）。全部 8 秒硬超时，绝不阻塞棋局。
/// </summary>
public sealed class GameSkillTracker
{
    private const string GameSkillKey = "game_skill";
    /// <summary>熟练度衰减：超过这么多天没玩，每多一天 -1。</summary>
    private const int DecayAfterDays = 3;

    private sealed class SkillState
    {
        public Dictionary<string, int> Proficiency { get; set; } = new();          // 棋类名 -> 熟练度 0..99
        public Dictionary<string, DateTime> LastPlayed { get; set; } = new();       // 棋类名 -> 最近游玩时间
        public Dictionary<string, string> DifficultyOverride { get; set; } = new(); // 棋类名 -> easy/normal/hard（LLM 最近一次判定）
        public int Seriousness { get; set; } = 5;                                   // 1..10，LLM 设定
    }

    private readonly StorageProvider _store;
    private readonly SettingsManager _settings;
    private readonly ApiGateway _api;
    private readonly GameEngine _engine;
    private readonly MemoryVault _memory;
    private SkillState _state = new();
    private bool _loaded;

    public GameSkillTracker(StorageProvider store, SettingsManager settings, ApiGateway api,
        GameEngine engine, MemoryVault memory)
    {
        _store = store;
        _settings = settings;
        _api = api;
        _engine = engine;
        _memory = memory;
    }

    public async Task InitializeAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            _state = await _store.Load<SkillState>(GameSkillKey) ?? new SkillState();
        }
        catch (Exception ex)
        {
            App.WriteLog($"GameSkillTracker.Initialize -> {ex.Message}");
        }
    }

    /// <summary>某棋类的熟练度（读取时自动应用「长时间不用会掉」的时间衰减）。</summary>
    public async Task<int> ProficiencyOfAsync(string game)
    {
        await InitializeAsync();
        if (!_state.LastPlayed.TryGetValue(game, out var last)) return 0;
        var idle = (DateTime.Now - last).TotalDays;
        var decay = idle <= DecayAfterDays ? 0 : (int)(idle - DecayAfterDays);
        if (decay <= 0) return _state.Proficiency.TryGetValue(game, out var p) ? p : 0;
        var cur = Math.Max(0, (_state.Proficiency.TryGetValue(game, out var v) ? v : 0) - decay);
        _state.Proficiency[game] = cur;
        await SaveAsync();
        return cur;
    }

    /// <summary>当前认真度（LLM 设定值 1..10，默认 5）。</summary>
    public async Task<int> SeriousnessAsync()
    {
        await InitializeAsync();
        return Math.Clamp(_state.Seriousness, 1, 10);
    }

    /// <summary>玩完一局：熟练度 +2（胜）/ +1（负），刷新最近游玩时间并落盘。</summary>
    public async Task RecordGameAsync(string game, bool won)
    {
        try
        {
            await InitializeAsync();
            var cur = _state.Proficiency.TryGetValue(game, out var v) ? v : 0;
            _state.Proficiency[game] = Math.Min(99, cur + (won ? 2 : 1));
            _state.LastPlayed[game] = DateTime.Now;
            await SaveAsync();
        }
        catch (Exception ex)
        {
            App.WriteLog($"GameSkillTracker.RecordGame({game}) -> {ex.Message}");
        }
    }

    /// <summary>AI 自动难度：以世界书+记忆+熟练度（含衰减）为输入问 LLM 判定难度与认真度；
    /// 云端不可用/超时回退本地经验规则。8 秒超时，绝不阻塞棋盘交互。</summary>
    public async Task<MiniGameEngine.AiDifficulty> DecideDifficultyAsync(string game, int moveCount)
    {
        await InitializeAsync();
        var prof = await ProficiencyOfAsync(game);
        var ser = Math.Clamp(_state.Seriousness, 1, 10);

        // 本地经验规则兜底：熟练度越高越难，认真度作额外杠杆（<12 放水，>40 全力以赴）
        MiniGameEngine.AiDifficulty Local() => (prof + (ser >= 8 ? 4 : 0)) switch
        {
            < 12 => MiniGameEngine.AiDifficulty.Easy,
            > 40 => MiniGameEngine.AiDifficulty.Hard,
            _ => MiniGameEngine.AiDifficulty.Normal
        };

        var persona = BuildPersona();
        var memory = await BuildMemoryAsync();
        try
        {
            var reply = await AskLlmAsync(game, persona, memory, prof, ser, moveCount);
            var parsed = ParseReply(reply);
            if (parsed is null) return Local();

            var (diff, serious) = parsed.Value;
            _state.DifficultyOverride[game] = diff;
            _state.Seriousness = Math.Clamp(serious, 1, 10);
            await SaveAsync();
            return diff switch
            {
                "easy" => MiniGameEngine.AiDifficulty.Easy,
                "hard" => MiniGameEngine.AiDifficulty.Hard,
                _ => MiniGameEngine.AiDifficulty.Normal
            };
        }
        catch (Exception ex)
        {
            App.WriteLog($"GameSkillTracker.DecideDifficulty({game}) -> {ex.Message}");
            return Local();
        }
    }

    /// <summary>世界书（角色人设）：优先当前主角，否则取库中第一个角色。</summary>
    private string BuildPersona()
    {
        try
        {
            var ch = _engine.ActiveCharacter;
            if (ch is null && _engine.Roster.Count > 0)
                ch = _engine.Roster.Values.First();
            if (ch is null) return "";
            var p = ch.Profile;
            var bits = new List<string>();
            if (!string.IsNullOrWhiteSpace(p.Personality)) bits.Add($"性格：{p.Personality}");
            if (!string.IsNullOrWhiteSpace(p.Description)) bits.Add($"背景：{p.Description}");
            if (!string.IsNullOrWhiteSpace(p.UserAddress)) bits.Add($"称呼玩家：{p.UserAddress}");
            return string.Join("；", bits);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>记忆：当前主角最近 6 条对话。</summary>
    private async Task<string> BuildMemoryAsync()
    {
        try
        {
            var entries = await _memory.Recent(_engine.State.CharacterId, 6);
            return string.Join("；", entries.TakeLast(6).Select(e => e.Content));
        }
        catch
        {
            return "";
        }
    }

    /// <summary>LLM 判定：输入世界书 + 记忆 + 表现，输出 {difficulty, seriousness} JSON。</summary>
    private async Task<string?> AskLlmAsync(string game, string persona, string memory,
        int proficiency, int seriousness, int moveCount)
    {
        var s = _settings.Current;
        if (string.IsNullOrWhiteSpace(s.AiUrl) || string.IsNullOrWhiteSpace(s.AiKey)) return null;
        var system = "你是棋类对战小游戏的难度调控器。根据小雨的人设、记忆与玩家表现，决定小雨下这局棋的难度和认真度。只输出 JSON。";
        var user = $"游戏：{game}\n" +
                   $"小雨人设（世界书）：{(string.IsNullOrWhiteSpace(persona) ? "无" : persona)}\n" +
                   $"最近记忆：{(string.IsNullOrWhiteSpace(memory) ? "无" : memory)}\n" +
                   $"玩家熟练度：{proficiency}（0-99；低于 12 请放水 easy，长期不玩会衰减）\n" +
                   $"当前认真度：{seriousness}（1-10）\n" +
                   $"本局已走 {moveCount} 步。\n" +
                   "输出格式：{\"difficulty\":\"easy|normal|hard\",\"seriousness\":1-10的整数}，不要任何其他文字";

        var body = new
        {
            model = string.IsNullOrWhiteSpace(s.AiModel) ? "gpt-4o" : s.AiModel,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            },
            temperature = 0.2,
            max_tokens = 60
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        try
        {
            var task = _api.ChatRaw(body);
            if (task is null) return null;
            return await task.WaitAsync(cts.Token);
        }
        catch { return null; }
    }

    private (string Difficulty, int Seriousness)? ParseReply(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return null;
        try
        {
            var cleaned = reply.Trim();
            if (cleaned.StartsWith("```")) cleaned = cleaned[3..];
            if (cleaned.EndsWith("```")) cleaned = cleaned[..^3];
            cleaned = cleaned.Trim();
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;
            var diff = (root.TryGetProperty("difficulty", out var d) ? d.GetString() : null)?.Trim().ToLowerInvariant();
            var ser = root.TryGetProperty("seriousness", out var s) ? s.GetInt32() : 5;
            if (diff is not ("easy" or "normal" or "hard")) return null;
            return (diff, ser);
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveAsync()
    {
        try { await _store.Save(GameSkillKey, _state); }
        catch (Exception ex) { App.WriteLog($"GameSkillTracker.Save -> {ex.Message}"); }
    }
}