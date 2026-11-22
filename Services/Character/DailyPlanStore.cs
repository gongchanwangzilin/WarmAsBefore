using WarmAsBefore.Models;
using WarmAsBefore.Modules.Tools;

namespace WarmAsBefore.Services;

/// <summary>
/// 日程落盘 helper：只负责「按角色把日程字典读写到独立 JSON」。
///
/// 省 token 设计的落盘侧：一天只写一次（生成时）+ 每次 Tick 有状态变化才写，
/// 不做实时落盘；文件按角色分，互不覆盖，跟存档走（MemoryScope 隔离）。
///
/// 依赖尽量从 ServicesHelper 惰性取（与 ImageGenerationService 等一致）；
/// 取不到就退回一个新 StorageProvider（其根目录就是 App.RootDirectory），
/// 保证监测页/后台在任何启动时序下都能安全读写，绝不因拿不到容器而崩。
/// </summary>
public sealed class DailyPlanStore
{
    private StorageProvider? _provider;
    private MemoryScope? _scope;
    private bool _resolved;

    private StorageProvider Provider
    {
        get
        {
            Resolve();
            return _provider ??= new StorageProvider();
        }
    }

    private MemoryScope? Scope
    {
        get { Resolve(); return _scope; }
    }

    private void Resolve()
    {
        if (_resolved) return;
        _resolved = true;
        try
        {
            var services = ServicesHelper.GetServices();
            _provider = services?.GetService(typeof(StorageProvider)) as StorageProvider;
            _scope = services?.GetService(typeof(MemoryScope)) as MemoryScope;
        }
        catch (Exception ex)
        {
            App.WriteLog("DailyPlanStore.Resolve -> " + ex.Message);
        }
    }

    /// <summary>当前作用域代号（存档号 / shared）。换档后token变化，服务据此丢缓存重读。</summary>
    public string ScopeToken
    {
        get
        {
            try { return Scope?.Current ?? "shared"; }
            catch { return "shared"; }
        }
    }

    /// <summary>角色 id 归一成安全文件名段（charId 理论上来自花名册，仍防一手）。</summary>
    private static string SafeSegment(string? charId)
    {
        var s = (charId ?? "").Trim();
        if (s.Length == 0) return "anonymous";
        var chars = s.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray();
        return new string(chars);
    }

    /// <summary>
    /// 存储键：跟存档走（MemoryScope.Key 会加存档号前缀），再按角色分一层。
    /// 共用作用域时形如 daily-plan/&lt;charId&gt;，隔离时形如 daily-plan/&lt;存档号&gt;/&lt;charId&gt;。
    /// </summary>
    public string KeyFor(string charId)
    {
        var raw = "daily-plan/" + SafeSegment(charId);
        try { return Scope is { } scope ? scope.Key(raw) : raw; }
        catch { return raw; }
    }

    /// <summary>供日志/回报用的落盘绝对路径（实际写盘仍走 StorageProvider）。</summary>
    public string PathFor(string charId)
    {
        try
        {
            var p = Provider.PathOf(KeyFor(charId));
            if (!string.IsNullOrWhiteSpace(p)) return p;
        }
        catch { /* 故意忽略：取不到存储根就退回应用根目录 */ }
        return System.IO.Path.Combine(App.RootDirectory, KeyFor(charId) + ".json");
    }

    public async Task<Dictionary<string, DailyPlan>> LoadAsync(string charId)
    {
        try
        {
            var dict = await Provider.Load<Dictionary<string, DailyPlan>>(KeyFor(charId));
            if (dict is not null) return dict;
        }
        catch (Exception ex)
        {
            App.WriteLog("DailyPlanStore.Load -> " + ex.Message);
        }
        return new Dictionary<string, DailyPlan>(StringComparer.Ordinal);
    }

    public async Task SaveAsync(string charId, Dictionary<string, DailyPlan> byDate)
    {
        try
        {
            await Provider.Save(KeyFor(charId), byDate);
        }
        catch (Exception ex)
        {
            App.WriteLog("DailyPlanStore.Save -> " + ex.Message);
        }
    }
}
