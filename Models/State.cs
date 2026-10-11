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

/// <summary>
/// 多端点档案：一条 =「供应商 + 某款 AI（模型）+ 该组合的密钥池」。
/// 密钥池里的多枚密钥轮流使用；多条档案组成轮换链，任一条失灵自动切换下一条，保证对话不间断。
/// </summary>
public sealed record AiEndpointProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>显示名（供应商名/备注），如「硅基流动-主力」。</summary>
    public string Name { get; init; } = "";
    /// <summary>端点地址（OpenAI 兼容 chat/completions）。</summary>
    public string Url { get; init; } = "";
    /// <summary>该供应商下要使用的 AI 模型名。</summary>
    public string Model { get; init; } = "";
    /// <summary>该组合的多枚密钥（轮流使用）。</summary>
    public List<string> Keys { get; init; } = new();
    /// <summary>单条启用开关。</summary>
    public bool Enabled { get; init; } = true;
}

public sealed record UserSettings
{
    public string Lang { get; init; } = "zh-CN";
    public double TextSpeed { get; init; } = 1.0;
    public double BgmLevel { get; init; } = 0.7;
    public double SfxLevel { get; init; } = 0.8;
    public bool ComplexPlot { get; init; }
    public bool NovelTestingEnabled { get; init; }   // 小说功能为测试：默认关闭，仅在设置中打开后才显示入口
    public bool ShowAllAffection { get; init; }
    /// <summary>好感等级提升动画：满 500 好感积分升 1 级时播放全屏动画。</summary>
    public bool AffectionLevelUpEnabled { get; init; } = true;
    public string GlassStyle { get; init; } = "none";   // 旧版单一效果字段（仅用于迁移，不再写入）
    public bool FrostEnabled { get; init; }             // 磨砂玻璃（半透明磨砂）
    public bool GlassEnabled { get; init; }             // 毛玻璃（磨砂的高级版）
    public bool LiquidEnabled { get; init; }            // 液态玻璃
    /// <summary>玻璃自适应透明度 0-1（Apple Liquid Glass 标准）：1=全液态透明；0=接近不透明。</summary>
    public double GlassTranslucency { get; init; } = 1.0;
    /// <summary>减弱透明降级（Accessibility）：true 时玻璃切不透明磨砂层，保证正文 4.5:1 对比。</summary>
    public bool GlassReducedTransparency { get; init; }
    /// <summary>磨砂程度 0-1：0=清玻璃，1=厚磨砂（奶白雾气浓度）。</summary>
    public double GlassFrost { get; init; } = 0.5;
    /// <summary>液态高度 0-1：投影深度 + 上沿高光，0=贴面，1=明显浮起。</summary>
    public double GlassHeight { get; init; } = 0.5;
    /// <summary>页面背景：none=纯色 · image=自定义图片 · clear=透明透视到窗口下层。
    /// 玻璃需要有东西可透，纯色背景下无论怎么调都看不出玻璃。</summary>
    /// <summary>光源：X/Y = 屏幕平面方向(-1..1)，Z = 离表面高度(0..1)，Width = 高光宽窄(0..1)。
    /// 决定高光落在哪条边、影子投往哪边、反光是锐利还是漫射。</summary>
    public double GlassLightX { get; init; } = -0.7;
    public double GlassLightY { get; init; } = -0.7;
    public double GlassLightZ { get; init; } = 0.3;
    public double GlassLightWidth { get; init; } = 0.4;
    public string BackgroundMode { get; init; } = "none";
    public string BackgroundImagePath { get; init; } = "";
    public string ThemeName { get; init; } = "classic"; // 配色主题：classic/sakura/bamboo/mist
    public string KeySfx { get; init; } = "default";
    public string MenuSide { get; init; } = "left";
    public bool AutoSaveEnabled { get; init; } = true;

    // 多角色单场景对话
    /// <summary>聊天显示风格：bubble=气泡式（消息直接成对话气泡流），galgame=剧本式（底部面板 + 立绘）。</summary>
    public string ChatStyle { get; init; } = "galgame";
    /// <summary>允许 AI 沉默：开启后角色可以选择不回复（什么都不输出），系统会自动停止该回合，不再每次都强行回复。</summary>
    public bool AllowSilence { get; init; }

    // AI 对话
    public string AiUrl { get; init; } = "https://api.openai.com/v1/chat/completions";
    public string AiKey { get; init; } = "";
    public string AiModel { get; init; } = "gpt-4o";
    /// <summary>#13：API 预设标签（openai/deepseek/mimo/kimi/doubao/zhipu/siliconflow/qwen/custom）。仅用于设置页一键填充。</summary>
    public string AiPreset { get; init; } = "custom";
    /// <summary>#13：识图（视觉）模型独立端点；三项都填才能用识图（如表情包自动描述）。</summary>
    public string VisionUrl { get; init; } = "";
    public string VisionKey { get; init; } = "";
    public string VisionModel { get; init; } = "";
    public double AiTemperature { get; init; } = 0.8;
    public int AiMaxTokens { get; init; } = 500;
    public bool DeepThink { get; init; }
    public string DeepModel { get; init; } = "";
    public int MemoryTurns { get; init; } = 5;

    // 多端点轮换（供应商 × AI × 密钥）：保存多条端点，自动轮流用，保证对话不间断
    /// <summary>
    /// 多端点档案：一条 =「供应商 + 某款 AI（模型）+ 该组合的密钥池」。
    /// 轮换开启时依次使用；任一密钥/端点失灵自动切换下一条。
    /// </summary>
    public List<AiEndpointProfile> AiProfiles { get; init; } = new();
    /// <summary>端点/密钥轮换总开关（可开关）：false 时完全按原来的单端点行为走。</summary>
    public bool AiRotationEnabled { get; init; } = true;
    /// <summary>上下文 Token 上限（估算值）：对话历史超过该值时，强制把旧对话总结成第一人称记忆。</summary>
    public int ContextTokenLimit { get; init; } = 32000;

    // 语音
    public bool TtsEnabled { get; init; } = true;
    public double TtsRate { get; init; } = 1.0;
    public bool SttEnabled { get; init; } = true;
    /// <summary>朗读引擎：system=系统自带语音，api=外部 TTS API。</summary>
    public string TtsEngine { get; init; } = "system";
    /// <summary>语音识别引擎：system=系统自带识别，local=本地下载的模型，api=外部 STT API。</summary>
    public string SttEngine { get; init; } = "system";
    /// <summary>本地识别模型名称（如 ggml-base.en.bin / ggml-small.bin），对应 {root}/stt/{model}。</summary>
    public string SttModelName { get; init; } = "";
    /// <summary>语音 API 基础地址（OpenAI 兼容，端点 /audio/speech、/audio/transcriptions；SoVITS 模式下为服务根地址，如 http://192.168.1.10:9870）。</summary>
    public string VoiceApiUrl { get; init; } = "https://api.openai.com/v1";
    public string VoiceApiKey { get; init; } = "";
    public string VoiceTtsModel { get; init; } = "tts-1";
    public string VoiceSttModel { get; init; } = "whisper-1";
    /// <summary>TTS 音色（OpenAI 如 alloy / echo / nova / shimmer；SoVITS 为角色参考音名）。</summary>
    public string VoiceName { get; init; } = "alloy";
    /// <summary>外部语音服务类型：openai=OpenAI 兼容（/v1/audio/*），sovits=GPT-SoVITS（/api/tts，本地或局域网服务）。</summary>
    public string VoiceApiMode { get; init; } = "openai";
    /// <summary>引擎特有扩展参数（SoVITS：spk_id 说话人编号，0 表示默认角色）。</summary>
    public string VoiceExtra { get; init; } = "";

    // 通知与陪伴
    public bool NotificationsEnabled { get; init; } = true;
    public bool GreetingEnabled { get; init; } = true;
    // 日记为每日自动生成的核心机制，不支持关闭；回忆录由全部好感时刻与对话回忆构成。

    // 天气与生活
    public string WeatherCity { get; init; } = "";
    public int CycleLength { get; init; } = 28;
    public int PeriodLength { get; init; } = 5;

    // 移动设备
    /// <summary>屏幕方向：auto=默认竖屏 / landscape=锁横屏 / portrait=锁竖屏（仅移动设备实际生效，桌面 no-op）。</summary>
    public string ScreenOrientation { get; init; } = "auto";

    // 电脑端
    public bool AlwaysOnTop { get; init; }
    /// <summary>桌宠闲置时长（分钟）：0 表示关闭闲置自动桌宠；达到闲置时长自动进入桌宠模式，重新有输入时自动恢复。</summary>
    public int PetIdleMinutes { get; init; } = 0;
    /// <summary>
    /// #E3 桌宠思考模式：off=不主动思考；random=按间隔随机（约一半概率）嘀咕；timer=按间隔定时嘀咕。
    /// 桌宠模式下单击立绘也总能触发一次思考。
    /// </summary>
    public string PetThinkMode { get; init; } = "off";
    /// <summary>桌宠思考间隔（分钟），默认 10。</summary>
    public int PetThinkIntervalMinutes { get; init; } = 10;

    // 官方接入（真微信 / QQ 官方机器人）
    public bool QqBotEnabled { get; init; }
    public string QqAppId { get; init; } = "";
    public string QqAppSecret { get; init; } = "";
    /// <summary>
    /// #16：QQ 机器人绑定「用它来陪聊的角色」。接入机器人时必须选定一个已有角色（相当于选存档），
    /// 空串表示回退到「当前游戏角色」的行为。
    /// </summary>
    public string QqBoundCharacterId { get; init; } = "";
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

    // 工具模式运行时（手动配置的 Java / Python 路径；空则自动在 PATH/便携目录中查找）
    public string PythonPath { get; init; } = "";
    public string JavaPath { get; init; } = "";

    /// <summary>自动更新通道：true=预发布（Beta）。此前只存在于视图模型，未落盘，重开设置页即复位。</summary>
    public bool BetaChannel { get; init; }

    // 开发者展示模式：设置页底部输入 114514 后解锁
    public bool DeveloperShowcaseUnlocked { get; init; }

    /// <summary>免责声明已确认（首次启动标题页弹层「知道了」后写 true；不再重复弹）。</summary>
    public bool DisclaimerAcknowledged { get; init; }

    // ===== 战斗系统 =====

    /// <summary>战斗系统总开关：关闭时 AI 不得发起战斗，battle_* 工具从可用列表剔除，战斗入口隐藏。</summary>
    public bool BattleEnabled { get; init; } = true;

    /// <summary>是否允许 AI 主动发起战斗（关闭时只有用户手动点进战斗页才能开战）。</summary>
    public bool BattleAiCanStart { get; init; } = true;

    // ===== 天气事件（打雷 / 下雨） =====

    /// <summary>打雷事件总开关：关闭则任何天气都不会触发打雷与白闪。</summary>
    public bool LightningEnabled { get; init; } = true;

    /// <summary>打雷触发概率基准（0..1，每分钟 tick 判定的基准值）。
    /// 雨天/极阴天按此值缩放，打雷天按此值的 4 倍（上限 1）——即「打雷天概率一般，雨天概率小」。</summary>
    public double LightningProbability { get; init; } = 0.06;

    /// <summary>下雨事件总开关：关闭则天气切到雨天时不会播放雨声/雨动画。</summary>
    public bool RainEventEnabled { get; init; } = true;

    /// <summary>下雨触发概率（0..1，天气进入雨天时每分钟判定一次的基准值）。</summary>
    public double RainProbability { get; init; } = 0.15;

    /// <summary>打雷音效文件路径（绝对路径；空则不打雷声，仅白闪）。</summary>
    public string LightningSfxPath { get; init; } = "";

    /// <summary>下雨音效文件路径（绝对路径；空则不播雨声）。</summary>
    public string RainSfxPath { get; init; } = "";

    /// <summary>打雷特效图片/动画帧序列（绝对路径，按次序顺序切换；为空则用默认「白一下再慢慢恢复」）。</summary>
    public List<string> LightningFrames { get; init; } = new();

    /// <summary>下雨特效图片/动画帧序列（绝对路径，按次序循环切换；为空则不显示雨视觉）。</summary>
    public List<string> RainFrames { get; init; } = new();

    /// <summary>特效帧切换间隔（毫秒）：只有一帧时表示「闪一下」的持续时长。</summary>
    public int WeatherEffectFrameMs { get; init; } = 120;

    // ===== 行进扩展（#9） =====

    /// <summary>AI 是否可单独出行（不带用户走）：开启后 AI 发【移动:】只移动自己，
    /// 用户视角停原地并出现「跟上」入口；关闭则维持旧行为（AI 走到哪、用户跟到哪）。</summary>
    public bool AiTravelAlone { get; init; }

    /// <summary>AI 单独出行时，用户是否可以「跟上」（显示跟上按钮）。</summary>
    public bool AllowFollowAi { get; init; } = true;
}