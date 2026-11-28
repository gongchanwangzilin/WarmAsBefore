import { useEffect, useState } from 'react';
import { Card, Field, PageHeader, Row, Sep, TextBtn } from '../../components/kit';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import type { ScreenProps } from '../types';
import { Actions, Inert, Note, SectionTitle, Stack } from './parts';

type ShowcaseRow = { id: string; name: string; characterName: string; steps: number };

/**
 * 编辑展示案：ShowcaseEditPage.xaml。
 * 从 ?id= 取一案，回显名称、主角与步骤数；名称改动只留在这一页。
 * 这一页没有返回钮（退出靠底部的「返回」），源码如此。
 */
export function ShowcaseEdit({ bridge, query, back }: ScreenProps) {
  const [script, setScript] = useState<ShowcaseRow | null>(null);
  const [name, setName] = useState('');
  const id = query.get('id');

  useEffect(() => {
    let alive = true;
    bridge
      .request('showcases.list', {})
      .then((result) => {
        if (!alive) return;
        const hit = id ? result.showcases.find((showcase) => showcase.id === id) ?? null : null;
        setScript(
          hit ? { id: hit.id, name: hit.name, characterName: hit.characterName, steps: hit.steps } : null,
        );
        if (hit) setName(hit.name);
      })
      .catch(() => undefined);
    return () => {
      alive = false;
    };
  }, [bridge, id]);

  const steps = script?.steps ?? 0;
  const characterName = script?.characterName.trim() ? script.characterName : '（未选择）';

  return (
    <PageShell>
      <PageHeader
        title="编辑展示案"
        right={
          <Inert>
            <TextBtn>保存</TextBtn>
          </Inert>
        }
      />

      <div className="screen" style={{ maxWidth: 760 }}>
        <Stack gap={3}>
          <SectionTitle>基本信息</SectionTitle>
          <Card>
            <Row title="名称" right={<div style={{ width: 'min(280px, 52vw)' }}><Field label="名称" value={name} onChange={setName} /></div>} />
            <Sep />
            <Row
              title="主角"
              desc={characterName}
              right={
                <Inert>
                  <TextBtn>选择角色</TextBtn>
                </Inert>
              }
            />
          </Card>

          <SectionTitle>步骤列表</SectionTitle>
          <Card>
            <div style={{ maxHeight: 260, overflowY: 'auto' }}>
              {steps > 0 ? (
                <Row title={`步骤 ${steps}`} />
              ) : (
                <Note>暂无步骤，点击下方按钮添加</Note>
              )}
            </div>
          </Card>

          <SectionTitle>添加步骤</SectionTitle>
          <Card>
            <Actions>
              <Inert>
                <TextBtn>添加对话</TextBtn>
              </Inert>
              <Inert>
                <TextBtn>插入CG</TextBtn>
              </Inert>
            </Actions>
            <Sep />
            <Actions>
              <Inert>
                <TextBtn>添加立绘</TextBtn>
              </Inert>
              <Inert>
                <TextBtn>清空立绘</TextBtn>
              </Inert>
            </Actions>
            <Sep />
            <Actions>
              <Inert>
                <TextBtn>导入临时素材</TextBtn>
              </Inert>
            </Actions>
          </Card>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr' }}>
            <GlassButton size="md" onClick={back}>返回</GlassButton>
          </div>
        </Stack>
      </div>
    </PageShell>
  );
}
