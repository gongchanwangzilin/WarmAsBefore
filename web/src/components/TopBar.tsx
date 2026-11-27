import type { ReactNode } from 'react';
import { Glass } from './Glass';

type Props = {
  title: string;
  onBack?: () => void;
  /** 右侧可选的动作（例如设置页的「全部设置」跳转） */
  right?: ReactNode;
};

export function TopBar({ title, onBack, right }: Props) {
  return (
    <Glass as="header" tone="bar" radius={0} className="topbar">
      <div className="topbar__inner">
        {onBack ? (
          <button type="button" className="topbar__back" onClick={onBack}>
            <span aria-hidden="true">←</span> 返回
          </button>
        ) : (
          <span className="topbar__spacer" />
        )}
        <h1 className="topbar__title" data-page-title tabIndex={-1}>
          {title}
        </h1>
        <span className="topbar__right">{right}</span>
      </div>
    </Glass>
  );
}
