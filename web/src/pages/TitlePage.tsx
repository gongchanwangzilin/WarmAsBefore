import { useCallback, useState } from 'react';
import type { Bridge } from '../bridge/client';
import type { SaveSummary } from '../bridge/protocol';
import { Glass } from '../components/Glass';
import { PageShell } from '../components/PageShell';
import { SignalButton } from '../components/SignalButton';
import { Skeleton } from '../components/states/Skeleton';
import { StateBlock } from '../components/states/StateBlock';
import { useResource } from '../lib/useResource';

type Props = {
  bridge: Bridge;
  onNavigate: (path: string) => void;
};

type Intent = 'idle' | 'pending' | 'unavailable';

export function TitlePage({ bridge, onNavigate }: Props) {
  const { state, reload } = useResource(() => bridge.request('settings.get', {}), [bridge]);
  const [intent, setIntent] = useState<Intent>('idle');

  const start = useCallback(async () => {
    setIntent('pending');
    try {
      const { accepted } = await bridge.request('nav.open', { route: '/play' });
      setIntent(accepted ? 'idle' : 'unavailable');
    } catch {
      setIntent('unavailable');
    }
  }, [bridge]);

  return (
    <PageShell>
      <header className="titlebar">
        <span className="titlebar__mark">温暖如初</span>
        <button type="button" className="link" onClick={() => onNavigate('/settings')}>
          设置
        </button>
      </header>

      <main className="content">
        <div className="hero">
          <p className="hero__mark">温暖如初</p>
          <h1 className="hero__name" data-page-title tabIndex={-1}>
            Warm As Before
          </h1>
          <p className="hero__tagline">让每一段重逢，都如初见般温暖</p>
        </div>

        {state.status === 'loading' ? <TitleSkeleton /> : null}

        {state.status === 'error' ? (
          <StateBlock
            tone="error"
            title="没能读到上次的存档"
            body={
              <p>
                本地服务这次没有应答，故事还好好放在盘上。先确认后台还在运行，再试一次；也可以先
                去设置里把外观调一下。
              </p>
            }
            detail={`${state.error.message}（${state.error.code}）`}
            action="再试一次"
            onAction={reload}
          />
        ) : null}

        {state.status === 'ready' ? (
          <>
            <div className="actions actions--center">
              <SignalButton onClick={start} busy={intent === 'pending'}>
                再续情缘
              </SignalButton>
            </div>

            {intent === 'unavailable' ? (
              <p className="hint" role="status">
                故事舞台还没接到这条桥上。这一批先把外观与导航跑通，按钮保持可按、结果如实告诉你。
              </p>
            ) : null}

            {state.data.save ? (
              <SaveCard save={state.data.save} onOpen={start} />
            ) : (
              <StartCard onStart={start} />
            )}
          </>
        ) : null}
      </main>
    </PageShell>
  );
}

function SaveCard({ save, onOpen }: { save: SaveSummary; onOpen: () => void }) {
  const when = new Date(save.savedAt).toLocaleString('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });

  return (
    <Glass as="button" type="button" tone="card" radius={12} className="savecard" onClick={onOpen}>
      <span className="savecard__eyebrow">上次的存档</span>
      <span className="savecard__name">{save.character}</span>
      <span className="savecard__scene">{save.scene}</span>
      <span className="savecard__meta">
        {when} · {save.minutes} 分钟
      </span>
    </Glass>
  );
}

function StartCard({ onStart }: { onStart: () => void }) {
  return (
    <Glass as="section" tone="card" radius={12} className="card startcard">
      <p className="state__eyebrow">还没有存档</p>
      <h2 className="state__title">第一次的相遇，会记在这里</h2>
      <ol className="steps">
        <li>按下「再续情缘」，先挑一个人物。</li>
        <li>说第一句话，故事就开始了。</li>
        <li>之后每次离开，这张卡会替你记住在哪儿、聊了多久。</li>
      </ol>
      <div className="state__actions">
        <button type="button" className="ghost" onClick={onStart}>
          开始一段新的
        </button>
      </div>
    </Glass>
  );
}

function TitleSkeleton() {
  return (
    <div aria-busy="true" aria-live="polite">
      <div className="actions actions--center">
        <Skeleton w="150px" h="42px" r="8px" />
      </div>
      <Glass as="section" tone="card" radius={12} className="card sk-card">
        <Skeleton w="42%" h="11px" />
        <div className="sk-gap" />
        <Skeleton w="58%" h="18px" />
        <div className="sk-gap" />
        <Skeleton w="100%" h="13px" />
        <div className="sk-gap" />
        <Skeleton w="34%" h="12px" />
      </Glass>
      <p className="sr-only">正在读取上次的存档…</p>
    </div>
  );
}
