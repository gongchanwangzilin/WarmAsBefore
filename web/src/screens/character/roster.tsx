import { useEffect, useState } from 'react';
import type { CharacterRow } from '../../bridge/protocol';
import { GlassButton } from '../../components/GlassButton';
import { Glass } from '../../components/Glass';
import { PageShell } from '../../components/PageShell';
import { Empty, PageHeader, TextBtn } from '../../components/kit';
import { useIsWide } from '../../lib/useIsWide';
import type { ScreenProps } from '../types';
import { BodySm, Caption, Eyebrow, Hair, PaneTitle, PrimaryBtn, Stack } from './parts';

/**
 * 角色库：Views/Character/CharacterLibraryPage.xaml（309 行）+ CharacterLibraryViewModel.cs。
 * 左边名册只负责「选」，右边预览负责「看」，改设定去工作室。
 */

/** 名册里的一行人（CharacterLibraryItem）。头像、立绘都没有时露姓名首字。 */
type RosterItem = {
  id: string;
  name: string;
  personality: string;
  description: string;
  /** 这个角色进过几份存档（「在 N 份存档里」/「还没进过存档」） */
  saveCount: number;
  /** 立绘格数（SPRITES 那一格） */
  cellCount: number;
  /** 好感点数（0–1000，角色库只给一瞥：等级用大字） */
  affection: number;
  /** 头像地址（宿主素材主机）；空 = 没有头像 */
  avatarUrl?: string;
};

/** Helpers.AffectionScale：每 100 点一级，共 10 级，0 点也算第 1 级。 */
const levelOf = (points: number) => (points <= 0 ? 1 : Math.min(10, Math.floor((points - 1) / 100) + 1));

export function Roster({ bridge, navigate, back }: ScreenProps) {
  const wide = useIsWide(720);
  const [items, setItems] = useState<RosterItem[]>([]);
  const [selectedId, setSelectedId] = useState('');

  // 真的花名册：CharacterLibrary.ListAsync()。没有角色就留空，走源码的空态。
  useEffect(() => {
    let dropped = false;
    const read = async () => {
      try {
        const { characters } = await bridge.request('characters.list', {});
        if (dropped) return;
        setItems(
          characters.map((c: CharacterRow) => ({
            id: c.id,
            name: c.nickname || c.name,
            personality: c.catchphrase || c.gender || '',
            description: '',
            saveCount: 0,
            cellCount: c.spriteCount ?? 0,
            affection: c.affection,
            avatarUrl: c.avatarUrl || '',
          })),
        );
        setSelectedId((prev) => (characters.some((c: CharacterRow) => c.id === prev) ? prev : characters[0]?.id ?? ''));
      } catch {
        /* 老宿主没有这个方法：名册留空，走源码的空态 */
      }
    };
    void read();
    const off = bridge.onEvent('data.changed', () => void read());
    return () => {
      dropped = true;
      off();
    };
  }, [bridge]);
  const selected = items.find((item) => item.id === selectedId) ?? null;
  const countLabel = items.length === 0 ? '还没有人' : `${items.length} 个角色`;

  const enter = () => {
    if (selected) navigate(`/studio?id=${encodeURIComponent(selected.id)}`);
  };

  return (
    <PageShell>
      <PageHeader
        title="角色库"
        eyebrow="// ROSTER"
        onBack={back}
        right={
          <>
            <GlassButton size="sm">监测</GlassButton>
            <span
              className="cap"
              style={{
                padding: '6px 10px',
                borderRadius: 4,
                background: 'var(--w-surface-tint)',
                display: wide ? undefined : 'none',
              }}
            >
              AI 也会用到其他角色
            </span>
            <PrimaryBtn onClick={() => navigate('/newcharacter')}>＋ 新建角色</PrimaryBtn>
          </>
        }
      />

      <div className="screen screen--full">
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: wide ? '372px minmax(0, 1fr)' : 'minmax(0, 1fr)',
            gap: 'var(--w-space-3)',
            alignItems: 'stretch',
          }}
        >
          {/* ── 名册 ─────────────────────────────────────────── */}
          <Glass as="section" tone="card" radius={12} style={{ padding: 0, minHeight: wide ? 0 : 300 }}>
            <div style={{ display: 'flex', flexDirection: 'column', minHeight: 0 }}>
              <div
                style={{
                  display: 'flex',
                  alignItems: 'flex-end',
                  justifyContent: 'space-between',
                  gap: 'var(--w-space-2)',
                  padding: 'var(--w-space-2) var(--w-pad-card)',
                }}
              >
                <div style={{ display: 'flex', flexDirection: 'column' }}>
                  <Eyebrow>// ALL SUBJECTS</Eyebrow>
                  <PaneTitle>这里的人</PaneTitle>
                </div>
                <Caption>{countLabel}</Caption>
              </div>
              <Hair />

              <div style={{ flex: 1, minHeight: 0, overflowY: 'auto' }}>
                {items.length === 0 ? (
                  <Empty
                    title="角色库还是空的"
                    reason="名册里还没有任何人"
                    hint="在角色选择页新建或导入角色，新角色会出现在这里"
                  />
                ) : (
                  items.map((item) => {
                    const on = item.id === selectedId;
                    return (
                      <div key={item.id} style={{ position: 'relative' }}>
                        {on ? (
                          <i
                            aria-hidden="true"
                            style={{
                              position: 'absolute',
                              left: 0,
                              top: 0,
                              bottom: 0,
                              width: 2,
                              background: 'var(--w-signal)',
                            }}
                          />
                        ) : null}
                        <div
                          role="button"
                          tabIndex={0}
                          onClick={() => setSelectedId(item.id)}
                          onKeyDown={(event) => {
                            if (event.key === 'Enter' || event.key === ' ') setSelectedId(item.id);
                          }}
                          style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: 'var(--w-space-3)',
                            width: '100%',
                            padding: 'var(--w-space-2) var(--w-pad-card)',
                            border: 0,
                            background: 'none',
                            font: 'inherit',
                            textAlign: 'left',
                            cursor: 'pointer',
                          }}
                        >
                          <span
                            style={{
                              width: 46,
                              height: 46,
                              flex: 'none',
                              display: 'grid',
                              placeItems: 'center',
                              borderRadius: 4,
                              background: 'var(--w-surface-tint)',
                              color: 'var(--w-ink-display)',
                              fontSize: 'var(--w-size-body)',
                            }}
                          >
                            {item.name.slice(0, 1) || '?'}
                          </span>
                          <span style={{ display: 'flex', flexDirection: 'column', gap: 1, minWidth: 0, flex: 1 }}>
                            <span
                              style={{
                                fontSize: 'var(--w-size-body)',
                                color: 'var(--w-ink-display)',
                                overflow: 'hidden',
                                textOverflow: 'ellipsis',
                                whiteSpace: 'nowrap',
                              }}
                            >
                              {item.name}
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
                              {item.personality}
                            </span>
                          </span>
                          <span style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-1)' }}>
                            <span onClick={(event) => event.stopPropagation()}>
                              <TextBtn>导出</TextBtn>
                            </span>
                            <span style={{ color: 'var(--w-ink-faint)', fontSize: 18 }}>›</span>
                          </span>
                        </div>
                        <Hair />
                      </div>
                    );
                  })
                )}
              </div>
            </div>
          </Glass>

          {/* ── 预览 ─────────────────────────────────────────── */}
          <Glass as="section" tone="card" radius={12} style={{ padding: 0 }}>
            {selected ? (
              <div style={{ display: 'flex', flexDirection: 'column', minHeight: '100%' }}>
                <div
                  style={{
                    position: 'relative',
                    minHeight: 300,
                    flex: 1,
                    borderRadius: '12px 12px 0 0',
                    background: 'var(--w-surface-tint)',
                    display: 'grid',
                    placeItems: 'center',
                    overflow: 'hidden',
                  }}
                >
                  <span style={{ position: 'absolute', left: 16, top: 16 }}>
                    <Eyebrow>// SUBJECT</Eyebrow>
                  </span>
                  <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 'var(--w-space-3)' }}>
                    <BodySm>还没有立绘</BodySm>
                    <Caption>进 TA 的工作室生成一套，或从 zip 补进来</Caption>
                  </div>
                </div>

                <div style={{ padding: 'var(--w-pad-card)' }}>
                  <Stack gap={3}>
                    <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
                      <div style={{ display: 'flex', flexDirection: 'column' }}>
                        <Eyebrow>// SUBJECT</Eyebrow>
                        <h2
                          style={{
                            margin: 0,
                            fontSize: 'var(--w-size-display)',
                            fontWeight: 600,
                            letterSpacing: '-0.5px',
                            color: 'var(--w-ink-display)',
                            lineHeight: 1.3,
                          }}
                        >
                          {selected.name}
                        </h2>
                        {selected.avatarUrl ? (
                          <img
                            src={selected.avatarUrl}
                            alt={selected.name}
                            style={{
                              width: 56,
                              height: 56,
                              borderRadius: 12,
                              objectFit: 'cover',
                              border: '1px solid var(--w-hairline)',
                              flex: 'none',
                            }}
                          />
                        ) : null}
                      </div>
                      <span
                        style={{
                          padding: '3px 8px',
                          borderRadius: 4,
                          background: 'var(--w-surface-tint)',
                          fontSize: 'var(--w-size-caption)',
                          color: 'var(--w-ink-muted)',
                          whiteSpace: 'nowrap',
                        }}
                      >
                        {selected.saveCount > 0 ? `在 ${selected.saveCount} 份存档里` : '还没进过存档'}
                      </span>
                    </div>

                    <BodySm>{selected.personality || '还没写性格'}</BodySm>
                    <Caption>
                      {selected.description || '还没有背景故事：进工作室补一段，AI 才会照着这个人回话。'}
                    </Caption>

                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
                      <Caption>还没有专属背景：点「换背景」选一张图 / GIF / 视频。</Caption>
                      <GlassButton size="sm">换背景</GlassButton>
                    </div>

                    <Hair />

                    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1px 1fr 1px 1fr', columnGap: 'var(--w-space-2)' }}>
                      <Reading
                        eyebrow="AFFECTION"
                        value={`Lv.${levelOf(selected.affection)}`}
                        note={`好感 · ${Math.max(0, Math.min(1000, selected.affection))}/1000`}
                      />
                      <i style={{ background: 'var(--w-hairline)' }} />
                      <Reading eyebrow="SPRITES" value={String(selected.cellCount)} note="张立绘" />
                      <i style={{ background: 'var(--w-hairline)' }} />
                      <Reading eyebrow="SAVES" value={String(selected.saveCount)} note="个存档" />
                    </div>

                    <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0,1fr) auto', columnGap: 'var(--w-space-3)', alignItems: 'center' }}>
                      <PrimaryBtn block onClick={enter}>
                        进入 TA 的工作室  →
                      </PrimaryBtn>
                      <TextBtn>删除</TextBtn>
                    </div>
                  </Stack>
                </div>
              </div>
            ) : (
              <div
                style={{
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  justifyContent: 'center',
                  gap: 'var(--w-space-1)',
                  padding: 'var(--w-pad-card)',
                  minHeight: 300,
                  textAlign: 'center',
                }}
              >
                <Eyebrow>// NO SUBJECTS</Eyebrow>
                <PaneTitle>还没有人可以站上这块舞台</PaneTitle>
                <Caption>去角色选择页新建或导入一个角色，回来就能在这里看见 TA。</Caption>
              </div>
            )}
          </Glass>
        </div>
      </div>
    </PageShell>
  );
}

/** 读数条里的一格（Eyebrow + 大数字 + 小标签）。 */
function Reading({ eyebrow, value, note }: { eyebrow: string; value: string; note: string }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
      <Eyebrow>{eyebrow}</Eyebrow>
      <div style={{ display: 'flex', alignItems: 'flex-end', gap: 'var(--w-space-1)' }}>
        <span
          style={{
            fontFamily: 'var(--w-font-num)',
            fontSize: 24,
            color: 'var(--w-ink-display)',
            fontVariantNumeric: 'tabular-nums',
          }}
        >
          {value}
        </span>
        <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)', paddingBottom: 4 }}>{note}</span>
      </div>
    </div>
  );
}
