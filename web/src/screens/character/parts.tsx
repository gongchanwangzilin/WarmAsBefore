import type { CSSProperties, ReactNode } from 'react';
import { GlassSwitch } from '../../components/GlassSwitch';
import { Field } from '../../components/kit';

/**
 * character 组自己的一小叠形状：工作室 / 新建向导 / 角色库 / 副设定对比共用。
 * 只用令牌，不碰共享的 kit / css —— 缺的那几件（眉标、段号、选项块、滑杆）都在这里补齐。
 */

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

/** 眉标（Eyebrow）：宽体大写拉丁，说的是「这一段叫什么」。 */
export function Eyebrow({ children }: { children: ReactNode }) {
  return <span className="eyebrow">{children}</span>;
}

/** 分段标题（PaneTitle / TxtSubhead）。 */
export function PaneTitle({ children }: { children: ReactNode }) {
  return (
    <h3
      style={{
        margin: 0,
        fontSize: 'var(--w-size-subhead)',
        fontWeight: 600,
        color: 'var(--w-ink-display)',
      }}
    >
      {children}
    </h3>
  );
}

/** 一行说明（TxtCaption）。faint = 弱一档的落款。 */
export function Caption({ children, faint }: { children: ReactNode; faint?: boolean }) {
  return (
    <span
      style={{
        fontSize: 'var(--w-size-caption)',
        color: faint ? 'var(--w-ink-faint)' : 'var(--w-ink-muted)',
        lineHeight: 1.55,
      }}
    >
      {children}
    </span>
  );
}

/** 正文小号（TxtBodySm）。 */
export function BodySm({ children }: { children: ReactNode }) {
  return (
    <span style={{ fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)', lineHeight: 1.5 }}>
      {children}
    </span>
  );
}

/** 段号脊上的一段：编号 + 眉标 + 标题 + 发丝线（SectionNo / Eyebrow / PaneTitle / HSep）。 */
export function SectionHead({ no, eyebrow, title }: { no: string; eyebrow: string; title: string }) {
  return (
    <Stack gap={2}>
      <div style={{ display: 'grid', gridTemplateColumns: '40px 1fr', columnGap: 'var(--w-space-2)' }}>
        <span
          style={{
            fontFamily: 'var(--w-font-num)',
            fontSize: '26px',
            color: 'var(--w-ink-faint)',
            alignSelf: 'center',
          }}
        >
          {no}
        </span>
        <div style={{ display: 'flex', flexDirection: 'column' }}>
          <Eyebrow>{eyebrow}</Eyebrow>
          <PaneTitle>{title}</PaneTitle>
        </div>
      </div>
      <Hair />
    </Stack>
  );
}

export function Hair({ inset }: { inset?: boolean }) {
  return (
    <div
      style={{
        height: 1,
        background: 'var(--w-hairline)',
        marginLeft: inset ? 'var(--w-pad-card)' : 0,
        marginRight: inset ? 'var(--w-pad-card)' : 0,
      }}
    />
  );
}

/** 一句说明（源码里各页的 TxtCaption 行）。 */
export function Note({ children, warn }: { children: ReactNode; warn?: boolean }) {
  return (
    <p
      style={{
        margin: 0,
        fontSize: 'var(--w-size-caption)',
        color: warn ? 'var(--w-signal)' : 'var(--w-ink-muted)',
        lineHeight: 1.6,
      }}
    >
      {children}
    </p>
  );
}

/** 一行字段：标签在字上、框在下（FieldLabel + Entry / Editor）。 */
export function FieldLine({
  label,
  value,
  onChange,
  placeholder,
  action,
  area,
}: {
  label: string;
  value: string;
  onChange: (next: string) => void;
  placeholder?: string;
  action?: ReactNode;
  area?: boolean;
}) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-1)' }}>
      <div style={{ display: 'flex', alignItems: 'flex-end', justifyContent: 'space-between', gap: 'var(--w-space-2)' }}>
        <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)' }}>{label}</span>
        {action}
      </div>
      <Field value={value} onChange={onChange} placeholder={placeholder} multiline={area} />
    </div>
  );
}

/** 选项块（Chip）：静止是一格比卡面沉一档的井，选中翻成墨底浅字。 */
export function ChipBtn({
  label,
  on,
  onClick,
}: {
  label: string;
  on?: boolean;
  onClick?: () => void;
}) {
  return (
    <button
      type="button"
      aria-pressed={on || undefined}
      onClick={onClick}
      style={{
        padding: '5px 10px',
        minHeight: 30,
        borderRadius: 99,
        border: on ? '1px solid transparent' : '1px solid var(--w-hairline)',
        background: on ? 'var(--w-ink-display)' : 'var(--w-tint-button)',
        color: on ? 'var(--w-bg)' : 'var(--w-ink-body)',
        font: 'inherit',
        fontSize: 'var(--w-size-caption)',
        cursor: 'pointer',
      }}
    >
      {label}
    </button>
  );
}

/** 一排选项块。 */
export function Chips({ children }: { children: ReactNode }) {
  return <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>{children}</div>;
}

/** 一颗开关 + 标题/说明（Grid ColumnDefinitions="*,Auto" + ViewSwitch）。 */
export function SwitchLine({
  title,
  desc,
  checked,
  onChange,
}: {
  title: string;
  desc?: ReactNode;
  checked: boolean;
  onChange: (next: boolean) => void;
}) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-3)' }}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 2, flex: 1, minWidth: 0 }}>
        <BodySm>{title}</BodySm>
        {desc ? <Caption>{desc}</Caption> : null}
      </div>
      <GlassSwitch checked={checked} onChange={onChange} label={title} />
    </div>
  );
}

/** 一根 0..100 的滑杆：标签 + 右侧读数（ViewSlider）。 */
export function SliderLine({
  label,
  reading,
  value,
  onChange,
  disabled,
}: {
  label: string;
  reading?: string;
  value: number;
  onChange: (next: number) => void;
  disabled?: boolean;
}) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: 'var(--w-space-2)' }}>
        <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)' }}>{label}</span>
        {reading ? <Caption>{reading}</Caption> : null}
      </div>
      <input
        type="range"
        min={0}
        max={100}
        step={1}
        value={Math.round(value)}
        disabled={disabled}
        aria-label={label}
        onChange={(event) => onChange(Number(event.target.value))}
        style={{
          width: '100%',
          accentColor: 'var(--w-signal)',
          background: 'transparent',
          cursor: disabled ? 'default' : 'pointer',
          opacity: disabled ? 0.45 : 1,
        }}
      />
    </div>
  );
}

/** 一排动作钮：靠左，窄窗换行。 */
export function Actions({ children, end }: { children: ReactNode; end?: boolean }) {
  return (
    <div
      style={{
        display: 'flex',
        flexWrap: 'wrap',
        alignItems: 'center',
        justifyContent: end ? 'flex-end' : 'flex-start',
        gap: 6,
      }}
    >
      {children}
    </div>
  );
}

/** 井面：比卡面沉一档的一块底（WWellBrush + WRadiusWell）。 */
export function Well({ children }: { children: ReactNode }) {
  return (
    <div
      style={{
        padding: 'var(--w-space-3)',
        borderRadius: 4,
        background: 'var(--w-surface-tint)',
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--w-space-3)',
      }}
    >
      {children}
    </div>
  );
}

/** 一颗小圆点（世界观的单选点亮 / 名册行的强调色短杠）。 */
export function Dot({ on }: { on?: boolean }) {
  return (
    <i
      aria-hidden="true"
      style={{
        display: 'block',
        width: 8,
        height: 8,
        borderRadius: 4,
        background: on ? 'var(--w-signal)' : 'transparent',
      }}
    />
  );
}

/** 实心小钮（BtnPrimarySm / BtnPrimary）：墨底浅字，一屏里的主行动。 */
export function PrimaryBtn({
  children,
  onClick,
  disabled,
  block,
}: {
  children: ReactNode;
  onClick?: () => void;
  disabled?: boolean;
  block?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      style={{
        height: 32,
        padding: '0 14px',
        width: block ? '100%' : undefined,
        minWidth: block ? undefined : 0,
        borderRadius: 'var(--w-radius-button)',
        border: 0,
        background: 'var(--w-ink-display)',
        color: 'var(--w-bg)',
        font: 'inherit',
        fontSize: 'var(--w-size-caption)',
        fontWeight: 600,
        cursor: disabled ? 'default' : 'pointer',
        opacity: disabled ? 0.45 : 1,
      }}
    >
      {children}
    </button>
  );
}

/** 危险档的文字钮（BtnDangerSm）：字用信号色，不铺一块实心红。 */
export function DangerBtn({ children, onClick }: { children: ReactNode; onClick?: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      style={{
        height: 32,
        padding: '0 10px',
        borderRadius: 'var(--w-radius-button)',
        border: '1px solid var(--w-signal-edge)',
        background: 'transparent',
        color: 'var(--w-signal)',
        font: 'inherit',
        fontSize: 'var(--w-size-caption)',
        cursor: 'pointer',
      }}
    >
      {children}
    </button>
  );
}
