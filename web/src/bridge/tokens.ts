import type { TokenMap } from './protocol';

/**
 * mock 后端下发的那一份令牌。真机上这份表由 `ui.tokens` 从 C# 侧发来，前端不认识任何一个具体颜色。
 *
 * 数值是按「铜版」色板在深色档现算出来的：纸 #27231B、卡面 #32302C、印墨（赭）#D48F5E、
 * 信号（铜绿）#8DC4B9、六级中性阶 #F2F2F1 → #3E3C37。浅色档由 StyleWorld 在同一套种子上另算一遍，
 * 前端拿到的永远只是值，不是规则。
 */
export const MOCK_TOKENS: TokenMap = {
  '--w-font-ui':
    "'Segoe UI Variable Text', 'Segoe UI', 'PingFang SC', 'Microsoft YaHei UI', 'Noto Sans SC', system-ui, sans-serif",
  '--w-font-num':
    "'Segoe UI Variable Display', 'Segoe UI', 'PingFang SC', 'Microsoft YaHei UI', system-ui, sans-serif",

  '--w-size-display': '30px',
  '--w-size-title': '20px',
  '--w-size-subhead': '15.5px',
  '--w-size-body': '14.5px',
  '--w-size-small': '13px',
  '--w-size-caption': '11.5px',

  '--w-space-1': '4px',
  '--w-space-2': '8px',
  '--w-space-3': '14px',
  '--w-space-4': '18px',
  '--w-pad-card': '16px',
  '--w-pad-page': '24px',
  '--w-row-h': '41px',
  '--w-header-h': '45px',

  '--w-radius-well': '4px',
  '--w-radius-card': '12px',
  '--w-radius-button': '8px',

  '--w-dur-press': '120ms',
  '--w-dur-state': '180ms',
  '--w-dur-page': '320ms',
  '--w-ease-out': 'cubic-bezier(0.22, 1, 0.36, 1)',
  '--w-ease-settle': 'cubic-bezier(0.34, 1.3, 0.64, 1)',

  '--w-bg': '#27231b',
  '--w-ink-seed': '#9a9996',
  '--w-ink-signal-seed': '#2f1f15',
  '--w-surface-tint': '#32302c',

  /* 印墨（赭）：主行动、当前项、图形笔画 */
  '--w-accent': '#d48f5e',
  '--w-accent-hi': '#d99c71',
  '--w-accent-lo': '#b2784f',
  '--w-accent-ink': '#2f1f15',
  '--w-accent-edge': 'rgba(212, 143, 94, 0.5)',
  '--w-accent-glow': 'rgba(212, 143, 94, 0.24)',

  /* 信号（铜绿）：只给状态与读数 */
  '--w-status': '#8dc4b9',
  '--w-status-fg': '#a8dcd1',
  '--w-status-ink': '#000000',
  '--w-status-edge': 'rgba(141, 196, 185, 0.42)',
  '--w-status-glow': 'rgba(141, 196, 185, 0.18)',
  '--w-status-tint': 'rgba(141, 196, 185, 0.12)',

  /* 旧名：一直指的是印墨，保留成别名（页面里六十处装饰用它） */
  '--w-signal': '#d48f5e',
  '--w-signal-hi': '#d99c71',
  '--w-signal-lo': '#b2784f',
  '--w-signal-edge': 'rgba(212, 143, 94, 0.5)',
  '--w-signal-glow': 'rgba(212, 143, 94, 0.24)',

  /* 六级中性阶：1 标题 → 6 发丝线 */
  '--w-ramp-1': '#f2f2f1',
  '--w-ramp-2': '#e5e5e4',
  '--w-ramp-3': '#d4d3d1',
  '--w-ramp-4': '#b6b4b2',
  '--w-ramp-5': '#868480',
  '--w-ramp-6': '#413f3b',

  '--w-hairline': '#413f3b',
  '--w-edge': '#575552',
  '--w-focus-ring': '#8dc4b9',

  '--w-tint-card': 'rgba(255, 255, 255, 0.03)',
  '--w-tint-bar': 'rgba(255, 255, 255, 0.035)',
  '--w-tint-button': 'rgba(255, 255, 255, 0.05)',
  '--w-tint-signal': 'rgba(212, 143, 94, 0.9)',
  '--w-tint-track': 'rgba(255, 255, 255, 0.07)',
  '--w-tint-knob': 'rgba(255, 255, 255, 0.12)',
  '--w-tint-thumb': 'rgba(255, 255, 255, 0.06)',

  '--w-ground-a': '#32302c',
  '--w-ground-b': '#27231b',
  '--w-ground-c': '#231e13',
  '--w-ground-warm': 'rgba(212, 143, 94, 0.2)',
  '--w-ground-warm-mid': 'rgba(212, 143, 94, 0.1)',
  '--w-ground-warm-end': 'rgba(212, 143, 94, 0)',
  '--w-ground-cool': 'rgba(141, 196, 185, 0.26)',
  '--w-ground-cool-mid': 'rgba(141, 196, 185, 0.09)',
  '--w-ground-cool-end': 'rgba(141, 196, 185, 0)',
  '--w-ground-light': 'rgba(212, 143, 94, 0.18)',
  '--w-ground-light-end': 'rgba(212, 143, 94, 0)',
  '--w-vignette': 'rgba(0, 0, 0, 0.45)',
};
