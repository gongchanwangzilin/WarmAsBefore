import type { CSSProperties, KeyboardEvent } from 'react';
import { Glass } from './Glass';
import { usePress } from '../lib/usePress';

type Props = {
  checked: boolean;
  onChange: (next: boolean) => void;
  label: string;
  /** 标签已经由外侧 <label> 或 aria-labelledby 给过时关掉 */
  hideLabel?: boolean;
  disabled?: boolean;
};

export function GlassSwitch({ checked, onChange, label, hideLabel, disabled }: Props) {
  const press = usePress<HTMLButtonElement>();
  const toggle = () => onChange(!checked);

  return (
    <button
      ref={press.ref}
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={hideLabel ? undefined : label}
      aria-disabled={disabled || undefined}
      disabled={disabled}
      className="switch"
      onPointerDown={press.down}
      onPointerUp={press.up}
      onPointerLeave={press.up}
      onPointerCancel={press.up}
      onClick={toggle}
      onKeyDown={(event: KeyboardEvent<HTMLButtonElement>) => {
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
