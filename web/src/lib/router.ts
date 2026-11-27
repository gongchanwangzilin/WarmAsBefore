import { useCallback, useEffect, useRef, useState } from 'react';
import { FALLBACK, matchRoute, type RouteDef } from './routes';

export type Route = RouteDef & { query: URLSearchParams };
export type Direction = 'forward' | 'back';

/**
 * 自写 hash 路由：不引 react-router。
 * 方向由「新页面的 depth vs 当前 depth」得出，所以浏览器自己的后退键、
 * 宿主推来的 nav.request、界面里的返回按钮走的是同一条判断。
 *
 * 地址里带查询串（/settingssection?key=ai），页面从 route.query 取参数。
 */
export function useRouter() {
  const [nav, setNav] = useState<{ route: Route; direction: Direction }>(() => ({
    route: read(),
    direction: 'forward',
  }));

  const latest = useRef(nav);

  useEffect(() => {
    const onHashChange = () => {
      const route = read();
      if (route.path === latest.current.route.path) {
        if (route.query.toString() !== latest.current.route.query.toString()) {
          setNav((prev) => ({ route, direction: prev.direction }));
        }
        return;
      }
      const direction: Direction = route.depth<= latest.current.route.depth ? 'back' : 'forward';
      setNav({ route, direction });
    };
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  useEffect(() => {
    latest.current = nav;
    window.document.title = `${nav.route.name} · 温暖如初`;
  }, [nav]);

  const navigate = useCallback((path: string) => {
    const next = path.startsWith('/') ? path : `/${path}`;
    if (`#${next}` === window.location.hash) return;
    window.location.hash = next;
  }, []);

  const back = useCallback(() => {
    if (history.length > 1) history.back();
    else window.location.hash = '/';
  }, []);

  return { route: nav.route, direction: nav.direction, navigate, back };
}

function read(): Route {
  const { route, query } = matchRoute(window.location.hash.replace(/^#/, '') || '/');
  return { ...(route ?? FALLBACK), query };
}
