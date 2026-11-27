import type { ReactNode } from 'react';

/**
 * 一页的滚动容器。顶栏 sticky 在它里面，所以每页各滚各的，
 * 过场时旧页停在它原来的位置，不跟着新页回顶。
 */
export function PageShell({ children }: { children: ReactNode }) {
  return <div className="page">{children}</div>;
}
