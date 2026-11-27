import { useId, useRef, type CSSProperties, type KeyboardEvent, type PointerEvent } from 'react';
import { Glass } from './Glass';
import { usePress } from '../lib/usePress';

type Props = {
  label: string;
  value: number;
  onChange: (next: number) => void;
  /** 右侧读数的写法，默认百分比 */
  format?: (value: number) => string;
};

const THUMB = 28;
const pct = (v: number) => `${Math.round(v * 100)}%`;

export function GlassSlider({ label, value, onChange, format = pct }: Props) {
  const labelId = useId();
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
    <div className="row row--slider">
      <span className="row__label" id={labelId}>
        {label}
      </span>
      <div className="row__control">
        <div
          ref={(node) => {
            press.ref.current = node;
            trackRef.current = node;
          }}
          className="slider"
          role="slider"
          tabIndex={0}
          aria-labelledby={labelId}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.round(value * 100)}
          aria-valuetext={format(value)}
          style={{ '--p': value } as CSSProperties}
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
        <span className="row__value" aria-hidden="true">
          {format(value)}
        </span>
      </div>
    </div>
  );
}
