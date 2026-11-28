import { DEFAULT_APPEARANCE, DEFAULT_SETTINGS } from '../lib/defaults';
import { SOLID_SWATCHES } from '../lib/appearance';
import { MOCK_TOKENS } from './tokens';
import type {
  Appearance,
  ErrorBody,
  Params,
  RequestEnvelope,
  ResponseEnvelope,
  SaveSummary,
  SettingsValues,
} from './protocol';

/**
 * 内存里的假后端。浏览器没有 WebView2 也没有 Android 注入时，
 * client 会回落到这里，于是同一份前端能在 Chrome 里独立跑。
 *
 * 故障开关只影响「页面级」请求（settings.get / appearance.get / *.set），
 * ui.tokens 永远成功 —— 令牌拿不到的话整个页面连颜色都没有，那是启动失败，不是页面状态。
 */

export type MockLatency = 0 | 320 | 1200;
export type MockFailure = 'none' | 'load' | 'write';
export type MockFaults = {
  latency: MockLatency;
  failure: MockFailure;
  /** 让后端「什么都没注册」：values/appearance 返回 null，页面走空状态 */
  empty: boolean;
};

const mockFaults: MockFaults = { latency: 0, failure: 'none', empty: false };

const KNOWN_ROUTES = new Set(['/', '/settings', '/settings/appearance']);

/** 假后端没有存档表：标题页那张「上次的存档」卡只摆时间与时长，角色与场景留空。 */
const SAMPLE_SAVE: SaveSummary = {
  character: '',
  scene: '',
  savedAt: '2026-11-26T22:41:00+08:00',
  minutes: 147,
};

const store: { appearance: Appearance; values: SettingsValues; save: SaveSummary; preset: string } = {
  appearance: structuredClone(DEFAULT_APPEARANCE),
  values: structuredClone(DEFAULT_SETTINGS),
  save: SAMPLE_SAVE,
  preset: 'ochre',
};

/**
 * 五套预设配色（与 C# 的 DesignSystem/Theme/PalettePresets.cs 同一批种子）。
 * 两颗预览色是真机上 palette.swatches 现算出来的值 —— 浏览器这边没有 Lock()，
 * 所以 palette.set 只换选中态，整屏配色仍按 MOCK_TOKENS 那一份：不假装重算了一整套。
 */
const MOCK_PRESETS = [
  { id: 'ochre', name: '赭石', note: '暖赭印在纸白上', accent: '#d48f5e', surface: '#32302c' },
  { id: 'slate', name: '墨青', note: '青灰底压一点砖红', accent: '#72aac0', surface: '#2b2d2f' },
  { id: 'sakura', name: '樱灰', note: '淡樱挨着石墨', accent: '#964a5e', surface: '#fdfdfe' },
  { id: 'moss', name: '苔绿', note: '苔绿长在暖砂上', accent: '#4c793c', surface: '#fdfcf9' },
  { id: 'amber', name: '夜珀', note: '最暗处一点琥珀', accent: '#d4ad5e', surface: '#2c2a29' },
];

const ok = (id: number, result: unknown): ResponseEnvelope =>
  ({ id, ok: true, result }) as ResponseEnvelope;

const fail = (id: number, error: ErrorBody): ResponseEnvelope => ({ id, ok: false, error });

function editSettings(edit: Params<'settings.set'>): void {
  switch (edit.key) {
    case 'text.scale':
      store.values.text = { scale: edit.value };
      break;
    case 'story.autoSave':
      store.values.story = { autoSave: edit.value };
      break;
    case 'language.locale':
      store.values.language = { locale: edit.value };
      break;
  }
}

const handlers = {
  'ui.tokens': () => ({ tokens: MOCK_TOKENS, theme: 'light' as const }),
  'palette.swatches': () => ({
    presets: MOCK_PRESETS,
    swatches: SOLID_SWATCHES.map((swatch) => ({ hex: swatch.hex, name: swatch.name })),
  }),
  'palette.get': () => ({ preset: store.preset }),
  'palette.set': (params: Params<'palette.set'>) => {
    const preset = MOCK_PRESETS.find((row) => row.id === params.id);
    if (!preset) {
      throw { code: 'E_BAD_PARAMS' as const, message: `没有这套配色：${params.id}。` };
    }
    store.preset = preset.id;
    return { id: preset.id, name: preset.name, note: preset.note, tokens: MOCK_TOKENS };
  },
  'settings.get': () => ({
    values: mockFaults.empty ? null : store.values,
    save: mockFaults.empty ? null : store.save,
  }),
  'settings.set': (params: Params<'settings.set'>) => {
    editSettings(params);
    return { values: store.values };
  },
  'appearance.get': () => ({ appearance: mockFaults.empty ? null : store.appearance }),
  'appearance.set': (params: Params<'appearance.set'>) => {
    if (!Number.isFinite(params.appearance.alpha)) {
      throw { code: 'E_BAD_PARAMS', message: '玻璃透明度不是数字。' } satisfies ErrorBody;
    }
    store.appearance = structuredClone(params.appearance);
    return { appearance: store.appearance };
  },
  'nav.open': (params: Params<'nav.open'>) => ({ accepted: KNOWN_ROUTES.has(params.route) }),
  'nav.back': () => ({ accepted: true }),
  // 假后端没有麦克风也没有朗读引擎：照实说不能听，按钮就停在禁用态，而不是假装录了一段。
  'voice.status': () => ({
    sttEnabled: true,
    sttEngine: 'system',
    ttsEnabled: true,
    ttsEngine: 'system',
    canListen: false,
    note: '浏览器里的假后端没有录音通道',
  }),
  'voice.listen': () => {
    throw { code: 'E_UNAVAILABLE', message: '浏览器里的假后端没有录音通道' } satisfies ErrorBody;
  },
  'voice.cancel': () => ({ stopped: true }),
  'voice.speak': () => {
    throw { code: 'E_UNAVAILABLE', message: '浏览器里的假后端没有朗读引擎' } satisfies ErrorBody;
  },
};

const sleep = (ms: number) => new Promise<void>((r) => window.setTimeout(r, ms));

export async function mockHandle(raw: string): Promise<string> {
  let req: RequestEnvelope;
  try {
    req = JSON.parse(raw) as RequestEnvelope;
  } catch {
    return JSON.stringify(fail(0, { code: 'E_BAD_PARAMS', message: '请求不是合法 JSON。' }));
  }

  await sleep(mockFaults.latency + 140);

  const isLoad = req.method === 'settings.get' || req.method === 'appearance.get';
  const isWrite = req.method === 'settings.set' || req.method === 'appearance.set';

  if (mockFaults.failure === 'load' && isLoad) {
    return JSON.stringify(
      fail(req.id, {
        code: 'E_BACKEND',
        message: '本地服务没有应答。',
        detail: 'mock: failure=load',
      }),
    );
  }
  if (mockFaults.failure === 'write' && isWrite) {
    return JSON.stringify(
      fail(req.id, {
        code: 'E_BACKEND',
        message: '保存没有写进去。',
        detail: 'mock: failure=write',
      }),
    );
  }

  try {
    const handler = handlers[req.method as keyof typeof handlers];
    if (!handler) {
      return JSON.stringify(
        fail(req.id, { code: 'E_UNKNOWN_METHOD', message: `没有这个方法：${req.method}` }),
      );
    }
    const result = (handler as (params: unknown) => unknown)(req.params);
    return JSON.stringify(ok(req.id, result));
  } catch (error) {
    const body =
      typeof error === 'object' && error !== null && 'code' in error
        ? (error as ErrorBody)
        : { code: 'E_BACKEND' as const, message: '假后端自己抛了一手。' };
    return JSON.stringify(fail(req.id, body));
  }
}
