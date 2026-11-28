import type { ReactNode } from 'react';

/**
 * 源码里有、Web 端却没有对应桥方法的那颗钮：画成禁用态，点了不会有假动作。
 * 恢复它需要在 bridge/protocol.ts 里加方法（见体检报告），这里不代填文案。
 */
export function Inert({ children }: { children: ReactNode }) {
  return (
    <span inert aria-disabled="true" style={{ display: 'inline-grid', opacity: 0.45 }}>
      {children}
    </span>
  );
}
