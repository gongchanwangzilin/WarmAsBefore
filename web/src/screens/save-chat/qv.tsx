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
  const run = useRun(bridge);
  // 还没问到「现在有没有在跑一份存档」之前，一个壳都不挂：
  // 壳一挂上就去读名册与历史，而那份名册是全局的 —— 没有当前档时读出来的东西
  // 不属于任何一局，看着就像「打开 QV 就进了某一档」。先问清楚，再决定挂壳还是挂存档闸。
  if (!run.known) {
    return (
      <PageShell>
        <div className="screen" aria-busy="true" />
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
