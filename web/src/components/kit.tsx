import type { ReactNode } from 'react';
import { Glass } from './Glass';

/**
 * 通用件：每个页面都从这几块里拿形状，页面自己不再写卡片边框、行高、内距。
 * 全部只用令牌（--w-*）与墨阶（--w-ink-*），没有任何写死的颜色或字号。
 */

/** 页头：返回 · 眉标/标题/副标题 · 右侧动作，下面一条发丝线（PageHeader.xaml）。 */
export function PageHeader({
  title,
  subtitle,
  eyebrow,
  onBack,
  backText = '< 返回',
  right,
  variant = 'bar',
}: {
  title: string;
  subtitle?: string;
  eyebrow?: string;
  onBack?: () => void;
  backText?: string;
  right?: ReactNode;
  /** bar：整条玻璃页头（多数页）；ground：落在地上、无底无线（信息页用） */
  variant?: 'bar' | 'ground';
}) {
  const inner = (
    <div className="ph__inner">
      {onBack ? (
        <button type="button" className="ph__back" data-back="1" onClick={onBack}>
          {backText}
        </button>
      ) : (
        <span />
      )}
      <div className="ph__mid">
        {eyebrow ? (
          <span className="ph__eyebrow">
            <i className="ph__mark" aria-hidden="true" />
            {eyebrow}
          </span>
        ) : null}
        <h1 className="ph__title" data-page-title tabIndex={-1}>
          {title}
        </h1>
        {subtitle ? (
          <span className="ph__sub" title={subtitle}>
            {subtitle}
          </span>
        ) : null}
      </div>
      <div className="ph__right">{right}</div>
    </div>
  );

  if (variant === 'ground') {
    return <header className="ph ph--ground">{inner}</header>;
  }
  return (
    <Glass as="header" tone="bar" radius={0} className="ph ph--bar">
      {inner}
    </Glass>
  );
}

/** 一张卡（CardSolid）：小标题 + 可选说明 + 内容。 */
export function Card({
  title,
  note,
  right,
  children,
  tight,
  className = '',
}: {
  title?: string;
  note?: string;
  right?: ReactNode;
  children: ReactNode;
  tight?: boolean;
  className?: string;
}) {
  return (
    <Glass as="section" tone="card" radius={12} className={`k-card${tight ? ' k-card--tight' : ''} ${className}`.trim()}>
      {title || right ? (
        <div className="k-card__head">
          <div>
            {title ? <h2 className="k-card__title">{title}</h2> : null}
            {note ? <p className="k-card__note">{note}</p> : null}
          </div>
          {right ? <div className="k-card__right">{right}</div> : null}
        </div>
      ) : null}
      {children}
    </Glass>
  );
}

/** 一行：主标题 / 说明 / 右侧（值、控件、`›`）。onClick 就整行可点。 */
export function Row({
  title,
  desc,
  right,
  onClick,
  chevron,
  disabled,
  className = '',
}: {
  title: ReactNode;
  desc?: ReactNode;
  right?: ReactNode;
  onClick?: () => void;
  chevron?: boolean;
  disabled?: boolean;
  className?: string;
}) {
  const body = (
    <>
      <div className="k-row__text">
        <span className="k-row__title">{title}</span>
        {desc ? <span className="k-row__desc">{desc}</span> : null}
      </div>
      {right ? <div className="k-row__right">{right}</div> : null}
      {chevron ? (
        <span className="k-row__chev" aria-hidden="true">
          ›
        </span>
      ) : null}
    </>
  );

  if (!onClick) {
    return <div className={`k-row ${className}`.trim()}>{body}</div>;
  }

  return (
    <button type="button" className={`k-row k-row--tap ${className}`.trim()} onClick={onClick} disabled={disabled}>
      {body}
    </button>
  );
}

/** 行间发丝线（HSep / HSepBleed）。 */
export function Sep({ bleed }: { bleed?: boolean }) {
  return <div className={bleed ? 'k-sep k-sep--bleed' : 'k-sep'} role="presentation" />;
}

/** 段落小标题（「常驻」「全部设置」那一档）。 */
export function GroupLabel({ children }: { children: ReactNode }) {
  return <h3 className="k-group">{children}</h3>;
}

/** 只有文字的钮（BtnText）：页头返回、行尾的「清空」「换一个」。 */
export function TextBtn({
  children,
  onClick,
  disabled,
  tone = 'plain',
}: {
  children: ReactNode;
  onClick?: () => void;
  disabled?: boolean;
  tone?: 'plain' | 'signal';
}) {
  return (
    <button
      type="button"
      className={tone === 'signal' ? 'k-textbtn k-textbtn--signal' : 'k-textbtn'}
      onClick={onClick}
      disabled={disabled}
    >
      {children}
    </button>
  );
}

/** 字节/标签（Chip）：一行「已启用」「终末地」这种小牌。 */
export function Chip({ children, tone = 'quiet' }: { children: ReactNode; tone?: 'quiet' | 'signal' | 'warn' }) {
  return <span className={`k-chip k-chip--${tone}`}>{children}</span>;
}

/** 读数（StatTile）：上面一个数、下面一行名。 */
export function Stat({ value, label, hint }: { value: ReactNode; label: string; hint?: string }) {
  return (
    <div className="k-stat">
      <span className="k-stat__value">{value}</span>
      <span className="k-stat__label">{label}</span>
      {hint ? <span className="k-stat__hint">{hint}</span> : null}
    </div>
  );
}

/** 横向占比条（一格里一根，值 0..1）。 */
export function Bar({ value, label }: { value: number; label?: string }) {
  return (
    <div className="k-bar" role="img" aria-label={label} aria-valuenow={Math.round(value * 100)}>
      <i style={{ width: `${Math.max(0, Math.min(1, value)) * 100}%` }} />
    </div>
  );
}

/** 两句空态：一句为什么空、一句怎么才不空（EmptyState.xaml）。 */
export function Empty({ title, reason, hint }: { title: string; reason?: string; hint?: string }) {
  return (
    <div className="k-empty">
      <span className="k-empty__title">{title}</span>
      {reason ? <span className="k-empty__reason">{reason}</span> : null}
      {hint ? <span className="k-empty__hint">{hint}</span> : null}
    </div>
  );
}

/** 两栏网格：宽窗两列，窄窗一列（源码里 Width > 900 / 720 几处分档统一收在这里）。 */
export function Grid2({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <div className={`k-grid2 ${className}`.trim()}>{children}</div>;
}

/** 输入框（Entry）：页面里出现的每一个都走这里，边框与内距一致。 */
export function Field({
  value,
  onChange,
  placeholder,
  label,
  type = 'text',
  multiline,
}: {
  value: string;
  onChange: (next: string) => void;
  placeholder?: string;
  label?: string;
  type?: 'text' | 'password' | 'number';
  multiline?: boolean;
}) {
  if (multiline) {
    return (
      <textarea
        className="k-field k-field--area"
        value={value}
        placeholder={placeholder}
        aria-label={label ?? placeholder}
        onChange={(event) => onChange(event.target.value)}
      />
    );
  }
  return (
    <input
      className="k-field"
      type={type}
      value={value}
      placeholder={placeholder}
      aria-label={label ?? placeholder}
      onChange={(event) => onChange(event.target.value)}
    />
  );
}

/** 下拉（Picker）：原生 select，样式统一，选项只来自传入的数组。 */
export function Select<T extends string>({
  value,
  options,
  onChange,
  label,
}: {
  value: T;
  options: { value: T; label: string }[];
  onChange: (next: T) => void;
  label?: string;
}) {
  return (
    <span className="k-select">
      <select value={value} aria-label={label} onChange={(event) => onChange(event.target.value as T)}>
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
      <i aria-hidden="true" />
    </span>
  );
}
