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
  if (run.known && !run.active) {
    return <SaveGate title="QV" eyebrow="// QV" onBack={back} onGoSave={() => navigate('/save')} />;
  }
  return (
    <PageShell>
      <QvClient bridge={bridge} navigate={navigate} back={back} />
    </PageShell>
  );
}
