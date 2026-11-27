import type { TokenMap } from '../bridge/protocol';

/** 服务端下发的令牌是唯一来源；这里只负责把它们挂到 :root 上。 */
export function applyTokens(tokens: TokenMap, scope: HTMLElement = document.documentElement): void {
  for (const [name, value] of Object.entries(tokens)) scope.style.setProperty(name, value);
}

/**
 * 减少动态：时长归零，颜色/字阶不动。
 * 必须在这里改而不能只写 CSS 媒体查询 —— 令牌是写在元素上的内联样式，权重比 :root 规则高。
 */
export function dampingTokens(tokens: TokenMap, reduced: boolean): TokenMap {
  if (!reduced) return tokens;
  return {
    ...tokens,
    '--w-dur-press': '1ms',
    '--w-dur-state': '1ms',
    '--w-dur-page': '1ms',
  };
}
