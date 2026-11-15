using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using WarmAsBefore.Controls;
using WarmAsBefore.Helpers;

namespace WarmAsBefore.Services.Core;

/// <summary>
/// 全局兜底：任何未处理异常都不再直接杀死进程，而是写一份结构化 bug 日志、
/// （可选地）弹出一个 bug 日志弹窗、把出问题的子系统标记为「已停止运行」（封存），避免反复崩溃。
///
/// 双通道（用户反馈 + 开发者反馈）：
///  · 用户侧：弹窗里看得到「发生了什么 / 已停用的子系统」，可复制日志、可收起；
///  · 开发者侧：每一次异常都落盘到 bugs.log，并同步追加进 feedback_outbox.json（结构化，
///    便于回传给开发者），与弹窗解耦——就算弹窗因界面未就绪退化为 Toast，落盘照常。
///
/// 用法：
///  - App.xaml.cs / Program.cs 的全局异常钩子调用 <see cref="Report"/> 作为最后一道网；
///  - 各子系统在 catch 到「非良性」异常时主动调用 <see cref="Report"/> 并自行停止该部分；
///  - 已知可自愈的良性异常用 <see cref="Report(Exception?, string?, string?, bool)"/> 的
///    recordOnly:true —— 只记录不弹窗、不封存子系统；
///  - 子系统重启前先查 <see cref="IsStopped"/>，已停用的不再拉起；<see cref="Reset"/> 恢复。
///
/// 本类不依赖 DI，纯静态、自吞一切内部异常——兜底机制自己绝不能成为新的崩溃源。
/// </summary>
public static class BugReporter
{
    private static readonly object _gate = new();
    private static readonly HashSet<string> _stopped = new();
    private static readonly Dictionary<string, (DateTime last, int count)> _recent = new();

    private static string BugLogPath => Path.Combine(App.RootDirectory, "bugs.log");
    private static string FeedbackOutboxPath => Path.Combine(App.RootDirectory, "feedback_outbox.json");

    /// <summary>某子系统是否已在本次会话内因故障被停用（封存）。</summary>
    public static bool IsStopped(string? subsystem)
    {
        if (string.IsNullOrWhiteSpace(subsystem)) return false;
        lock (_gate) return _stopped.Contains(subsystem!);
    }

    /// <summary>封存某子系统：之后不再自动运行，直到进程重启或显式 <see cref="Reset"/>。</summary>
    public static void Stop(string? subsystem)
    {
        if (string.IsNullOrWhiteSpace(subsystem)) return;
        lock (_gate) _stopped.Add(subsystem!);
    }

    /// <summary>解除某子系统的封存（用户手动重新启用 / 开发者恢复入口）。</summary>
    public static void Reset(string? subsystem)
    {
        if (string.IsNullOrWhiteSpace(subsystem)) return;
        lock (_gate) _stopped.Remove(subsystem!);
    }

    /// <summary>只记录、不弹窗、不封存子系统（已知可自愈的良性异常走这条）。</summary>
    public static void Report(Exception? ex, string? subsystem = null, string? context = null)
        => Report(ex, subsystem, context, recordOnly: false);

    /// <summary>
    /// 上报一个 bug。会：写 bugs.log、必要时弹 bug 日志窗（带去重）、追加 feedback_outbox.json。
    /// <paramref name="recordOnly"/>=true 时只记录与落盘反馈箱，不弹窗、不封存子系统。
    /// 传 null 异常时直接返回，不抛。
    /// </summary>
    public static void Report(Exception? ex, string? subsystem, string? context, bool recordOnly)
    {
        if (ex is null) return;
        var when = DateTime.Now;
        try { AppendLog(when, subsystem, context, ex); }
        catch { /* 日志落盘失败也不能让兜底崩 */ }

        try { AppendFeedback(when, subsystem, context, ex); }
        catch { }

        if (recordOnly) return;

        if (!string.IsNullOrWhiteSpace(subsystem))
            Stop(subsystem);

        // 同一处错误 5 秒内只弹一次窗，避免一帧一抛把界面刷爆；重复次数合并显示。
        var sig = $"{subsystem}|{ex.GetType().FullName}|{context ?? ""}";
        int count;
        bool show;
        lock (_gate)
        {
            if (_recent.TryGetValue(sig, out var v))
            {
                if ((when - v.last).TotalSeconds > 5)
                {
                    _recent[sig] = (when, 1);
                    count = 1;
                    show = true;
                }
                else
                {
                    _recent[sig] = (v.last, v.count + 1);
                    count = v.count + 1;
                    show = false;
                }
            }
            else
            {
                _recent[sig] = (when, 1);
                count = 1;
                show = true;
            }
        }

        if (show)
        {
            // 跨线程（含线程池 / 托盘回调）一律走 Ui.Post，禁 MainThread.*（取不到 DispatcherQueue 即崩进程）。
            Ui.Post(() => ShowPopup(when, subsystem, context, ex, count));
        }
    }

    private static void AppendLog(DateTime when, string? subsystem, string? context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(App.RootDirectory);
            var sb = new StringBuilder();
            sb.AppendLine($"[{when:yyyy-MM-dd HH:mm:ss.fff}] subsystem={(subsystem ?? "?")} context={(context ?? "?")}");
            AppendException(sb, ex, 0);
            sb.AppendLine("--------------------------------------------------");
            File.AppendAllText(BugLogPath, sb.ToString());
        }
        catch { }
    }

    /// <summary>开发者反馈箱：结构化追加（JSONL），与弹窗解耦，便于回传给开发者。</summary>
    private static void AppendFeedback(DateTime when, string? subsystem, string? context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(App.RootDirectory);
            var entry = new
            {
                at = when.ToString("o"),
                subsystem = subsystem ?? "",
                context = context ?? "",
                type = ex.GetType().FullName,
                message = ex.Message,
                stopped = !string.IsNullOrWhiteSpace(subsystem) && IsStopped(subsystem),
                stack = ex.ToString()
            };
            File.AppendAllText(FeedbackOutboxPath, JsonSerializer.Serialize(entry) + "\n");
        }
        catch { }
    }

    private static void AppendException(StringBuilder sb, Exception ex, int depth)
    {
        if (ex is null || depth > 5) return;
        sb.AppendLine($"  [{depth}] {ex.GetType().FullName}: {ex.Message}");
        sb.AppendLine(ex.StackTrace ?? "  (no stacktrace)");
        if (ex is AggregateException ae)
            foreach (var ie in ae.InnerExceptions) AppendException(sb, ie, depth + 1);
        else if (ex.InnerException is not null)
            AppendException(sb, ex.InnerException, depth + 1);
    }

    private static string BuildDetail(DateTime when, string? subsystem, string? context, Exception ex, int count, string feedbackPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"时间：{when:yyyy-MM-dd HH:mm:ss.fff}");
        sb.AppendLine($"子系统：{subsystem ?? "（未知）"}");
        sb.AppendLine($"位置：{context ?? "（未指明）"}");
        if (count > 1) sb.AppendLine($"本次会话同一错误已发生 {count} 次（已合并显示）");
        sb.AppendLine($"异常：{ex.GetType().FullName}");
        sb.AppendLine($"消息：{ex.Message}");
        if (!string.IsNullOrWhiteSpace(subsystem))
            sb.AppendLine($"\n⚠ 子系统「{subsystem}」已停止运行，以避免反复崩溃。需要恢复请到设置中重新启用，或重启应用。");
        sb.AppendLine($"\n开发者反馈已记录到：{Path.GetFileName(feedbackPath)}（可在「查看日志」里找到完整路径）");
        sb.AppendLine("\n----- 堆栈 -----");
        sb.AppendLine(ex.ToString());
        return sb.ToString();
    }

    /// <summary>从应用资源字典取令牌色；取不到时退回系统色（绝不写死 HEX）。</summary>
    private static Color Token(string key, Color fallback)
    {
        try { if (Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c) return c; }
        catch { }
        return fallback;
    }

    private static void ShowPopup(DateTime when, string? subsystem, string? context, Exception ex, int count)
    {
        Page? page = null;
        try
        {
            page = (Shell.Current?.CurrentPage as Page)
                   ?? (Application.Current?.Windows?.FirstOrDefault()?.Page as Page);
        }
        catch { page = null; }

        var detail = BuildDetail(when, subsystem, context, ex, count, FeedbackOutboxPath);

        if (page is null)
        {
            // 还没有任何页面能挂弹窗（极早期崩溃）：退化为 Toast，至少不让用户毫无知觉。
            try { Toast.Error($"捕获到异常（{subsystem ?? "应用"}）：{ex.GetType().Name} —— 详见 bugs.log"); }
            catch { }
            return;
        }

        try
        {
            var mono = new Label
            {
                Text = detail,
                FontSize = 12,
                LineBreakMode = LineBreakMode.WordWrap,
                TextColor = Token("SurfaceFg", Colors.White),
                FontFamily = "Consolas, Menlo, Monaco, monospace",
                Padding = new Thickness(2)
            };
            var scroll = new ScrollView
            {
                Content = mono,
                MaximumHeightRequest = 360,
                Padding = new Thickness(4)
            };

            var copyBtn = new Button
            {
                Text = "复制日志",
                TextColor = Token("AccentWarmFg", Colors.White),
                BackgroundColor = Token("AccentWarm", Colors.DodgerBlue),
                CornerRadius = 6,
                HeightRequest = 38
            };
            var closeBtn = new Button
            {
                Text = "关闭",
                TextColor = Token("SurfaceFg", Colors.White),
                BackgroundColor = Token("SurfaceBg", Colors.Gray),
                CornerRadius = 6,
                HeightRequest = 38
            };

            // 弱模态：可点外部关闭（这是错误提示不是锁死，用户不应被卡住），与全站「不铺全屏 scrim 锁死」一致。
            var popup = new CommunityToolkit.Maui.Views.Popup
            {
                Color = Colors.Black.WithAlpha(0.5f),
                CanBeDismissedByTappingOutsideOfPopup = true,
                Content = new Border
                {
                    Stroke = Token("DangerTint", Colors.DarkRed),
                    StrokeThickness = 2,
                    BackgroundColor = Token("SurfaceBg", Colors.Black),
                    Padding = new Thickness(16),
                    Content = new VerticalStackLayout
                    {
                        Spacing = 12,
                        Children =
                        {
                            new Label
                            {
                                Text = "⚠ 捕获到未处理异常（已记录到 bugs.log）",
                                TextColor = Token("DangerTint", Colors.OrangeRed),
                                FontSize = 16,
                                FontAttributes = FontAttributes.Bold
                            },
                            scroll,
                            new Grid
                            {
                                ColumnDefinitions = new ColumnDefinitionCollection
                                {
                                    new ColumnDefinition(GridLength.Star),
                                    new ColumnDefinition(GridLength.Star)
                                },
                                ColumnSpacing = 10,
                                Children =
                                {
                                    copyBtn,
                                    closeBtn
                                }
                            }
                        }
                    }
                }
            };

            copyBtn.Clicked += async (_, __) =>
            {
                try
                {
                    await Clipboard.Default.SetTextAsync(detail);
                    try { Toast.Success("日志已复制到剪贴板"); } catch { }
                }
                catch { try { Toast.Error("复制失败"); } catch { } }
            };
            closeBtn.Clicked += (_, __) => popup.Close();

            page.ShowPopup(popup);
        }
        catch
        {
            // 弹窗都建不出来：退回 Toast，绝不在兜底路径上二次崩溃。
            try { Toast.Error($"捕获到异常（{subsystem ?? "应用"}）：{ex.GetType().Name} —— 详见 bugs.log"); }
            catch { }
        }
    }
}
