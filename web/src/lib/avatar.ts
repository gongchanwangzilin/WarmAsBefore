import type { Bridge } from '../bridge/client';

/**
 * 换头像：图由宿主挑（前端没有文件系统的门），宿主拷进角色目录再写回 Avatar。
 * 用户点掉选图框返回 null —— 那不是失败，所以不报错。
 */
export async function importAvatar(bridge: Bridge, id: string): Promise<string | null> {
  if (!id) return null;
  const result = await bridge.request('characters.importAvatar', { id });
  return result.canceled ? null : result.avatarUrl || null;
}
