import { useEffect, useState, type ReactNode } from 'react';
import { Glass } from '../../components/Glass';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import { Field, PageHeader, TextBtn } from '../../components/kit';
import { useIsWide } from '../../lib/useIsWide';
import { importAvatar } from '../../lib/avatar';
import type { CharacterFields, Result } from '../../bridge/protocol';
import type { ScreenProps } from '../types';
import {
  Actions,
  BodySm,
  Caption,
  ChipBtn,
  Chips,
  Eyebrow,
  Hair,
  PaneTitle,
  PrimaryBtn,
  SectionHead,
  SliderLine,
  Stack,
  SwitchLine,
  Well,
} from './parts';

/**
 * 角色工作室：Views/Character/CharacterStudioPage.xaml（16 行壳）+ CharacterStudioPanel.xaml（1604 行）
 * 加 CharacterStudioViewModel*.cs。左边立绘舞台，右边七个页签的资料；窄窗上下排（源码断点 820）。
 *
 * 带 ?id= 进来时按 characters.get 读真实档案；没有 id 或读不到，就从 BlankDraft 那张空白卡起步：
 * 姓名与性别等人来填，其余各栏先落 CreateDefault 的出厂值。
 */

type Detail = NonNullable<Result<'characters.get'>>;

type Tab = 'set' | 'state' | 'art' | 'mem' | 'link' | 'talk' | 'cg';

const TABS: { key: Tab; label: string }[] = [
  { key: 'set', label: '01 设定' },
  { key: 'state', label: '02 状态' },
  { key: 'art', label: '03 素材' },
  { key: 'mem', label: '04 记忆' },
  { key: 'link', label: '05 关联' },
  { key: 'talk', label: '06 说句话' },
  { key: 'cg', label: '07 CG' },
];

/** 出厂性别库（GenderLibrary 内置四项）。 */
const GENDERS = ['女', '男', '其他', '保密'];

/** 心情档位（CharacterStudioViewModel.MoodChoices）：盘上存英文键，界面给人话标签。 */
const MOODS: { key: string; label: string }[] = [
  { key: 'normal', label: '平常' },
  { key: 'happy', label: '开心' },
  { key: 'shy', label: '害羞' },
  { key: 'sad', label: '低落' },
  { key: 'angry', label: '闹脾气' },
  { key: 'tired', label: '疲惫' },
  { key: 'worried', label: '不安' },
];

const OUTFITS = [{ key: 'default', label: '默认装扮' }];
const EMOTIONS = [{ key: 'normal', label: 'normal' }];

/** StudioCopy.OutfitName：default 有名字，其余直接用盘上的键。 */
function outfitLabel(key: string): string {
  if (key === 'default') return '默认装扮';
  if (key === '') return '（没有服装）';
  return key;
}

/** 记忆五层（MemoryLayers.Name / Describe）。 */
const LAYERS = [
  { name: '工作记忆', detail: '本轮对话的上下文，长度由「记忆条数」决定，超出即丢弃' },
  { name: '人类记忆', detail: '只记事件、印象、重要时刻与单张照片；重要的忘得慢，琐碎的自然淡忘' },
  { name: '关系网', detail: '词与词的联系，负责由一个词想起另一件事' },
  { name: '长时记忆库', detail: '所有对话与好感记录都在，只增不删，靠检索工具按需翻' },
  { name: '档案层', detail: '日记与回忆录，是整理给人看的稿子' },
];

const VOICE_ENGINES = [
  { key: '', label: '跟随全局' },
  { key: 'system', label: '系统语音' },
  { key: 'api', label: '外部 TTS 接口' },
  { key: 'sovits', label: 'GPT-SoVITS' },
];

const VOICE_API_MODES = [
  { key: '', label: '跟随全局' },
  { key: 'openai', label: 'OpenAI 兼容' },
  { key: 'sovits', label: 'GPT-SoVITS' },
];

type TalkLine = { id: number; who: string; text: string; fromMe: boolean };

type Fields = {
  name: string;
  nickname: string;
  userAddress: string;
  gender: string;
  personality: string;
  description: string;
  greeting: string;
  moral: string;
  life: string;
  biography: string;
  political: string;
  ideology: string;
  deepMemories: string;
  trauma: string;
  age: string;
  birthday: string;
  naturalAging: boolean;
  romance: boolean;
  affection: number;
  trust: number;
  energy: number;
  libido: number;
  libidoLocked: boolean;
  libidoLockValue: number;
  affectionPoints: string;
  location: string;
  mood: string;
  cycleTracked: boolean;
  cycleDay: string;
  cyclePhase: string;
  crossSave: boolean;
  ovMemoryTurns: string;
  ovContextLimit: string;
  ovTemperature: string;
  ovWindowDays: string;
  ovRecall: string;
  voiceEnabled: boolean;
  voiceEngine: string;
  voiceName: string;
  voiceExtra: string;
  voiceApiUrl: string;
  voiceApiKey: string;
  voiceApiMode: string;
  voiceTtsModel: string;
  allowed: boolean;
  duplicateName: string;
};

/**
 * 一份草案：CharacterLibrary.CreateDefault 的出厂值（性别「未知」、性格「温柔可爱」、
 * 背景那句「刚刚诞生的伙伴…」都是源码给的默认值）。姓名与称呼以外的四栏留空，等填。
 * 没有 ?id= 或 characters.get 取不到人时，就从草案起步，不假装盘上有人。
 */
const DRAFT: Fields = {
  name: '',
  nickname: '',
  userAddress: '主人',
  gender: '',
  personality: '温柔可爱',
  description: '刚刚诞生的伙伴，一切都是崭新的开始。',
  greeting: '',
  moral: '',
  life: '',
  biography: '',
  political: '',
  ideology: '',
  deepMemories: '',
  trauma: '',
  age: '',
  birthday: '',
  naturalAging: true,
  romance: true,
  affection: 0,
  trust: 0,
  energy: 100,
  libido: 20,
  libidoLocked: false,
  libidoLockValue: 20,
  affectionPoints: '',
  location: 'home',
  mood: 'normal',
  cycleTracked: false,
  cycleDay: '0',
  cyclePhase: '',
  crossSave: true,
  ovMemoryTurns: '',
  ovContextLimit: '',
  ovTemperature: '',
  ovWindowDays: '',
  ovRecall: '',
  voiceEnabled: true,
  voiceEngine: '',
  voiceName: '',
  voiceExtra: '',
  voiceApiUrl: '',
  voiceApiKey: '',
  voiceApiMode: '',
  voiceTtsModel: '',
  allowed: true,
  duplicateName: '（副本）',
};

/**
 * 工作室表单里真正能写回角色的那些键（character.save 的白名单）。
 * 页面没从盘上读出来的字段不进 patch：只写改过的，别的字段一个都不动。
 */
const SAVE_KEYS = new Set<keyof Fields>([
  'name',
  'nickname',
  'userAddress',
  'gender',
  'personality',
  'description',
  'greeting',
  'moral',
  'life',
  'biography',
  'political',
  'ideology',
  'deepMemories',
  'trauma',
  'age',
  'birthday',
  'naturalAging',
  'romance',
  'affection',
  'trust',
  'energy',
  'libido',
  'libidoLocked',
  'libidoLockValue',
  'mood',
  'location',
  'cycleTracked',
  'cycleDay',
  'cyclePhase',
  'ovMemoryTurns',
  'ovContextLimit',
  'ovTemperature',
  'ovWindowDays',
  'ovRecall',
  'voiceEnabled',
  'voiceEngine',
  'voiceName',
  'voiceExtra',
  'voiceApiUrl',
  'voiceApiKey',
  'voiceApiMode',
  'voiceTtsModel',
]);

type Ctx = {
  f: Fields;
  set: <K extends keyof Fields>(key: K, value: Fields[K], label: string) => void;
  say: (text: string) => void;
  advancedOpen: boolean;
  toggleAdvanced: () => void;
  navigate: (path: string) => void;
  resetSubProfile: () => void;
  facingLeft: boolean;
  setFacing: (next: boolean) => void;
  talkInput: string;
  setTalkInput: (next: string) => void;
  talkLines: TalkLine[];
  talkHint: string;
  talkSend: () => void;
  talkClear: () => void;
  outfits: { key: string; label: string }[];
  emotions: { key: string; label: string }[];
  outfit: string;
  emotion: string;
  setOutfit: (key: string) => void;
  setEmotion: (key: string) => void;
  previewUrl: string;
  avatarUrl: string;
  /** 点头像/点「换头像」：由宿主弹选图框，选完写回角色。 */
  changeAvatar: () => void;
  spriteOf: (emotion: string) => string;
};

export function Studio({ bridge, query, navigate, back }: ScreenProps) {
  const wide = useIsWide(820);
  const [isNew, setIsNew] = useState(true);
  const [f, setT] = useState<Fields>(DRAFT);
  const [saved, setSaved] = useState<Fields>(DRAFT);
  const [changed, setChanged] = useState<{ key: keyof Fields; label: string }[]>([]);
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState('先填姓名和性别，其余都能之后再补——按「建好角色」才会进名册。');
  const [tab, setTab] = useState<Tab>('set');
  const [advancedOpen, setAdvancedOpen] = useState(false);
  const [facingLeft, setFacingLeft] = useState(false);
  const [talkInput, setTalkInput] = useState('');
  const [talkLines, setTalkLines] = useState<TalkLine[]>([]);
  const [talkHint, setTalkHint] = useState('');
  const [nextLineId, setNextLineId] = useState(1);
  const [subject, setSubject] = useState<Detail | null>(null);
  const [outfit, setOutfit] = useState('default');
  const [emotion, setEmotion] = useState('normal');

  const charId = query.get('id')?.trim() ?? '';
  /** 在「新建」那一档建好之后拿到的 id：之后这颗按钮的执行对象就是它。 */
  const [createdId, setCreatedId] = useState('');
  const targetId = createdId || charId;

  useEffect(() => {
    if (!charId) return;
    let alive = true;
    bridge
      .request('characters.get', { id: charId })
      .then((found) => {
        if (!alive || !found) return;
        const filled: Fields = {
          ...DRAFT,
          name: found.name,
          nickname: found.nickname,
          gender: found.gender,
          age: found.age === null ? '' : String(found.age),
          affection: found.affection,
          trust: found.trust,
          energy: found.energy,
        };
        setSubject(found);
        setIsNew(false);
        setT(filled);
        setSaved(filled);
        setChanged([]);
        setStatus('');
        setOutfit(found.outfit || 'default');
        setEmotion(found.emotion || 'normal');
      })
      .catch(() => {
        if (alive) setStatus('这个角色的资料读不出来了。');
      });
    return () => {
      alive = false;
    };
  }, [bridge, charId]);

  const outfits = subject
    ? Array.from(new Set([subject.outfit, ...subject.emotions.map((item) => item.outfit)]))
        .filter((key) => key.length > 0)
        .map((key) => ({ key, label: outfitLabel(key) }))
    : OUTFITS;
  const emotions = subject
    ? Array.from(new Set(subject.emotions.filter((item) => item.outfit === outfit).map((item) => item.emotion)))
        .map((key) => ({ key, label: key }))
    : EMOTIONS;
  const avatarUrl = subject?.avatarUrl ?? '';
  const previewUrl =
    subject?.emotions.find((item) => item.outfit === outfit && item.emotion === emotion)?.url || avatarUrl;

  const dirty = changed.length > 0;
  const saveLabel = isNew ? '建好角色' : '保存';
  const headline = isNew ? '新建角色' : f.nickname.trim() ? `${f.name}·${f.nickname}` : f.name;
  const metaLine = isNew
    ? '先起个名字、选个性别，其余都能之后补'
    : subject
      ? subject.gender
      : `${f.gender} · 叫对方「${f.userAddress}」 · ${f.romance ? '恋爱对象' : '不恋爱'}`;
  const previewCaption = `${outfitLabel(outfit)} · ${emotion} · ${facingLeft ? '朝左' : '朝右'}`;

  const set = <K extends keyof Fields>(key: K, value: Fields[K], label: string) => {
    setT((prev) => ({ ...prev, [key]: value }));
    setChanged((list) => (list.some((item) => item.key === key) ? list : [...list, { key, label }]));
  };

  /**
   * 「保存」真的写盘：只把改动过、且桥认得的那几个键送过去（见 SAVE_KEYS），
   * 整份覆盖会把页面没从盘上读出来的字段冲成空值。写成了才说已保存，失败就把宿主那句话上屏。
   */
  const save = () => {
    if (!dirty) {
      setStatus('没有要保存的改动。');
      return;
    }
    if (isNew && !f.name.trim()) {
      setStatus('先给 TA 起个名字——没有名字进不了名册。');
      return;
    }
    if (isNew && !f.gender.trim()) {
      setStatus('性别也挑一颗（想要别的，设置 → 性别库里加一个，这里立刻多一颗）。');
      return;
    }
    if (busy) return;

    const patch: Record<string, unknown> = {};
    const skipped: string[] = [];
    for (const item of changed) {
      if (SAVE_KEYS.has(item.key)) patch[item.key] = f[item.key];
      else skipped.push(item.label);
    }
    const notWritten = skipped.length ? `这几项不在角色保存里，这一版没写：${skipped.join('、')}。` : '';
    const failed = (error: unknown) => {
      setBusy(false);
      setStatus(error instanceof Error ? error.message : '保存失败：宿主没有回应。');
    };

    setBusy(true);
    if (isNew) {
      const basics = {
        name: f.name.trim(),
        gender: f.gender.trim(),
        personality: f.personality,
        description: f.description,
        greeting: f.greeting,
        userAddress: f.userAddress,
        nickname: f.nickname,
        romance: f.romance,
      };
      const rest: Record<string, unknown> = {};
      for (const [key, value] of Object.entries(patch)) if (!(key in basics)) rest[key] = value;
      bridge
        .request('character.create', basics)
        .then(async (created) => {
          // 建好之后再补这一份表单里多改的那几项（状态、语音、参数）：它们是「改」，不是「出生设定」。
          if (Object.keys(rest).length) {
            await bridge.request('character.save', { id: created.id, fields: rest as CharacterFields });
          }
          setCreatedId(created.id);
          setIsNew(false);
          setSaved(f);
          setChanged([]);
          setBusy(false);
          setStatus(`「${created.name}」已经进名册。` + notWritten);
        })
        .catch(failed);
      return;
    }

    bridge
      .request('character.save', {
        id: targetId,
        fields: patch as CharacterFields,
      })
      .then((result) => {
        setBusy(false);
        setSaved(f);
        setChanged([]);
        setStatus(`已保存「${result.name}」的改动。` + notWritten);
      })
      .catch(failed);
  };

  const revert = () => {
    if (isNew) {
      setT(DRAFT);
      setChanged([]);
      setStatus('改动已经放弃，回到空白那一张。');
      return;
    }
    setT(saved);
    setChanged([]);
    setStatus('改动已经放弃，回到盘上那一版。');
  };

  const close = () => {
    if (dirty) {
      const leave = window.confirm(
        `改动还没保存\n\n「${headline}」这一屏上有没保存的改动。\n离开就会丢掉它们，盘上还是原来那一版。`,
      );
      if (!leave) return;
    }
    back();
  };

  const resetSubProfile = () => {
    const yes = window.confirm(
      `副设定回正\n\n把「${f.name.trim()}」的副设定退回主设定的副本？这会清空累积的当下心境与心事，主设定不受影响。`,
    );
    if (!yes) return;
    setStatus('回正要写到角色身上：桥里还没有这个方法（characters.subProfile.reset），这次什么也没改。');
  };

  const talkSend = () => {
    const text = talkInput.trim();
    if (!text) {
      setTalkHint('先写一句话——比如「今天怎么这么早」。');
      return;
    }
    setTalkLines((lines) => [...lines, { id: nextLineId, who: '你', text, fromMe: true }]);
    setNextLineId(nextLineId + 1);
    setTalkInput('');
    setTalkHint('');
    setStatus('TA 没接话——这一句试话没成功。');
  };

  const talkClear = () => {
    setTalkLines([]);
    setTalkHint('这几句擦掉了——本来也没写进 TA 的记忆。');
  };

  const chooseOutfit = (key: string) => {
    setOutfit(key);
    const available = (subject?.emotions ?? []).filter((item) => item.outfit === key);
    if (available.length > 0 && !available.some((item) => item.emotion === emotion)) {
      setEmotion(available[0].emotion);
    }
  };

  const ctx: Ctx = {
    f,
    set,
    say: setStatus,
    advancedOpen,
    toggleAdvanced: () => setAdvancedOpen(!advancedOpen),
    navigate,
    resetSubProfile,
    facingLeft,
    setFacing: (next) => {
      setFacingLeft(next);
      setStatus((next ? 'TA 现在朝左站' : 'TA 现在朝右站') + '（还没保存）');
    },
    talkInput,
    setTalkInput,
    talkLines,
    talkHint,
    talkSend,
    talkClear,
    outfits,
    emotions,
    outfit,
    emotion,
    setOutfit: chooseOutfit,
    setEmotion,
    previewUrl,
    avatarUrl,
    changeAvatar: () => {
      void importAvatar(bridge, targetId).then((url) => {
        if (!url) return;
        setSubject((prev) => (prev ? { ...prev, avatarUrl: url } : prev));
        setStatus('头像换好了。');
      });
    },
    spriteOf: (key) => subject?.emotions.find((item) => item.outfit === outfit && item.emotion === key)?.url ?? '',
  };

  const visibleTabs = isNew ? TABS.filter((item) => item.key !== 'link') : TABS;

  return (
    <PageShell>
      <PageHeader
        title={headline}
        subtitle={metaLine}
        eyebrow="// SUBJECT STUDIO"
        onBack={close}
        right={
          <>
            {dirty ? <span className="cap">{`有 ${changed.length} 处没保存`}</span> : null}
            {dirty ? <GlassButton size="sm" onClick={revert}>放弃</GlassButton> : null}
            <PrimaryBtn onClick={save} disabled={!dirty || busy}>
              {saveLabel}
            </PrimaryBtn>
          </>
        }
      />

      <div className="screen screen--full">
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: wide ? '352px minmax(0, 1fr)' : 'minmax(0, 1fr)',
            gap: 'var(--w-space-3)',
            alignItems: 'start',
          }}
        >
          <Stage ctx={ctx} headline={headline} caption={previewCaption} wide={wide} />
          <Pane ctx={ctx} tab={tab} setTab={setTab} tabs={visibleTabs} />
        </div>
      </div>

      <div
        style={{
          position: 'sticky',
          bottom: 0,
          zIndex: 5,
          borderTop: '1px solid var(--w-hairline)',
          padding: 'var(--w-space-2) var(--w-pad-page)',
          background: 'var(--w-surface-tint)',
          display: 'flex',
          alignItems: 'center',
          gap: 'var(--w-space-2)',
          minHeight: 36,
        }}
      >
        <span style={{ fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)', lineHeight: 1.5 }}>{status}</span>
      </div>
    </PageShell>
  );
}

/** 左栏：立绘舞台（立绘 + 名字牌 + 读数条 + 服装/表情 + 共用记忆开关）。 */
function Stage({ ctx, headline, caption, wide }: { ctx: Ctx; headline: string; caption: string; wide: boolean }) {
  const { f, set, setFacing, facingLeft, outfits, emotions, outfit, emotion, setOutfit, setEmotion, previewUrl, avatarUrl } = ctx;
  return (
    <Glass
      as="section"
      tone="card"
      radius={12}
      className="cs-stage"
      style={{
        padding: 'var(--w-pad-card)',
        position: wide ? 'sticky' : undefined,
        top: wide ? 72 : undefined,
        maxHeight: wide ? 'calc(100vh - 96px)' : undefined,
      }}
    >
      <Stack gap={2} style={{ overflowY: wide ? 'auto' : undefined, minHeight: 0 }}>
        <div
          style={{
            height: 300,
            borderRadius: 4,
            background: 'var(--w-surface-tint)',
            display: 'grid',
            placeItems: 'center',
            overflow: 'hidden',
          }}
        >
          {previewUrl ? (
            <img
              src={previewUrl}
              alt=""
              style={{ height: '92%', width: 'auto', maxWidth: '100%', objectFit: 'contain', display: 'block' }}
            />
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 'var(--w-space-3)', textAlign: 'center' }}>
              <BodySm>还没有立绘</BodySm>
              <Caption>在下面「素材」里用一句话生成一套，或从 zip 补进来</Caption>
            </div>
          )}
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0,1fr) 64px', columnGap: 'var(--w-space-3)' }}>
          <Stack gap={1}>
            <Hair />
            <span style={{ marginTop: 'var(--w-space-1)' }}>
              <Eyebrow>// SUBJECT</Eyebrow>
            </span>
            <span
              style={{
                fontSize: 27,
                fontWeight: 700,
                color: 'var(--w-ink-display)',
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap',
              }}
            >
              {headline}
            </span>
            <Caption>{caption}</Caption>
          </Stack>
          <button
            type="button"
            className="cs-avatar"
            style={{ width: 64, height: 64, alignSelf: 'end', justifySelf: 'end', textAlign: 'center' }}
            title={avatarUrl ? '点头像换一张' : '点头像导入头像'}
            onClick={() => ctx.changeAvatar()}
          >
            {avatarUrl ? (
              <img src={avatarUrl} alt="" />
            ) : (
              <>
                <span style={{ fontSize: 20, color: 'var(--w-ink-muted)' }}>＋</span>
                <Caption>头像</Caption>
              </>
            )}
          </button>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1px 1fr 1px 1fr', columnGap: 'var(--w-space-2)', marginTop: 'var(--w-space-2)' }}>
          <Reading eyebrow="AFFECTION" value={String(Math.round(f.affection))} note="好感" />
          <i style={{ background: 'var(--w-hairline)' }} />
          <Reading eyebrow="TRUST" value={String(Math.round(f.trust))} note="信任" />
          <i style={{ background: 'var(--w-hairline)' }} />
          <Reading eyebrow="ENERGY" value={String(Math.round(f.energy))} note="精力" />
        </div>

        <Stack gap={2} style={{ marginTop: 'var(--w-space-2)' }}>
          <Hair />
          <div style={{ display: 'grid', gridTemplateColumns: '34px minmax(0,1fr)', columnGap: 'var(--w-space-1)', alignItems: 'center' }}>
            <Caption>服装</Caption>
            <Chips>
              {outfits.map((item) => (
                <ChipBtn
                  key={item.key}
                  label={item.label}
                  on={item.key === outfit}
                  onClick={() => {
                    setOutfit(item.key);
                    ctx.say(`已换上「${item.label}」——还没保存，按「保存」才落到角色身上。`);
                  }}
                />
              ))}
            </Chips>
          </div>
          <div style={{ display: 'grid', gridTemplateColumns: '34px minmax(0,1fr)', columnGap: 'var(--w-space-1)', alignItems: 'center' }}>
            <Caption>表情</Caption>
            <Chips>
              {emotions.map((item) => (
                <ChipBtn
                  key={item.key}
                  label={item.label}
                  on={item.key === emotion}
                  onClick={() => setEmotion(item.key)}
                />
              ))}
            </Chips>
          </div>
          <Chips>
            <ChipBtn label="↔ 换朝向" onClick={() => setFacing(!facingLeft)} />
            <ChipBtn label="换头像" onClick={() => ctx.changeAvatar()} />
          </Chips>

          <SwitchLine
            title="所有存档共用一份记忆"
            desc="关掉就一份存档记一份，互不串"
            checked={f.crossSave}
            onChange={(next) => set('crossSave', next, '记忆作用域')}
          />

          <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
            <Caption faint>占位图记 · 鸣谢 ignoredone.space 个人图标包</Caption>
          </div>
        </Stack>
      </Stack>
    </Glass>
  );
}

function Reading({ eyebrow, value, note }: { eyebrow: string; value: string; note: string }) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
      <Eyebrow>{eyebrow}</Eyebrow>
      <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'flex-end', gap: 'var(--w-space-1)' }}>
        <span style={{ fontFamily: 'var(--w-font-num)', fontSize: 24, color: 'var(--w-ink-display)' }}>{value}</span>
        <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)', paddingBottom: 4 }}>{note}</span>
      </div>
    </div>
  );
}

/** 右栏：页签带 + 切口线 + 一栏内容。 */
function Pane({
  ctx,
  tab,
  setTab,
  tabs,
}: {
  ctx: Ctx;
  tab: Tab;
  setTab: (next: Tab) => void;
  tabs: { key: Tab; label: string }[];
}) {
  return (
    <Glass as="section" tone="card" radius={12} style={{ padding: 0 }}>
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(82px, 1fr))',
          gap: 6,
          padding: 'var(--w-space-2) var(--w-pad-card)',
        }}
      >
        {tabs.map((item) => (
          <button
            key={item.key}
            type="button"
            aria-pressed={tab === item.key}
            onClick={() => setTab(item.key)}
            style={{
              height: 30,
              borderRadius: 99,
              border: tab === item.key ? '1px solid transparent' : '1px solid var(--w-hairline)',
              background: tab === item.key ? 'var(--w-ink-display)' : 'var(--w-tint-button)',
              color: tab === item.key ? 'var(--w-bg)' : 'var(--w-ink-body)',
              font: 'inherit',
              fontSize: 'var(--w-size-caption)',
              cursor: 'pointer',
              whiteSpace: 'nowrap',
              padding: 0,
            }}
          >
            {item.label}
          </button>
        ))}
      </div>
      <div style={{ height: 2, background: 'var(--w-signal)' }} />
      <div style={{ padding: 'var(--w-space-3) var(--w-pad-card) var(--w-pad-card)' }}>
        {tab === 'set' ? <SettingTab ctx={ctx} /> : null}
        {tab === 'state' ? <StateTab ctx={ctx} /> : null}
        {tab === 'art' ? <ArtTab ctx={ctx} /> : null}
        {tab === 'mem' ? <MemoryTab ctx={ctx} /> : null}
        {tab === 'link' ? <LinkTab ctx={ctx} /> : null}
        {tab === 'talk' ? <TalkTab ctx={ctx} /> : null}
        {tab === 'cg' ? <CgTab /> : null}
      </div>
    </Glass>
  );
}

/** ══ 01 设定 ══ */
function SettingTab({ ctx }: { ctx: Ctx }) {
  const { f, set, say } = ctx;
  const aiRewrite = () => say('AI 没有回应：检查设置 → AI 里的接口与密钥。');
  return (
    <Stack gap={3}>
      <SectionHead no="01" eyebrow="// WHO" title="TA 是谁" />

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', columnGap: 'var(--w-space-3)' }}>
        <FieldLine label="姓名" value={f.name} onChange={(next) => set('name', next, '姓名')} />
        <FieldLine
          label="昵称（叫 TA 什么）"
          value={f.nickname}
          onChange={(next) => set('nickname', next, '昵称')}
          placeholder="留空就用姓名"
        />
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', columnGap: 'var(--w-space-3)' }}>
        <FieldLine
          label="称呼 TA 对你的叫法"
          value={f.userAddress}
          onChange={(next) => set('userAddress', next, '称呼')}
          placeholder="主人 / 哥哥 / 名字"
        />
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-1)' }}>
          <FieldLabel>性别</FieldLabel>
          <Chips>
            {GENDERS.map((item) => (
              <ChipBtn key={item} label={item} on={f.gender === item} onClick={() => set('gender', item, '性别')} />
            ))}
          </Chips>
          <Caption>想要别的性别？设置 → 性别库里加一个，这里立刻多一颗。</Caption>
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', columnGap: 'var(--w-space-3)' }}>
        <FieldLine label="年龄" value={f.age} onChange={(next) => set('age', next, '年龄')} placeholder="例：24" />
        <FieldLine
          label="生日"
          value={f.birthday}
          onChange={(next) => set('birthday', next, '生日')}
          placeholder="MM-dd，如 03-14"
        />
      </div>

      <SwitchLine
        title="自然增长"
        desc="关掉后年龄就停在你填的这个数；年龄与生日仍会告诉 AI。"
        checked={f.naturalAging}
        onChange={(next) => set('naturalAging', next, '自然增长')}
      />
      <SwitchLine
        title="恋爱对象"
        desc="关掉后 TA 不会主动推进关系，周期与亲密内容也不进提示词。"
        checked={f.romance}
        onChange={(next) => set('romance', next, '恋爱对象')}
      />

      <FieldLine
        label="性格"
        value={f.personality}
        onChange={(next) => set('personality', next, '性格')}
        placeholder="TA 的说话方式与脾气"
        area
        action={<GlassButton size="sm" onClick={aiRewrite}>AI 改写</GlassButton>}
      />
      <FieldLine
        label="背景 / 前情"
        value={f.description}
        onChange={(next) => set('description', next, '背景')}
        placeholder="你们之间发生过什么"
        area
        action={<GlassButton size="sm" onClick={aiRewrite}>AI 改写</GlassButton>}
      />
      <FieldLine
        label="开场白"
        value={f.greeting}
        onChange={(next) => set('greeting', next, '问候语')}
        placeholder="TA 上线第一句说什么"
        area
        action={<GlassButton size="sm" onClick={aiRewrite}>AI 改写</GlassButton>}
      />

      <Hair />

      <Stack gap={3}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
          <PaneTitle>高级设置 · 选填</PaneTitle>
          <GlassButton size="sm" onClick={ctx.toggleAdvanced}>
            {ctx.advancedOpen ? '收起 ▲' : '展开 ▼'}
          </GlassButton>
        </div>
        <Caption>不填也没关系：这些栏留空时完全不会写进提示词，TA 仍按性格自然说话。</Caption>
        {ctx.advancedOpen ? (
          <Stack gap={3}>
            <FieldLine
              label="道德观"
              value={f.moral}
              onChange={(next) => set('moral', next, '道德观')}
              placeholder="TA 觉得什么该做、什么绝不越线（可留空）"
              area
            />
            <FieldLine
              label="人生观"
              value={f.life}
              onChange={(next) => set('life', next, '人生观')}
              placeholder="TA 觉得什么值得、怎么看聚散与日子（可留空）"
              area
            />
            <FieldLine
              label="生平"
              value={f.biography}
              onChange={(next) => set('biography', next, '生平')}
              placeholder="TA 从哪来、经历过什么（可留空）"
              area
            />
            <FieldLine
              label="政治倾向"
              value={f.political}
              onChange={(next) => set('political', next, '政治倾向')}
              placeholder="TA 对权力、秩序与立场的看法（可留空）"
              area
            />
            <FieldLine
              label="思维主义 / 意识形态"
              value={f.ideology}
              onChange={(next) => set('ideology', next, '思维主义')}
              placeholder="TA 用什么眼光看世界、信什么道理（可留空）"
              area
            />
            <FieldLine
              label="深刻记忆（可分行写）"
              value={f.deepMemories}
              onChange={(next) => set('deepMemories', next, '深刻记忆')}
              placeholder="一行一条，TA 忘不掉的那几件事（可留空）"
              area
            />
            <FieldLine
              label="创伤背景"
              value={f.trauma}
              onChange={(next) => set('trauma', next, '创伤背景')}
              placeholder="TA 受过的伤、留下的阴影（可留空）"
              area
            />
            <Caption>精神状况请在超级监测 / 心理健康处配置，不在这里新增字段。</Caption>
          </Stack>
        ) : null}
      </Stack>

      <Hair />

      <Stack gap={3}>
        <PaneTitle>让 AI 写一整份设定</PaneTitle>
        <Caption>
          一句话描述想要的人，TA 的名字、性别、性格、背景、开场白一次填好。写进字段后仍要按「保存」才落到盘上。
        </Caption>
        <AiBrief onGenerate={say} />
      </Stack>

      <Hair />

      <Stack gap={3}>
        <PaneTitle>TA 的世界观</PaneTitle>
        <Caption>可多本复合，作为生成 TA 世界观的语料。角色世界观可以与存档世界书不同；存档内仍可改世界观。</Caption>

        <Stack gap={1}>
          <FieldLabel>世界书（可多选）</FieldLabel>
          <Caption>还没选世界书：TA 的世界观只靠设定文字。可多本复合，作为生成 TA 世界观的语料。</Caption>
        </Stack>

        <Hair />

        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
            <BodySm>角色世界观</BodySm>
            <Caption>现在启用：没有角色世界观</Caption>
          </div>
          <GlassButton size="sm" className="is-off">新建一套</GlassButton>
        </div>
        <Caption>一套 = 几本世界书 + 一段描述。点一行就是指定 TA 默认用哪一套（单选）。</Caption>
        <Caption>还没有角色世界观：TA 就用上面勾的世界书，或者沿用这一档的世界树。</Caption>
      </Stack>
    </Stack>
  );
}

/** 一句话 → 整套设定。没有模型可问时只把源码那句提示回给状态条。 */
function AiBrief({ onGenerate }: { onGenerate: (text: string) => void }) {
  const [brief, setBrief] = useState('');
  return (
    <>
      <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0,1fr) auto', columnGap: 'var(--w-space-3)' }}>
        <Field
          value={brief}
          onChange={setBrief}
          placeholder="例：青梅竹马的邻居姐姐，学医，嘴上嫌弃其实很惦记人"
        />
        <PrimaryBtn
          onClick={() =>
            onGenerate(
              brief.trim()
                ? 'AI 没有回应：检查设置 → AI 里的接口与密钥。'
                : '先用一句话说想要什么样的角色（例如：青梅竹马的邻居姐姐，学医，嘴上嫌弃其实很惦记人）。',
            )
          }
        >
          生成
        </PrimaryBtn>
      </div>
    </>
  );
}

/** 字段标签（FieldLabel）。 */
function FieldLabel({ children }: { children: string }) {
  return <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)' }}>{children}</span>;
}

/** 一行字段：标签在上、框在下。 */
function FieldLine({
  label,
  value,
  onChange,
  placeholder,
  action,
  area,
}: {
  label: string;
  value: string;
  onChange: (next: string) => void;
  placeholder?: string;
  action?: ReactNode;
  area?: boolean;
}) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-1)' }}>
      <div style={{ display: 'flex', alignItems: 'flex-end', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
        <FieldLabel>{label}</FieldLabel>
        {action}
      </div>
      <Field value={value} onChange={onChange} placeholder={placeholder} multiline={area} />
    </div>
  );
}

/** ══ 02 状态 ══ */
function StateTab({ ctx }: { ctx: Ctx }) {
  const { f, set, navigate, resetSubProfile } = ctx;
  const cycleNote = f.cycleTracked
    ? `第 ${f.cycleDay || 0} 天 · 阶段 ${f.cyclePhase}`
    : '没有开周期记录：TA 的提示词里不会出现身体周期。';
  return (
    <Stack gap={3}>
      <SectionHead no="02" eyebrow="// STATUS" title="TA 此刻的状态" />
      <Caption>拖动就改，但和设定一样：按「保存」才写进角色。这几项直接进 AI 的提示词。</Caption>

      <SliderLine
        label="好感"
        reading={`${Math.round(f.affection)} / 1000`}
        value={f.affection}
        onChange={(next) => set('affection', next, '好感')}
      />
      <SliderLine
        label="信任"
        reading={`${Math.round(f.trust)} / 100`}
        value={f.trust}
        onChange={(next) => set('trust', next, '信任')}
      />
      <Stack gap={1}>
        <SliderLine
          label="精力"
          reading={`${Math.round(f.energy)} / 100`}
          value={f.energy}
          onChange={(next) => set('energy', next, '精力')}
        />
        <Caption>精力低的时候 TA 会想睡、说话也短。</Caption>
      </Stack>

      <Caption>
        性欲系统在设置 → 角色与内容里是关着的：这里的数字只留在档案上，游戏里暂时不会参与。打开总开关后才会生效。
      </Caption>
      <SliderLine
        label="性欲"
        reading={`${Math.round(f.libido)} / 100`}
        value={f.libido}
        onChange={(next) => set('libido', next, '性欲')}
      />
      <SwitchLine
        title="设定锁"
        desc="锁上之后性欲停在你写的值上：周期与 AI 判定都改不动它。"
        checked={f.libidoLocked}
        onChange={(next) => set('libidoLocked', next, '性欲设定锁')}
      />
      {f.libidoLocked ? (
        <SliderLine
          label="锁在"
          reading={String(Math.round(f.libidoLockValue))}
          value={f.libidoLockValue}
          onChange={(next) => set('libidoLockValue', next, '性欲设定锁')}
        />
      ) : null}

      <FieldLine
        label="好感积分（升级用的那把数）"
        value={f.affectionPoints}
        onChange={(next) => set('affectionPoints', next, '好感积分')}
      />

      <FieldLine label="此刻在哪" value={f.location} onChange={(next) => set('location', next, '地点')} placeholder="家 / 学校 / 医院" />

      <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-1)' }}>
        <FieldLabel>心情</FieldLabel>
        <Chips>
          {MOODS.map((item) => (
            <ChipBtn key={item.key} label={item.label} on={f.mood === item.key} onClick={() => set('mood', item.key, '心情')} />
          ))}
        </Chips>
      </div>

      <Hair />

      <Stack gap={3}>
        <SwitchLine
          title="记身体周期"
          desc="关掉后提示词里不会出现周期，与 TA 的性别无关。"
          checked={f.cycleTracked}
          onChange={(next) => set('cycleTracked', next, '周期')}
        />
        {f.cycleTracked ? (
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', columnGap: 'var(--w-space-3)' }}>
            <FieldLine label="第几天" value={f.cycleDay} onChange={(next) => set('cycleDay', next, '周期')} />
            <FieldLine
              label="阶段"
              value={f.cyclePhase}
              onChange={(next) => set('cyclePhase', next, '周期')}
              placeholder="menstrual / follicular / luteal"
            />
          </div>
        ) : null}
        <Caption>{cycleNote}</Caption>
      </Stack>

      <Hair />

      <Stack gap={3}>
        <PaneTitle>副设定（当下这一阵的 TA）</PaneTitle>
        <Caption>主设定是「TA 是什么样的人」，副设定是「TA 最近怎么样」。它会自己漂，也能回正。</Caption>
        <Well>
          <BodySm>对你还有些生疏，说话会留一点距离；心里对你还有保留。</BodySm>
          <Caption>（还没有记下的心事）</Caption>
          <Caption>主设定（内核，不随时间改变）</Caption>
        </Well>
        <Actions>
          <GlassButton size="sm" onClick={() => navigate('/subProfile')}>
            看完整副设定
          </GlassButton>
          <GlassButton size="sm" onClick={resetSubProfile}>
            回正到主设定
          </GlassButton>
          <GlassButton size="sm" onClick={() => navigate('/mentalHealth')}>
            心理状况
          </GlassButton>
        </Actions>
      </Stack>
    </Stack>
  );
}

/** ══ 03 素材 ══ */
function ArtTab({ ctx }: { ctx: Ctx }) {
  const { navigate, outfits, emotions, outfit, setEmotion, spriteOf } = ctx;
  const filled = emotions.filter((item) => spriteOf(item.key)).length;
  const outfitName = outfits.find((item) => item.key === outfit)?.label ?? outfitLabel(outfit);
  return (
    <Stack gap={3}>
      <SectionHead no="03" eyebrow="// PORTRAIT" title="服装 × 表情" />
      <Caption>一格就是一张立绘。点有图的格子换成那张，点空的格子直接去生成它。</Caption>

      <Stack gap={1}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
          <BodySm>{outfitName}</BodySm>
          <Caption>{`${filled}/${emotions.length} 格有图`}</Caption>
        </div>
        <Chips>
          {emotions.map((item) => {
            const url = spriteOf(item.key);
            return (
              <button
                key={item.key}
                type="button"
                disabled={!url}
                onClick={url ? () => setEmotion(item.key) : undefined}
                style={{
                  width: 76,
                  height: 86,
                  borderRadius: 4,
                  border: '1px solid var(--w-hairline)',
                  background: 'var(--w-tint-card)',
                  display: 'flex',
                  alignItems: 'flex-start',
                  justifyContent: 'flex-end',
                  padding: 4,
                  overflow: 'hidden',
                  font: 'inherit',
                  cursor: url ? 'pointer' : 'default',
                }}
              >
                {url ? (
                  <img src={url} alt="" style={{ width: '100%', height: '100%', objectFit: 'cover', borderRadius: 2 }} />
                ) : (
                  <Caption>缺</Caption>
                )}
              </button>
            );
          })}
        </Chips>
      </Stack>

      <Hair />

      <Stack gap={3}>
        <PaneTitle>生成这一格</PaneTitle>
        <ArtPrompt />
        <Caption>画面生成没开启：设置 → AI → 画面生成里填好接口与密钥，这一格才能生成。</Caption>
      </Stack>

      <Actions>
        <GlassButton size="sm" className="is-off">导入素材包（zip）</GlassButton>
        <GlassButton size="sm" onClick={() => navigate('/figurePack')}>
          形象包…
        </GlassButton>
        <GlassButton size="sm" onClick={() => navigate('/avatarcrop')}>
          换头像
        </GlassButton>
      </Actions>
    </Stack>
  );
}

function ArtPrompt() {
  const [prompt, setPrompt] = useState('');
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
      <Field
        value={prompt}
        onChange={setPrompt}
        placeholder="例：白色针织衫配浅色长裙，站在午后窗边，微微笑"
        multiline
      />
      <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-3)' }}>
        <PrimaryBtn disabled>画立绘</PrimaryBtn>
      </div>
    </div>
  );
}

/** ══ 04 记忆 ══ */
function MemoryTab({ ctx }: { ctx: Ctx }) {
  const { f, set, navigate } = ctx;
  const scopeLabel = f.crossSave ? '所有存档共用这一份记忆' : '只在这份存档里';
  const scopePath = f.crossSave ? 'memories.json' : 'memories';
  const memoryHint = f.crossSave
    ? '现在所有存档共用同一份记忆：在这份存档里聊过的事，换个存档 TA 也记得。想要「换了存档就重新认识」，把下面那颗开关关掉。'
    : '现在记忆按存档隔离：这一份存档里发生的事只属于这一份。想让它跟着角色跨存档走，打开下面那颗开关（对所有人都生效）。';
  const engineText = f.voiceEngine === '' ? '引擎跟随全局' : engineLabel(f.voiceEngine);
  const nameText = f.voiceName.trim() ? `音色 ${f.voiceName.trim()}` : '音色跟随全局';
  const extraText = f.voiceExtra.trim() ? ` · 参数 ${f.voiceExtra.trim()}` : '';
  const hasApi = f.voiceApiUrl.trim().length > 0 && f.voiceApiKey.trim().length > 0;
  const halfApi = f.voiceApiUrl.trim().length > 0 !== f.voiceApiKey.trim().length > 0;
  const apiText = hasApi
    ? ` · 专属API ${f.voiceApiMode === '' ? '协议跟随全局' : apiModeLabel(f.voiceApiMode)} · ${
        f.voiceTtsModel.trim() ? `模型 ${f.voiceTtsModel.trim()}` : '模型跟随全局'
      }`
    : halfApi
      ? ' · ⚠ 专属 API 只填了地址或密钥的一半，已整体回退全局'
      : ' · API 跟随全局';
  const voiceSummary = f.voiceEnabled
    ? engineText + ' · ' + nameText + extraText + apiText
    : '这个角色不出声：朗读与配音都会跳过 TA。';

  return (
    <Stack gap={3}>
      <SectionHead no="04" eyebrow="// MEMORY" title="TA 记得什么" />

      <Stack gap={0}>
        {LAYERS.map((layer) => (
          <div
            key={layer.name}
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: 'var(--w-space-3)',
              padding: '5px 0',
            }}
          >
            <div style={{ display: 'flex', flexDirection: 'column', gap: 1, minWidth: 0 }}>
              <BodySm>{layer.name}</BodySm>
              <Caption>{layer.detail}</Caption>
            </div>
            <Caption>—</Caption>
          </div>
        ))}
      </Stack>

      <Hair />

      <Stack gap={3}>
        <PaneTitle>这份记忆归谁</PaneTitle>
        <SwitchLine
          title="所有存档共用一份记忆"
          desc={scopePath}
          checked={f.crossSave}
          onChange={(next) => set('crossSave', next, '记忆作用域')}
        />
        <BodySm>{scopeLabel}</BodySm>
        <Caption>{memoryHint}</Caption>
        <Caption>这颗开关是全应用的：改了它，所有角色、所有存档一起换档。</Caption>
      </Stack>

      <Hair />

      <Stack gap={3}>
        <PaneTitle>这一个角色的 AI 参数</PaneTitle>
        <Caption>留空 = 跟随全局设置。填了就只改 TA，别人的不动。</Caption>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0,1fr))', columnGap: 'var(--w-space-3)' }}>
          <FieldLine label="记忆条数" value={f.ovMemoryTurns} onChange={(next) => set('ovMemoryTurns', next, '记忆参数')} placeholder="全局" />
          <FieldLine label="上下文上限" value={f.ovContextLimit} onChange={(next) => set('ovContextLimit', next, '记忆参数')} placeholder="全局" />
          <FieldLine label="温度" value={f.ovTemperature} onChange={(next) => set('ovTemperature', next, '记忆参数')} placeholder="全局" />
        </div>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', columnGap: 'var(--w-space-3)' }}>
          <FieldLine label="遗忘指数（天）" value={f.ovWindowDays} onChange={(next) => set('ovWindowDays', next, '记忆参数')} placeholder="全局" />
          <FieldLine label="回忆随机度" value={f.ovRecall} onChange={(next) => set('ovRecall', next, '记忆参数')} placeholder="全局" />
        </div>
      </Stack>

      <Hair />

      <Stack gap={3}>
        <PaneTitle>这一个角色的声音</PaneTitle>
        <SwitchLine
          title="让 TA 出声"
          desc="关掉后 TA 不再开口朗读：对话照常，只是没有声音，别的角色不受影响。"
          checked={f.voiceEnabled}
          onChange={(next) => set('voiceEnabled', next, '语音')}
        />
        <Caption>{voiceSummary}</Caption>

        <FieldLabel>语音引擎</FieldLabel>
        <Chips>
          {VOICE_ENGINES.map((item) => (
            <ChipBtn
              key={item.key || 'global'}
              label={item.label}
              on={f.voiceEngine === item.key}
              onClick={() => set('voiceEngine', item.key, '语音引擎')}
            />
          ))}
        </Chips>

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', columnGap: 'var(--w-space-3)' }}>
          <FieldLine label="音色" value={f.voiceName} onChange={(next) => set('voiceName', next, '音色')} placeholder="留空跟随全局" />
          <FieldLine label="额外参数" value={f.voiceExtra} onChange={(next) => set('voiceExtra', next, '语音参数')} placeholder="留空跟随全局" />
        </div>

        <FieldLabel>专属语音 API（可选，地址+密钥都填才生效）</FieldLabel>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', columnGap: 'var(--w-space-3)' }}>
          <FieldLine label="API 地址" value={f.voiceApiUrl} onChange={(next) => set('voiceApiUrl', next, '语音 API')} placeholder="留空跟随全局" />
          <FieldLine
            label="API 密钥"
            value={f.voiceApiKey}
            onChange={(next) => set('voiceApiKey', next, '语音 API')}
            placeholder="留空跟随全局"
          />
        </div>
        <FieldLabel>API 协议</FieldLabel>
        <Chips>
          {VOICE_API_MODES.map((item) => (
            <ChipBtn
              key={item.key || 'global'}
              label={item.label}
              on={f.voiceApiMode === item.key}
              onClick={() => set('voiceApiMode', item.key, '语音 API')}
            />
          ))}
        </Chips>
        <FieldLine
          label="TTS 模型"
          value={f.voiceTtsModel}
          onChange={(next) => set('voiceTtsModel', next, '语音 API')}
          placeholder="留空跟随全局（如 tts-1 / tts-1-hd）"
        />
        <div>
          <GlassButton size="sm" className="is-off">
            保存这一档语音
          </GlassButton>
        </div>

        <Hair />

        <BodySm>只给 TA 用的模型</BodySm>
        <Caption>设置 → AI 里还没有可用的厂商：填好地址与密钥并打开，这里才有得选。现在 TA 走全局设置。</Caption>
        <FieldLabel>厂商</FieldLabel>
        <Chips>
          <ChipBtn label="跟随全局" on />
        </Chips>
        <FieldLabel>模型</FieldLabel>
      </Stack>

      <Hair />

      <Stack gap={3}>
        <PaneTitle>把别处的记忆搬进来</PaneTitle>
        <Caption>
          灵枢（Lingshu）导出的记忆 JSON 可以直接读进来：追加是并进来，替换是先清掉 TA 现在记得的。
        </Caption>
        <Actions>
          <GlassButton size="sm" className="is-off">追加记忆</GlassButton>
          <GlassButton size="sm" className="is-off">替换记忆</GlassButton>
        </Actions>
      </Stack>

      <Actions>
        <GlassButton size="sm" onClick={() => navigate('/memoryManage')}>
          核心记忆
        </GlassButton>
        <GlassButton size="sm" onClick={() => navigate('/memoryGraph')}>
          关系网
        </GlassButton>
        <GlassButton size="sm" onClick={() => navigate('/memorySearch')}>
          搜事件
        </GlassButton>
      </Actions>
    </Stack>
  );
}

function engineLabel(key: string): string {
  return VOICE_ENGINES.find((item) => item.key === key)?.label ?? key;
}

function apiModeLabel(key: string): string {
  return VOICE_API_MODES.find((item) => item.key === key)?.label ?? key;
}

/** ══ 05 关联 ══ */
function LinkTab({ ctx }: { ctx: Ctx }) {
  const { f, set } = ctx;
  const allowedHint = f.allowed
    ? 'TA 在这份存档的可用配角里，聊天时 AI 能找到 TA。当前名单是空的 = 不限制，角色库里所有人都能被 AI 调用。'
    : 'TA 已被移出这份存档：AI 不会再随手把 TA 拉进对话。当前名单是空的 = 不限制，角色库里所有人都能被 AI 调用。';
  return (
    <Stack gap={3}>
      <SectionHead no="05" eyebrow="// THIS SAVE" title="TA 在这份存档里" />

      <SwitchLine
        title="这份存档的可用配角"
        desc={allowedHint}
        checked={f.allowed}
        onChange={(next) => {
          set('allowed', next, '可用配角');
          ctx.say(
            next ? `已把「${f.name.trim()}」加进本存档的可用配角。` : `已把「${f.name.trim()}」移出本存档的可用配角。`,
          );
        }}
      />

      <Hair />

      <Stack gap={3}>
        <PaneTitle>复制一个 TA</PaneTitle>
        <Caption>副本共用同一批立绘文件，不占双份磁盘。记忆可以一起带走，也可以从零开始。</Caption>
        <FieldLine label="副本名字" value={f.duplicateName} onChange={(next) => set('duplicateName', next, '副本名字')} />
        <Actions>
          <GlassButton size="sm" className="is-off">只要资料</GlassButton>
          <PrimaryBtn disabled>连记忆一起复制</PrimaryBtn>
        </Actions>
      </Stack>

      <Well>
        <div style={{ textAlign: 'center' }}>
          <Caption>暂无存档</Caption>
        </div>
      </Well>
    </Stack>
  );
}

/** ══ 07 CG ══ */
function CgTab() {
  return (
    <Stack gap={3}>
      <SectionHead no="07" eyebrow="// GALLERY" title="TA 的画面收藏" />

      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
        <Caption>剧情里触发过才算解锁；解锁前只看得到条件，看不到画面。</Caption>
        <GlassButton size="sm" className="is-off">刷新</GlassButton>
      </div>

      <Stack gap={3}>
        <BodySm>还没有 CG。</BodySm>
        <Caption>在剧情里触发第一张之后，这里会按类别列出它的时长、大小、关联场景与引入条件。</Caption>
      </Stack>
    </Stack>
  );
}

/** ══ 06 说句话 ══ */
function TalkTab({ ctx }: { ctx: Ctx }) {
  const { talkLines, talkInput, talkHint, setTalkInput, talkSend, talkClear } = ctx;
  return (
    <Stack gap={3}>
      <SectionHead no="06" eyebrow="// REPLY" title="先听听 TA 怎么接话" />
      <Caption>这几句只按你现在填的设定回答（还没保存的也算），不写进 TA 的记忆、不进存档。</Caption>

      {talkLines.length === 0 ? (
        <Stack gap={1}>
          <BodySm>还没开口。</BodySm>
          <Caption>试一句平时会说的话，比如「今天怎么这么早」——口气对不对，一句就能听出来。</Caption>
        </Stack>
      ) : (
        <Stack gap={3}>
          {talkLines.map((line) =>
            line.fromMe ? (
              <div key={line.id} style={{ display: 'flex', justifyContent: 'flex-end' }}>
                <div
                  style={{
                    maxWidth: 380,
                    padding: 'var(--w-space-2) var(--w-space-3)',
                    borderRadius: 4,
                    background: 'var(--w-surface-tint)',
                  }}
                >
                  <BodySm>{line.text}</BodySm>
                </div>
              </div>
            ) : (
              <div key={line.id} style={{ display: 'flex', justifyContent: 'flex-start' }}>
                <div
                  style={{
                    maxWidth: 380,
                    padding: 'var(--w-space-2) var(--w-space-3)',
                    borderRadius: 4,
                    background: 'var(--w-tint-card)',
                    border: '1px solid var(--w-hairline)',
                  }}
                >
                  <Stack gap={0}>
                    <Caption>{line.who}</Caption>
                    <BodySm>{line.text}</BodySm>
                  </Stack>
                </div>
              </div>
            ),
          )}
        </Stack>
      )}

      <div
        style={{
          borderRadius: 4,
          border: '1px solid var(--w-hairline)',
          background: 'var(--w-surface-tint)',
          borderLeft: '2px solid var(--w-signal)',
          padding: '8px 12px',
          display: 'flex',
          flexDirection: 'column',
          gap: 6,
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
          <Eyebrow>// SAY</Eyebrow>
          <Caption>回车送出</Caption>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-3)' }}>
          <span style={{ fontFamily: 'var(--w-font-num)', fontSize: 18, color: 'var(--w-ink-faint)' }}>›</span>
          <div style={{ flex: 1, minWidth: 0 }}>
            <Field
              value={talkInput}
              onChange={setTalkInput}
              placeholder="想对 TA 说的话"
            />
          </div>
          <PrimaryBtn onClick={talkSend}>说出去 →</PrimaryBtn>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-3)' }}>
          <Caption>{talkHint}</Caption>
          {talkLines.length > 0 ? <TextBtn onClick={talkClear}>擦掉这几句</TextBtn> : null}
        </div>
      </div>
    </Stack>
  );
}
