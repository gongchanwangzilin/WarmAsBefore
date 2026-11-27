import { useState } from 'react';
import { GlassButton } from '../../components/GlassButton';
import { GlassSlider } from '../../components/GlassSlider';
import { GlassSwitch } from '../../components/GlassSwitch';
import { PageShell } from '../../components/PageShell';
import { Card, Empty, Field, PageHeader, Sep, TextBtn } from '../../components/kit';
import type { ScreenProps } from '../types';

/**
 * AI 模型管理：Views/Settings/AiModelsPage.xaml（307 行）+ ViewModels/Settings/AiModelsViewModel.cs。
 * 列表与详情同页切换；每款模型单独配上下文窗口与能力，预设一键批量设置。
 */

type ModelRow = {
  id: string;
  model: string;
  label: string;
  enabled: boolean;
  contextWindow: string;
  supportsTools: boolean;
  supportsJson: boolean;
  supportsReasoning: boolean;
  supportsVision: boolean;
  supportsImageGen: boolean;
  supportsOcr: boolean;
  supportsAudioInput: boolean;
  supportsAudioOutput: boolean;
  supportsVideoInput: boolean;
  supportsVideoOutput: boolean;
  priority: number;
  showThinkTierBar: boolean;
  showThinkToggle: boolean;
  showQuickButtons: boolean;
};

type ProviderRow = { id: string; name: string; url: string; enabled: boolean; models: ModelRow[] };

type Preset = {
  name: string;
  hint: string;
  tools: boolean;
  json: boolean;
  vision: boolean;
  ocr: boolean;
  reasoning: boolean;
  imageGen: boolean;
  audioIn: boolean;
  audioOut: boolean;
  videoIn: boolean;
  videoOut: boolean;
};

const BUILTIN_PRESETS: Preset[] = [
  { name: '通用对话', hint: '工具 + JSON', tools: true, json: true, vision: false, ocr: false, reasoning: false, imageGen: false, audioIn: false, audioOut: false, videoIn: false, videoOut: false },
  { name: '视觉模型', hint: '识图 + OCR', tools: false, json: false, vision: true, ocr: true, reasoning: false, imageGen: false, audioIn: false, audioOut: false, videoIn: false, videoOut: false },
  { name: '全模态', hint: '十项能力全开', tools: true, json: true, vision: true, ocr: true, reasoning: true, imageGen: true, audioIn: true, audioOut: true, videoIn: true, videoOut: true },
  { name: '纯文本小模型', hint: '能力全关，做便宜兜底', tools: false, json: false, vision: false, ocr: false, reasoning: false, imageGen: false, audioIn: false, audioOut: false, videoIn: false, videoOut: false },
];

const newModel = (model: string): ModelRow => ({
  id: String(Math.random()).slice(2, 10),
  model,
  label: '',
  enabled: true,
  contextWindow: '',
  supportsTools: true,
  supportsJson: false,
  supportsReasoning: false,
  supportsVision: false,
  supportsImageGen: false,
  supportsOcr: false,
  supportsAudioInput: false,
  supportsAudioOutput: false,
  supportsVideoInput: false,
  supportsVideoOutput: false,
  priority: 0,
  showThinkTierBar: true,
  showThinkToggle: true,
  showQuickButtons: true,
});

const parseWindow = (text: string) => {
  const value = Number.parseInt(text.trim(), 10);
  return Number.isFinite(value) && value > 0 ? Math.min(2_000_000, Math.max(1000, value)) : 0;
};

const displayNameOf = (row: ModelRow) =>
  row.label.trim() ? row.label.trim() : row.model.trim() ? row.model.trim() : '（未填模型）';

const capabilityTextOf = (row: ModelRow) => {
  const parts: string[] = [];
  const window = parseWindow(row.contextWindow);
  if (window) parts.push(`${Math.round((window / 1000) * 10) / 10}K 上下文`);
  if (row.supportsTools) parts.push('工具');
  if (row.supportsJson) parts.push('JSON');
  if (row.supportsReasoning) parts.push('思考');
  if (row.supportsVision) parts.push('识图');
  if (row.supportsImageGen) parts.push('生图');
  if (row.supportsOcr) parts.push('OCR');
  if (row.supportsAudioInput) parts.push('听音频');
  if (row.supportsAudioOutput) parts.push('出音频');
  if (row.supportsVideoInput) parts.push('看视频');
  if (row.supportsVideoOutput) parts.push('出视频');
  if (!row.enabled) parts.push('已停用');
  return parts.length === 0 ? '未标注能力' : parts.join(' · ');
};

const providerTitleOf = (row: ProviderRow) => (row.name.trim() ? row.name.trim() : '（未命名厂商）');
const modelCountOf = (row: ProviderRow) =>
  `${row.models.filter((model) => model.enabled).length}/${row.models.length} 款启用`;

const CAPTION = { fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)' } as const;
const BODY_SM = { fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)' } as const;

/** 一行「标题 + 右侧开关」，对应源码里 *,Auto 的 Grid。 */
function ToggleLine({
  title,
  checked,
  onChange,
}: {
  title: string;
  checked: boolean;
  onChange: (next: boolean) => void;
}) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
      <span style={BODY_SM}>{title}</span>
      <GlassSwitch label={title} checked={checked} onChange={onChange} />
    </div>
  );
}

export function AiModels({ navigate, back }: ScreenProps) {
  const [providers, setProviders] = useState<ProviderRow[]>([]);
  const [customPresets, setCustomPresets] = useState<Preset[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [showDisplaySwitches, setShowDisplaySwitches] = useState(false);
  const [status, setStatus] = useState('');

  const selected = providers
    .flatMap((provider) => provider.models)
    .find((model) => model.id === selectedId) ?? null;

  const patchProvider = (id: string, next: Partial<ProviderRow>) =>
    setProviders((list) => list.map((item) => (item.id === id ? { ...item, ...next } : item)));

  const patchModel = (id: string, next: Partial<ModelRow>) =>
    setProviders((list) =>
      list.map((provider) => ({
        ...provider,
        models: provider.models.map((model) => (model.id === id ? { ...model, ...next } : model)),
      })),
    );

  const moveProvider = (id: string, delta: number) =>
    setProviders((list) => {
      const index = list.findIndex((item) => item.id === id);
      const target = index + delta;
      if (index < 0 || target < 0 || target >= list.length) return list;
      const copy = list.slice();
      const [row] = copy.splice(index, 1);
      copy.splice(target, 0, row);
      return copy;
    });

  const moveModel = (providerId: string, modelId: string, delta: number) =>
    setProviders((list) =>
      list.map((provider) => {
        if (provider.id !== providerId) return provider;
        const index = provider.models.findIndex((model) => model.id === modelId);
        const target = index + delta;
        if (index < 0 || target < 0 || target >= provider.models.length) return provider;
        const models = provider.models.slice();
        const [row] = models.splice(index, 1);
        models.splice(target, 0, row);
        return { ...provider, models };
      }),
    );

  const applyPreset = (preset: Preset) => {
    if (!selected) return;
    patchModel(selected.id, {
      supportsTools: preset.tools,
      supportsJson: preset.json,
      supportsVision: preset.vision,
      supportsOcr: preset.ocr,
      supportsReasoning: preset.reasoning,
      supportsImageGen: preset.imageGen,
      supportsAudioInput: preset.audioIn,
      supportsAudioOutput: preset.audioOut,
      supportsVideoInput: preset.videoIn,
      supportsVideoOutput: preset.videoOut,
    });
    setStatus(`已套用「${preset.name}」：${preset.hint}`);
  };

  const header = (
    <PageHeader
      title="AI 模型管理"
      subtitle="每款模型单独配上下文窗口与能力；预设一键批量设置，改动自动保存。"
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
    const patch = (next: Partial<ModelRow>) => patchModel(selected.id, next);
    return (
      <PageShell>
        {header}
        <div className="screen screen--narrow">
          <p style={{ margin: 0, fontSize: 'var(--w-size-subhead)', color: 'var(--w-ink-display)' }}>
            {displayNameOf(selected)}
          </p>

          <Card title="基础">
            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
              <div style={{ display: 'grid', gridTemplateColumns: '88px 1fr', alignItems: 'center', gap: 'var(--w-space-3)' }}>
                <span style={BODY_SM}>模型 id</span>
                <Field value={selected.model} onChange={(model) => patch({ model })} placeholder="如 deepseek-chat" label="模型 id" />
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '88px 1fr', alignItems: 'center', gap: 'var(--w-space-3)' }}>
                <span style={BODY_SM}>显示名</span>
                <Field value={selected.label} onChange={(label) => patch({ label })} placeholder="留空则用模型 id" label="显示名" />
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '88px 1fr', alignItems: 'center', gap: 'var(--w-space-3)' }}>
                <span style={BODY_SM}>上下文窗口</span>
                <Field
                  value={selected.contextWindow}
                  onChange={(contextWindow) => patch({ contextWindow })}
                  placeholder="如 128000"
                  type="number"
                  label="上下文窗口"
                />
              </div>
              <p style={{ margin: 0, ...CAPTION }}>留空 = 用全局上限；填了按 70% 折叠历史，为输出留空间。</p>
            </div>
          </Card>

          <Card title="能力预设（一键批量设置）">
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--w-space-2)' }}>
              {BUILTIN_PRESETS.map((preset) => (
                <GlassButton key={preset.name} size="sm" onClick={() => applyPreset(preset)}>
                  {preset.name}
                </GlassButton>
              ))}
            </div>
            <p style={{ margin: 'var(--w-space-2) 0 0', ...CAPTION }}>
              通用对话 = 工具 + JSON；视觉模型 = 识图 + OCR；全模态 = 全开；纯文本小模型 = 全关。
            </p>
            <Sep bleed />
            {customPresets.length > 0 ? (
              <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 'var(--w-space-2)' }}>
                {customPresets.map((preset) => (
                  <span key={preset.name} style={{ display: 'inline-flex', alignItems: 'center', gap: '2px' }}>
                    <TextBtn onClick={() => applyPreset(preset)}>{preset.name}</TextBtn>
                    <TextBtn
                      onClick={() => {
                        setCustomPresets((list) => list.filter((item) => item.name !== preset.name));
                        setStatus(`已删除预设「${preset.name}」。`);
                      }}
                    >
                      删除
                    </TextBtn>
                  </span>
                ))}
              </div>
            ) : null}
            <div style={{ marginTop: 'var(--w-space-2)' }}>
              <TextBtn
                onClick={() => {
                  const name = `${displayNameOf(selected)} · 预设`;
                  setCustomPresets((list) => [
                    ...list,
                    {
                      name,
                      hint: '自定义',
                      tools: selected.supportsTools,
                      json: selected.supportsJson,
                      vision: selected.supportsVision,
                      ocr: selected.supportsOcr,
                      reasoning: selected.supportsReasoning,
                      imageGen: selected.supportsImageGen,
                      audioIn: selected.supportsAudioInput,
                      audioOut: selected.supportsAudioOutput,
                      videoIn: selected.supportsVideoInput,
                      videoOut: selected.supportsVideoOutput,
                    },
                  ]);
                  setStatus(`已保存预设「${name}」。`);
                }}
              >
                另存为预设
              </TextBtn>
            </div>
          </Card>

          <Card title="文本与工具">
            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
              <ToggleLine
                title="工具（function calling）"
                checked={selected.supportsTools}
                onChange={(supportsTools) => patch({ supportsTools })}
              />
              <ToggleLine
                title="JSON 结构化输出"
                checked={selected.supportsJson}
                onChange={(supportsJson) => patch({ supportsJson })}
              />
              <ToggleLine
                title="深度思考（推理强度）"
                checked={selected.supportsReasoning}
                onChange={(supportsReasoning) => patch({ supportsReasoning })}
              />
            </div>
          </Card>

          <Card title="图像">
            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
              <ToggleLine
                title="识图（看图理解）"
                checked={selected.supportsVision}
                onChange={(supportsVision) => patch({ supportsVision })}
              />
              <ToggleLine
                title="生图（生成图片）"
                checked={selected.supportsImageGen}
                onChange={(supportsImageGen) => patch({ supportsImageGen })}
              />
              <ToggleLine
                title="OCR（自带文字识别）"
                checked={selected.supportsOcr}
                onChange={(supportsOcr) => patch({ supportsOcr })}
              />
            </div>
          </Card>

          <Card title="音频与视频">
            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
              <ToggleLine
                title="听音频（输入音频）"
                checked={selected.supportsAudioInput}
                onChange={(supportsAudioInput) => patch({ supportsAudioInput })}
              />
              <ToggleLine
                title="出音频（语音合成）"
                checked={selected.supportsAudioOutput}
                onChange={(supportsAudioOutput) => patch({ supportsAudioOutput })}
              />
              <ToggleLine
                title="看视频（视频理解）"
                checked={selected.supportsVideoInput}
                onChange={(supportsVideoInput) => patch({ supportsVideoInput })}
              />
              <ToggleLine
                title="出视频（视频生成）"
                checked={selected.supportsVideoOutput}
                onChange={(supportsVideoOutput) => patch({ supportsVideoOutput })}
              />
            </div>
          </Card>

          <Card title="使用顺序（Priority）">
            <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-3)' }}>
              <GlassSlider
                label="使用顺序"
                value={selected.priority / 20}
                onChange={(value) => patch({ priority: Math.round(value * 20) })}
                format={(value) => String(Math.round(value * 20))}
              />
              <span style={{ minWidth: '28px', textAlign: 'center', color: 'var(--w-ink-body)' }}>
                {selected.priority}
              </span>
            </div>
            <p style={{ margin: 'var(--w-space-2) 0 0', ...CAPTION }}>
              数值小的优先被路由选中；0 = 按列表顺序。上移/下移按钮调整列表顺序。
            </p>
          </Card>

          <Card>
            <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-3)' }}>
              <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: '2px' }}>
                <span style={{ fontSize: 'var(--w-size-subhead)', color: 'var(--w-ink-display)' }}>
                  对话页控件显示
                </span>
                <span style={CAPTION}>按这款模型决定对话页显示哪些控件。</span>
              </div>
              <GlassSwitch
                label="对话页控件显示"
                checked={showDisplaySwitches}
                onChange={setShowDisplaySwitches}
              />
            </div>
            {showDisplaySwitches ? (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)', marginTop: 'var(--w-space-2)' }}>
                <ToggleLine
                  title="思考等级条"
                  checked={selected.showThinkTierBar}
                  onChange={(showThinkTierBar) => patch({ showThinkTierBar })}
                />
                <ToggleLine
                  title="思考开关"
                  checked={selected.showThinkToggle}
                  onChange={(showThinkToggle) => patch({ showThinkToggle })}
                />
                <ToggleLine
                  title="快速按钮组"
                  checked={selected.showQuickButtons}
                  onChange={(showQuickButtons) => patch({ showQuickButtons })}
                />
              </div>
            ) : null}
          </Card>

          {status ? <p style={{ margin: 0, ...CAPTION }}>{status}</p> : null}
        </div>
      </PageShell>
    );
  }

  return (
    <PageShell>
      {header}
      <div className="screen screen--narrow">
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
                  {providerTitleOf(provider)}
                </span>
                <span style={{ ...CAPTION, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {provider.url}
                </span>
                <span style={CAPTION}>{modelCountOf(provider)}</span>
              </div>
              <GlassSwitch
                label={`${providerTitleOf(provider)} 启用`}
                checked={provider.enabled}
                onChange={(enabled) => patchProvider(provider.id, { enabled })}
              />
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)', marginTop: 'var(--w-space-3)' }}>
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
                      onChange={(enabled) => patchModel(model.id, { enabled })}
                    />
                  </div>
                  <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 'var(--w-space-2)', marginTop: 'var(--w-space-2)' }}>
                    <TextBtn onClick={() => setSelectedId(model.id)}>编辑</TextBtn>
                    <TextBtn
                      onClick={() => {
                        moveModel(provider.id, model.id, -1);
                        setStatus('顺序已保存：同能力下排在前面的模型优先被选中。');
                      }}
                    >
                      上移
                    </TextBtn>
                    <TextBtn
                      onClick={() => {
                        moveModel(provider.id, model.id, 1);
                        setStatus('顺序已保存：同能力下排在前面的模型优先被选中。');
                      }}
                    >
                      下移
                    </TextBtn>
                    <TextBtn
                      onClick={() => {
                        if (!window.confirm(`删除模型\n删掉「${displayNameOf(model)}」？`)) return;
                        setProviders((list) =>
                          list.map((item) =>
                            item.id === provider.id
                              ? { ...item, models: item.models.filter((row) => row.id !== model.id) }
                              : item,
                          ),
                        );
                        setStatus('已删除模型。');
                      }}
                    >
                      删除
                    </TextBtn>
                  </div>
                </Card>
              ))}
            </div>

            <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 'var(--w-space-2)', marginTop: 'var(--w-space-3)' }}>
              <GlassButton
                size="sm"
                onClick={() => {
                  const row = newModel('new-model');
                  setProviders((list) =>
                    list.map((item) =>
                      item.id === provider.id ? { ...item, models: [...item.models, row] } : item,
                    ),
                  );
                  setStatus('已加模型，进入详情填写模型 id 与能力。');
                  setSelectedId(row.id);
                }}
              >
                加模型
              </GlassButton>
              <TextBtn
                onClick={() => {
                  moveProvider(provider.id, -1);
                  setStatus('顺序已保存：排在前面的厂商优先被轮换。');
                }}
              >
                上移
              </TextBtn>
              <TextBtn
                onClick={() => {
                  moveProvider(provider.id, 1);
                  setStatus('顺序已保存：排在前面的厂商优先被轮换。');
                }}
              >
                下移
              </TextBtn>
              <TextBtn
                onClick={() => {
                  if (!window.confirm(`删除厂商\n删掉「${providerTitleOf(provider)}」及其下所有模型？`)) return;
                  setProviders((list) => list.filter((item) => item.id !== provider.id));
                  setStatus('已删除厂商。');
                }}
              >
                删除厂商
              </TextBtn>
            </div>
          </Card>
        ))}

        {providers.length === 0 ? (
          <Empty
            title="还没有厂商"
            reason="模型都挂在厂商下面，厂商列表是空的，所以这里也没有模型可配。"
            hint="先点「加厂商」，或到「厂商管理」补端点与密钥。"
          />
        ) : null}

        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-start', gap: 'var(--w-space-2)' }}>
          <GlassButton
            size="sm"
            onClick={() => {
              setProviders((list) => [
                ...list,
                { id: String(Math.random()).slice(2, 10), name: '新厂商', url: '', enabled: true, models: [] },
              ]);
              setStatus('已加厂商，去「厂商管理」补端点与密钥。');
            }}
          >
            加厂商
          </GlassButton>
          <TextBtn onClick={() => navigate('/modelDeployment')}>模型部署向导</TextBtn>
          <p style={{ margin: 0, ...CAPTION }}>端点与密钥在「厂商管理」维护；这里只管模型与能力。</p>
        </div>

        {status ? <p style={{ margin: 0, ...CAPTION }}>{status}</p> : null}
      </div>
    </PageShell>
  );
}
