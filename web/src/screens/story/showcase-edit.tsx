import { useState } from 'react';
import { Card, Field, PageHeader, Row, Sep, TextBtn } from '../../components/kit';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import type { ScreenProps } from '../types';
import { Actions, Note, SectionTitle, Stack } from './parts';

/**
 * 编辑展示案：ShowcaseEditPage.xaml。
 * 展示案数据在 ShowcaseStore 里，前端只留名称与页内步骤；
 * 选角色、插 CG、加立绘都要读角色库与素材库，这里保留按钮与空态。
 */
export function ShowcaseEdit({ query, back }: ScreenProps) {
  const [name, setName] = useState(query.get('name') ?? '');
  const [steps] = useState<string[]>([]);

  const save = () => window.alert('展示案已保存');

  return (
    <PageShell>
      <PageHeader
        onBack={back}
        title="编辑展示案"
        right={<TextBtn onClick={save}>保存</TextBtn>}
      />

      <div className="screen" style={{ maxWidth: 760 }}>
        <Stack gap={3}>
          <SectionTitle>基本信息</SectionTitle>
          <Card>
            <Row title="名称" right={<div style={{ width: 280 }}><Field label="名称" value={name} onChange={setName} /></div>} />
            <Sep />
            <Row
              title="主角"
              desc="（未选择）"
              right={<TextBtn>选择角色</TextBtn>}
            />
          </Card>

          <SectionTitle>步骤列表</SectionTitle>
          <Card>
            <div style={{ maxHeight: 260, overflowY: 'auto' }}>
              {steps.map((step) => (
                <div key={step}>
                  <Row title={step} right={<Actions><TextBtn>↑</TextBtn><TextBtn>↓</TextBtn><TextBtn>×</TextBtn></Actions>} />
                  <Sep />
                </div>
              ))}
              {steps.length === 0 ? <Note>暂无步骤，点击下方按钮添加</Note> : null}
            </div>
          </Card>

          <SectionTitle>添加步骤</SectionTitle>
          <Card>
            <Actions>
              <TextBtn>添加对话</TextBtn>
              <TextBtn>插入CG</TextBtn>
            </Actions>
            <Sep />
            <Actions>
              <TextBtn>添加立绘</TextBtn>
              <TextBtn>清空立绘</TextBtn>
            </Actions>
            <Sep />
            <Actions>
              <TextBtn>导入临时素材</TextBtn>
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
