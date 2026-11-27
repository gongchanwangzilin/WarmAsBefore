import type { TokenMap } from './protocol';

/**
 * mock 后端下发的那一份令牌，数值抄自 spike/web-glass/src/styles/tokens.css。
 * 真机上这份表由 `ui.tokens` 从 C# 侧发来，前端不认识任何一个具体颜色。
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

  '--w-bg': '#0b0c0e',
  '--w-ink-seed': '#7a7670',
  '--w-ink-signal-seed': '#1a1406',
  '--w-surface-tint': '#2b2723',
  '--w-signal': '#fad542',
  '--w-signal-hi': '#ffe071',
  '--w-signal-lo': '#d9a916',
  '--w-signal-edge': 'rgba(255, 234, 140, 0.5)',
  '--w-signal-glow': 'rgba(250, 212, 31, 0.24)',
  '--w-hairline': 'rgba(255, 255, 255, 0.1)',
  '--w-edge': 'rgba(255, 255, 255, 0.62)',
  '--w-focus-ring': '#ffe071',

  '--w-tint-card': 'rgba(255, 255, 255, 0.03)',
  '--w-tint-bar': 'rgba(255, 255, 255, 0.035)',
  '--w-tint-button': 'rgba(255, 255, 255, 0.05)',
  '--w-tint-signal': 'rgba(250, 213, 66, 0.9)',
  '--w-tint-track': 'rgba(255, 255, 255, 0.07)',
  '--w-tint-knob': 'rgba(255, 255, 255, 0.12)',
  '--w-tint-thumb': 'rgba(255, 255, 255, 0.06)',

  '--w-ground-a': '#2c2d30',
  '--w-ground-b': '#191a1d',
  '--w-ground-c': '#0b0c0e',
  '--w-ground-warm': 'rgba(250, 212, 31, 0.2)',
  '--w-ground-warm-mid': 'rgba(196, 150, 60, 0.1)',
  '--w-ground-warm-end': 'rgba(196, 150, 60, 0)',
  '--w-ground-cool': 'rgba(58, 92, 112, 0.26)',
  '--w-ground-cool-mid': 'rgba(58, 92, 112, 0.09)',
  '--w-ground-cool-end': 'rgba(58, 92, 112, 0)',
  '--w-ground-light': 'rgba(255, 214, 120, 0.18)',
  '--w-ground-light-end': 'rgba(255, 214, 120, 0)',
  '--w-vignette': 'rgba(0, 0, 0, 0.45)',
};
