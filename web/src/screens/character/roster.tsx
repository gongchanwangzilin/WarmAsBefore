import { useEffect, useState } from 'react';
import type { CharacterRow } from '../../bridge/protocol';
import { GlassButton } from '../../components/GlassButton';
import { Glass } from '../../components/Glass';
import { PageShell } from '../../components/PageShell';
import { Empty, PageHeader, TextBtn } from '../../components/kit';
import { useIsWide } from '../../lib/useIsWide';
import { importAvatar } from '../../lib/avatar';
import { NEED_SAVE, useRun } from '../../lib/run';
import type { ScreenProps } from '../types';
import { BodySm, Caption, Eyebrow, Hair, PaneTitle, PrimaryBtn, Stack } from './parts';
import './roster.css';

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
  /** 当前该显示的那张立绘（后端按服装+情绪解析）；空 = 没有立绘 */
  spriteUrl?: string;
};

/** Helpers.AffectionScale：每 100 点一级，共 10 级，0 点也算第 1 级。 */
const levelOf = (points: number) => (points <= 0 ? 1 : Math.min(10, Math.floor((points - 1) / 100) + 1));

export function Roster({ bridge, navigate, back }: ScreenProps) {
  const wide = useIsWide(720);
  const run = useRun(bridge);
  /** 监测画的是这一局的状态：没进存档就灰着。 */
  const outsideSave = run.known && !run.active;
  const [items, setItems] = useState<RosterItem[]>([]);
  const [selectedId, setSelectedId] = useState('');
  const [status, setStatus] = useState('');
  const [busyId, setBusyId] = useState('');

  // 真的花名册：CharacterLibrary.ListAsync()。没有角色就留空，走源码的空态。
  useEffect(() => {
    let dropped = false;
    const read = async () => {
      try {
        const [{ characters }, saves] = await Promise.all([
          bridge.request('characters.list', {}),
          bridge.request('saves.list', {}).catch(() => null),
        ]);
        if (dropped) return;
        // 「在 N 份存档里」按存档表里的角色 id 数（SaveSlot.Character 存的就是 id）：读不到存档表时整格退回「还没进过存档」，不编数。
        const perId = new Map<string, number>();
        for (const row of saves?.slots ?? []) {
          if (!row.character) continue;
          perId.set(row.character, (perId.get(row.character) ?? 0) + 1);
        }
        setItems(
          characters.map((c: CharacterRow) => ({
            id: c.id,
            name: c.nickname || c.name,
            personality: c.catchphrase || c.gender || '',
            description: '',
            saveCount: perId.get(c.id) ?? 0,
            cellCount: c.spriteCount ?? 0,
            affection: c.affection,
            avatarUrl: c.avatarUrl || '',
            spriteUrl: c.currentSpriteUrl || '',
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

  const failure = (error: unknown) =>
    error instanceof Error ? error.message : '宿主没有回应，这一步没做完。';

  // 导出：宿主打单角色包，再把包交给系统分享面板。成没成都照宿主那句话上屏，不自己编「已导出」。
  const exportOne = (item: RosterItem) => {
    if (busyId) return;
    setBusyId(item.id);
    setStatus('');
    bridge
      .request('character.export', { id: item.id })
      .then((result) => setStatus(result.message))
      .catch((error: unknown) => setStatus(failure(error)))
      .finally(() => setBusyId(''));
  };

  // 删除：确认框里把「有 N 份存档把它当主角」说清楚，删除失败就把宿主那句原样上屏。
  const remove = (item: RosterItem) => {
    if (busyId) return;
    const warn =
      item.saveCount > 0
        ? `\n\n有 ${item.saveCount} 份存档把它当作主角：删除后那些存档里这个人的设定与立绘都会消失，进度回不来。`
        : '';
    if (!window.confirm(`删除「${item.name}」的角色资料与立绘？这个操作不可恢复。${warn}`)) return;
    setBusyId(item.id);
    setStatus('');
    bridge
      .request('character.delete', { id: item.id })
      .then((result) => {
        setStatus(result.message);
        // 名册靠 data.changed 重读；这里先把选中挪开，免得删完预览还停在刚删掉的那个人身上。
        setSelectedId('');
      })
      .catch((error: unknown) => setStatus(failure(error)))
      .finally(() => setBusyId(''));
  };

  return (
    <PageShell>
      <PageHeader
        title="角色库"
        eyebrow="// ROSTER"
        // 那句提示是说明，不是动作：它跟着标题走，不跟动作簇抢位置。
        subtitle={wide ? 'AI 也会用到其他角色' : undefined}
        onBack={back}
        right={
          <>
            <GlassButton
              size="sm"
              onClick={() => navigate('/superMonitor')}
              disabled={outsideSave}
              title={outsideSave ? NEED_SAVE : '这一局的监测图'}
            >
              监测
            </GlassButton>
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
          <Glass
            as="section"
            tone="card"
            radius={12}
            className="cr-roster"
            style={{
              padding: 0,
              minHeight: wide ? 0 : 300,
              height: wide ? '100%' : undefined,
              maxHeight: wide ? 'calc(100vh - 96px)' : undefined,
            }}
          >
            <div style={{ display: 'flex', flexDirection: 'column', minHeight: 0, height: '100%' }}>
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

              <div className="cr-roster__list">
                {items.length === 0 ? (
                  <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-start', gap: 'var(--w-space-2)', padding: 'var(--w-pad-card)' }}>
                    <Empty
                      title="角色库还是空的"
                      reason="名册里还没有任何人"
                      hint="在角色选择页新建或导入角色，新角色会出现在这里"
                    />
                    <GlassButton size="md" onClick={() => navigate('/newcharacter')}>＋ 新建角色</GlassButton>
                  </div>
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
                          className="cr-who"
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
                              overflow: 'hidden',
                            }}
                          >
                            {item.avatarUrl ? (
                              <img
                                src={item.avatarUrl}
                                alt=""
                                style={{ width: '100%', height: '100%', objectFit: 'cover', display: 'block' }}
                              />
                            ) : (
                              item.name.slice(0, 1) || '?'
                            )}
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
                              <TextBtn onClick={() => exportOne(item)} disabled={busyId === item.id}>
                                {busyId === item.id ? '导出中…' : '导出'}
                              </TextBtn>
                            </span>
                            <span className="k-row__chev" aria-hidden="true" />
                          </span>
                        </div>
                        <Hair />
                      </div>
                    );
                  })
                )}
              </div>

              <div style={{ flex: 'none', background: 'var(--w-surface-tint)', padding: 'var(--w-space-2) var(--w-pad-card)' }}>
                <Caption>点一行看右边的预览；改设定再进 TA 的工作室。</Caption>
                {status ? (
                  <p
                    style={{
                      margin: '2px 0 0',
                      fontSize: 'var(--w-size-caption)',
                      color: 'var(--w-ink-body)',
                      lineHeight: 1.5,
                      wordBreak: 'break-word',
                    }}
                  >
                    {status}
                  </p>
                ) : null}
              </div>
            </div>
          </Glass>

          {/* ── 预览 ─────────────────────────────────────────── */}
          <Glass as="section" tone="card" radius={12} className="cr-preview" style={{ padding: 0 }}>
            {selected ? (
              <div style={{ display: 'flex', flexDirection: 'column', height: '100%', minHeight: 0 }}>
                <div
                  style={{
                    position: 'relative',
                    flex: '1 1 auto',
                    minHeight: 'clamp(260px, 46vh, 520px)',
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
                  {selected.spriteUrl ? (
                    <span style={{ position: 'absolute', right: 12, top: 12 }}>
                      <GlassButton
                        size="sm"
                        onClick={() => navigate(`/characterView?id=${encodeURIComponent(selected.id)}`)}
                        title="整屏看 TA 的立绘、换服装与表情"
                      >
                        仔细看
                      </GlassButton>
                    </span>
                  ) : null}
                  {selected.spriteUrl ? (
                    <img
                      src={selected.spriteUrl}
                      alt={selected.name}
                      style={{ position: 'absolute', inset: 0, width: '100%', height: '100%', objectFit: 'contain', objectPosition: 'bottom center', display: 'block', padding: 'var(--w-space-3)' }}
                    />
                  ) : (
                    <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 'var(--w-space-3)' }}>
                      <BodySm>还没有立绘</BodySm>
                      <Caption>进 TA 的工作室生成一套，或从 zip 补进来</Caption>
                    </div>
                  )}
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
                        <button
                          type="button"
                          className="cr-avatar"
                          title={selected.avatarUrl ? '点头像换一张' : '点头像导入头像'}
                          onClick={() => {
                            void importAvatar(bridge, selected.id).then((url) => {
                              if (!url) return;
                              setItems((list) =>
                                list.map((item) => (item.id === selected.id ? { ...item, avatarUrl: url } : item)),
                              );
                            });
                          }}
                        >
                          {selected.avatarUrl ? (
                            <img src={selected.avatarUrl} alt={selected.name} />
                          ) : (
                            <>
                              <span className="cr-avatar__plus">＋</span>
                              <span className="cr-avatar__hint">导入头像</span>
                            </>
                          )}
                        </button>
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
                      <GlassButton size="sm" className="is-off">
                        换背景
                      </GlassButton>
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

                    {/* 主行动是页头那颗「新建角色」：这里的是这一栏的收尾动作，实底不再重复。
                        删除是危险动作，单独占一行、靠行尾 —— 不和「进工作室」挤在同一行。 */}
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
                      <div style={{ display: 'flex', flexDirection: 'column' }}>
                        <GlassButton size="md" onClick={enter} title="进入 TA 的工作室">
                          进入 TA 的工作室  →
                        </GlassButton>
                      </div>
                      <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                        <TextBtn onClick={() => remove(selected)} disabled={busyId === selected.id}>
                          {busyId === selected.id ? '删除中…' : '删除'}
                        </TextBtn>
                      </div>
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
                <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-2)', marginTop: 'var(--w-space-2)' }}>
                  <GlassButton size="md" onClick={() => navigate('/newcharacter')}>＋ 新建角色</GlassButton>
                  <TextBtn onClick={() => navigate('/select')}>去角色选择</TextBtn>
                </div>
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
      <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'flex-end', gap: 'var(--w-space-1)' }}>
        <span
          style={{
            fontFamily: 'var(--w-font-num)',
            fontSize: 'var(--w-size-title)',
            lineHeight: 'var(--w-lh-title)',
            letterSpacing: '-0.015em',
            color: 'var(--w-ramp-1)',
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
