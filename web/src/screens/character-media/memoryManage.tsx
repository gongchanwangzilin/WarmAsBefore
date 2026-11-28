import { useEffect, useState } from 'react';
import { PageShell } from '../../components/PageShell';
import { PageHeader, Card, Sep } from '../../components/kit';
import type { ScreenProps } from '../types';
import { Cap, GhostSm, Muted, RowLine, SectionTitle, TextSm } from './parts';

/** 人类记忆层的五个分类色名（MemoryHighlight.Legend 的顺序与文案）。 */
const LEGEND = ['人', '话题', '事', '地点', '物'];

/** 标签分类：MemoryHighlight.SubjectText 的键与话，顺序同 Legend。 */
const SUBJECTS = [
  { key: 'person', label: '人' },
  { key: 'talk', label: '话题' },
  { key: 'event', label: '事' },
  { key: 'place', label: '地点' },
  { key: 'thing', label: '物' },
];

type MemoryTag = { name: string; subject: string; count: number };

/**
 * 记忆管理（MemoryManagePage.xaml，189 行）：页头 + 三个入口 + 人类记忆层说明 + 记忆条目。
 * 标签段读第一个角色的 memory.tags，按分类分组；记得的事按源码空态渲染。
 */
export function MemoryManage({ bridge, navigate, back }: ScreenProps) {
  const [charName, setCharName] = useState('');
  const [tags, setTags] = useState<MemoryTag[]>([]);
  const [ticket, setTicket] = useState(0);

  useEffect(() => {
    let alive = true;
    const load = async () => {
      try {
        const roster = await bridge.request('characters.list', {});
        const first = roster.characters[0];
        if (!first) return;
        if (alive) setCharName(first.name);
        const result = await bridge.request('memory.tags', { charId: first.id });
        if (alive) setTags(result.tags);
      } catch {
        /* 读不到就落回空态 */
      }
    };
    void load();
    return () => {
      alive = false;
    };
  }, [bridge, ticket]);

  const groups = SUBJECTS.map((subject) => ({
    ...subject,
    tags: tags.filter((tag) => (tag.subject || 'event') === subject.key),
  })).filter((group) => group.tags.length > 0);

  return (
    <PageShell>
      <PageHeader
        title={charName ? `记忆管理 · ${charName}` : '记忆管理'}
        subtitle="这里能看到并修改 TA 的记忆：关系网、标签、TA 记得的事，以及主设定 × 副设定。"
        onBack={back}
      />

      <div className="screen" style={{ gap: 'var(--w-space-3)' }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-2)' }}>
          <Cap>这些记忆：只在这份存档里（开关在设置 → 记忆里）</Cap>
          <TextSm onClick={() => setTicket((n) => n + 1)}>刷新</TextSm>
        </div>

        <Card tight>
          <RowLine
            title="副设定（当下之我）"
            desc="主设定不可改；副设定随经历漂移，可以看对比、也可以回正到主设定。"
            right={<GhostSm disabled>打开主设定 × 副设定对比</GhostSm>}
          />
          <Sep />
          <RowLine
            title="关系网与搜事件（各开一扇窗）"
            desc="词、人、地点、标签、照片、记得的事都在同一张网上：看得见它长出来，也能当场改 —— 设等价、加联系、断开、忘掉。搜事件按一句话、标签、那种感觉、或者那天的日子找。"
            right={
              <span style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)', alignItems: 'flex-end' }}>
                <GhostSm onClick={() => navigate('/memoryGraph')}>打开关系网</GhostSm>
                <GhostSm onClick={() => navigate('/memorySearch')}>搜事件</GhostSm>
              </span>
            }
          />
          <Sep />
          <RowLine
            title="标签（TA 怎么记人和事）"
            desc={
              groups.length === 0 ? (
                '还没有标签。可以在这里手工加，AI 在对话里也会自己打。'
              ) : (
                <span style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
                  {groups.map((group) => (
                    <span key={group.key} style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                      <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-label)', lineHeight: 1.6 }}>
                        {group.label}
                      </span>
                      {group.tags.map((tag) => (
                        <span
                          key={tag.name}
                          style={{
                            fontSize: 'var(--w-size-caption)',
                            color: 'var(--w-ink-display)',
                            lineHeight: 1.6,
                            overflow: 'hidden',
                            textOverflow: 'ellipsis',
                            whiteSpace: 'nowrap',
                          }}
                        >
                          {`${tag.name}（用过 ${tag.count} 次）`}
                        </span>
                      ))}
                    </span>
                  ))}
                </span>
              )
            }
            right={
              <span style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)', alignItems: 'flex-end' }}>
                <GhostSm disabled>加标签</GhostSm>
                <GhostSm disabled>删标签</GhostSm>
              </span>
            }
          />
        </Card>

        <div>
          <SectionTitle>TA 记得的事（人类记忆层）</SectionTitle>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6, marginBottom: 'var(--w-space-2)' }}>
            <Muted>只记事件、印象、重要时刻与单张照片；越重要忘得越慢，久了的会模糊。</Muted>
            <Muted>点了标签的句子会铺上底色：颜色 = 这句记的是人 / 话题 / 事 / 地点 / 物。想改就点那句。</Muted>
            <Muted>
              核心记忆是人格的锚点：永不遗忘、每次都会被想起，还会影响周围的记忆；创伤是特殊的核心记忆，只会随具体互动慢慢降温，内容不会消失。
            </Muted>
          </div>

          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--w-space-1)', marginBottom: 'var(--w-space-2)' }}>
            {LEGEND.map((label) => (
              <span
                key={label}
                style={{
                  padding: '2px 8px',
                  borderRadius: 6,
                  border: '1px solid var(--w-hairline)',
                  background: 'var(--w-surface-tint)',
                  fontSize: 'var(--w-size-caption)',
                  color: 'var(--w-ink-display)',
                }}
              >
                {label}
              </span>
            ))}
          </div>

          <GhostSm disabled>＋ 手动加一条记忆</GhostSm>
        </div>

        <Cap>还没有记得的事——多聊几句，重要的会被记下来。</Cap>
      </div>
    </PageShell>
  );
}
