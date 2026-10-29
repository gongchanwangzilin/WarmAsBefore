namespace WarmAsBefore.ViewModels;

/// <summary>
/// 设置分类目录：把所有设置卡片按用途归成若干分类，首页只列分类，点进去才看具体项。
///
/// 这份目录是「设置页结构」的唯一事实来源——卡片模板通过 <see cref="Section.CardKeys"/>
/// 组合，首页的分类说明与搜索关键词也来自这里，避免页面上再散落一份分类逻辑。
/// </summary>
public static class SettingsCatalog
{
    public sealed record Section(string Key, string Title, string Icon, string Desc, string[] CardKeys, string[] Keywords);

    public static IReadOnlyList<Section> All { get; } = new[]
    {
        new Section("appearance", "外观与主题", "🎨",
            "玻璃效果、光源方向、页面背景",
            new[] { "Card02", "Card03" },
            new[] { "外观", "主题", "玻璃", "毛玻璃", "磨砂", "液态", "光源", "高光", "背景", "配色", "glass", "theme" }),

        new Section("sound", "音量与音效", "🔊",
            "背景音乐与音效音量、按键/打字音",
            new[] { "Card01", "Card08" },
            new[] { "音量", "声音", "音乐", "音效", "bgm", "sfx", "按键音", "打字音" }),

        new Section("ai", "AI 与模型", "🤖",
            "厂商与模型、密钥、识图、量化、花费与省钱、AI 绘画",
            new[] { "Card09", "Card31" },
            new[] { "ai", "模型", "端点", "厂商", "密钥", "api", "温度", "超时", "识图", "视觉", "量化", "嵌入", "向量", "embedding", "轮换", "省钱", "花费", "token", "价钱", "绘画", "生图", "图片生成", "image" }),

        new Section("memory", "记忆与对话", "🧠",
            "记多少、记多久、上下文上限、核心记忆、创伤与链式回忆",
            new[] { "Card10", "Card30" },
            new[] { "记忆", "回忆", "上下文", "总结", "折叠", "关系网", "标签", "副设定", "记忆窗口", "核心记忆", "永久", "创伤", "链式", "遗忘" }),

        new Section("window", "窗口与运行", "🪟",
            "手机独立窗口、关窗联动、超省内存模式",
            new[] { "Card27" },
            new[] { "窗口", "多开", "手机", "独立窗口", "关闭", "关窗", "内存", "低内存", "省内存", "memory" }),

        new Section("persona", "身份与心理", "🫀",
            "旁白 AI、你的性别、自定义性别、心理状况与身体感受、大脑预判",
            new[] { "Card28", "Card29", "Card34" },
            new[] { "旁白", "性别", "自定义性别", "称呼", "心理", "疾病", "抑郁", "焦虑", "创伤", "身体", "冷暖", "饥渴", "想念", "大脑", "预判", "期待", "多巴胺", "意料", "意外", "惊喜", "数值", "好感", "信任", "精力", "结算" }),

        new Section("voice", "语音", "🎙",
            "朗读与语音识别、音色、外部语音服务",
            new[] { "Card11" },
            new[] { "语音", "朗读", "tts", "stt", "识别", "音色", "sovits", "whisper" }),

        new Section("interface", "界面与玩法", "🎮",
            "屏幕方向、聊天风格、小游戏难度与棋力、金钱系统、小组件、着装与性欲",
            new[] { "Card05", "Card06", "Card07", "Card32", "Card36" },
            new[] { "界面", "布局", "方向", "横屏", "竖屏", "聊天风格", "气泡", "galgame", "小游戏", "难度", "棋", "棋力", "金钱", "亲密币", "余额", "加钱", "小组件", "信息栏", "组件", "着装", "衣服", "形象", "性欲", "周期", "关系网", "动画" }),

        new Section("platform", "平台与运行", "🖥",
            "桌宠、显卡偏好、外部工具运行时、实验功能",
            new[] { "Card25", "Card14", "Card17", "Card20" },
            new[] { "桌宠", "桌面", "置顶", "闲置", "托盘", "显卡", "独显", "gpu", "运行时", "python", "java", "proot", "实验" }),

        new Section("integrations", "外部接入", "🔌",
            "QQ 与微信机器人、MCP 服务、工具安全、插件与工坊、视频号外链",
            new[] { "Card15", "Card16", "Card04", "Card33" },
            new[] { "qq", "微信", "机器人", "群聊", "openid", "外发", "公众号", "mcp", "插件", "plugin", "工坊", "钩子", "hook", "服务器", "工具", "沙箱", "安全", "信任", "视频号", "论坛" }),

        new Section("life", "通知与生活", "🔔",
            "通知与问候、浮动通知的样式与位置、天气、生理周期、存档运行时",
            new[] { "Card12", "Card13", "Card35", "Card37" },
            new[] { "通知", "问候", "提醒", "浮动", "位置", "停留", "天气", "打雷", "下雨", "生活", "周期", "生理", "运行时", "后台", "在线", "自己玩", "打游戏", "主动发消息", "安静" }),

        new Section("data", "数据与素材", "💾",
            "数据导入导出、云存档、自动更新、素材库、能力包",
            new[] { "Card18", "Card26", "Card19", "Card21", "Card22" },
            new[] { "数据", "导出", "导入", "备份", "云存档", "同步", "github", "双端", "更新", "版本", "beta", "素材", "音乐库", "能力包", "skill" }),

        new Section("advanced", "其他与开发者", "🧪",
            "支持作者、开发者展示模式",
            new[] { "Card23", "Card24" },
            new[] { "作者", "赞助", "支持", "开发者", "展示", "调试" }),
    };

    public static Section? ByKey(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(s => s.Key == key);

    /// <summary>某张卡片属于哪个分类（分类子页里用于校验）。</summary>
    public static Section? ByCard(string cardKey) =>
        All.FirstOrDefault(s => s.CardKeys.Contains(cardKey, StringComparer.Ordinal));
}
