import { PageShell } from '../../components/PageShell';
import { SaveGate } from '../../components/SaveGate';
import { useRun } from '../../lib/run';
import type { ScreenProps } from '../types';
import { QqClient } from './qqclient';
import './savechat.css';

/**
 * QQ 壳的入口：Views/Chat/QqPage.xaml（335 行）、WeChatViewModel 的 SetShell("qq")。
 * 壳本身在 qqclient.tsx —— 左窄条 / 会话列表 / 会话区 / 群信息栏四块摊在一页里。
 */
export function Qqchat({ bridge, navigate, back }: ScreenProps) {
  const run = useRun(bridge);
  if (run.known && !run.active) {
    return <SaveGate title="QQ" eyebrow="// QQ" onBack={back} onGoSave={() => navigate('/save')} />;
  }
  return (
    <PageShell>
      <QqClient bridge={bridge} navigate={navigate} back={back} />
    </PageShell>
  );
}
