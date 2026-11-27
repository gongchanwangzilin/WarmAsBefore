import type { ReactNode } from 'react';
import { Glass } from '../Glass';

type Props = {
  tone: 'empty' | 'error';
  title: string;
  body: ReactNode;
  /** 恢复动作；空状态也永远给一条能往前走的路 */
  action?: string;
  onAction?: () => void;
  detail?: string;
  busy?: boolean;
};

/**
 * 空与错共用一块玻璃，但说的是两件事：
 * 空状态教用户怎么开始，错误状态说清哪里断了、走哪条路回来。
 */
export function StateBlock({ tone, title, body, action, onAction, detail, busy }: Props) {
  return (
    <Glass as="section" tone="card" radius={12} className={`card state state--${tone}`}>
      <p className="state__eyebrow">{tone === 'error' ? '连接断了' : '从这里开始'}</p>
      <h2 className="state__title">{title}</h2>
      <div className="state__body">{body}</div>
      {detail ? <p className="state__detail">{detail}</p> : null}
      {action && onAction ? (
        <div className="state__actions">
          <button type="button" className="ghost" onClick={onAction} disabled={busy} aria-busy={busy || undefined}>
            {busy ? '正在重试…' : action}
          </button>
        </div>
      ) : null}
      <span className="sr-only" role="status">
        {tone === 'error' ? `${title}。${detail ?? ''}` : title}
      </span>
    </Glass>
  );
}
