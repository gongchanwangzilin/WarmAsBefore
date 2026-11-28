import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { toErrorBody } from '../../bridge/client';
import type { Appearance, SettingsValues } from '../../bridge/protocol';
import { Glass } from '../../components/Glass';
import { GlassButton } from '../../components/GlassButton';
import { GlassSlider } from '../../components/GlassSlider';
import { GlassSwitch } from '../../components/GlassSwitch';
import { PageShell } from '../../components/PageShell';
import { SignalButton } from '../../components/SignalButton';
import { SettingCard, SettingRow, SettingBlock, Sep } from '../../components/SettingCard';
import { Card, Chip, Empty, Field, PageHeader, Select } from '../../components/kit';
import {
  BACKGROUND_MODE_CHOICES,
  SOLID_SWATCHES,
  pageTintHint,
  solidHint,
  solidVisible,
} from '../../lib/appearance';
import { useAppearance } from '../../lib/useAppearance';
import { DEFAULT_SETTINGS, fillSettings } from '../../lib/defaults';
import type { ScreenProps } from '../types';

/**
 * 「设置分类子页」：SettingsSectionPage.xaml 按 ?key= 渲染 11 个分类。
 * 标题与说明取自 ViewModels/Settings/SettingsCatalog.cs；
 * 每张卡的标题、行序、开关/滑杆/说明逐条照抄 XAML（Card01…Card40）。
 */

type SectionDef = { key: string; title: string; desc: string };

const SECTIONS: SectionDef[] = [
  { key: 'appearance', title: '外观与主题', desc: '玻璃效果、光源方向、页面背景与天气叠加' },
  {
    key: 'sound',
    title: '声音与语音',
    desc: '背景音乐、音效、按键/打字音；朗读与语音识别、音色、外部语音服务',
  },
  { key: 'ai', title: 'AI 与模型', desc: '厂商与模型、密钥、识图、量化、花费与省钱、AI 绘画' },
  { key: 'memory', title: '记忆与对话', desc: '记多少、记多久、上下文上限、核心记忆、创伤与链式回忆' },
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
  { key: 'data', title: '数据与素材', desc: '数据导入导出、云存档、自动更新、素材库、能力包' },
  { key: 'advanced', title: '其他与开发者', desc: '支持作者、开发者展示模式' },
];

/* ── 小店：状态与行 ───────────────────────────────────── */

function useBool(initial: boolean) {
  const [value, setValue] = useState(initial);
  return [value, setValue] as const;
}

function useStr(initial: string) {
  const [value, setValue] = useState(initial);
  return [value, setValue] as const;
}

function useNum(initial: number) {
  const [value, setValue] = useState(initial);
  return [value, setValue] as const;
}

const opts = (labels: readonly string[]) => labels.map((label) => ({ value: label, label }));

/* ── 写回：settings.patch 一条通道，300ms 合并 ──────────── */

type PatchValue = string | number | boolean;
type PatchStatus = 'idle' | 'saved' | 'error';

/**
 * 首帧按源码默认值渲染，settings.get 回来再换成后端的值。
 * 只覆盖这几个键的出厂值，其余照 DEFAULT_SETTINGS。
 */
const INITIAL_SETTINGS: SettingsValues = {
  ...DEFAULT_SETTINGS,
  sound: { bgm: 0.7, sfx: 0.8 },
  memory: { windowDays: 3, contextLimit: '32K' },
  platform: { petIdleMinutes: 0, preferDiscreteGpu: false },
  life: { notifications: true, weatherCity: '', customWeatherSource: false },
};

/** 按 'sound.bgm' 这样的路径改本地副本，返回新对象。 */
function setPath<T extends object>(target: T, path: string, value: PatchValue): T {
  const [group, field] = path.split('.');
  return { ...target, [group]: { ...(target[group as keyof T] as object), [field]: value } };
}

type SettingsWriter = {
  values: SettingsValues;
  status: PatchStatus;
  error: string;
  /** 就地改一行：本地立刻生效，300ms 后把改动合并成一次 settings.patch */
  set: (path: string, value: PatchValue) => void;
};

const PATCH_DEBOUNCE_MS = 300;

/**
 * 设置页的真写入。和 useAppearance 同一套节奏：先动本地状态，后端回的是权威值；
 * 成功与失败只落在页面顶上的状态行（源码里的「已保存」/「保存失败：…」）。
 */
function useSettingsWriter(bridge: ScreenProps['bridge']): SettingsWriter {
  const [values, setValues] = useState<SettingsValues>(INITIAL_SETTINGS);
  const [status, setStatus] = useState<PatchStatus>('idle');
  const [error, setError] = useState('');
  const pending = useRef<Record<string, PatchValue>>({});
  const timer = useRef<number | null>(null);
  /** 已经动过手了：首读就算晚到也不能把刚改的值盖回去。 */
  const touched = useRef(false);

  const flush = useCallback(() => {
    timer.current = null;
    const patch = pending.current;
    if (Object.keys(patch).length === 0) return;
    pending.current = {};
    bridge.request('settings.patch', { patch }).then(
      ({ values: remote }) => {
        setStatus('saved');
        setError('');
        // 拖滑杆时后一次写还在路上：别拿这次的旧值盖掉刚拖到的位置。
        if (timer.current === null && Object.keys(pending.current).length === 0) {
          const filled = fillSettings(remote);
          if (filled) setValues(filled);
        }
      },
      (cause: unknown) => {
        setStatus('error');
        setError(toErrorBody(cause).message);
      },
    );
  }, [bridge]);

  useEffect(() => {
    let live = true;
    bridge.request('settings.get', {}).then(
      ({ values: remote }) => {
        const filled = fillSettings(remote);
        if (live && filled && !touched.current) setValues(filled);
      },
      () => undefined,
    );
    return () => {
      live = false;
      if (timer.current !== null) window.clearTimeout(timer.current);
      const patch = pending.current;
      if (Object.keys(patch).length > 0) {
        pending.current = {};
        bridge.request('settings.patch', { patch }).then(undefined, () => undefined);
      }
    };
  }, [bridge]);

  // 别的窗口（原生设置页、手机窗）改了就跟着换；自己刚拖动时不动。
  useEffect(
    () =>
      bridge.onEvent('settings.changed', (payload) => {
        if (timer.current !== null || Object.keys(pending.current).length > 0) return;
        const filled = fillSettings(payload.values);
        if (filled) setValues(filled);
      }),
    [bridge],
  );

  const set = useCallback(
    (path: string, value: PatchValue) => {
      touched.current = true;
      setValues((prev) => setPath(prev, path, value));
      pending.current = { ...pending.current, [path]: value };
      setStatus((prev) => (prev === 'saved' ? 'idle' : prev));
      if (timer.current !== null) window.clearTimeout(timer.current);
      timer.current = window.setTimeout(flush, PATCH_DEBOUNCE_MS);
    },
    [flush],
  );

  return { values, status, error, set };
}

function WriteStatus({ writer }: { writer: SettingsWriter }) {
  if (writer.status === 'saved') return <p className="cap">已保存</p>;
  if (writer.status === 'error') return <p className="cap cap--warn">保存失败：{writer.error}</p>;
  return null;
}

/** 源码里宽度写死的下拉（Picker WidthRequest）。 */
function PickRow({
  title,
  desc,
  value,
  options,
  onChange,
  width = 190,
  disabled,
  label,
}: {
  title: string;
  desc?: string;
  value: string;
  options: readonly string[];
  onChange: (next: string) => void;
  width?: number;
  disabled?: boolean;
  label?: string;
}) {
  const list = opts(options);
  return (
    <SettingRow
      title={title}
      desc={desc}
      control={
        <span
          style={{
            display: 'inline-flex',
            /* 定宽是源码的 Picker WidthRequest；内容靠右，右侧边才和开关、滑杆落在同一条线 */
            justifyContent: 'flex-end',
            width,
            maxWidth: '100%',
            opacity: disabled ? 0.5 : undefined,
            pointerEvents: disabled ? 'none' : undefined,
          }}
        >
          <Select
            value={list.some((o) => o.value === value) ? value : list[0]?.value ?? ''}
            options={list}
            onChange={onChange}
            label={label ?? title}
          />
        </span>
      }
    />
  );
}

/** Entry 行：左边主标题，右边占一条宽输入框。 */
function FieldRow({
  title,
  value,
  onChange,
  placeholder,
  type = 'text',
  width = 320,
}: {
  title: string;
  value: string;
  onChange: (next: string) => void;
  placeholder?: string;
  type?: 'text' | 'password' | 'number';
  width?: number;
}) {
  return (
    <SettingRow
      title={title}
      control={
        <span style={{ display: 'inline-block', width: `min(${width}px, 44vw)`, maxWidth: '100%' }}>
          <Field value={value} onChange={onChange} placeholder={placeholder} type={type} label={title} />
        </span>
      }
    />
  );
}

/** 0..1 之外的滑杆：内部仍用 GlassSlider 的分数，对外换算成源码里的量程。 */
function RangeSlider({
  label,
  min,
  max,
  step = 0.01,
  value,
  onChange,
  width = 130,
  disabled,
}: {
  label: string;
  min: number;
  max: number;
  step?: number;
  value: number;
  onChange: (next: number) => void;
  width?: number;
  disabled?: boolean;
}) {
  const span = max - min || 1;
  return (
    <GlassSlider
      label={label}
      width={width}
      disabled={disabled}
      value={Math.min(1, Math.max(0, (value - min) / span))}
      onChange={(t) => {
        const raw = min + t * span;
        onChange(Math.round(raw / step) * step);
      }}
    />
  );
}

function Sw({
  label,
  checked,
  onChange,
  disabled,
}: {
  label: string;
  checked: boolean;
  onChange: (next: boolean) => void;
  disabled?: boolean;
}) {
  return <GlassSwitch label={label} checked={checked} onChange={onChange} disabled={disabled} />;
}

function Btn({
  children,
  onClick,
  disabled,
}: {
  children: ReactNode;
  onClick?: () => void;
  disabled?: boolean;
}) {
  // 没有 onClick 的钮（宿主还没给这条动作）画成禁用态：看得见、按不下，
  // 别让它演成一颗点得动却什么都不发生的钮。
  const off = disabled || !onClick;
  return (
    <span style={{ opacity: off ? 0.5 : undefined, pointerEvents: off ? 'none' : undefined }}>
      <GlassButton size="md" className="gbtn--tight" onClick={onClick} disabled={off}>
        {children}
      </GlassButton>
    </span>
  );
}

/**
 * 卡片内的二级小标题：源码里是 TxtSubhead（与卡标题同档），不是页级分组。
 * 之前在卡里用 GroupLabel（大写字距 + 26px 上边距），节奏比源码大了一档。
 */
function SubHead({ children }: { children: ReactNode }) {
  return (
    <span
      style={{
        marginTop: 14,
        fontSize: 'var(--w-size-subhead)',
        fontWeight: 700,
        color: 'var(--w-ink-body)',
      }}
    >
      {children}
    </span>
  );
}

/** 卡片内的一行粗体小标题（源码 TxtBodySm + FontAttributes=Bold，如「QQ 官方机器人」）。 */
function RowHead({ children }: { children: ReactNode }) {
  return (
    <span
      style={{
        marginTop: 13,
        fontSize: 'var(--w-size-small)',
        fontWeight: 700,
        color: 'var(--w-ink-body)',
      }}
    >
      {children}
    </span>
  );
}

/** 源码里有、Web 端没有对应桥方法的那颗钮：画成禁用态，不假装成功。 */
function Inert({ children }: { children: ReactNode }) {
  return (
    <span inert aria-disabled="true" style={{ display: 'inline-grid', opacity: 0.45 }}>
      {children}
    </span>
  );
}

function BtnRow({ children }: { children: ReactNode }) {
  return <div className="btnrow">{children}</div>;
}

function Divider() {
  return <div style={{ height: 1, margin: '4px 0', background: 'var(--w-hairline)' }} role="presentation" />;
}

/* ── 入口 ─────────────────────────────────────────────── */

export function SettingsSection({ bridge, query, back, navigate }: ScreenProps) {
  const raw = (query.get('key') ?? '').trim();
  const key = raw === 'window' ? 'platform' : raw === 'voice' ? 'sound' : raw;
  const section = SECTIONS.find((item) => item.key === key);
  const writer = useSettingsWriter(bridge);

  const body = !section ? (
    <Empty title="未知分类" reason={raw} />
  ) : section.key === 'appearance' ? (
    <AppearanceSection bridge={bridge} />
  ) : section.key === 'sound' ? (
    <SoundSection writer={writer} />
  ) : section.key === 'ai' ? (
    <AiSection writer={writer} bridge={bridge} navigate={navigate} />
  ) : section.key === 'memory' ? (
    <MemorySection writer={writer} />
  ) : section.key === 'persona' ? (
    <PersonaSection navigate={navigate} />
  ) : section.key === 'interface' ? (
    <InterfaceSection writer={writer} />
  ) : section.key === 'platform' ? (
    <PlatformSection writer={writer} navigate={navigate} />
  ) : section.key === 'integrations' ? (
    <IntegrationsSection writer={writer} bridge={bridge} navigate={navigate} />
  ) : section.key === 'life' ? (
    <LifeSection writer={writer} />
  ) : section.key === 'data' ? (
    <DataSection navigate={navigate} />
  ) : (
    <AdvancedSection />
  );

  return (
    <PageShell>
      <PageHeader
        title={section ? section.title : '未知分类'}
        subtitle={section ? section.desc : raw}
        onBack={back}
      />
      <main className="section">
        <div className="section__body">
          <WriteStatus writer={writer} />
          <p className="cap">
            这一页只有接到写回通道的项会真存下来：声音与文字速度、AI 超时与思考档、记忆天数、自动存档、
            界面方向 / 聊天风格 / 难度、桌宠与显卡偏好、QQ 与微信与 MCP 接入、通知与天气城市、语言。
            其余开关与钮改了只在本页生效，重开就回默认；顶上的「已保存」只在真的写进盘之后才出现。
          </p>
          {body}
        </div>
      </main>
    </PageShell>
  );
}

/* ── 外观与主题：Card02 + Card03（与 pages/AppearancePage.tsx 同一份文案） ── */

function AppearanceSection({ bridge }: { bridge: ScreenProps['bridge'] }) {
  const store = useAppearance(bridge);
  const { value } = store;
  const set = (patch: Partial<Appearance>) => store.update({ ...value, ...patch });
  const glassActive = value.alpha > 0.01;

  if (store.status === 'loading') {
    return (
      <Glass as="section" tone="card" radius={12} className="s-card" aria-busy="true">
        <span className="cap">正在读取设置…</span>
      </Glass>
    );
  }

  if (store.status === 'error') {
    return (
      <Card title="读不到设置" note="本地服务这次没有回话。确认后台还在运行，再试一次。">
        <BtnRow>
          <Btn onClick={store.reload}>重新读取</Btn>
        </BtnRow>
      </Card>
    );
  }

  if (store.status === 'empty') {
    return (
      <Card title="没有可调的外观" note="后端还没有下发外观参数。等它挂上来再看。">
        <BtnRow>
          <Btn onClick={store.reload}>再读一次</Btn>
        </BtnRow>
      </Card>
    );
  }

  return (
    <>
      <SettingCard title="玻璃效果">
        <SettingRow
          title="轻量模式（关阴影，低配更流畅）"
          desc="关闭后卡片不再有投影，观感朴素一些。阴影在 WinUI 上是每个元素一个合成层，卡片多或显卡弱时更划算；本机（独立显卡）实测差异在噪声范围内。"
          control={
            <Sw label="轻量模式" checked={value.lightweight} onChange={(lightweight) => set({ lightweight })} />
          }
        />
        <Sep />
        <SettingRow
          gap
          title="玻璃透明度"
          desc="0 = 表面不透明，等同于关闭玻璃；1 = 最通透"
          control={<GlassSlider label="玻璃透明度" value={value.alpha} onChange={(alpha) => set({ alpha })} />}
        />
        {glassActive ? null : (
          <p className="s-warn">玻璃已关闭（透明度为 0）：表面为不透明材质，下方选项不生效。</p>
        )}
        <Sep />
        <SettingRow
          gap
          title="磨砂程度"
          desc="0 = 清玻璃 · 1 = 厚磨砂（奶白雾气浓度）"
          control={
            <GlassSlider
              label="磨砂程度"
              value={value.frost}
              onChange={(frost) => set({ frost })}
              disabled={!glassActive}
            />
          }
        />
        <Sep />
        <SettingRow
          gap
          title="液态高度"
          desc="0 = 贴在页面上 · 1 = 明显浮起（投影深度 + 上沿高光）"
          control={
            <GlassSlider
              label="液态高度"
              value={value.liquid}
              onChange={(liquid) => set({ liquid })}
              disabled={!glassActive}
            />
          }
        />
        <Sep />
        <SettingRow
          title="减弱透明降级（无障碍）"
          desc="开启后玻璃切不透明磨砂层，保证正文 4.5:1 对比"
          control={
            <Sw
              label="减弱透明降级"
              checked={value.reducedTransparency}
              onChange={(reducedTransparency) => set({ reducedTransparency })}
            />
          }
        />
      </SettingCard>

      <AppearanceBackgroundCard bridge={bridge} store={store} />

      {store.save === 'error' ? (
        <p className="cap cap--warn">
          没有写进去。
          <button type="button" className="link" onClick={store.retrySave}>
            重试
          </button>
        </p>
      ) : null}
    </>
  );
}

function AppearanceBackgroundCard({
  store,
}: {
  bridge: ScreenProps['bridge'];
  store: ReturnType<typeof useAppearance>;
}) {
  const { value } = store;
  const bg = value.background;
  const scene = value.scene;
  const set = (patch: Partial<Appearance>) => store.update({ ...value, ...patch });
  const setBackground = (patch: Partial<Appearance['background']>) => set({ background: { ...bg, ...patch } });
  const setScene = (patch: Partial<Appearance['scene']>) => set({ scene: { ...scene, ...patch } });

  const solidPicker = useRef<HTMLInputElement | null>(null);
  const tintPicker = useRef<HTMLInputElement | null>(null);
  const imagePicker = useRef<HTMLInputElement | null>(null);

  return (
    <SettingCard
      title="背景（决定玻璃能透出什么）"
      note="半透明只是前提 —— 背后得有内容才看得见。纯色背景下无论怎么调，玻璃都像一块实心板。"
    >
      <SettingRow
        title="背景模式"
        desc="窗口透视会让整个窗口背景透明，直接看穿到桌面"
        control={
          <select
            className="picker"
            aria-label="背景模式"
            value={bg.mode}
            onChange={(event) => setBackground({ mode: event.target.value as Appearance['background']['mode'] })}
          >
            {BACKGROUND_MODE_CHOICES.map((choice) => (
              <option key={choice.mode} value={choice.mode}>
                {choice.label}
              </option>
            ))}
          </select>
        }
      />

      <Sep />

      {solidVisible(bg.mode) ? (
        <SettingBlock>
          <span className="s-row__title">纯色底</span>
          <p className="s-row__desc">
            十颗挑好的底色：冷/暖/中性、浅/深都有。选中立刻生效 ——
            玻璃背后那张糊底会按新色重新合成，不用重启。想要这十颗以外的颜色，点「选个颜色…」。
          </p>
          <div className="swatches">
            {SOLID_SWATCHES.map((swatch) => {
              const selected = swatch.hex.toLowerCase() === bg.solidHex.toLowerCase();
              return (
                <button
                  key={swatch.hex}
                  type="button"
                  className="swatch"
                  aria-label={swatch.name}
                  aria-pressed={selected}
                  onClick={() => setBackground({ solidHex: swatch.hex })}
                >
                  <span className="swatch__fill" style={{ background: swatch.hex }} />
                </button>
              );
            })}
          </div>
          <div className="btnrow">
            <GlassButton size="md" className="gbtn--tight" onClick={() => solidPicker.current?.click()}>
              选个颜色…
            </GlassButton>
            {bg.solidHex ? <span className="cap">当前 {bg.solidHex}</span> : <span />}
            <button type="button" className="textbtn" onClick={() => setBackground({ solidHex: '' })}>
              跟主题
            </button>
          </div>
          <p className="cap">{solidHint(bg.solidHex)}</p>
          <input
            ref={solidPicker}
            type="color"
            className="colorpick"
            tabIndex={-1}
            aria-hidden="true"
            value={bg.solidHex || SOLID_SWATCHES[0].hex}
            onChange={(event) => setBackground({ solidHex: event.target.value.toUpperCase() })}
          />
        </SettingBlock>
      ) : null}

      <Sep />

      <SettingBlock>
        <span className="s-row__title">页面色（卡片与页面的底色）</span>
        <p className="s-row__desc">
          页面色只决定卡片与页面的底色；背景色决定玻璃背后透出什么。两者互不覆盖，可以各挑各的。
        </p>
        <div className="btnrow">
          <GlassButton size="md" className="gbtn--tight" onClick={() => tintPicker.current?.click()}>
            选个颜色…
          </GlassButton>
          {bg.pageTintHex ? <span className="cap">当前 {bg.pageTintHex}</span> : <span />}
          <button type="button" className="textbtn" onClick={() => setBackground({ pageTintHex: '' })}>
            跟主题
          </button>
        </div>
        <p className="cap">{pageTintHint(bg.pageTintHex)}</p>
        <input
          ref={tintPicker}
          type="color"
          className="colorpick"
          tabIndex={-1}
          aria-hidden="true"
          value={bg.pageTintHex || SOLID_SWATCHES[0].hex}
          onChange={(event) => setBackground({ pageTintHex: event.target.value.toUpperCase() })}
        />
      </SettingBlock>

      {bg.mode === 'image' ? (
        <SettingRow
          title="背景图片"
          desc={bg.imageLabel}
          control={
            <>
              <GlassButton size="md" className="gbtn--tight" onClick={() => imagePicker.current?.click()}>
                选择图片
              </GlassButton>
              <input
                ref={imagePicker}
                type="file"
                accept="image/*"
                className="sr-only"
                tabIndex={-1}
                onChange={(event) =>
                  setBackground({ imageLabel: event.target.files?.[0]?.name ?? '尚未选择图片' })
                }
              />
            </>
          }
        />
      ) : null}

      <SettingRow
        title="测试版场景特效"
        desc="实体云、近景雨、光柱与光向。默认关闭；开启后下方场景选项才生效。"
        control={
          <Sw
            label="测试版场景特效"
            checked={scene.experimental}
            onChange={(experimental) => setScene({ experimental })}
          />
        }
      />

      {scene.experimental ? (
        <>
          <Sep />
          <SettingRow
            title="天气场景叠加"
            desc="晴/阴/毛毛雨/小雨/大雨，雨滴会浮在界面上"
            control={<Sw label="天气场景叠加" checked={scene.weather} onChange={(weather) => setScene({ weather })} />}
          />
          <SettingRow
            title="场景光照与光柱"
            desc="光的方向随一天变化、强度随能见度变化；关掉只保留云与底色"
            control={<Sw label="场景光照与光柱" checked={scene.light} onChange={(light) => setScene({ light })} />}
          />
          {scene.weather ? (
            <>
              <SettingRow
                title="场景音效"
                desc="雨声、风声、闪电；需先在素材库分配音效卡槽"
                control={<Sw label="场景音效" checked={scene.audio} onChange={(audio) => setScene({ audio })} />}
              />
              <SettingRow
                gap
                title="场景音量"
                control={
                  <GlassSlider
                    label="场景音量"
                    width={140}
                    value={scene.audioLevel}
                    onChange={(audioLevel) => setScene({ audioLevel })}
                  />
                }
              />
            </>
          ) : null}
        </>
      ) : null}
    </SettingCard>
  );
}

/* ── 声音与语音：Card01 + Card08 + Card11 ─────────────── */

function SoundSection({ writer }: { writer: SettingsWriter }) {
  const { values, set: write } = writer;
  const [uiSound, setUiSound] = useBool(true);
  const [uiSoundLevel, setUiSoundLevel] = useNum(0.5);
  const [keySfx, setKeySfx] = useStr('default');

  const [tts, setTts] = useBool(true);
  const [ttsEngine, setTtsEngine] = useStr('system');
  const [stt, setStt] = useBool(true);
  const [sttEngine, setSttEngine] = useStr('system');
  const [audioInputMode, setAudioInputMode] = useStr('internal');
  const [advancedAnalysis, setAdvancedAnalysis] = useBool(false);
  const [cameraFace, setCameraFace] = useBool(false);
  const [ttsRate, setTtsRate] = useNum(1);
  const [previewText, setPreviewText] = useStr('你好，我是温暖如初。');
  const [voiceMode, setVoiceMode] = useStr('OpenAI 兼容（网络 API）');
  const [voiceUrl, setVoiceUrl] = useStr('https://api.openai.com/v1');
  const [voiceKey, setVoiceKey] = useStr('');
  const [voiceTtsModel, setVoiceTtsModel] = useStr('tts-1');
  const [voiceName, setVoiceName] = useStr('alloy');
  const [voiceSttModel, setVoiceSttModel] = useStr('whisper-1');
  const [voiceExtra, setVoiceExtra] = useStr('');

  const needsVoiceApi = ttsEngine === 'api' || sttEngine === 'api';
  const sovits = voiceMode === 'GPT-SoVITS（本地/局域网）';

  return (
    <>
      <SettingCard title="音量">
        <SettingRow
          title="BGM"
          control={<GlassSlider label="BGM" value={values.sound.bgm} onChange={(value) => write('sound.bgm', value)} />}
        />
        <Sep />
        <SettingRow
          title="音效"
          control={<GlassSlider label="音效" value={values.sound.sfx} onChange={(value) => write('sound.sfx', value)} />}
        />
        <Sep />
        <SettingRow
          title="界面提示音"
          desc="按钮按下、选择确认、导入完成时的轻响。关掉不影响背景音乐与素材音效。"
          control={<Sw label="界面提示音" checked={uiSound} onChange={setUiSound} />}
        />
        {uiSound ? (
          <SettingRow
            title="提示音音量"
            control={<GlassSlider label="提示音音量" value={uiSoundLevel} onChange={setUiSoundLevel} />}
          />
        ) : null}
      </SettingCard>

      <SettingCard title="文字与音效">
        <SettingRow
          title="文本速度"
          control={
            <RangeSlider
              label="文本速度"
              min={0.5}
              max={2}
              step={0.1}
              value={values.text.scale}
              onChange={(value) => write('text.scale', value)}
            />
          }
        />
        <Sep />
        <PickRow
          title="按键音效"
          desc="点击按钮时的反馈音"
          value={keySfx}
          options={['default', 'soft', 'typewriter', 'none']}
          onChange={setKeySfx}
          width={120}
        />
      </SettingCard>

      <SettingCard title="语音">
        <p className="cap" style={{ margin: '0 0 var(--w-space-2)', color: 'var(--w-signal)', lineHeight: 1.6 }}>
          这一张卡只在页内状态里：宿主还没有写回语音设置的方法（voice.save），开关与密钥填了不会落盘。
        </p>
        <SettingRow
          title="朗读回复"
          desc="收到回复时用语音读出来"
          control={<Sw label="朗读回复" checked={tts} onChange={setTts} />}
        />
        <Sep />
        <PickRow
          title="朗读引擎"
          desc="system=系统自带语音，api=外部 TTS API"
          value={ttsEngine}
          options={['system', 'api', 'local']}
          onChange={setTtsEngine}
          width={100}
        />
        <Sep />
        <SettingRow
          title="语音输入"
          desc="显示语音输入按钮（需麦克风权限）"
          control={<Sw label="语音输入" checked={stt} onChange={setStt} />}
        />
        <Sep />
        <PickRow
          title="识别引擎"
          desc="system=系统自带，local=本地下载模型，api=外部 STT API"
          value={sttEngine}
          options={['system', 'api', 'local']}
          onChange={setSttEngine}
          width={100}
        />
        <PickRow
          title="音频输入模式"
          desc="internal=先内部识别成文字；direct=音频直送支持听音频的大模型"
          value={audioInputMode}
          options={['internal', 'direct']}
          onChange={setAudioInputMode}
          width={100}
        />
        <SettingRow
          title="高级分析"
          desc="把人脸识别/语音韵律的情绪与年龄交大模型深度解读（需配置 API）"
          control={<Sw label="高级分析" checked={advancedAnalysis} onChange={setAdvancedAnalysis} />}
        />
        <SettingRow
          title="摄像头人脸识别"
          desc="允许本地人脸模型分析情绪与年龄，数据只在本机处理"
          control={<Sw label="摄像头人脸识别" checked={cameraFace} onChange={setCameraFace} />}
        />
        <p className="cap">
          只读：只产出情绪 / 年龄 / 是否在场这类信息，不改任何数值、不推剧情、不触发动作；图像只在本机，不上传、不常驻保存。AI
          想「看一眼」时必须经你按下快门，对话开始时也只在有足够新的画面时才判断一次。
        </p>

        {sttEngine === 'local' ? (
          <SettingBlock>
            <span className="s-row__title">本地识别模型</span>
            <PickRow
              title="本地模型"
              value="未选择本地模型"
              options={['未选择本地模型']}
              onChange={() => undefined}
              width={240}
              label="本地模型"
            />
            <p className="cap">未选择本地模型</p>
            <div style={{ display: 'flex', gap: 'var(--w-space-2)' }}>
              <Inert>
                <Btn>下载模型</Btn>
              </Inert>
              <Inert>
                <Btn>删除模型</Btn>
              </Inert>
            </div>
            <p className="cap">local=离线识别（需先下载模型）；api=外部服务（电脑端录音 8 秒后识别）</p>
          </SettingBlock>
        ) : null}

        <SettingRow
          title="朗读语速"
          control={<RangeSlider label="朗读语速" min={0.5} max={2} step={0.1} value={ttsRate} onChange={setTtsRate} />}
        />

        <SettingBlock>
          <span className="s-row__title">试听当前音色</span>
          <Field value={previewText} onChange={setPreviewText} placeholder="输入一段话试听，如：你好，我是温暖如初。" label="试听文本" />
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-2)' }}>
            <span className="cap">按当前设置合成试听：系统引擎走系统语音；API 引擎走外部服务</span>
            <Inert>
              <Btn>试听</Btn>
            </Inert>
          </div>
        </SettingBlock>

        {needsVoiceApi ? (
          <SettingBlock>
            <span className="s-row__title">API 语音配置</span>
            <PickRow
              title="服务类型"
              desc="OpenAI 兼容走网络 API；GPT-SoVITS 可连本地或局域网服务"
              value={voiceMode}
              options={['OpenAI 兼容（网络 API）', 'GPT-SoVITS（本地/局域网）']}
              onChange={setVoiceMode}
              width={150}
            />
            <FieldRow title="API 地址" value={voiceUrl} onChange={setVoiceUrl} placeholder="https://api.openai.com/v1" />
            <FieldRow title="API 密钥" value={voiceKey} onChange={setVoiceKey} placeholder="sk-…" type="password" />
            <FieldRow title="TTS 模型" value={voiceTtsModel} onChange={setVoiceTtsModel} placeholder="tts-1" />
            <FieldRow
              title="TTS 音色"
              value={voiceName}
              onChange={setVoiceName}
              placeholder="alloy / echo / nova / shimmer"
            />
            <FieldRow title="STT 模型" value={voiceSttModel} onChange={setVoiceSttModel} placeholder="whisper-1" />
            {sovits ? (
              <FieldRow title="说话人编号" value={voiceExtra} onChange={setVoiceExtra} placeholder="0=默认角色" />
            ) : null}
            <div>
              <Inert>
                <Btn>测试连接</Btn>
              </Inert>
            </div>
          </SettingBlock>
        ) : null}
      </SettingCard>
    </>
  );
}

/* ── AI 与模型：Card09 + Card31 + Card38 + Card39 ─────── */

const TIMEOUT_CHOICES = ['10', '15', '25', '40', '60', '120', '180'];

/**
 * 思考由谁决定的三档。与宿主 SettingsSectionPage 的「思考由谁决定」下拉同一组说法，
 * 值仍是 UserSettings.ThinkMode 的 auto / on / off。
 */
const THINK_MODE_CHOICES = ['跟随 AI', '总是开', '总是关'] as const;
const THINK_MODE_KEY: Record<string, 'auto' | 'on' | 'off'> = { '跟随 AI': 'auto', '总是开': 'on', '总是关': 'off' };
const THINK_MODE_LABEL: Record<'auto' | 'on' | 'off', string> = { auto: '跟随 AI', on: '总是开', off: '总是关' };

const AI_PRESETS = [
  '通用（OpenAI兼容）',
  'DeepSeek',
  'MiMo（小米）',
  'Kimi（月之暗面）',
  '豆包（火山方舟）',
  '智谱 AI',
  '硅基流动',
  '通义千问（DashScope）',
  '自定义',
];

/* 厂商与模型：ai.providers 的真表。读不到就落回源码那句空态。 */

type ProviderModel = { model: string; label: string; enabled: boolean };

type Provider = {
  name: string;
  endpoint: string;
  enabled: boolean;
  timeoutSeconds: number;
  keyCount: number;
  models: ProviderModel[];
};

/** AiProviderRow.Title / DisplayName / Summary 的同一套写法。 */
const providerTitle = (provider: Provider) => (provider.name.trim() ? provider.name : '（未命名厂商）');

const modelName = (model: ProviderModel) =>
  model.label.trim() ? model.label : model.model.trim() ? model.model : '（未填模型）';

const providerSummary = (provider: Provider) => {
  const on = provider.models.filter((model) => model.enabled).length;
  const timeout = provider.timeoutSeconds > 0 ? ` · 超时 ${provider.timeoutSeconds}s` : '';
  return `${on}/${provider.models.length} 款模型 · ${provider.keyCount} 枚密钥${timeout}`;
};

/** 一家厂商先摊这几款模型，再多就只用一句「共 N 款」收尾。 */
const MODEL_CHIPS = 4;

function ProviderList({ bridge }: { bridge: ScreenProps['bridge'] }) {
  const [providers, setProviders] = useState<Provider[] | null>(null);

  useEffect(() => {
    let live = true;
    bridge.request('ai.providers', {}).then(
      ({ providers: rows }) => {
        if (live) setProviders(rows);
      },
      () => {
        if (live) setProviders([]);
      },
    );
    return () => {
      live = false;
    };
  }, [bridge]);

  if (!providers || providers.length === 0) {
    return <p className="cap">还没有接入厂商。填端点与密钥后点「＋添加厂商」，再往里加模型。</p>;
  }

  const total = providers.reduce((sum, provider) => sum + provider.models.length, 0);

  return (
    <>
      {providers.map((provider, index) => (
        <div
          key={`${index}·${provider.name}·${provider.endpoint}`}
          style={{
            display: 'flex',
            alignItems: 'flex-start',
            gap: 'var(--w-space-2)',
            padding: '10px 12px',
            marginTop: 'var(--w-space-1)',
            borderRadius: 'var(--w-radius-well)',
            background: 'var(--w-surface-tint)',
          }}
        >
          <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 2 }}>
            <span
              style={{
                fontSize: 'var(--w-size-small)',
                fontWeight: 700,
                color: 'var(--w-ink-body)',
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap',
              }}
            >
              {providerTitle(provider)}
            </span>
            <span className="cap" style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
              {provider.endpoint}
            </span>
            <span className="cap">{providerSummary(provider)}</span>
            <div
              style={{
                display: 'flex',
                flexWrap: 'wrap',
                alignItems: 'center',
                gap: 'var(--w-space-1)',
                marginTop: 2,
              }}
            >
              {provider.models.length === 0 ? (
                <span className="cap">还没有模型：到「模型管理」里加一款。</span>
              ) : (
                <>
                  {provider.models.slice(0, MODEL_CHIPS).map((model) => (
                    <span
                      key={model.model}
                      style={{
                        padding: '1px 7px',
                        borderRadius: 9,
                        border: '1px solid var(--w-hairline)',
                        fontSize: 'var(--w-size-caption)',
                        color: model.enabled ? 'var(--w-ink-body)' : 'var(--w-ink-muted)',
                      }}
                    >
                      {modelName(model)}
                    </span>
                  ))}
                  {provider.models.length > MODEL_CHIPS ? (
                    <span className="cap">共 {provider.models.length} 款</span>
                  ) : null}
                </>
              )}
            </div>
          </div>
          <Chip tone={provider.enabled ? 'signal' : 'quiet'}>{provider.enabled ? '已启用' : '已停用'}</Chip>
        </div>
      ))}
      <p className="cap">
        {`已接入 ${providers.length} 家厂商，共 ${total} 款模型。按列表顺序使用：靠前的先用，失灵自动换下一家/下一款（可用 ↑↓ 调整）。`}
      </p>
    </>
  );
}

function AiSection({
  writer,
  bridge,
  navigate,
}: {
  writer: SettingsWriter;
  bridge: ScreenProps['bridge'];
  navigate: ScreenProps['navigate'];
}) {
  const { values, set: write } = writer;
  const [preset, setPreset] = useStr('自定义');
  const [aiUrl, setAiUrl] = useStr('https://api.openai.com/v1/chat/completions');
  const [aiKey, setAiKey] = useStr('');
  const [aiModel, setAiModel] = useStr('gpt-4o');
  const [temperature, setTemperature] = useNum(0.8);
  const [maxTokens, setMaxTokens] = useNum(500);
  const [deepThink, setDeepThink] = useBool(false);
  const [deepModel, setDeepModel] = useStr('');
  const [smallFirst, setSmallFirst] = useBool(false);
  const [smallUrl, setSmallUrl] = useStr('');
  const [smallKey, setSmallKey] = useStr('');
  const [smallModel, setSmallModel] = useStr('');
  const [planFlow, setPlanFlow] = useBool(false);
  const [planMaxSteps, setPlanMaxSteps] = useNum(8);
  const [advisor, setAdvisor] = useBool(false);
  const [advisorFreq, setAdvisorFreq] = useStr('never');
  const [visionUrl, setVisionUrl] = useStr('');
  const [visionKey, setVisionKey] = useStr('');
  const [visionModel, setVisionModel] = useStr('');
  const [embedding, setEmbedding] = useBool(true);
  const [embeddingUrl, setEmbeddingUrl] = useStr('');
  const [embeddingKey, setEmbeddingKey] = useStr('');
  const [embeddingModel, setEmbeddingModel] = useStr('');
  const [embeddingDims, setEmbeddingDims] = useStr('0');
  const [rotation, setRotation] = useBool(true);
  const [newProviderName, setNewProviderName] = useStr('');
  const [newProviderModel, setNewProviderModel] = useStr('');
  const [newProviderUrl, setNewProviderUrl] = useStr('');
  const [newProviderKeys, setNewProviderKeys] = useStr('');
  const [newProviderTimeout, setNewProviderTimeout] = useStr('');
  const [priceModel, setPriceModel] = useStr('');
  const [priceIn, setPriceIn] = useStr('');
  const [priceOut, setPriceOut] = useStr('');
  const [chartMode, setChartMode] = useStr('条形图');

  const [imageGen, setImageGen] = useBool(false);
  const [imageUrl, setImageUrl] = useStr('https://api.openai.com/v1');
  const [imageKey, setImageKey] = useStr('');
  const [imageModel, setImageModel] = useStr('gpt-image-1');
  const [imageSize, setImageSize] = useStr('1024x1024');

  const [paramsFormat, setParamsFormat] = useStr('openai_flat');
  const [paramsJson, setParamsJson] = useStr('');


  // TimeoutChoices：10, 15, 25, 40, 60, 120, 180；存档里是别的值就把它自己补进下拉，别显示成第一档。
  const timeoutNow = String(values.ai.timeoutSeconds);
  const timeoutChoices = TIMEOUT_CHOICES.includes(timeoutNow)
    ? TIMEOUT_CHOICES
    : [...TIMEOUT_CHOICES, timeoutNow].sort((a, b) => Number(a) - Number(b));

  return (
    <>
      {/* 这一段的端点、密钥与模型只在页内状态里：桥里没有写回厂商/模型的方法（ai.providers.save），
          所以不画「已保存」，也不让用户以为配完就能说话。 */}
      <SettingCard title="AI 对话">
        <p className="cap" style={{ margin: '0 0 var(--w-space-2)', color: 'var(--w-signal)', lineHeight: 1.6 }}>
          这一段的地址、密钥与模型只在页面上：宿主还没有写回厂商与模型的方法（ai.providers.save），填了不会落盘，对话也不会用上。
          要真的接上，请在桌面端的同名页面里配。
        </p>
        <PickRow title="API 预设" value={preset} options={AI_PRESETS} onChange={setPreset} width={240} label="API 预设" />
        <FieldRow
          title="API 地址"
          value={aiUrl}
          onChange={setAiUrl}
          placeholder="https://api.openai.com/v1/chat/completions"
          width={380}
        />
        <FieldRow title="API 密钥" value={aiKey} onChange={setAiKey} placeholder="sk-…" type="password" width={320} />
        <SettingRow
          title="模型"
          control={
            <span style={{ display: 'flex', gap: 'var(--w-space-2)', alignItems: 'center' }}>
              <Select value={aiModel} options={[{ value: aiModel, label: aiModel }]} onChange={setAiModel} label="模型" />
              <Inert>
                <Btn>检测</Btn>
              </Inert>
            </span>
          }
        />
        <SettingRow title="温度" control={<GlassSlider label="温度" value={temperature} onChange={setTemperature} />} />
        <SettingRow
          title="最大 Token"
          control={
            <RangeSlider label="最大 Token" min={100} max={2000} step={100} value={maxTokens} onChange={setMaxTokens} />
          }
        />
        <SettingRow
          title="深度思考"
          desc="复杂剧情时使用更强的模型"
          control={<Sw label="深度思考" checked={deepThink} onChange={setDeepThink} />}
        />
        <FieldRow title="深度模型" value={deepModel} onChange={setDeepModel} placeholder="o3 / 推理模型" width={320} />

        <Divider />
        <span className="s-row__title">小模型优先（可选）</span>
        <p className="s-row__desc">
          开启后所有对话先交给小模型，处理不来再升级到大模型。小模型要动游戏却又没有工具时，会按提示直接说「我需要使用大模型」，由大模型接手——所以女仆桥连着时也照样能用，不会把工具交互弄坏。
        </p>
        <SettingRow
          title="开启小模型优先"
          control={<Sw label="开启小模型优先" checked={smallFirst} onChange={setSmallFirst} />}
        />
        <FieldRow
          title="小模型接口"
          value={smallUrl}
          onChange={setSmallUrl}
          placeholder="留空则沿用上面的 AI 地址"
          width={340}
        />
        <Sep />
        <FieldRow
          title="小模型密钥"
          value={smallKey}
          onChange={setSmallKey}
          placeholder="留空则沿用上面的 AI 密钥"
          type="password"
          width={340}
        />
        <Sep />
        <FieldRow
          title="小模型名"
          value={smallModel}
          onChange={setSmallModel}
          placeholder="一个便宜/快的模型名，如 qwen-flash / glm-4-flash"
          width={340}
        />

        <Divider />
        <span className="s-row__title">AI 自主能力（实验）</span>
        <p className="s-row__desc">
          让 AI 在对话中自行决定思考深度、或发起多步复合任务。思考深度归「思考由谁决定」统管：跟随 AI 时由那次模型调用自己给、需要时 AI 可临时提升档位；设成「总是开 / 总是关」则档位由你强制。多步流程默认关闭，开启后 AI 可自主编排「探测→采集→建造→验证」多步流程，多步上限用于防止失控。
        </p>
        <SettingRow
          title="AI 自主发起多步计划"
          control={<Sw label="AI 自主发起多步计划" checked={planFlow} onChange={setPlanFlow} />}
        />
        <SettingRow
          title="多步上限"
          control={
            <RangeSlider label="多步上限" min={1} max={32} step={1} value={planMaxSteps} onChange={setPlanMaxSteps} />
          }
        />

        <Divider />
        <span className="s-row__title">设置调节 AI（F08）</span>
        <p className="s-row__desc">
          用一段与角色人格、记忆完全隔离的临时 AI 调用，分析当前设置并给优化建议；不写入任何角色记忆。默认「永不」打扰，开启后按你设定的频率运行（每天 / 每三天 / 每周）。
        </p>
        <SettingRow
          title="启用设置建议 AI"
          control={<Sw label="启用设置建议 AI" checked={advisor} onChange={setAdvisor} />}
        />
        <PickRow
          title="运行频率"
          value={advisorFreq}
          options={['never', 'daily', '3days', 'weekly']}
          onChange={setAdvisorFreq}
          width={190}
        />
        <BtnRow>
          <Inert>
            <Btn>立即分析</Btn>
          </Inert>
        </BtnRow>

        <Divider />
        <span className="s-row__title">识图模型（可选）</span>
        <p className="s-row__desc">
          独立配置一个支持看图的模型；配好后「表情包库」里可以一键让 AI 写描述。不填也能用：会优先找厂商里标注了「能看图」的模型，都没有就直接用大模型兜底。
        </p>
        <FieldRow
          title="识图端点"
          value={visionUrl}
          onChange={setVisionUrl}
          placeholder="https://…/v1/chat/completions"
          width={340}
        />
        <FieldRow title="识图 Key" value={visionKey} onChange={setVisionKey} placeholder="sk-…" type="password" width={320} />
        <FieldRow
          title="识图模型"
          value={visionModel}
          onChange={setVisionModel}
          placeholder="gpt-4o-mini / qwen-vl-plus / glm-4v …"
          width={320}
        />

        <Divider />
        <SettingRow
          title="量化模型（embedding）"
          desc="专门的知识库管理员：把词语和句子换成数值，近似的说法数值就近，「谁和它最像」就能直接算出来。关系网的近邻、按数值补线和按数值搜记忆都优先问它；没配置或额度用完就退回本地共现统计（不花钱，界面会说是哪条路算的）。"
          control={<Sw label="量化模型" checked={embedding} onChange={setEmbedding} />}
        />
        <FieldRow
          title="量化端点"
          value={embeddingUrl}
          onChange={setEmbeddingUrl}
          placeholder="留空则复用上面的对话端点（自动换成 /embeddings）"
          width={380}
        />
        <FieldRow
          title="量化 Key"
          value={embeddingKey}
          onChange={setEmbeddingKey}
          placeholder="sk-…（留空则复用对话密钥与厂商密钥池）"
          type="password"
          width={380}
        />
        <FieldRow
          title="量化模型"
          value={embeddingModel}
          onChange={setEmbeddingModel}
          placeholder="text-embedding-3-small / BAAI/bge-m3 …（空 = 只用本地统计）"
          width={420}
        />
        <PickRow
          title="向量维度"
          value={embeddingDims}
          options={['0', '256', '512', '768', '1024', '1536', '2048']}
          onChange={setEmbeddingDims}
          width={150}
        />
        <p className="cap">
          0 = 由模型自己定。换模型或换维度等于换一套向量，算过的会重新算一次（同模型同文本只算一次，之后不重复花钱）。
        </p>

        <Divider />
        <SettingRow
          title="多端点轮换（供应商 / AI / 密钥）"
          desc="保存多条端点（一条 = 供应商 + 某款 AI + 多枚密钥）。开启后密钥与端点自动轮流使用；某条失灵就换下一条，保证对话不间断。"
          control={<Sw label="多端点轮换" checked={rotation} onChange={setRotation} />}
        />
        <PickRow
          title="请求超时（秒）"
          desc="全局默认；某条端点需要更久时，在下面那条端点的「超时」里单独填"
          value={String(values.ai.timeoutSeconds)}
          options={timeoutChoices}
          onChange={(next) => write('ai.timeoutSeconds', Number(next))}
          width={90}
        />
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 'var(--w-space-2)' }}>
          <Btn onClick={() => navigate('/aiProviders')}>厂商管理页面 →</Btn>
          <Btn onClick={() => navigate('/aiModels')}>AI 模型管理页面 →</Btn>
          <Btn onClick={() => navigate('/modelDeployment')}>模型部署向导 →</Btn>
        </div>
        <ProviderList bridge={bridge} />

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--w-space-2)' }}>
          <Field value={newProviderName} onChange={setNewProviderName} placeholder="厂商名（如：硅基流动）" label="厂商名" />
          <Field
            value={newProviderModel}
            onChange={setNewProviderModel}
            placeholder="首款模型（如：deepseek-chat）"
            label="首款模型"
          />
        </div>
        <Field
          value={newProviderUrl}
          onChange={setNewProviderUrl}
          placeholder="端点（https://…/v1/chat/completions）"
          label="端点"
        />
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 100px', gap: 'var(--w-space-2)' }}>
          <Field value={newProviderKeys} onChange={setNewProviderKeys} placeholder="密钥（一行一枚，多枚轮流用）" label="密钥" />
          <Field value={newProviderTimeout} onChange={setNewProviderTimeout} placeholder="超时秒" label="超时秒" />
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--w-space-2)' }}>
          <Inert>
            <Btn>＋添加厂商</Btn>
          </Inert>
          <Inert>
            <Btn>用当前配置预填</Btn>
          </Inert>
        </div>

        <Divider />
        <span className="s-row__title">花费与省钱</span>
        <SettingRow
          title="智能省钱"
          desc="开启后折叠历史前先预测「现在总结 / 不总结」哪个更省，并识别编程办公场景改用轻总结；关闭则恢复「到上限就总结」。"
          control={
            <Sw label="智能省钱" checked={values.ai.smartSaving} onChange={(value) => write('ai.smartSaving', value)} />
          }
        />
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr auto', gap: 'var(--w-space-1)' }}>
          <Field value={priceModel} onChange={setPriceModel} placeholder="模型名" label="模型名" />
          <Field value={priceIn} onChange={setPriceIn} placeholder="输入价/百万" label="输入价" />
          <Field value={priceOut} onChange={setPriceOut} placeholder="输出价/百万" label="输出价" />
          <Inert>
            <Btn>保存单价</Btn>
          </Inert>
        </div>
        <p className="cap">单价可以只填一侧（另一侧按 0 计），不必每个模型都填。</p>
        <BtnRow>
          <Inert>
            <Btn>刷新用量 / 花费</Btn>
          </Inert>
          <Inert>
            <Btn>按当前上限试算</Btn>
          </Inert>
          <Btn onClick={() => navigate('/usagestats')}>用量统计…</Btn>
        </BtnRow>
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            minHeight: 190,
            padding: 6,
            borderRadius: 10,
            background: 'var(--w-surface-tint)',
            border: '1px solid var(--w-hairline)',
          }}
        >
          <p className="cap">还没有用量记录（端点返回 usage 后自动记账）</p>
        </div>
        <BtnRow>
          <Btn onClick={() => setChartMode(chartMode === '条形图' ? '折线图' : '条形图')}>{chartMode}</Btn>
          <Inert>
            <Btn>导出 CSV</Btn>
          </Inert>
        </BtnRow>
        <span className="s-row__title" style={{ fontWeight: 700 }}>
          价钱表（每百万 token）
        </span>
        <p className="cap">还没有录入单价。按「模型 输入价/百万 输出价/百万」录入后，这里会出现价钱表。</p>
        <span className="s-row__title" style={{ fontWeight: 700, marginTop: 6 }}>
          token 表
        </span>
        <p className="cap">还没有用量记录——端点返回 usage 后会自动记账。</p>
        <span className="s-row__title" style={{ fontWeight: 700, marginTop: 6 }}>
          花钱排行榜
        </span>
        <p className="cap">还没录单价，排不出花钱榜。</p>
        <span className="s-row__title" style={{ fontWeight: 700, marginTop: 6 }}>
          token 排行榜
        </span>
        <p className="cap">还没有用量记录。</p>
      </SettingCard>

      <SettingCard title="AI 绘画（装扮 / 背景 / 立绘）">
        <p className="cap" style={{ margin: '0 0 var(--w-space-2)', color: 'var(--w-signal)', lineHeight: 1.6 }}>
          这一张卡只在页内状态里：宿主还没有写回画面生成设置的方法（imagegen.save），地址与密钥填了不会落盘。
        </p>
        <SettingRow
          title="启用图片生成"
          desc="用 OpenAI 兼容的 /images/generations 接口生成装扮、场景背景与角色立绘；生成结果落在本地 generated 目录，可再导入素材库。关闭时不花钱。"
          control={<Sw label="启用图片生成" checked={imageGen} onChange={setImageGen} />}
        />
        <Sep />
        <FieldRow title="接口地址" value={imageUrl} onChange={setImageUrl} placeholder="https://api.openai.com/v1" width={340} />
        <Sep />
        <FieldRow
          title="密钥"
          value={imageKey}
          onChange={setImageKey}
          placeholder="只存本地设置，不进云存档"
          type="password"
          width={340}
        />
        <Sep />
        <FieldRow title="模型" value={imageModel} onChange={setImageModel} placeholder="gpt-image-1 / flux / sd…" width={320} />
        <Sep />
        <FieldRow title="尺寸" value={imageSize} onChange={setImageSize} placeholder="1024x1024" width={200} />
      </SettingCard>

      <SettingCard title="自定义 AI 参数（可选）">
        <p className="s-row__desc">
          以 JSON 对象填写额外请求参数（如 reasoning_effort、top_k），会按所选格式并入发给模型的请求体。留空则不附加；结构错误会在日志里跳过，不影响对话。
        </p>
        <PickRow
          title="参数格式"
          value={paramsFormat}
          options={['openai_flat', 'nested_by_role', 'vendor_ext', 'widget_binding']}
          onChange={setParamsFormat}
          width={220}
        />
        <SettingBlock>
          <span className="s-row__title">参数 JSON</span>
          <Field value={paramsJson} onChange={setParamsJson} placeholder="留空表示不附加任何参数" multiline label="参数 JSON" />
        </SettingBlock>
      </SettingCard>

      <SettingCard title="思考档位（思考条 + 快速按钮）">
        <p className="s-row__desc">
          为模型自配若干档推理强度，对话中输入条上方会出现切换按钮、并在思考时显示进度条。第 0 档永远为「关闭」。当前档位可在对话里即时切换并自动保存。
        </p>
        <PickRow
          title="思考由谁决定"
          value={THINK_MODE_LABEL[values.ai.think.mode]}
          options={THINK_MODE_CHOICES}
          onChange={(next) => write('ai.thinkMode', THINK_MODE_KEY[next] ?? 'auto')}
          width={130}
        />
        <p className="s-row__desc">
          跟随 AI：不摆手动开关，这一轮要不要深入想由那次模型调用自己给，需要时 AI 可自行抬档。总是开：按当前档位强制注入，AI 不能改低。总是关：一律不注入，AI 也不能抬档。
        </p>
        <SettingRow
          title="显示思考条（正在思考 / 深度思考中）"
          control={<Sw label="显示思考条" checked={values.ai.think.bar} onChange={(value) => write('ai.thinkBar', value)} />}
        />
        <SettingRow
          title="显示快速按钮（切换思考档位）"
          control={<Sw label="显示快速按钮" checked={values.ai.think.quick} onChange={(value) => write('ai.quickButtons', value)} />}
        />
        <SettingRow
          title="思考档位数（1~5）"
          control={
            <span style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
              <Btn onClick={() => write('ai.thinkTierCount', Math.max(1, values.ai.think.tiers - 1))}>−</Btn>
              <span className="s-row__title">{values.ai.think.tiers}</span>
              <Btn onClick={() => write('ai.thinkTierCount', Math.min(5, values.ai.think.tiers + 1))}>＋</Btn>
            </span>
          }
        />
        <FieldRow title="每档标签" value={values.ai.think.labels} onChange={(next) => write('ai.thinkTierLabels', next)} placeholder="逗号分隔，如 轻度,标准,深度" width={340} />
        <FieldRow title="推理强度参数键名" value={values.ai.think.paramKey} onChange={(next) => write('ai.thinkParamKey', next)} placeholder="如 reasoning_effort" width={260} />
        <FieldRow title="每档参数值" value={values.ai.think.paramValues} onChange={(next) => write('ai.thinkParamValues', next)} placeholder="逗号分隔，如 low,medium,high" width={300} />
        <p className="cap">
          提示：参数键名与每档值需与你所选模型的推理强度接口对应（例如 OpenAI 推理模型用 reasoning_effort=low/medium/high）。不匹配时该模型会忽略或报错，属正常配置差异。
        </p>
      </SettingCard>
    </>
  );
}

/* ── 记忆与对话：Card10 + Card30 ────────────────────── */

/** MemoryWindowChoices：0 = 不限。 */
const MEMORY_WINDOW_DAYS = [0.25, 0.5, 1, 3, 7, 14, 30, 90, 0];

/** 档位不在上面那张表里时，按 SettingsViewModel.WindowText 的写法念出来。 */
function windowText(days: number): string {
  if (days <= 0) return '不限';
  if (days < 1) return `${Math.round(days * 240) / 10} 小时`;
  if (days < 30) return `${Math.round(days * 10) / 10} 天`;
  return `${Math.round((days / 30) * 10) / 10} 个月`;
}

function MemorySection({ writer }: { writer: SettingsWriter }) {
  const { values, set: write } = writer;
  const [turns, setTurns] = useStr('5');
  const [contextLimit, setContextLimit] = useStr('32K');
  const [randomness, setRandomness] = useNum(0.3);
  const [subProfile, setSubProfile] = useBool(false);
  const [semantic, setSemantic] = useBool(false);
  const [crossSave, setCrossSave] = useBool(false);
  const [coreMemory, setCoreMemory] = useBool(true);
  const [threshold, setThreshold] = useNum(0.82);
  const [trauma, setTrauma] = useBool(true);
  const [chainLimit, setChainLimit] = useNum(5);

  const MEMORY_WINDOWS = ['6 小时', '12 小时', '1 天', '3 天', '1 周', '2 周', '1 个月', '3 个月', '不限'];
  // MemoryWindowChoices（天）：0 = 不限；档位名与数字一一对应（SettingsViewModel.SyncWindowDisplay）。
  const windowDays = values.memory.windowDays;
  const windowIndex = MEMORY_WINDOW_DAYS.indexOf(windowDays);
  const windowLabel = windowIndex >= 0 ? MEMORY_WINDOWS[windowIndex] : windowText(windowDays);

  return (
    <>
      <SettingCard title="记忆与对话">
        <PickRow
          title="记忆上下文轮数"
          desc="AI 每次回复可回忆起的历史消息条数"
          value={turns}
          options={['3', '5', '8', '10', '12', '15', '20']}
          onChange={setTurns}
          width={110}
        />
        <Sep />
        <PickRow
          title="上下文上限"
          value={contextLimit}
          options={['4K', '8K', '16K', '32K', '64K', '128K', '256K']}
          onChange={setContextLimit}
          width={180}
          label="超过后强制总结"
        />
        <p className="cap">对话历史估算超过该 Token 数时，会像人回忆一样把旧对话总结成第一人称记忆；平时不动历史。</p>
        <PickRow
          title="遗忘指数"
          desc="值越低忘得越快。默认只以三天内的事为主，越久越模糊，但记得很牢的事不会归零；选「不限」能想起最早的记忆。"
          value={windowLabel}
          options={MEMORY_WINDOWS}
          onChange={(next) => write('memory.windowDays', MEMORY_WINDOW_DAYS[MEMORY_WINDOWS.indexOf(next)])}
          width={110}
          label="遗忘快慢"
        />
        <SettingRow
          title="回忆的随机度"
          desc="越大越像人：会跳着想、偶尔想不起来。0 = 严格按时间与重要性排序。"
          control={<GlassSlider label="回忆的随机度" width={140} value={randomness} onChange={setRandomness} />}
        />
        <p className="cap">需要个别角色记性好/记性差时，在角色库 →「参数覆写」里单独设置这两个值。</p>
        <SettingRow
          title="让模型参与副设定改写"
          desc="开启后每 12 轮对话，让模型重新表述一次「TA 现在的样子」（性格与处境）；主设定不动、随时可回正。默认关闭以免额外花钱。"
          control={<Sw label="让模型参与副设定改写" checked={subProfile} onChange={setSubProfile} />}
        />
        <Sep />
        <SettingRow
          title="让模型辅助建立关系网"
          desc="关联网默认只靠共现统计自动长（不花钱）；打开后每 20 轮让模型补 3 条关联，标为 AI 来源，人工纠错仍优先。"
          control={<Sw label="让模型辅助建立关系网" checked={semantic} onChange={setSemantic} />}
        />
        <Sep />
        <SettingRow
          title="跨存档共用记忆"
          desc="默认关闭：每个存档各记各的，换一个档，TA 不记得别处发生过什么。打开后所有存档读写同一份记忆与关系网；想让两个人分开，就去角色库「复制角色 · 连记忆一起复制」分一份出来。"
          control={<Sw label="跨存档共用记忆" checked={crossSave} onChange={setCrossSave} />}
        />
      </SettingCard>

      <SettingCard title="记忆的深处">
        <SettingRow
          title="核心记忆"
          desc="情绪强度与重要性都过线的事会成为核心记忆：永不遗忘、完整豁免重复机制、始终可回想，并像锚一样影响周围的记忆。阈值越高越难成为核心。"
          control={<Sw label="核心记忆" checked={coreMemory} onChange={setCoreMemory} />}
        />
        <SettingRow
          title="成为核心的阈值"
          control={
            <RangeSlider
              label="成为核心的阈值"
              min={0.5}
              max={0.99}
              step={0.01}
              value={threshold}
              onChange={setThreshold}
              width={140}
              disabled={!coreMemory}
            />
          }
        />
        <p className="cap">当前阈值：{threshold.toFixed(2)}</p>
        <Sep />
        <SettingRow
          title="创伤可以被治愈"
          desc="创伤不会被删除，只能被治愈：靠具体的安慰与陪伴慢慢降低情绪强度，时间几乎不起作用。会从一碰就痛，走到能平静地叹口气，最后融进性格与行为里。"
          control={<Sw label="创伤可以被治愈" checked={trauma} onChange={setTrauma} />}
        />
        <Sep />
        <SettingRow
          title="链式搜索条数"
          desc="从一条线索顺着共用词、邻近时间、同一个字与相同特质往下找。默认 5 条；同时间内越相似越靠前，新事优先。"
          control={
            <RangeSlider
              label="链式搜索条数"
              min={1}
              max={20}
              step={1}
              value={chainLimit}
              onChange={setChainLimit}
              width={140}
            />
          }
        />
        <p className="cap">每次最多找 {chainLimit.toFixed(0)} 条</p>
      </SettingCard>
    </>
  );
}

/* ── 身份与心理：Card28 + Card29 + Card34 ─────────────── */

function PersonaSection({ navigate }: { navigate: ScreenProps['navigate'] }) {
  const [narrator, setNarrator] = useBool(false);
  const [gender, setGender] = useStr('未设置');
  const [birthday, setBirthday] = useStr('');
  const [newGenderName, setNewGenderName] = useStr('');
  const [newGenderPronoun, setNewGenderPronoun] = useStr('TA');
  const [newGenderDesc, setNewGenderDesc] = useStr('');
  const [scriptLang, setScriptLang] = useStr('js');
  const [script, setScript] = useStr('');

  const [mental, setMental] = useBool(true);
  const [psychBehavior, setPsychBehavior] = useBool(true);
  const [physiology, setPhysiology] = useBool(true);
  const [innerLife, setInnerLife] = useBool(true);
  const [concretization, setConcretization] = useBool(true);
  const [innerDrive, setInnerDrive] = useBool(true);
  const [toolCalls, setToolCalls] = useBool(false);
  const [chatFull, setChatFull] = useBool(false);

  const [statAi, setStatAi] = useBool(true);
  const [brain, setBrain] = useBool(true);
  const [expectation, setExpectation] = useBool(true);
  const [surprise, setSurprise] = useBool(true);
  const [brainAi, setBrainAi] = useBool(false);
  const [brainUrl, setBrainUrl] = useStr('');
  const [brainKey, setBrainKey] = useStr('');
  const [brainModel, setBrainModel] = useStr('');
  const [brainTurns, setBrainTurns] = useNum(8);

  return (
    <>
      <SettingCard title="身份与旁白">
        <SettingRow
          title="旁白 AI"
          desc="开启后，回复里允许出现简短的第三人称环境/动作描写（「她把杯子放回桌上」）。不替你做决定，也不描写你的心理。"
          control={<Sw label="旁白 AI" checked={narrator} onChange={setNarrator} />}
        />
        <Sep />
        <PickRow
          title="你的性别"
          desc="只影响称呼与相处方式，不写死刻板印象；留空 = 不指定。角色自己的性别在角色库里设。"
          value={gender}
          options={['未设置', '女', '男', '其他']}
          onChange={setGender}
          width={140}
        />
        <Sep />
        <SettingRow
          title="你的生日"
          desc="留空 = 不设置。填了之后 AI 会记得这一天；只存月日，不需要年份。"
          control={
            <span style={{ display: 'inline-block', width: 140 }}>
              <Field value={birthday} onChange={setBirthday} placeholder="MM-dd，如 08-02" label="你的生日" />
            </span>
          }
        />
        <SubHead>自定义性别</SubHead>
        <p className="s-row__desc">
          名字 + 称呼 + 一段说明即可；还可以挂一段脚本（JS/Python/Java），让性别决定周期、称呼或随经历变化的描述。脚本输入 JSON、输出文字，超时与语句数都有硬上限。
        </p>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 110px', gap: 'var(--w-space-2)' }}>
          <Field value={newGenderName} onChange={setNewGenderName} placeholder="名字（如：扶她 / 无性别 / 流性）" label="名字" />
          <Field value={newGenderPronoun} onChange={setNewGenderPronoun} placeholder="自称（TA）" label="自称" />
        </div>
        <Field value={newGenderDesc} onChange={setNewGenderDesc} placeholder="一句话说明（会进提示词）" label="说明" />
        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
          <span className="s-row__title">脚本语言</span>
          <Select value={scriptLang} options={opts(['js', 'python', 'java'])} onChange={setScriptLang} label="脚本语言" />
        </div>
        <Field
          value={script}
          onChange={setScript}
          placeholder={'可选：脚本代码（输入 JSON：{"name":"..."}，返回一段文字）'}
          multiline
          label="脚本代码"
        />
        <div style={{ display: 'grid', gridTemplateColumns: '1fr auto', gap: 'var(--w-space-2)' }}>
          <Inert>
            <SignalButton>保存自定义性别</SignalButton>
          </Inert>
          <Inert>
            <Btn>从 JSON 导入</Btn>
          </Inert>
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr auto', gap: 'var(--w-space-2)' }}>
          <Select
            value="还没有自定义性别"
            options={[{ value: '还没有自定义性别', label: '还没有自定义性别' }]}
            onChange={() => undefined}
            label="选择要删除的自定义性别"
          />
          <Inert>
            <Btn>删除</Btn>
          </Inert>
        </div>
      </SettingCard>

      <SettingCard title="心理与身体">
        <SettingRow
          title="心理状况系统"
          desc="可为角色设置轻/中/重度心理状况（内置 115 条：情绪、焦虑、创伤、强迫、进食、睡眠、精神病性、人格、神经发育、神经、躯体、冲动与特质）。只把当前角色生效的条目写进提示词，不贴标签。"
          control={<Sw label="心理状况系统" checked={mental} onChange={setMental} />}
        />
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 'var(--w-space-2)',
            padding: '4px 0',
          }}
        >
          <span className="s-row__desc">
            打开后可逐角色设定；涉及自伤/自杀/伤人的条目有硬边界：不提供方法、不美化，只做陪伴与转向求助。
          </span>
          <Btn onClick={() => navigate('/mentalHealth')}>管理心理状况</Btn>
        </div>
        <Sep />
        <SettingRow
          title="心理驱动的行为"
          desc="允许角色因心理状态做出回避、依赖、易怒等反应。安全条目永远优先：不会生成自伤/伤人的具体做法，也不会鼓励。"
          control={<Sw label="心理驱动的行为" checked={psychBehavior} onChange={setPsychBehavior} />}
        />
        <Sep />
        <SettingRow
          title="身体感受（冷暖 / 饥渴 / 想念）"
          desc="按天气与时间缓慢漂移，聊天与亲密互动会改变它；角色会说「有点冷」「想你了」，而不是报数值。"
          control={<Sw label="身体感受" checked={physiology} onChange={setPhysiology} />}
        />
        <Sep />
        <SettingRow
          title="内心生活（内分泌 / 执念）"
          desc="她的心绪有自己的来处：游戏里被打、被喂食，聊天里的冷暖话，都会让激素涨落并渗进语气；她说「不知道」的事会长成执念，惦记到一定程度会自己想明白、主动来讲。"
          control={<Sw label="内心生活" checked={innerLife} onChange={setInnerLife} />}
        />
        <Sep />
        <SettingRow
          title="情绪具体化（身体 / 行为）"
          desc="情绪会落到身体与行为上：影响食欲、想不想说话、想不想走动；长期压抑或高唤起还可能躯体化——肚子疼、胃灼烧、呼吸急促。判定全部走纯代码，只在对话里自然渗进语气与动作。"
          control={<Sw label="情绪具体化" checked={concretization} onChange={setConcretization} />}
        />
        <Sep />
        <SettingRow
          title="内驱动系统（基本动力 / 七个驱动 / 自律）"
          desc="给她一套自己的动机：攻击性、性、自恋三种基本动力决定她多容易被推动；贪欲、暴食、色欲、嫉妒、傲慢、怠惰、暴怒七个驱动决定她此刻想做什么，做成之后化成干劲与松快。自律是闸门，方向由角色的道德观、人生观、思维主义决定，也会被她自己的克制与放纵练硬或磨软。"
          control={<Sw label="内驱动系统" checked={innerDrive} onChange={setInnerDrive} />}
        />
        <p className="s-row__desc">
          生气在这一层被拆成八种出口，从强到弱：叛逆、撒泼、怄气、任性、郁闷、拖延、厌烦、退缩。走哪个出口由火气、攻击性与自律共同决定，说完会松一口，忍着则留下闷并把自律练硬一点。
        </p>
        <Sep />
        <SettingRow
          title="显示工具调用"
          desc="勾选后，对话里会出现一条可展开的「工具调用」气泡，能看到每次调用的名字、参数和返回结果；不勾就完全不显示。"
          control={<Sw label="显示工具调用" checked={toolCalls} onChange={setToolCalls} />}
        />
        <Sep />
        <SettingRow
          title="对话信息全屏"
          desc="把聊天内容铺满整屏，隐藏联系人栏；聊天页顶部有「返回」按钮随时退回。切页前会记住这次的选择。"
          control={<Sw label="对话信息全屏" checked={chatFull} onChange={setChatFull} />}
        />
      </SettingCard>

      <SettingCard title="数值结算">
        <SettingRow
          title="数值由 AI 控制"
          desc="好感、信任、精力的增减交给 AI 判（回复里内联指令为主，没给就兜底问一次，判不动就不动）。关掉则回到旧的固定增量：说一句 +1、动作 +2/+3/+5、精力纯按清醒时长。"
          control={<Sw label="数值由 AI 控制" checked={statAi} onChange={setStatAi} />}
        />
        <Sep />
        <SubHead>大脑预判</SubHead>
        <SettingRow
          title="开启大脑预判"
          desc="TA 会下意识预估你这次会有多热：超预期就受用、好感涨得快些，低于预期就把热情收着。关掉后一切照旧（好感恒定 +1）。"
          control={<Sw label="开启大脑预判" checked={brain} onChange={setBrain} />}
        />
        <SettingRow
          title="期待差（多巴胺）"
          desc="按预测误差浮动好感涨速（约 0.2~1.8 倍）。"
          control={<Sw label="期待差" checked={expectation} onChange={setExpectation} disabled={!brain} />}
        />
        <Sep />
        <SettingRow
          title="意料之外（惊喜）"
          desc="偶尔不完全顺着最顺的走向：停半拍、换个角度、提一件好奇的小事。只微微制造一点预测不到的心动，情绪低落时自动停用。"
          control={<Sw label="意料之外" checked={surprise} onChange={setSurprise} disabled={!brain} />}
        />
        <SubHead>免费小模型辅助预判（可选）</SubHead>
        <p className="s-row__desc">
          默认完全本地跑、不花钱。想更准可指定一枚便宜/免费的模型，每 N 轮补一次「预判下一条多热」，结果缓存进存档；不开就用本地启发式。
        </p>
        <SettingRow
          title="用小模型补预判"
          control={<Sw label="用小模型补预判" checked={brainAi} onChange={setBrainAi} disabled={!brain} />}
        />
        <FieldRow title="接口地址" value={brainUrl} onChange={setBrainUrl} placeholder="留空则沿用上面的 AI 地址" width={340} />
        <Sep />
        <FieldRow
          title="密钥"
          value={brainKey}
          onChange={setBrainKey}
          placeholder="留空则沿用上面的 AI 密钥（只存本地，不进云存档）"
          type="password"
          width={340}
        />
        <Sep />
        <FieldRow title="模型" value={brainModel} onChange={setBrainModel} placeholder="一个便宜/免费的模型名" width={300} />
        <SettingRow
          title={`每 ${brainTurns} 轮补一次`}
          control={
            <RangeSlider
              label="每 N 轮补一次"
              min={2}
              max={30}
              step={1}
              value={brainTurns}
              onChange={setBrainTurns}
              width={140}
              disabled={!brainAi}
            />
          }
        />
      </SettingCard>
    </>
  );
}

/* ── 界面与玩法：Card05 + Card40 + Card06 + Card07 + Card32 + Card36 ── */

function InterfaceSection({ writer }: { writer: SettingsWriter }) {
  const { values, set: write } = writer;
  const [palette] = useStr('终末地');
  const [menuRight, setMenuRight] = useBool(false);
  // 后端只认简体/English；另外两门语言先在本地停住，不拿它去写。
  const [langLocal, setLangLocal] = useStr('');
  const [allowSilence, setAllowSilence] = useBool(false);

  const [tileOnline, setTileOnline] = useBool(true);

  const [autoDifficulty, setAutoDifficulty] = useBool(false);

  const langValue = langLocal || (values.language.locale === 'en-US' ? 'English' : '简体中文');
  const chatStyleValue = values.ui.chatStyle === 'bubble' ? '气泡式' : '剧本式';
  const orientationValue =
    values.ui.orientation === 'landscape' ? '横屏' : values.ui.orientation === 'portrait' ? '竖屏' : '自动（竖屏）';
  const difficultyValue =
    values.ui.difficulty === 'easy' ? '简单（新手练手）' : values.ui.difficulty === 'hard' ? '困难（步步紧逼）' : '普通（默认）';

  const [chess, setChess] = useBool(false);
  const [chessUrl, setChessUrl] = useStr('');
  const [chessKey, setChessKey] = useStr('');
  const [chessModel, setChessModel] = useStr('gpt-4o-mini');

  const [money, setMoney] = useBool(true);
  const [moneyInput, setMoneyInput] = useStr('100');

  const [widgets, setWidgets] = useBool(false);
  const [graphSkip, setGraphSkip] = useBool(false);
  const [skipNodes, setSkipNodes] = useNum(240);
  const [saveOobe, setSaveOobe] = useBool(true);
  const [outfitNew, setOutfitNew] = useBool(true);
  const [maskNames, setMaskNames] = useBool(false);
  const [libido, setLibido] = useBool(true);
  const [libidoCycle, setLibidoCycle] = useNum(0.5);

  return (
    <>
      <SettingCard title="界面">
        <SettingRow
          title="语义调色板"
          desc="覆盖按钮、文字、面板、背景的关键色（只保留终末地；地面色在「纯色底」里选）"
          control={
            <Select value={palette} options={[{ value: palette, label: palette }]} onChange={() => undefined} label="语义调色板" />
          }
        />
        <Sep />
        <SettingRow
          title="主题居左，按钮居右"
          desc="电脑端主菜单按钮与主题装饰列互换位置（默认按钮居左）"
          control={<Sw label="主题居左，按钮居右" checked={menuRight} onChange={setMenuRight} />}
        />
        <Sep />
        <PickRow
          title="语言"
          desc="界面显示语言"
          value={langValue}
          options={['简体中文', '繁體中文', 'English', '日本語']}
          onChange={(next) => {
            if (next === '简体中文' || next === 'English') {
              setLangLocal('');
              write('language.locale', next === 'English' ? 'en-US' : 'zh-CN');
            } else {
              setLangLocal(next);
            }
          }}
          width={120}
        />
        <Sep />
        <SettingRow
          title="自动保存"
          desc="开启后设置变更即时保存"
          control={
            <Sw label="自动保存" checked={values.story.autoSave} onChange={(value) => write('story.autoSave', value)} />
          }
        />
        <Sep />
        <PickRow
          title="多角色聊天显示"
          desc="气泡式：消息直接成对话气泡流（适合多角色同场）；剧本式：底部对话面板 + 立绘"
          value={chatStyleValue}
          options={['剧本式', '气泡式']}
          onChange={(next) => write('ui.chatStyle', next === '气泡式' ? 'bubble' : 'galgame')}
          width={110}
        />
        <Sep />
        <SettingRow
          title="允许 AI 沉默"
          desc="开启后角色可以选择不回复（什么都不输出），系统会自动停止该回合，而不是每次提问都强行给出答复"
          control={<Sw label="允许 AI 沉默" checked={allowSilence} onChange={setAllowSilence} />}
        />
        <Sep />
        <PickRow
          title="屏幕方向"
          desc="移动端专用：自动=默认竖屏，手机/平板都竖屏；也可手动锁横屏（电脑版布局）或竖屏。切换后 3 秒内未确认会自动变回来再让你确认一次"
          value={orientationValue}
          options={['自动（竖屏）', '横屏', '竖屏']}
          onChange={(next) =>
            write('ui.orientation', next === '横屏' ? 'landscape' : next === '竖屏' ? 'portrait' : 'auto')
          }
          width={130}
        />
      </SettingCard>

      <SettingCard title="地图底图与离线包">
        <p className="s-row__desc">
          地图页的底图：高德 / OSM 需要联网；离线包是一个 .mbtiles 文件，整个地区打包在本地，没网也能看。
        </p>
        <SettingRow
          title="允许联网取瓦片"
          desc="关掉后一个请求都不发，只读本地缓存与离线包。"
          control={<Sw label="允许联网取瓦片" checked={tileOnline} onChange={setTileOnline} />}
        />
        <Sep />
        <SettingBlock>
          <span className="s-row__title">MBTiles 离线包</span>
          <p className="cap">还没有选择离线包，地图只用联网瓦片与缓存。</p>
          <div>
            <Inert>
              <SignalButton>选择 .mbtiles 文件</SignalButton>
            </Inert>
          </div>
          <p className="cap">
            选中的文件会复制进应用目录，原文件可以删。包里没有的档位或区域会在画面上用父级瓦片顶着，不会一直空白。
          </p>
        </SettingBlock>
        <Sep />
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            gap: 'var(--w-space-3)',
            padding: '13px 0',
          }}
        >
          <div className="s-row__text">
            <span className="s-row__title">瓦片缓存</span>
            <span className="s-row__desc">还没有缓存，联网看过地图之后这里会显示占用量。</span>
          </div>
          <Inert>
            <Btn>清空瓦片缓存</Btn>
          </Inert>
        </div>
      </SettingCard>

      <SettingCard title="小游戏 AI 聪明度">
        <PickRow
          title="AI 难度"
          desc="简单=随机放水，普通=贪吃，困难=前瞻防反"
          value={difficultyValue}
          options={['简单（新手练手）', '普通（默认）', '困难（步步紧逼）']}
          onChange={(next) => write('ui.difficulty', next === '简单（新手练手）' ? 'easy' : next === '困难（步步紧逼）' ? 'hard' : 'normal')}
          width={130}
          disabled={autoDifficulty}
        />
        <Sep />
        <SettingRow
          title="AI 自动调节"
          desc="由 AI 按你的对局表现智能升降难度"
          control={<Sw label="AI 自动调节" checked={autoDifficulty} onChange={setAutoDifficulty} />}
        />
      </SettingCard>

      <SettingCard
        title="云端棋力脑（可选）"
        note="默认使用内置本地棋力，不依赖网络。填写 OpenAI 兼容下棋 API 后可让 AI 更强。"
      >
        <SettingRow
          title="启用云端棋力"
          control={<Sw label="启用云端棋力" checked={chess} onChange={setChess} />}
        />
        <Sep />
        <FieldRow title="API 地址" value={chessUrl} onChange={setChessUrl} placeholder="https://api.openai.com/v1/chat/completions" width={380} />
        <Sep />
        <FieldRow title="API Key" value={chessKey} onChange={setChessKey} placeholder="sk-…" type="password" width={300} />
        <Sep />
        <FieldRow title="模型" value={chessModel} onChange={setChessModel} placeholder="gpt-4o-mini" width={240} />
      </SettingCard>

      <SettingCard title="金钱系统">
        <SettingRow
          title="启用金钱"
          desc="关闭后商店与礼物不再扣钱，余额原样保留；再打开时继续用原来的数。开着时也可以在这里随意加钱或改余额。"
          control={<Sw label="启用金钱" checked={money} onChange={setMoney} />}
        />
        {money ? null : <p className="cap">金钱系统已关闭</p>}
        <div style={{ display: 'grid', gridTemplateColumns: '1fr auto auto', gap: 'var(--w-space-2)', padding: '10px 0' }}>
          <Field value={moneyInput} onChange={setMoneyInput} placeholder="金额（可负数）" type="number" label="金额" />
          <Inert>
            <SignalButton>增加</SignalButton>
          </Inert>
          <Inert>
            <Btn>设为余额</Btn>
          </Inert>
        </div>
      </SettingCard>

      <SettingCard title="界面组件">
        <SettingRow
          title="信息栏改用小组件"
          desc="主界面左栏与顶栏由可挑选的小组件组成：时间、天气、好感、身体状态、当前地点各占一格，可自己决定显示哪些、放在哪一栏。关掉则回到固定排版。"
          control={<Sw label="信息栏改用小组件" checked={widgets} onChange={setWidgets} />}
        />
        <Sep />
        <SettingRow
          title="关系网自动跳过展开动画"
          desc="节点多的时候，长枝条动画要播很久才有反应。超过阈值就直接出图。"
          control={<Sw label="关系网自动跳过展开动画" checked={graphSkip} onChange={setGraphSkip} />}
        />
        <SettingRow
          title={`节点超过 ${skipNodes.toFixed(0)} 个就跳过`}
          control={
            <RangeSlider
              label="节点阈值"
              min={40}
              max={1200}
              step={1}
              value={skipNodes}
              onChange={setSkipNodes}
              width={150}
              disabled={!graphSkip}
            />
          }
        />
        <Sep />
        <SettingRow
          title="建档分步引导"
          desc="开新档时一步一步问（世界 → 人设 → 细则 → 确认）。关掉则一屏填完。"
          control={<Sw label="建档分步引导" checked={saveOobe} onChange={setSaveOobe} />}
        />
      </SettingCard>

      <SettingCard title="着装系统">
        <SettingRow
          title="新版着装（形象包 + 服装轴）"
          desc="按部件拼立绘、换配色与服装，可让 AI 生成素材并录制动画。关掉则回到旧版：一张张静态立绘按表情切换。"
          control={<Sw label="新版着装" checked={outfitNew} onChange={setOutfitNew} />}
        />
      </SettingCard>

      <SettingCard title="角色与内容">
        <SettingRow
          title="允许 AI 随意加的角色先隐去名字"
          desc="对放开角色白名单的存档：新角色登场时名字显示为「？？？」，等 TA 自我介绍或被介绍之后才显示真名。关掉则直接显示。"
          control={<Sw label="先隐去名字" checked={maskNames} onChange={setMaskNames} />}
        />
        <Sep />
        <SettingRow
          title="性欲系统"
          desc="只对你在存档里点名的角色生效（主角与指定配角），其余角色不参与。数值由 AI 与时间共同推进；你在设定里写死的值优先于周期。"
          control={<Sw label="性欲系统" checked={libido} onChange={setLibido} />}
        />
        <SettingRow
          title={`生理周期影响 ${Math.round(libidoCycle * 100)}%`}
          desc="周期对性欲的抬升/压低幅度。0 = 完全不看周期，只按设定与情境。"
          control={
            <GlassSlider
              label="生理周期影响"
              width={150}
              value={libidoCycle}
              onChange={setLibidoCycle}
              disabled={!libido}
            />
          }
        />
      </SettingCard>
    </>
  );
}

/* ── 平台与运行：Card25 + Card27 + Card14 + Card17 + Card20 ── */

function PlatformSection({ writer, navigate }: { writer: SettingsWriter; navigate: ScreenProps['navigate'] }) {
  const { values, set: write } = writer;
  const [phoneWindow, setPhoneWindow] = useBool(true);
  const [closeBehavior, setCloseBehavior] = useStr('每次询问');
  const [lowMemory, setLowMemory] = useBool(false);

  const [alwaysOnTop, setAlwaysOnTop] = useBool(false);
  const [petThink, setPetThink] = useStr('关闭');
  const [petInterval, setPetInterval] = useStr('10');
  const [petScale, setPetScale] = useNum(1);
  const [crossScreen, setCrossScreen] = useBool(false);

  const [performance, setPerformance] = useBool(false);
  const [complexPlot, setComplexPlot] = useBool(false);
  const [novelTesting, setNovelTesting] = useBool(false);
  const [showAffection, setShowAffection] = useBool(false);
  const [affectionUp, setAffectionUp] = useBool(true);
  const [lightning, setLightning] = useBool(true);
  const [lightningProb, setLightningProb] = useStr('0.06');
  const [rain, setRain] = useBool(true);
  const [rainProb, setRainProb] = useStr('0.15');
  const [lightningSfx, setLightningSfx] = useStr('');
  const [rainSfx, setRainSfx] = useStr('');
  const [lightningFrames, setLightningFrames] = useStr('');
  const [rainFrames, setRainFrames] = useStr('');
  const [frameMs, setFrameMs] = useStr('120');
  const [travelAlone, setTravelAlone] = useBool(false);
  const [allowFollow, setAllowFollow] = useBool(true);

  return (
    <>
      <SettingCard title="显卡">
        <SettingRow
          title="优先使用独显"
          desc="在本程序的 Windows 图形设置里登记「高性能」，重启程序后生效。它只影响玻璃模糊、大图缩放这类合成工作——之前量到的界面构建慢是 CPU 侧的事，换显卡帮不上。"
          control={
            <Sw
              label="优先使用独显"
              checked={values.platform.preferDiscreteGpu}
              onChange={(value) => write('platform.preferDiscreteGpu', value)}
            />
          }
        />
      </SettingCard>

      <SettingCard title="窗口与运行">
        <SettingRow
          title="手机开成独立窗口"
          desc="开着：主窗口继续聊天，手机桌面在旁边单开一扇（Windows）。关掉：仍然跳转到应用内的手机页。手机上始终是页内页面。"
          control={<Sw label="手机开成独立窗口" checked={phoneWindow} onChange={setPhoneWindow} />}
        />
        <Sep />
        <PickRow
          title="关主窗口时，其他窗口怎么办"
          desc="「每次询问」会在真有关着的窗口时弹一次选择；选择时可勾选记住，之后来这里改回。只关主窗口时其他窗口继续开，全部关完程序才退出。"
          value={closeBehavior}
          options={['每次询问', '连其他窗口一起关', '只关主窗口']}
          onChange={setCloseBehavior}
          width={160}
        />
        <Sep />
        <SettingRow
          title="超省内存模式"
          desc="记忆、关系网读完就丢缓存，闲置角色的会话清空，启动时不再后台预热；代价是下次回忆要多读一次盘。低配机/大存档时打开。"
          control={<Sw label="超省内存模式" checked={lowMemory} onChange={setLowMemory} />}
        />
      </SettingCard>

      <SettingCard title="电脑端">
        <SettingRow
          title="窗口置顶"
          desc="让游戏窗口始终显示在最前面"
          control={<Sw label="窗口置顶" checked={alwaysOnTop} onChange={setAlwaysOnTop} />}
        />
        <Sep />
        <PickRow
          title="桌宠闲置时长"
          desc="0 关闭；闲置满时长自动进入桌宠，重新操作时自动恢复"
          value={String(values.platform.petIdleMinutes)}
          options={['0', '1', '3', '5', '10', '15', '30', '60']}
          onChange={(next) => write('platform.petIdleMinutes', Number(next))}
          width={110}
        />
        <Sep />
        <PickRow
          title="桌宠思考"
          desc="关闭=不主动嘀咕；随机=按间隔掷骰子（约一半概率）；定时=按间隔必想一次。桌宠模式下单击立绘总能触发一次"
          value={petThink}
          options={['关闭', '随机', '定时']}
          onChange={setPetThink}
          width={110}
        />
        <Sep />
        <PickRow
          title="思考间隔"
          desc="随机 / 定时模式下每隔多久想一次（分钟）"
          value={petInterval}
          options={['1', '5', '10', '15', '30', '60']}
          onChange={setPetInterval}
          width={110}
        />
        <Sep />
        <SettingRow
          title="桌宠体型"
          desc="在「屏幕高的 42%」这个基准上再乘一刀：屏越大，基准顶得越高，这里才是真正调大小的地方。用滚轮或双指捏过之后，这里会跟着变"
          control={
            <span style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
              <RangeSlider
                label="桌宠体型"
                min={0.4}
                max={2.6}
                step={0.05}
                value={petScale}
                onChange={setPetScale}
                width={150}
              />
              <span className="cap" style={{ width: 42 }}>
                {Math.round(petScale * 100)}%
              </span>
            </span>
          }
        />
        <Sep />
        <SettingRow
          title="对话小窗跨屏跟随"
          desc="多显示器用：开启后角色拖到副屏，头顶输入条跟着跨过去；关闭时小窗只在角色那块屏里，贴到屏边会折回本屏。单屏无区别"
          control={<Sw label="对话小窗跨屏跟随" checked={crossScreen} onChange={setCrossScreen} />}
        />
        <Sep />
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--w-space-2)' }}>
          <Btn onClick={() => navigate('/petsettings')}>桌宠设置…</Btn>
          <Btn onClick={() => navigate('/petstudio')}>AI 制作桌宠…</Btn>
        </div>
      </SettingCard>

      <SettingCard
        title="工具模式运行时（Java / Python）"
        note="外部工具使用统一的 JSON-RPC 协议，由 Java / Python 解释器执行。策略：先检测本机 → 手动指定路径 → 必要时自动下载便携运行时。"
      >
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
          <div className="s-row__text">
            <span className="s-row__title" style={{ fontWeight: 700 }}>
              Python
            </span>
            <span className="cap">未检测（点击下方「检测运行时」）</span>
          </div>
          <Inert>
            <Btn>检测</Btn>
          </Inert>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
          <div className="s-row__text">
            <span className="s-row__title" style={{ fontWeight: 700 }}>
              Java
            </span>
            <span className="cap">未检测（点击下方「检测运行时」）</span>
          </div>
          <Inert>
            <Btn>检测</Btn>
          </Inert>
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--w-space-2)' }}>
          <Inert>
            <Btn>下载 Python 便携版</Btn>
          </Inert>
          <Inert>
            <Btn>下载 Java 便携版</Btn>
          </Inert>
        </div>

        <Divider />
        <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
          <div className="s-row__text">
            <span className="s-row__title" style={{ fontWeight: 700 }}>
              MCP 与插件要用的外部运行时
            </span>
            <span className="s-row__desc">未检测：接 MCP 服务或装带网页的插件之前点一次。</span>
          </div>
          <Inert>
            <Btn>检测</Btn>
          </Inert>
        </div>

        <Divider />
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
          <span className="cap">Python 路径：还没有手动指定</span>
          <Inert>
            <Btn>设置路径</Btn>
          </Inert>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
          <span className="cap">Java 路径：还没有手动指定</span>
          <Inert>
            <Btn>设置路径</Btn>
          </Inert>
        </div>
      </SettingCard>

      <SettingCard title="实验性">
        <SettingRow
          title="性能模式"
          desc="弱机、远程桌面或页面滚动发涩时打开：关掉进场/页面动效，卡片阴影改平；功能与布局不变，随时可以关掉。"
          control={<Sw label="性能模式" checked={performance} onChange={setPerformance} />}
        />
        <Sep />
        <SettingRow
          title="复杂剧情模式"
          desc="启用深度思考，增加伏笔"
          control={<Sw label="复杂剧情模式" checked={complexPlot} onChange={setComplexPlot} />}
        />
        <Sep />
        <SettingRow
          title="小说功能（测试）"
          desc="测试用途：导入小说生成剧情概要，不参与正式流程"
          control={<Sw label="小说功能" checked={novelTesting} onChange={setNovelTesting} />}
        />
        <Sep />
        <SettingRow
          title="非攻略对象显示好感"
          control={<Sw label="非攻略对象显示好感" checked={showAffection} onChange={setShowAffection} />}
        />
        <Sep />
        <SettingRow
          title="好感等级提升动画"
          desc="每 100 点好感升 1 级（共 10 级，上限 1000 点），跨越等级时全屏播放爱心动画"
          control={<Sw label="好感等级提升动画" checked={affectionUp} onChange={setAffectionUp} />}
        />
        <Divider />
        <span className="s-row__title">天气事件（打雷 / 下雨）</span>
        <SettingRow
          title="启用打雷事件"
          desc="雨天/极阴天随机打雷：屏幕白一下再慢慢恢复，可配雷声与帧动画"
          control={<Sw label="启用打雷事件" checked={lightning} onChange={setLightning} />}
        />
        <Sep />
        <SettingRow
          title="打雷概率（基准，0-1）"
          desc="打雷天自动×4，雨天为基准值，极阴天×0.3。数值越大越容易打雷"
          control={
            <span style={{ display: 'inline-block', width: 72 }}>
              <Field value={lightningProb} onChange={setLightningProb} type="number" label="打雷概率" />
            </span>
          }
        />
        <Sep />
        <SettingRow
          title="启用下雨事件"
          desc="天气为雨天时随机触发雨声/雨视觉"
          control={<Sw label="启用下雨事件" checked={rain} onChange={setRain} />}
        />
        <Sep />
        <SettingRow
          title="下雨概率（0-1）"
          control={
            <span style={{ display: 'inline-block', width: 72 }}>
              <Field value={rainProb} onChange={setRainProb} type="number" label="下雨概率" />
            </span>
          }
        />
        <Sep />
        <SettingRow
          title="雷声文件路径"
          control={
            <span style={{ display: 'inline-block', width: 180 }}>
              <Field value={lightningSfx} onChange={setLightningSfx} label="雷声文件路径" />
            </span>
          }
        />
        <Sep />
        <SettingRow
          title="雨声文件路径"
          control={
            <span style={{ display: 'inline-block', width: 180 }}>
              <Field value={rainSfx} onChange={setRainSfx} label="雨声文件路径" />
            </span>
          }
        />
        <SettingBlock>
          <span className="s-row__title">打雷帧图（每行一个路径，可留空）</span>
          <span className="cap">按次序切换；只有一张则闪一下；留空则默认白一下再慢慢恢复</span>
          <Field value={lightningFrames} onChange={setLightningFrames} multiline label="打雷帧图" />
        </SettingBlock>
        <SettingBlock>
          <span className="s-row__title">下雨帧图（每行一个路径，可留空）</span>
          <span className="cap">循环切换；留空则不显示雨视觉（只有雨声）</span>
          <Field value={rainFrames} onChange={setRainFrames} multiline label="下雨帧图" />
        </SettingBlock>
        <SettingRow
          title="特效帧切换间隔（毫秒）"
          desc="打雷白闪时长 / 帧图切换频率，建议 80-300"
          control={
            <span style={{ display: 'inline-block', width: 72 }}>
              <Field value={frameMs} onChange={setFrameMs} type="number" label="帧切换间隔" />
            </span>
          }
        />
        <Divider />
        <span className="s-row__title">行进扩展（AI 出行 / 跟随）</span>
        <SettingRow
          title="AI 可单独出行（不带我）"
          desc="开启后 AI 发【移动:】只移动 AI 自己，我的视角留在原地；关闭则维持旧行为（AI 走到哪我跟到哪）"
          control={<Sw label="AI 可单独出行" checked={travelAlone} onChange={setTravelAlone} />}
        />
        <Sep />
        <SettingRow
          title="允许我「跟上」"
          desc="AI 单独走远后，主界面出现「跟上」按钮；点一下即可跟过去"
          control={<Sw label="允许我「跟上」" checked={allowFollow} onChange={setAllowFollow} disabled={!travelAlone} />}
        />
      </SettingCard>
    </>
  );
}

/* ── 外部接入：Card15 + Card16 + Card04 + Card33 ──────── */

/** PluginRow：一行插件。StateLabel 与源码同义（跑不起来 / 不能运行 / 生效中 / 已停用）。 */
type Plugin = {
  id: string;
  name: string;
  version: string;
  description: string;
  author: string;
  enabled: boolean;
  permissions: string[];
  settingsIssue: string;
  missing: string[];
};

const pluginState = (plugin: Plugin) =>
  plugin.missing.length > 0
    ? '跑不起来'
    : plugin.settingsIssue
      ? '不能运行'
      : plugin.enabled
        ? '生效中'
        : '已停用';

function IntegrationsSection({
  writer,
  bridge,
  navigate,
}: {
  writer: SettingsWriter;
  bridge: ScreenProps['bridge'];
  navigate: ScreenProps['navigate'];
}) {
  const { values, set: write } = writer;
  const [qqAppId, setQqAppId] = useStr('');
  const [qqAppSecret, setQqAppSecret] = useStr('');
  const [qqReplyGroup, setQqReplyGroup] = useBool(false);
  const [qqOpenId, setQqOpenId] = useStr('');
  const [qqUserName, setQqUserName] = useStr('');
  const [qqProactive, setQqProactive] = useBool(false);
  const [qqConfirm, setQqConfirm] = useBool(true);
  const [qqPerHour, setQqPerHour] = useStr('3');
  const [outbound, setOutbound] = useStr('自动');
  const [wechatAppId, setWechatAppId] = useStr('');
  const [wechatAppSecret, setWechatAppSecret] = useStr('');
  const [wechatToken, setWechatToken] = useStr('');
  const [wechatPort, setWechatPort] = useStr('8012');
  const [channelsUrl, setChannelsUrl] = useStr('');

  const [mcpAuto, setMcpAuto] = useBool(true);

  const [pluginHooks, setPluginHooks] = useBool(true);
  const [bundlePlugins, setBundlePlugins] = useBool(true);
  const [crashLogs, setCrashLogs] = useBool(true);

  // 已安装的插件：plugins.list 的真表。读不到就落回源码那句「还没有安装插件…」。
  const [plugins, setPlugins] = useState<Plugin[] | null>(null);
  const [pluginsLoading, setPluginsLoading] = useState(true);

  const loadPlugins = useCallback(() => {
    setPluginsLoading(true);
    bridge.request('plugins.list', {}).then(
      ({ plugins: rows }) => {
        setPlugins(rows);
        setPluginsLoading(false);
      },
      () => {
        setPlugins([]);
        setPluginsLoading(false);
      },
    );
  }, [bridge]);

  useEffect(() => {
    loadPlugins();
  }, [loadPlugins]);

  return (
    <>
      <SettingCard
        title="官方接入（真微信 / QQ）"
        note="配置后，游戏里的微信 / QQ 将接入你的真实账号：好友发给你的消息会由 TA 亲自回复。官方方案仅支持单聊，不占用鼠标键盘，也无需额外安装任何程序。"
      >
        <RowHead>QQ 官方机器人</RowHead>
        <SettingRow
          title="启用 QQ 接入"
          desc="前往 q.qq.com 开放平台创建机器人"
          control={
            <Sw label="启用 QQ 接入" checked={values.integrations.qq} onChange={(value) => write('integrations.qq', value)} />
          }
        />
        <Sep />
        <FieldRow title="AppID" value={qqAppId} onChange={setQqAppId} placeholder="机器人 AppID" width={300} />
        <Sep />
        <FieldRow title="AppSecret" value={qqAppSecret} onChange={setQqAppSecret} placeholder="机器人 AppSecret" type="password" width={300} />
        <Sep />
        <PickRow
          title="陪聊角色"
          value="跟随当前游戏角色"
          options={['跟随当前游戏角色']}
          onChange={() => undefined}
          width={220}
          label="选一个角色（存档）"
        />
        <p className="cap">接入 QQ 机器人前先选好用它陪聊的角色；没选则跟随当前游戏角色。</p>

        <SettingRow
          title="回复群聊消息"
          desc="默认不理群里的消息（免得在群里刷屏）；打开后只回应 @ 到机器人的消息"
          control={<Sw label="回复群聊消息" checked={qqReplyGroup} onChange={setQqReplyGroup} />}
        />
        <Sep />
        <FieldRow
          title="我的 QQ openid"
          value={qqOpenId}
          onChange={setQqOpenId}
          placeholder="留空则认最近跟你私聊过的人"
          width={300}
        />
        <Sep />
        <FieldRow title="我的称呼" value={qqUserName} onChange={setQqUserName} placeholder="角色这样称呼你（如：小林）" width={300} />
        <p className="cap">填了 openid 就能认出「哪个是你」；之后角色可以主动给你手机发消息。</p>

        <SettingRow
          title="允许角色主动发消息到我手机"
          desc="存档里 TA「拿起手机发个消息」，就真的发到你的 QQ。默认关闭"
          control={<Sw label="允许角色主动发消息" checked={qqProactive} onChange={setQqProactive} />}
        />
        <Sep />
        <SettingRow
          title="发送前需要我确认"
          desc="开启后每条外发消息都先排队，主界面点「发出去」才真的发送（AI 不能未经同意往外发）"
          control={<Sw label="发送前需要我确认" checked={qqConfirm} onChange={setQqConfirm} />}
        />
        <Sep />
        <PickRow
          title="每小时上限"
          desc="防止误触或被刷屏；超出后 AI 会被告知「先别打扰对方」"
          value={qqPerHour}
          options={['1', '3', '5', '10']}
          onChange={setQqPerHour}
          width={90}
        />
        <Sep />
        <PickRow
          title="走哪个通道"
          desc="QQ 与微信的身份是两套 openid；自动 = 谁在线且认识你就走谁"
          value={outbound}
          options={['自动', '只走 QQ', '只走微信']}
          onChange={setOutbound}
          width={110}
        />

        <RowHead>微信公众号</RowHead>
        <SettingRow
          title="启用微信接入"
          desc="需在公众号后台配置服务器 URL（可用内网穿透）指向本机"
          control={
            <Sw
              label="启用微信接入"
              checked={values.integrations.wechat}
              onChange={(value) => write('integrations.wechat', value)}
            />
          }
        />
        <Sep />
        <FieldRow title="AppID" value={wechatAppId} onChange={setWechatAppId} placeholder="公众号 AppID" width={300} />
        <Sep />
        <FieldRow title="AppSecret" value={wechatAppSecret} onChange={setWechatAppSecret} placeholder="公众号 AppSecret" type="password" width={300} />
        <Sep />
        <FieldRow title="Token" value={wechatToken} onChange={setWechatToken} placeholder="服务器配置 Token" width={300} />
        <Sep />
        <FieldRow title="回调端口" value={wechatPort} onChange={setWechatPort} placeholder="8012" type="number" width={140} />
        <Sep />
        <PickRow
          title="陪聊角色"
          value="跟随当前游戏角色"
          options={['跟随当前游戏角色']}
          onChange={() => undefined}
          width={220}
          label="选一个角色（存档）"
        />
        <p className="cap">微信可以绑另一个角色；和 QQ 选同一个角色，就是 TA 在两个平台上同时在线。</p>
        <Sep />
        <FieldRow
          title="视频号链接"
          value={channelsUrl}
          onChange={setChannelsUrl}
          placeholder="https://…（视频号/频道主页，留空则壳里点不开）"
          width={380}
        />
        <Sep />
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
          <div className="s-row__text">
            <span className="s-row__title">连接状态</span>
            <span className="cap">未启用</span>
          </div>
          <Inert>
            <Btn>测试连接</Btn>
          </Inert>
        </div>
        <p className="cap">
          注意：官方方案不支持群聊；如需要群聊，请自行接入 MCP 服务器，与本应用无关。
        </p>
      </SettingCard>

      <SettingCard title="MCP 配置">
        <p className="s-row__desc">
          MCP 是外部服务，不是文件：要么让本机按一条命令把服务起起来（stdio），要么连一个已经跑着的地址（HTTP）。登记时写清楚从哪儿启动，握手之后它自己报工具清单。
        </p>
        <SettingRow
          title="启用 MCP"
          desc="开启后可登记并连接外部 MCP 服务（本机进程 stdio 或网络地址 HTTP）；插件是另一套系统，不在此列"
          control={
            <Sw label="启用 MCP" checked={values.integrations.mcp} onChange={(value) => write('integrations.mcp', value)} />
          }
        />
        <SettingRow
          title="自动确认"
          desc="开启后脚本/AI 调用 MCP 工具无需逐次人工确认"
          control={<Sw label="自动确认" checked={mcpAuto} onChange={setMcpAuto} />}
        />
        <Divider />
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-2)' }}>
          <span className="s-row__title" style={{ fontWeight: 700 }}>
            已接入的服务
          </span>
          <span style={{ display: 'flex', gap: 'var(--w-space-2)' }}>
            <Inert>
              <Btn>刷新</Btn>
            </Inert>
            <Inert>
              <Btn>＋ 接入服务</Btn>
            </Inert>
          </span>
        </div>
        <p className="cap">
          还没有接入的服务。点「＋ 接入服务」登记一个：本机进程要给启动命令，网络服务要给地址。
        </p>
        <p className="cap">
          下面列出的工具由服务自己报上来：握手成功后 AI 用 mcp_list 就能看到。插件不在这里安装也不在这里显示——插件中心管的是应用内扩展，和 MCP 是两套系统。
        </p>
      </SettingCard>

      <SettingCard title="工具安全 · 沙箱信任名单">
        <Empty
          title="还没有信任的工具目录"
          reason="外部工具默认读不到模型数据（API Key / 对话历史）。"
          hint="点下面的「＋ 信任一个工具目录」挑一个目录，加进来才会放行。"
        />
        <BtnRow>
          <Inert>
            <Btn>＋ 信任一个工具目录</Btn>
          </Inert>
        </BtnRow>
        <p className="cap">默认外部工具无法读取模型数据（API Key / 对话历史）；加入信任后放行。</p>
        <Divider />
        <span className="s-row__title">完整性检查</span>
        <p className="cap">核对该有的工具是否齐全、描述与参数 schema 是否完整、聊天可用工具集能否正常构建。</p>
        <BtnRow>
          <Inert>
            <Btn>检查工具完整性</Btn>
          </Inert>
        </BtnRow>
        <Divider />
        <span className="s-row__title">表情包库</span>
        <p className="cap">导入的表情可以在对话里发送；写上描述，AI 也能按场合挑表情发。</p>
        <BtnRow>
          <Inert>
            <Btn>＋ 导入表情</Btn>
          </Inert>
        </BtnRow>
        <Empty
          title="表情包库是空的"
          reason="还没有导入过表情，列表里没有可用的图片。"
          hint="点「＋ 导入表情」选一张，导入后可以写描述让 AI 按场合挑。"
        />
      </SettingCard>

      <SettingCard title="插件">
        <SettingRow
          title="启用插件钩子"
          desc="插件可以改人设、改写发送前/回复后的文本、加设置页、加页面、调用已注册的工具。钩子跑在受限的 JS 沙箱里（超时、语句数、内存都有上限）；关掉后所有插件效果立即停用，但文件与设置保留。"
          control={<Sw label="启用插件钩子" checked={pluginHooks} onChange={setPluginHooks} />}
        />
        <Sep />
        <SettingRow
          title="存档连同插件一起打包"
          desc="导出存档时把插件代码与设置一并带走，换设备不会出现「有数据没程序」。插件文件较大时会让存档变大；敏感密钥（云存档令牌）不会进包。"
          control={<Sw label="存档连同插件一起打包" checked={bundlePlugins} onChange={setBundlePlugins} />}
        />
        <Sep />
        <SettingRow
          title="记录插件崩溃日志"
          desc="插件钩子抛错时写日志（单文件 256KB 轮转）并弹出可查看的原因；关闭后只静默跳过。无论开关与否，一个插件的错误不会拖垮主程序。"
          control={<Sw label="记录插件崩溃日志" checked={crashLogs} onChange={setCrashLogs} />}
        />
        <Sep />
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-2)' }}>
          <span className="s-row__title" style={{ fontWeight: 700 }}>
            已安装的插件
          </span>
          <span style={{ display: 'flex', gap: 'var(--w-space-2)' }}>
            <Btn onClick={loadPlugins}>刷新</Btn>
            <Btn onClick={() => navigate('/pluginCenter')}>打开插件中心</Btn>
          </span>
        </div>
        {pluginsLoading ? <p className="cap">正在读取已安装的插件…</p> : null}
        {plugins && plugins.length > 0
          ? plugins.map((plugin) => (
              <div
                key={plugin.id}
                style={{
                  display: 'flex',
                  alignItems: 'flex-start',
                  gap: 'var(--w-space-2)',
                  padding: '10px 12px',
                  marginTop: 'var(--w-space-1)',
                  borderRadius: 'var(--w-radius-well)',
                  background: 'var(--w-surface-tint)',
                }}
              >
                <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 2 }}>
                  <span style={{ display: 'flex', alignItems: 'baseline', gap: 'var(--w-space-1)', minWidth: 0 }}>
                    <span
                      style={{
                        fontSize: 'var(--w-size-small)',
                        fontWeight: 700,
                        color: 'var(--w-ink-body)',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                      }}
                    >
                      {plugin.name}
                    </span>
                    <span className="cap">{pluginState(plugin)}</span>
                  </span>
                  <span className="cap">v{plugin.version}</span>
                  {plugin.description ? <span className="cap">{plugin.description}</span> : null}
                  {plugin.settingsIssue ? <span className="cap cap--warn">{plugin.settingsIssue}</span> : null}
                </div>
              </div>
            ))
          : null}
        {!pluginsLoading && (!plugins || plugins.length === 0) ? (
          <p className="cap">还没有安装插件。插件中心能装 zip / 文件夹，也能让 AI 按你的想法现场写一个。</p>
        ) : null}
        <p className="cap">
          插件是应用内的全栈扩展：改人设与回复、加设置页、加页面、调工具；MCP 是外部服务工具（本机进程或网络地址）。两套系统分开安装，插件不会出现在 MCP 列表里，MCP 也不会占用插件目录。
        </p>
      </SettingCard>
    </>
  );
}

/* ── 通知与生活：Card12 + Card13 + Card35 + Card37 ───── */

/** Modules/RealWorld/CityCatalog.Popular 的城名（不联网也能点）。 */
const POPULAR_CITIES = [
  '北京', '上海', '广州', '深圳', '杭州', '南京', '苏州', '无锡', '宁波', '温州', '合肥', '福州', '厦门', '南昌',
  '武汉', '长沙', '郑州', '洛阳', '成都', '重庆', '昆明', '贵阳', '西安', '兰州', '西宁', '银川', '乌鲁木齐', '拉萨',
  '南宁', '海口', '三亚', '佛山', '东莞', '珠海', '天津', '石家庄', '唐山', '太原', '呼和浩特', '沈阳', '大连', '长春',
  '哈尔滨', '青岛', '烟台', '济南', '台北', '香港', '澳门', '东京', '大阪', '首尔', '新加坡', '曼谷', '吉隆坡', '悉尼',
  '墨尔本', '温哥华', '多伦多', '洛杉矶', '旧金山', '纽约', '伦敦', '巴黎', '柏林', '莫斯科', '迪拜',
];

function LifeSection({ writer }: { writer: SettingsWriter }) {
  const { values, set: write } = writer;
  const [greeting, setGreeting] = useBool(true);

  const [citySearch, setCitySearch] = useStr('');
  const [chooserOpen, setChooserOpen] = useBool(false);
  const [weatherSource, setWeatherSource] = useStr('Open-Meteo（默认，免密钥）');
  const [weatherUrl, setWeatherUrl] = useStr('');
  const [weatherKey, setWeatherKey] = useStr('');
  const [cycleGender, setCycleGender] = useStr('女');
  const [cycleTracked, setCycleTracked] = useBool(false);
  const [cycleLength, setCycleLength] = useStr('28');
  const [periodLength, setPeriodLength] = useStr('5');
  const [cycleAdvanced, setCycleAdvanced] = useBool(false);

  const [toast, setToast] = useBool(true);
  const [toastPosition, setToastPosition] = useStr('右上角（默认）');
  const [toastSeconds, setToastSeconds] = useNum(4);

  const [runtime, setRuntime] = useBool(false);
  const [runtimeAi, setRuntimeAi] = useBool(true);
  const [runtimeAiInterval, setRuntimeAiInterval] = useStr('每 20 分钟');
  const [runtimeGames, setRuntimeGames] = useBool(true);
  const [runtimeProactive, setRuntimeProactive] = useBool(false);
  const [runtimeBudget, setRuntimeBudget] = useStr('每小时 6 次');
  const [runtimeQuiet, setRuntimeQuiet] = useBool(true);

  const chooseCity = (raw: string) => {
    const city = raw.trim();
    if (!city || city === values.life.weatherCity) return;
    write('life.weatherCity', city);
    setCitySearch('');
    setChooserOpen(false);
  };

  return (
    <>
      <SettingCard title="通知与陪伴">
        <SettingRow
          title="消息通知"
          desc="真实微信 / QQ 消息到达时提示"
          control={
            <Sw
              label="消息通知"
              checked={values.life.notifications}
              onChange={(value) => write('life.notifications', value)}
            />
          }
        />
        <Sep />
        <SettingRow
          title="空闲问候"
          desc="TA 会在空闲时主动问候你"
          control={<Sw label="空闲问候" checked={greeting} onChange={setGreeting} />}
        />
      </SettingCard>

      <SettingCard title="天气与生活">
        <div
          style={{
            display: 'flex',
            alignItems: 'flex-start',
            justifyContent: 'space-between',
            gap: 'var(--w-space-3)',
            padding: '13px 0',
          }}
        >
          <div className="s-row__text" style={{ gap: 2 }}>
            <span className="s-row__title">所在城市</span>
            <span style={{ fontSize: 15, color: 'var(--w-ink-body)' }}>
              {values.life.weatherCity || '未设置（默认北京）'}
            </span>
          </div>
          <span style={{ display: 'flex', gap: 'var(--w-space-2)' }}>
            <Inert>
              <Btn>定位</Btn>
            </Inert>
            <Btn onClick={() => setChooserOpen(!chooserOpen)}>选择城市</Btn>
          </span>
        </div>
        <p className="cap">定位优先用设备 GPS（电脑走 Windows 定位服务）；IP 只做候选，必须你确认才会生效。</p>
        {chooserOpen ? (
          <SettingBlock>
            {/* 打完城市名按回车（或点去别处）就写进 life.weatherCity。 */}
            <span
              onKeyDown={(event) => {
                if (event.key !== 'Enter') return;
                chooseCity(citySearch);
              }}
              onBlur={() => chooseCity(citySearch)}
            >
              <Field value={citySearch} onChange={setCitySearch} placeholder="输入城市名搜索，如：杭州" label="城市搜索" />
            </span>
            <span className="cap">热门城市（不联网也能选）</span>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px' }}>
              {POPULAR_CITIES.filter((city) => city.includes(citySearch.trim())).map((city) => (
                <Btn key={city} onClick={() => chooseCity(city)}>
                  {city}
                </Btn>
              ))}
            </div>
          </SettingBlock>
        ) : null}
        <PickRow
          title="天气数据源"
          desc="默认 Open-Meteo，可换 wttr.in 或自定义地址"
          value={weatherSource}
          options={['Open-Meteo（默认，免密钥）', 'wttr.in（免密钥）', '自定义（Open-Meteo 兼容）']}
          onChange={setWeatherSource}
          width={180}
        />
        {weatherSource === '自定义（Open-Meteo 兼容）' ? (
          <SettingBlock>
            <FieldRow title="接口地址" value={weatherUrl} onChange={setWeatherUrl} placeholder="https://example.com" width={320} />
            <FieldRow title="密钥" value={weatherKey} onChange={setWeatherKey} placeholder="sk-…" type="password" width={320} />
            <p className="cap">
              自定义地址按 Open-Meteo 协议请求（地址 + /v1/forecast?latitude=…&longitude=…）；密钥只发给这个地址，按
              Authorization: Bearer 头带上
            </p>
          </SettingBlock>
        ) : null}
        <BtnRow>
          <Inert>
            <Btn>测试取一次天气</Btn>
          </Inert>
        </BtnRow>
        <PickRow
          title="适用性别"
          desc="先选性别，再给这个性别配周期"
          value={cycleGender}
          options={['女', '男', '其他']}
          onChange={setCycleGender}
          width={110}
        />
        <Sep />
        <SettingRow
          title="这个性别有生理周期"
          desc="关掉后，提示词和界面都不再出现经期表述"
          control={<Sw label="这个性别有生理周期" checked={cycleTracked} onChange={setCycleTracked} />}
        />
        {cycleTracked ? (
          <SettingBlock>
            <PickRow
              title="生理周期天数"
              desc="TA 的一轮生理周期长度"
              value={cycleLength}
              options={['21', '22', '23', '24', '25', '26', '27', '28', '29', '30', '31', '32', '33', '34', '35']}
              onChange={setCycleLength}
              width={110}
            />
            <PickRow
              title="经期天数"
              desc="每月特殊日子的长度"
              value={periodLength}
              options={['3', '4', '5', '6', '7']}
              onChange={setPeriodLength}
              width={110}
            />
          </SettingBlock>
        ) : null}
        <SettingRow
          title="高级：自定义性别的周期"
          desc="出厂只给「女」配了周期；其他性别要有周期，得在这里新建一条规则"
          control={<Sw label="高级：自定义性别的周期" checked={cycleAdvanced} onChange={setCycleAdvanced} />}
        />
        {cycleAdvanced ? (
          <SettingBlock>
            <div>
              <Inert>
                <Btn>添加性别规则…</Btn>
              </Inert>
            </div>
            <p className="cap">新加的规则默认「无周期」，选中它再打开上面的开关</p>
          </SettingBlock>
        ) : null}
      </SettingCard>

      <SettingCard title="浮动通知">
        <SettingRow
          title="用浮动通知代替弹窗"
          desc="保存、同步、导入这类结果不再弹窗挡住按键：从屏幕边缘滑进来，几秒后自己滑回去。需要你确认的事（删除存档、覆盖文件）仍然用对话框。"
          control={<Sw label="用浮动通知代替弹窗" checked={toast} onChange={setToast} />}
        />
        <Sep />
        <PickRow
          title="出现位置"
          desc="默认右上角：贴着边缘但不压住顶栏按钮。"
          value={toastPosition}
          options={['右上角（默认）', '左上角', '右下角', '左下角', '顶部居中', '底部居中', '屏幕中间']}
          onChange={setToastPosition}
          width={150}
          disabled={!toast}
        />
        <Sep />
        <SettingRow
          title={`停留 ${Number(toastSeconds.toFixed(1))} 秒`}
          desc="从滑入到滑走的停留时长；滑入滑走本身另有 0.2 秒左右。"
          control={
            <RangeSlider
              label="停留时长"
              min={1.5}
              max={12}
              step={0.1}
              value={toastSeconds}
              onChange={setToastSeconds}
              width={150}
              disabled={!toast}
            />
          }
        />
      </SettingCard>

      <SettingCard
        title="存档运行时"
        note="存档打开后，这一档里的角色都在后台继续活着：有自己的作息，会自己下棋、玩小游戏，会用自己的模型写下此刻的生活片段，也会在你想聊的时候接着刚才的事说下去。回标题时这些状态一起存进这一档。"
      >
        <SettingRow
          title="存档打开时让角色继续运行"
          desc="关掉则一切照旧：只有你主动找 TA 时角色才会动。"
          control={<Sw label="存档打开时让角色继续运行" checked={runtime} onChange={setRuntime} />}
        />
        <Sep />
        <SettingRow
          title="让 TA 用模型写生活片段"
          desc="角色用自己的模型写一小段此刻在做什么，记进 TA 的记忆。关掉只做本地作息推演，不花 token。"
          control={<Sw label="让 TA 用模型写生活片段" checked={runtimeAi} onChange={setRuntimeAi} disabled={!runtime} />}
        />
        {runtimeAi ? (
          <PickRow
            title="生活回合间隔"
            desc="每个角色平均多久写一次；实际会按角色错开。"
            value={runtimeAiInterval}
            options={['每 10 分钟', '每 15 分钟', '每 20 分钟', '每 30 分钟', '每 45 分钟', '每 60 分钟']}
            onChange={setRuntimeAiInterval}
            width={120}
            disabled={!runtime}
          />
        ) : null}
        <Sep />
        <SettingRow
          title="让 TA 自己下棋、玩小游戏"
          desc="本地结算，不花 token；只写一条生活记忆，不影响你与 TA 对弈时的棋力记录。"
          control={<Sw label="让 TA 自己下棋、玩小游戏" checked={runtimeGames} onChange={setRuntimeGames} disabled={!runtime} />}
        />
        <Sep />
        <SettingRow
          title="允许 TA 主动给你发消息"
          desc="还要在「外部接入 → QQ/微信」里打开「允许角色主动发消息」，并且让 TA 认识你。只有绑定该通道的角色才会发。"
          control={<Sw label="允许 TA 主动给你发消息" checked={runtimeProactive} onChange={setRuntimeProactive} disabled={!runtime} />}
        />
        {runtimeProactive ? (
          <PickRow
            title="后台每小时最多跑几次 AI"
            desc="所有角色共用这个预算，避免后台把额度吃光。"
            value={runtimeBudget}
            options={['每小时 3 次', '每小时 6 次', '每小时 12 次', '每小时 20 次']}
            onChange={setRuntimeBudget}
            width={120}
            disabled={!runtime}
          />
        ) : null}
        <Sep />
        <SettingRow
          title="深夜安静（23:00–7:00）"
          desc="这段时间只换作息，不写生活片段、不下棋、不发消息，也不花 token。"
          control={<Sw label="深夜安静" checked={runtimeQuiet} onChange={setRuntimeQuiet} disabled={!runtime} />}
        />
        <Sep />
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
          <div className="s-row__text">
            <span className="s-row__title">角色此刻在做什么</span>
            <span className="cap">还没有角色动起来，进存档待一会儿再来看看。</span>
          </div>
          <Inert>
            <Btn>刷新</Btn>
          </Inert>
        </div>
      </SettingCard>
    </>
  );
}

/* ── 数据与素材：Card18 + Card26 + Card19 + Card21 + Card22 ── */

function DataSection({ navigate }: { navigate: ScreenProps['navigate'] }) {
  const [includeSecrets, setIncludeSecrets] = useBool(false);

  const [cloud, setCloud] = useBool(false);
  const [cloudRepo, setCloudRepo] = useStr('');
  const [cloudToken, setCloudToken] = useStr('');
  const [cloudDevice, setCloudDevice] = useStr('');
  const [cloudAuto, setCloudAuto] = useBool(true);
  const [cloudReconcile, setCloudReconcile] = useBool(true);

  const [beta, setBeta] = useBool(false);

  return (
    <>
      <SettingCard
        title="数据管理"
        note="导出 / 导入整个应用数据（立绘、存档、角色、背景等）。默认不含 API 密钥与机器人密钥，可在下方勾选。"
      >
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--w-space-2)' }}>
          <Inert>
            <SignalButton>导出数据包</SignalButton>
          </Inert>
          <Inert>
            <Btn>导入数据包</Btn>
          </Inert>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-2)' }}>
          <span className="cap">包含 API 密钥与机器人密钥</span>
          <Sw label="包含 API 密钥与机器人密钥" checked={includeSecrets} onChange={setIncludeSecrets} />
        </div>
      </SettingCard>

      <SettingCard title="云存档" note="把存档、角色与聊天记录镜像到你自己的 GitHub 仓库。两台设备各写各的目录，按时间取新，不会互相覆盖；同时使用最坏也只是旧的一份被放进 conflicts 备份。">
        <SettingBlock>
          <span className="cap" style={{ lineHeight: 1.5 }}>
            只接受私人仓库（Private）：公开仓库会被拒绝同步；访问令牌需要有 repo（私有仓库读写）权限，且只保存在本机。
          </span>
        </SettingBlock>
        <SettingRow
          title="启用云存档"
          desc="关闭时只在本机保存；开启后改动会自动上传"
          control={<Sw label="启用云存档" checked={cloud} onChange={setCloud} />}
        />
        <Sep />
        <FieldRow title="仓库" value={cloudRepo} onChange={setCloudRepo} placeholder="owner/repo（建议私有仓库）" width={300} />
        <Sep />
        <FieldRow
          title="访问令牌"
          value={cloudToken}
          onChange={setCloudToken}
          placeholder="github_pat_…（只需 Contents 读写）"
          type="password"
          width={300}
        />
        <Sep />
        <FieldRow title="设备名" value={cloudDevice} onChange={setCloudDevice} placeholder="家里电脑 / 我的手机" width={260} />
        <Sep />
        <SettingRow
          title="改动后自动上传"
          desc="停手约两秒后上传一次，不打断输入"
          control={<Sw label="改动后自动上传" checked={cloudAuto} onChange={setCloudAuto} />}
        />
        <Sep />
        <SettingRow
          title="同步后整理记忆"
          desc="双端合并后让对话模型去掉重复记忆；未配置模型时跳过"
          control={<Sw label="同步后整理记忆" checked={cloudReconcile} onChange={setCloudReconcile} />}
        />
        <Sep />
        <div style={{ display: 'grid', gridTemplateColumns: '1fr auto', gap: 'var(--w-space-2)', padding: '13px 0' }}>
          <Inert>
            <SignalButton>立即同步</SignalButton>
          </Inert>
          <Inert>
            <Btn>测试连接</Btn>
          </Inert>
        </div>
        <p className="cap">
          令牌只保存在这台设备的设置里，settings.json 不会上传；仓库里只有存档与数据文件，立绘/背景等图片素材不上传。
        </p>
      </SettingCard>

      <SettingCard title="自动更新">
        <SettingRow
          title="测试计划（Beta 渠道）"
          desc="开启后 *-beta 测试版 tag 也会提示更新"
          control={<Sw label="测试计划（Beta 渠道）" checked={beta} onChange={setBeta} />}
        />
        <div style={{ display: 'grid', gridTemplateColumns: '1fr auto', gap: 'var(--w-space-2)' }}>
          <Inert>
            <SignalButton>检查更新</SignalButton>
          </Inert>
          <Inert>
            <Btn>下载</Btn>
          </Inert>
        </div>
      </SettingCard>

      <SettingCard title="素材库（背景 · 音乐）">
        <p className="s-row__desc" style={{ textAlign: 'center' }}>
          导入 / 管理背景图与音乐，并把音乐设为左键音、右键音、按键音或背景音轮播
        </p>
        <BtnRow>
          <Btn onClick={() => navigate('/materials')}>进入素材库</Btn>
        </BtnRow>
      </SettingCard>

      <SettingCard title="能力包（Skills）">
        <p className="cap" style={{ textAlign: 'center' }}>
          还没有装能力包。
        </p>
        <p className="s-row__desc" style={{ textAlign: 'center' }}>
          一个能力包 = 一个目录，可含 SKILL.md（写给 AI 的说明书，按需加载）和 tool.json（可执行入口，复用 Python/Java 运行时）。装上后 AI 会先读说明书再动手。
        </p>
        <BtnRow>
          <Inert>
            <Btn>导入 zip / 目录</Btn>
          </Inert>
          <Inert>
            <Btn>管理</Btn>
          </Inert>
        </BtnRow>
      </SettingCard>
    </>
  );
}

/* ── 其他与开发者：Card23 + Card24 ───────────────────── */

function AdvancedSection() {
  const [taps, setTaps] = useState(0);

  return (
    <>
      <SettingCard title="支持作者">
        <p className="s-row__desc" style={{ textAlign: 'center' }}>
          如果您喜欢这个项目，欢迎赞助作者支持 continued development
        </p>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--w-space-3)' }}>
          <Inert>
            <SignalButton>爱发电赞助</SignalButton>
          </Inert>
          <Inert>
            <Btn>B站主页</Btn>
          </Inert>
        </div>
      </SettingCard>

      <SettingCard title="开发者展示模式">
        <p className="s-row__desc" style={{ textAlign: 'center' }}>
          隐藏模式：为创作者提供的展示案编辑 / 播放功能（含多立绘支持）
        </p>
        <p className="cap" style={{ textAlign: 'center' }}>
          滚到设置最底部后，连续点按“连击解锁”按钮 114514 次可开启
        </p>
        <BtnRow>
          <Btn onClick={() => setTaps(taps + 1)}>连击解锁</Btn>
        </BtnRow>
        <p className="cap" style={{ textAlign: 'center' }}>
          已点按 {taps} 次
        </p>
      </SettingCard>
    </>
  );
}
