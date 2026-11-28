import type { Appearance, SettingsValues } from '../bridge/protocol';

export const DEFAULT_APPEARANCE: Appearance = {
  alpha: 0.85,
  frost: 0.5,
  liquid: 0.4,
  lightweight: false,
  reducedTransparency: false,
  background: { mode: 'solid', solidHex: '', pageTintHex: '', imageLabel: '尚未选择图片' },
  scene: { experimental: false, weather: true, light: true, audio: true, audioLevel: 0.6, weatherKind: 'Sunny', weatherIntensity: 0.15 },
};

/** 假后端的示例设置；摘要文案的样子全由这几个数决定（见 lib/settingsHome.ts）。 */
export const DEFAULT_SETTINGS: SettingsValues = {
  appearance: { liquid: true, glass: true, frost: true, theme: 'endfield' },
  sound: { bgm: 0.7, sfx: 0.8 },
  text: { scale: 1 },
  story: { autoSave: true },
  language: { locale: 'zh-CN' },
  ai: { providers: 2, smartSaving: false, timeoutSeconds: 60 },
  memory: { windowDays: 7, contextLimit: '32K' },
  ui: { orientation: 'auto', chatStyle: 'galgame', difficulty: 'normal' },
  platform: { petIdleMinutes: 10, preferDiscreteGpu: false },
  integrations: { qq: false, wechat: false, mcp: true },
  life: { notifications: true, weatherCity: '杭州', customWeatherSource: false },
  data: { skillsInstalled: false },
};


/**
 * 把后端回的那份设置补齐成完整形状。
 *
 * 之前设置页直接读 values.appearance.liquid：宿主少回一组就抛异常，整页被卸载、窗口只剩空底。
 * 数据形状不一致是宿主的问题，但空白屏不该是用户看到的结果 —— 缺的组一律落回出厂值。
 */
export function fillSettings(values: Partial<SettingsValues> | null): SettingsValues | null {
  if (!values) return null;
  const merged = { ...DEFAULT_SETTINGS } as SettingsValues;
  for (const key of Object.keys(DEFAULT_SETTINGS) as (keyof SettingsValues)[]) {
    const incoming = values[key];
    if (incoming && typeof incoming === 'object') {
      merged[key] = { ...(DEFAULT_SETTINGS[key] as object), ...(incoming as object) } as never;
    }
  }
  return merged;
}
