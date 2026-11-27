import { useRef, type CSSProperties, type KeyboardEvent, type PointerEvent } from 'react';
import { Glass } from './Glass';
import { usePress } from '../lib/usePress';

type Props = {
  label: string;
  value: number;
  onChange: (next: number) => void;
  /** 轨道宽度：玻璃三项 120，场景音量 140（源码里的 WidthRequest） */
  width?: number;
  disabled?: boolean;
  /** 右侧读数的写法；设置卡里的滑杆不带读数，这一项默认不显示 */
  format?: (value: number) => string;
};

const THUMB = 28;
const pct = (v: number) => `${Math.round(v * 100)}%`;

/**
 * 滑杆只剩轨道本身：主标题与说明归 SettingRow（源码里滑杆只占右侧那一格，
 * 宽度 120，没有数值读数）。
 */
export function GlassSlider({ label, value, onChange, width = 120, disabled, format = pct }: Props) {
  const press = usePress<HTMLDivElement>();
  const trackRef = useRef<HTMLDivElement | null>(null);
  const dragging = useRef(false);

  const clamp = (v: number) => Math.min(1, Math.max(0, v));

  const setFromX = (clientX: number) => {
    const el = trackRef.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    const travel = Math.max(1, rect.width - THUMB);
    onChange(clamp((clientX - rect.left - THUMB / 2) / travel));
  };

  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    if (disabled) return;
    event.currentTarget.setPointerCapture(event.pointerId);
    dragging.current = true;
    press.down();
    setFromX(event.clientX);
  };

  const onPointerMove = (event: PointerEvent<HTMLDivElement>) => {
    if (dragging.current) setFromX(event.clientX);
  };

  const endDrag = (event: PointerEvent<HTMLDivElement>) => {
    dragging.current = false;
    press.up();
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (disabled) return;
    const step = event.shiftKey ? 0.1 : 0.02;
    const map: Record<string, number | undefined> = {
      ArrowLeft: value - step,
      ArrowDown: value - step,
      ArrowRight: value + step,
      ArrowUp: value + step,
      PageDown: value - 0.1,
      PageUp: value + 0.1,
      Home: 0,
      End: 1,
    };
    const next = map[event.key];
    if (next === undefined) return;
    event.preventDefault();
    press.flash();
    onChange(clamp(next));
  };

  return (
    <div
      ref={(node) => {
        press.ref.current = node;
        trackRef.current = node;
      }}
      className="slider"
      role="slider"
      tabIndex={disabled ? -1 : 0}
      aria-label={label}
      aria-disabled={disabled || undefined}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(value * 100)}
      aria-valuetext={format(value)}
      data-disabled={disabled ? '1' : undefined}
      style={{ '--p': value, width: `${width}px` } as CSSProperties}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={endDrag}
      onPointerCancel={endDrag}
      onKeyDown={onKeyDown}
    >
      <Glass tone="track" radius={6} className="slider__track" />
      <span className="slider__fill" aria-hidden="true" />
      <span className="slider__bump" aria-hidden="true" />
      <span className="slider__tension" aria-hidden="true" />
      <Glass tone="thumb" radius={14} className="slider__thumb" />
    </div>
  );
}
