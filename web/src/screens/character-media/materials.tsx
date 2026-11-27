import { useState } from 'react';
import { PageShell } from '../../components/PageShell';
import { PageHeader } from '../../components/kit';
import type { ScreenProps } from '../types';
import { PrimarySm, Well } from './parts';

type LibTab = 'backgrounds' | 'music';

const TAB_BASE = {
  minHeight: 34,
  padding: '10px 6px',
  border: 0,
  borderRadius: 'var(--w-radius-button)',
  font: 'inherit',
  fontSize: 'var(--w-size-small)',
  cursor: 'pointer',
} as const;

/**
 * 素材库（MaterialsPage.xaml，202 行）：背景库 / 音乐库两页签 + 三列网格 + 导入。
 * 两个目录的扫描结果来自后端（这里没有），所以按源码的两个空态渲染；
 * 页签切换与页头右侧的「背景 · 音乐」是本地状态。
 */
export function Materials({ back }: ScreenProps) {
  const [tab, setTab] = useState<LibTab>('backgrounds');
  const isMusic = tab === 'music';

  return (
    <PageShell>
      <PageHeader
        title="素材库"
        onBack={back}
        right={
          <span
            style={{
              padding: '5px 10px',
              borderRadius: '99px',
              background: 'var(--w-surface-tint)',
              fontSize: 'var(--w-size-small)',
              color: 'var(--w-ink-display)',
            }}
          >
            背景 · 音乐
          </span>
        }
      />

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--w-space-2)', padding: '16px var(--w-pad-card)' }}>
        <button
          type="button"
          aria-pressed={!isMusic}
          onClick={() => setTab('backgrounds')}
          style={{
            ...TAB_BASE,
            background: isMusic ? 'transparent' : 'var(--w-surface-tint)',
            color: isMusic ? 'var(--w-ink-body)' : 'var(--w-ink-display)',
            fontWeight: isMusic ? 400 : 700,
          }}
        >
          背景库
        </button>
        <button
          type="button"
          aria-pressed={isMusic}
          onClick={() => setTab('music')}
          style={{
            ...TAB_BASE,
            background: isMusic ? 'var(--w-surface-tint)' : 'transparent',
            color: isMusic ? 'var(--w-ink-display)' : 'var(--w-ink-body)',
            fontWeight: isMusic ? 700 : 400,
          }}
        >
          音乐库
        </button>
      </div>

      <div className="screen">
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            gap: 'var(--w-space-3)',
            padding: 32,
            minHeight: 320,
          }}
        >
          <Well width={96} height={96} radius="99px" />
          <h2 style={{ margin: 0, fontSize: 'var(--w-size-title)', fontWeight: 600, color: 'var(--w-ink-display)' }}>
            {isMusic ? '音乐库还是空的' : '背景库还是空的'}
          </h2>
          {isMusic ? (
            <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-faint)' }}>
              导入音乐后可设为左键音/右键音/按键音/背景音
            </span>
          ) : (
            <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-faint)' }}>
              导入 JPG / PNG / MP4 背景，可在地图里选用
            </span>
          )}
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--w-space-3)', padding: 'var(--w-pad-card)' }}>
        {isMusic ? <span /> : <PrimarySm>导入背景</PrimarySm>}
        {isMusic ? <PrimarySm>导入音乐</PrimarySm> : <span />}
      </div>
    </PageShell>
  );
}
