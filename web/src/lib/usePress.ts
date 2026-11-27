import { useCallback, useEffect, useRef, type RefObject } from 'react';
import { PRESS_MS, canonical, tween, type TweenHandle } from './motion';

export type PressControl = {
  set(value: number): void;
  dispose(): void;
};

const clamp01 = (v: number) => Math.min(1, Math.max(0, v));

/** 元素上的 --press 0..1：位移与缩放都从这里取，不碰布局。 */
export function bindPress(el: HTMLElement, name = '--press'): PressControl {
  let value = 0;
  let handle: TweenHandle | null = null;

  const write = (v: number) => {
    value = v;
    el.style.setProperty(name, v.toFixed(3));
  };

  return {
    set(next: number) {
      const target = clamp01(next);
      handle?.cancel();
      handle = tween({ press: value }, { press: target }, canonical(PRESS_MS), (v) => {
        write(v.press);
      });
    },
    dispose() {
      handle?.cancel();
    },
  };
}

export function usePress<T extends HTMLElement>(external?: RefObject<T | null>) {
  const local = useRef<T | null>(null);
  const ref = external ?? local;
  const control = useRef<PressControl | null>(null);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    control.current = bindPress(el);
    return () => {
      control.current?.dispose();
      control.current = null;
    };
  }, [ref]);

  const down = useCallback(() => control.current?.set(1), []);
  const up = useCallback(() => control.current?.set(0), []);
  const flash = useCallback(() => {
    control.current?.set(1);
    window.setTimeout(() => control.current?.set(0), 120);
  }, []);

  return { ref, down, up, flash };
}
