import { useState } from 'react';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import { Card, Empty, Field, PageHeader, Select, TextBtn } from '../../components/kit';
import type { ScreenProps } from '../types';

/**
 * 模型部署向导：Views/Settings/ModelDeploymentPage.xaml（125 行）+ ViewModels/Settings/ModelDeploymentViewModel.cs。
 * 名字、来源、安装位置全部由用户填，工具只负责下载 → 校验 → 解压 → 登记。
 */

type ModelKind = 'Embedding' | 'SmallModel' | 'LLM' | 'Vision';

type Suggestion = { kind: ModelKind; name: string; exampleSource: string; note: string };

const KIND_CHOICES: { value: ModelKind; label: string }[] = [
  { value: 'Embedding', label: 'Embedding' },
  { value: 'SmallModel', label: 'SmallModel' },
  { value: 'LLM', label: 'LLM' },
  { value: 'Vision', label: 'Vision' },
];

const CAPTION = { fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)' } as const;
const LABEL = { fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-label)' } as const;

/** ModelDeploymentService.SuggestByPerformance：只看核数与内存给一档示例，名字仍要用户自己定。 */
function buildSuggestions(): Suggestion[] {
  const cores = Math.max(1, navigator.hardwareConcurrency || 1);
  const ramGb = (navigator as Navigator & { deviceMemory?: number }).deviceMemory ?? 0;
  const tier = cores >= 8 && ramGb >= 16 ? '高' : cores >= 4 ? '中' : '低';
  return [
    {
      kind: 'Embedding',
      name: '你定的向量模型名',
      exampleSource: 'https://你的镜像/模型.gguf 或 .onnx',
      note: `近义检索用；${tier}配机建议先试小尺寸（约 100MB 级）`,
    },
    {
      kind: 'SmallModel',
      name: '你定的小模型名',
      exampleSource: 'https://你的镜像/小模型.gguf',
      note: '情绪表演 / 工具解析 / 状态包压缩；便宜优先',
    },
    {
      kind: 'LLM',
      name: '你定的大模型名',
      exampleSource: 'https://你的镜像/大模型',
      note: `主对话；当前约 ${Math.round(ramGb)}GB 内存、${cores} 核`,
    },
  ];
}

export function ModelDeployment({ back }: ScreenProps) {
  const [name, setName] = useState('');
  const [kind, setKind] = useState<ModelKind>('Embedding');
  const [source, setSource] = useState('');
  const [installPath, setInstallPath] = useState('');
  const [checksum, setChecksum] = useState('');
  const [status, setStatus] = useState('');
  const [suggestions, setSuggestions] = useState<Suggestion[]>([]);

  const deploy = () => {
    if (name.trim().length === 0 || source.trim().length === 0 || installPath.trim().length === 0) {
      setStatus('名字、来源、安装位置三项都要填。');
      return;
    }
    setStatus('部署异常：这一层没有接上下载与解压服务，表单只负责收下你要装的东西。');
  };

  return (
    <PageShell>
      <PageHeader
        title="模型部署向导"
        subtitle="下载 / 校验 / 解压 / 安装全自动。模型名、来源（链接或本机文件）、安装位置全部由你定。"
        onBack={back}
        backText="←"
      />

      <div className="screen">
        <Card title="添加部署">
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
            <div style={{ display: 'grid', gridTemplateColumns: 'auto 1fr', alignItems: 'center', gap: 'var(--w-space-3)' }}>
              <span style={LABEL}>模型名</span>
              <Field value={name} onChange={setName} placeholder="你起的名字，随便一个也行" label="模型名" />
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: 'auto 1fr', alignItems: 'center', gap: 'var(--w-space-3)' }}>
              <span style={LABEL}>类型</span>
              <span>
                <Select label="类型" value={kind} options={KIND_CHOICES} onChange={setKind} />
              </span>
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: 'auto 1fr', alignItems: 'center', gap: 'var(--w-space-3)' }}>
              <span style={LABEL}>来源</span>
              <Field
                value={source}
                onChange={setSource}
                placeholder="http(s) 链接，或本机文件路径"
                label="来源"
              />
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: 'auto 1fr', alignItems: 'center', gap: 'var(--w-space-3)' }}>
              <span style={LABEL}>安装位置</span>
              <Field value={installPath} onChange={setInstallPath} label="安装位置" />
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: 'auto 1fr', alignItems: 'center', gap: 'var(--w-space-3)' }}>
              <span style={LABEL}>校验和</span>
              <Field
                value={checksum}
                onChange={setChecksum}
                placeholder="可选 SHA256，填了才校验"
                label="校验和"
              />
            </div>

            <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 'var(--w-space-3)' }}>
              <GlassButton size="md" onClick={() => setSuggestions(buildSuggestions())}>
                按电脑性能推荐
              </GlassButton>
              <GlassButton size="md" onClick={deploy}>
                开始部署
              </GlassButton>
              <TextBtn disabled>取消</TextBtn>
            </div>

            {status ? <p style={{ margin: 0, ...CAPTION }}>{status}</p> : null}

            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
              <span style={CAPTION}>按性能推荐（示例，可改）</span>
              {suggestions.map((suggestion) => (
                <Card key={suggestion.name} tight>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-3)' }}>
                    <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: '2px' }}>
                      <span style={{ fontSize: 'var(--w-size-small)', color: 'var(--w-ink-display)' }}>
                        {suggestion.name}
                      </span>
                      <span style={CAPTION}>{suggestion.note}</span>
                      <span style={{ ...CAPTION, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                        {suggestion.exampleSource}
                      </span>
                    </div>
                    <TextBtn
                      onClick={() => {
                        setName(suggestion.name);
                        setKind(suggestion.kind);
                        setSource(suggestion.exampleSource);
                        setStatus('已按建议预填（名字 / 来源 / 位置你都能改）。');
                      }}
                    >
                      用这条
                    </TextBtn>
                  </div>
                </Card>
              ))}
            </div>
          </div>
        </Card>

        <h2 style={{ margin: 0, fontSize: 'var(--w-size-body)', fontWeight: 700, color: 'var(--w-ink-display)' }}>
          已部署
        </h2>

        <Empty
          title="还没有部署登记"
          reason="登记表是空的：这台机器上还没有装过任何本地模型。"
          hint="填好上面的表单点「开始部署」，装完的模型会登记到这里，附状态与安装位置。"
        />
      </div>
    </PageShell>
  );
}
