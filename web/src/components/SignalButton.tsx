import type { ReactNode } from 'react';
import { Glass } from './Glass';
import { usePress } from '../lib/usePress';

type Props = {
  children: ReactNode;
  onClick?: () => void;
  disabled?: boolean;
  busy?: boolean;
  className?: string;
};

/** 一屏只响一次的那颗钮（Signal 档）。 */
export function SignalButton({ children, onClick, disabled, busy, className = '' }: Props) {
  const press = usePress<HTMLDivElement>();

  return (
    <Glass
      as="button"
      type="button"
      tone="signal"
      radius={8}
      className={`signal ${className}`.trim()}
      elementRef={press.ref}
      disabled={disabled}
      aria-busy={busy || undefined}
      onPointerDown={press.down}
      onPointerUp={press.up}
      onPointerLeave={press.up}
      onPointerCancel={press.up}
      onClick={onClick}
    >
      <span className="signal__label">{children}</span>
    </Glass>
  );
}
