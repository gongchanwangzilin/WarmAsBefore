import { Fragment, useCallback } from 'react';
import type { Bridge } from '../bridge/client';
import type { SaveSummary } from '../bridge/protocol';
import { Glass } from '../components/Glass';
import { GlassButton } from '../components/GlassButton';
import { PageShell } from '../components/PageShell';
import { SignalButton } from '../components/SignalButton';
import { Skeleton } from '../components/states/Skeleton';
import { StateBlock } from '../components/states/StateBlock';
import { useResource } from '../lib/useResource';

type Props = {
  bridge: Bridge;
  onNavigate: (path: string) => void;
};

/** 四个次要入口（TitlePage.xaml 的 01~04）。Web 端只有「设置」有页。 */
const MENU: ReadonlyArray<{ index: string; label: string; to: string | null }> = [
  { index: '01', label: '存档管理', to: null },
  { index: '02', label: '平行宇宙', to: null },
  { index: '03', label: '角色库', to: null },
  { index: '04', label: '设置', to: '/settings' },
];

/** 右上角读数：「配色 · 风格」。色板锁定 endfield（ThemeManager.LockedPalette），风格恒为档案室。 */
const THEME_LEGEND = { color: '终末地', effect: '档案室' };

/**
 * 标题页：一整页铺满窗口的卡，左边导航带、右边主行动区，中间一根发丝线（TitlePage.xaml）。
 */
export function TitlePage({ bridge, onNavigate }: Props) {
  const { state, reload } = useResource(() => bridge.request('settings.get', {}), [bridge]);

  const start = useCallback(() => {
    void bridge.request('nav.open', { route: '/play' }).catch(() => undefined);
  }, [bridge]);

  const save = state.status === 'ready' ? state.data.save : null;

  return (
    <PageShell>
      <main className="title">
        <Glass as="div" tone="card" radius={12} className="shell">
          <div className="shell__rail">
            <div>
              <div className="brand">
                <span className="brand__mark">
                  <img src="/images/titlelogo.png" alt="" />
                </span>
                <span className="brand__text">
                  <span className="brand__name">温暖如初</span>
                  <span className="brand__latin">WARM AS BEFORE</span>
                </span>
              </div>
              <div className="hr" />
              <nav className="menu">
                {MENU.map((item, index) => (
                  <Fragment key={item.index}>
                    {index > 0 ? <div className="hr" /> : null}
                    <button
                      type="button"
                      className="menu__row"
                      onClick={() => item.to && onNavigate(item.to)}
                      aria-disabled={item.to ? undefined : true}
                    >
                      <span className="menu__num">{item.index}</span>
                      <span className="menu__label">{item.label}</span>
                      <span className="menu__chev" aria-hidden="true">
                        ›
                      </span>
                    </button>
                  </Fragment>
                ))}
              </nav>
            </div>

            <div className="rail__foot">
              <div className="hr" />
              <div className="foot">
                <p className="foot__name">✦ 同住体验</p>
                <p className="foot__desc">每天有 TA 的陪伴，聊天、生活、小游戏，温暖如初。</p>
                <div className="foot__btns">
                  <GlassButton size="sm">桌宠设置</GlassButton>
                  <GlassButton size="sm">退出游戏</GlassButton>
                </div>
              </div>
            </div>
          </div>

          <div className="shell__sep" aria-hidden="true" />

          <div className="hero">
            <p className="hero__legend">
              <span>{THEME_LEGEND.color}</span>
              <span>·</span>
              <span>{THEME_LEGEND.effect}</span>
            </p>

            {state.status === 'loading' ? <TitleSkeleton /> : null}

            {state.status === 'error' ? (
              <StateBlock
                tone="error"
                title="读不到设置"
                body={<p>本地服务这次没有回话。确认后台还在运行，再试一次。</p>}
                detail={`${state.error.message}（${state.error.code}）`}
                action="重新读取"
                onAction={reload}
              />
            ) : null}

            {state.status === 'ready' ? (
              <>
                <div className="hero__band">
                  <h1 className="hero__tagline" data-page-title tabIndex={-1}>
                    让每一段重逢，都如初见般温暖
                  </h1>
                  <SignalButton onClick={start}>{save ? '再续情缘' : '创建新欢'}</SignalButton>
                </div>

                {save ? (
                  <Glass as="div" tone="card" radius={12} className="recent">
                    <div className="recent__text">
                      <span className="eyebrow">// LAST SAVE</span>
                      <span className="recent__name">{save.character}</span>
                      <span className="recent__scene">{save.scene}</span>
                      <span className="cap">{recentMeta(save)}</span>
                    </div>
                    <GlassButton size="md" onClick={start}>
                      继续
                    </GlassButton>
                  </Glass>
                ) : null}
              </>
            ) : null}
          </div>
        </Glass>
      </main>
    </PageShell>
  );
}

/** RecentMeta = "MM 月 dd 日 HH:mm · N 分钟"（TitleViewModel.RefreshState）。 */
function recentMeta(save: SaveSummary): string {
  const at = new Date(save.savedAt);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(at.getMonth() + 1)} 月 ${pad(at.getDate())} 日 ${pad(at.getHours())}:${pad(
    at.getMinutes(),
  )} · ${save.minutes} 分钟`;
}

function TitleSkeleton() {
  return (
    <div className="hero__band" aria-busy="true">
      <Skeleton w="78%" h="30px" />
      <div className="sk-gap" />
      <Skeleton w="200px" h="46px" r="8px" />
      <p className="sr-only">正在读取设置…</p>
    </div>
  );
}
