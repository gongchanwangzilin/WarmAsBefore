import { useState } from 'react';
import { Card, Field, PageHeader, Row, Sep, Select, TextBtn } from '../../components/kit';
import { PageShell } from '../../components/PageShell';
import { SignalButton } from '../../components/SignalButton';
import type { ScreenProps } from '../types';
import { Actions, Note, SectionTitle, Stack } from './parts';

type WorldCharacter = { name: string; gender: string; personality: string; description: string };

type WorldEntry = {
  id: string;
  title: string;
  description: string;
  idea: string;
  mode: '纯文字' | '带立绘';
  characters: WorldCharacter[];
};

function summaryOf(entry: WorldEntry): string {
  const text = (entry.description.trim() ? entry.description : entry.idea).trim().replace(/\n/g, ' ');
  return text.length <= 80 ? text : `${text.slice(0, 80)}…`;
}

/**
 * 世界书：WorldbookPage.xaml 的库（WORLDS）与编辑（EDIT WORLD）两态。
 * 没有世界树后端，库从空开始；新建/编辑保存在页内状态，刷新即失。
 */
export function Worldbook({ back }: ScreenProps) {
  const [entries, setEntries] = useState<WorldEntry[]>([]);
  const [draft, setDraft] = useState<WorldEntry | null>(null);
  const [aiPrompt, setAiPrompt] = useState('');
  const [mode, setMode] = useState<'纯文字' | '带立绘'>('纯文字');
  const [status, setStatus] = useState('');
  const [editStatus, setEditStatus] = useState('');

  const openEditor = (entry: WorldEntry, message = '') => {
    setDraft({ ...entry, characters: entry.characters.map((c) => ({ ...c })) });
    setEditStatus(message);
    setStatus('');
  };

  const newManual = () => openEditor(
    { id: `w${Date.now()}`, title: '未命名世界', description: '', idea: '', mode, characters: [] },
    '还没有落盘。写好标题与设定，按「保存」。',
  );

  const saveEdit = () => {
    if (!draft) return;
    const title = draft.title.trim();
    if (title.length === 0) {
      setEditStatus('标题不能空着——存档列表要靠它认出这一份世界。');
      return;
    }
    const saved: WorldEntry = {
      ...draft,
      title,
      description: draft.description.trim(),
      idea: draft.idea.trim(),
      characters: draft.characters.filter((c) => c.name.trim().length > 0),
    };
    setEntries((list) => {
      const hit = list.findIndex((e) => e.id === saved.id);
      return hit < 0 ? [...list, saved] : list.map((e) => (e.id === saved.id ? saved : e));
    });
    setDraft(null);
    setEditStatus('');
    setStatus('已保存。绑定它的存档会在下一次整理记忆时用上新设定。');
  };

  const remove = (entry: WorldEntry) => {
    const text = `「${entry.title}」会从世界书库里删掉。已经绑定它的其它存档将失去世界设定。`;
    if (!window.confirm(text)) return;
    setEntries((list) => list.filter((e) => e.id !== entry.id));
    setStatus('已删除。');
  };

  const setChar = (index: number, patch: Partial<WorldCharacter>) => {
    if (!draft) return;
    setDraft({
      ...draft,
      characters: draft.characters.map((c, i) => (i === index ? { ...c, ...patch } : c)),
    });
  };

  return (
    <PageShell>
      <PageHeader
        title="世界书"
        subtitle={status || undefined}
        onBack={draft ? () => setDraft(null) : back}
        right={
          draft ? null : (
            <TextBtn onClick={newManual}>新建</TextBtn>
          )
        }
      />

      <div className="screen" style={{ maxWidth: 960 }}>
        {!draft ? (
          <Stack gap={4}>
            <Stack gap={1}>
              <span className="eyebrow">WORLDS</span>
              <p style={{ margin: 0, fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)' }}>
                存档绑定的就是这里的一份。可以自己写，也可以让 AI 造一个，再由你改。
              </p>
            </Stack>

            <Card>
              <SectionTitle>让 AI 造一个世界</SectionTitle>
              <Field
                multiline
                label="让 AI 造一个世界"
                value={aiPrompt}
                onChange={setAiPrompt}
                placeholder="一句话就够：战后废土上的移动旅馆；或者一座只在雨夜出现的旧城。"
              />
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
                <Select
                  label="生成模式"
                  value={mode}
                  options={[
                    { value: '纯文字', label: '纯文字' },
                    { value: '带立绘', label: '带立绘' },
                  ]}
                  onChange={setMode}
                />
                <SignalButton onClick={() => { if (!aiPrompt.trim()) setStatus('先写一句你想要的世界。'); }}>
                  生成并编辑
                </SignalButton>
              </div>
            </Card>

            <Card>
              {entries.map((entry) => {
                const bits = [entry.characters.length > 0 ? `${entry.characters.length} 位角色` : '没有角色', entry.mode];
                return (
                  <div key={entry.id}>
                    <Row
                      title={entry.title || '未命名世界'}
                      desc={
                        <span style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
                          {summaryOf(entry) ? <span>{summaryOf(entry)}</span> : null}
                          <span className="cap">{bits.join(' · ')}</span>
                        </span>
                      }
                      right={
                        <Actions>
                          <TextBtn onClick={() => openEditor(entry)}>编辑</TextBtn>
                          <TextBtn onClick={() => remove(entry)}>删除</TextBtn>
                        </Actions>
                      }
                    />
                    <Sep />
                  </div>
                );
              })}
              {entries.length === 0 ? (
                <Stack gap={1}>
                  <SectionTitle>还没有世界书</SectionTitle>
                  <Note>上面写一句，让 AI 造第一个；或者点右上角「新建」自己写。</Note>
                </Stack>
              ) : null}
            </Card>

            <Note>没绑定世界书的存档是普通酒馆：只聊天，地图锁上。绑定之后随时可以换，也可以解绑。</Note>
          </Stack>
        ) : (
          <Stack gap={4}>
            <Stack gap={1}>
              <span className="eyebrow">EDIT WORLD</span>
              <p style={{ margin: 0, fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)' }}>
                改哪一条都行。保存之后，绑定它的存档在下一次整理记忆时生效。
              </p>
            </Stack>

            <Card>
              <span className="cap">标题</span>
              <Field label="标题" value={draft.title} onChange={(title) => setDraft({ ...draft, title })} placeholder="给这个世界一个名字" />

              <span className="cap">世界设定</span>
              <Field
                multiline
                label="世界设定"
                value={draft.description}
                onChange={(description) => setDraft({ ...draft, description })}
                placeholder="这个世界一直成立的事：地理、规则、气氛、势力、日常。不要写具体剧情。"
              />

              <span className="cap">最初的想法</span>
              <Field
                multiline
                label="最初的想法"
                value={draft.idea}
                onChange={(idea) => setDraft({ ...draft, idea })}
                placeholder="你当初用来生成它的那句话。留着自己看，也方便以后让 AI 重写。"
              />
            </Card>

            <Stack gap={2}>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
                <SectionTitle>世界里的人</SectionTitle>
                <TextBtn
                  onClick={() =>
                    setDraft({
                      ...draft,
                      characters: [...draft.characters, { name: '', gender: '女', personality: '', description: '' }],
                    })
                  }
                >
                  添加角色
                </TextBtn>
              </div>

              <Card>
                {draft.characters.map((c, index) => (
                  <div key={index}>
                    <Stack gap={2}>
                      <div style={{ display: 'grid', gridTemplateColumns: '1fr 76px', gap: 'var(--w-space-3)' }}>
                        <Field label="角色名" value={c.name} onChange={(name) => setChar(index, { name })} placeholder="角色名" />
                        <Field label="性别" value={c.gender} onChange={(gender) => setChar(index, { gender })} placeholder="性别" />
                      </div>
                      <Field
                        label="性格"
                        value={c.personality}
                        onChange={(personality) => setChar(index, { personality })}
                        placeholder="性格（一两句话）"
                      />
                      <Field
                        multiline
                        label="描述"
                        value={c.description}
                        onChange={(description) => setChar(index, { description })}
                        placeholder="外观、背景、在这个世界里的位置"
                      />
                      <Actions>
                        <TextBtn
                          onClick={() =>
                            setDraft({ ...draft, characters: draft.characters.filter((_, i) => i !== index) })
                          }
                        >
                          移除这个角色
                        </TextBtn>
                      </Actions>
                    </Stack>
                    <Sep />
                  </div>
                ))}
                {draft.characters.length === 0 ? (
                  <Note>这份世界还没有角色。可以只写世界，也可以在这里加几个人。</Note>
                ) : null}
              </Card>
            </Stack>

            {editStatus ? <Note warn>{editStatus}</Note> : null}

            <Actions>
              <SignalButton onClick={saveEdit}>保存</SignalButton>
              <TextBtn>AI 润色</TextBtn>
              <span style={{ flex: 1 }} />
              <TextBtn onClick={() => setDraft(null)}>取消编辑</TextBtn>
            </Actions>
          </Stack>
        )}
      </div>
    </PageShell>
  );
}
