import { useCallback, useEffect, useRef, useState } from 'react';

export type RouteId = 'title' | 'settings' | 'appearance';

export type Route = {
  id: RouteId;
  path: string;
  /** 越深越靠里；只用于判断过场方向 */
  depth: number;
  title: string;
  parent: RouteId | null;
};

export const ROUTES: Record<RouteId, Route> = {
  title: { id: 'title', path: '/', depth: 0, title: '温暖如初', parent: null },
  settings: { id: 'settings', path: '/settings', depth: 1, title: '设置', parent: 'title' },
  appearance: {
    id: 'appearance',
    path: '/settings/appearance',
    depth: 2,
    title: '设置',
    parent: 'settings',
  },
};

export type Direction = 'forward' | 'back';

const BY_PATH = new Map<string, Route>(Object.values(ROUTES).map((route) => [route.path, route]));
const FALLBACK = ROUTES.title;

export function matchRoute(path: string): Route {
  const clean = path.replace(/\/+$/, '') || '/';
  return BY_PATH.get(clean) ?? FALLBACK;
}

function readPath(): string {
  const raw = window.location.hash.replace(/^#/, '');
  return raw.startsWith('/') ? raw : '/';
}

/**
 * 自写 hash 路由：不引 react-router。
 * 方向由「新页面的深度 vs 当前深度」得出，所以浏览器自己的后退键、
 * 宿主推来的 nav.request、界面里的返回按钮走的是同一条判断。
 */
export function useRouter() {
  const [nav, setNav] = useState<{ route: Route; direction: Direction }>(() => ({
    route: matchRoute(readPath()),
    direction: 'forward',
  }));

  const latest = useRef(nav);
  const level = useRef(1);

  useEffect(() => {
    const onHashChange = () => {
      const route = matchRoute(readPath());
      if (route.id === latest.current.route.id) return;
      const direction: Direction = route.depth < latest.current.route.depth ? 'back' : 'forward';
      const next = { route, direction };
      latest.current = next;
      level.current = Math.max(1, level.current + (direction === 'back' ? -1 : 1));
      setNav(next);
    };
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  const navigate = useCallback((path: string) => {
    const target = matchRoute(path);
    if (target.id === latest.current.route.id) return;
    window.location.hash = `#${target.path}`;
  }, []);

  const back = useCallback(() => {
    if (level.current > 1) {
      window.history.back();
      return;
    }
    const parent = latest.current.route.parent;
    if (parent) {
      window.location.hash = `#${ROUTES[parent].path}`;
    }
  }, []);

  return {
    route: nav.route,
    direction: nav.direction,
    navigate,
    back,
    canGoBack: nav.route.parent !== null,
  };
}
