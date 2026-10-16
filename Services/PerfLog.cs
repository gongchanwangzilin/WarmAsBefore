using System.Diagnostics;

namespace WarmAsBefore.Services;

/// <summary>
/// 轻量性能日志：把「一次操作花了多久」写到 {数据根}/perf.log，用于定位卡顿。
///
/// 为什么要它：卡顿不能靠猜——点击一次按钮会牵动「保存设置 → 下发运行时配置 → 换主题画刷 → 重建界面」，
/// 到底慢在哪一段，只有打点才知道。日志按大小自动截断，开销可忽略（单行追加）。
/// 用 PerfLog.Measure("名字") 包住可疑代码即可。
/// </summary>
public static class PerfLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    /// <summary>慢于此毫秒数的段落会带 SLOW 标记，便于过滤。</summary>
    public static int SlowMs { get; set; } = 50;

    private static string Path_
    {
        get
        {
            if (_path is not null) return _path;
            try { _path = System.IO.Path.Combine(App.RootDirectory, "perf.log"); }
            catch { _path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "warm_perf.log"); }
            return _path;
        }
    }

    public static void Note(string label, string detail = "")
    {
        Write($"{label}{(detail.Length > 0 ? "  " + detail : "")}");
    }

    /// <summary>测量一段耗时；用 using 包住即可：using (PerfLog.Measure("设置页.OnAppearing")) { ... }</summary>
    public static Scope Measure(string label) => new(label);

    public sealed class Scope : IDisposable
    {
        private readonly string _label;
        private readonly long _start;
        internal Scope(string label)
        {
            _label = label;
            _start = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            var ms = (Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency;
            var flag = ms >= SlowMs ? "  <<< SLOW" : "";
            Write($"{_label}  {ms:0.0} ms{flag}");
        }
    }

    private static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                var path = Path_;
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    // 截断保留最近一半：诊断用，不需要历史
                    var tail = File.ReadAllLines(path);
                    File.WriteAllLines(path, tail.Skip(tail.Length / 2));
                }
                File.AppendAllText(path,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {line}{Environment.NewLine}");
            }
        }
        catch
        {
            // 性能日志本身绝不能影响主流程
        }
    }
}
