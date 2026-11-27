import type { Screen } from './types';
import { screens as characterMedia } from './character-media';
import { screens as character } from './character';
import { screens as games } from './games';
import { screens as main } from './main';
import { screens as saveChat } from './save-chat';
import { screens as section } from './section';
import { screens as settingsAi } from './settings-ai';
import { screens as settingsMisc } from './settings-misc';
import { screens as story } from './story';

/**
 * 路由名 → 页面。九组各管一段，键就是 AppShell 里注册的那个名字，
 * 所以「地址」与「页面」之间没有第二套映射。
 */
export const SCREENS: Record<string, Screen> = {
  ...main,
  ...section,
  ...settingsAi,
  ...settingsMisc,
  ...character,
  ...characterMedia,
  ...story,
  ...games,
  ...saveChat,
};
