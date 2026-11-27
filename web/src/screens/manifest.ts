import type { Screen } from './types';

/**
 * 路由 → 组。**只为懒加载服务**：这一张表让 Vite 把每一组切成独立的 chunk，
 * 打开应用只下载首屏那一组（标题页/设置首页本来就在主包里），其余页面走到才下载。
 *
 * 键必须与 routes.ts 的 id 一致；漏一个的后果是那一页白屏 —— App 会兜底画「还没有搬到 Web 端」，
 * 但正确做法是补上这一行。
 */
export const ROUTE_GROUP: Record<string, GroupName> = {
  main: 'main',
  phone: 'main',
  select: 'main',
  setup: 'main',

  settingssection: 'section',

  aiProviders: 'settings-ai',
  aiModels: 'settings-ai',
  modelDeployment: 'settings-ai',
  pluginCenter: 'settings-ai',
  pluginSettings: 'settings-ai',
  pluginWeb: 'settings-ai',
  pluginPerms: 'settings-ai',
  saveRules: 'settings-ai',

  usagestats: 'settings-misc',
  cloudsaves: 'settings-misc',
  colorpicker: 'settings-misc',
  mentalHealth: 'settings-misc',
  petsettings: 'settings-misc',
  petstudio: 'settings-misc',

  studio: 'character',
  newcharacter: 'character',
  roster: 'character',
  subProfile: 'character',

  gallery: 'character-media',
  outfit: 'character-media',
  figurePack: 'character-media',
  materials: 'character-media',
  portraitCalibration: 'character-media',
  avatarcrop: 'character-media',
  memoryManage: 'character-media',
  memoryGraph: 'character-media',
  memorySearch: 'character-media',
  superMonitor: 'character-media',
  moduleInspector: 'character-media',

  newrun: 'story',
  worldbook: 'story',
  chatimport: 'story',
  scenelibrary: 'story',
  SceneCalibrationPage: 'story',
  forcecast: 'story',
  'showcase-list': 'story',
  'showcase-edit': 'story',
  'showcase-play': 'story',
  novelselect: 'story',
  novelworld: 'story',

  game: 'games',
  map: 'games',
  mapcreate: 'games',
  mapcapture: 'games',
  minimal3d: 'games',
  flows: 'games',
  shop: 'games',
  'affection-level-up': 'games',
  'cg-view': 'games',
  pet: 'games',

  save: 'save-chat',
  saveTree: 'save-chat',
  chat: 'save-chat',
  qqchat: 'save-chat',
  qqforum: 'save-chat',
  bindwizard: 'save-chat',
};

export type GroupName =
  | 'main'
  | 'section'
  | 'settings-ai'
  | 'settings-misc'
  | 'character'
  | 'character-media'
  | 'story'
  | 'games'
  | 'save-chat';

export const GROUP_LOADERS: Record<GroupName, () => Promise<{ screens: Record<string, Screen> }>> = {
  main: () => import('./main'),
  section: () => import('./section'),
  'settings-ai': () => import('./settings-ai'),
  'settings-misc': () => import('./settings-misc'),
  character: () => import('./character'),
  'character-media': () => import('./character-media'),
  story: () => import('./story'),
  games: () => import('./games'),
  'save-chat': () => import('./save-chat'),
};
