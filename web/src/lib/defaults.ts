import type { Appearance, SettingsValues } from '../bridge/protocol';

export const DEFAULT_APPEARANCE: Appearance = {
  alpha: 0.85,
  frost: 0.5,
  liquid: 0.4,
  lightweight: false,
  reducedTransparency: false,
  background: { mode: 'solid', solidHex: '', pageTintHex: '', imageLabel: '尚未选择图片' },
  scene: { experimental: false, weather: true, light: true, audio: true, audioLevel: 0.6 },
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
