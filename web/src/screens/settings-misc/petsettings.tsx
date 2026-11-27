import { useEffect, useMemo, useRef, useState, type CSSProperties } from 'react';
import { Glass } from '../../components/Glass';
import { GlassButton } from '../../components/GlassButton';
import { GlassSlider } from '../../components/GlassSlider';
import { GlassSwitch } from '../../components/GlassSwitch';
import { PageShell } from '../../components/PageShell';
import { Card, PageHeader, Select, Sep } from '../../components/kit';
import { useIsWide } from '../../lib/useIsWide';
import type { ScreenProps } from '../types';

/**
 * 桌宠设置（Views/Games/PetSettingsPage.xaml）：左实时预览、右七段设置。
 * 预览不是贴图摆件 —— 字号、发光、气泡节奏、悬停按钮都跟右边的改动即时走；
 * 每一段的卡片、行序、开关与下拉都按源码摆，窄屏时预览在上、设置在下。
 */

const SEGMENTS = ['外观', '动作', '气泡', '声音', '语音', '位置', '自主'];

const PREVIEW_LINES = [
  '今天也要好好吃饭呀。',
  '你在旁边，干活都慢了半拍——不过我不嫌。',
  '窗台上那盆草又冒新芽了，我一直记着浇水的。',
  '累了就歇会儿，桌面上的东西我帮你看着。',
];

const HOVER_CATALOG = [
  { id: 'talk', name: '说话' },
  { id: 'think', name: '想一次' },
  { id: 'pet', name: '摸摸头' },
  { id: 'purr', name: '呼噜' },
  { id: 'walk', name: '走走' },
  { id: 'settings', name: '设置' },
  { id: 'home', name: '回窗口' },
];

const HOVER_ON = ['talk', 'think', 'pet', 'home'];

const DOUBLE_CLICK = ['回到主窗口', '说句话', '什么都不做'];
const WALKING = ['站着不动', '沿屏幕底沿散步'];
const LANGUAGES = ['跟随全局', '简体中文', '繁體中文', 'English', '日本語'];
const THINK = ['不主动思考', '随机嘀咕', '定时嘀咕'];
const RESPOND = ['有话就说', '被点到才答', '少插话'];
const VOICES = ['跟随全局', 'alloy', 'echo', 'fable', 'onyx', 'nova', 'shimmer'];
const IDLE = [0, 2, 5, 10, 15, 30, 60];
const THINK_INTERVAL = [5, 10, 15, 30, 60];

const options = (list: string[]) => list.map((label) => ({ value: label, label }));

const caption: CSSProperties = { margin: 0, fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)', lineHeight: 1.6 };
const bodySm: CSSProperties = { margin: 0, fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)', lineHeight: 1.5 };
const rowTitle: CSSProperties = { margin: 0, fontSize: 'var(--w-size-body)', color: 'var(--w-ink-body)' };
const rowDesc: CSSProperties = { margin: 0, fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)', lineHeight: 1.5 };
const subhead: CSSProperties = {
  margin: 0,
  fontSize: 'var(--w-size-subhead)',
  fontWeight: 600,
  color: 'var(--w-ink-display)',
};
const eyebrow: CSSProperties = { fontSize: 'var(--w-size-caption)', letterSpacing: '0.08em', color: 'var(--w-ink-muted)' };

const clamp = (v: number, min: number, max: number) => Math.min(max, Math.max(min, v));

/** 源码里 ViewSlider 的三列：说明 | 滑杆 150 | 读数 72。 */
function SliderRow({
  title,
  desc,
  min,
  max,
  value,
  onChange,
  readout,
  step,
}: {
  title: string;
  desc?: string;
  min: number;
  max: number;
  value: number;
  onChange: (next: number) => void;
  readout: string;
  step?: number;
}) {
  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: 'minmax(0,1fr) 150px 72px',
        gap: 'var(--w-space-2)',
        alignItems: 'center',
        padding: '10px 0',
      }}
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
        <span style={rowTitle}>{title}</span>
        {desc ? <span style={rowDesc}>{desc}</span> : null}
      </div>
      <GlassSlider
        label={title}
        width={150}
        value={clamp((value - min) / (max - min), 0, 1)}
        onChange={(t) => {
          const raw = min + t * (max - min);
          onChange(step ? Math.round(raw / step) * step : raw);
        }}
        format={() => readout}
      />
      <span style={{ ...caption, textAlign: 'right', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
        {readout}
      </span>
    </div>
  );
}

function SwitchRow({
  title,
  desc,
  checked,
  onChange,
}: {
  title: string;
  desc: string;
  checked: boolean;
  onChange: (next: boolean) => void;
}) {
  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0,1fr) auto', gap: 'var(--w-space-2)', alignItems: 'center', padding: '10px 0' }}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
        <span style={bodySm}>{title}</span>
        <span style={rowDesc}>{desc}</span>
      </div>
      <GlassSwitch checked={checked} onChange={onChange} label={title} />
    </div>
  );
}

function ChoiceRow({
  title,
  desc,
  value,
  choices,
  onChange,
  width,
  first,
}: {
  title: string;
  desc?: string;
  value: string;
  choices: { value: string; label: string }[];
  onChange: (next: string) => void;
  width: number;
  first?: boolean;
}) {
  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0,1fr) auto', gap: 'var(--w-space-2)', alignItems: 'center', padding: '10px 0' }}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
        <span style={bodySm}>{title}</span>
        {desc ? <span style={rowDesc}>{desc}</span> : null}
      </div>
      <div style={{ width, display: 'flex', justifyContent: 'flex-end' }}>
        <Select value={value} options={choices} onChange={onChange} label={first ? undefined : title} />
      </div>
    </div>
  );
}

export function Petsettings({ navigate, back }: ScreenProps) {
  const wide = useIsWide(900);
  const [tab, setTab] = useState(0);

  const [scale, setScale] = useState(1);
  const [fontSize, setFontSize] = useState(12);
  const [glow, setGlow] = useState(0);
  const [fps, setFps] = useState(30);
  const [fpsLock, setFpsLock] = useState(true);
  const [bubbleSpeed, setBubbleSpeed] = useState(1);
  const [statusBubble, setStatusBubble] = useState(true);
  const [inputBubble, setInputBubble] = useState(true);
  const [typingSound, setTypingSound] = useState(false);
  const [hover, setHover] = useState(() =>
    [...HOVER_ON.map((id) => HOVER_CATALOG.find((o) => o.id === id)!), ...HOVER_CATALOG.filter((o) => !HOVER_ON.includes(o.id))].map(
      (o) => ({ ...o, on: HOVER_ON.includes(o.id) }),
    ),
  );
  const [hoverHint, setHoverHint] = useState('');
  const [binding, setBinding] = useState('跟随存档主角');
  const [walking, setWalking] = useState(WALKING[0]);
  const [doubleClick, setDoubleClick] = useState(DOUBLE_CLICK[0]);
  const [tapLevel, setTapLevel] = useState(1);
  const [typingLevel, setTypingLevel] = useState(1);
  const [purr, setPurr] = useState(6);
  const [remember, setRemember] = useState(true);
  const [hideFullscreen, setHideFullscreen] = useState(false);
  const [crossScreen, setCrossScreen] = useState(false);
  const [language, setLanguage] = useState(LANGUAGES[0]);
  const [idle, setIdle] = useState('0');
  const [think, setThink] = useState(THINK[0]);
  const [thinkInterval, setThinkInterval] = useState('10');
  const [speakReplies, setSpeakReplies] = useState(false);
  const [voice, setVoice] = useState(VOICES[0]);
  const [pitch, setPitch] = useState(1);
  const [rate, setRate] = useState(1);
  const [respond, setRespond] = useState(RESPOND[0]);
  const [selfConfig, setSelfConfig] = useState(false);

  const [lineIdx, setLineIdx] = useState(0);
  const [typed, setTyped] = useState('');
  const [replay, setReplay] = useState(0);
  const holdRef = useRef(0);

  const full = PREVIEW_LINES[lineIdx % PREVIEW_LINES.length];
  const typedInterval = clamp(60 / clamp(bubbleSpeed, 0.2, 6), 8, 320);

  useEffect(() => {
    let idx = 0;
    let accum = 0;
    holdRef.current = 0;
    setTyped('');
    const timer = window.setInterval(() => {
      if (idx < full.length) {
        accum += 16;
        if (accum < typedInterval) return;
        const chars = Math.floor(accum / typedInterval);
        accum -= chars * typedInterval;
        idx = Math.min(full.length, idx + chars);
        setTyped(full.slice(0, idx));
        holdRef.current = 0;
        return;
      }
      holdRef.current += 1;
      if (holdRef.current > 160) setLineIdx((n) => n + 1);
    }, 16);
    return () => window.clearInterval(timer);
  }, [full, typedInterval, replay]);

  const activeHover = useMemo(() => hover.filter((o) => o.on), [hover]);

  const scaleText = `${Math.round(clamp(scale, 0.4, 2.6) * 100)}%`;
  const fontText = `${Math.round(fontSize)} px`;
  const fpsText = `${Math.round(fps)} fps`;
  const speedText = `×${bubbleSpeed.toFixed(1).replace(/\.0$/, '')}`;
  const purrText = `${Math.round(purr)} 秒`;

  const bodyHeight = clamp(168 * scale, 56, 264);
  const glowSize = bodyHeight * (1.15 + 0.5 * clamp(glow, 0, 1));
  const readout = `体型 ${scaleText} · 字号 ${fontText} · 发光 ${Math.round(glow * 100)}% · ${fpsText}${
    fpsLock ? '（锁定）' : '（放开跑）'
  } · 悬停 ${activeHover.length} 键 · 气泡 ×${bubbleSpeed.toFixed(1).replace(/\.0$/, '')}`;

  const toggleHover = (id: string, on: boolean) => {
    if (on && hover.filter((o) => o.on).length >= 6) {
      setHoverHint('悬停按钮最多 6 个——先移除一个再换。');
      return;
    }
    setHoverHint('');
    setHover((list) => {
      const next = list.map((o) => (o.id === id ? { ...o, on } : o));
      const ordered = next.filter((o) => o.on);
      return [...ordered, ...next.filter((o) => !o.on)];
    });
  };

  const move = (id: string, delta: number) => {
    setHover((list) => {
      const from = list.findIndex((o) => o.id === id);
      const to = from + delta;
      if (from < 0 || to < 0 || to >= list.length) return list;
      const next = [...list];
      const [item] = next.splice(from, 1);
      next.splice(to, 0, item);
      return next;
    });
  };

  const stage = (
    <Glass as="section" tone="card" radius={12} style={{ overflow: 'hidden' }}>
      <div
        style={{
          position: 'relative',
          display: 'grid',
          gridTemplateRows: 'auto 1fr auto',
          minHeight: wide ? 420 : 400,
          height: wide ? undefined : 400,
        }}
      >
        <div aria-hidden="true" style={{ position: 'absolute', inset: 0, background: 'var(--w-bg)' }} />
        <div aria-hidden="true" style={{ position: 'absolute', left: 0, right: 0, bottom: 0, height: 18, background: 'var(--w-tint-track)' }} />
        <div
          aria-hidden="true"
          style={{
            position: 'absolute',
            left: 0,
            right: 0,
            bottom: 4,
            display: 'flex',
            justifyContent: 'center',
            gap: 'var(--w-space-1)',
          }}
        >
          {[0, 1, 2].map((i) => (
            <span key={i} style={{ width: 8, height: 8, borderRadius: 4, background: 'var(--w-hairline)' }} />
          ))}
        </div>

        <div style={{ position: 'relative', zIndex: 3, display: 'flex', flexDirection: 'column', gap: 'var(--w-space-1)', padding: '14px 14px 0' }}>
          {statusBubble ? (
            <div
              style={{
                alignSelf: 'center',
                maxWidth: 300,
                padding: '6px 10px',
                borderRadius: 12,
                background: 'var(--w-tint-track)',
              }}
            >
              <span style={{ color: 'var(--w-ink-body)', fontSize, lineHeight: 1.5 }}>{typed || '\u00a0'}</span>
            </div>
          ) : null}
          {inputBubble ? (
            <div
              style={{
                alignSelf: 'center',
                width: 260,
                padding: '5px 8px',
                borderRadius: 14,
                border: '1px solid var(--w-hairline)',
                background: 'var(--w-tint-track)',
                display: 'flex',
                alignItems: 'center',
                gap: 'var(--w-space-1)',
              }}
            >
              <span style={{ flex: 1, color: 'var(--w-ink-muted)', fontSize }}>和 TA 说点什么…</span>
              <span
                style={{
                  padding: '2px 8px',
                  borderRadius: 9,
                  background: 'var(--w-surface-tint)',
                  color: 'var(--w-ink-label)',
                  fontSize: 11,
                }}
              >
                发送
              </span>
            </div>
          ) : null}
        </div>

        <div style={{ position: 'relative', minHeight: 160 }}>
          <div
            aria-hidden="true"
            style={{
              position: 'absolute',
              left: '50%',
              bottom: 18,
              width: glowSize,
              height: glowSize,
              transform: 'translateX(-50%)',
              opacity: clamp(glow, 0, 1) * 0.85,
              borderRadius: '50%',
              background: `radial-gradient(circle, color-mix(in srgb, var(--w-signal) 70%, transparent) 0%, color-mix(in srgb, var(--w-signal) 34%, transparent) 55%, transparent 100%)`,
            }}
          />
          <div
            style={{
              position: 'absolute',
              left: '50%',
              bottom: 18,
              width: bodyHeight,
              height: bodyHeight,
              transform: 'translateX(-50%)',
              borderRadius: 90,
              background: 'var(--w-tint-track)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            <span style={{ fontSize: 15, color: 'var(--w-ink-label)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
              (˶ᵔ ᵕ ᔔ˶)
            </span>
          </div>
          {activeHover.length > 0 ? (
            <div
              style={{
                position: 'relative',
                display: 'flex',
                justifyContent: 'center',
                gap: 'var(--w-space-1)',
                padding: '6px 2px',
              }}
            >
              {activeHover.map((item) => (
                <span
                  key={item.id}
                  style={{
                    padding: '4px 10px',
                    borderRadius: 11,
                    background: 'var(--w-tint-track)',
                    color: 'var(--w-ink-body)',
                    fontSize: 13,
                    whiteSpace: 'nowrap',
                  }}
                >
                  {item.name}
                </span>
              ))}
            </div>
          ) : null}
        </div>

        <div style={{ position: 'relative', display: 'grid', gridTemplateColumns: 'minmax(0,1fr) auto', gap: 'var(--w-space-2)', padding: '4px 14px 10px' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 0, justifyContent: 'center' }}>
            <span style={caption}>实时预览</span>
            <span style={{ ...bodySm, lineHeight: 1.5 }}>{readout}</span>
          </div>
          <GlassButton size="sm" onClick={() => setReplay((n) => n + 1)}>
            重新演示
          </GlassButton>
        </div>
      </div>
    </Glass>
  );

  const settings = (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)', minWidth: 0, paddingBottom: 24 }}>
      {hoverHint ? <p style={{ ...caption, color: 'var(--w-ink-label)' }}>{hoverHint}</p> : null}

      <Card>
        <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0,1fr) auto', gap: 'var(--w-space-2)', alignItems: 'center' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
            <span style={eyebrow}>// PET STUDIO</span>
            <span style={subhead}>AI 制作桌宠</span>
            <span style={rowDesc}>还没进存档，不知道该看谁的素材包</span>
            <span style={caption}>Q 版化一整套状态帧：散步会迈步、被摸头会跳、呼噜比心，久了就坐下、睡着</span>
          </div>
          <GlassButton size="md" onClick={() => navigate('/petstudio')}>
            去制作…
          </GlassButton>
        </div>
      </Card>

      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--w-space-1)' }}>
        {SEGMENTS.map((name, i) => (
          <button
            key={name}
            type="button"
            onClick={() => setTab(i)}
            style={{
              padding: '6px 12px',
              borderRadius: 'var(--w-radius-button)',
              border: '1px solid',
              borderColor: i === tab ? 'transparent' : 'var(--w-hairline)',
              background: i === tab ? 'var(--w-signal)' : 'transparent',
              color: i === tab ? 'var(--w-ink-on-signal)' : 'var(--w-ink-body)',
              font: 'inherit',
              fontSize: 'var(--w-size-small)',
              cursor: 'pointer',
            }}
          >
            {name}
          </button>
        ))}
      </div>

      {tab === 0 ? (
        <Card>
          <SliderRow title="体型" desc="站在桌面上的大小；也可以在角色上滚轮直接调" min={0.4} max={2.6} value={scale} onChange={setScale} readout={scaleText} />
          <Sep bleed />
          <SliderRow
            title="气泡字号"
            desc="头顶气泡与输入条的字有多大；改完气泡高度会跟着重新量"
            min={10}
            max={22}
            value={fontSize}
            onChange={setFontSize}
            readout={fontText}
            step={1}
          />
          <Sep bleed />
          <SliderRow title="外发光" desc="沿角色轮廓的一圈柔光；0 就是没有" min={0} max={1} value={glow} onChange={setGlow} readout="0–1" />
          <Sep bleed />
          <ChoiceRow
            title="绑定角色"
            desc="默认跟随当前存档主角；换档时桌面上的她会跟着换，也可以钉住某一个角色"
            value={binding}
            choices={options(['跟随存档主角'])}
            onChange={setBinding}
            width={180}
            first
          />
        </Card>
      ) : null}

      {tab === 1 ? (
        <>
          <Card>
            <SliderRow
              title="动作帧率"
              desc="走动/呼噜这些自发动作的动画帧率；预览里的呼吸节奏就是这个数"
              min={10}
              max={60}
              value={fps}
              onChange={setFps}
              readout={fpsText}
              step={1}
            />
            <Sep bleed />
            <SwitchRow title="帧率锁定" desc="开=严格按帧率抽帧（省电）；关=有余量就跑满到 60" checked={fpsLock} onChange={setFpsLock} />
            <Sep bleed />
            <ChoiceRow
              title="走动"
              desc="开散步后她会沿屏幕底沿慢慢走动，碰到边缘自己回头"
              value={walking}
              choices={options(WALKING)}
              onChange={setWalking}
              width={150}
            />
            <Sep bleed />
            <ChoiceRow title="双击角色" desc="双击她一下要发生什么" value={doubleClick} choices={options(DOUBLE_CLICK)} onChange={setDoubleClick} width={150} />
          </Card>

          <Card>
            <p style={bodySm}>悬停按钮（最多 6 个，出现顺序就是这里的顺序）</p>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 0, margin: '6px 0 4px' }}>
              {hover.map((item) => (
                <div
                  key={item.id}
                  style={{
                    display: 'grid',
                    gridTemplateColumns: 'minmax(0,1fr) auto auto auto',
                    gap: 'var(--w-space-2)',
                    alignItems: 'center',
                    padding: '8px 0',
                  }}
                >
                  <span style={{ ...bodySm, overflow: 'hidden', whiteSpace: 'nowrap', textOverflow: 'ellipsis' }}>{item.name}</span>
                  {item.on ? (
                    <button
                      type="button"
                      aria-label={`${item.name}上移`}
                      onClick={() => move(item.id, -1)}
                      style={ARROW}
                    >
                      ↑
                    </button>
                  ) : (
                    <span />
                  )}
                  {item.on ? (
                    <button
                      type="button"
                      aria-label={`${item.name}下移`}
                      onClick={() => move(item.id, 1)}
                      style={ARROW}
                    >
                      ↓
                    </button>
                  ) : (
                    <span />
                  )}
                  <GlassSwitch checked={item.on} onChange={(on) => toggleHover(item.id, on)} label={item.name} />
                </div>
              ))}
            </div>
          </Card>
        </>
      ) : null}

      {tab === 2 ? (
        <Card>
          <SwitchRow title="状态气泡" desc="头顶那一行小字：她现在在做什么。关掉就一个字都不写" checked={statusBubble} onChange={setStatusBubble} />
          <Sep bleed />
          <SwitchRow title="输入气泡" desc="头顶那条输入条。关了就只剩角色本人" checked={inputBubble} onChange={setInputBubble} />
          <Sep bleed />
          <SliderRow
            title="气泡浮现速度"
            desc="回复逐字浮现的节奏；×1 ≈ 正文同速，越大越快"
            min={0.2}
            max={6}
            value={bubbleSpeed}
            onChange={setBubbleSpeed}
            readout={speedText}
          />
          <Sep bleed />
          <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0,1fr) auto auto', gap: 'var(--w-space-2)', alignItems: 'center', padding: '10px 0' }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
              <span style={bodySm}>打字音</span>
              <span style={rowDesc}>气泡逐字浮现时的那串小键盘声</span>
            </div>
            <GlassButton size="sm">试听</GlassButton>
            <GlassSwitch checked={typingSound} onChange={setTypingSound} label="打字音" />
          </div>
        </Card>
      ) : null}

      {tab === 3 ? (
        <Card>
          <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0,1fr) 150px 72px 44px', gap: 'var(--w-space-2)', alignItems: 'center', padding: '10px 0' }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
              <span style={bodySm}>单击音效</span>
              <span style={caption}>点她一下、按悬停按钮时的那一声</span>
            </div>
            <GlassSlider label="单击音效" width={150} value={tapLevel} onChange={setTapLevel} format={() => `${Math.round(tapLevel * 100)}%`} />
            <span style={{ ...caption, textAlign: 'right' }}>{Math.round(tapLevel * 100)}%</span>
            <GlassButton size="sm">试听</GlassButton>
          </div>
          <Sep bleed />
          <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0,1fr) 150px 72px 44px', gap: 'var(--w-space-2)', alignItems: 'center', padding: '10px 0' }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
              <span style={bodySm}>打字音</span>
              <span style={caption}>气泡逐字浮现时那串小键盘声；开关在「气泡」段里</span>
            </div>
            <GlassSlider label="打字音音量" width={150} value={typingLevel} onChange={setTypingLevel} format={() => `${Math.round(typingLevel * 100)}%`} />
            <span style={{ ...caption, textAlign: 'right' }}>{Math.round(typingLevel * 100)}%</span>
            <GlassButton size="sm">试听</GlassButton>
          </div>
          <Sep bleed />
          <SliderRow
            title="撒娇时长"
            desc="按「呼噜/摸摸头」时她抖多久，状态行也写多久"
            min={2}
            max={20}
            value={purr}
            onChange={setPurr}
            readout={purrText}
            step={1}
          />
        </Card>
      ) : null}

      {tab === 4 ? (
        <Card>
          <SwitchRow
            title="回复读出声"
            desc="她答完一句，用 TTS 念出来；引擎与开关在设置 › 声音里"
            checked={speakReplies}
            onChange={setSpeakReplies}
          />
          <Sep bleed />
          <ChoiceRow
            title="音色"
            desc="跟随全局=用设置里的音色；API 模式认 alloy / nova 这些，本地引擎认系统声音名"
            value={voice}
            choices={options(VOICES)}
            onChange={setVoice}
            width={180}
          />
          <Sep bleed />
          <SliderRow title="音高" desc="×1 是原声；调小更低沉，调大更软" min={0.5} max={2} value={pitch} onChange={setPitch} readout={`×${pitch.toFixed(2)}`} />
          <Sep bleed />
          <SliderRow title="语速" desc="与全局语速相乘，不是替换" min={0.5} max={2} value={rate} onChange={setRate} readout={`×${rate.toFixed(2)}`} />
          <Sep bleed />
          <ChoiceRow
            title="回应模式"
            desc="有话就说=按「主动思考」自己开口；被点到才答=只在你点她时回应；少插话=主动开口减到偶发"
            value={respond}
            choices={options(RESPOND)}
            onChange={setRespond}
            width={150}
          />
        </Card>
      ) : null}

      {tab === 5 ? (
        <Card>
          <SwitchRow
            title="记住位置"
            desc="拖到哪儿下次还在哪儿；关了则每次回到工作区右下角"
            checked={remember}
            onChange={setRemember}
          />
          <Sep bleed />
          <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0,1fr) auto auto', gap: 'var(--w-space-2)', alignItems: 'center', padding: '10px 0' }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
              <span style={bodySm}>当前锚点</span>
              <span style={rowDesc}>还没记过位置——拖到喜欢的地方再点「记住当前位置」。</span>
            </div>
            <GlassButton size="sm">记住当前位置</GlassButton>
            <GlassButton size="sm">清除</GlassButton>
          </div>
          <Sep bleed />
          <SwitchRow
            title="全屏时收起"
            desc="前台进入全屏（放映/游戏）时把她一起收走，退出全屏再回来"
            checked={hideFullscreen}
            onChange={setHideFullscreen}
          />
          <Sep bleed />
          <SwitchRow
            title="小窗跨屏跟随"
            desc="多显示器：开=头顶小窗可以跟角色跨到副屏；关=只在本屏工作区收边"
            checked={crossScreen}
            onChange={setCrossScreen}
          />
        </Card>
      ) : null}

      {tab === 6 ? (
        <Card>
          <ChoiceRow
            title="桌宠语言"
            desc="她头顶那行状态字用哪一种；空=跟随全局语言"
            value={language}
            choices={options(LANGUAGES)}
            onChange={setLanguage}
            width={150}
            first
          />
          <Sep bleed />
          <ChoiceRow
            title="闲置自动桌宠"
            desc="闲置多少分钟后自动收纳成桌宠；关闭=不自动进"
            value={idle}
            choices={IDLE.map((n) => ({ value: String(n), label: String(n) }))}
            onChange={setIdle}
            width={120}
          />
          <Sep bleed />
          <ChoiceRow title="主动思考" desc="没人打扰时她自己嘀咕一句" value={think} choices={options(THINK)} onChange={setThink} width={150} />
          <Sep bleed />
          <ChoiceRow
            title="思考间隔"
            value={thinkInterval}
            choices={THINK_INTERVAL.map((n) => ({ value: String(n), label: String(n) }))}
            onChange={setThinkInterval}
            width={120}
          />
          <Sep bleed />
          <SwitchRow
            title="让她自己调桌宠设置"
            desc="开=她可以在回复里带一句 pet 指令，直接改体型/发光/字号；默认关，免得她的改动悄悄盖掉你的偏好"
            checked={selfConfig}
            onChange={setSelfConfig}
          />
        </Card>
      ) : null}
    </div>
  );

  return (
    <PageShell>
      <PageHeader
        eyebrow="// DESKTOP PET"
        title="桌宠设置"
        subtitle="改了立刻生效——左边就是她"
        onBack={back}
        right={
          <GlassButton size="sm" onClick={() => navigate('/usagestats')}>
            使用统计…
          </GlassButton>
        }
      />
      <main className="screen">
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: wide ? '440px minmax(0,1fr)' : 'minmax(0,1fr)',
            gap: 'var(--w-space-3)',
            alignItems: 'start',
          }}
        >
          {stage}
          {settings}
        </div>
      </main>
    </PageShell>
  );
}

const ARROW: CSSProperties = {
  padding: '2px 8px',
  height: 28,
  borderRadius: 'var(--w-radius-button)',
  border: '1px solid var(--w-hairline)',
  background: 'transparent',
  color: 'var(--w-ink-body)',
  font: 'inherit',
  fontSize: 'var(--w-size-small)',
  cursor: 'pointer',
};
