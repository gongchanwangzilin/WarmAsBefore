import { useEffect, useRef, useState } from 'react';
import type { Bridge } from '../bridge/client';
import type { Appearance } from '../bridge/protocol';
import { Glass } from '../components/Glass';
import { GlassSlider } from '../components/GlassSlider';
import { GlassButton } from '../components/GlassButton';
import { GlassSwitch } from '../components/GlassSwitch';
import { PageShell } from '../components/PageShell';
import { SettingBlock, SettingCard, SettingRow, Sep } from '../components/SettingCard';
import { TopBar } from '../components/TopBar';
import { Skeleton } from '../components/states/Skeleton';
import { StateBlock } from '../components/states/StateBlock';
import type { AppearanceStore } from '../lib/useAppearance';
import {
  BACKGROUND_MODE_CHOICES,
  SOLID_SWATCHES,
  pageTintHint,
  solidHint,
  solidVisible,
} from '../lib/appearance';

/** 分类页的标题与说明取自 SettingsCatalog（key=appearance）。 */
const SECTION_TITLE = '外观与主题';
const SECTION_DESC = '玻璃效果、光源方向、页面背景与天气叠加';

type Props = {
  bridge: Bridge;
  store: AppearanceStore;
  onBack: () => void;
};

/**
 * 「外观与主题」分类页：SettingsSectionPage.xaml 的 Card02（玻璃效果）+ Card03（背景）。
 * 每张卡的标题、每一行的主标题与说明都逐字取自源码；控件顺序一致。
 */
/** 色板只有一处定义：区间色板来自 DesignSystem 的 XAML（palette.swatches），取不到才用自带的十颗。 */
function useSwatches(bridge: Bridge | undefined) {
  const [swatches, setSwatches] = useState(SOLID_SWATCHES as unknown as { hex: string; name: string }[]);
  useEffect(() => {
    if (!bridge) return;
    let dropped = false;
    void bridge
      .request('palette.swatches', {})
      .then(({ swatches: rows }) => {
        if (!dropped && rows.length > 0) setSwatches(rows);
      })
      .catch(() => undefined);
    return () => {
      dropped = true;
    };
  }, [bridge]);
  return swatches;
}

export function AppearancePage({ bridge, store, onBack }: Props) {
  const swatches = useSwatches(bridge);
  const { value } = store;
  const set = (patch: Partial<Appearance>) => store.update({ ...value, ...patch });
  const glassActive = value.alpha > 0.01;

  return (
    <PageShell>
      <TopBar variant="bar" title="设置" onBack={onBack} />

      <main className="section">
        <div className="section__body">
          <div className="section__head">
            <div className="section__title-row">
              <span className="section__mark" aria-hidden="true" />
              <h2 className="section__title">{SECTION_TITLE}</h2>
            </div>
            <p className="section__desc">{SECTION_DESC}</p>
          </div>

          {store.status === 'loading' ? <CardSkeleton /> : null}

          {store.status === 'error' ? (
            <StateBlock
              tone="error"
              title="读不到设置"
              body={<p>本地服务这次没有回话。确认后台还在运行，再试一次。</p>}
              detail={store.error ? `${store.error.message}（${store.error.code}）` : undefined}
              action="重新读取"
              onAction={store.reload}
            />
          ) : null}

          {store.status === 'empty' ? (
            <StateBlock
              tone="empty"
              title="没有可调的外观"
              body={<p>后端还没有下发外观参数。等它挂上来再看。</p>}
              action="再读一次"
              onAction={store.reload}
            />
          ) : null}

          {store.status === 'ready' ? (
            <>
              <SettingCard title="玻璃效果">
                <SettingRow
                  title="轻量模式（关阴影，低配更流畅）"
                  desc="关闭后卡片不再有投影，观感朴素一些。阴影在 WinUI 上是每个元素一个合成层，卡片多或显卡弱时更划算；本机（独立显卡）实测差异在噪声范围内。"
                  control={
                    <GlassSwitch
                      label="轻量模式"
                      checked={value.lightweight}
                      onChange={(lightweight) => set({ lightweight })}
                    />
                  }
                />
                <Sep />
                <SettingRow
                  gap
                  title="玻璃透明度"
                  desc="0 = 表面不透明，等同于关闭玻璃；1 = 最通透"
                  control={
                    <GlassSlider
                      label="玻璃透明度"
                      value={value.alpha}
                      onChange={(alpha) => set({ alpha })}
                    />
                  }
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
                    <GlassSwitch
                      label="减弱透明降级"
                      checked={value.reducedTransparency}
                      onChange={(reducedTransparency) => set({ reducedTransparency })}
                    />
                  }
                />
              </SettingCard>

              <BackgroundCard store={store} swatches={swatches} />

              {store.save === 'error' ? (
                <p className="cap cap--warn">
                  没有写进去。
                  <button type="button" className="link" onClick={store.retrySave}>
                    重试
                  </button>
                </p>
              ) : null}
            </>
          ) : null}
        </div>
      </main>
    </PageShell>
  );
}

function BackgroundCard({
  store,
  swatches,
}: {
  store: AppearanceStore;
  swatches: { hex: string; name: string }[];
}) {
  const { value } = store;
  const bg = value.background;
  const scene = value.scene;
  const set = (patch: Partial<Appearance>) => store.update({ ...value, ...patch });
  const setBackground = (patch: Partial<Appearance['background']>) =>
    set({ background: { ...bg, ...patch } });
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
            onChange={(event) =>
              setBackground({ mode: event.target.value as Appearance['background']['mode'] })
            }
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
            {swatches.map((swatch) => {
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
              <GlassButton
                size="md"
                className="gbtn--tight"
                onClick={() => imagePicker.current?.click()}
              >
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
          <GlassSwitch
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
            control={
              <GlassSwitch
                label="天气场景叠加"
                checked={scene.weather}
                onChange={(weather) => setScene({ weather })}
              />
            }
          />
          <SettingRow
            title="场景光照与光柱"
            desc="光的方向随一天变化、强度随能见度变化；关掉只保留云与底色"
            control={
              <GlassSwitch
                label="场景光照与光柱"
                checked={scene.light}
                onChange={(light) => setScene({ light })}
              />
            }
          />
          {scene.weather ? (
            <>
              <SettingRow
                title="场景音效"
                desc="雨声、风声、闪电；需先在素材库分配音效卡槽"
                control={
                  <GlassSwitch
                    label="场景音效"
                    checked={scene.audio}
                    onChange={(audio) => setScene({ audio })}
                  />
                }
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

function CardSkeleton() {
  return (
    <Glass as="section" tone="card" radius={12} className="s-card" aria-busy="true">
      <Skeleton w="30%" h="14px" />
      <div className="sk-gap" />
      <Skeleton w="86%" h="12px" />
      <div className="sk-gap" />
      <Skeleton w="62%" h="12px" />
      <p className="sr-only">正在读取设置…</p>
    </Glass>
  );
}
