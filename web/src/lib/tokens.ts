import type { TokenMap } from '../bridge/protocol';

/** 服务端下发的令牌是唯一来源；这里只负责把它们挂到 :root 上。 */
export function applyTokens(tokens: TokenMap, scope: HTMLElement = document.documentElement): void {
  for (const [name, value] of Object.entries(tokens)) scope.style.setProperty(name, value);
}

/**
 * 一次预览要把 :root 上那份令牌原样退回去：先抄一份，换色失败时按它还原。
 * 抄的是**所有** --w-*（包括这次换色会新加进来的键），所以还原时也能把多出来的删掉。
 */
export type TokenSnapshot = { name: string; value: string }[];

export function snapshotTokens(scope: HTMLElement = document.documentElement): TokenSnapshot {
  return Array.from(scope.style)
    .filter((name) => name.startsWith('--w-'))
    .map((name) => ({ name, value: scope.style.getPropertyValue(name) }));
}

/** 按快照还原：先删掉这次新加的键，再把抄下来的值写回去。 */
export function restoreTokens(snapshot: TokenSnapshot, scope: HTMLElement = document.documentElement): void {
  const keep = new Set(snapshot.map((entry) => entry.name));
  for (const name of Array.from(scope.style))
    if (name.startsWith('--w-') && !keep.has(name)) scope.style.removeProperty(name);
  for (const { name, value } of snapshot) scope.style.setProperty(name, value);
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
