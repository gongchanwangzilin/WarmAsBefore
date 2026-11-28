import { useEffect, useRef } from 'react';

/**
 * Esc 关掉当前这层：面板、弹窗、整屏观察页都该吃这一下。
 * 只在打开时挂监听；处理函数存在 ref 里，重渲染不会反复摘挂。
 */
export function useEscape(active: boolean, onEscape: () => void): void {
  const handler = useRef(onEscape);
  handler.current = onEscape;

  useEffect(() => {
    if (!active) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return;
      event.stopPropagation();
      handler.current();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [active]);
}
