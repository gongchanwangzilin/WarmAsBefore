import { useCallback, useEffect, useRef, useState } from 'react';
import { toErrorBody, type Bridge } from '../bridge/client';
import type { Appearance, ErrorBody } from '../bridge/protocol';
import { DEFAULT_APPEARANCE } from './defaults';

export type AppearanceStatus = 'loading' | 'ready' | 'empty' | 'error';
export type SaveStatus = 'idle' | 'saving' | 'error';

export type AppearanceStore = {
  value: Appearance;
  status: AppearanceStatus;
  error: ErrorBody | null;
  save: SaveStatus;
  reload: () => void;
  /** 本地立刻生效，300ms 合并一次写回后端 */
  update: (next: Appearance) => void;
  /** 后端推来的权威值，不写回 */
  replace: (next: Appearance) => void;
  retrySave: () => void;
};

const SAVE_DEBOUNCE_MS = 300;

/**
 * 同一页里可能有不止一个外观 store（分类子页自己起一个），
 * 广播一下让它们同步：外观改的是整屏材质，不能只改发起那一份。
 */
const SYNC_EVENT = 'wab:appearance';

function announce(next: Appearance) {
  window.dispatchEvent(new CustomEvent<Appearance>(SYNC_EVENT, { detail: next }));
}

/**
 * 外观是全局状态：它在后端、却被每一个玻璃块用着（透明度改的是整屏材质）。
 * 所以加载一次放在 App 上，分类页只负责改它、显示它的四种状态。
 */
export function useAppearance(bridge: Bridge): AppearanceStore {
  const [value, setValue] = useState<Appearance>(DEFAULT_APPEARANCE);
  const [status, setStatus] = useState<AppearanceStatus>('loading');
  const [error, setError] = useState<ErrorBody | null>(null);
  const [save, setSave] = useState<SaveStatus>('idle');

  const latest = useRef<Appearance>(DEFAULT_APPEARANCE);
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
    bridge.request('appearance.get', {}).then(
      ({ appearance }) => {
        if (!appearance) {
          setStatus('empty');
          return;
        }
        latest.current = appearance;
        setValue(appearance);
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
    (next: Appearance) => {
      setSave('saving');
      bridge.request('appearance.set', { appearance: next }).then(
        ({ appearance }) => {
          latest.current = appearance;
          setValue(appearance);
          setStatus('ready');
          setSave('idle');
        },
        () => setSave('error'),
      );
    },
    [bridge],
  );

  const update = useCallback(
    (next: Appearance) => {
      latest.current = next;
      setValue(next);
      announce(next);
      setSave('saving');
      if (timer.current) window.clearTimeout(timer.current);
      timer.current = window.setTimeout(() => persist(latest.current), SAVE_DEBOUNCE_MS);
    },
    [persist],
  );

  const replace = useCallback((next: Appearance) => {
    latest.current = next;
    setValue(next);
    setStatus('ready');
  }, []);

  useEffect(() => {
    const onSync = (event: Event) => {
      const next = (event as CustomEvent<Appearance>).detail;
      if (!next || next === latest.current) return;
      latest.current = next;
      setValue(next);
    };
    window.addEventListener(SYNC_EVENT, onSync);
    return () => window.removeEventListener(SYNC_EVENT, onSync);
  }, []);

  const retrySave = useCallback(() => persist(latest.current), [persist]);

  return { value, status, error, save, reload, update, replace, retrySave };
}
