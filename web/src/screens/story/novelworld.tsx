import { useState } from 'react';
import { Field, PageHeader } from '../../components/kit';
import { Glass } from '../../components/Glass';
import { PageShell } from '../../components/PageShell';
import { SignalButton } from '../../components/SignalButton';
import type { ScreenProps } from '../types';

type NovelMessageRow = { text: string; mine: boolean };

/**
 * 平行宇宙对话：NovelWorldPage.xaml。
 * 世界设定与回复来自 NovelLibrary + ApiGateway；这里只保留页头发送一条气泡的对话壳，
 * 没有世界可载时标题停在源码的「平行宇宙」。
 */
export function Novelworld({ query, back }: ScreenProps) {
  const [input, setInput] = useState('');
  const [messages, setMessages] = useState<NovelMessageRow[]>([]);
  const novelId = query.get('id');

  const send = () => {
    const text = input;
    if (!text.trim()) return;
    setInput('');
    setMessages((list) => [...list, { text, mine: true }]);
    if (!novelId) return;
  };

  return (
    <PageShell>
      <PageHeader title="平行宇宙" onBack={back} />

      <div className="screen" style={{ maxWidth: 960 }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-1)' }}>
          {messages.map((message, index) => (
            <div key={index} style={{ display: 'flex', justifyContent: message.mine ? 'flex-end' : 'flex-start' }}>
              <div
                style={{
                  maxWidth: 480,
                  padding: 'var(--w-space-2) var(--w-space-3)',
                  borderRadius: 'var(--w-radius-well)',
                  background: message.mine ? 'var(--w-tint-button)' : 'var(--w-tint-card)',
                  color: 'var(--w-ink-body)',
                  fontSize: 'var(--w-size-body)',
                  lineHeight: 1.6,
                  whiteSpace: 'pre-wrap',
                }}
              >
                {message.text}
              </div>
            </div>
          ))}
        </div>
      </div>

      <Glass
        as="div"
        tone="bar"
        radius={0}
        style={{ position: 'sticky', bottom: 0, zIndex: 5, borderTop: '1px solid var(--w-hairline)' }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-2)', padding: 'var(--w-space-2) var(--w-pad-page)' }}>
          <div
            style={{
              flex: 1,
              display: 'flex',
              alignItems: 'center',
              padding: '0 10px',
              borderRadius: 'var(--w-radius-well)',
              background: 'var(--w-surface-tint)',
            }}
          >
            <Field label="对这个世界说点什么" value={input} onChange={setInput} placeholder="对这个世界说点什么…" />
          </div>
          <SignalButton onClick={send}>发送</SignalButton>
        </div>
      </Glass>
    </PageShell>
  );
}
