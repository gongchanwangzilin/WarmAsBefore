using System.Globalization;

namespace WarmAsBefore.Helpers;

/// <summary>
/// 状态文字的唯一映射：界面上的状态永远不直接显示枚举名。
///
/// 为什么需要它：状态在数据层是 <c>DeployStatus.Failed</c> 这样的标识符，页面里一旦
/// <c>ToString()</c> 兜底，用户就会看到「按本地账本计 · Failed」—— 一半中文一半英文，
/// 而且同一件事在三个页面能显示成三种写法（Failed / failed / 失败）。
/// 这里只保留一张表：所有页面查同一份，加一个状态就只加一行。
///
/// 认不出的标识符一律折成 <see cref="Unknown"/>，不把原始英文露出去：
/// 露出去等于把「这里没人翻过」写在界面上。带空格或已是中文的文字原样保留，
/// 因为那些是给人写的状态描述，不是标识符。
/// </summary>
public static class StatusText
{
    public const string Unknown = "—";

    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // 进度
        ["idle"] = "空闲",
        ["running"] = "进行中",
        ["queued"] = "排队中",
        ["pending"] = "等待中",
        ["waiting"] = "等待中",
        ["loading"] = "加载中",
        ["starting"] = "启动中",
        ["stopping"] = "停止中",
        ["downloading"] = "下载中",
        ["installing"] = "安装中",
        ["busy"] = "忙碌",
        ["paused"] = "已暂停",
        ["stopped"] = "已停止",
        ["cancelled"] = "已取消",
        ["canceled"] = "已取消",
        // 结果
        ["ready"] = "就绪",
        ["done"] = "已完成",
        ["completed"] = "已完成",
        ["success"] = "成功",
        ["succeeded"] = "成功",
        ["ok"] = "正常",
        ["warning"] = "警告",
        ["failed"] = "失败",
        ["failure"] = "失败",
        ["error"] = "出错",
        ["blocked"] = "受阻",
        ["expired"] = "已过期",
        // 开关
        ["enabled"] = "已启用",
        ["disabled"] = "已停用",
        ["active"] = "生效中",
        ["inactive"] = "未启用",
        ["installed"] = "已安装",
        ["uninstalled"] = "未安装",
        ["updated"] = "已更新",
        ["draft"] = "草稿",
        ["published"] = "已发布",
        ["scheduled"] = "已排期",
        // 连接
        ["online"] = "在线",
        ["offline"] = "离线",
        ["connected"] = "已连接",
        ["disconnected"] = "未连接",
        ["unknown"] = "未知",
        ["none"] = "无"
    };

    /// <summary>标识符 → 人话。空值给破折号，认不出的纯标识符也给破折号。</summary>
    public static string Format(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Unknown;
        var text = raw.Trim();
        if (Map.TryGetValue(text, out var mapped)) return mapped;
        // 带空格 / 已经是中文：这是描述，不是标识符，原样显示。
        if (text.Contains(' ') || text.Any(ch => ch > 0x2E80)) return text;
        return Unknown;
    }

    public static string Format(object? value) => value is null ? Unknown : Format(value.ToString());
}
