import { useState } from 'react';
import { Empty, PageHeader } from '../../components/kit';
import { PageShell } from '../../components/PageShell';
import { SignalButton } from '../../components/SignalButton';
import type { ScreenProps } from '../types';
import { Actions, FootBar } from './parts';

/**
 * 点名 · 进入剧情：ForceCastPage.xaml。
 * 角色库与地图都来自后端，前端拿不到任何角色，所以这里停在源码的空状态；
 * 勾选、选场景、点名的提交同样需要那两份数据。
 */
export function Forcecast({ back }: ScreenProps) {
  const [status, setStatus] = useState('勾选要点名的角色，并为 TA 指定初始位置');

  return (
    <PageShell>
      <PageHeader title="点名 · 进入剧情" onBack={back} right={<span className="cap">{status}</span>} />

      <div className="screen" style={{ minHeight: 'clamp(200px, 44vh, 420px)', justifyContent: 'center' }}>
        <Empty title="角色库还是空的" reason="先创建几个角色，再来点名吧" />
      </div>

      <FootBar>
        <div style={{ width: '100%' }}>
          <Actions>
            <SignalButton onClick={() => setStatus('还没有勾选任何角色。')}>确定点名 · 进入剧情</SignalButton>
          </Actions>
        </div>
      </FootBar>
    </PageShell>
  );
}
