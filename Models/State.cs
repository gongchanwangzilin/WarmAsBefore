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

    /// <summary>
    /// 本存档可用的角色白名单：非空时 AI 只能在这些角色里选配角，
    /// 避免「角色库里只要有角色就疯狂加配角」。空 = 不限制。
    /// </summary>
    public HashSet<string> AllowedCharacters { get; init; } = new();

    /// <summary>AI 主动黑屏（屏幕上什么都不显示，但仍然可以对话）。</summary>
    public bool Blackout { get; set; }
    /// <summary>黑屏原因（AI 给的说明，供用户理解此刻在发生什么）。</summary>
    public string BlackoutReason { get; set; } = "";
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
/// 多端点：一条 =「供应商 + 某款 AI（模型）+ 该组合的密钥池」。
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

    /// <summary>该端点的超时时长（秒）：null 或 ≤0 = 用全局设置。慢模型/长思考端点可以单独放宽。</summary>
    public int? TimeoutSeconds { get; init; }
}

/// <summary>
/// 厂商下的一款模型：厂商决定端点和密钥，模型只是它的一个选择项。
/// 同一厂商挂多个模型（贵的主力 + 便宜的兜底）比给每个模型重复填一遍端点密钥省事得多。
/// </summary>
public sealed record AiProviderModel
{
    public string Model { get; init; } = "";
    /// <summary>显示名（可留空，用模型名）。</summary>
    public string Label { get; init; } = "";
    public bool Enabled { get; init; } = true;

    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? Model : Label;
}

/// <summary>
/// 接入的厂商（供应商）：一个端点 + 一组密钥 + 若干可用模型。
/// 轮换池按「启用的厂商 × 启用的模型」展开，顺序即优先级。
/// </summary>
public sealed record AiProvider
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>厂商显示名，如「硅基流动」「DeepSeek 官方」。</summary>
    public string Name { get; init; } = "";
    /// <summary>OpenAI 兼容端点（chat/completions）。</summary>
    public string Url { get; init; } = "";
    /// <summary>该厂商的多枚密钥（轮流使用）。</summary>
    public List<string> Keys { get; init; } = new();
    public bool Enabled { get; init; } = true;
    public int? TimeoutSeconds { get; init; }
    /// <summary>这家厂商下要用的模型（可多款）。</summary>
    public List<AiProviderModel> Models { get; init; } = new();

    public string Summary =>
        $"{Models.Count(m => m.Enabled)}/{Models.Count} 款模型 · {Keys.Count(k => !string.IsNullOrWhiteSpace(k))} 枚密钥"
        + (TimeoutSeconds is { } t and > 0 ? $" · 超时 {t}s" : "");
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
    /// <summary>
    /// 风格世界：soft（轻质感，默认）/ minimal（柔和极简）/ gradient（柔和渐变）/ liquid（液态玻璃）。
    /// 空串表示这份存档还没有选过 —— 由旧的三个玻璃布尔推出来，老用户的观感不会一夜变样。
    /// </summary>
    public string StyleWorldName { get; init; } = "";
    public bool FrostEnabled { get; init; }             // 磨砂玻璃（半透明磨砂）
    public bool GlassEnabled { get; init; }             // 毛玻璃（磨砂的高级版）
    public bool LiquidEnabled { get; init; }            // 液态玻璃
    /// <summary>玻璃自适应透明度 0-1（Apple Liquid Glass 标准）：1=全液态透明；0=接近不透明。</summary>
    public double GlassTranslucency { get; init; } = 1.0;
    /// <summary>减弱透明降级（Accessibility）：true 时玻璃切不透明磨砂层，保证正文 4.5:1 对比。</summary>
    public bool GlassReducedTransparency { get; init; }
    /// <summary>
    /// 优先使用独显：在 Windows 图形设置里给本程序登记「高性能」，重启程序生效。
    /// 只影响玻璃模糊/大图缩放等合成工作；界面构建慢属于 CPU 侧，换显卡无帮助。
    /// </summary>
    public bool PreferDiscreteGpu { get; init; }

    /// <summary>
    /// 轻量模式：关闭卡片/信息条的投影阴影。阴影在 WinUI 上是每个元素一个合成层，
    /// 卡片多的页面（比如设置页有 24 张卡）首屏与重排都会明显变慢——低配机器上开它能立竿见影。
    /// </summary>
    public bool LightMode { get; init; }

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

    // 量化模型（embedding）：把说法换成数值，近义的说法数值就近
    /// <summary>
    /// 量化模型总开关。开着但没填模型名时不会打网络 —— 该角色自动退回本地共现统计（不花钱的那条路）。
    /// </summary>
    public bool EmbeddingEnabled { get; init; } = true;
    /// <summary>量化端点（OpenAI 兼容，实际请求 {base}/embeddings）。留空则复用对话端点或厂商地址。</summary>
    public string EmbeddingUrl { get; init; } = "";
    /// <summary>量化密钥。留空则复用对话密钥或厂商密钥池（轮换时逐枚试，额度耗尽换下一枚）。</summary>
    public string EmbeddingKey { get; init; } = "";
    /// <summary>量化模型名，如 <c>text-embedding-3-small</c> / <c>BAAI/bge-m3</c>。空 = 只用本地统计。</summary>
    public string EmbeddingModel { get; init; } = "";
    /// <summary>向量维度：0 = 由模型自己定。换维度等于换一套向量，缓存按「模型 + 维度」分开存。</summary>
    public int EmbeddingDimensions { get; init; }
    public double AiTemperature { get; init; } = 0.8;
    public int AiMaxTokens { get; init; } = 500;
    public bool DeepThink { get; init; }
    public string DeepModel { get; init; } = "";
    public int MemoryTurns { get; init; } = 5;

    /// <summary>
    /// 遗忘指数（天）：人类记忆衰减的时间常数，默认以三天内的事为主，越久越模糊。
    /// 可以小于 1（0.25 = 6 小时，用来演示「转头就忘」）；0 = 不限（能想起最早的记忆）。
    /// 衰减是叠加曲线，重要的事留有永久成分，不会一路掉到零。可按角色覆写。
    /// </summary>
    public double MemoryWindowDays { get; init; } = HumanMemoryMath.DefaultWindowDays;

    /// <summary>回忆的随机扰动强度 0~1：越大越像人一样跳着想起、偶尔漏掉。可按角色覆写。</summary>
    public double MemoryRecallRandomness { get; init; } = HumanMemoryMath.DefaultRandomness;

    /// <summary>
    /// 允许模型参与副设定的改写（默认关闭）：让角色对「自己现在的样子」的表述随时间演化。
    /// 低频触发、单次短输出，但毕竟要花钱，所以默认不开。
    /// </summary>
    public bool SubProfileAiRewrite { get; init; }

    /// <summary>
    /// 允许模型辅助建立关系网（默认关闭）。
    /// 关闭时关联网只靠共现统计自动长；打开后每 20 轮让模型补几条它认为相关的联系，
    /// 这些边标记为 ai 来源，人工纠错仍然优先。
    /// </summary>
    public bool SemanticAiAssist { get; init; }

    /// <summary>
    /// 让同一个角色跨存档共用一份记忆（默认关闭）。
    /// 关掉时每个存档各记各的：换一个档，TA 不记得别处发生过什么——
    /// 「这段关系是在这个存档里慢慢长出来的」是产品的底座，串档记忆会把它抹平。
    /// 打开后所有存档读写同一份；那时想把两个人分开，只能复制角色（连记忆一起复制一份）。
    /// </summary>
    public bool CrossSaveMemory { get; init; }

    // 多端点轮换（供应商 × AI × 密钥）：保存多条端点，自动轮流用，保证对话不间断
    /// <summary>
    /// 多端点档案：一条 =「供应商 + 某款 AI（模型）+ 该组合的密钥池」。
    /// 轮换开启时依次使用；任一密钥/端点失灵自动切换下一条。
    /// </summary>
    public List<AiEndpointProfile> AiProfiles { get; init; } = new();

    /// <summary>
    /// 厂商列表（推荐用这个）：一家厂商 = 一个端点 + 一组密钥 + 多款模型。
    /// 轮换按「启用的厂商 × 启用的模型」展开。旧的 <see cref="AiProfiles"/> 会在启动时自动迁移进来。
    /// </summary>
    public List<AiProvider> AiProviders { get; init; } = new();
    /// <summary>端点/密钥轮换总开关（可开关）：false 时完全按原来的单端点行为走。</summary>
    public bool AiRotationEnabled { get; init; } = true;

    /// <summary>请求超时（秒）：全局默认，各端点可单独覆写（慢模型/长思考可放宽）。默认 25 秒。</summary>
    public int AiTimeoutSeconds { get; init; } = 25;

    /// <summary>模型单价表（每百万 token）：录入后才会统计花费并给出省钱建议。</summary>
    public List<ModelPrice> ModelPrices { get; init; } = new();

    /// <summary>
    /// 智能省钱总开关（默认开）：开启时折叠历史前会先预测「现在总结 / 不总结」哪个更省，
    /// 并识别编程/办公场景改用轻总结；关闭后恢复「到上限就总结」的老行为。
    /// </summary>
    public bool SmartSavingEnabled { get; init; } = true;
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
    /// <summary>
    /// 城市坐标（由「定位」或「选择城市」写入）。为空说明是旧数据里手填的城市名，
    /// 拉天气时再走一次地理编码，不能因为升级就让用户重选一遍城市。
    /// </summary>
    public double? WeatherLatitude { get; init; }
    public double? WeatherLongitude { get; init; }
    /// <summary>天气数据源：open-meteo（默认）/ wttr / custom（Open-Meteo 兼容的自建或镜像端点）。</summary>
    public string WeatherSource { get; init; } = "open-meteo";
    /// <summary>自定义源基址，仅 <see cref="WeatherSource"/> 为 custom 时生效。</summary>
    public string WeatherApiUrl { get; init; } = "";
    public string WeatherApiKey { get; init; } = "";
    /// <summary>
    /// 周期规则按性别分开存，键是性别原文（Trim 后）。空字典 = 还没配过，
    /// 由 <see cref="CycleRuleFor"/> 回落到出厂规则。
    /// </summary>
    public Dictionary<string, CycleRule> CycleRules { get; init; } = new();

    /// <summary>
    /// 某个性别的周期规则。没单独配过的性别一律「无周期」，不去猜：
    /// 凭空给某个性别安一段周期，提示词里就多了一件从没发生过的事，模型会当真。
    /// </summary>
    public CycleRule CycleRuleFor(string? gender)
    {
        var g = (gender ?? "").Trim();
        return g.Length > 0 && CycleRules.TryGetValue(g, out var rule) ? rule : CycleRule.DefaultFor(g);
    }

    // 旧版单一周期（v1.36 起按性别拆成 CycleRules）。留着只为了给老存档搬家，运行时不再读。
    [System.Obsolete("改用 CycleRules；仅 SettingsManager.MigrateLegacyAsync 读取")]
    public int CycleLength { get; init; } = 28;
    [System.Obsolete("改用 CycleRules；仅 SettingsManager.MigrateLegacyAsync 读取")]
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
    /// <summary>
    /// 多显示器：开=头顶小窗可以跟角色跨到副屏（按整个虚拟桌面收边）；
    /// 关=只按角色所在那块屏的工作区收边，角色贴屏边时小窗折回本屏，不会跨过缝隙跑到另一块屏上。
    /// 只有一块屏时两个选项没区别。
    /// </summary>
    public bool PetChromeCrossScreen { get; init; }

    // 官方接入（真微信 / QQ 官方机器人）
    public bool QqBotEnabled { get; init; }
    public string QqAppId { get; init; } = "";
    public string QqAppSecret { get; init; } = "";
    /// <summary>
    /// #16：QQ 机器人绑定「用它来陪聊的角色」。接入机器人时必须选定一个已有角色（相当于选存档），
    /// 空串表示回退到「当前游戏角色」的行为。
    /// </summary>
    public string QqBoundCharacterId { get; init; } = "";

    /// <summary>是否回复群聊消息（默认否）：群聊里默认不理，免得在群里刷屏。</summary>
    public bool QqReplyGroup { get; init; }

    /// <summary>
    /// 用来识别「用户本人」的 QQ openid：主动发消息时发给它。
    /// 留空则用最后一个跟机器人私聊过的人（读日志可查）。
    /// </summary>
    public string QqUserOpenId { get; init; } = "";

    /// <summary>用户在 QQ 里的昵称（识别到本人时，角色会用这个称呼叫他）。</summary>
    public string QqUserName { get; init; } = "";

    /// <summary>允许角色主动发消息到用户手机（存档里的「TA 拿起手机发消息」）。默认关闭。</summary>
    public bool QqProactiveEnabled { get; init; }

    /// <summary>主动发消息是否需要人工确认（默认需要）：AI 不能未经同意就往外发。</summary>
    public bool QqProactiveRequireConfirm { get; init; } = true;

    /// <summary>主动发消息的每小时上限（防骚扰 / 防误触刷屏）。</summary>
    public int QqProactivePerHour { get; init; } = 3;

    /// <summary>主动发消息优先走哪个通道：auto=有 QQ 就 QQ、否则微信；也可固定 qq / wechat。</summary>
    public string RealOutboundChannel { get; init; } = "auto";
    public bool WechatEnabled { get; init; }
    public string WechatAppId { get; init; } = "";
    public string WechatAppSecret { get; init; } = "";
    public string WechatToken { get; init; } = "";
    public int WechatPort { get; init; } = 8012;

    /// <summary>
    /// 视频号外链：社交壳（微信/QQ）里点「视频号」时直接跳转到的用户链接（留空则提示去设置里填）。
    /// 微信壳与 QQ 壳共用这一个链接（都是「视频号」入口，只是宿主 App 不同）。
    /// </summary>
    public string ChannelsUrl { get; init; } = "";

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

    // ===== 云存档（GitHub 仓库托管） =====
    // 只镜像数据文件，不镜像 settings.json：令牌与设备身份留在本机，绝不进仓库。
    /// <summary>云存档总开关。</summary>
    public bool CloudSaveEnabled { get; init; }
    /// <summary>托管仓库，形如 "owner/repo"（建议私有仓库）。</summary>
    public string CloudRepo { get; init; } = "";
    /// <summary>GitHub 细粒度访问令牌（需要该仓库的 Contents 读写权限）。</summary>
    public string CloudToken { get; init; } = "";
    /// <summary>分支；空则用仓库默认分支。</summary>
    public string CloudBranch { get; init; } = "";
    /// <summary>设备名（只用于仓库里的目录说明与状态显示）。</summary>
    public string CloudDeviceName { get; init; } = "";
    /// <summary>改动停手后自动上传。</summary>
    public bool CloudAutoUpload { get; init; } = true;
    /// <summary>跨设备合并后用对话模型整理重复记忆。</summary>
    public bool CloudReconcileMemory { get; init; } = true;

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

/// <summary>
/// 一个性别的周期规则。<see cref="Enabled"/> 为 false 表示这个性别没有周期，
/// 桌宠/提示词里都不该出现「经期第几天」这类表述。
/// </summary>
public sealed record CycleRule
{
    public bool Enabled { get; init; }
    public int CycleLength { get; init; } = 28;
    public int PeriodLength { get; init; } = 5;

    /// <summary>出厂规则：只有「女」默认带周期，其余（含自定义性别）一律不带，要带得去设置里写。</summary>
    public static CycleRule DefaultFor(string gender) => gender switch
    {
        "女" => new CycleRule { Enabled = true },
        _ => new CycleRule { Enabled = false },
    };
}