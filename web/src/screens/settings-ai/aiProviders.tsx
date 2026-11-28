import { useEffect, useState } from 'react';
import { GlassButton } from '../../components/GlassButton';
import { GlassSwitch } from '../../components/GlassSwitch';
import { PageShell } from '../../components/PageShell';
import { Card, Empty, Field, PageHeader } from '../../components/kit';
import type { ScreenProps } from '../types';

/**
 * AI 厂商管理：Views/Settings/AiProvidersPage.xaml（111 行）+ 同名 code-behind。
 * 一家厂商 = 一个端点 + 一组密钥 + 多款模型；顺序即优先级，失灵自动换下一家。
 * 列表来自 ai.providers（WebBridge.AiProviders）：只读已经存下的厂商与模型，不含密钥本身。
 */

type ProviderModel = {
  model: string;
  label: string;
  enabled: boolean;
  vision: boolean;
  tools: boolean;
  json: boolean;
  contextWindow: number;
};

type Provider = {
  name: string;
  endpoint: string;
  enabled: boolean;
  timeoutSeconds: number;
  keyCount: number;
  models: ProviderModel[];
};

const CAPTION = { fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)' } as const;

const titleOf = (provider: Provider) => (provider.name.trim() ? provider.name : '（未命名厂商）');

/** AiProviderRow.Summary：启用几款 / 共几款 · 几枚密钥 · 超时。 */
const summaryOf = (provider: Provider) => {
  const on = provider.models.filter((model) => model.enabled).length;
  const timeout = provider.timeoutSeconds > 0 ? ` · 超时 ${provider.timeoutSeconds}s` : '';
  return `${on}/${provider.models.length} 款模型 · ${provider.keyCount} 枚密钥${timeout}`;
};

/** AiProviderRow.ModelsHint：这家挂了哪些模型、谁被停用。 */
const modelsHintOf = (provider: Provider) =>
  provider.models.length === 0
    ? '还没有模型：到「模型管理」里加一款。'
    : '模型：' +
      provider.models
        .map((model) => (model.label.trim() ? model.label : model.model) + (model.enabled ? '' : '（停用）'))
        .join('、');

export function AiProviders({ bridge, navigate, back }: ScreenProps) {
  const [providers, setProviders] = useState<Provider[]>([]);
  const [newName, setNewName] = useState('');
  const [newModel, setNewModel] = useState('');
  const [newUrl, setNewUrl] = useState('');
  const [newKeys, setNewKeys] = useState('');
  const [newTimeout, setNewTimeout] = useState('');
  const [status, setStatus] = useState('');

  useEffect(() => {
    let dropped = false;
    bridge
      .request('ai.providers', {})
      .then(({ providers: rows }) => {
        if (!dropped) setProviders(rows);
      })
      .catch(() => undefined);
    return () => {
      dropped = true;
    };
  }, [bridge]);

  const addProvider = () => {
    if (newUrl.trim().length === 0) {
      setStatus('先填端点地址（OpenAI 兼容），再点「添加厂商」。');
      return;
    }
    setStatus('新增厂商还没有接通写入：这一页只显示已经存下的厂商与模型。');
  };

  return (
    <PageShell>
      <PageHeader
        title="AI 厂商管理"
        subtitle="一家厂商 = 一个端点 + 一组密钥 + 多款模型；顺序即优先级，失灵自动换下一家。"
        onBack={back}
        backText="←"
        right={
          <GlassButton size="sm" onClick={() => navigate('/aiModels')}>
            模型管理 →
          </GlassButton>
        }
      />

      <div className="screen">
        {providers.map((provider, index) => (
          <Card key={`${provider.endpoint}-${index}`}>
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
                  {titleOf(provider)}
                </span>
                <span
                  style={{ ...CAPTION, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
                >
                  {provider.endpoint}
                </span>
                <span style={CAPTION}>{summaryOf(provider)}</span>
              </div>
              <GlassSwitch
                label={`${titleOf(provider)} 启用`}
                checked={provider.enabled}
                onChange={() => undefined}
                disabled
              />
            </div>

            <p style={{ margin: 'var(--w-space-2) 0 0', ...CAPTION }}>{modelsHintOf(provider)}</p>
          </Card>
        ))}

        {providers.length === 0 ? (
          <Empty
            title="还没有厂商"
            reason="还没有接入厂商。"
            hint="填端点与密钥后点「＋添加厂商」，再往里加模型。"
          />
        ) : null}

        <Card title="新增厂商">
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--w-space-3)' }}>
              <Field value={newName} onChange={setNewName} placeholder="厂商名（如：硅基流动）" label="厂商名" />
              <Field
                value={newModel}
                onChange={setNewModel}
                placeholder="首款模型（如：deepseek-chat）"
                label="首款模型"
              />
            </div>
            <Field
              value={newUrl}
              onChange={setNewUrl}
              placeholder="端点（https://…/v1/chat/completions）"
              label="端点"
            />
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 110px', gap: 'var(--w-space-3)' }}>
              <Field
                value={newKeys}
                onChange={setNewKeys}
                placeholder="密钥（一行一枚，可多枚轮流用）"
                label="密钥"
              />
              <Field value={newTimeout} onChange={setNewTimeout} placeholder="超时秒" type="number" label="超时秒" />
            </div>
            <div>
              <GlassButton size="md" onClick={addProvider}>
                ＋ 添加厂商
              </GlassButton>
            </div>
          </div>
        </Card>

        {status ? <p style={{ margin: 0, ...CAPTION }}>{status}</p> : null}
      </div>
    </PageShell>
  );
}
