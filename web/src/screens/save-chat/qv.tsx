import { useEffect, useState } from 'react';
import { Empty } from '../../components/kit';
import { PageShell } from '../../components/PageShell';
import { SaveGate } from '../../components/SaveGate';
import { useRun } from '../../lib/run';
import type { ScreenProps } from '../types';
import { QvClient } from './qvclient';
import './savechat.css';

/**
 * QV：QQ 与假微信合成的那一个应用（Views/Chat/QqPage.xaml 的四栏壳）。
 * 两个来源的会话并成一条名册，壳本身在 qvclient.tsx。
 * 老的 /chat 与 /qqchat 两个地址由路由表指到这里。
 */
export function Qv({ bridge, navigate, back }: ScreenProps) {
  const [attempt, setAttempt] = useState(0);
  const run = useRun(bridge, attempt);
  // 「有没有在存档里」这一问没有超时：宿主不回，页面就永远是一块空地。
  // 五秒还没答上来就说清楚在等什么，并把重试摆出来 —— 白屏查不出是谁的问题。
  const [overdue, setOverdue] = useState(false);
  useEffect(() => {
    if (run.known) {
      setOverdue(false);
      return;
    }
    const timer = window.setTimeout(() => setOverdue(true), 5000);
    return () => window.clearTimeout(timer);
  }, [run.known, attempt]);

  // 还没问到「现在有没有在跑一份存档」之前，一个壳都不挂：
  // 壳一挂上就去读名册与历史，而那份名册是全局的 —— 没有当前档时读出来的东西
  // 不属于任何一局，看着就像「打开 QV 就进了某一档」。先问清楚，再决定挂壳还是挂存档闸。
  if (!run.known) {
    return (
      <PageShell>
        <div className="screen">
          {overdue ? (
            <>
              <Empty
                title="QV 打不开"
                reason="读不到存档状态：这一问（app.info）一直没回应。没有它，QV 不知道该挂名册还是挂存档闸。"
                hint="先点重试；还是打不开，就去设置里看 AI 厂商那一页，或把 Documents/warm_startup.log 发给开发者。"
              />
              <button type="button" className="k-textbtn" onClick={() => setAttempt((n) => n + 1)}>
                重试
              </button>
            </>
          ) : (
            <div className="screen" aria-busy="true" />
          )}
        </div>
      </PageShell>
    );
  }
  if (!run.active) {
    return <SaveGate title="QV" eyebrow="// QV" onBack={back} onGoSave={() => navigate('/save')} />;
  }
  return (
    <PageShell>
      <QvClient bridge={bridge} navigate={navigate} back={back} />
    </PageShell>
  );
}
