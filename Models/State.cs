namespace WarmAsBefore.Models;

public sealed record GameState
{
    public string CharacterId { get; set; } = string.Empty;
    public string Location { get; set; } = "home";
    public string Background { get; set; } = string.Empty;
    public string Bgm { get; set; } = string.Empty;
    public DateTime GameTime { get; set; } = DateTime.Now;
    public bool AutoPlay { get; set; }
    public bool Paused { get; set; }
    public Dictionary<string, bool> Flags { get; init; } = new();
    public HashSet<string> CgUnlocked { get; init; } = new();
    public HashSet<string> Achievements { get; init; } = new();
    public List<AffectionTick> AffectionLog { get; init; } = new();
}

public sealed record AffectionTick
{
    public int Value { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTime When { get; init; } = DateTime.UtcNow;
}

public sealed record SaveSlot
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public string Label { get; init; } = string.Empty;
    public DateTime SavedAt { get; init; } = DateTime.UtcNow;
    public string Character { get; init; } = string.Empty;
    public string Scene { get; init; } = string.Empty;
    public int PlayMinutes { get; init; }
}

/// <summary>会话中的一条聊天记录：随存档一起持久化，读档后恢复聊天界面。</summary>
public sealed record ChatRecord
{
    public string Role { get; init; } = "";
    public string Text { get; init; } = "";
    public DateTime At { get; init; } = DateTime.Now;
}

public sealed record UserSettings
{
    public string Lang { get; init; } = "zh-CN";
    public double TextSpeed { get; init; } = 1.0;
    public double BgmLevel { get; init; } = 0.7;
    public double SfxLevel { get; init; } = 0.8;
    public bool DeveloperMode { get; init; }
    public bool ComplexPlot { get; init; }
    public bool NovelTestingEnabled { get; init; }   // 小说功能为测试：默认关闭，仅在设置中打开后才显示入口
    public bool ShowAllAffection { get; init; }
    public string GlassStyle { get; init; } = "none";   // 旧版单一效果字段（仅用于迁移，不再写入）
    public bool FrostEnabled { get; init; }             // 磨砂玻璃（半透明磨砂）
    public bool GlassEnabled { get; init; }             // 毛玻璃（磨砂的高级版）
    public bool LiquidEnabled { get; init; }            // 液态玻璃
    public string ThemeName { get; init; } = "classic"; // 配色主题：classic/sakura/bamboo/mist
    public string KeySfx { get; init; } = "default";
    public string MenuSide { get; init; } = "left";
    public bool AutoSaveEnabled { get; init; } = true;

    // AI 对话
    public string AiUrl { get; init; } = "https://api.openai.com/v1/chat/completions";
    public string AiKey { get; init; } = "";
    public string AiModel { get; init; } = "gpt-4o";
    public double AiTemperature { get; init; } = 0.8;
    public int AiMaxTokens { get; init; } = 500;
    public bool DeepThink { get; init; }
    public string DeepModel { get; init; } = "";
    public int MemoryTurns { get; init; } = 5;

    // 语音
    public bool TtsEnabled { get; init; } = true;
    public double TtsRate { get; init; } = 1.0;
    public bool SttEnabled { get; init; } = true;
    /// <summary>朗读引擎：system=系统自带语音，api=外部 TTS API。</summary>
    public string TtsEngine { get; init; } = "system";
    /// <summary>语音识别引擎：system=系统自带识别，api=外部 STT API。</summary>
    public string SttEngine { get; init; } = "system";
    /// <summary>语音 API 基础地址（OpenAI 兼容，端点 /audio/speech、/audio/transcriptions）。</summary>
    public string VoiceApiUrl { get; init; } = "https://api.openai.com/v1";
    public string VoiceApiKey { get; init; } = "";
    public string VoiceTtsModel { get; init; } = "tts-1";
    public string VoiceSttModel { get; init; } = "whisper-1";
    /// <summary>TTS 音色（如 alloy / echo / nova / shimmer）。</summary>
    public string VoiceName { get; init; } = "alloy";

    // 通知与陪伴
    public bool NotificationsEnabled { get; init; } = true;
    public bool GreetingEnabled { get; init; } = true;
    // 日记为每日自动生成的核心机制，不支持关闭；回忆录由全部好感时刻与对话回忆构成。

    // 天气与生活
    public string WeatherCity { get; init; } = "";
    public int CycleLength { get; init; } = 28;
    public int PeriodLength { get; init; } = 5;

    // 电脑端
    public bool AlwaysOnTop { get; init; }
    /// <summary>桌宠闲置时长（分钟）：0 表示关闭闲置自动桌宠；达到闲置时长自动进入桌宠模式，重新有输入时自动恢复。</summary>
    public int PetIdleMinutes { get; init; } = 0;

    // 官方接入（真微信 / QQ 官方机器人）
    public bool QqBotEnabled { get; init; }
    public string QqAppId { get; init; } = "";
    public string QqAppSecret { get; init; } = "";
    public bool WechatEnabled { get; init; }
    public string WechatAppId { get; init; } = "";
    public string WechatAppSecret { get; init; } = "";
    public string WechatToken { get; init; } = "";
    public int WechatPort { get; init; } = 8012;

    // MCP 配置
    public bool McpEnabled { get; init; }
    /// <summary>MCP 工具调用是否自动确认（false 时主界面调用 MCP 工具需人工确认）。</summary>
    public bool McpAutoApprove { get; init; } = true;
    public string McpNetworkUrl { get; init; } = "";
    public string McpGitHubRepo { get; init; } = "";
    public string McpImportZipPath { get; init; } = "";
    public string McpImportFolderPath { get; init; } = "";

    // 小游戏 AI 难度：easy/normal/hard（默认 normal）
    public string GameDifficulty { get; init; } = "normal";
    /// <summary>小游戏 AI 难度由 AI 自动调节（true 时忽略 GameDifficulty 手动值）。</summary>
    public bool AiAutoDifficulty { get; init; }

    // 商店 GitHub 仓库同步：形如 "owner/repo"，仓库根目录须含 shop/catalog.json（含菜品图片）
    public string ShopGitHubRepo { get; init; } = "";

    // 云端棋力脑：可选接入 OpenAI 兼容下棋 API（未配置时纯本地启发式 AI）
    public bool ChessApiEnabled { get; init; }
    public string ChessApiUrl { get; init; } = "";
    public string ChessApiKey { get; init; } = "";
    public string ChessApiModel { get; init; } = "gpt-4o-mini";
}