import { useCallback, useEffect, useRef, useState } from 'react';
import { toErrorBody } from '../bridge/client';
import type { ErrorBody } from '../bridge/protocol';

export type Resource<T> =
  | { status: 'loading' }
  | { status: 'error'; error: ErrorBody }
  | { status: 'ready'; data: T };

/**
 * 一页一个请求的装载状态。三条出口：loading（骨架屏）、error（说清 + 可重试）、
 * ready（数据在手里，空不空由页面自己判断——空是数据的一种，不是状态的一种）。
 */
export function useResource<T>(
  load: () => Promise<T>,
  deps: readonly unknown[] = [],
): { state: Resource<T>; reload: () => void } {
  const loadRef = useRef(load);
  loadRef.current = load;

  const [state, setState] = useState<Resource<T>>({ status: 'loading' });
  const ticket = useRef(0);

  const run = useCallback(() => {
    const mine = ++ticket.current;
    setState({ status: 'loading' });
    loadRef.current().then(
      (data) => {
        if (mine === ticket.current) setState({ status: 'ready', data });
      },
      (error: unknown) => {
        if (mine === ticket.current) setState({ status: 'error', error: toErrorBody(error) });
      },
    );
  }, []);

  useEffect(() => {
    run();
    return () => {
      ticket.current += 1;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [run, ...deps]);

  return { state, reload: run };
}
