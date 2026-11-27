import type { ReactNode } from 'react';
import { Glass } from './Glass';

/**
 * 一张设置卡（DataTemplate 的 Border Style=CardSolid + VerticalStackLayout Spacing=0）：
 * 小标题、可选的一行说明、然后是一串行。行与行之间的发丝线由 <Sep /> 显式放 ——
 * 源码里哪些行之间有线是逐处写出来的，不是「有行就有线」（场景特效那一段就没有）。
 */
export function SettingCard({
  title,
  note,
  children,
  className = '',
}: {
  title: string;
  note?: string;
  children: ReactNode;
  className?: string;
}) {
  return (
    <Glass as="section" tone="card" radius={12} className={`s-card ${className}`.trim()}>
      <h2 className="s-card__title">{title}</h2>
      {note ? <p className="s-card__note">{note}</p> : null}
      <div className="s-rows">{children}</div>
    </Glass>
  );
}

/** 一行：左边主标题 + 说明（TxtBodySm / TxtCaption），右边控件，Padding="0,13"。 */
export function SettingRow({
  title,
  desc,
  control,
  gap = false,
  className = '',
}: {
  title: string;
  desc?: string;
  control: ReactNode;
  /** 滑杆行在源码里带 ColumnSpacing=WSpaceLoose，开关行不带 */
  gap?: boolean;
  className?: string;
}) {
  return (
    <div className={`s-row${gap ? ' s-row--gap' : ''} ${className}`.trim()}>
      <div className="s-row__text">
        <span className="s-row__title">{title}</span>
        {desc ? <span className="s-row__desc">{desc}</span> : null}
      </div>
      <div className="s-row__control">{control}</div>
    </div>
  );
}

/** 通到卡边的发丝线（HSepBleed：靠负的横向 Margin 抵消卡内距）。 */
export function Sep() {
  return <div className="s-sep" role="presentation" />;
}

/** 只有文字、没有控件的一段（玻璃关闭提示、纯色底整块、页面色整块）。 */
export function SettingBlock({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <div className={`s-block ${className}`.trim()}>{children}</div>;
}
