import type { ReactNode } from 'react';
import { Glass } from './Glass';
import { MotifScan, Rail } from './kit';

type Props = {
  title: string;
  onBack?: () => void;
  /** 右侧可选的动作 */
  right?: ReactNode;
  /**
   * plain：设置首页那一档 —— 页名压在页面底色上，下面一根发丝线（SettingsPage.xaml 的 0/1 行）；
   * bar：分类子页那一档 —— 整条玻璃页头，内距 16,12（SettingsSectionPage.xaml 的 Header）。
   */
  variant?: 'plain' | 'bar';
  /** 标签轨上的标签；缺省不摆（标题本身已经说了这是哪一页） */
  tag?: string;
  /** 标签轨上的编号；缺省按层级给（子页 02 / 根页 01） */
  index?: string;
};

export function TopBar({ title, onBack, right, variant = 'plain', tag, index }: Props) {
  const inner = (
    <div className="bar__body">
      <div className="bar__inner">
        {onBack ? (
          <button type="button" className="bar__back" onClick={onBack}>
            &lt; 返回
          </button>
        ) : (
          <span />
        )}
        <h1 className="bar__title" data-page-title tabIndex={-1}>
          {title}
        </h1>
        <span className="bar__right">{right}</span>
      </div>
      <Rail num={index ?? (onBack ? '02' : '01')} tag={tag} />
    </div>
  );

  if (variant === 'bar') {
    return (
      <Glass as="header" tone="bar" radius={0} className="bar bar--glass">
        {inner}
        <MotifScan className="bar__scan" />
      </Glass>
    );
  }

  return <header className="bar bar--plain">{inner}</header>;
}
