import type { CSSProperties, ReactNode } from 'react';

/**
 * 本组共用的几块小件：副标题、分区标题、说明字、图占位井、三档按钮。
 * 全部只用令牌与墨阶，尺寸照源码里 TxtSubhead / TxtSection / TxtCaption / TxtMuted
 * 与 BtnGhostSm / BtnPrimarySm / BtnTextSm 的档位。
 */

/** TxtSubhead：一张卡的主标题。 */
export function Sub({ children }: { children: ReactNode }) {
  return (
    <h2
      style={{
        margin: 0,
        fontSize: 'var(--w-size-subhead)',
        fontWeight: 600,
        color: 'var(--w-ink-display)',
        lineHeight: 1.4,
      }}
    >
      {children}
    </h2>
  );
}

/** TxtSection：区与区之间的分组标题（不进卡面）。 */
export function SectionTitle({ children }: { children: ReactNode }) {
  return (
    <h3
      style={{
        margin: '26px 0 12px 4px',
        fontSize: 'var(--w-size-small)',
        fontWeight: 700,
        color: 'var(--w-ink-body)',
      }}
    >
      {children}
    </h3>
  );
}

/** TxtCaption：压在标题下面的说明。 */
export function Cap({ children, style }: { children: ReactNode; style?: CSSProperties }) {
  return (
    <p
      style={{
        margin: 0,
        fontSize: 'var(--w-size-caption)',
        color: 'var(--w-ink-muted)',
        lineHeight: 1.6,
        ...style,
      }}
    >
      {children}
    </p>
  );
}

/** TxtMuted：比说明再淡一档的脚注。 */
export function Muted({ children }: { children: ReactNode }) {
  return (
    <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-faint)', lineHeight: 1.6 }}>
      {children}
    </span>
  );
}

/** 还没有选项的下拉：照 MAUI 的 Picker 把 Title 当占位字，宽度跟输入框同档。 */
export function PickerPlaceholder({ title }: { title: string }) {
  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: 'var(--w-space-2)',
        boxSizing: 'border-box',
        width: '100%',
        padding: '7px 12px',
        border: '1px solid var(--w-hairline)',
        borderRadius: 'var(--w-radius-button)',
        background: 'var(--w-surface-tint)',
        fontSize: 'var(--w-size-small)',
        color: 'var(--w-ink-faint)',
      }}
    >
      <span>{title}</span>
      <span
        aria-hidden="true"
        style={{
          width: 0,
          height: 0,
          borderLeft: '4px solid transparent',
          borderRight: '4px solid transparent',
          borderTop: '5px solid var(--w-ink-muted)',
        }}
      />
    </div>
  );
}

/**
 * 图片位置的空井：源码里的 Image 在 Web 端拿不到本地文件，
 * 这里留一块带边框的空框 + 一行说明，尺寸照源码的 WidthRequest / HeightRequest。
 */
export function Well({
  width,
  height,
  ratio,
  caption,
  radius = 'var(--w-radius-well)',
  children,
  style,
}: {
  width?: number | string;
  height?: number | string;
  ratio?: number;
  caption?: string;
  radius?: string;
  children?: ReactNode;
  style?: CSSProperties;
}) {
  return (
    <div
      style={{
        width,
        height,
        aspectRatio: ratio,
        boxSizing: 'border-box',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        borderRadius: radius,
        border: '1px solid var(--w-hairline)',
        background: 'var(--w-surface-tint)',
        overflow: 'hidden',
        ...style,
      }}
    >
      {children ??
        (caption ? (
          <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-faint)' }}>{caption}</span>
        ) : null)}
    </div>
  );
}

/** BtnGhostSm：次级钮，34 高、发丝环。 */
export function GhostSm({
  children,
  onClick,
  disabled,
  width,
  style,
}: {
  children: ReactNode;
  onClick?: () => void;
  disabled?: boolean;
  width?: number | string;
  style?: CSSProperties;
}) {
  return (
    <button
      type="button"
      className="ghost"
      onClick={onClick}
      disabled={disabled}
      style={{
        height: 34,
        padding: '0 12px',
        fontSize: 'var(--w-size-caption)',
        width,
        ...(disabled ? { cursor: 'default', opacity: 0.45 } : null),
        ...style,
      }}
    >
      {children}
    </button>
  );
}

/** BtnPrimarySm：一屏一次的主动作。 */
export function PrimarySm({
  children,
  onClick,
  disabled,
  style,
}: {
  children: ReactNode;
  onClick?: () => void;
  disabled?: boolean;
  style?: CSSProperties;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      style={{
        height: 34,
        padding: '0 16px',
        border: 0,
        borderRadius: 'var(--w-radius-button)',
        background: 'var(--w-signal)',
        color: 'var(--w-ink-on-signal)',
        font: 'inherit',
        fontSize: 'var(--w-size-small)',
        fontWeight: 700,
        cursor: disabled ? 'default' : 'pointer',
        opacity: disabled ? 0.5 : 1,
        whiteSpace: 'nowrap',
        ...style,
      }}
    >
      {children}
    </button>
  );
}

/** BtnTextSm：只有文字的动作（行右侧的「忘掉」「加标签」）。 */
export function TextSm({
  children,
  onClick,
  danger,
  bold,
  style,
}: {
  children: ReactNode;
  onClick?: () => void;
  danger?: boolean;
  bold?: boolean;
  style?: CSSProperties;
}) {
  return (
    <button
      type="button"
      className="k-textbtn"
      onClick={onClick}
      style={{
        fontSize: 'var(--w-size-caption)',
        color: danger ? 'var(--w-signal)' : undefined,
        fontWeight: bold ? 700 : undefined,
        whiteSpace: 'nowrap',
        ...style,
      }}
    >
      {children}
    </button>
  );
}

/** 只有说明的一行（源码里空列表用 TxtRowDesc 单独占一行，没有主标题）。 */
export function NoteRow({ children }: { children: ReactNode }) {
  return (
    <div className="k-row">
      <div className="k-row__text">
        <span className="k-row__desc">{children}</span>
      </div>
    </div>
  );
}

/** 卡片里一行：左文字、右控件，与 kit 的 Row 同档但允许右栏竖排两三颗钮。 */
export function RowLine({
  title,
  desc,
  right,
  onClick,
  chevron,
}: {
  title: ReactNode;
  desc?: ReactNode;
  right?: ReactNode;
  onClick?: () => void;
  chevron?: boolean;
}) {
  const body = (
    <>
      <div className="k-row__text">
        <span className="k-row__title">{title}</span>
        {desc ? <span className="k-row__desc">{desc}</span> : null}
      </div>
      {right ? (
        <div className="k-row__right" style={{ flexWrap: 'wrap', justifyContent: 'flex-end' }}>
          {right}
        </div>
      ) : null}
      {chevron ? (
        <span className="k-row__chev" aria-hidden="true">
          ›
        </span>
      ) : null}
    </>
  );
  if (!onClick) return <div className="k-row" style={{ flexWrap: 'wrap', rowGap: 6 }}>{body}</div>;
  return (
    <button type="button" className="k-row k-row--tap" style={{ flexWrap: 'wrap', rowGap: 6 }} onClick={onClick}>
      {body}
    </button>
  );
}
