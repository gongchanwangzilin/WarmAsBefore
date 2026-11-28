import type { ReactNode } from 'react';
import { Glass } from './Glass';
import { HeadBlock, MotifScan } from './kit';

type Props = {
  title: string;
  onBack?: () => void;
  /** 右侧可选的动作 */
  right?: ReactNode;
  /**
   * plain：设置首页那一档 —— 页名压在页面底色上，下面一根发丝线（SettingsPage.xaml 的 0/1 行）；
   * bar：分类子页那一档 —— 整条玻璃页头。
   */
  variant?: 'plain' | 'bar';
  /** 标签轨上的标签；缺省不摆（标题本身已经说了这是哪一页） */
  tag?: string;
  /** 标签轨上的编号；缺省按层级给（子页 02 / 根页 01） */
  index?: string;
};

/** 顶栏：与 PageHeader 共用同一份页头骨架（.hd），只有底材一档不同。 */
export function TopBar({ title, onBack, right, variant = 'plain', tag, index }: Props) {
  const inner = <HeadBlock title={title} onBack={onBack} right={right} eyebrow={tag} index={index} />;

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
