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
  };
};

/** 标题页「上次的存档」卡。 */
export type SaveSummary = {
  character: string;
  scene: string;
  /** ISO-8601 */
  savedAt: string;
  minutes: number;
};

export type ThemeName = 'classic' | 'sakura' | 'bamboo' | 'mist' | 'endfield';

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
  ai: { providers: number; smartSaving: boolean; timeoutSeconds: number };
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
  'appearance.get': {
    params: Record<string, never>;
    /** appearance 为 null = 这套设备还没有下发外观（空状态） */
    result: { appearance: Appearance | null };
  };
  'appearance.set': {
    params: { appearance: Appearance };
    result: { appearance: Appearance };
  };
  'nav.open': {
    params: { route: string };
    result: { accepted: boolean };
  };
  'nav.back': {
    params: { from: string };
    result: { accepted: boolean };
  };
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
  | 'E_TRANSPORT';

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
  'theme.tokens': { tokens: TokenMap };
};

export type EventName = keyof ServerEvents;
export type EventEnvelope<E extends EventName = EventName> = {
  event: E;
  payload: ServerEvents[E];
};

export type Incoming = ResponseEnvelope | EventEnvelope;
