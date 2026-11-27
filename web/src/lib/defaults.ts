import type { GlassPalette, SettingsValues } from '../bridge/protocol';

export const DEFAULT_PALETTE: GlassPalette = {
  alpha: 0.85,
  frost: 0.5,
  liquid: 0.4,
  lightweight: false,
};

export const DEFAULT_SETTINGS: SettingsValues = {
  glass: DEFAULT_PALETTE,
  text: { scale: 1 },
  sound: { enabled: true },
  story: { autoSave: true },
  language: { locale: 'zh-CN' },
};
