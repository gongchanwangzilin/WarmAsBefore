import type { Screen } from '../types';
import { AiModels } from './aiModels';
import { AiProviders } from './aiProviders';
import { ModelDeployment } from './modelDeployment';
import { PluginCenter } from './pluginCenter';
import { PluginPerms } from './pluginPerms';
import { PluginSettings } from './pluginSettings';
import { PluginWeb } from './pluginWeb';
import { SaveRules } from './saveRules';

/** 这一组页面。子页各自一个文件，名字就是路由名。 */
export const screens: Record<string, Screen> = {
  aiProviders: AiProviders,
  aiModels: AiModels,
  modelDeployment: ModelDeployment,
  pluginCenter: PluginCenter,
  pluginSettings: PluginSettings,
  pluginPerms: PluginPerms,
  pluginWeb: PluginWeb,
  saveRules: SaveRules,
};
