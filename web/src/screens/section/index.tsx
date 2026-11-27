import type { Screen } from '../types';
import { SettingsSection } from './settingssection';

/** 这一组页面。子页各自一个文件，名字就是路由名。 */
export const screens: Record<string, Screen> = {
  settingssection: SettingsSection,
};
