import { useEffect, useState } from 'react';
import { Card, Empty, PageHeader, Row, Sep, TextBtn } from '../../components/kit';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import type { ScreenProps } from '../types';
import { Stack } from './parts';

/** novels.list 的一行。 */
type NovelRow = {
  id: string;
  title: string;
  summary: string;
  characters: string[];
  branches: number;
  importedAt: string;
};

/** 导入时间只要月日，与源码的 `{0:MM/dd}` 同档。 */
function monthDayOf(iso: string): string {
  const at = new Date(iso);
  if (Number.isNaN(at.getTime())) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${pad(at.getMonth() + 1)}/${pad(at.getDate())}`;
}

/** 摘要一行两行封顶：源码是 MaxLines=2 + TailTruncation。 */
function summaryOf(summary: string): string {
  const text = summary.trim().replace(/\n/g, ' ');
  return text.length <= 90 ? text : `${text.slice(0, 90)}…`;
}

/**
 * 平行宇宙：NovelSelectPage.xaml。
 * 一部小说一行，列表读 novels.list；进入世界带走它的 id。
 */
export function NovelSelect({ bridge, navigate, back }: ScreenProps) {
  const [novels, setNovels] = useState<NovelRow[]>([]);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    let alive = true;
    bridge
      .request('novels.list', {})
      .then((result) => {
        if (!alive) return;
        setNovels(
          result.novels.map((novel) => ({
            id: novel.id,
            title: novel.title,
            summary: novel.summary,
            characters: novel.characters,
            branches: novel.branches,
            importedAt: novel.importedAt,
          })),
        );
        setLoaded(true);
      })
      .catch(() => {
        if (alive) setLoaded(true);
      });
    return () => {
      alive = false;
    };
  }, [bridge]);

  return (
    <PageShell>
      <PageHeader title="平行宇宙" onBack={back} />

      <div className="screen" style={{ minHeight: 360 }}>
        <Card>
          {novels.map((novel) => {
            const summary = summaryOf(novel.summary);
            return (
              <div key={novel.id}>
                <Row
                  title={
                    <span style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
                      <span>{novel.title}</span>
                      <span className="cap">{monthDayOf(novel.importedAt)}</span>
                    </span>
                  }
                  desc={
                    <span style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                      {summary ? <span>{summary}</span> : null}
                      <span className="cap">
                        {novel.characters.length > 0 ? `角色：${novel.characters.join('、')}` : '角色：—'}
                      </span>
                      <span className="cap">{`分支：${novel.branches}`}</span>
                    </span>
                  }
                  right={
                    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 'var(--w-space-2)' }}>
                      <GlassButton size="sm" onClick={() => navigate(`/novelworld?id=${encodeURIComponent(novel.id)}`)}>
                        进入世界
                      </GlassButton>
                      <TextBtn>删除</TextBtn>
                    </div>
                  }
                />
                <Sep />
              </div>
            );
          })}
          {loaded && novels.length === 0 ? (
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', minHeight: 260 }}>
              <Empty title="还没有平行宇宙" reason="导入一部小说，它就会变成一个可以进入的世界" />
            </div>
          ) : null}
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
