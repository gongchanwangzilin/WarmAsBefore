import { useEffect, useState } from 'react';
import { Field, PageHeader } from '../../components/kit';
import { Glass } from '../../components/Glass';
import { PageShell } from '../../components/PageShell';
import { SignalButton } from '../../components/SignalButton';
import type { ScreenProps } from '../types';

type NovelRow = {
  id: string;
  title: string;
  summary: string;
  characters: string[];
  branches: number;
};

type NovelMessageRow = { text: string; mine: boolean };

/** 开场那句：与 NovelWorldViewModel.LoadAsync 里的欢迎语同文。 */
function welcomeOf(novel: NovelRow): string {
  if (novel.characters.length === 0) return `欢迎来到《${novel.title}》的平行宇宙。\n\n${novel.summary}`;
  return (
    `欢迎来到《${novel.title}》的平行宇宙。你是误入这个世界的旅人，\n` +
    `这里生活着：${novel.characters.join('、')}。` +
    '这里的角色会把你当成这个世界的一部分，和你一起经历接下来的故事。'
  );
}

/**
 * 平行宇宙对话：NovelWorldPage.xaml。
 * 从 ?id= 取一部小说（没有对应 id 就用第一部），页头是它的书名与世界角色，
 * 开场气泡是它自己的欢迎语；回复本身没有接口，发出去的那句后面跟源码里那句沉默。
 */
export function Novelworld({ bridge, query, back }: ScreenProps) {
  const [novels, setNovels] = useState<NovelRow[]>([]);
  const [messages, setMessages] = useState<NovelMessageRow[]>([]);
  const [input, setInput] = useState('');
  const id = query.get('id');

  useEffect(() => {
    let alive = true;
    bridge
      .request('novels.list', {})
      .then((result) => {
        if (!alive) return;
        setNovels(
          result.novels.map((novel) => ({
            id: novel.id,
            title: novel.title,
            summary: novel.summary,
            characters: novel.characters,
            branches: novel.branches,
          })),
        );
      })
      .catch(() => undefined);
    return () => {
      alive = false;
    };
  }, [bridge]);

  const novel = novels.find((row) => row.id === id) ?? novels[0] ?? null;

  useEffect(() => {
    if (!novel) return;
    setMessages([{ text: welcomeOf(novel), mine: false }]);
  }, [novel]);

  const worldLabel =
    novel === null ? '' : novel.characters.length > 0 ? `世界角色：${novel.characters.join('、')}` : '未知世界';

  const send = () => {
    const text = input;
    if (!text.trim()) return;
    setInput('');
    setMessages((list) => [...list, { text, mine: true }, { text: '（世界短暂沉默了一下…）', mine: false }]);
  };

  return (
    <PageShell>
      <PageHeader title={novel?.title ?? '平行宇宙'} subtitle={worldLabel || undefined} onBack={back} />

      <div className="screen" style={{ maxWidth: 960 }}>
        {novel ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            {novel.summary.trim() ? (
              <p style={{ margin: 0, fontSize: 'var(--w-size-small)', color: 'var(--w-ink-muted)', lineHeight: 1.7 }}>
                {novel.summary}
              </p>
            ) : null}
            <span className="cap">{`分支：${novel.branches}`}</span>
          </div>
        ) : null}

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
