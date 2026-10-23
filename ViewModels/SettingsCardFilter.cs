using CommunityToolkit.Mvvm.ComponentModel;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 设置页的卡片可见性：搜索时只留下命中的卡片，其余收起。
///
/// 为什么按「卡片」而不是「单个设置项」过滤：设置页每张卡片是一个完整主题，
/// 连带说明文字一起保留才看得懂；命中卡片里再逐条挑反而更难读。
/// </summary>
public sealed partial class SettingsCardFilter : ObservableObject
{
    [ObservableProperty] private bool _volume = true;
    [ObservableProperty] private bool _glass = true;
    [ObservableProperty] private bool _glassLight = true;
    [ObservableProperty] private bool _background = true;
    [ObservableProperty] private bool _sandbox = true;
    [ObservableProperty] private bool _interface = true;
    [ObservableProperty] private bool _minigame = true;
    [ObservableProperty] private bool _chess = true;
    [ObservableProperty] private bool _textSfx = true;
    [ObservableProperty] private bool _ai = true;
    [ObservableProperty] private bool _memory = true;
    [ObservableProperty] private bool _voice = true;
    [ObservableProperty] private bool _notify = true;
    [ObservableProperty] private bool _weather = true;
    [ObservableProperty] private bool _desktop = true;
    [ObservableProperty] private bool _official = true;
    [ObservableProperty] private bool _mcp = true;
    [ObservableProperty] private bool _runtime = true;
    [ObservableProperty] private bool _data = true;
    [ObservableProperty] private bool _update = true;
    [ObservableProperty] private bool _experimental = true;
    [ObservableProperty] private bool _materials = true;
    [ObservableProperty] private bool _skills = true;
    [ObservableProperty] private bool _sponsor = true;
    [ObservableProperty] private bool _developer = true;

    /// <summary>卡片关键词：搜索时按这些词匹配（含同义词，免得「记忆」搜不到「回忆」）。</summary>
    private static readonly (string Key, string[] Words)[] Table =
    {
        ("Volume", new[] { "音量", "声音", "音乐", "音效", "bgm", "sfx", "按键音" }),
        ("Glass", new[] { "玻璃", "毛玻璃", "磨砂", "液态", "透明", "glass", "frost" }),
        ("GlassLight", new[] { "光源", "高光", "投影", "光照" }),
        ("Background", new[] { "背景", "背景图", "页面背景", "主题色" }),
        ("Sandbox", new[] { "工具安全", "沙箱", "信任", "权限", "安全", "sandbox" }),
        ("Interface", new[] { "界面", "布局", "方向", "横屏", "竖屏", "聊天风格", "气泡", "galgame" }),
        ("Minigame", new[] { "小游戏", "难度", "棋" }),
        ("Chess", new[] { "棋力", "棋力脑", "下棋", "围棋", "国际象棋" }),
        ("TextSfx", new[] { "文字音效", "打字音", "文字声音" }),
        ("Ai", new[] { "ai", "模型", "端点", "厂商", "密钥", "api", "温度", "超时", "识图", "视觉", "轮换" }),
        ("Memory", new[] { "记忆", "回忆", "上下文", "总结", "折叠", "关系网", "标签", "副设定", "省", "花费", "token", "价钱", "排行榜", "日记" }),
        ("Voice", new[] { "语音", "朗读", "tts", "stt", "识别", "音色", "sovits", "whisper" }),
        ("Notify", new[] { "通知", "问候", "提醒" }),
        ("Weather", new[] { "天气", "打雷", "下雨", "生活", "周期" }),
        ("Desktop", new[] { "桌面", "桌宠", "置顶", "闲置", "托盘", "任务栏" }),
        ("Official", new[] { "qq", "微信", "机器人", "群聊", "openid", "外发", "公众号" }),
        ("Mcp", new[] { "mcp", "插件", "服务器", "工具集" }),
        ("Runtime", new[] { "运行时", "python", "java", "proot", "沙箱运行时" }),
        ("Data", new[] { "数据", "导出", "导入", "备份", "存档", "云存档", "同步", "github", "仓库" }),
        ("Update", new[] { "更新", "版本", "beta", "渠道" }),
        ("Experimental", new[] { "实验", "测试功能" }),
        ("Materials", new[] { "素材", "背景库", "音乐库", "素材库" }),
        ("Skills", new[] { "能力包", "skill", "技能包" }),
        ("Sponsor", new[] { "作者", "赞助", "支持" }),
        ("Developer", new[] { "开发者", "展示", "调试" }),
    };

    /// <summary>当前查询（空 = 全部显示）。</summary>
    public string Query { get; private set; } = "";

    /// <summary>命中卡片数（给 UI 显示「找到 N 项」）。</summary>
    public int MatchCount { get; private set; }

    public bool IsSearching => Query.Length > 0;

    /// <summary>按查询词过滤：命中则保留该卡片，未命中收起。空查询恢复全部。</summary>
    public void Apply(string? query)
    {
        Query = (query ?? "").Trim();
        var q = Query.ToLowerInvariant();
        var matched = 0;
        foreach (var (key, words) in Table)
        {
            var hit = q.Length == 0 || words.Any(w => w.Contains(q, StringComparison.OrdinalIgnoreCase) || q.Contains(w, StringComparison.OrdinalIgnoreCase));
            if (hit) matched++;
            Set(key, hit);
        }
        // 搜索时永远保留「支持作者/开发者」之外的卡片；这两个不属于设置项，搜索时收起
        if (q.Length > 0)
        {
            Developer = Table.First(t => t.Key == "Developer").Words.Any(w => q.Contains(w, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            Developer = true;
        }
        MatchCount = matched;
        OnPropertyChanged(nameof(MatchCount));
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(EmptyHint));
    }

    /// <summary>搜不到时的提示。</summary>
    public string EmptyHint => IsSearching && MatchCount == 0
        ? "没有匹配的设置项。试试更短的词，比如「记忆」「模型」「语音」。"
        : "";

    private void Set(string key, bool value)
    {
        switch (key)
        {
            case "Volume": Volume = value; break;
            case "Glass": Glass = value; break;
            case "GlassLight": GlassLight = value; break;
            case "Background": Background = value; break;
            case "Sandbox": Sandbox = value; break;
            case "Interface": Interface = value; break;
            case "Minigame": Minigame = value; break;
            case "Chess": Chess = value; break;
            case "TextSfx": TextSfx = value; break;
            case "Ai": Ai = value; break;
            case "Memory": Memory = value; break;
            case "Voice": Voice = value; break;
            case "Notify": Notify = value; break;
            case "Weather": Weather = value; break;
            case "Desktop": Desktop = value; break;
            case "Official": Official = value; break;
            case "Mcp": Mcp = value; break;
            case "Runtime": Runtime = value; break;
            case "Data": Data = value; break;
            case "Update": Update = value; break;
            case "Experimental": Experimental = value; break;
            case "Materials": Materials = value; break;
            case "Skills": Skills = value; break;
            case "Sponsor": Sponsor = value; break;
            case "Developer": Developer = value; break;
        }
    }
}
