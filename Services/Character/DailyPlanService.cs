using System.Globalization;
using System.Reflection;
using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.ApiManager;
using WarmAsBefore.Modules.Tools;

namespace WarmAsBefore.Services;

/// <summary>
/// 每角色独立的 AI 日程服务（省 token 核心）。
///
/// 设计要点（务必保持）：
/// 1. 一天开始时「一次性」让 AI 把整天要做的事写成结构化 JSON 日程，之后按表执行；
///    不再每轮实时生成——这是省 token 的关键，聊天轮次再多也不会重复烧日程 token。
/// 2. 每天每角色最多生成一次，成功后写盘；跨天自动重生成。没配 API / 解析失败一律静默
///    用本地模板兜底，用户永远不会看到空白日程，也不会弹出 API 错误。
/// 3. Tick(charId) 只把「到点」的条目标记 Done 并写进 presence/落盘，绝不驱动真实聊天、
///    不打断用户；用户不在时角色世界照常推进。
/// 4. 全程 try/catch：这是监测页的展示性/装饰性功能，失败绝不拖垮主流程。
/// </summary>
public sealed class DailyPlanService
{
    private const string DateFormat = "yyyy-MM-dd";

    private static DailyPlanService? _instance;
    private static readonly object InstanceGate = new();

    /// <summary>静态单例。监测页与后台 Tick 共用同一份内存缓存，避免重复生成。</summary>
    public static DailyPlanService Instance
    {
        get
        {
            if (_instance is null)
            {
                lock (InstanceGate)
                    _instance ??= new DailyPlanService();
            }
            return _instance;
        }
    }

    private readonly DailyPlanStore _store = new();
    private readonly object _gate = new();
    private readonly PlanBook _book = new();
    private readonly HashSet<string> _loaded = new(StringComparer.Ordinal);
    private readonly HashSet<string> _generating = new(StringComparer.Ordinal);
    private string _scopeToken = "";

    private DailyPlanService() { }

    /// <summary>日程被生成/勾选/推进后触发（参数 = charId），监测页据此刷新时间线。</summary>
    public event Action<string>? Changed;

    private static string DateKeyOf(DateTime dt) => dt.ToString(DateFormat, CultureInfo.InvariantCulture);
    private static string TodayKey => DateKeyOf(DateTime.Now);

    // ==================== 缓存 / 落盘 ====================

    private void DropCacheIfScopeChanged()
    {
        try
        {
            var token = _store.ScopeToken;
            lock (_gate)
            {
                if (_scopeToken == token) return;
                _scopeToken = token;
                _book.ByCharacter.Clear();
                _loaded.Clear();
            }
        }
        catch (Exception ex) { App.WriteLog("DailyPlan.Scope -> " + ex.Message); }
    }

    private async Task EnsureLoadedAsync(string charId)
    {
        lock (_gate)
            if (_loaded.Contains(charId)) return;
        await LoadAsync(charId);
    }

    private async Task LoadAsync(string charId)
    {
        try
        {
            var byDate = await _store.LoadAsync(charId);
            lock (_gate)
            {
                _book.ByCharacter[charId] = byDate;
                _loaded.Add(charId);
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("DailyPlan.Load -> " + ex.Message);
            lock (_gate) _loaded.Add(charId);   // 读失败也标记，避免每帧重试刷屏
        }
    }

    /// <summary>取某角色的日期字典；没有就建一个（调用方需持 _gate）。</summary>
    private Dictionary<string, DailyPlan> ByDateLocked(string charId)
    {
        if (!_book.ByCharacter.TryGetValue(charId, out var byDate))
        {
            byDate = new Dictionary<string, DailyPlan>(StringComparer.Ordinal);
            _book.ByCharacter[charId] = byDate;
        }
        return byDate;
    }

    private void Persist(string charId)
    {
        Dictionary<string, DailyPlan> snapshot;
        lock (_gate)
        {
            if (!_loaded.Contains(charId)) return;
            snapshot = new Dictionary<string, DailyPlan>(ByDateLocked(charId), StringComparer.Ordinal);
        }
        _ = Task.Run(async () =>
        {
            App.TraceStep($"DailyPlan.Persist begin char={charId} path={_store.PathFor(charId)}");
            await _store.SaveAsync(charId, snapshot);
            App.TraceStep("DailyPlan.Persist end");
        });
    }

    private void RaiseChanged(string charId)
    {
        try { Changed?.Invoke(charId); }
        catch (Exception ex) { App.WriteLog("DailyPlan.Changed -> " + ex.Message); }
    }

    // ==================== 对外 API ====================

    /// <summary>
    /// 确保 charId 今天有日程：当天无日程才生成（默认true）。已存在（含昨天的旧日程）
    /// 直接返回；跨天则由调用方拿到旧日期后重新走一次生成。
    /// 返回 null 只可能发生在 generateIfMissing=false 且当天还没有日程时。
    /// </summary>
    public async Task<DailyPlan?> EnsureTodayAsync(string charId, bool generateIfMissing = true)
    {
        if (string.IsNullOrWhiteSpace(charId)) return null;
        try
        {
            DropCacheIfScopeChanged();
            await EnsureLoadedAsync(charId);

            var today = TodayKey;
            lock (_gate)
                if (ByDateLocked(charId).TryGetValue(today, out var exists) && exists is not null)
                    return exists;

            // 跨天清理：只保留今天，昨天的不再占内存（盘上仍留档，Snapshot 需要时可再读）
            lock (_gate)
            {
                var byDate = ByDateLocked(charId);
                if (byDate.Keys.Count > 1)
                    foreach (var old in byDate.Keys.Where(k => k != today).ToList())
                        byDate.Remove(old);
            }

            if (!generateIfMissing) return null;

            lock (_gate)
            {
                if (!_generating.Add(charId)) return null;   // 并发重复进入：让先到的那次负责生成
            }

            try
            {
                App.TraceStep($"DailyPlan.Ensure begin char={charId} date={today}");
                var plan = await TryGenerateWithAiAsync(charId, today) ?? BuildTemplate(charId, today);

                lock (_gate)
                    ByDateLocked(charId)[today] = plan;

                Persist(charId);
                RaiseChanged(charId);
                App.WriteLog($"DailyPlan.Ensure done char={charId} date={today} ai={plan.GeneratedByAi} items={plan.Items.Count}");
                return plan;
            }
            finally
            {
                lock (_gate) _generating.Remove(charId);
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("DailyPlan.EnsureToday -> " + ex.Message);
            return null;
        }
    }

    /// <summary>同步拿今天的日程（未加载时会触发后台加载，首次调用可能返回 null；UI 加载时应先 await EnsureTodayAsync）。</summary>
    public DailyPlan? GetToday(string charId)
    {
        if (string.IsNullOrWhiteSpace(charId)) return null;
        try
        {
            DropCacheIfScopeChanged();
            bool loaded;
            lock (_gate) loaded = _loaded.Contains(charId);
            if (!loaded) _ = LoadAsync(charId);

            var today = TodayKey;
            lock (_gate)
                return ByDateLocked(charId).TryGetValue(today, out var p) ? p : null;
        }
        catch (Exception ex)
        {
            App.WriteLog("DailyPlan.GetToday -> " + ex.Message);
            return null;
        }
    }

    /// <summary>手动勾选某条为已完成（UI 点击用）。</summary>
    public void MarkDone(string charId, string itemId)
    {
        if (string.IsNullOrWhiteSpace(charId) || string.IsNullOrWhiteSpace(itemId)) return;
        try
        {
            bool changed = false;
            lock (_gate)
            {
                if (ByDateLocked(charId).TryGetValue(TodayKey, out var plan) && plan is not null)
                {
                    var item = plan.Items.FirstOrDefault(i => i.Id == itemId);
                    if (item is not null && !item.Done)
                    {
                        item.Done = true;
                        item.ResultAt = DateTime.UtcNow;
                        changed = true;
                    }
                }
            }
            if (changed) { Persist(charId); RaiseChanged(charId); }
        }
        catch (Exception ex) { App.WriteLog("DailyPlan.MarkDone -> " + ex.Message); }
    }

    /// <summary>
    /// 世界时钟推进：把当前时刻之前、尚未完成的条目自动标记 Done。
    /// 只更新日程与 presence 痕迹，不驱动聊天、不发消息，绝不打扰用户。
    /// </summary>
    public void Tick(string charId)
    {
        if (string.IsNullOrWhiteSpace(charId)) return;
        try
        {
            bool loaded;
            lock (_gate) loaded = _loaded.Contains(charId);
            if (!loaded) { _ = LoadAsync(charId); return; }

            var now = DateTime.Now;
            var nowMin = now.Hour * 60 + now.Minute;
            bool changed = false;
            lock (_gate)
            {
                if (ByDateLocked(charId).TryGetValue(DateKeyOf(now), out var plan) && plan is not null)
                {
                    foreach (var item in plan.Items)
                    {
                        if (item.Done) continue;
                        if (item.Hour * 60 + item.Minute <= nowMin)
                        {
                            item.Done = true;
                            item.ResultAt = DateTime.UtcNow;
                            changed = true;
                        }
                    }
                }
            }
            if (changed) { Persist(charId); RaiseChanged(charId); }
        }
        catch (Exception ex) { App.WriteLog("DailyPlan.Tick -> " + ex.Message); }
    }

    /// <summary>监测页展示用的快照（匿名对象，字段稳定，UI 读属性即可）。</summary>
    public object Snapshot(string charId)
    {
        var plan = GetToday(charId);
        if (plan is null) return new { hasPlan = false, date = TodayKey, characterId = charId ?? "" };
        try
        {
            lock (_gate)
            {
                return new
                {
                    hasPlan = true,
                    date = plan.Date,
                    characterId = plan.CharacterId,
                    outfit = plan.Outfit,
                    note = plan.Note,
                    generatedByAi = plan.GeneratedByAi,
                    generatedAt = plan.GeneratedAt,
                    items = plan.Items.Select(i => new
                    {
                        id = i.Id,
                        hour = i.Hour,
                        minute = i.Minute,
                        time = i.TimeText,
                        kind = i.Kind.ToString(),
                        title = i.Title,
                        detail = i.Detail,
                        done = i.Done,
                        resultAt = i.ResultAt
                    }).ToList()
                };
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("DailyPlan.Snapshot -> " + ex.Message);
            return new { hasPlan = false, date = TodayKey, characterId = charId ?? "" };
        }
    }

    // ==================== AI 生成（一天一次） ====================

    private async Task<DailyPlan?> TryGenerateWithAiAsync(string charId, string date)
    {
        try
        {
            var services = ServicesHelper.GetServices();
            var api = services?.GetService(typeof(ApiGateway)) as ApiGateway;
            if (api is null || !api.HasAnyEndpoint) return null;   // 未配 API：静默走模板，不报错

            var name = ResolveName(charId);
            var weather = ResolveWeatherText();
            var system = BuildSystemPrompt(name, weather);
            var msgs = new List<ChatMessage>
            {
                new() { Role = "system", Content = system },
                new() { Role = "user", Content = $"安排{name}今天的日程，只输出 JSON 数组。" }
            };

            App.TraceStep($"DailyPlan.AI begin char={charId} promptLen={system.Length}");
            var reply = await api.Chat(msgs);
            if (string.IsNullOrWhiteSpace(reply)) return null;

            var items = ParseItems(reply, date);
            if (items.Count < 3) return null;   // 解析不干净就当失败，交模板兜底

            var wear = items.FirstOrDefault(i => i.Kind == PlanKind.Wear);
            return new DailyPlan
            {
                Date = date,
                CharacterId = charId,
                Items = items,
                Outfit = wear?.Title ?? WeatherOutfit(weather),
                Note = $"{name}今天有自己的安排。",
                GeneratedByAi = true,
                GeneratedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            App.WriteLog("DailyPlan.AI -> " + ex.Message);
            return null;
        }
    }

    /// <summary>极短提示词（&lt;400 字）：只要 JSON 数组，不要解释；字段与枚举严格限定。</summary>
    private static string BuildSystemPrompt(string name, string weather) =>
        "你是日程助手。只输出一个 JSON 数组，不要解释、不要代码块、不要 Markdown。" +
        "每个元素含 hour(0-23整数)、minute(0-59整数)、kind(只取 Wear/Chat/Work/Rest/Move/Eat/Sleep/Mood/Social/Misc)、" +
        "title(不超过12字)、detail(不超过30字)。共6到8条，按时间升序，覆盖起床穿衣、三餐、休息、活动、社交、睡觉。" +
        $"角色：{name}。天气：{weather}。";

    /// <summary>严格解析：必须是 JSON 数组；字段缺失/枚举非法/时间越界的条目直接丢弃。</summary>
    private static List<PlanItem> ParseItems(string reply, string date)
    {
        var result = new List<PlanItem>();
        try
        {
            var text = reply.Trim();
            // 容忍模型套了 ```json ... ``` 代码块，但里面仍必须是合法 JSON
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                var nl = text.IndexOf('\n');
                if (nl >= 0) text = text[(nl + 1)..];
                var tail = text.LastIndexOf("```", StringComparison.Ordinal);
                if (tail >= 0) text = text[..tail];
            }
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

            var i = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                if (!el.TryGetProperty("hour", out var h) || !h.TryGetInt32(out var hour)) continue;
                if (hour is < 0 or > 23) continue;

                var minute = 0;
                if (el.TryGetProperty("minute", out var m) && m.TryGetInt32(out var mm))
                    minute = Math.Clamp(mm, 0, 59);

                var kind = PlanKind.Misc;
                if (el.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String)
                {
                    if (!Enum.TryParse(k.GetString(), ignoreCase: true, out kind)) continue;  // 非法枚举：跳过该条
                }
                else continue;

                var title = ReadString(el, "title");
                if (string.IsNullOrWhiteSpace(title)) continue;

                result.Add(new PlanItem
                {
                    Id = $"{date}-{i++}",
                    Hour = hour,
                    Minute = minute,
                    Kind = kind,
                    Title = title,
                    Detail = ReadString(el, "detail")
                });
            }
            result.Sort((a, b) => (a.Hour * 60 + a.Minute).CompareTo(b.Hour * 60 + b.Minute));
        }
        catch (Exception ex)
        {
            App.WriteLog("DailyPlan.Parse -> " + ex.Message);
            result.Clear();
        }
        return result;
    }

    private static string ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

    // ==================== 本地模板兜底 ====================

    /// <summary>没配 API 或 AI 失败时的兜底：按小时排 6-8 个时段的占位日程，绝不空白。</summary>
    private static DailyPlan BuildTemplate(string charId, string date)
    {
        var name = ResolveName(charId);
        var weather = ResolveWeatherText();
        var outfit = WeatherOutfit(weather);

        List<PlanItem> Items() => new()
        {
            Tpl(date, 0, 7, 0,  PlanKind.Wear,  "起床穿衣", $"换上{outfit}"),
            Tpl(date, 1, 8, 0,  PlanKind.Eat,   "吃早饭",   "简单吃点，别赶时间"),
            Tpl(date, 2, 10, 0, PlanKind.Work,  "处理事情", "做今天最要紧的那件"),
            Tpl(date, 3, 12, 30,PlanKind.Eat,   "吃午饭",   "好好吃一顿"),
            Tpl(date, 4, 14, 0, PlanKind.Rest,  "午休",     "眯一会儿，缓口气"),
            Tpl(date, 5, 16, 30,PlanKind.Move,  "出门走走", weather == "雨" || weather == "雪" ? "就在附近转转，注意脚下" : "出去晒晒太阳、动一动"),
            Tpl(date, 6, 19, 30,PlanKind.Social,"聊聊天",   "和惦记的人说几句"),
            Tpl(date, 7, 23, 0, PlanKind.Sleep, "睡觉",     "早点休息，明天继续")
        };

        return new DailyPlan
        {
            Date = date,
            CharacterId = charId,
            Items = Items(),
            Outfit = outfit,
            Note = $"{name}今天按自己的节奏过（本地安排）。{weather}",
            GeneratedByAi = false,
            GeneratedAt = DateTime.UtcNow
        };
    }

    private static PlanItem Tpl(string date, int i, int hour, int minute, PlanKind kind, string title, string detail) =>
        new() { Id = $"{date}-{i}", Hour = hour, Minute = minute, Kind = kind, Title = title, Detail = detail };

    private static string WeatherOutfit(string weather) => weather switch
    {
        "雨" => "防水外套，带把伞",
        "雪" => "厚外套和围巾",
        "晴" => "轻便的日常衣服",
        _ => "舒服的日常衣服"
    };

    // ==================== 依赖解析（可失败降级） ====================

    private static string ResolveName(string charId)
    {
        try
        {
            var engine = ServicesHelper.GetServices()?.GetService(typeof(GameEngine)) as GameEngine;
            if (engine is not null && engine.Roster.TryGetValue(charId, out var ch))
                return ch.Profile.Name;
        }
        catch (Exception ex) { App.WriteLog("DailyPlan.ResolveName -> " + ex.Message); }
        return "她";
    }

    /// <summary>天气若可得就带上（反射读 WeatherController，避免平台条件编译依赖）；取不到就空串。</summary>
    private static string ResolveWeatherText()
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("WarmAsBefore.Weather3D.WeatherController");
                if (t is null) continue;
                var inst = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var val = t.GetProperty("Current", BindingFlags.Public | BindingFlags.Instance)?.GetValue(inst);
                return val?.ToString() switch
                {
                    "Rain" => "雨",
                    "Snow" => "雪",
                    "Clear" => "晴",
                    _ => ""
                };
            }
        }
        catch (Exception ex) { App.WriteLog("DailyPlan.ResolveWeather -> " + ex.Message); }
        return "";
    }

    /// <summary>换档/新开局后由主 Agent 调用：丢掉内存缓存，下一轮按需重读当前存档的日程。</summary>
    public void ReleaseCache()
    {
        try
        {
            lock (_gate)
            {
                _book.ByCharacter.Clear();
                _loaded.Clear();
                _generating.Clear();
                _scopeToken = "";
            }
        }
        catch (Exception ex) { App.WriteLog("DailyPlan.ReleaseCache -> " + ex.Message); }
    }
}
