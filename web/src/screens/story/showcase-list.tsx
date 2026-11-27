import { useState } from 'react';
import { Card, PageHeader, Row, Sep, TextBtn } from '../../components/kit';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import type { ScreenProps } from '../types';
import { Actions, Stack } from './parts';

type ScriptRow = { id: string; name: string; characterName: string; steps: number };

/**
 * 开发者展示模式：ShowcaseListPage.xaml。
 * 展示案存在 ShowcaseStore 里，没有前端 method；新建/删除只改页内状态，
 * 编辑与播放在没有案可载入时各自停在空档。
 */
export function ShowcaseList({ navigate, back }: ScreenProps) {
  const [scripts, setScripts] = useState<ScriptRow[]>([]);

  const create = () => {
    const script: ScriptRow = { id: `s${Date.now()}`, name: '我的第一段演出', characterName: '', steps: 0 };
    setScripts((list) => [script, ...list]);
    navigate(`/showcase-edit?name=${encodeURIComponent(script.name)}`);
  };

  const play = (script: ScriptRow) => {
    if (script.steps === 0) {
      window.alert('该展示案还没有任何步骤，无法播放。');
      return;
    }
    navigate('/showcase-play');
  };

  const remove = (script: ScriptRow) => {
    if (!window.confirm(`确定删除「${script.name}」？`)) return;
    setScripts((list) => list.filter((s) => s.id !== script.id));
  };

  return (
    <PageShell>
      <PageHeader title="开发者展示模式" onBack={back} />

      <div className="screen">
        <Card>
          {scripts.map((script) => (
            <div key={script.id}>
              <Row
                title={script.name}
                desc={
                  <span style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                    <span>{script.characterName}</span>
                    <span className="cap">{`步骤 ${script.steps}`}</span>
                  </span>
                }
                right={
                  <Actions>
                    <TextBtn onClick={() => navigate(`/showcase-edit?name=${encodeURIComponent(script.name)}`)}>
                      编辑
                    </TextBtn>
                    <TextBtn onClick={() => play(script)}>▶</TextBtn>
                    <TextBtn onClick={() => remove(script)}>×</TextBtn>
                  </Actions>
                }
              />
              <Sep />
            </div>
          ))}
        </Card>

        <Stack>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr' }}>
            <GlassButton size="md" onClick={create}>＋ 新建展示案</GlassButton>
          </div>
        </Stack>
      </div>
    </PageShell>
  );
}
