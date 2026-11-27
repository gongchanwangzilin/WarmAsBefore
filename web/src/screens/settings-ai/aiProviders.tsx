import { useState } from 'react';
import { GlassButton } from '../../components/GlassButton';
import { GlassSwitch } from '../../components/GlassSwitch';
import { PageShell } from '../../components/PageShell';
import { Card, Empty, Field, PageHeader, TextBtn } from '../../components/kit';
import type { ScreenProps } from '../types';

/**
 * AI 厂商管理：Views/Settings/AiProvidersPage.xaml（111 行）+ 同名 code-behind。
 * 一家厂商 = 一个端点 + 一组密钥 + 多款模型；顺序即优先级。
 */

type ProviderModel = { model: string; label: string; enabled: boolean };

type Provider = {
  id: string;
  name: string;
  url: string;
  keys: string[];
  enabled: boolean;
  timeout: string;
  models: ProviderModel[];
};

/** AiProviderRow.SplitKeys：换行、英文/中文逗号、分号都算分隔符。 */
const splitKeys = (text: string) =>
  text
    .split(/[\n\r,，;；]/)
    .map((part) => part.trim())
    .filter((part) => part.length > 0);

const titleOf = (provider: Provider) => (provider.name.trim() ? provider.name : '（未命名厂商）');

const parseTimeout = (text: string) => {
  const value = Number.parseInt(text.trim(), 10);
  return Number.isFinite(value) && value > 0 ? Math.min(600, Math.max(3, value)) : null;
};

const summaryOf = (provider: Provider) => {
  const on = provider.models.filter((model) => model.enabled).length;
  const timeout = parseTimeout(provider.timeout);
  return `${on}/${provider.models.length} 款模型 · ${provider.keys.length} 枚密钥${timeout ? ` · 超时 ${timeout}s` : ''}`;
};

const modelsHintOf = (provider: Provider) =>
  provider.models.length === 0
    ? '还没有模型：到「模型管理」里加一款。'
    : '模型：' +
      provider.models
        .map((model) => (model.label.trim() ? model.label : model.model) + (model.enabled ? '' : '（停用）'))
        .join('、');

const rowAction = { marginRight: 'var(--w-space-2)' } as const;

export function AiProviders({ navigate, back }: ScreenProps) {
  const [providers, setProviders] = useState<Provider[]>([]);
  const [newName, setNewName] = useState('');
  const [newModel, setNewModel] = useState('');
  const [newUrl, setNewUrl] = useState('');
  const [newKeys, setNewKeys] = useState('');
  const [newTimeout, setNewTimeout] = useState('');
  const [status, setStatus] = useState('');

  const patch = (id: string, next: Partial<Provider>) =>
    setProviders((list) => list.map((item) => (item.id === id ? { ...item, ...next } : item)));

  const move = (id: string, delta: number) =>
    setProviders((list) => {
      const index = list.findIndex((item) => item.id === id);
      const target = index + delta;
      if (index < 0 || target < 0 || target >= list.length) return list;
      const copy = list.slice();
      const [row] = copy.splice(index, 1);
      copy.splice(target, 0, row);
      return copy;
    });

  const editBasics = (provider: Provider) => {
    const name = window.prompt('厂商名称\n好认的名字，如「硅基流动」「DeepSeek 官方」。', provider.name);
    if (name === null) return;
    const url = window.prompt('端点地址\nOpenAI 兼容的 chat/completions 地址。', provider.url);
    if (url === null) return;
    patch(provider.id, { name: name.trim(), url: url.trim() });
    setStatus('厂商信息已保存。');
  };

  const editKeys = (provider: Provider) => {
    const keys = window.prompt(
      `「${titleOf(provider)}」的密钥\n一行一枚，可多枚轮流用（也可用逗号分隔）。`,
      provider.keys.join('\n'),
    );
    if (keys === null) return;
    patch(provider.id, { keys: splitKeys(keys) });
    setStatus('密钥已保存。');
  };

  const editTimeout = (provider: Provider) => {
    const timeout = window.prompt(
      `「${titleOf(provider)}」的超时\n秒数（3~600），留空 = 用全局超时。`,
      provider.timeout,
    );
    if (timeout === null) return;
    patch(provider.id, { timeout: timeout.trim() });
    setStatus('超时已保存。');
  };

  const remove = (provider: Provider) => {
    if (!window.confirm(`删除厂商\n删掉「${titleOf(provider)}」？它下面的模型也一起没了。`)) return;
    setProviders((list) => list.filter((item) => item.id !== provider.id));
    setStatus('已删除。');
  };

  const addProvider = () => {
    const url = newUrl.trim();
    if (url.length === 0) {
      setStatus('先填端点地址（OpenAI 兼容），再点「添加厂商」。');
      return;
    }
    const model = newModel.trim();
    const row: Provider = {
      id: String(Math.random()).slice(2, 10),
      name: newName.trim(),
      url,
      keys: splitKeys(newKeys),
      enabled: true,
      timeout: newTimeout.trim(),
      models: model.length > 0 ? [{ model, label: '', enabled: true }] : [],
    };
    setProviders((list) => [...list, row]);
    setNewName('');
    setNewUrl('');
    setNewKeys('');
    setNewModel('');
    setNewTimeout('');
    setStatus('已添加厂商。到「模型管理」里补上下文窗口和能力。');
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
        {providers.map((provider) => (
          <Card key={provider.id}>
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
                  style={{
                    fontSize: 'var(--w-size-caption)',
                    color: 'var(--w-ink-muted)',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                  }}
                >
                  {provider.url}
                </span>
                <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)' }}>
                  {summaryOf(provider)}
                </span>
              </div>
              <GlassSwitch
                label={`${titleOf(provider)} 启用`}
                checked={provider.enabled}
                onChange={(enabled) => {
                  patch(provider.id, { enabled });
                  setStatus(enabled ? '已启用这家厂商。' : '已停用这家厂商：轮换池里不再用它。');
                }}
              />
            </div>

            <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', marginTop: 'var(--w-space-2)' }}>
              <span style={rowAction}>
                <TextBtn onClick={() => editBasics(provider)}>名称/端点</TextBtn>
              </span>
              <span style={rowAction}>
                <TextBtn onClick={() => editKeys(provider)}>密钥</TextBtn>
              </span>
              <span style={rowAction}>
                <TextBtn onClick={() => editTimeout(provider)}>超时</TextBtn>
              </span>
              <span style={rowAction}>
                <TextBtn
                  onClick={() => {
                    move(provider.id, -1);
                    setStatus('顺序已调整：靠前的先用。');
                  }}
                >
                  上移
                </TextBtn>
              </span>
              <span style={rowAction}>
                <TextBtn
                  onClick={() => {
                    move(provider.id, 1);
                    setStatus('顺序已调整。');
                  }}
                >
                  下移
                </TextBtn>
              </span>
              <TextBtn onClick={() => remove(provider)}>删除</TextBtn>
            </div>

            <p
              style={{
                margin: 'var(--w-space-2) 0 0',
                fontSize: 'var(--w-size-caption)',
                color: 'var(--w-ink-muted)',
              }}
            >
              {modelsHintOf(provider)}
            </p>
          </Card>
        ))}

        {providers.length === 0 ? (
          <Empty
            title="还没有厂商"
            reason="轮换池里一家都没有：对话端点还没接上。"
            hint="在下面「新增厂商」里填端点、密钥和首款模型，点「＋ 添加厂商」就会出现在上面。"
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

        {status ? (
          <p style={{ margin: 0, fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)' }}>{status}</p>
        ) : null}
      </div>
    </PageShell>
  );
}
