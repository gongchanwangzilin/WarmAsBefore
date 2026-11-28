import { useEffect, useState } from 'react';
import { GlassButton } from '../../components/GlassButton';
import { GlassSwitch } from '../../components/GlassSwitch';
import { PageShell } from '../../components/PageShell';
import { Card, Empty, PageHeader, TextBtn } from '../../components/kit';
import type { ScreenProps } from '../types';

/**
 * AI 模型管理：Views/Settings/AiModelsPage.xaml（307 行）+ ViewModels/Settings/AiModelsViewModel.cs。
 * 列表与详情同页切换；每款模型单独配上下文窗口与能力，预设一键批量设置。
 * 两者都来自 ai.providers（WebBridge.AiProviders）：这一页只读已经存下的模型与能力。
 */

type ModelRow = {
  id: string;
  model: string;
  label: string;
  enabled: boolean;
  contextWindow: number;
  tools: boolean;
  json: boolean;
  vision: boolean;
};

type ProviderRow = { name: string; endpoint: string; enabled: boolean; models: ModelRow[] };

const displayNameOf = (row: ModelRow) =>
  row.label.trim() ? row.label.trim() : row.model.trim() ? row.model.trim() : '（未填模型）';

/** AiModelRow.CapabilityText：上下文窗口 + 已有的能力 + 停用标记。 */
const capabilityTextOf = (row: ModelRow) => {
  const parts: string[] = [];
  if (row.contextWindow > 0) parts.push(`${Math.round((row.contextWindow / 1000) * 10) / 10}K 上下文`);
  if (row.tools) parts.push('工具');
  if (row.json) parts.push('JSON');
  if (row.vision) parts.push('识图');
  if (!row.enabled) parts.push('已停用');
  return parts.length === 0 ? '未标注能力' : parts.join(' · ');
};

const providerTitleOf = (row: ProviderRow) => (row.name.trim() ? row.name.trim() : '（未命名厂商）');
const modelCountOf = (row: ProviderRow) =>
  `${row.models.filter((model) => model.enabled).length}/${row.models.length} 款启用`;

const CAPTION = { fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)' } as const;
const BODY_SM = { fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)' } as const;

/** 一行「标题 + 右侧开关」，对应源码里 *,Auto 的 Grid。开关只反映能力值。 */
function ToggleLine({ title, checked }: { title: string; checked: boolean }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
      <span style={BODY_SM}>{title}</span>
      <GlassSwitch label={title} checked={checked} onChange={() => undefined} disabled />
    </div>
  );
}

/** 一行「左侧标签 + 右侧值」，对应源码里 88,* 的 Grid（只读，所以不是 Entry）。 */
function ValueLine({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: 'grid', gridTemplateColumns: '88px 1fr', alignItems: 'center', gap: 'var(--w-space-3)' }}>
      <span style={BODY_SM}>{label}</span>
      <span style={{ fontSize: 'var(--w-size-body)', color: 'var(--w-ink-display)' }}>{value}</span>
    </div>
  );
}

export function AiModels({ bridge, navigate, back }: ScreenProps) {
  const [providers, setProviders] = useState<ProviderRow[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  useEffect(() => {
    let dropped = false;
    bridge
      .request('ai.providers', {})
      .then(({ providers: rows }) => {
        if (dropped) return;
        setProviders(
          rows.map((provider, providerIndex) => ({
            name: provider.name,
            endpoint: provider.endpoint,
            enabled: provider.enabled,
            models: provider.models.map((model, modelIndex) => ({
              id: `${providerIndex}-${modelIndex}`,
              model: model.model,
              label: model.label,
              enabled: model.enabled,
              contextWindow: model.contextWindow,
              tools: model.tools,
              json: model.json,
              vision: model.vision,
            })),
          })),
        );
      })
      .catch(() => undefined);
    return () => {
      dropped = true;
    };
  }, [bridge]);

  const selected = providers.flatMap((provider) => provider.models).find((model) => model.id === selectedId) ?? null;

  const header = (
    <PageHeader
      title="AI 模型管理"
      subtitle="每款模型单独配上下文窗口与能力；这一页只读——改动要到宿主里做。"
      onBack={selected ? () => setSelectedId(null) : back}
      backText={selected ? '返回列表' : '返回'}
      right={
        selected ? null : (
          <GlassButton size="sm" onClick={() => navigate('/aiProviders')}>
            厂商管理
          </GlassButton>
        )
      }
    />
  );

  if (selected) {
    return (
      <PageShell>
        {header}
        <div className="screen screen--narrow">
          <p style={{ margin: 0, fontSize: 'var(--w-size-subhead)', color: 'var(--w-ink-display)' }}>
            {displayNameOf(selected)}
          </p>

          <Card title="基础">
            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
              <ValueLine label="模型 id" value={selected.model} />
              <ValueLine label="显示名" value={selected.label} />
              <ValueLine
                label="上下文窗口"
                value={selected.contextWindow > 0 ? String(selected.contextWindow) : ''}
              />
              <p style={{ margin: 0, ...CAPTION }}>留空 = 用全局上限；填了按 70% 折叠历史，为输出留空间。</p>
            </div>
          </Card>

          <Card title="文本与工具">
            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
              <ToggleLine title="工具（function calling）" checked={selected.tools} />
              <ToggleLine title="JSON 结构化输出" checked={selected.json} />
            </div>
          </Card>

          <Card title="图像">
            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
              <ToggleLine title="识图（看图理解）" checked={selected.vision} />
            </div>
          </Card>
        </div>
      </PageShell>
    );
  }

  return (
    <PageShell>
      {header}
      <div className="screen screen--narrow">
        {providers.map((provider, providerIndex) => (
          <Card key={`${provider.endpoint}-${providerIndex}`}>
            <div style={{ display: 'flex', alignItems: 'flex-start', gap: 'var(--w-space-3)' }}>
              <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: '2px' }}>
                <span
                  style={{
                    fontSize: 'var(--w-size-body)',
                    fontWeight: 700,
                    color: 'var(--w-ink-display)',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                  }}
                >
                  {providerTitleOf(provider)}
                </span>
                <span style={{ ...CAPTION, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {provider.endpoint}
                </span>
                <span style={CAPTION}>{modelCountOf(provider)}</span>
              </div>
              <GlassSwitch
                label={`${providerTitleOf(provider)} 启用`}
                checked={provider.enabled}
                onChange={() => undefined}
                disabled
              />
            </div>

            <div
              style={{
                display: 'flex',
                flexDirection: 'column',
                gap: 'var(--w-space-3)',
                marginTop: 'var(--w-space-3)',
              }}
            >
              {provider.models.map((model) => (
                <Card key={model.id} tight>
                  <div style={{ display: 'flex', alignItems: 'flex-start', gap: 'var(--w-space-3)' }}>
                    <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: '2px' }}>
                      <span
                        style={{
                          fontSize: 'var(--w-size-small)',
                          fontWeight: 700,
                          color: 'var(--w-ink-display)',
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                          whiteSpace: 'nowrap',
                        }}
                      >
                        {displayNameOf(model)}
                      </span>
                      <span style={{ ...CAPTION, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                        {capabilityTextOf(model)}
                      </span>
                    </div>
                    <GlassSwitch
                      label={`${displayNameOf(model)} 启用`}
                      checked={model.enabled}
                      onChange={() => undefined}
                      disabled
                    />
                  </div>
                  <div
                    style={{
                      display: 'flex',
                      flexWrap: 'wrap',
                      alignItems: 'center',
                      gap: 'var(--w-space-2)',
                      marginTop: 'var(--w-space-2)',
                    }}
                  >
                    <TextBtn onClick={() => setSelectedId(model.id)}>编辑</TextBtn>
                  </div>
                </Card>
              ))}
            </div>
          </Card>
        ))}

        {providers.length === 0 ? (
          <Empty
            title="还没有厂商"
            reason="模型都挂在厂商下面，厂商列表是空的，所以这里也没有模型可配。"
            hint="到「厂商管理」补端点与密钥。"
          />
        ) : null}

        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-start', gap: 'var(--w-space-2)' }}>
          <TextBtn onClick={() => navigate('/modelDeployment')}>模型部署向导</TextBtn>
          <p style={{ margin: 0, ...CAPTION }}>端点与密钥在「厂商管理」维护；这里只管模型与能力。</p>
        </div>
      </div>
    </PageShell>
  );
}
