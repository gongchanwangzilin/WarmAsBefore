import { useEffect, useState } from 'react';
import { Card, Field, PageHeader, Row, Sep, TextBtn } from '../../components/kit';
import { PageShell } from '../../components/PageShell';
import { SignalButton } from '../../components/SignalButton';
import type { ScreenProps } from '../types';
import { Actions, FootBar, Lead, Note, Stack } from './parts';

const STEP_NAMES = ['世界', '人设', '细则', '开始'];

type WorldRow = { id: string; title: string; description: string; characters: number };

/**
 * 新开一局 / 本局设定：NewRunPage.xaml 的 ① 世界 ② 人设 ③ 细则 ④ 回顾。
 * 世界列表读 worldbook.list，主角名与 id 读 characters.list；「开始这一局」真的走 save.new，
 * 落盘成功才跳主界面，失败就把宿主那句话上屏。
 */
export function Newrun({ bridge, query, navigate, back }: ScreenProps) {
  const [step, setStep] = useState(0);
  const [persona, setPersona] = useState('');
  const [worldId, setWorldId] = useState('');
  const [status, setStatus] = useState('');
  const [busy, setBusy] = useState(false);
  const [charName, setCharName] = useState('');
  const [worlds, setWorlds] = useState<WorldRow[]>([]);
  const [worldsRead, setWorldsRead] = useState(false);

  const character = (query.get('character') ?? '').trim();

  useEffect(() => {
    let alive = true;
    void bridge
      .request('characters.list', {})
      .then(({ characters }) => {
        if (!alive) return;
        const hit = characters.find((row) => row.id === character);
        setCharName(hit?.nickname || hit?.name || '');
      })
      .catch(() => undefined);
    void bridge
      .request('worldbook.list', {})
      .then(({ worlds: rows }) => {
        if (!alive) return;
        setWorlds(rows.map((row) => ({ id: row.id, title: row.title, description: row.description, characters: row.characters })));
      })
      .catch(() => undefined)
      .finally(() => {
        if (alive) setWorldsRead(true);
      });
    return () => {
      alive = false;
    };
  }, [bridge, character]);

  const characterName = charName || 'TA';
  const world = worlds.find((row) => row.id === worldId) ?? null;
  const selectedWorldDisplay = world ? world.title : '不绑定 · 普通酒馆';
  const mapHint = world ? '地图可用 · 地点来自世界书' : '地图锁定 · 这一局只有对话';
  const personaCount = persona.trim().length === 0 ? '留空也可以' : `${persona.trim().length} 字`;
  const hasPersona = persona.trim().length > 0;
  const importHint = '可选。把微信/QQ 导出的聊天记录带进来，这一局的关系会从你们的过去开始。';

  const begin = async () => {
    if (!character) {
      setStatus('没有可进入的角色：先回角色库选一个人，再来开新的一局。');
      return;
    }
    if (busy) return;
    setBusy(true);
    setStatus('');
    try {
      await bridge.request('save.new', {
        characterId: character,
        persona: hasPersona ? persona.trim() : undefined,
        worldId: world?.id,
        worldName: world?.title,
      });
      navigate('/main');
    } catch (cause) {
      setStatus(cause instanceof Error ? cause.message : '这一局没有开起来：存档没有写进盘。');
    } finally {
      setBusy(false);
    }
  };

  return (
    <PageShell>
      <PageHeader
        title="新的一局"
        onBack={back}
        right={<span className="cap">{characterName}</span>}
      />

      <div className="screen" style={{ maxWidth: 880 }}>
        <Stack gap={2}>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 6, height: 4 }}>
            {STEP_NAMES.map((name, index) => (
              <i
                key={name}
                style={{
                  display: 'block',
                  borderRadius: 2,
                  background: step >= index ? 'var(--w-signal)' : 'var(--w-hairline)',
                }}
              />
            ))}
          </div>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr auto auto', alignItems: 'center', gap: 'var(--w-space-3)' }}>
            <span className="eyebrow">{STEP_NAMES[step]}</span>
            {step > 0 ? (
              <TextBtn onClick={() => setStep(step - 1)}>← 上一步</TextBtn>
            ) : (
              <span />
            )}
            <span className="cap" style={{ fontFamily: 'var(--w-font-num)' }}>{`${step + 1} / 4`}</span>
          </div>
        </Stack>

        <Stack gap={4}>
          {step === 0 ? (
            <Stack gap={3}>
              <Lead eyebrow="01 · WORLD" title="这段关系发生在哪个世界" sub={`和「${characterName}」的这段相处，从哪里开始？`} />

              <Stack gap={2}>
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
                  <span style={{ fontSize: 'var(--w-size-subhead)', color: 'var(--w-ink-display)' }}>世界书</span>
                  <TextBtn onClick={() => navigate('/worldbook')}>管理世界书</TextBtn>
                </div>

                <Card>
                  {worlds.map((row) => (
                    <div key={row.id}>
                      <Row
                        title={
                          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
                            {worldId === row.id ? (
                              <i style={{ width: 3, height: 24, borderRadius: 2, background: 'var(--w-signal)' }} />
                            ) : null}
                            {row.title || '未命名世界'}
                          </span>
                        }
                        desc={row.description.trim() || '没有写设定'}
                        right={
                          <span style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 2 }}>
                            {worldId === row.id ? <span className="cap">已选</span> : null}
                            <span className="cap">{row.characters > 0 ? `${row.characters} 位角色` : '没有角色'}</span>
                          </span>
                        }
                        onClick={() => setWorldId(row.id)}
                      />
                      <Sep />
                    </div>
                  ))}
                  <Row
                    title={
                      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
                        {worldId === '' ? (
                          <i style={{ width: 3, height: 24, borderRadius: 2, background: 'var(--w-signal)' }} />
                        ) : null}
                        {'不绑定 · 普通酒馆'}
                      </span>
                    }
                    desc="只聊天，没有地图。TA 不会假装知道你们在哪儿。"
                    right={
                      <span style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 2 }}>
                        {worldId === '' ? <span className="cap">已选</span> : null}
                        <span className="cap">无地图</span>
                      </span>
                    }
                    onClick={() => setWorldId('')}
                  />
                  {worldsRead && worlds.length === 0 ? (
                    <p className="cap" style={{ margin: 0 }}>
                      还没有世界书。到「世界书」里手写一份，或者让 AI 帮你造一个世界。
                    </p>
                  ) : null}
                </Card>

                <Note>不绑定也能开始：那就是普通酒馆——只聊天，地图与移动会锁上。</Note>
              </Stack>
            </Stack>
          ) : null}

          {step === 1 ? (
            <Stack gap={3}>
              <Lead
                eyebrow="02 · YOU"
                title="你是谁"
                sub="写你在这段关系里的身份：工作、习惯、和 TA 的关系、已经说好的事。留空也可以。"
              />

              <Card>
                <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
                  <span className="cap" style={{ margin: 0 }}>人设</span>
                  <span className="cap">{personaCount}</span>
                </div>
                <Field
                  multiline
                  label="人设"
                  value={persona}
                  onChange={setPersona}
                  placeholder="比如：我叫林晚，做建筑设计，和 TA 已经结婚两年。怕冷，早上一定要喝一杯热茶；不喜欢被临时改计划。"
                />
              </Card>

              <Note>人设不会整段塞进上下文：它会被整理成 TA 本来就知道的记忆，需要的时候才想起来。</Note>
            </Stack>
          ) : null}

          <Card>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-2)' }}>
              <Stack gap={1}>
                <span style={{ fontSize: 'var(--w-size-subhead)', color: 'var(--w-ink-display)' }}>从聊天记录开始</span>
                <span className="cap">{importHint}</span>
              </Stack>
              <TextBtn onClick={() => navigate(character ? `/chatimport?character=${encodeURIComponent(character)}` : '/chatimport')}>
                导入…
              </TextBtn>
            </div>
          </Card>

          {step === 2 ? (
            <Stack gap={3}>
              <Lead
                eyebrow="03 · DETAILS"
                title="开局细则"
                sub="没有额外要填的：这一页只把已经选好的东西摆清楚。之后也能随时从主界面回来改。"
              />

              <Card>
                <Row
                  title={
                    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
                      <span className="cap" style={{ minWidth: 48 }}>TA</span>
                      {characterName}
                    </span>
                  }
                  right={<span className="cap">这一局的主角</span>}
                />
                <Sep />
                <Row
                  title={
                    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
                      <span className="cap" style={{ minWidth: 48 }}>世界</span>
                      <span style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                        <span>{selectedWorldDisplay}</span>
                        <span className="cap">{mapHint}</span>
                      </span>
                    </span>
                  }
                  right={<TextBtn onClick={() => setStep(0)}>改</TextBtn>}
                />
                <Sep />
                <Row
                  title={
                    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
                      <span className="cap" style={{ minWidth: 48 }}>人设</span>
                      {personaCount}
                    </span>
                  }
                  right={<TextBtn onClick={() => setStep(1)}>去写</TextBtn>}
                />
              </Card>

              <Note>开始前会先整理人格记忆，再落盘；整理好的记忆不占用之后的每一轮上下文。</Note>
            </Stack>
          ) : null}

          {step === 3 ? (
            <Stack gap={3}>
              <Lead
                eyebrow="04 · BEGIN"
                title="就这样开始"
                sub="看一眼就好。开始之后，这个世界与你的人设会先变成 TA 的记忆。"
              />

              <Card>
                <Row
                  title={
                    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
                      <span className="cap" style={{ minWidth: 48 }}>与谁</span>
                      {characterName}
                    </span>
                  }
                />
                <Sep />
                <Row
                  title={
                    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
                      <span className="cap" style={{ minWidth: 48 }}>世界</span>
                      <span style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                        <span>{selectedWorldDisplay}</span>
                        <span className="cap">{mapHint}</span>
                      </span>
                    </span>
                  }
                />
                <Sep />
                <Row
                  title={
                    <span style={{ display: 'inline-flex', alignItems: 'flex-start', gap: 'var(--w-space-2)' }}>
                      <span className="cap" style={{ minWidth: 48 }}>人设</span>
                      {hasPersona ? (
                        <span style={{ fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)', whiteSpace: 'pre-wrap' }}>
                          {persona}
                        </span>
                      ) : (
                        <span className="cap">还没写。开始后 TA 会先按世界书与已有的记忆认识你。</span>
                      )}
                    </span>
                  }
                />
              </Card>
            </Stack>
          ) : null}

          {status ? <Note warn>{status}</Note> : null}
        </Stack>
      </div>

      <FootBar>
        <span className="cap">{selectedWorldDisplay}</span>
        <Actions>
          {step < 3 ? (
            <SignalButton onClick={() => setStep(step + 1)}>下一步</SignalButton>
          ) : character ? (
            <SignalButton onClick={() => void begin()}>{busy ? '正在开局…' : '开始这一局'}</SignalButton>
          ) : (
            <TextBtn onClick={() => navigate('/roster')}>先回角色库选一个人</TextBtn>
          )}
        </Actions>
      </FootBar>
    </PageShell>
  );
}
