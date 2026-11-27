import type { ReactNode } from 'react';
import { Glass } from './Glass';

export function Section({
  title,
  note,
  children,
  className = '',
}: {
  title?: string;
  note?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <Glass as="section" tone="card" radius={12} className={`card ${className}`.trim()}>
      {title ? <h2 className="card__title">{title}</h2> : null}
      <div className="rows">{children}</div>
      {note ? <p className="card__note">{note}</p> : null}
    </Glass>
  );
}

export type EntryProps = {
  label: string;
  /** 右侧当前值摘要 */
  summary: string;
  /** 有 to 才是入口；没有就是这一批还没接线的行 */
  to?: string;
  badge?: string;
  onOpen?: (to: string) => void;
};

export function EntryRow({ label, summary, to, badge, onOpen }: EntryProps) {
  const trail = (
    <span className="entry__trail">
      <span className="entry__summary">{summary}</span>
      {badge ? <span className="entry__badge">{badge}</span> : null}
      {to ? (
        <span className="entry__chev" aria-hidden="true">
          ›
        </span>
      ) : null}
    </span>
  );

  if (!to) {
    return (
      <div className="entry entry--idle">
        <span className="entry__label">{label}</span>
        {trail}
      </div>
    );
  }

  return (
    <button type="button" className="entry" onClick={() => onOpen?.(to)}>
      <span className="entry__label">{label}</span>
      {trail}
    </button>
  );
}
