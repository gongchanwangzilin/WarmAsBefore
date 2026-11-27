import type { CSSProperties } from 'react';
import { Glass } from './Glass';
import { usePress } from '../lib/usePress';

type Props = {
  checked: boolean;
  onChange: (next: boolean) => void;
  label: string;
};

export function GlassSwitch({ checked, onChange, label }: Props) {
  const press = usePress<HTMLButtonElement>();
  const toggle = () => onChange(!checked);

  return (
    <button
      ref={press.ref}
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      className="switch"
      onPointerDown={press.down}
      onPointerUp={press.up}
      onPointerLeave={press.up}
      onPointerCancel={press.up}
      onClick={toggle}
      onKeyDown={(event) => {
        if (event.key === ' ' || event.key === 'Enter') {
          event.preventDefault();
          press.flash();
          toggle();
        }
      }}
    >
      <Glass
        tone="track"
        radius={13.5}
        className="switch__track"
        style={{ '--on': checked ? 1 : 0 } as CSSProperties}
      >
        <span className="switch__liquid" aria-hidden="true" />
        <Glass tone="knob" radius={10.5} className="switch__knob" />
      </Glass>
    </button>
  );
}
