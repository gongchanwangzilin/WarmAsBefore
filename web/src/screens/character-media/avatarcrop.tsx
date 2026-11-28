import { useState } from 'react';
import { PageShell } from '../../components/PageShell';
import { PageHeader } from '../../components/kit';
import type { ScreenProps } from '../types';
import { GhostSm, PrimarySm, TextSm } from './parts';

const RULE = { position: 'absolute', background: 'var(--w-hairline)' } as const;

/**
 * 头像裁剪（AvatarCropPage.xaml，119 行）：页头 + 正方形取景框 + 缩放条 + 底部取消/确认。
 * 原图要后端给（这里没有），所以取景框里是空框、缩放条按 HasImage 隐藏；
 * 三分线与「隐藏网格」是本地状态，「取消」走返回。
 */
export function Avatarcrop({ back }: ScreenProps) {
  const [showGrid, setShowGrid] = useState(true);

  return (
    <PageShell>
      <div style={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
        <PageHeader title="裁剪头像" onBack={back} right={<GhostSm onClick={() => setShowGrid(true)}>↺ 重置</GhostSm>} />

        <div style={{ position: 'relative', flex: 1, minHeight: 420, background: 'var(--w-bg)', overflow: 'hidden' }}>
          <div style={{ position: 'absolute', inset: 0, background: 'var(--w-surface-tint)', opacity: 0.4 }} />

          <div
            style={{
              position: 'absolute',
              left: '50%',
              top: '50%',
              transform: 'translate(-50%, -50%)',
              width: 'min(300px, calc(100% - 32px))',
              aspectRatio: '1',
              overflow: 'hidden',
              background: 'var(--w-bg)',
              border: '1px solid var(--w-hairline)',
              borderRadius: 2,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)' }}>头像</span>

            {showGrid ? (
              <div style={{ position: 'absolute', inset: 0, pointerEvents: 'none' }} aria-hidden="true">
                <i style={{ ...RULE, left: '33.33%', top: 0, bottom: 0, width: 1 }} />
                <i style={{ ...RULE, left: '66.66%', top: 0, bottom: 0, width: 1 }} />
                <i style={{ ...RULE, top: '33.33%', left: 0, right: 0, height: 1 }} />
                <i style={{ ...RULE, top: '66.66%', left: 0, right: 0, height: 1 }} />
              </div>
            ) : null}
          </div>
        </div>

        <div style={{ padding: '10px 20px 4px', display: 'flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
          <GhostSm onClick={() => setShowGrid((value) => !value)} style={{ height: 32 }}>
            {showGrid ? '隐藏网格' : '显示网格'}
          </GhostSm>
          <span style={{ flex: 1, textAlign: 'center', fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)' }}>
            原图由宿主给：这一版还没有取图与缩放通路
          </span>
        </div>

        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'auto 1fr auto',
            alignItems: 'center',
            gap: 'var(--w-space-3)',
            padding: '10px 16px 14px',
            background: 'var(--w-surface-tint)',
          }}
        >
          <TextSm onClick={back}>取消</TextSm>
          <span
            style={{
              textAlign: 'center',
              fontSize: 'var(--w-size-caption)',
              color: 'var(--w-ink-muted)',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
            }}
          >
            TA
          </span>
          <PrimarySm disabled>确认</PrimarySm>
        </div>
      </div>
    </PageShell>
  );
}
