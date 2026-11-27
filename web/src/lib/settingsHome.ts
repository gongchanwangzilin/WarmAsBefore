import type { SettingsValues, ThemeName } from '../bridge/protocol';

/**
 * 设置首页的两组行：分组、顺序、摘要拼法全部照搬
 * SettingsCatalog.All + SettingsViewModel.Home.cs（RefreshSectionSummaries）+ SettingsPage.xaml.cs。
 *
 * 三处口径都在源码里写死过，这里一一对应：
 *   · 常驻组固定三行：桌宠设置 → 用量统计 → 外观与主题（后两者不是目录里的分类，是手工造的行）；
 *   · 其余分类按目录原序进「全部设置」；
 *   · 右侧摘要是「当前值」，不是说明 —— 没在 switch 里列出的分类退回自己的 Desc。
 */

export type SettingsSectionRow = {
  key: string;
  title: string;
  desc: string;
  summary: string;
  /** 这一行在 Web 端有没有可以进去的页；没有就只显示，不跳 */
  to: string | null;
};

type CatalogSection = { key: string; title: string; desc: string };

/** 与 SettingsCatalog.All 同序同文。 */
const SETTINGS_CATALOG: CatalogSection[] = [
  {
    key: 'appearance',
    title: '外观与主题',
    desc: '玻璃效果、光源方向、页面背景与天气叠加',
  },
  {
    key: 'sound',
    title: '声音与语音',
    desc: '背景音乐、音效、按键/打字音；朗读与语音识别、音色、外部语音服务',
  },
  {
    key: 'ai',
    title: 'AI 与模型',
    desc: '厂商与模型、密钥、识图、量化、花费与省钱、AI 绘画',
  },
  {
    key: 'memory',
    title: '记忆与对话',
    desc: '记多少、记多久、上下文上限、核心记忆、创伤与链式回忆',
  },
  {
    key: 'persona',
    title: '身份与心理',
    desc: '旁白 AI、你的性别、自定义性别、心理状况与身体感受、大脑预判',
  },
  {
    key: 'interface',
    title: '界面与玩法',
    desc: '屏幕方向、聊天风格、地图底图与离线包、小游戏难度与棋力、金钱系统、小组件、着装与性欲',
  },
  {
    key: 'platform',
    title: '平台与运行',
    desc: '桌宠、窗口与多开、显卡偏好、外部工具运行时、实验功能',
  },
  {
    key: 'integrations',
    title: '外部接入',
    desc: 'QQ 与微信机器人、MCP 服务、工具安全、插件与工坊、视频号外链',
  },
  {
    key: 'life',
    title: '通知与生活',
    desc: '通知与问候、浮动通知的样式与位置、天气、生理周期、存档运行时',
  },
  {
    key: 'data',
    title: '数据与素材',
    desc: '数据导入导出、云存档、自动更新、素材库、能力包',
  },
  {
    key: 'advanced',
    title: '其他与开发者',
    desc: '支持作者、开发者展示模式',
  },
];

/** 分类在 Web 端已经有页的，走这里。 */
/** 分类 → 子页地址。地址就是 AppShell 里注册的名字，查询串带分类键。 */
const SECTION_ROUTES: Record<string, string> = {
  appearance: '/settingssection?key=appearance',
  sound: '/settingssection?key=sound',
  ai: '/settingssection?key=ai',
  memory: '/settingssection?key=memory',
  persona: '/settingssection?key=persona',
  interface: '/settingssection?key=interface',
  platform: '/settingssection?key=platform',
  integrations: '/settingssection?key=integrations',
  life: '/settingssection?key=life',
  data: '/settingssection?key=data',
  advanced: '/settingssection?key=advanced',
};

const THEME_DISPLAY: Record<ThemeName, string> = {
  sakura: '樱花粉',
  bamboo: '翠竹绿',
  mist: '晨雾蓝灰',
  endfield: '终末地',
  classic: '经典',
};

/** 把「天」讲成人话：不足一天说小时，一年上下说月（WindowText）。 */
function windowText(days: number): string {
  if (days <= 0) return '不限';
  if (days < 1) return `${Number((days * 24).toFixed(1))} 小时`;
  if (days < 30) return `${Number(days.toFixed(1))} 天`;
  return `${Number((days / 30).toFixed(1))} 个月`;
}

const percent = (value: number) => `${Math.round(value * 100)}%`;

const ORIENTATION_DISPLAY = {
  auto: '自动（竖屏）',
  landscape: '横屏',
  portrait: '竖屏',
} as const;

const DIFFICULTY_DISPLAY = {
  easy: '简单（新手练手）',
  normal: '普通（默认）',
  hard: '困难（步步紧逼）',
} as const;

function summaryOf(key: string, desc: string, values: SettingsValues): string {
  const { appearance, sound, ai, memory, ui, platform, integrations, life, data } = values;
  switch (key) {
    case 'appearance': {
      const tier = appearance.liquid
        ? '液态玻璃'
        : appearance.glass
          ? '毛玻璃'
          : appearance.frost
            ? '磨砂'
            : '纯色';
      return `${tier} · ${THEME_DISPLAY[appearance.theme] ?? '经典'}`;
    }
    case 'sound':
      return `BGM ${percent(sound.bgm)} · 音效 ${percent(sound.sfx)}`;
    case 'ai':
      return `${ai.providers} 家厂商 · ${ai.smartSaving ? '省钱开' : '省钱关'} · 超时 ${ai.timeoutSeconds}s`;
    case 'memory':
      return `回想 ${windowText(memory.windowDays)} · 上下文 ${memory.contextLimit}`;
    case 'interface':
      return `${ORIENTATION_DISPLAY[ui.orientation]} · ${
        ui.chatStyle === 'bubble' ? '气泡式' : '剧本式'
      } · 难度 ${DIFFICULTY_DISPLAY[ui.difficulty]}`;
    case 'platform':
      return `桌宠闲置 ${
        platform.petIdleMinutes === 0 ? '关' : `${platform.petIdleMinutes} 分`
      } · ${platform.preferDiscreteGpu ? '独显' : '自动显卡'}`;
    case 'integrations':
      return `${integrations.qq ? 'QQ 开' : 'QQ 关'} · ${
        integrations.wechat ? '微信开' : '微信关'
      } · MCP ${integrations.mcp ? '开' : '关'}`;
    case 'life':
      return `${life.notifications ? '通知开' : '通知关'} · ${
        life.weatherCity.trim() ? life.weatherCity : '未设城市'
      }${life.customWeatherSource ? '（自定义源）' : ''}`;
    case 'data':
      return `素材库 · 能力包 ${data.skillsInstalled ? '已装' : '未装'}`;
    default:
      return desc;
  }
}

/** 两枚手工行：不是目录里的分类，摘要也是页面里写死的常量。 */
const PET_ROW: SettingsSectionRow = {
  key: '__petsettings__',
  title: '桌宠设置',
  desc: '桌面上陪你的那一位',
  summary: '体型 · 动作 · 气泡 · 语音 · 位置',
  to: '/petsettings',
};

const USAGE_ROW: SettingsSectionRow = {
  key: '__usage__',
  title: '用量统计',
  desc: 'Token 用量与花费的本地账本',
  summary: '今日 · 近 7 天 · 成本构成 · 热力图',
  to: '/usagestats',
};

/** 常驻 + 全部设置两组，顺序与 SettingsPage.xaml.cs 的 RebuildList 一致。 */
export function homeGroups(values: SettingsValues): {
  primary: SettingsSectionRow[];
  rest: SettingsSectionRow[];
} {
  const catalog = SETTINGS_CATALOG.map((section) => ({
    key: section.key,
    title: section.title,
    desc: section.desc,
    summary: summaryOf(section.key, section.desc, values),
    to: SECTION_ROUTES[section.key] ?? null,
  }));

  const appearance = catalog.filter((row) => row.key === 'appearance');
  const others = catalog.filter((row) => row.key !== 'appearance');

  return {
    primary: [PET_ROW, USAGE_ROW, ...appearance],
    rest: others,
  };
}

/** 搜索：标题、说明、当前值任一处命中即留下（分类关键词表没有搬过来，见报告）。 */
export function matchesQuery(row: SettingsSectionRow, query: string): boolean {
  const q = query.trim().toLowerCase();
  if (!q) return true;
  return [row.title, row.desc, row.summary].some((text) => text.toLowerCase().includes(q));
}
