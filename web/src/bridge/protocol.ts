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

export type GlassPalette = {
  /** 玻璃透明度 0..1，驱动 --w-face-k */
  alpha: number;
  /** 磨砂程度 0..1，驱动 backdrop blur */
  frost: number;
  /** 液态高度 0..1，驱动柱面带高度 */
  liquid: number;
  /** 轻量模式：撤掉指针光与背景响应 */
  lightweight: boolean;
};

/** 标题页「上次的存档」卡。 */
export type SaveSummary = {
  character: string;
  scene: string;
  /** ISO-8601 */
  savedAt: string;
  minutes: number;
};

export type SettingsValues = {
  glass: GlassPalette;
  text: { scale: number };
  sound: { enabled: boolean };
  story: { autoSave: boolean };
  language: { locale: 'zh-CN' | 'en-US' };
};

/** settings.set 的载荷按 key 收窄，写错类型在编译期就报。 */
export type SettingEdit =
  | { key: 'glass'; value: GlassPalette }
  | { key: 'text.scale'; value: number }
  | { key: 'sound.enabled'; value: boolean }
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
  'palette.get': {
    params: Record<string, never>;
    /** palette 为 null = 这套设备还没有下发光谱（空状态） */
    result: { palette: GlassPalette | null };
  };
  'palette.set': {
    params: { palette: GlassPalette };
    result: { palette: GlassPalette };
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
  'palette.changed': { palette: GlassPalette };
  'settings.changed': { values: SettingsValues };
  'theme.tokens': { tokens: TokenMap };
};

export type EventName = keyof ServerEvents;
export type EventEnvelope<E extends EventName = EventName> = {
  event: E;
  payload: ServerEvents[E];
};

export type Incoming = ResponseEnvelope | EventEnvelope;
