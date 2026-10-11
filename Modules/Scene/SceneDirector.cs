using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Maui.ApplicationModel;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Scene;

/// <summary>
/// 生效来源。区分「主动」与「被动」两大类：
///  - 主动（Active）：Ai（模型/【场景:】标记）、Manual（用户在场景库手动切换）
///  - 被动（Passive）：Time（时间规则自动触发）、Weather（天气规则自动触发）
///  - None：尚未生效
/// 仅用于展示与诊断。
/// </summary>
public enum SceneApplySource
{
    None,
    Ai,
    Time,
    /// <summary>用户在场景库页手动切换。</summary>
    Manual,
    /// <summary>天气规则自动触发（被动）。</summary>
    Weather
}

public static class SceneApplySourceExtensions
{
    /// <summary>是否属于「主动发起」（AI 指令或用户手动）。</summary>
    public static bool IsActive(this SceneApplySource s)
        => s is SceneApplySource.Ai or SceneApplySource.Manual;

    /// <summary>是否属于「被动触发」（时间/天气等环境因素）。</summary>
    public static bool IsPassive(this SceneApplySource s)
        => s is SceneApplySource.Time or SceneApplySource.Weather;
}

/// <summary>
/// 场景选项：一个「条目」或「条目/模式」组合在某一时刻的可选性快照。
/// 设计要点：AI 必须先从这张表里选（BuildContext 注入 / get_scene_options 工具），不能再凭想象硬切；
/// 每项同时给出「此刻能不能用（及原因）」与「用了看不看得出变化」，让模型不必靠反复尝试去猜。
/// </summary>
public sealed class SceneOption
{
    /// <summary>写回用的规格串：无模式时=条目名，有模式时=「条目名/模式名」（可直接写进【场景:…】标记）。</summary>
    public string Spec { get; set; } = "";
    public string EntryId { get; set; } = "";
    public string EntryName { get; set; } = "";
    public string? ModeId { get; set; }
    public string? ModeName { get; set; }

    /// <summary>展示名：有模式时「条目 · 模式」。</summary>
    public string Title => ModeName is null ? EntryName : $"{EntryName} · {ModeName}";

    /// <summary>此刻是否可用（所有门控都放行）。</summary>
    public bool Available { get; set; }

    /// <summary>不在生效时段内的理由（自然语言，直接给 AI 看）。只是提醒——不阻止切换。</summary>
    public string? UnavailableReason { get; set; }

    /// <summary>可用条件说明（如「受时间限制：22:00-06:00（当前在区间内）」）；无门控时为空。</summary>
    public string? AvailableReason { get; set; }

    /// <summary>就是当前生效的选项（此时 Available 为真但不会产生任何变化）。</summary>
    public bool AlreadyActive { get; set; }

    /// <summary>应用后画面（背景图/背景色/亮度遮罩）是否会产生变化。BGM 变化不算「看得见」。</summary>
    public bool VisibleChange { get; set; }

    /// <summary>变化说明；无可见变化时解释为什么看不出（如「白天开灯」）。</summary>
    public string ChangeDetail { get; set; } = "";
}

/// <summary>
/// 应用选项的结果。把「真的切了没有 / 看不看得出来」如实带回给 AI，
/// 取代旧实现里不论成败都回「已切换到 X」的谎报（那会让 AI 以为自己成功了而继续加戏）。
/// </summary>
public sealed record SceneApplyResult(
    string Status,
    string Message,
    bool VisibleChange,
    SceneOption? Option)
{
    /// <summary>已尝试应用（画面有无变化都算）。</summary>
    public bool Applied => Status is "applied" or "unchanged";

    public bool NotFound => Status == "not_found";

    /// <summary>
    /// 历史遗留：门控已不再阻止切换，所以状态不会再是 "unavailable"，此属性现在恒为 false。
    /// 保留是为了不破坏既有调用方（工具与 UI 的状态映射）。
    /// </summary>
    public bool Unavailable => Status == "unavailable";
}

/// <summary>
/// 选项可用性门控：给定选项与时刻，判断此刻能否被选。
/// 目前只有时间区间一种实现（SceneTimeGate）；以后加天气/节日/好感，实现本接口后
/// 塞进 SceneDirector 的门控表即可，选项表与工具输出会自动带上新条件，不必改 AI 侧协议。
/// </summary>
public interface ISceneOptionGate
{
    /// <summary>返回 null = 此刻在生效时段内；否则返回要给 AI 的提醒理由（不再阻止切换）。</summary>
    string? BlockReason(SceneLibraryEntry entry, SceneMode? mode, DateTime now);

    /// <summary>放行时给出的「为什么现在能用」说明（可空）。用于选项表里标注条件，如「限 22:00-06:00」。</summary>
    string? AllowReason(SceneLibraryEntry entry, SceneMode? mode, DateTime now) => null;
}

/// <summary>时间区间门控：模式声明了 TimeRanges 时，只有当前时刻落在区间内才可选。</summary>
public sealed class SceneTimeGate : ISceneOptionGate
{
    public string? BlockReason(SceneLibraryEntry entry, SceneMode? mode, DateTime now)
    {
        if (mode is null || mode.TimeRanges.Count == 0) return null;
        if (SceneDirector.TimeRangeMatches(mode.TimeRanges, now)) return null;
        return $"该模式限定在 {string.Join("、", mode.TimeRanges)} 生效，当前 {now:HH:mm} 不在区间内";
    }

    public string? AllowReason(SceneLibraryEntry entry, SceneMode? mode, DateTime now)
        => mode is { TimeRanges.Count: > 0 }
            ? $"受时间限制：{string.Join("、", mode.TimeRanges)}（当前在区间内）"
            : null;
}

/// <summary>
/// 天气门控：模式声明了 WeatherTags 时，只有当前天气命中任一标签才可选。
/// 与时间门控并列，两者互不干涉：一条模式可以只受时间约束、只受天气约束，或两者都受。
/// 天气来源由外部通过委托注入（读的是 SceneDirector 缓存的最新天气串），
/// 因此门控本身不持有网络依赖，也不会阻塞 UI 线程。
/// </summary>
public sealed class SceneWeatherGate : ISceneOptionGate
{
    private readonly Func<string?> _weatherOf;

    /// <param name="weatherOf">读取「当前天气描述/标签」的委托；返回 null 或空表示天气未知。</param>
    public SceneWeatherGate(Func<string?> weatherOf) => _weatherOf = weatherOf;

    public string? BlockReason(SceneLibraryEntry entry, SceneMode? mode, DateTime now)
    {
        if (mode is null || mode.WeatherTags.Count == 0) return null;
        var current = _weatherOf();
        if (string.IsNullOrWhiteSpace(current)) return null; // 天气未知时不拦，避免断网就全锁死
        if (Matches(current, mode.WeatherTags)) return null;
        return $"该模式限定在天气「{string.Join("、", mode.WeatherTags)}」时生效，当前天气「{current}」不匹配";
    }

    public string? AllowReason(SceneLibraryEntry entry, SceneMode? mode, DateTime now)
    {
        if (mode is null || mode.WeatherTags.Count == 0) return null;
        var current = _weatherOf();
        if (string.IsNullOrWhiteSpace(current)) return null;
        return Matches(current, mode.WeatherTags)
            ? $"受天气限制：{string.Join("、", mode.WeatherTags)}（当前「{current}」命中）"
            : null;
    }

    /// <summary>天气串与标签的匹配：忽略大小写，且「标签被天气串包含」也算命中（如天气 "light rain" 命中标签 "rain"）。</summary>
    private static bool Matches(string current, List<string> tags)
    {
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag)) continue;
            if (current.Contains(tag, StringComparison.OrdinalIgnoreCase)) return true;
            if (tag.Contains(current, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

/// <summary>
/// 场景导演：场景库的统一执行入口。
/// 职责：
///  1. 加载/持久化场景库（scene_library.json）；
///  2. 选项制切换：把「条目 × 模式」做成带可用性（时间门控等）与可见变化判定的选项表，
///     AI 先取表再选（【场景:名/模式】标记 或 set_scene_option 工具），无选项/看不出来都会如实回报；
///  3. 时间触发：按模式的 TimeRanges 评估并应用（关灯=夜间夜灯），未命中不动、不回退；
///  4. 组装注入 ChatEngine 的场景语境（BuildContext，含当前可用选项与 AiRules）；
///  5. 应用模式：切换地图场景引用、背景/背景色、亮度遮罩、BGM。
/// 与地图页的「单场景直接可用」打通：MapSceneRef 为空时用库条目的 Background 直接渲染。
/// </summary>
public sealed class SceneDirector : IDisposable
{
    private readonly StorageProvider _store;
    private readonly MapService _map;
    private readonly AudioController _audio;
    private readonly MaterialLibrary _materials;
    private readonly Timer _timeTimer;

    /// <summary>串行门：ApplyAsync 会被时间 Timer 线程与 AI/UI 线程同时调用，_active* 是共享状态。</summary>
    private readonly SemaphoreSlim _applyLock = new(1, 1);

    /// <summary>可用性门控表（顺序即评估顺序，第一个拦下的理由会返回给 AI）。</summary>
    /// <remarks>含时间门控与天气门控；天气门控通过委托读取下方缓存的 _currentWeather，故此处延迟到构造函数内初始化。</remarks>
    private readonly List<ISceneOptionGate> _gates;

    /// <summary>天气数据源（可空：未注册时天气门控视为「未知」，不拦任何模式）。</summary>
    private readonly Modules.RealWorld.WeatherProvider? _weather;

    /// <summary>
    /// 最近一次刷新的「当前天气」串（如 "clear"/"rain"）。volatile 保证跨线程可见：
    /// 刷新发生在定时器线程，读取发生在 UI/AI 线程的门控里。
    /// </summary>
    private volatile string? _currentWeather;

    private List<SceneLibraryEntry> _library = new();
    private SceneLibraryEntry? _active;
    private SceneMode? _activeMode;

    /// <summary>当前生效来源与时刻（替代旧的无意义死字段 _timeApplied）。</summary>
    private SceneApplySource _source = SceneApplySource.None;
    private DateTime _appliedAt;

    /// <summary>最近一次时间评估命中的模式（诊断「为什么切不动」用）。</summary>
    private SceneMode? _lastTimeMatch;

    /// <summary>当前亮度遮罩（0..1），由生效模式的 Lighting 换算而来。</summary>
    private double _activeDim;

    /// <summary>当前音乐（Bgm 为空表示「不改音乐」，因此要记住上一首才能判断是否真的换了）。</summary>
    private string _activeBgm = "";

    private static readonly Regex SceneMarkerRe =
        new(@"【场景:([^】]+)】", RegexOptions.Compiled);

    public SceneDirector(StorageProvider store, MapService map, AudioController audio, MaterialLibrary materials,
        Modules.RealWorld.WeatherProvider? weather = null)
    {
        _store = store;
        _map = map;
        _audio = audio;
        _materials = materials;
        _weather = weather;
        // 门控表：时间 + 天气。天气门控用委托读缓存的 _currentWeather（volatile），不直接碰网络。
        _gates = new() { new SceneTimeGate(), new SceneWeatherGate(() => _currentWeather) };
        // 1 分钟一次：早中晚边界响应更快（旧值 5 分钟最坏要等 5 分钟才换背景）。
        // tick 只做一次线性查找 + 状态比较，成本可忽略；真正会阻塞的只有命中后的应用。
        _timeTimer = new Timer(_ => _ = EvaluateTimeAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1));
        _ = LoadAsync();
        // 启动即刷新一次天气；失败也无妨，门控会按「未知」放行。
        _ = RefreshWeatherAsync();
    }

    // ============ 加载 / 持久化 ============

    public async Task LoadAsync()
    {
        try
        {
            var loaded = await _store.Load<List<SceneLibraryEntry>>("scene_library");
            if (loaded is not null) _library = loaded;
        }
        catch (Exception ex)
        {
            App.WriteLog("SceneDirector.Load -> " + ex);
        }
    }

    public async Task SaveAsync() => await _store.Save("scene_library", _library);

    public IReadOnlyList<SceneLibraryEntry> Library => _library;

    public SceneLibraryEntry? Active => _active;
    public SceneMode? ActiveMode => _activeMode;

    /// <summary>当前生效来源：时间规则 / AI / 无。UI 用来解释「AI 为什么切不动」。</summary>
    public SceneApplySource Source => _source;

    /// <summary>当前生效时刻。</summary>
    public DateTime AppliedAt => _appliedAt;

    /// <summary>最近一次时间评估命中的模式。</summary>
    public SceneMode? LastTimeMatch => _lastTimeMatch;

    /// <summary>
    /// 当前亮度遮罩值 0..1（0=不加遮罩，越大越暗）。
    /// 主界面在背景层之上叠一个黑色 BoxView，把 Opacity 绑到这里即可看到 dim 模式的夜色效果。
    /// </summary>
    public double ActiveDim => _activeDim;

    /// <summary>模式被应用时触发（时间触发与 AI 触发共用）。保证在 UI 线程派发。</summary>
    public event Action<SceneLibraryEntry, SceneMode?>? ModeApplied;

    /// <summary>
    /// 天气事件（打雷 / 下雨）触发时回调，参数是事件种类与当前天气串。
    /// 判定在时间 Timer 线程完成，主界面收到后须自行切到 UI 线程渲染白色闪光 / 图片帧 / 播放音效。
    /// </summary>
    public event Action<WeatherEventKind, string?>? WeatherEventTriggered;

    /// <summary>天气事件种类。</summary>
    public enum WeatherEventKind
    {
        /// <summary>打雷：白色闪光 + 可选雷声 + 可选帧序列。</summary>
        Lightning,
        /// <summary>下雨：可选雨声 + 可选雨帧循环。</summary>
        Rain
    }

    /// <summary>
    /// 天气事件设置快照。由主界面在设置变更时写入，Timer 线程读取。
    /// 用 volatile 引用整体替换，避免逐字段跨线程读写的竞态。
    /// </summary>
    private volatile WeatherEventConfig _weatherEvents = new();

    /// <summary>更新天气事件配置（设置页改动后调用；可空字段保持默认）。</summary>
    public void SetWeatherEventConfig(WeatherEventConfig config) => _weatherEvents = config ?? new();

    /// <summary>读取当前配置（UI 诊断用）。</summary>
    public WeatherEventConfig WeatherEvents => _weatherEvents;

    /// <summary>上次触发打雷/下雨的时刻，用于冷却，避免同一场雨里连续轰。</summary>
    private DateTime _lastLightningAt = DateTime.MinValue;
    private DateTime _lastRainAt = DateTime.MinValue;

    /// <summary>天气事件配置（主界面写入、SceneDirector 读取；全部为不可变值语义，便于整块替换）。</summary>
    public sealed record WeatherEventConfig
    {
        public bool LightningEnabled { get; init; } = true;
        public double LightningProbability { get; init; } = 0.06;
        public bool RainEventEnabled { get; init; } = true;
        public double RainProbability { get; init; } = 0.15;
    }

    // ============ 库管理 ============

    public SceneLibraryEntry AddEntry(string name)
    {
        var e = new SceneLibraryEntry { Name = name };
        _library.Add(e);
        return e;
    }

    public void RemoveEntry(string entryId)
    {
        _library.RemoveAll(e => e.Id == entryId);
        if (_active?.Id == entryId) { _active = null; _activeMode = null; _activeDim = 0; _source = SceneApplySource.None; }
    }

    public SceneMode AddMode(SceneLibraryEntry entry, string name)
    {
        var m = new SceneMode { Name = name };
        entry.Modes.Add(m);
        return m;
    }

    /// <summary>删除模式；若正生效则退回「仅条目」态（条目自身不变，只去掉模式覆盖）。</summary>
    public void RemoveMode(SceneLibraryEntry entry, SceneMode mode)
    {
        entry.Modes.Remove(mode);
        if (ReferenceEquals(_activeMode, mode))
        {
            _activeMode = null;
            _activeDim = 0;
            _activeBgm = "";
        }
    }

    // ============ 选项表 ============

    /// <summary>
    /// 列出全库选项及其在当前时刻的可用性与可见变化（条目 × 模式，外加「仅条目」默认态）。
    /// now 可显式传入（便于测试/预览其它时段）；AI 语境与 get_scene_options 都走这里，保证口径一致。
    /// </summary>
    public IReadOnlyList<SceneOption> BuildOptions(DateTime? now = null)
    {
        var at = now ?? DateTime.Now;
        var list = new List<SceneOption>();
        // 复制一份再遍历：UI 线程可能同时在增删条目，直接枚举 List 会抛
        foreach (var e in _library.ToList())
        {
            foreach (var m in e.Modes.ToList()) list.Add(BuildOptionAt(e, m, at));
            list.Add(BuildOptionAt(e, null, at));
        }
        return list;
    }

    /// <summary>单独构造一个选项（含门控与可见变化判定），不改变任何状态。</summary>
    public SceneOption BuildOption(SceneLibraryEntry entry, SceneMode? mode, DateTime? now = null)
        => BuildOptionAt(entry, mode, now ?? DateTime.Now);

    private SceneOption BuildOptionAt(SceneLibraryEntry entry, SceneMode? mode, DateTime now)
    {
        var opt = new SceneOption
        {
            Spec = mode is null ? entry.Name : $"{entry.Name}/{mode.Name}",
            EntryId = entry.Id,
            EntryName = entry.Name,
            ModeId = mode?.Id,
            ModeName = mode?.Name,
            AlreadyActive = ReferenceEquals(_active, entry) && ReferenceEquals(_activeMode, mode)
        };

        opt.Available = true;
        var allows = new List<string>();
        foreach (var gate in _gates)
        {
            if (gate.BlockReason(entry, mode, now) is { } reason)
            {
                opt.Available = false;
                opt.UnavailableReason = reason;
                break;
            }
            if (gate.AllowReason(entry, mode, now) is { } allow) allows.Add(allow);
        }
        if (opt.Available && allows.Count > 0) opt.AvailableReason = string.Join("；", allows);

        if (!opt.AlreadyActive)
        {
            var diff = _active is null
                ? new RenderDiff(true, "背景图", "")
                : Diff(CurrentRenderState(), ResolveRenderState(entry, mode));
            opt.VisibleChange = diff.Visual;
            opt.ChangeDetail = diff.Describe();
        }
        else
        {
            opt.VisibleChange = false;
            opt.ChangeDetail = "就是当前生效的选项";
        }
        return opt;
    }

    // ============ 可见变化判定 ============

    /// <summary>一帧画面的可比较快照：背景图 / 背景色 / 亮度遮罩 / 灯光基调 / BGM。</summary>
    private sealed record RenderState(string Background, string Color, double Dim, string Lighting, string Bgm);

    /// <summary>差异结果：Visual=画面会变；Fields=会变的字段（可为空=完全无变化）；Why=看不出变化时的解释。</summary>
    private readonly record struct RenderDiff(bool Visual, string Fields, string Why)
    {
        /// <summary>选项表用：切换后会发生什么。</summary>
        public string Describe() => Fields.Length switch
        {
            0 => "切换后看不出变化" + Why,
            _ when Visual => $"切换后会改变：{Fields}",
            _ => $"切换后画面不变，只改 {Fields}"
        };

        /// <summary>应用后的回报用（绝不谎报「切换成功但画面没变」）。</summary>
        public string Report(string title) => Fields.Length switch
        {
            0 => $"已切换到「{title}」，但看不出变化{Why}",
            _ when Visual => $"已切换到「{title}」，画面变化：{Fields}",
            _ => $"已切换到「{title}」，画面没有变化，只改了 {Fields}"
        };
    }

    /// <summary>
    /// 当前实际渲染态。BGM 用「正在播放的那首」而不是生效模式的字段——
    /// 模式 Bgm 为空表示「不改音乐」，此时上一首还在放，用字段比会误判成「换了 BGM」。
    /// </summary>
    private RenderState CurrentRenderState()
        => _active is null
            ? new RenderState("", "", 0, "", "")
            : ResolveRenderState(_active, _activeMode) with { Bgm = _activeBgm };

    /// <summary>解析选项最终会渲染成什么（与 MainGameViewModel.OnSceneModeApplied 的取用顺序保持一致）。</summary>
    private RenderState ResolveRenderState(SceneLibraryEntry entry, SceneMode? mode)
    {
        var lighting = mode?.Lighting ?? "";
        var bg = mode?.Background is { Length: > 0 } mb ? mb : entry.Background;
        var color = mode?.BackgroundColor is { Length: > 0 } mc ? mc : entry.BackgroundColor;

        // 条目引用了地图场景、且自身没给背景时，画面由地图场景渲染（MoveToAsync → SceneChanged）
        if (string.IsNullOrEmpty(bg) && !string.IsNullOrEmpty(entry.MapSceneRef)
            && _map.IsLoaded && _map.Map.SceneById(entry.MapSceneRef) is { } scene)
        {
            bg = scene.Background;
            if (string.IsNullOrEmpty(color)) color = scene.BackgroundColor;
        }
        return new RenderState(bg, color, DimForLighting(lighting), lighting, mode?.Bgm ?? "");
    }

    private static RenderDiff Diff(RenderState cur, RenderState next)
    {
        var fields = new List<string>();
        var bgChanged = !string.Equals(cur.Background, next.Background, StringComparison.Ordinal);
        var colorChanged = !string.Equals(cur.Color, next.Color, StringComparison.Ordinal);
        var dimChanged = Math.Abs(cur.Dim - next.Dim) > 0.001;
        if (bgChanged) fields.Add("背景图");
        if (colorChanged) fields.Add("背景色");
        if (dimChanged) fields.Add("亮度");
        // Bgm 为空 = 不改音乐，所以只有目标指定了 BGM 且与当前不同才算换曲
        var bgmChanged = next.Bgm.Length > 0 && !string.Equals(cur.Bgm, next.Bgm, StringComparison.Ordinal);
        if (bgmChanged) fields.Add("BGM");

        var visual = bgChanged || colorChanged || dimChanged;
        if (fields.Count == 0)
        {
            // 灯光基调变了但遮罩值相同：这正是「早上开灯看不出来」——白天本就没有遮罩
            var why = !dimChanged
                && !string.Equals(cur.Lighting, next.Lighting, StringComparison.OrdinalIgnoreCase)
                ? $"（灯光基调由「{LightingLabel(cur.Lighting)}」变为「{LightingLabel(next.Lighting)}」，"
                  + "但两者的亮度遮罩相同——就像白天开灯，画面不会变）"
                : "（与当前画面、音乐完全一致，可能已经是这个状态）";
            return new RenderDiff(false, "", why);
        }
        return new RenderDiff(visual, string.Join("、", fields), "");
    }

    /// <summary>
    /// 灯光基调 → 亮度遮罩（0=不加遮罩，1=全黑）。
    /// dim 压暗到 45%（夜灯/关灯的观感）；normal 与 bright 都不加遮罩——
    /// 二者在「画面亮度」上等价，所以白天把 normal 切成 bright 看不出变化，这是有意为之的语义。
    /// </summary>
    public static double DimForLighting(string? lighting) => (lighting ?? "").Trim().ToLowerInvariant() switch
    {
        "dim" => 0.45,
        _ => 0.0
    };

    /// <summary>灯光基调的中文标签（选项表、场景库页共用一份，避免两处口径漂移）。</summary>
    public static string LightingLabel(string? lighting) => (lighting ?? "").Trim().ToLowerInvariant() switch
    {
        "dim" => "暗",
        "bright" => "亮",
        _ => "正常"
    };

    // ============ 时间触发 ============

    /// <summary>
    /// 按当前时间评估并应用。三条规则（对应旧实现的坑）：
    ///  1. 未命中 → 什么都不做，保持 AI/用户当前的态（旧实现没有回退路径，这里明确不回退，
    ///     否则时间区间一结束就会把 AI 刚切的场景顶掉）；
    ///  2. 同时命中多个 → 若当前生效模式也在命中集合内则保持它，避免多规则在同一时段来回抖动；
    ///  3. 命中且与当前不同 → 应用并标记来源为 Time。
    /// </summary>
    private async Task EvaluateTimeAsync()
    {
        try
        {
            // 顺带刷新天气：被动触发的第二类来源。放在时间评估同一 tick 里，避免再起一个定时器。
            await RefreshWeatherAsync();
            // 天气事件（打雷/下雨）：与被动场景应用同 tick 判定，独立回调，不影响 _active 状态。
            EvaluateWeatherEvents();
            var now = DateTime.Now;

            // 被动触发优先级：时间规则 > 天气规则（两者都属被动，时间优先因为它对时刻更敏感）。
            // 任一命中即可，未命中不动、不回退——沿用旧约定。
            var target = FindModesByTime(now).FirstOrDefault(h => ReferenceEquals(h.Mode, _activeMode))
                         ?? FindModesByTime(now).FirstOrDefault();
            var source = SceneApplySource.Time;

            if (target is null)
            {
                target = FindModesByWeather().FirstOrDefault(h => ReferenceEquals(h.Mode, _activeMode))
                         ?? FindModesByWeather().FirstOrDefault();
                source = SceneApplySource.Weather;
            }

            if (target is null) return;
            _lastTimeMatch = target.Mode;
            if (ReferenceEquals(_active, target.Entry) && ReferenceEquals(_activeMode, target.Mode)) return;

            var result = await ApplyOptionAsync(target.Entry, target.Mode, source);
            if (!result.Applied)
                App.WriteLog($"SceneDirector.EvaluatePassive: 命中({source})但未应用 -> {result.Message}");
        }
        catch (Exception ex)
        {
            App.WriteLog("SceneDirector.EvaluateTime -> " + ex);
        }
    }

    /// <summary>按当前天气扫描库中所有模式的 WeatherTags，按库顺序返回全部命中项。</summary>
    private List<TimeMatchResult> FindModesByWeather()
    {
        var hits = new List<TimeMatchResult>();
        var current = _currentWeather;
        if (string.IsNullOrWhiteSpace(current)) return hits;
        foreach (var e in _library.ToList())
            foreach (var m in e.Modes.ToList())
                if (m.WeatherTags.Count > 0 && m.WeatherTags.Any(t =>
                        !string.IsNullOrWhiteSpace(t) &&
                        (current.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                         t.Contains(current, StringComparison.OrdinalIgnoreCase))))
                    hits.Add(new TimeMatchResult(e, m));
        return hits;
    }

    // ============ 天气事件（打雷 / 下雨） ============

    /// <summary>打雷冷却：同一场雨里两次打雷至少隔 45 秒，避免连续轰。</summary>
    private static readonly TimeSpan LightningCooldown = TimeSpan.FromSeconds(45);

    /// <summary>下雨事件冷却：60 秒，避免每次 tick 都重放雨声。</summary>
    private static readonly TimeSpan RainCooldown = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 天气事件判定：读取当前天气串与设置，按概率随机触发打雷/下雨回调。
    /// 概率口径（与设置项注释一致）：
    ///  - 打雷：rain/drizzle/storm/thunder 命中时基准概率；thunder/storm（打雷天）放大 4 倍；
    ///  - 下雨：rain/drizzle/storm/thunder 命中时按下雨概率。
    /// 全部候选都走冷却，命中后置位时间戳；异常一律吞掉（天气事件绝不打断主流程）。
    /// </summary>
    private void EvaluateWeatherEvents()
    {
        try
        {
            var cfg = _weatherEvents;
            var current = _currentWeather;
            if (string.IsNullOrWhiteSpace(current)) return;

            var w = current.ToLowerInvariant();
            bool isRain = w.Contains("rain") || w.Contains("drizzle") || w.Contains("shower");
            bool isStorm = w.Contains("thunder") || w.Contains("storm") || w.Contains("lightning");
            bool isOvercast = w.Contains("overcast") || w.Contains("cloud") || w.Contains("阴");
            if (!isRain && !isStorm && !isOvercast) return; // 晴天不打雷不下雨

            var now = DateTime.Now;
            var rng = Random.Shared;

            // 打雷：极阴天也能打，但概率更低（乘 0.3）；雨天基准；打雷天 4 倍。
            if (cfg.LightningEnabled && now - _lastLightningAt >= LightningCooldown)
            {
                double p = cfg.LightningProbability;
                if (isStorm) p *= 4.0;
                else if (isRain) p *= 1.0;
                else if (isOvercast) p *= 0.3;
                p = Math.Clamp(p, 0, 1);
                if (rng.NextDouble() < p)
                {
                    _lastLightningAt = now;
                    WeatherEventTriggered?.Invoke(WeatherEventKind.Lightning, current);
                }
            }

            // 下雨：天气本身已是雨/雷时，按下雨概率触发（用于「进入雨天的雨声/雨视觉」）。
            if (cfg.RainEventEnabled && (isRain || isStorm) && now - _lastRainAt >= RainCooldown)
            {
                var p = Math.Clamp(cfg.RainProbability, 0, 1);
                if (rng.NextDouble() < p)
                {
                    _lastRainAt = now;
                    WeatherEventTriggered?.Invoke(WeatherEventKind.Rain, current);
                }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("SceneDirector.EvaluateWeatherEvents -> " + ex);
        }
    }

    // ============ 时间匹配 ============

    /// <summary>按当前时间扫描库中所有模式的 TimeRanges，按库顺序返回全部命中项。</summary>
    private List<TimeMatchResult> FindModesByTime(DateTime now)
    {
        var hits = new List<TimeMatchResult>();
        foreach (var e in _library.ToList())
            foreach (var m in e.Modes.ToList())
                if (TimeRangeMatches(m.TimeRanges, now)) hits.Add(new TimeMatchResult(e, m));
        return hits;
    }

    internal sealed record TimeMatchResult(SceneLibraryEntry Entry, SceneMode Mode);

    /// <summary>
    /// 解析单条区间（"22:00-06:00"），支持跨午夜。
    /// UI 校验与门控共用这一份实现，避免两处解析口径漂移（"第几条错了"才说得准）。
    /// </summary>
    public static bool TryParseRange(string? text, out TimeSpan from, out TimeSpan to)
    {
        from = default;
        to = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('-');
        if (parts.Length != 2) return false;
        if (!TryParseTime(parts[0], out from)) return false;
        if (!TryParseTime(parts[1], out to)) return false;
        // TimeSpan.TryParse 也接受 "22"（=22 天）或 "1.02:00"，超过一天的判非法
        return from >= TimeSpan.Zero && from < TimeSpan.FromDays(1)
            && to >= TimeSpan.Zero && to < TimeSpan.FromDays(1);
    }

    public static bool TimeRangeMatches(List<string> ranges, DateTime now)
    {
        if (ranges is null || ranges.Count == 0) return false;
        var cur = now.TimeOfDay;
        foreach (var r in ranges)
        {
            if (!TryParseRange(r, out var from, out var to)) continue;
            if (from <= to)
            {
                if (cur >= from && cur < to) return true;
            }
            else // 跨午夜（如 22:00-06:00）
            {
                if (cur >= from || cur < to) return true;
            }
        }
        return false;
    }

    private static bool TryParseTime(string s, out TimeSpan ts)
    {
        return TimeSpan.TryParse(s.Trim(), out ts);
    }

    // ============ AI 指令触发（【场景:名/模式】） ============

    /// <summary>
    /// 解析 AI 回复中的【场景:名/模式】标记并执行切换。
    /// 标记值格式：库条目标题，或「库条目/模式名」。
    /// 返回被剥离标记后的回复与执行结果摘要——结果如实区分「不存在 / 切了但看不出变化 / 切了但不符合当前时段」。
    /// </summary>
    public async Task<(string CleanReply, string Note)> ExecuteMarkersAsync(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply) || !reply.Contains("【场景:", StringComparison.Ordinal))
            return (reply, "");

        var notes = new List<string>();
        var matches = SceneMarkerRe.Matches(reply);
        foreach (Match m in matches)
        {
            var spec = m.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(spec)) continue;
            var result = await ApplySpecAsync(spec, SceneApplySource.Ai);
            notes.Add("（" + result.Message + "）");
        }
        var clean = SceneMarkerRe.Replace(reply, "").Trim();
        return (clean, string.Join("\n", notes));
    }

    /// <summary>
    /// 按规格串（「条目」或「条目/模式」）应用选项。三种失败都要说清理由，
    /// 不能含糊成「已切换」——AI 与工具都靠 Message 判断自己到底做成了没有。
    /// </summary>
    public async Task<SceneApplyResult> ApplySpecAsync(string spec, SceneApplySource source)
    {
        spec = (spec ?? "").Trim();
        if (spec.Length == 0) return new SceneApplyResult("bad_spec", "空的场景选项名", false, null);

        var (entry, mode, modeMissing) = ResolveSpecEx(spec);
        if (entry is null)
        {
            var hint = _library.Count == 0
                ? "场景库为空"
                : "可用条目：" + string.Join("、", _library.Select(e => e.Name));
            return new SceneApplyResult("not_found", $"场景「{spec}」不存在（{hint}）", false, null);
        }
        if (modeMissing)
        {
            var slash = spec.IndexOf('/');
            var wanted = slash >= 0 ? spec[(slash + 1)..].Trim() : spec;
            var modes = entry.Modes.Count == 0
                ? "该条目还没有模式"
                : "可用模式：" + string.Join(" / ", entry.Modes.Select(x => x.Name));
            return new SceneApplyResult("not_found", $"「{entry.Name}」下没有模式「{wanted}」（{modes}）", false, null);
        }

        return await ApplyOptionAsync(entry, mode, source);
    }

    /// <summary>
    /// 解析「条目」或「条目/模式」。ModeMissing=true 表示写了模式名但该条目下没有——
    /// 旧实现会悄悄退到第一个模式，造成「切换成功」的谎报，这里必须显式区分。
    /// </summary>
    public (SceneLibraryEntry? Entry, SceneMode? Mode, bool ModeMissing) ResolveSpecEx(string spec)
    {
        var slash = spec.IndexOf('/');
        if (slash > 0)
        {
            var entryName = spec[..slash].Trim();
            var modeName = spec[(slash + 1)..].Trim();
            var e = FindByName(entryName);
            if (e is null) return (null, null, false);
            var m = e.Modes.FirstOrDefault(x => x.Name == modeName);
            return m is null ? (e, null, true) : (e, m, false);
        }
        return (FindByName(spec), null, false);
    }

    /// <summary>
    /// 旧签名兼容入口：模式名写错时退到该条目的第一个模式（保留旧行为）。
    /// 需要「模式名写错」这个信息请用 ResolveSpecEx，否则会把误切当成成功。
    /// </summary>
    public (SceneLibraryEntry? Entry, SceneMode? Mode) ResolveSpec(string spec)
    {
        var (e, m, _) = ResolveSpecEx(spec);
        return (e, m ?? e?.Modes.FirstOrDefault());
    }

    private SceneLibraryEntry? FindByName(string name)
    {
        // 库条目名 或 地图场景名（MapSceneRef 反向解析）
        var direct = _library.FirstOrDefault(e => e.Name == name);
        if (direct is not null) return direct;
        var byRef = _library.FirstOrDefault(e => e.MapSceneRef == name);
        return byRef;
    }

    // ============ 应用 ============

    /// <summary>兼容旧调用：auto=true 表示由时间触发。</summary>
    public async Task ApplyAsync(SceneLibraryEntry entry, SceneMode? mode, bool auto)
        => await ApplyOptionAsync(entry, mode, auto ? SceneApplySource.Time : SceneApplySource.Ai);

    /// <summary>
    /// 应用一个选项（条目 + 可选模式）。所有调用方（时间 Timer / AI 标记 / 内置工具 / UI）
    /// 都经 _applyLock 串行化：_active* 是跨线程共享状态，两个线程同时改会让回报与实际状态脱节。
    /// </summary>
    public async Task<SceneApplyResult> ApplyOptionAsync(SceneLibraryEntry entry, SceneMode? mode, SceneApplySource source)
    {
        // 门控只作**提醒**，不再阻止切换。
        // 拒绝会让 AI 在"用户说关灯、但现在是白天"这类场景下彻底无能为力——
        // 而模型自己也该有权判断合不合适。所以照切，并把"此刻不该用"如实告知。
        var gateNote = "";
        foreach (var gate in _gates)
        {
            if (gate.BlockReason(entry, mode, DateTime.Now) is { } reason)
            {
                gateNote = $"（注意：{reason}——这个选项通常不该在此刻使用，已按你的要求强制切换）";
                break;
            }
        }

        await _applyLock.WaitAsync();
        try
        {
            // 选项快照必须在改状态之前生成，AlreadyActive/可见变化才是「切换前」的口径
            var option = BuildOptionAt(entry, mode, DateTime.Now);
            var diff = _active is null || option.AlreadyActive
                ? new RenderDiff(true, "背景图", "")
                : Diff(CurrentRenderState(), ResolveRenderState(entry, mode));

            _active = entry;
            _activeMode = mode;
            _source = source;
            _appliedAt = DateTime.Now;

            // 1. 地图单场景引用：有 MapSceneRef 则让地图服务移动过去（触发 SceneChanged → ApplyScene）
            if (!string.IsNullOrEmpty(entry.MapSceneRef))
            {
                try
                {
                    var walk = await _map.MoveToAsync(entry.MapSceneRef);
                    if (!string.IsNullOrWhiteSpace(walk) && source != SceneApplySource.Time)
                        App.WriteLog("SceneDirector: walk note = " + walk);
                }
                catch (Exception ex)
                {
                    App.WriteLog("SceneDirector.MoveTo -> " + ex);
                }
            }

            // 2. 模式覆盖：亮度遮罩 / BGM（Bgm 为空 = 不改音乐）
            _activeDim = ResolveRenderState(entry, mode).Dim;
            var bgmWarning = "";
            if (mode is not null && !string.IsNullOrEmpty(mode.Bgm))
            {
                if (await PlayModeBgmAsync(mode.Bgm)) _activeBgm = mode.Bgm;
                // 换曲失败也要说出来：否则回报里「BGM 有变化」就是假的
                else bgmWarning = $"（注意：BGM「{mode.Bgm}」在素材库里找不到，实际没有换曲）";
            }

            // 3. 通知订阅方（纯库场景直接渲染；地图场景此时也已完成切换）
            await RaiseModeAppliedAsync(entry, mode);
            await SaveAsync();

            var suffix = source == SceneApplySource.Time ? "（时间规则自动触发）" : "";
            var status = !option.AlreadyActive && diff.Visual ? "applied" : "unchanged";
            var message = option.AlreadyActive
                ? $"「{option.Title}」已经是当前生效的选项，无需切换{suffix}{bgmWarning}{gateNote}"
                : diff.Report(option.Title) + suffix + bgmWarning + gateNote;
            App.WriteLog($"SceneDirector: {option.Spec} source={source} status={status} -> {diff.Fields}{bgmWarning}");
            return new SceneApplyResult(status, message, diff.Visual, option);
        }
        catch (Exception ex)
        {
            App.WriteLog("SceneDirector.Apply -> " + ex);
            return new SceneApplyResult("failed", $"切换「{DescribeOption(entry, mode)}」时出错：{ex.Message}", false, null);
        }
        finally
        {
            try { _applyLock.Release(); } catch (ObjectDisposedException) { /* 退出时已释放 */ }
        }
    }

    /// <summary>
    /// 播放模式 BGM。Bgm 存的是素材库音乐 Id（兼容旧数据里手写的绝对路径）。
    /// 返回是否真的播出去了——调用方要据此决定回报里能不能说「换了 BGM」。
    /// </summary>
    private async Task<bool> PlayModeBgmAsync(string bgm)
    {
        try
        {
            await _materials.EnsureLoadedAsync();
            var item = await _materials.FindMusicAsync(bgm);
            var abs = item is not null ? _materials.ResolveAbs(item.RelPath) : bgm;
            if (!Path.IsPathRooted(abs))
            {
                App.WriteLog($"SceneDirector.Bgm: 找不到音乐「{bgm}」（素材库无此 Id，也不是绝对路径）");
                return false;
            }
            _audio.PlaySfxFile(abs);
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("SceneDirector.Bgm -> " + ex);
            return false;
        }
    }

    /// <summary>
    /// ModeApplied 必须在 UI 线程派发：订阅方会直接改可绑定属性（背景、亮度遮罩）。
    /// 时间触发来自 Timer 线程，不切线程会在 WinUI 上抛跨线程访问。
    /// </summary>
    private async Task RaiseModeAppliedAsync(SceneLibraryEntry entry, SceneMode? mode)
    {
        var handler = ModeApplied;
        if (handler is null) return;
        try
        {
            if (MainThread.IsMainThread)
            {
                handler(entry, mode);
                return;
            }
            await MainThread.InvokeOnMainThreadAsync(() => handler(entry, mode));
        }
        catch (Exception ex)
        {
            App.WriteLog("SceneDirector.ModeApplied -> " + ex);
        }
    }

    private static string DescribeOption(SceneLibraryEntry entry, SceneMode? mode)
        => mode is null ? entry.Name : $"{entry.Name} · {mode.Name}";

    private static string SourceLabel(SceneApplySource source) => source switch
    {
        SceneApplySource.Time => "时间规则",
        SceneApplySource.Ai => "AI/指令",
        SceneApplySource.Manual => "手动切换",
        SceneApplySource.Weather => "天气规则",
        _ => "无"
    };

    // ============ AI 语境 ============

    /// <summary>
    /// 组装注入 ChatEngine 的场景语境：列出当前生效态、**此刻可用的选项**（含可用性理由与
    /// 「切了看不看得出变化」）以及变换规则，并说明【场景:条目/模式】标记的用法。
    /// 这是 AI 「访问选项」的主通道，先看表再选，不必靠试错。
    /// </summary>
    public string BuildContext()
    {
        if (_library.Count == 0) return "";

        var now = DateTime.Now;
        var options = BuildOptions(now);
        var available = options.Where(o => o.Available).ToList();
        var blocked = options.Where(o => !o.Available).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("【场景选项】你有一个场景库。想切换场景/氛围时，必须先看这份「此刻可用选项」再选：");
        sb.AppendLine("用【场景:条目名/模式名】标记（会被自动执行），或调用 set_scene_option 工具（会返回是否真的看得出变化）。");
        sb.AppendLine($"当前生效：{(_active is null ? "（无）" : DescribeOption(_active, _activeMode))}"
            + $"；来源：{SourceLabel(_source)}；当前时间 {now:HH:mm}，亮度遮罩 {_activeDim:0.00}。");

        if (available.Count == 0)
        {
            sb.AppendLine("此刻没有任何选项处于它自己的生效时段内。如果需要（例如用户明确要求），"
                + "你仍然可以从下面的选项里挑一个来切，只是要清楚它此刻通常不该生效。");
        }
        else
        {
            sb.AppendLine("此刻处于生效时段内的选项：");
            foreach (var o in available)
            {
                var cond = string.IsNullOrEmpty(o.AvailableReason) ? "" : $"（{o.AvailableReason}）";
                sb.AppendLine($"· {o.Spec}{(o.AlreadyActive ? "〔当前生效〕" : "")}{cond} —— {o.ChangeDetail}");
            }
        }

        if (blocked.Count > 0)
        {
            sb.AppendLine("此刻不在生效时段内的选项（仍可切换，但除非用户明确要求，否则别选——"
                + "切了会被如实告知「不符合当前时段」）：");
            foreach (var o in blocked)
                sb.AppendLine($"· {o.Spec} —— {o.UnavailableReason}");
        }

        var rules = _library.Where(e => !string.IsNullOrWhiteSpace(e.AiRules)).ToList();
        if (rules.Count > 0)
        {
            sb.AppendLine("条目变换规则：");
            foreach (var e in rules)
                sb.AppendLine($"· {e.Name}：{e.AiRules}");
        }

        sb.AppendLine("提示：标着「看不出变化」的选项切换后画面不会变（例如白天开灯、或已经是该状态），"
            + "这属于正常现象——如实说一句即可，不要反复重试，也不要宣称画面已经变了。");
        return sb.ToString();
    }

    // ============ 时间评估入口（手动/启动时） ============

    public async Task EvaluateTimeNowAsync() => await EvaluateTimeAsync();

    /// <summary>对外暴露的「当前天气」串（门控与 UI 共用），null 表示未知。</summary>
    public string? CurrentWeather => _currentWeather;

    /// <summary>手动触发一次天气刷新（设置改城市后可调）。</summary>
    public async Task RefreshWeatherNowAsync() => await RefreshWeatherAsync();

    /// <summary>
    /// 拉取一次天气并缓存其 Condition 串。异常一律吞掉：天气拿不到时门控按「未知」放行，
    /// 绝不能因为断网把整个场景库锁死。
    /// </summary>
    private async Task RefreshWeatherAsync()
    {
        if (_weather is null) return;
        try
        {
            var reading = await _weather.Fetch();
            if (reading is not null && !string.IsNullOrWhiteSpace(reading.Condition))
                _currentWeather = reading.Condition;
        }
        catch (Exception ex)
        {
            App.WriteLog("SceneDirector.RefreshWeather -> " + ex);
        }
    }

    public void Dispose()
    {
        _timeTimer?.Dispose();
        _applyLock.Dispose();
    }
}
