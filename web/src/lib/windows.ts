import type { Bridge } from '../bridge/client';

/**
 * 「把这一页摊到旁边去」：手机、关系网这类在桌面上本来就该另开一扇的页。
 *
 * 宿主能开窗就直接开（各窗口共用同一座桥，事件是广播的，所以主窗改设置那扇窗立刻跟着变）；
 * 开不了（手机端、或宿主没注册页面）就退回页内导航 —— 点了没反应比少一扇窗更糟。
 */
export async function openInWindow(
  bridge: Bridge,
  route: string,
  title: string,
  size: { width?: number; height?: number } = {},
  fallback?: () => void,
): Promise<boolean> {
  try {
    const { opened } = await bridge.request('window.open', {
      route,
      title,
      width: size.width,
      height: size.height,
    });
    if (opened) return true;
  } catch {
    /* 老宿主没有这个方法，按开不了处理 */
  }
  fallback?.();
  return false;
}

/** 窗里那页的「←」：关掉自己这扇窗，别去翻主窗的返回栈。 */
export async function closeWindow(bridge: Bridge, fallback: () => void): Promise<void> {
  try {
    await bridge.request('window.close', {});
  } catch {
    fallback();
  }
}
