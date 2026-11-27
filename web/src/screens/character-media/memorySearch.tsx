import { useState } from 'react';
import { PageShell } from '../../components/PageShell';
import { PageHeader, Card } from '../../components/kit';
import type { ScreenProps } from '../types';
import { Cap, GhostSm, TextSm } from './parts';

/** 类型页签：全部 + 人类记忆层的四种（BuildKindChips 的原顺序）。 */
const KINDS = ['全部', '事件', '重要时刻', '印象', '照片'];

/**
 * 搜事件（MemorySearchPage.xaml，168 行）：搜索条 + 类型/感觉/数值三排胶囊 + 结果。
 * 记得的事都在后端（这里没有），所以搜索永远落到源码那句「没找着。」的空态；
 * 查询串、类型筛选与「链式」开关是本地状态。
 */
export function MemorySearch({ navigate, back }: ScreenProps) {
  const [query, setQuery] = useState('');
  const [kind, setKind] = useState('全部');
  const [chain, setChain] = useState(false);

  return (
    <PageShell>
      <PageHeader title="搜事件" onBack={back} />

      <div className="screen">
        <Card>
          <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-2)', flexWrap: 'wrap' }}>
            <input
              className="k-field"
              style={{ flex: 1, minWidth: 180, fontSize: 'var(--w-size-body)' }}
              value={query}
              placeholder="一句话、#标签、一种感觉、或者 8月24号…"
              aria-label="搜事件"
              onChange={(event) => setQuery(event.target.value)}
            />
            <TextSm onClick={() => setQuery('')}>清空</TextSm>
            <GhostSm
              onClick={() => setChain((v) => !v)}
              style={{
                background: chain ? 'var(--w-surface-tint)' : undefined,
                fontWeight: chain ? 700 : 400,
                color: chain ? 'var(--w-ink-display)' : undefined,
              }}
            >
              {chain ? '链式 · 开' : '链式'}
            </GhostSm>
            <GhostSm onClick={() => navigate('/memoryGraph')}>关系网</GhostSm>
          </div>

          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--w-space-1)' }}>
            {KINDS.map((item) => {
              const active = item === kind;
              return (
                <GhostSm
                  key={item}
                  onClick={() => setKind(item)}
                  style={{
                    background: active ? 'var(--w-surface-tint)' : undefined,
                    fontWeight: active ? 700 : 400,
                    color: active ? 'var(--w-ink-display)' : undefined,
                  }}
                >
                  {item}
                </GhostSm>
              );
            })}
          </div>

          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-2)' }}>
            <Cap>
              {query.trim()
                ? `「${query.trim()}」命中 0 件` + (kind === '全部' ? '' : ` · 只看${kind}`)
                : 'TA 记得 0 件（最新在这些）'}
            </Cap>
            <Cap>只在这份存档里</Cap>
          </div>
        </Card>

        <div
          style={{
            padding: 'var(--w-space-3)',
            borderRadius: 'var(--w-radius-card)',
            border: '1px solid var(--w-hairline)',
            background: 'var(--w-surface-tint)',
            display: 'flex',
            flexDirection: 'column',
            gap: 'var(--w-space-1)',
          }}
        >
          <span style={{ fontSize: 'var(--w-size-body)', fontWeight: 700, color: 'var(--w-ink-body)' }}>没找着。</span>
          <Cap>
            {query.trim()
              ? chain
                ? `顺着「${query.trim()}」一条也没连上。换个线索：那个人名、那天的天气、当时说过的半句话。`
                : `没有哪件事对上「${query.trim()}」。换条路试试：贴过的标签名、那种感觉的名字、或者那天的日子（8月24号）。`
              : '还没记下什么事。多聊几句，重要的那些会被 TA 自己记住。'}
          </Cap>
        </div>
      </div>
    </PageShell>
  );
}
