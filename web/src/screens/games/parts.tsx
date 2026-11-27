import type { CSSProperties, ReactNode } from 'react';

/** 纵向一叠，间距走令牌档位（WSpaceTight / Regular / Loose / Section）。 */
export function Column({
  gap = 2,
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

/** 一段的小标题（TxtSection / TxtMuted 两档）。 */
export function Section({
  children,
  muted,
  style,
}: {
  children: ReactNode;
  muted?: boolean;
  style?: CSSProperties;
}) {
  return (
    <span
      style={{
        fontSize: muted ? 'var(--w-size-caption)' : 'var(--w-size-subhead)',
        fontWeight: muted ? 400 : 600,
        letterSpacing: muted ? '0.04em' : undefined,
        color: muted ? 'var(--w-ink-muted)' : 'var(--w-ink-display)',
        ...style,
      }}
    >
      {children}
    </span>
  );
}

/** 一行说明小字（TxtCaption / TxtRowDesc / TxtMuted）。 */
export function Cap({
  children,
  tone = 'muted',
  style,
}: {
  children: ReactNode;
  tone?: 'muted' | 'body' | 'signal' | 'faint';
  style?: CSSProperties;
}) {
  const color =
    tone === 'body'
      ? 'var(--w-ink-body)'
      : tone === 'signal'
        ? 'var(--w-signal)'
        : tone === 'faint'
          ? 'var(--w-ink-faint)'
          : 'var(--w-ink-muted)';
  return (
    <span
      style={{
        fontSize: 'var(--w-size-caption)',
        color,
        lineHeight: 1.6,
        ...style,
      }}
    >
      {children}
    </span>
  );
}

/** 井面小块（WellBg + WRadiusWell）：楼层、地图来源、绑定模式都用它。 */
export function Well({
  children,
  active,
  onClick,
  style,
}: {
  children: ReactNode;
  active?: boolean;
  onClick?: () => void;
  style?: CSSProperties;
}) {
  const base: CSSProperties = {
    display: 'inline-flex',
    alignItems: 'center',
    padding: '7px 10px',
    borderRadius: 'var(--w-radius-well)',
    border: 0,
    font: 'inherit',
    fontSize: 'var(--w-size-small)',
    fontWeight: active ? 600 : 400,
    color: active ? 'var(--w-ink-on-signal)' : 'var(--w-ink-body)',
    background: active ? 'var(--w-signal)' : 'var(--w-surface-tint)',
    cursor: onClick ? 'pointer' : 'default',
    ...style,
  };
  if (!onClick) return <span style={base}>{children}</span>;
  return (
    <button type="button" style={base} aria-pressed={active} onClick={onClick}>
      {children}
    </button>
  );
}

/** 浮在图上的圆形钮（地图的缩放 / 定位 / 指南针 / 图层）。 */
export function Fab({
  label,
  onClick,
  size = 44,
  fontSize = 'var(--w-size-body)',
  title,
}: {
  label: string;
  onClick?: () => void;
  size?: number;
  fontSize?: string;
  title?: string;
}) {
  return (
    <button
      type="button"
      aria-label={title ?? label}
      onClick={onClick}
      style={{
        width: size,
        height: size,
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: 99,
        border: '1px solid var(--w-hairline)',
        background: 'var(--w-surface-tint)',
        color: 'var(--w-ink-body)',
        font: 'inherit',
        fontSize,
        cursor: 'pointer',
      }}
    >
      {label}
    </button>
  );
}

/** 表单行（SettingRow）：左右一栏，窄窗换行。 */
export function FormRow({
  title,
  desc,
  right,
  children,
}: {
  title?: ReactNode;
  desc?: ReactNode;
  right?: ReactNode;
  children?: ReactNode;
}) {
  return (
    <div
      style={{
        display: 'flex',
        flexWrap: 'wrap',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: 'var(--w-space-2)',
        minHeight: 'var(--w-row-h)',
        padding: '10px 0',
      }}
    >
      {title || desc ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 2, flex: '1 1 220px', minWidth: 0 }}>
          {title ? (
            <span style={{ fontSize: 'var(--w-size-body)', color: 'var(--w-ink-body)' }}>{title}</span>
          ) : null}
          {desc ? <Cap>{desc}</Cap> : null}
        </div>
      ) : null}
      {right ? <div style={{ display: 'inline-flex', alignItems: 'center', gap: 'var(--w-space-1)' }}>{right}</div> : null}
      {children}
    </div>
  );
}

/** 一排动作钮：靠左、窄窗换行（FlexLayout Wrap）。 */
export function ToolRow({ children }: { children: ReactNode }) {
  return (
    <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 'var(--w-space-2)' }}>
      {children}
    </div>
  );
}

/** 一行输入框（源码里 Entry 都长这样：井底 + 一圈发丝边）。 */
export function InlineField({
  value,
  onChange,
  placeholder,
  onEnter,
  fontSize = 'var(--w-size-small)',
  style,
}: {
  value: string;
  onChange: (next: string) => void;
  placeholder?: string;
  onEnter?: () => void;
  fontSize?: string;
  style?: CSSProperties;
}) {
  return (
    <input
      type="text"
      className="k-field"
      value={value}
      placeholder={placeholder}
      aria-label={placeholder}
      onChange={(event) => onChange(event.target.value)}
      onKeyDown={(event) => {
        if (event.key === 'Enter' && onEnter) onEnter();
      }}
      style={{ fontSize, padding: '7px 10px', ...style }}
    />
  );
}
