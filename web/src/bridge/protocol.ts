/**
 * Web 前端与 C# 后端之间的唯一契约。
 *
 * 三种形状：
 *   请求   { id, method, params }        前端 → 后端
 *   应答   { id, ok, result | error }    后端 → 前端（与请求同 id）
 *   事件   { event, payload }            后端 → 前端（无 id，可随时来）
 *
 * 这份文件不依赖任何运行时；mock 与真机桥都按它发/收，所以两边不会各说各话。
 */

/** 服务端下发的令牌：全部是 CSS 自定义属性的文本值。 */
export type TokenMap = Record<`--w-${string}`, string>;

/** 背景模式的三个档（对应 BackgroundMode 的 none / image / clear）。 */
export type BackgroundMode = 'solid' | 'image' | 'clear';

/**
 * 天气场景：一份枚举名，就是 C# 的 <c>WeatherSceneKind</c>（Models/Background/BackgroundModels.cs）。
 * 不让前端自己编档 —— 用户能得到的天气只有这八种，宿主按实时天气解析出来的也是这八种。
 */
export type WeatherKind =
  | 'Sunny'
  | 'Cloudy'
  | 'Drizzle'
  | 'LightRain'
  | 'HeavyRain'
  | 'Storm'
  | 'Snow'
  | 'Fog';

/**
 * 「外观与主题」这一页的全部可调项。
 * 玻璃三项 + 轻量模式驱动整屏材质（App 把它们写进 --w-alpha / --w-frost / --w-liquid），
 * 背景与场景特效只决定玻璃背后透出什么。
 */
export type Appearance = {
  /** 玻璃透明度 0..1，驱动 --w-face-k */
  alpha: number;
  /** 磨砂程度 0..1，驱动 backdrop blur */
  frost: number;
  /** 液态高度 0..1，驱动柱面带高度 */
  liquid: number;
  /** 轻量模式：撤掉指针光与背景响应 */
  lightweight: boolean;
  /** 减弱透明降级（无障碍）：玻璃切不透明磨砂层 */
  reducedTransparency: boolean;
  background: {
    mode: BackgroundMode;
    /** 纯色底；空串 = 跟主题走 */
    solidHex: string;
    /** 页面色；空串 = 跟色板走 */
    pageTintHex: string;
    /** 背景图片的文件名；没有图片时是「尚未选择图片」 */
    imageLabel: string;
  };
  scene: {
    /** 测试版场景特效：总闸，关掉时下面四项不生效 */
    experimental: boolean;
    /** 天气场景叠加 */
    weather: boolean;
    /** 场景光照与光柱 */
    light: boolean;
    /** 场景音效 */
    audio: boolean;
    /** 场景音量 0..1 */
    audioLevel: number;
    /** 当前天气场景（BackgroundService.CurrentScene.Kind，随天气轮询变化） */
    weatherKind: WeatherKind;
    /** 当前场景强度 0..1（BackgroundCatalog 里该场景的 Intensity，随场景固定） */
    weatherIntensity: number;
  };
};

/** 标题页「上次的存档」卡。 */
export type SaveSummary = {
  /** 存档 id：「继续」拿它去 save.load */
  id?: string;
  label?: string;
  character: string;
  scene: string;
  /** ISO-8601 */
  savedAt: string;
  minutes: number;
};

export type ThemeName = 'classic' | 'sakura' | 'bamboo' | 'mist' | 'endfield';

/** 思考由谁决定（UserSettings.ThinkMode）：auto=跟随 AI / on=总是开 / off=总是关。 */
export type ThinkMode = 'auto' | 'on' | 'off';

/**
 * 设置首页每一行的右侧摘要就是这几个数拼出来的（对应 SettingsViewModel.RefreshSectionSummaries）。
 * 拼法在 lib/settingsHome.ts，这里只放后端给的原值。
 */
export type SettingsValues = {
  appearance: { liquid: boolean; glass: boolean; frost: boolean; theme: ThemeName };
  sound: { bgm: number; sfx: number };
  text: { scale: number };
  story: { autoSave: boolean };
  language: { locale: 'zh-CN' | 'en-US' };
  ai: {
    providers: number;
    smartSaving: boolean;
    timeoutSeconds: number;
    /**
     * 思考档位（对应 UserSettings.ThinkMode / ThinkBarEnabled / …）。
     * mode：auto=跟随 AI（默认，不摆手动开关，这一轮要不要深入想由那次模型调用自己给）、
     * on=总是开（按 tiers 与 paramValues 里当前档位强制注入）、off=总是关（一律不注入，AI 也不能抬档）。
     */
    think: {
      mode: ThinkMode;
      bar: boolean;
      quick: boolean;
      tiers: number;
      labels: string;
      paramKey: string;
      paramValues: string;
    };
  };
  memory: { windowDays: number; contextLimit: string };
  ui: {
    orientation: 'auto' | 'landscape' | 'portrait';
    chatStyle: 'bubble' | 'galgame';
    difficulty: 'easy' | 'normal' | 'hard';
  };
  platform: { petIdleMinutes: number; preferDiscreteGpu: boolean };
  integrations: { qq: boolean; wechat: boolean; mcp: boolean };
  life: { notifications: boolean; weatherCity: string; customWeatherSource: boolean };
  data: { skillsInstalled: boolean };
};

/** settings.set 的载荷按 key 收窄，写错类型在编译期就报。 */
export type SettingEdit =
  | { key: 'text.scale'; value: number }
  | { key: 'story.autoSave'; value: boolean }
  | { key: 'language.locale'; value: SettingsValues['language']['locale'] };

/** 一行一个方法：params 是请求的载荷，result 是 ok 时的载荷。 */
export type Requests = {
  'ui.ready': { params: Record<string, never>; result: { ok: boolean } };
  'ui.tokens': {
    params: Record<string, never>;
    result: { tokens: TokenMap; theme: 'dark' };
  };
  'settings.get': {
    params: Record<string, never>;
    /** values 为 null = 后端还没注册任何设置项（空状态） */
    result: { values: SettingsValues | null; save: SaveSummary | null };
  };
  'settings.set': {
    params: SettingEdit;
    result: { values: SettingsValues };
  };
  /** 一次改多组设置；键是白名单里的路径（sound.bgm / ui.chatStyle / life.notifications …）。 */
  'settings.patch': {
    params: { patch: Record<string, string | number | boolean> };
    result: { values: SettingsValues };
  };
  'appearance.get': {
    params: Record<string, never>;
    /** appearance 为 null = 这套设备还没有下发外观（空状态） */
    result: { appearance: Appearance | null };
  };
  'appearance.set': {
    params: { appearance: Appearance };
    result: { appearance: Appearance };
  };
  /**
   * 应用与宿主信息。`run` 回答「现在有没有在跑一份存档」——
   * 系在存档上的入口（QQ、论坛、监测）靠它决定出不出现，没存档就没有 TA 的状态可看。
   */
  /**
   * 语音：能不能听、听一句、停掉这一句、念一段。
   * status 决定按钮可不可点（不能点就把 note 原样摆出来），listen 等一句话再回文字，
   * 没听出字时 text 为空、note 说原因（没麦、没授权、没说话）。speak 交给宿主的朗读引擎。
   */
  'voice.status': {
    params: { shell?: ChatShell };
    result: { sttEnabled: boolean; sttEngine: string; ttsEnabled: boolean; ttsEngine: string; canListen: boolean; note: string };
  };
  'voice.listen': {
    params: { shell?: ChatShell; characterId?: string };
    result: { characterId: string; text: string; note: string };
  };
  'voice.cancel': {
    params: Record<string, never>;
    result: { stopped: boolean };
  };
  'voice.speak': {
    params: { text: string; characterId?: string };
    result: { ok: boolean };
  };
  'app.info': {
    params: Record<string, never>;
    result: {
      name: string;
      version: string;
      build: string;
      platform: string;
      kind: string;
      webView: { engine: string; version: string };
      dev: string;
      debug: boolean;
      /** active=false 时 saveId 与 character 都是空串 */
      run: { active: boolean; saveId: string; character: string };
    };
  };
  'nav.open': {
    params: { route: string };
    result: { accepted: boolean };
  };
  'nav.back': {
    params: { from: string };
    result: { accepted: boolean };
  };
  /** 把一页搬进自己的窗口（手机、关系网这类「旁边摊一扇」的页）。开不了窗时 opened=false。 */
  'window.open': {
    params: { route: string; title?: string; width?: number; height?: number };
    result: { opened: boolean; route?: string; reason?: string };
  };
  /** 关掉自己那扇窗（窗里的「←」）。主窗不关。 */
  'window.close': {
    params: Record<string, never>;
    result: { closed: boolean };
  };
  /** 真的存档表，按保存时间倒序。 */
  'saves.list': {
    params: Record<string, never>;
    result: { slots: SaveRow[] };
  };
  /**
   * 真的把这一档载入当前局（SaveManager.Load）。
   * loaded=false 且 needsConfirm=true = 存档能打开但核心状态整段重建过，先停在原地让用户确认；
   * 读不出来时整条应答失败，error.message 是宿主给的原话。
   */
  'save.load': {
    params: { id: string };
    result: { id: string; loaded: boolean; needsConfirm: boolean; note: string };
  };
  /** 开新的一局：重置局面、定主角、分新档号、把这一档的人设/世界整理成记忆、落盘。 */
  'save.new': {
    params: { characterId: string; label?: string; worldId?: string; worldName?: string; persona?: string };
    result: { id: string; characterId: string; character: string; label: string };
  };
  /** 改写 meta.Label 并原子回写；写不进去整条应答失败。 */
  'save.rename': {
    params: { id: string; label: string };
    result: { id: string; label: string };
  };
  /** 删掉这一档；文件本来就不在时整条应答失败（不谎报已删）。 */
  'save.delete': {
    params: { id: string };
    result: { id: string; deleted: boolean };
  };
  /** 这一档的插件/工具/角色白名单（存进这份存档的 state.permissions）。 */
  'save.rules.get': {
    params: { id?: string };
    result: SaveRules;
  };
  /** 把白名单写进这一档；写回失败整条应答失败，原有规则不变。 */
  'save.rules.set': {
    params: { id?: string; rules: SaveRulesEdit };
    result: { id: string; saved: boolean; rules: SaveRulesEdit };
  };
  /** 微信 / QQ 的会话名册：显式好友在前，这一档其余角色补在后面。 */
  'chat.contacts': {
    params: { shell?: ChatShell };
    result: {
      contacts: ChatContactRow[];
      /** 默认会话对象（没有会话时是 null） */
      partner: { id: string; name: string } | null;
      /** 顶栏那句模式：真接入 / 本地陪聊 */
      modeLabel: string;
    };
  };
  /** 加微信好友：名字照角色库解析（Id / 名字 / 昵称都认），备注随联系人落盘。
   *  已经加过的人 added=false 并给出源码那句提示，不覆盖备注；没有当前档整条应答失败。 */
  'chat.contact.add': {
    params: { name: string; note?: string };
    result: { id: string; name: string; added: boolean; message: string };
  };
  /** 从好友表里删掉一位（不可撤销）。删不掉时整条应答失败，原因在 error.message。 */
  'chat.contact.remove': {
    params: { id: string };
    result: { id: string; message: string };
  };
  /**
   * 未读总数（顶栏「手机」的小点、手机桌面与 Dock 的 QV 角标）。
   * byContact 只列有未读的人；total 是它们的和。没读够 0 条时就是一张空表 —— 不画角标。
   */
  'chat.unread.summary': {
    params: Record<string, never>;
    result: { total: number; byContact: { id: string; count: number }[] };
  };
  /** 一段会话的历史，倒着分页：before 传上一次的 nextBefore，空 = 最新一页。 */
  'chat.history': {
    params: { shell?: ChatShell; characterId?: string; before?: string; limit?: number };
    result: { characterId: string; messages: ChatRow[]; hasMore: boolean; nextBefore: string | null };
  };
  /** 真的把这句话送进聊天引擎，拿到回复再落盘。失败时整条应答失败，原因在 error.message。 */
  'chat.send': {
    params: { shell?: ChatShell; characterId?: string; text: string };
    result: { characterId: string; user: ChatRow; reply: ChatRow };
  };
  /** 弹宿主的选图/选文件框，把选中的文件存成一条媒体消息（拷进 assets/chat/，与 chat.send 同一条落盘路径）。
   *  文件存不进去整条应答失败；用户在选图框里点了取消是 canceled: true，不是失败。 */
  'chat.attach': {
    params: { shell?: ChatShell; characterId?: string; kind: ChatMediaKind };
    result: { canceled: boolean; id: string; kind: ChatMediaKind; url: string; fileName: string; size: number };
  };
  /** 用系统默认程序打开一条文件消息里的那个文件；消息不在会话里或文件已不在素材夹里都整条失败。 */
  'chat.openMedia': {
    params: { shell?: ChatShell; characterId?: string; id: string };
    result: { opened: boolean; path: string };
  };
  /** 纯色底那十颗色板：直接来自 DesignSystem 的调色板 XAML，前端不再自带一份。 */
  'palette.swatches': {
    params: Record<string, never>;
    result: { swatches: { hex: string; name: string }[] };
  };
  /** 一个角色的全部细节：档案 + 状态 + 立绘清单。null = 没有这个角色。 */
  'characters.get': {
    params: { id: string };
    result: {
      id: string;
      name: string;
      nickname: string;
      gender: string;
      age: number | null;
      catchphrase: string;
      avatarUrl: string;
      outfit: string;
      emotion: string;
      affection: number;
      trust: number;
      energy: number;
      emotions: { outfit: string; emotion: string; url: string }[];
    } | null;
  };
  /** 设置里的 AI 厂商与模型（绝不含密钥）。 */
  'ai.providers': {
    params: Record<string, never>;
    result: {
      providers: {
        name: string;
        endpoint: string;
        enabled: boolean;
        timeoutSeconds: number;
        keyCount: number;
        models: {
          model: string;
          label: string;
          enabled: boolean;
          vision: boolean;
          tools: boolean;
          json: boolean;
          contextWindow: number;
        }[];
      }[];
    };
  };
  /** 已装插件。 */
  'plugins.list': {
    params: Record<string, never>;
    result: {
      plugins: {
        id: string;
        name: string;
        version: string;
        description: string;
        author: string;
        enabled: boolean;
        permissions: string[];
        settingsIssue: string;
        missing: string[];
      }[];
    };
  };
  /** 开发者展示案。 */
  'showcases.list': {
    params: Record<string, never>;
    result: {
      showcases: {
        id: string;
        name: string;
        description: string;
        characterName: string;
        steps: number;
        backgroundUrl: string;
        updatedAt: string;
      }[];
    };
  };
  /** 平行宇宙（导入的小说世界）。 */
  'novels.list': {
    params: Record<string, never>;
    result: {
      novels: {
        id: string;
        title: string;
        summary: string;
        characters: string[];
        branches: number;
        importedAt: string;
      }[];
    };
  };
  /** 世界书（世界树）。 */
  'worldbook.list': {
    params: Record<string, never>;
    result: {
      worlds: { id: string; title: string; description: string; mode: string; characters: number; createdAt: string }[];
    };
  };
  /** 某个角色身上的记忆标签。 */
  'memory.tags': {
    params: { charId: string };
    result: { tags: { name: string; subject: string; count: number }[] };
  };
  /** 角色库真表。 */
  'characters.list': {
    params: Record<string, never>;
    result: { characters: CharacterRow[] };
  };
  'characters.importAvatar': {
    params: { id: string };
    result: { canceled: boolean; avatarUrl: string };
  };
  /** 新建角色并落盘。名字与性别是唯一门槛（其余留空落 CreateDefault 的出厂值）。 */
  'character.create': {
    params: {
      name: string;
      gender: string;
      personality?: string;
      description?: string;
      greeting?: string;
      userAddress?: string;
      nickname?: string;
      birthday?: string;
      naturalAging?: boolean;
      romance?: boolean;
    };
    result: { id: string; name: string; gender: string };
  };
  /** 工作室保存：只写传了的那些字段，盘上别的字段不动。 */
  'character.save': {
    params: { id: string; fields: CharacterFields };
    result: { id: string; name: string; written: string[] };
  };
  /** 删除角色（资料 + 立绘目录）。存档里引用过它的人不会一起删。 */
  'character.delete': {
    params: { id: string };
    result: { id: string; message: string };
  };
  /** 打包单个角色并交给系统分享面板。 */
  'character.export': {
    params: { id: string };
    result: { fileName: string; path: string; sizeLabel: string; shared: boolean; message: string };
  };
};

/**
 * 工作室表单能改的字段（character.save 的白名单）。
 * 键名就是页面那一份 Fields 的键；按「传了哪个写哪个」合并，整份覆盖会把页面没读出来的字段冲成空。
 */
export type CharacterFields = {
  // 设定
  name?: string;
  nickname?: string;
  userAddress?: string;
  gender?: string;
  personality?: string;
  description?: string;
  greeting?: string;
  moral?: string;
  life?: string;
  biography?: string;
  political?: string;
  ideology?: string;
  deepMemories?: string;
  trauma?: string;
  /** 年龄：空串 = 没填 */
  age?: string;
  /** 生日：MM-dd */
  birthday?: string;
  naturalAging?: boolean;
  romance?: boolean;
  // 状态
  /** 好感点数 0..1000 */
  affection?: number;
  trust?: number;
  energy?: number;
  libido?: number;
  libidoLocked?: boolean;
  libidoLockValue?: number;
  mood?: string;
  location?: string;
  cycleTracked?: boolean;
  cycleDay?: string;
  cyclePhase?: string;
  // 这一个角色的 AI 参数（空串 = 跟随全局）
  ovMemoryTurns?: string;
  ovContextLimit?: string;
  ovTemperature?: string;
  ovWindowDays?: string;
  ovRecall?: string;
  // 这一个角色的声音（空串 = 跟随全局）
  voiceEnabled?: boolean;
  voiceEngine?: string;
  voiceName?: string;
  voiceExtra?: string;
  voiceApiUrl?: string;
  voiceApiKey?: string;
  voiceApiMode?: string;
  voiceTtsModel?: string;
};

/** 存档一行（saves.list）。 */
export type SaveRow = {
  id: string;
  label: string;
  character: string;
  scene: string;
  /** ISO-8601 */
  savedAt: string;
  minutes: number;
  world: string;
  parentId?: string | null;
  branch: string;
  historyNote: string;
};

/** 微信 / QQ / 主界面三面会话共用一枚壳名。 */
export type ChatShell = 'main' | 'wechat' | 'qq';

/** 会话名册的一行（chat.contacts）。 */
export type ChatContactRow = {
  id: string;
  name: string;
  /** 可直接放进 <img src> 的素材地址；空 = 没有头像 */
  avatarUrl: string;
  /** 最近一条的摘要；没有就是「还没有聊过」 */
  last: string;
  /** 右上角时间（今天给时分，昨天给「昨天」）；没有最后一条就是空串 */
  time: string;
  /** 有没有未读（等价于 unreadCount > 0，留着给只认布尔的地方） */
  unread: boolean;
  /** 未读条数：会话行上的数字角标；超过 99 由前端收成「99+」 */
  unreadCount: number;
  /** 加好友时写的备注；空 = 没写 */
  note: string;
  /** friend = 好友表里的（能删）；roster = 只是这一档的角色，补在名册后面 */
  source: 'friend' | 'roster';
  /** 能不能删：只有好友表里的行能删 */
  removable: boolean;
};

/** 一条消息的形态：文字、图片、表情、文件。老存档没有这个字段，宿主一律按 text 读。 */
export type ChatMediaKind = 'text' | 'image' | 'sticker' | 'file';

/** 会话里的一条消息（chat.history / chat.send）。 */
export type ChatRow = {
  id: string;
  text: string;
  /** true = 自己发的 */
  mine: boolean;
  sender: string;
  /** ISO-8601 */
  at: string;
  /** 这条消息是什么：text 画文字气泡，image 画图片，sticker 先用文字标记，file 画文件卡片 */
  kind: ChatMediaKind;
  /** 媒体文件的素材地址（https://wab.assets/…）；文字消息是空串 */
  mediaUrl: string;
  /** 气泡里先用这一张（缓存没有时宿主给的是原图地址）；空 = 没有可用图 */
  thumbUrl: string;
  /** 用户选中的原文件名；只有文件与图片消息有 */
  fileName: string;
  /** 媒体文件的字节数；文字消息是 0 */
  size: number;
};

/** 这一档的插件/工具/角色白名单（save.rules.get）。 */
export type SaveRules = {
  id: string;
  label: string;
  character: string;
  rules: SaveRulesEdit;
  /** 角色库真表；main = 这一档的主角 */
  characters: { id: string; name: string; main: boolean }[];
  plugins: { id: string; name: string; version: string; enabled: boolean }[];
  tools: { name: string; description: string }[];
};

/** 写回去的那一份白名单；items 里没给的项表示「没改」。 */
export type SaveRulesEdit = {
  allowAllPlugins: boolean;
  allowAllTools: boolean;
  allowAiInstallPlugins: boolean;
  /** allowAllCharacters = true 时这份一定是空表（空 = 不限制） */
  allowedCharacters: string[];
  allowedPlugins?: string[];
  allowedTools?: string[];
  allowAllCharacters: boolean;
};

/** 角色一行（characters.list）。 */
export type CharacterRow = {
  id: string;
  name: string;
  nickname: string;
  gender: string;
  age: number | null;
  catchphrase: string;
  avatar: string;
  /** 可直接放进 <img src> 的素材地址（宿主把存储根挂在 wab.assets 上）；空 = 没有素材 */
  avatarUrl: string;
  /** 立绘总数（SpriteMap 的条目数） */
  spriteCount: number;
  /** 有哪些服装 */
  outfits: string[];
  /** 当前该显示的那张立绘；空 = 这个角色还没有立绘 */
  currentSpriteUrl: string;
  affection: number;
  trust: number;
  energy: number;
  /** 安全感 0–1000（与好感同刻度）。 */
  security?: number;
  mood: string;
};

export type Method = keyof Requests;
export type Params<M extends Method> = Requests[M]['params'];
export type Result<M extends Method> = Requests[M]['result'];

export type RequestEnvelope<M extends Method = Method> = {
  id: number;
  method: M;
  params: Params<M>;
};

export type ErrorCode =
  | 'E_BACKEND'
  | 'E_UNKNOWN_METHOD'
  | 'E_BAD_PARAMS'
  | 'E_REJECTED'
  | 'E_TIMEOUT'
  | 'E_TRANSPORT'
  /** 这台设备没有这条通路（没麦克风、没配识别模型、朗读关着）：不是错误代码的问题，是能力不在。 */
  | 'E_UNAVAILABLE'
  /** 上一条还占着同一条通路（比如已经在听上一句）。 */
  | 'E_BUSY';

export type ErrorBody = {
  code: ErrorCode;
  /** 给人看的一句话，直接上屏 */
  message: string;
  /** 给日志的细节，可无 */
  detail?: string;
};

export type ResponseEnvelope<M extends Method = Method> =
  | { id: number; ok: true; result: Result<M> }
  | { id: number; ok: false; error: ErrorBody };

/** 后端主动推给前端的消息。 */
export type ServerEvents = {
  'nav.request': { route: string };
  'appearance.changed': { appearance: Appearance };
  'settings.changed': { values: SettingsValues };
  /** 有存档/角色写盘时宿主推一条，页面据此重读（跨窗口联动靠它）。 */
  'data.changed': { what: 'saves' | 'characters' };
  'theme.tokens': { tokens: TokenMap };
  /** 原生侧发生的事（导入成功、存档写入、警告）：条的画法归前端，宿主只给内容。 */
  'ui.toast': { title: string; body: string; code: string; kind: 'info' | 'success' | 'warn' | 'error'; glyph: string };
  /**
   * 她发来一条手机消息（PhoneMessageService.PublishAsync 落盘之后推）。
   * 前端据此刷新未读角标；那一格会话没开着时再弹一条提示，正文就是 preview（消息本身的截断）。
   */
  'chat.arrived': { characterId: string; preview: string; name: string };
  /** 有人把某一格会话标成已读了（打开会话 / 在里面说话）：所有窗的角标都要跟着重读。 */
  'chat.unread.changed': { what: 'read' };
};

export type EventName = keyof ServerEvents;
export type EventEnvelope<E extends EventName = EventName> = {
  event: E;
  payload: ServerEvents[E];
};

export type Incoming = ResponseEnvelope | EventEnvelope;
