using System.Text;
using System.Text.RegularExpressions;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Scene;

/// <summary>
/// 场景导演：场景库的统一执行入口。
/// 职责：
///  1. 加载/持久化场景库（scene_library.json）；
///  2. 时间触发：按模式的 TimeRanges 在整点/应用启动时评估并应用（关灯=夜间夜灯）；
///  3. AI 指令触发：解析 AI 回复中的【场景:名/模式】标记，执行切换；
///  4. 组装注入 ChatEngine 的场景语境（BuildSceneContext，含 AiRules 变换规则）；
///  5. 应用模式：切换地图场景引用、背景/背景色、灯光基调、BGM。
/// 与地图页的「单场景直接可用」打通：MapSceneRef 为空时用库条目的 Background 直接渲染。
/// </summary>
public sealed class SceneDirector : IDisposable
{
    private readonly StorageProvider _store;
    private readonly MapService _map;
    private readonly AudioController _audio;
    private readonly Timer _timeTimer;

    private List<SceneLibraryEntry> _library = new();
    private SceneLibraryEntry? _active;
    private SceneMode? _activeMode;
    private bool _timeApplied;

    private static readonly Regex SceneMarkerRe =
        new(@"【场景:([^】]+)】", RegexOptions.Compiled);

    public SceneDirector(StorageProvider store, MapService map, AudioController audio)
    {
        _store = store;
        _map = map;
        _audio = audio;
        _timeTimer = new Timer(_ => _ = EvaluateTimeAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5));
        _ = LoadAsync();
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
        if (_active?.Id == entryId) { _active = null; _activeMode = null; }
    }

    public SceneMode AddMode(SceneLibraryEntry entry, string name)
    {
        var m = new SceneMode { Name = name };
        entry.Modes.Add(m);
        return m;
    }

    // ============ 时间触发 ============

    public event Action<SceneLibraryEntry, SceneMode>? ModeApplied;

    private async Task EvaluateTimeAsync()
    {
        try
        {
            var now = DateTime.Now;
            var matched = FindModeByTime(now);
            if (matched is { } m)
            {
                if (!ReferenceEquals(_activeMode, m.Mode))
                    await ApplyAsync(m.Entry, m.Mode, auto: true);
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("SceneDirector.EvaluateTime -> " + ex);
        }
    }

    /// <summary>按当前时间扫描库中所有模式的 TimeRanges，返回第一个命中条目+模式。</summary>
    private TimeMatchResult? FindModeByTime(DateTime now)
    {
        foreach (var e in _library)
        {
            foreach (var m in e.Modes)
            {
                if (TimeRangeMatches(m.TimeRanges, now))
                    return new TimeMatchResult(e, m);
            }
        }
        return null;
    }

    internal sealed record TimeMatchResult(SceneLibraryEntry Entry, SceneMode Mode);

    internal static bool TimeRangeMatches(List<string> ranges, DateTime now)
    {
        if (ranges is null || ranges.Count == 0) return false;
        var cur = now.TimeOfDay;
        foreach (var r in ranges)
        {
            var parts = r.Split('-');
            if (parts.Length != 2) continue;
            if (!TryParseTime(parts[0], out var from) || !TryParseTime(parts[1], out var to)) continue;
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
    /// 返回被剥离标记后的回复与执行结果摘要（可附在回复末尾）。
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
            var (entry, mode) = ResolveSpec(spec);
            if (entry is null)
            {
                notes.Add($"（场景 {spec} 不存在）");
                continue;
            }
            await ApplyAsync(entry, mode, auto: false);
            notes.Add($"（已切换到「{entry.Name}{(mode is null ? "" : " · " + mode.Name)}」）");
        }
        var clean = SceneMarkerRe.Replace(reply, "").Trim();
        return (clean, string.Join("\n", notes));
    }

    /// <summary>解析「条目」或「条目/模式」。</summary>
    public (SceneLibraryEntry? Entry, SceneMode? Mode) ResolveSpec(string spec)
    {
        var slash = spec.IndexOf('/');
        if (slash > 0)
        {
            var entryName = spec[..slash].Trim();
            var modeName = spec[(slash + 1)..].Trim();
            var e = FindByName(entryName);
            if (e is null) return (null, null);
            var m2 = e.Modes.FirstOrDefault(x => x.Name == modeName);
            return (e, m2 ?? e.Modes.FirstOrDefault());
        }
        return (FindByName(spec), null);
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

    /// <summary>应用库条目（可选模式）。auto=true 表示由时间触发。</summary>
    public async Task ApplyAsync(SceneLibraryEntry entry, SceneMode? mode, bool auto)
    {
        _active = entry;
        _activeMode = mode;
        _timeApplied = auto;

        // 1. 地图单场景引用：有 MapSceneRef 则让地图服务移动过去（触发 SceneChanged → ApplyScene）
        if (!string.IsNullOrEmpty(entry.MapSceneRef))
        {
            try
            {
                var walk = await _map.MoveToAsync(entry.MapSceneRef);
                if (!string.IsNullOrWhiteSpace(walk) && !auto)
                    App.WriteLog("SceneDirector: walk note = " + walk);
            }
            catch (Exception ex)
            {
                App.WriteLog("SceneDirector.MoveTo -> " + ex);
            }
        }
        else
        {
            // 纯库场景：直接发事件让页面渲染背景
            ModeApplied?.Invoke(entry, mode);
        }

        // 2. 模式覆盖：背景/色/BGM
        if (mode is not null)
        {
            if (!string.IsNullOrEmpty(mode.Bgm))
            {
                try { _audio.PlaySfxFile(mode.Bgm); }
                catch (Exception ex) { App.WriteLog("SceneDirector.Bgm -> " + ex); }
            }
        }

        ModeApplied?.Invoke(entry, mode);
        await SaveAsync();
    }

    // ============ AI 语境 ============

    /// <summary>
    /// 组装注入 ChatEngine 的场景语境：列出当前库条目、可用模式与变换规则，
    /// 并说明【场景:条目/模式】标记的用法。
    /// </summary>
    public string BuildContext()
    {
        if (_library.Count == 0) return "";
        var sb = new StringBuilder();
        sb.AppendLine("你拥有一个场景库，可用【场景:条目名/模式名】标记主动切换场景与氛围（标记会被自动执行）。");
        foreach (var e in _library)
        {
            var modes = e.Modes.Count == 0
                ? ""
                : "，模式：" + string.Join(" / ", e.Modes.Select(m => m.Name));
            sb.AppendLine($"· {e.Name}{modes}");
            if (!string.IsNullOrWhiteSpace(e.AiRules))
                sb.AppendLine($"  规则：{e.AiRules}");
        }
        sb.AppendLine("在合适时机（如下雨、深夜「关灯」、节日）主动提议或直接用标记切换。");
        return sb.ToString();
    }

    // ============ 时间评估入口（手动/启动时） ============

    public async Task EvaluateTimeNowAsync() => await EvaluateTimeAsync();

    public void Dispose()
    {
        _timeTimer?.Dispose();
    }
}
