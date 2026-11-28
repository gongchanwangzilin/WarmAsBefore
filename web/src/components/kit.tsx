import { useId, type ReactNode } from 'react';
import { Glass } from './Glass';

/**
 * 通用件：每个页面都从这几块里拿形状，页面自己不再写卡片边框、行高、内距。
 * 全部只用令牌（--w-*）与墨阶（--w-ink-*），没有任何写死的颜色或字号。
 */

/**
 * 标签轨：页眉下面那条「编号 + 标签 + 发丝线 + 刻度」。
 * 编号是层级，不是纸张编号：根页 01、子页 02（给了 index 就用给的）。
 * 标签吃页面自己的眉标，不新造字。
 */
export function Rail({ num, tag }: { num: string; tag?: string }) {
  return (
    <div className="ph__rail" aria-hidden="true">
      <span className="rail__num">{num}</span>
      <span className="rail__tag">{tag ? tag.replace(/^[/\s]+/, '') : ''}</span>
      <i className="rail__rule" />
      <i className="rail__end" />
    </div>
  );
}

/** 页头：返回 · 标题/副标题 · 标签轨 · 右侧动作，下面一条发丝线（PageHeader.xaml）。 */
export function PageHeader({
  title,
  subtitle,
  eyebrow,
  onBack,
  backText = '< 返回',
  right,
  variant = 'bar',
  index,
}: {
  title: string;
  subtitle?: string;
  eyebrow?: string;
  onBack?: () => void;
  backText?: string;
  right?: ReactNode;
  /** bar：整条玻璃页头（多数页）；ground：落在地上、无底无线（信息页用） */
  variant?: 'bar' | 'ground';
  /** 标签轨上的编号；缺省按层级给（子页 02 / 根页 01） */
  index?: string;
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
        <h1 className="ph__title" data-page-title tabIndex={-1}>
          {title}
        </h1>
        {subtitle ? (
          <span className="ph__sub" title={subtitle}>
            {subtitle}
          </span>
        ) : null}
        <Rail num={index ?? (onBack ? '02' : '01')} tag={eyebrow} />
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
      <MotifScan className="ph__scan" />
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

/**
 * 段落小标题 = 编号 + 标题 + 说明（「常驻」「全部设置」那一档）。
 * 编号不用传：CSS 计数器按页面顺序给（01、02…），要指定才传 index。
 */
export function GroupLabel({
  children,
  index,
  note,
}: {
  children: ReactNode;
  index?: string;
  note?: string;
}) {
  return (
    <div className="k-group">
      <span className="k-group__row">
        <span className="k-group__num">{index ?? ''}</span>
        <h3 className="k-group__title">{children}</h3>
        <i className="k-group__rule" aria-hidden="true" />
      </span>
      {note ? <span className="k-group__note">{note}</span> : null}
    </div>
  );
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

/** 读数（StatTile）：上面一个数、下面一行名。unit 比数小一档，不跟着数一起放大。 */
export function Stat({
  value,
  label,
  hint,
  unit,
}: {
  value: ReactNode;
  label: string;
  hint?: string;
  unit?: string;
}) {
  return (
    <div className="k-stat">
      <span className="k-stat__value">
        {value}
        {unit ? <span className="k-stat__unit">{unit}</span> : null}
      </span>
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

/**
 * 图形母题：四个 inline SVG，笔画全走 currentColor，容器给 --w-ramp-* 那一档。
 * 不用图片、不用图标字体、不加依赖 —— 换配色时它们跟着令牌一起换。
 */

/** 扫描线：3px 周期的三根横线，压在页头或读数带的底边。 */
export function MotifScan({ className = '' }: { className?: string }) {
  return (
    <svg
      className={`motif motif--scan ${className}`.trim()}
      viewBox="0 0 100 6"
      preserveAspectRatio="none"
      aria-hidden="true"
    >
      <line x1="0" y1="0.5" x2="100" y2="0.5" />
      <line x1="0" y1="2.5" x2="100" y2="2.5" />
      <line x1="0" y1="4.5" x2="100" y2="4.5" />
    </svg>
  );
}

/** 栅格：24px 一格的空场，给「这里还没有东西」的面一层蚀刻底纹。 */
export function MotifGrid({ size = 24, className = '' }: { size?: number; className?: string }) {
  const id = `motif-grid-${useId().replace(/:/g, '')}`;
  return (
    <svg className={`motif motif--grid ${className}`.trim()} aria-hidden="true">
      <defs>
        <pattern id={id} width={size} height={size} patternUnits="userSpaceOnUse">
          <path d={`M ${size} 0 L 0 0 L 0 ${size}`} />
        </pattern>
      </defs>
      <rect width="100%" height="100%" fill={`url(#${id})`} />
    </svg>
  );
}

/** 角标：一个 10px 的 L，四个拼起来就是一个括号。 */
export function MotifCorner({ className = '' }: { className?: string }) {
  return (
    <svg
      className={`motif motif--corner ${className}`.trim()}
      viewBox="0 0 10 10"
      aria-hidden="true"
    >
      <path d="M0.5 10 L0.5 0.5 L10 0.5" />
    </svg>
  );
}

/** 分隔符：一根发丝线，两端各一颗竖刻度。 */
export function MotifRule({ className = '' }: { className?: string }) {
  return (
    <svg
      className={`motif motif--rule ${className}`.trim()}
      viewBox="0 0 100 5"
      preserveAspectRatio="none"
      aria-hidden="true"
    >
      <line x1="0" y1="2.5" x2="100" y2="2.5" />
      <line x1="0.5" y1="0.5" x2="0.5" y2="4.5" />
      <line x1="99.5" y1="0.5" x2="99.5" y2="4.5" />
    </svg>
  );
}
