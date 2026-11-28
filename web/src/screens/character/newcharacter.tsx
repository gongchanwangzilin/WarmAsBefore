import { useState } from 'react';
import { GlassButton } from '../../components/GlassButton';
import { Glass } from '../../components/Glass';
import { PageShell } from '../../components/PageShell';
import { Field, PageHeader } from '../../components/kit';
import { useIsWide } from '../../lib/useIsWide';
import type { ScreenProps } from '../types';
import {
  Actions,
  BodySm,
  Caption,
  ChipBtn,
  Chips,
  Eyebrow,
  Hair,
  Note,
  PrimaryBtn,
  Stack,
  SwitchLine,
  Well,
} from './parts';

/**
 * 新建角色：Views/Character/NewCharacterPage.xaml（467 行）+ NewCharacterViewModel.cs。
 * 一页四步 —— 基本 → 人设 → 形象 → 世界观；宽屏左预览右表单，窄屏上下叠。
 */

const STEPS = ['基本', '人设', '形象', '世界观'];
const LAST = STEPS.length - 1;

/** 出厂性别库（GenderLibrary 的内置四项）。 */
const GENDERS = ['女', '男', '其他', '保密'];

export function NewCharacter({ bridge, navigate, back }: ScreenProps) {
  const wide = useIsWide(860);
  const [step, setStep] = useState(0);
  const [name, setName] = useState('');
  const [gender, setGender] = useState('');
  const [address, setAddress] = useState('主人');
  const [romance, setRomance] = useState(true);
  const [personality, setPersonality] = useState('');
  const [description, setDescription] = useState('');
  const [greeting, setGreeting] = useState('');
  const [nameError, setNameError] = useState('');
  const [genderError, setGenderError] = useState('');
  const [createHint, setCreateHint] = useState('');
  const [busy, setBusy] = useState(false);

  const isLast = step >= LAST;
  const previewName = name.trim() ? name.trim() : '还没起名';
  const previewMeta = `${gender.trim() ? gender.trim() : '性别未选'} · ${romance ? '恋爱对象' : '伙伴'} · 称呼你「${
    address.trim() ? address.trim() : '主人'
  }」`;

  const validate = () => {
    const nextName = name.trim() ? '' : '名字不能空着：先给 TA 起个名字。';
    const nextGender = gender.trim() ? '' : '选一个性别：称呼与周期规则都从这里来。';
    setNameError(nextName);
    setGenderError(nextGender);
    return !nextName && !nextGender;
  };

  const next = () => {
    if (step === 0 && !validate()) return;
    if (step < LAST) setStep(step + 1);
  };

  const pick = (value: string) => {
    setGender(value);
    setGenderError('');
  };

  const create = () => {
    if (!validate()) {
      setStep(0);
      return;
    }
    if (busy) return;
    setBusy(true);
    setCreateHint('');
    bridge
      .request('character.create', {
        name: name.trim(),
        gender: gender.trim(),
        personality: personality.trim(),
        description: description.trim(),
        greeting: greeting.trim(),
        userAddress: address.trim(),
        romance,
      })
      // 真的落盘了才往工作室走：带着新 id 进去，那一页读的就是盘上这一份。
      .then((created) => navigate(`/studio?id=${encodeURIComponent(created.id)}`))
      .catch((error: unknown) => {
        setCreateHint(error instanceof Error ? error.message : '创建没有成功：宿主没有回应。');
        setBusy(false);
      });
  };

  return (
    <PageShell>
      <PageHeader
        title="新建角色"
        eyebrow="// NEW SUBJECT"
        onBack={back}
        right={<span className="cap" style={{ fontFamily: 'var(--w-font-num)' }}>{`${Math.min(step, LAST) + 1} / ${STEPS.length}`}</span>}
      />

      <div className="screen screen--full">
        <Stack gap={3}>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 6px 1fr 6px 1fr 6px 1fr', height: 4 }}>
            {STEPS.map((label, index) => (
              <i
                key={label}
                aria-hidden="true"
                style={{
                  gridColumn: index * 2 + 1,
                  borderRadius: 2,
                  background: step >= index ? 'var(--w-signal)' : 'var(--w-hairline)',
                }}
              />
            ))}
          </div>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
            <Eyebrow>{STEPS[step]}</Eyebrow>
            <Caption>随时可以回上一步</Caption>
          </div>
        </Stack>

        <div
          style={{
            display: 'grid',
            gridTemplateColumns: wide ? 'minmax(0, 1fr) minmax(0, 1fr)' : 'minmax(0, 1fr)',
            gap: 'var(--w-space-3)',
            alignItems: 'start',
          }}
        >
          {/* ── 左：预览卡 ─────────────────────────────────── */}
          <Glass as="section" tone="card" radius={12} style={{ padding: 'var(--w-pad-card)' }}>
            <Stack gap={3}>
              <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
                <Eyebrow>// PREVIEW</Eyebrow>
                <Caption>{`${Math.min(step, LAST) + 1} / ${STEPS.length}`}</Caption>
              </div>

              <div
                style={{
                  height: 360,
                  borderRadius: 4,
                  background: 'var(--w-surface-tint)',
                  display: 'grid',
                  placeItems: 'center',
                  textAlign: 'center',
                }}
              >
                <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 'var(--w-space-3)', padding: 'var(--w-pad-card)' }}>
                  <BodySm>还没有立绘或头像</BodySm>
                  <Caption>到「形象」那一步挑一张，这块就亮起来</Caption>
                </div>
              </div>

              <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                <span
                  style={{
                    fontSize: 26,
                    fontWeight: 600,
                    color: 'var(--w-ink-display)',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                    whiteSpace: 'nowrap',
                  }}
                >
                  {previewName}
                </span>
                <Caption>{previewMeta}</Caption>
                <span style={{ fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)', lineHeight: 1.5, marginTop: 4 }}>
                  {personality.trim() ? personality.trim() : '性格还没写'}
                </span>
              </div>

              <Hair />

              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', columnGap: 'var(--w-space-3)' }}>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
                  <Eyebrow>立绘</Eyebrow>
                  <Caption>还没有选立绘</Caption>
                </div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
                  <Eyebrow>头像</Eyebrow>
                  <Caption>还没有头像（创建时会先用立绘）</Caption>
                </div>
              </div>

              <Caption>这里只是预览：按「创建」之后，TA 才会出现在角色库与对话里。</Caption>
            </Stack>
          </Glass>

          {/* ── 右：分步表单 ───────────────────────────────── */}
          <Glass as="section" tone="card" radius={12} style={{ padding: 'var(--w-pad-card)' }}>
            <Stack gap={4}>
              {step === 0 ? (
                <Stack gap={3}>
                  <Lead eyebrow="01 · BASICS" title="先认识一下" sub="名字与性别是必填的；其余留空也能创建，之后在工作室里随时补。" />

                  <Stack gap={1}>
                    <FieldLabel>名字</FieldLabel>
                    <Field value={name} onChange={setName} placeholder="比如：林晚" />
                    {nameError ? <ErrorText>{nameError}</ErrorText> : null}
                  </Stack>

                  <Stack gap={1}>
                    <FieldLabel>性别</FieldLabel>
                    <Chips>
                      {GENDERS.map((item) => (
                        <ChipBtn key={item} label={item} on={gender === item} onClick={() => pick(item)} />
                      ))}
                    </Chips>
                    {genderError ? <ErrorText>{genderError}</ErrorText> : null}
                    <Caption>候选来自本机性别库；想要别的，去设置 → 性别库加一个。</Caption>
                  </Stack>

                  <Stack gap={1}>
                    <FieldLabel>TA 怎么称呼你</FieldLabel>
                    <Field value={address} onChange={setAddress} placeholder="主人 / 哥哥 / 名字" />
                  </Stack>

                  <SwitchLine
                    title="恋爱对象"
                    desc="关掉后 TA 不会主动推进关系，周期与亲密内容也不进提示词。"
                    checked={romance}
                    onChange={setRomance}
                  />
                </Stack>
              ) : null}

              {step === 1 ? (
                <Stack gap={3}>
                  <Lead eyebrow="02 · PERSONA" title="TA 是什么样的人" sub="这三段直接进提示词。写具体一点，TA 回话时才有东西可抓。" />

                  <Stack gap={1}>
                    <FieldLabel>性格</FieldLabel>
                    <Field
                      value={personality}
                      onChange={setPersonality}
                      placeholder="说话方式与脾气：嘴硬心软、慢热、爱开玩笑……"
                      multiline
                    />
                  </Stack>

                  <Stack gap={1}>
                    <FieldLabel>简介 / 前情</FieldLabel>
                    <Field
                      value={description}
                      onChange={setDescription}
                      placeholder="你们之间发生过什么、TA 现在过着怎样的日子"
                      multiline
                    />
                  </Stack>

                  <Stack gap={1}>
                    <FieldLabel>开场白</FieldLabel>
                    <Field value={greeting} onChange={setGreeting} placeholder="TA 上线第一句说什么" multiline />
                  </Stack>
                </Stack>
              ) : null}

              {step === 2 ? (
                <Stack gap={3}>
                  <Lead
                    eyebrow="03 · IMAGE"
                    title="让 TA 站到光里"
                    sub="立绘先支持一张默认表情；头像会用在列表、对话与手机里。两样都能之后在工作室换。"
                  />

                  <Well>
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
                      <BodySm>立绘</BodySm>
                      <Caption>还没有选立绘</Caption>
                    </div>
                    <Actions>
                      <GlassButton size="sm" className="is-off">
                        选一张立绘
                      </GlassButton>
                    </Actions>
                    <Caption>这张会记成 default/normal；要更多表情，创建后去工作室补。</Caption>
                  </Well>

                  <Well>
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
                      <BodySm>头像</BodySm>
                      <Caption>还没有头像（创建时会先用立绘）</Caption>
                    </div>
                    <Actions>
                      <PrimaryBtn disabled>选头像并裁剪</PrimaryBtn>
                      <GlassButton size="sm" className="is-off">
                        直接用这张图
                      </GlassButton>
                    </Actions>
                    <Caption>裁剪会框成方脸，存成 512×512；直接用原图会保留原尺寸。</Caption>
                  </Well>

                  <Caption>没有图也能创建：TA 会先以姓名首字出现在列表里，之后随时补图。</Caption>
                </Stack>
              ) : null}

              {step === 3 ? (
                <Stack gap={3}>
                  <Lead eyebrow="04 · WORLD" title="TA 从哪个世界来" sub="这一步可以整步跳过：不绑世界书也能创建，之后在工作室里补。" />

                  <Stack gap={2}>
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
                      <span style={{ fontSize: 'var(--w-size-subhead)', color: 'var(--w-ink-display)' }}>世界书（可多选）</span>
                      <Caption>未选择</Caption>
                    </div>

                    <Glass as="div" tone="card" radius={12} style={{ padding: 0 }}>
                      <div style={{ padding: 'var(--w-space-2) var(--w-pad-card)' }}>
                        <Caption>
                          还没有世界书。可以先去「世界书」手写一份，或者让 AI 造一个；这一步跳过也不影响创建。
                        </Caption>
                      </div>
                    </Glass>

                    <Caption>不绑定世界书（可以跳过）</Caption>
                  </Stack>

                  <Well>
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
                      <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                        <BodySm>语音引擎</BodySm>
                        <Caption>用哪套声音说话</Caption>
                      </div>
                      <Caption>跟随全局</Caption>
                    </div>
                    <Hair />
                    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
                      <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                        <BodySm>AI 模型</BodySm>
                        <Caption>用哪一档模型回话</Caption>
                      </div>
                      <Caption>跟随全局</Caption>
                    </div>
                    <Caption>想给 TA 单独指定，创建后在工作室的「记忆 / 素材」里改；这里先跟着全局。</Caption>
                  </Well>
                </Stack>
              ) : null}
            </Stack>
          </Glass>
        </div>
      </div>

      <div style={{ position: 'sticky', bottom: 0, zIndex: 5, borderTop: '1px solid var(--w-hairline)' }}>
        <Glass as="div" tone="bar" radius={0}>
          <div
            style={{
              display: 'grid',
              gridTemplateColumns: 'auto minmax(0,1fr) auto',
              alignItems: 'center',
              gap: 'var(--w-space-3)',
              padding: 'var(--w-space-2) var(--w-pad-page)',
            }}
          >
            {step > 0 ? (
              <button
                type="button"
                onClick={() => setStep(step - 1)}
                style={{
                  border: 0,
                  background: 'none',
                  padding: '2px 6px',
                  font: 'inherit',
                  fontSize: 'var(--w-size-caption)',
                  color: 'var(--w-ink-muted)',
                  cursor: 'pointer',
                }}
              >
                ← 上一步
              </button>
            ) : (
              <span />
            )}
            <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)', textAlign: 'center' }}>
              {STEPS[step]}
            </span>
            <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-3)' }}>
              {isLast ? (
                <PrimaryBtn onClick={create} disabled={busy}>
                  创建角色
                </PrimaryBtn>
              ) : (
                <PrimaryBtn onClick={next}>下一步 →</PrimaryBtn>
              )}
            </div>
          </div>
          {createHint ? (
            <p style={{ margin: 0, padding: '0 var(--w-pad-page) var(--w-space-2)', fontSize: 'var(--w-size-caption)', color: 'var(--w-signal)', lineHeight: 1.6 }}>
              {createHint}
            </p>
          ) : null}
        </Glass>
      </div>
    </PageShell>
  );
}

/** 字段标签（FieldLabel：字号小、色弱、压在框上）。 */
function FieldLabel({ children }: { children: string }) {
  return (
    <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)', margin: '0 0 -1px 2px' }}>{children}</span>
  );
}

/** 就地报错（ErrorText）。 */
function ErrorText({ children }: { children: string }) {
  return (
    <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-signal)', margin: '4px 0 0 2px', lineHeight: 1.5 }}>
      {children}
    </span>
  );
}

/** 步首：眉标 + 大标题 + 一句说明（Eyebrow / StepTitle / StepSub）。 */
function Lead({ eyebrow, title, sub }: { eyebrow: string; title: string; sub: string }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-1)' }}>
      <Eyebrow>{eyebrow}</Eyebrow>
      <h2
        style={{
          margin: 0,
          fontSize: 'var(--w-size-display)',
          fontWeight: 600,
          color: 'var(--w-ink-display)',
          lineHeight: 1.35,
        }}
      >
        {title}
      </h2>
      <Note>{sub}</Note>
    </div>
  );
}
