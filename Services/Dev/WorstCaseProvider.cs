using System;

namespace WarmAsBefore.Services.Dev;

/// <summary>
/// 压测数据的档位。Demo 是正式数据（什么都不换）；其余四档把边界上的数据源整个换成
/// <see cref="WorstCaseFixtures"/> 里的假数据，用来把界面逼到最坏情况。
/// </summary>
public enum WorstCaseMode
{
    /// <summary>正式数据。默认值，也是唯一会影响真实用户的档位。</summary>
    Demo,
    /// <summary>最坏但真实：超长姓名、无空格邮箱、单字名、混排文字、极端计数、缺头像。</summary>
    Worst,
    /// <summary>空：零条会话、零条日程、零个记忆节点。</summary>
    Empty,
    /// <summary>恰好一条：查单复数与「1 条记忆」这类文案。</summary>
    One,
    /// <summary>量级上限：1000 条会话、96 条日程、480 个节点。</summary>
    Huge
}

/// <summary>页面模型实现它，就能在 dev 压测条上直接换档重读，不必重开应用。</summary>
public interface IWorstCaseAware
{
    void ReloadWorstCase();
}

/// <summary>
/// 只读 dev 开关：<c>--worst=demo|worst|empty|one|huge</c>（或环境变量 <c>WAB_WORST</c>）。
///
/// 三条硬边界：
/// 1. 只在 Debug 构建里生效，Release 永远返回 <see cref="WorstCaseMode.Demo"/>；
/// 2. 打开后各页面只读 fixture、不写盘、不走 AI（正式数据一个字节都不会被碰）；
/// 3. 没传这个参数时 <see cref="EnabledAtLaunch"/> 为 false，页面上连压测条都不出现。
/// </summary>
public static class WorstCaseProvider
{
#if DEBUG
    private const bool IsDevBuild = true;
#else
    private const bool IsDevBuild = false;
#endif

    private static readonly (WorstCaseMode Mode, bool Enabled) Startup = Detect();

    private static WorstCaseMode _mode = Startup.Mode;

    /// <summary>启动时是否带了这个开关（带 <c>--worst=demo</c> 也算：压测条要能切到其它档）。</summary>
    public static bool EnabledAtLaunch => Startup.Enabled;

    public static WorstCaseMode Mode => _mode;

    /// <summary>true = 数据源该换成 fixture 了。所有注入点只看这一个判断。</summary>
    public static bool IsActive => _mode != WorstCaseMode.Demo;

    /// <summary>换档（只有压测条会调）。</summary>
    public static event Action? ModeChanged;

    public static void SetMode(WorstCaseMode mode)
    {
        if (!EnabledAtLaunch || _mode == mode) return;
        _mode = mode;
        try { App.WriteLog("WorstCaseProvider -> " + Name(mode)); } catch { }
        try { MainThread.BeginInvokeOnMainThread(() => ModeChanged?.Invoke()); } catch { ModeChanged?.Invoke(); }
    }

    public static IReadOnlyList<WorstCaseMode> AllModes { get; } = new[]
    {
        WorstCaseMode.Demo, WorstCaseMode.Worst, WorstCaseMode.Empty, WorstCaseMode.One, WorstCaseMode.Huge
    };

    public static string Name(WorstCaseMode mode) => mode switch
    {
        WorstCaseMode.Worst => "Worst",
        WorstCaseMode.Empty => "Empty",
        WorstCaseMode.One => "One",
        WorstCaseMode.Huge => "Huge",
        _ => "Demo"
    };

    private static (WorstCaseMode Mode, bool Enabled) Detect()
    {
        if (!IsDevBuild) return (WorstCaseMode.Demo, false);
        var raw = ReadRaw();
        if (string.IsNullOrWhiteSpace(raw)) return (WorstCaseMode.Demo, false);
        var mode = Parse(raw);
        try { App.WriteLog("WorstCaseProvider: 压测数据 = " + Name(mode) + "（" + raw.Trim() + "）"); } catch { }
        return (mode, true);
    }

    private static string? ReadRaw()
    {
        try
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i] ?? "";
                if (a.StartsWith("--worst=", StringComparison.OrdinalIgnoreCase)) return a[8..];
                if (string.Equals(a, "--worst", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) return args[i + 1];
            }
        }
        catch { /* 取不到命令行就当没传 */ }
        try { return Environment.GetEnvironmentVariable("WAB_WORST"); }
        catch { return null; }
    }

    private static WorstCaseMode Parse(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "worst" or "worstcase" or "worst-case" => WorstCaseMode.Worst,
        "empty" or "none" or "zero" => WorstCaseMode.Empty,
        "one" or "single" or "1" => WorstCaseMode.One,
        "huge" or "large" or "1000" => WorstCaseMode.Huge,
        _ => WorstCaseMode.Demo
    };
}
