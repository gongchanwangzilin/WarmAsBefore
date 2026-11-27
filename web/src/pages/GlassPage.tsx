import { DEFAULT_PALETTE } from '../lib/defaults';
import type { PaletteStore } from '../lib/usePalette';
import { Glass } from '../components/Glass';
import { GlassSlider } from '../components/GlassSlider';
import { GlassSwitch } from '../components/GlassSwitch';
import { PageShell } from '../components/PageShell';
import { Section } from '../components/Section';
import { TopBar } from '../components/TopBar';
import { Skeleton } from '../components/states/Skeleton';
import { StateBlock } from '../components/states/StateBlock';

type Props = {
  store: PaletteStore;
  onBack: () => void;
};

export function GlassPage({ store, onBack }: Props) {
  const { value } = store;
  const set = (patch: Partial<typeof value>) => store.update({ ...value, ...patch });

  return (
    <PageShell>
      <TopBar title="玻璃效果" onBack={onBack} />

      <main className="content">
        {store.status === 'loading' ? <GlassSkeleton /> : null}

        {store.status === 'error' ? (
          <StateBlock
            tone="error"
            title="没拿到这台设备的玻璃参数"
            body={
              <p>
                光谱存在后端，前端只负责画。这最后一次请求没有回来，所以现在显示的是上一份值。
                先重试；还是不行就退回标题页，故事不受影响。
              </p>
            }
            detail={store.error ? `${store.error.message}（${store.error.code}）` : undefined}
            action="重新取一次"
            onAction={store.reload}
          />
        ) : null}

        {store.status === 'empty' ? (
          <StateBlock
            tone="empty"
            title="这套设备还没有下发光谱"
            body={
              <p>
                后端没有登记玻璃参数，所以这里没有可调的东西。可以先套一份默认光谱 —— 默认值
                与原型里的初始状态一致，之后所有改动都会写回后端。
              </p>
            }
            action="用默认光谱开始"
            onAction={() => store.update(DEFAULT_PALETTE)}
          />
        ) : null}

        {store.status === 'ready' ? (
          <>
            <Section
              title="玻璃效果"
              note="指针在窗口任意位置移动，卡片与按钮上的光都跟着走，离得越远越暗；墨色由合成面算出，正文不低于 4.5:1。"
            >
              <div className="row">
                <span className="row__label">轻量模式</span>
                <GlassSwitch
                  label="轻量模式"
                  checked={value.lightweight}
                  onChange={(lightweight) => set({ lightweight })}
                />
              </div>
              <GlassSlider
                label="玻璃透明度"
                value={value.alpha}
                onChange={(alpha) => set({ alpha })}
              />
              <GlassSlider label="磨砂程度" value={value.frost} onChange={(frost) => set({ frost })} />
              <GlassSlider
                label="液态高度"
                value={value.liquid}
                onChange={(liquid) => set({ liquid })}
              />
            </Section>

            <div className="actions actions--between">
              <button type="button" className="ghost" onClick={() => store.update(DEFAULT_PALETTE)}>
                恢复默认
              </button>
              <span className="save-line" role="status">
                {store.save === 'saving' ? '保存中…' : null}
                {store.save === 'idle' ? '已保存到后端' : null}
                {store.save === 'error' ? (
                  <>
                    没写进去。
                    <button type="button" className="link" onClick={store.retrySave}>
                      重试
                    </button>
                  </>
                ) : null}
              </span>
            </div>
          </>
        ) : null}
      </main>
    </PageShell>
  );
}

function GlassSkeleton() {
  return (
    <div aria-busy="true">
      <Glass as="section" tone="card" radius={12} className="card sk-card">
        <Skeleton w="30%" h="14px" />
        <div className="sk-gap" />
        <div className="sk-row">
          <Skeleton w="34%" h="13px" />
          <Skeleton w="46px" h="27px" r="13px" />
        </div>
        <div className="sk-row">
          <Skeleton w="30%" h="13px" />
          <Skeleton w="48%" h="12px" />
        </div>
        <div className="sk-row">
          <Skeleton w="26%" h="13px" />
          <Skeleton w="48%" h="12px" />
        </div>
        <div className="sk-row">
          <Skeleton w="32%" h="13px" />
          <Skeleton w="48%" h="12px" />
        </div>
      </Glass>
      <p className="sr-only">正在读取玻璃参数…</p>
    </div>
  );
}
