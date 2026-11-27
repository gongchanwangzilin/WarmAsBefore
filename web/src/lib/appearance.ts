import type { BackgroundMode } from '../bridge/protocol';

/** 十颗纯色底：四颗中性、两颗冷、两颗暖、两颗深（SettingsViewModel.SolidPalette）。 */
export const SOLID_SWATCHES = [
  { hex: '#FDF8F0', name: '奶油纸' },
  { hex: '#E8E6E1', name: '素灰' },
  { hex: '#DDE6F0', name: '晨雾蓝' },
  { hex: '#CBDCD8', name: '青瓷' },
  { hex: '#F3E3CF', name: '暖砂' },
  { hex: '#F6E1E6', name: '樱雾' },
  { hex: '#3A3D42', name: '石墨' },
  { hex: '#2C3A4E', name: '幽蓝' },
  { hex: '#4A3527', name: '赭石' },
  { hex: '#22322A', name: '墨绿' },
] as const;

/** 背景模式下拉的三条（BackgroundModeChoices）。 */
export const BACKGROUND_MODE_CHOICES: ReadonlyArray<{ mode: BackgroundMode; label: string }> = [
  { mode: 'solid', label: '纯色（默认）' },
  { mode: 'image', label: '自定义图片' },
  { mode: 'clear', label: '窗口透视（看穿到桌面）' },
];

/** 摘要在色板行下面回执一句现在是什么色（BackgroundSolidHint / PageTintHint）。 */
export function solidHint(hex: string): string {
  if (!hex) return '背景跟着主题走';
  const named = SOLID_SWATCHES.find((swatch) => swatch.hex.toLowerCase() === hex.toLowerCase());
  return named ? `背景：${named.name} ${named.hex}` : `背景：${hex}`;
}

export function pageTintHint(hex: string): string {
  return hex ? `页面：${hex}` : '页面跟着色板走';
}

/** 纯色档与页面色两块只在「背后是这块地面」时才有意义（BackgroundSolidVisible）。 */
export const solidVisible = (mode: BackgroundMode) => mode !== 'image' && mode !== 'clear';
