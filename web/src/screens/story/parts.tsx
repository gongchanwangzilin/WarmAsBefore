import type { CSSProperties, ReactNode } from 'react';
import { Glass } from '../../components/Glass';

/** 纵向一叠，间距走令牌档位（源码里的 WSpaceTight / Regular / Loose / Section）。 */
export function Stack({
  gap = 3,
  children,
  style,
}: {
  gap?: number;
  children: ReactNode;
  style?: CSSProperties;
}) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: `var(--w-space-${gap})`, ...style }}>
      {children}
    </div>
  );
}

/** 步骤开头：眉标 + 标题 + 副标题（Eyebrow / StepTitle / StepSub）。 */
export function Lead({ eyebrow, title, sub }: { eyebrow?: string; title: string; sub?: ReactNode }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-1)' }}>
      {eyebrow ? <span className="eyebrow">{eyebrow}</span> : null}
      <h2
        style={{
          margin: 0,
          fontSize: 'var(--w-size-display)',
          fontWeight: 600,
          color: 'var(--w-ink-display)',
          lineHeight: 1.35,
        }}
      >
        {title}
      </h2>
      {sub ? (
        <p style={{ margin: 0, fontSize: 'var(--w-size-small)', color: 'var(--w-ink-muted)', lineHeight: 1.6 }}>
          {sub}
        </p>
      ) : null}
    </div>
  );
}

/** 一排动作钮：不撑满，靠左，窄窗换行。 */
export function Actions({ children, end }: { children: ReactNode; end?: boolean }) {
  return (
    <div
      style={{
        display: 'flex',
        flexWrap: 'wrap',
        alignItems: 'center',
        justifyContent: end ? 'flex-end' : 'flex-start',
        gap: 'var(--w-space-2)',
      }}
    >
      {children}
    </div>
  );
}

/** 页尾动作条：与内容同色地面 + 一根发丝线，钉在视口底。 */
export function FootBar({ children }: { children: ReactNode }) {
  return (
    <Glass
      as="div"
      tone="bar"
      radius={0}
      style={{ position: 'sticky', bottom: 0, zIndex: 5, borderTop: '1px solid var(--w-hairline)' }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 'var(--w-space-3)',
          padding: 'var(--w-space-2) var(--w-pad-page)',
        }}
      >
        {children}
      </div>
    </Glass>
  );
}

/** 一行说明（TxtCaption / TxtMuted）。 */
export function Note({ children, warn }: { children: ReactNode; warn?: boolean }) {
  return <p className={warn ? 'cap cap--warn' : 'cap'} style={{ margin: 0, lineHeight: 1.6 }}>{children}</p>;
}

/** 小分区标题（TxtSection / TxtSubhead 那一档）。 */
export function SectionTitle({ children }: { children: ReactNode }) {
  return (
    <h3 style={{ margin: 0, fontSize: 'var(--w-size-subhead)', fontWeight: 600, color: 'var(--w-ink-display)' }}>
      {children}
    </h3>
  );
}
