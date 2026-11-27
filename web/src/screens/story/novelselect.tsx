import { useState } from 'react';
import { Card, Empty, PageHeader } from '../../components/kit';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import type { ScreenProps } from '../types';
import { Stack } from './parts';

type NovelRow = { id: string; title: string; summary: string; importedAt: string; charactersLabel: string };

/**
 * 平行宇宙：NovelSelectPage.xaml。
 * 小说库要读目录并经 AI 解析，前端没有对应 method；从空库开始，
 * 导入按钮保留源码文案，导入本身停在原地。
 */
export function NovelSelect({ back }: ScreenProps) {
  const [novels] = useState<NovelRow[]>([]);

  return (
    <PageShell>
      <PageHeader title="平行宇宙" onBack={back} />

      <div className="screen" style={{ minHeight: 360 }}>
        <Card>
          {novels.length === 0 ? (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', minHeight: 260 }}>
              <Empty title="还没有平行宇宙" reason="导入一部小说，它就会变成一个可以进入的世界" />
            </div>
          ) : (
            novels.map((novel) => (
              <div key={novel.id} style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
                <span style={{ fontSize: 'var(--w-size-body)', color: 'var(--w-ink-display)' }}>{novel.title}</span>
                <span className="cap">{novel.summary}</span>
                <span className="cap">{novel.charactersLabel}</span>
              </div>
            ))
          )}
        </Card>

        <Stack>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr' }}>
            <GlassButton size="lg">导入小说，创建新世界</GlassButton>
          </div>
        </Stack>
      </div>
    </PageShell>
  );
}
