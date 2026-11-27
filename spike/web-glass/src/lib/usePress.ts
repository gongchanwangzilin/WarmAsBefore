import { useCallback, useEffect, useRef, type RefObject } from 'react';
import { bindPress, type PressControl } from './motion';

export function usePress<T extends HTMLElement>(external?: RefObject<T | null>) {
  const local = useRef<T | null>(null);
  const ref = external ?? local;
  const ctl = useRef<PressControl | null>(null);

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    ctl.current = bindPress(el);
    return () => {
      ctl.current?.dispose();
      ctl.current = null;
    };
  }, [ref]);

  const down = useCallback(() => ctl.current?.set(1), []);
  const up = useCallback(() => ctl.current?.set(0), []);
  const flash = useCallback(() => {
    ctl.current?.set(1);
    window.setTimeout(() => ctl.current?.set(0), 120);
  }, []);

  return { ref, down, up, flash };
}
