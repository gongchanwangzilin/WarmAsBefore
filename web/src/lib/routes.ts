/**
 * 全站路由表。路径就是 AppShell.xaml.cs 里 RegisterRoute 的那个名字 ——
 * 不多加一层「page」「view」的后缀，源码叫什么，地址就是什么。
 *
 * depth 只用来判过场方向（越深越靠里），不代表真实返回栈；
 * 真实返回栈由 PageDeck 里叠着的那两层决定。
 */

export type RouteDef = {
  /** 注册名，同时是路径去掉前导斜杠后的值 */
  id: string;
  path: string;
  /** 0 = 标题页；1 = 从标题页直达；2 = 一级详情；3 = 详情里的详情 */
  depth: number;
  /** 窗口标题 / 读屏用的页面名（可见页头由各页自己画） */
  name: string;
};

const route = (id: string, depth: number, name: string): RouteDef => ({
  id,
  path: `/${id}`,
  depth,
  name,
});

export const ROUTES: RouteDef[] = [
  { id: 'title', path: '/', depth: 0, name: '温暖如初' },

  // 从标题页直达
  route('main', 1, '游戏'),
  route('setup', 1, '开局设置'),
  route('select', 1, '选择角色'),
  route('save', 1, '存档管理'),
  route('settings', 1, '设置'),
  route('phone', 1, '手机'),
  route('worldbook', 1, '世界书'),
  route('pet', 1, '桌宠'),
  route('gallery', 1, '回忆录'),
  route('roster', 1, '角色库'),
  route('characterView', 2, '仔细看'),
  route('novelselect', 1, '平行宇宙'),
  route('showcase-list', 1, '开发者展示模式'),

  // 设置分类子页与它的详情
  route('settingssection', 2, '设置'),
  route('usagestats', 3, '用量统计'),
  route('petsettings', 3, '桌宠设置'),
  route('petstudio', 4, 'AI 制作桌宠'),
  route('colorpicker', 4, '选个颜色'),
  route('mentalHealth', 4, '心理状况'),
  route('aiProviders', 4, 'AI 厂商'),
  route('aiModels', 4, '模型管理'),
  route('modelDeployment', 5, '模型部署'),
  route('pluginCenter', 4, '插件中心'),
  route('pluginSettings', 5, '插件设置'),
  route('pluginWeb', 6, '插件页面'),
  route('pluginPerms', 5, '插件权限'),
  route('saveRules', 3, '存档高级设置'),
  route('cloudsaves', 3, '云存档'),

  // 存档
  route('saveTree', 2, '分支树'),

  // 角色
  route('newcharacter', 2, '新建角色'),
  route('studio', 2, '角色工作室'),
  route('figurePack', 3, '形象包'),
  route('outfit', 3, '换装'),
  route('materials', 2, '素材库'),
  route('portraitCalibration', 3, '立绘标定'),
  route('avatarcrop', 3, '头像裁剪'),
  route('subProfile', 2, '主设定 × 副设定'),
  route('memoryManage', 2, '记忆管理'),
  route('memoryGraph', 3, '关系网'),
  route('memorySearch', 3, '搜事件'),

  // 剧情
  route('newrun', 2, '新开一局'),
  route('chatimport', 2, '导入对话'),
  route('scenelibrary', 2, '场景工坊'),
  route('SceneCalibrationPage', 3, '背景标定'),
  route('forcecast', 3, '点名 · 进入剧情'),
  route('showcase-edit', 2, '编辑展示案'),
  route('showcase-play', 3, '播放展示案'),
  route('novelworld', 2, '世界'),

  // 游戏内
  route('game', 2, '小游戏'),
  route('map', 2, '地图'),
  route('mapcreate', 3, '创建地图'),
  route('mapcapture', 3, '取景与导入'),
  route('minimal3d', 3, '3D 灰度视图'),
  route('flows', 3, '流程图'),
  route('shop', 2, '商店'),
  route('affection-level-up', 3, '好感提升'),
  route('cg-view', 3, 'CG'),

  // 聊天
  route('chat', 2, '微信'),
  route('qqchat', 2, 'QQ'),
  route('qqforum', 3, '论坛'),
  route('bindwizard', 2, '绑定向导'),

  // 监测
  route('superMonitor', 2, '超级监测'),
  route('moduleInspector', 3, '模块详情'),
];

/** 页面已经实现的那几条（原有的三页仍在 pages/ 下，其余在 screens/ 下）。 */
export const BY_PATH = new Map<string, RouteDef>(ROUTES.map((r) => [r.path, r]));

export const FALLBACK = ROUTES[0];

export function parsePath(raw: string): { path: string; query: URLSearchParams } {
  const clean = raw.startsWith('/') ? raw : `/${raw}`;
  const cut = clean.indexOf('?');
  const path = (cut >= 0 ? clean.slice(0, cut) : clean).replace(/\/+$/, '') || '/';
  return { path, query: new URLSearchParams(cut >= 0 ? clean.slice(cut + 1) : '') };
}

export function matchRoute(raw: string): { route: RouteDef; query: URLSearchParams } {
  const { path, query } = parsePath(raw);
  return { route: BY_PATH.get(path) ?? FALLBACK, query };
}
