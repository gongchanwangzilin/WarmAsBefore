using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using WarmAsBefore.Modules.SaveSystem;

namespace WarmAsBefore.Services.Core;

/// <summary>
/// 修复回执的当前快照（2026-11-14，容错自愈）。
/// 只存「本次读档修了什么、可能丢了什么」这一份，供目标页（MainGamePage）常驻回执条显示：
/// 修复成功后仍自动进入游戏（用户诉求「修复后直接进入」），报告以回执条随目标页保留，
/// 不能再靠 Toast —— Toast 会自动消失，用户还没看清就没了。
///
/// 用单例 + INotifyPropertyChanged：XAML 侧 BindingContext 直接绑
/// <c>{x:Static core:RepairReportService.Instance}</c>，不必改 MainGameViewModel。
/// </summary>
public sealed class RepairReportService : INotifyPropertyChanged
{
    public static RepairReportService Instance { get; } = new();

    private RepairReportService() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
    {
        try { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
        catch { /* 通知失败不能影响修复主流程 */ }
    }

    private SaveRepairReport? _report;
    private bool _expanded;

    public SaveRepairReport? Current => _report;

    public bool HasReceipt => _report is not null && _report.NeedsReceipt;
    public string ReceiptSummary => _report?.ReceiptSummary ?? "";
    public string ReceiptBadge => _report?.BadgeText ?? "";
    public string ReceiptTintKey => _report?.BadgeTintKey ?? "InfoTint";
    public string DetailIncident => _report?.IncidentSummary ?? "";
    public string WriteBackNote => _report?.WriteBackNote ?? "";
    public IReadOnlyList<string> LostItems => _report?.LostItems ?? (IReadOnlyList<string>)Array.Empty<string>();
    public IReadOnlyList<string> FixedItems => _report?.FixedItems ?? (IReadOnlyList<string>)Array.Empty<string>();

    /// <summary>详情整段（已修复 + 可能丢失 + 回写说明），一个 Label 即可绑定，避免新增转换器。</summary>
    public string DetailText
    {
        get
        {
            if (_report is null) return "";
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(_report.IncidentSummary)) sb.AppendLine(_report.IncidentSummary);
            if (_report.FixedItems.Count > 0)
            {
                sb.AppendLine("已自动修复：");
                foreach (var f in _report.FixedItems) sb.AppendLine("  · " + f);
            }
            if (_report.LostItems.Count > 0)
            {
                sb.AppendLine("可能丢失：");
                foreach (var l in _report.LostItems) sb.AppendLine("  · " + l);
            }
            if (!string.IsNullOrEmpty(_report.WriteBackNote)) sb.AppendLine(_report.WriteBackNote);
            return sb.ToString().TrimEnd();
        }
    }

    private ICommand? _toggleCmd, _clearCmd;
    public ICommand ToggleExpandedCommand => _toggleCmd ??= new Command(ToggleExpanded);
    public ICommand ClearCommand => _clearCmd ??= new Command(Clear);

    /// <summary>展开态：详情只切子节点 IsVisible，不重建任何控件。</summary>
    public bool IsExpanded => _expanded;

    /// <summary>详情区当前是否可见（HasReceipt && IsExpanded），XAML 一处绑定即可。</summary>
    public bool DetailVisible => HasReceipt && _expanded;

    /// <summary>显示一份修复回执（修复成功自动进入后调用）。</summary>
    public void Show(SaveRepairReport report)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _expanded = false;
        Raise(nameof(HasReceipt));
        Raise(nameof(ReceiptSummary));
        Raise(nameof(ReceiptBadge));
        Raise(nameof(ReceiptTintKey));
        Raise(nameof(DetailIncident));
        Raise(nameof(WriteBackNote));
        Raise(nameof(LostItems));
        Raise(nameof(FixedItems));
        Raise(nameof(DetailText));
        Raise(nameof(IsExpanded));
        Raise(nameof(DetailVisible));
    }

    /// <summary>用户点「收起」后清空回执。</summary>
    public void Clear()
    {
        _report = null;
        _expanded = false;
        Raise(nameof(HasReceipt));
        Raise(nameof(ReceiptSummary));
        Raise(nameof(ReceiptBadge));
        Raise(nameof(ReceiptTintKey));
        Raise(nameof(DetailIncident));
        Raise(nameof(WriteBackNote));
        Raise(nameof(LostItems));
        Raise(nameof(FixedItems));
        Raise(nameof(DetailText));
        Raise(nameof(IsExpanded));
        Raise(nameof(DetailVisible));
    }

    /// <summary>展开/收起明细。</summary>
    public void ToggleExpanded()
    {
        if (_report is null) return;
        _expanded = !_expanded;
        Raise(nameof(IsExpanded));
        Raise(nameof(DetailVisible));
    }
}
