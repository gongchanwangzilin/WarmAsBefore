import type { ReactNode } from 'react';
import { Glass } from './Glass';
import { usePress } from '../lib/usePress';

type Props = {
  children: ReactNode;
  onClick?: () => void;
};

export function SignalButton({ children, onClick }: Props) {
  const press = usePress<HTMLDivElement>();

  return (
    <Glass
      as="button"
      type="button"
      tone="signal"
      radius={8}
      className="signal"
      elementRef={press.ref}
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
