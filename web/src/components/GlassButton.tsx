import type { KeyboardEvent, ReactNode } from 'react';
import { Glass } from './Glass';
import { usePress } from '../lib/usePress';

type Props = {
  children: ReactNode;
  onClick?: () => void;
  /** sm = 34（页脚、行内动作）/ md = 40（存档卡）/ lg = 46（主按钮） */
  size?: 'sm' | 'md' | 'lg';
  className?: string;
  ariaLabel?: string;
};

/** 玻璃按钮（源码里的 GlassButton / BtnGhost）：底、环、光都来自材质，尺寸只有三档。 */
export function GlassButton({ children, onClick, size = 'md', className = '', ariaLabel }: Props) {
  const press = usePress<HTMLButtonElement>();

  return (
    <Glass
      as="button"
      type="button"
      tone="button"
      radius={8}
      className={`gbtn gbtn--${size} ${className}`.trim()}
      elementRef={press.ref as unknown as React.RefObject<HTMLElement | null>}
      aria-label={ariaLabel}
      onPointerDown={press.down}
      onPointerUp={press.up}
      onPointerLeave={press.up}
      onPointerCancel={press.up}
      onClick={onClick}
      onKeyDown={(event: KeyboardEvent<HTMLButtonElement>) => {
        if (event.key === 'Enter' || event.key === ' ') press.flash();
      }}
    >
      {children}
    </Glass>
  );
}
