import { useCallback, useEffect, useRef, useState } from 'react';
import { toErrorBody, type Bridge } from '../bridge/client';
import type { ErrorBody, GlassPalette } from '../bridge/protocol';
import { DEFAULT_PALETTE } from './defaults';

export type PaletteStatus = 'loading' | 'ready' | 'empty' | 'error';
export type SaveStatus = 'idle' | 'saving' | 'error';

export type PaletteStore = {
  value: GlassPalette;
  status: PaletteStatus;
  error: ErrorBody | null;
  save: SaveStatus;
  reload: () => void;
  /** 本地立刻生效，300ms 合并一次写回后端 */
  update: (next: GlassPalette) => void;
  /** 后端推来的权威值，不写回 */
  replace: (next: GlassPalette) => void;
  retrySave: () => void;
};

const SAVE_DEBOUNCE_MS = 300;

/**
 * 光谱是全局状态：它在后端、却被每一个玻璃块用着（透明度改的是整屏材质）。
 * 所以加载一次放在 App 上，玻璃页只负责改它、显示它的四种状态。
 */
export function usePalette(bridge: Bridge): PaletteStore {
  const [value, setValue] = useState<GlassPalette>(DEFAULT_PALETTE);
  const [status, setStatus] = useState<PaletteStatus>('loading');
  const [error, setError] = useState<ErrorBody | null>(null);
  const [save, setSave] = useState<SaveStatus>('idle');

  const latest = useRef<GlassPalette>(DEFAULT_PALETTE);
  const timer = useRef<number | null>(null);

  useEffect(
    () => () => {
      if (timer.current) window.clearTimeout(timer.current);
    },
    [],
  );

  const reload = useCallback(() => {
    setStatus('loading');
    setError(null);
    bridge.request('palette.get', {}).then(
      ({ palette }) => {
        if (!palette) {
          setStatus('empty');
          return;
        }
        latest.current = palette;
        setValue(palette);
        setStatus('ready');
      },
      (cause: unknown) => {
        setError(toErrorBody(cause));
        setStatus('error');
      },
    );
  }, [bridge]);

  useEffect(() => {
    reload();
  }, [reload]);

  const persist = useCallback(
    (palette: GlassPalette) => {
      setSave('saving');
      bridge.request('palette.set', { palette }).then(
        ({ palette: saved }) => {
          latest.current = saved;
          setValue(saved);
          setStatus('ready');
          setSave('idle');
        },
        () => setSave('error'),
      );
    },
    [bridge],
  );

  const update = useCallback(
    (next: GlassPalette) => {
      latest.current = next;
      setValue(next);
      setSave('saving');
      if (timer.current) window.clearTimeout(timer.current);
      timer.current = window.setTimeout(() => persist(latest.current), SAVE_DEBOUNCE_MS);
    },
    [persist],
  );

  const replace = useCallback((next: GlassPalette) => {
    latest.current = next;
    setValue(next);
    setStatus('ready');
  }, []);

  const retrySave = useCallback(() => persist(latest.current), [persist]);

  return { value, status, error, save, reload, update, replace, retrySave };
}
