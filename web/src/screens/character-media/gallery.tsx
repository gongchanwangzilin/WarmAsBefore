import { useState } from 'react';
import { PageShell } from '../../components/PageShell';
import { PageHeader, Card, Row, Sep } from '../../components/kit';
import type { ScreenProps } from '../types';
import { Cap, NoteRow, SectionTitle } from './parts';

type Tab = 'memoir' | 'remembered' | 'diary' | 'cg';

const TABS: { id: Tab; label: string }[] = [
  { id: 'memoir', label: '回忆录' },
  { id: 'remembered', label: '记得的事' },
  { id: 'diary', label: '日记' },
  { id: 'cg', label: 'CG收藏册' },
];

/**
 * 回忆录（GalleryPage.xaml，439 行）：页头 + 记忆分层总览 + 四页签。
 * 记忆分层、四条长列表、CG 网格都要后端，这里按源码的空态渲染；
 * 页签切换、搜索框与「清空」是本地状态，键位与源码一致。
 */
export function Gallery({ navigate, back }: ScreenProps) {
  const [tab, setTab] = useState<Tab>('memoir');
  const [search, setSearch] = useState('');

  return (
    <PageShell>
      <PageHeader
        variant="ground"
        onBack={back}
        title="回忆录"
        subtitle="全部好感时刻与全部对话都在这里，可搜索；日记是每天自动生成的陪伴总结（核心机制，无需开启）"
      />

      <div className="screen">
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, minmax(0, 1fr))', gap: 'var(--w-space-2)' }}>
          {TABS.map((item) => (
            <button
              key={item.id}
              type="button"
              aria-pressed={tab === item.id}
              onClick={() => setTab(item.id)}
              style={{
                minHeight: 40,
                padding: '10px 0',
                border: 0,
                borderRadius: 'var(--w-radius-well)',
                background: tab === item.id ? 'var(--w-surface-tint)' : 'transparent',
                color: 'var(--w-ink-display)',
                font: 'inherit',
                fontSize: 'var(--w-size-small)',
                fontWeight: tab === item.id ? 700 : 400,
                cursor: 'pointer',
              }}
            >
              {item.label}
            </button>
          ))}
        </div>

        {tab === 'remembered' ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
            <Cap>这是 TA 脑子里真正记得的事——重要的忘得慢，琐碎的自然淡忘；照片式记忆只有单张。</Cap>

            <Card tight>
              <Row
                title="记忆管理（标签 / 记得的事 / 副设定）"
                chevron
                onClick={() => navigate('/memoryManage')}
              />
              <Sep />
              <Row title="打开关系网" chevron onClick={() => navigate('/memoryGraph')} />
              <Sep />
              <Row title="搜事件" chevron onClick={() => navigate('/memorySearch')} />
            </Card>

            <Cap>关系网和搜事件各开一扇窗，主窗口继续聊。</Cap>

            <Card tight>
              <NoteRow>还没有记得的事——多聊几句，重要的会被记下来。</NoteRow>
            </Card>
          </div>
        ) : null}

        {tab === 'memoir' ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
            <Card>
              <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <input
                    className="k-field"
                    value={search}
                    placeholder="搜索对话回忆…"
                    aria-label="搜索对话回忆…"
                    onChange={(event) => setSearch(event.target.value)}
                    style={{ fontSize: 'var(--w-size-small)' }}
                  />
                </div>
                <button type="button" className="k-textbtn" onClick={() => setSearch('')}>
                  清空
                </button>
              </div>
            </Card>

            <SectionTitle>好感时刻（全部）</SectionTitle>
            <Card tight>
              <NoteRow>还没有好感时刻，去和 TA 互动吧~</NoteRow>
            </Card>

            <SectionTitle>对话回忆（可搜索）</SectionTitle>
            <Card tight>
              <NoteRow>还没有对话回忆</NoteRow>
            </Card>
          </div>
        ) : null}

        {tab === 'diary' ? (
          <Card tight>
            <NoteRow>还没有日记，今天多陪陪 TA，明天早上就会有一篇总结啦~</NoteRow>
          </Card>
        ) : null}

        {tab === 'cg' ? (
          <Cap>还没有收藏的 CG。当 TA 在对话里引用【CG:文件名】时就会解锁收藏~</Cap>
        ) : null}
      </div>
    </PageShell>
  );
}
