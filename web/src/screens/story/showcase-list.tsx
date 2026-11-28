import { useEffect, useState } from 'react';
import { Card, Empty, PageHeader, Row, Sep, TextBtn } from '../../components/kit';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import type { ScreenProps } from '../types';
import { Actions, Stack } from './parts';

/** showcases.list 的一行（后端已按 UpdatedAt 倒序）。 */
type ShowcaseRow = {
  id: string;
  name: string;
  characterName: string;
  steps: number;
  updatedAt: string;
};

/** 更新时间只读到日子：`2026-11-20`。 */
function dayOf(iso: string): string {
  const at = new Date(iso);
  if (Number.isNaN(at.getTime())) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${at.getFullYear()}-${pad(at.getMonth() + 1)}-${pad(at.getDate())}`;
}

/**
 * 开发者展示模式：ShowcaseListPage.xaml。
 * 列表读 showcases.list；播放与编辑各自带走这个案的 id。
 */
export function ShowcaseList({ bridge, navigate, back }: ScreenProps) {
  const [scripts, setScripts] = useState<ShowcaseRow[]>([]);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    let alive = true;
    bridge
      .request('showcases.list', {})
      .then((result) => {
        if (!alive) return;
        setScripts(
          result.showcases.map((showcase) => ({
            id: showcase.id,
            name: showcase.name,
            characterName: showcase.characterName,
            steps: showcase.steps,
            updatedAt: showcase.updatedAt,
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

  const edit = (script: ShowcaseRow) => navigate(`/showcase-edit?id=${encodeURIComponent(script.id)}`);

  const play = (script: ShowcaseRow) => {
    if (script.steps === 0) {
      window.alert('该展示案还没有任何步骤，无法播放。');
      return;
    }
    navigate(`/showcase-play?id=${encodeURIComponent(script.id)}`);
  };

  return (
    <PageShell>
      <PageHeader title="开发者展示模式" onBack={back} />

      <div className="screen">
        <Card>
          {scripts.map((script) => {
            const day = dayOf(script.updatedAt);
            return (
              <div key={script.id}>
                <Row
                  title={script.name}
                  desc={
                    <span style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                      {script.characterName ? <span>{script.characterName}</span> : null}
                      <span className="cap">{`步骤 ${script.steps}`}</span>
                      {day ? <span className="cap">{`更新 ${day}`}</span> : null}
                    </span>
                  }
                  right={
                    <Actions>
                      <TextBtn onClick={() => edit(script)}>编辑</TextBtn>
                      <TextBtn tone="signal" onClick={() => play(script)}>▶</TextBtn>
                      <TextBtn>×</TextBtn>
                    </Actions>
                  }
                />
                <Sep />
              </div>
            );
          })}
          {loaded && scripts.length === 0 ? (
            <Empty title="还没有展示案" reason="新建一段演出，它会出现在这个列表里" />
          ) : null}
        </Card>

        <Stack>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr' }}>
            <GlassButton size="md" onClick={() => navigate('/showcase-edit')}>＋ 新建展示案</GlassButton>
          </div>
        </Stack>
      </div>
    </PageShell>
  );
}
