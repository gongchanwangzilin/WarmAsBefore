import { lazy, type ComponentType } from 'react';
import { GROUP_LOADERS, ROUTE_GROUP } from './manifest';
import type { Screen, ScreenProps } from './types';

/**
 * 路由名 → 页面，但**不再是静态导入**。
 *
 * 之前 61 页全在主包里：一份 1.2 MB 的 JS 要在启动时解析完，之后每次切页都在这张大表上重新渲染，
 * 于是「点什么都慢」。现在每一组单独成一个 chunk：首屏只要标题页/设置首页那份，
 * 走到哪一组才下载哪一组；页面本身也只在那条路由第一次出现时加载。
 *
 * 加载期间 App 画骨架（Suspense），不是白屏。
 */
const cache = new Map<string, ComponentType<ScreenProps>>();

export function screenFor(id: string): Screen | null {
  const cached = cache.get(id);
  if (cached) return cached as Screen;

  const group = ROUTE_GROUP[id];
  if (!group) return null;

  const Lazy = lazy(async () => {
    const mod = await GROUP_LOADERS[group]();
    const Screen = mod.screens[id];
    if (!Screen) throw new Error(`页面 ${id} 没有在 ${group} 里注册`);
    return { default: Screen };
  });

  cache.set(id, Lazy as unknown as ComponentType<ScreenProps>);
  return Lazy as unknown as Screen;
}

/** 预取一组（悬停/按下入口时可以先热起来，真点了就不等）。 */
export function prefetchGroup(id: string): void {
  const group = ROUTE_GROUP[id];
  if (group) void GROUP_LOADERS[group]();
}
