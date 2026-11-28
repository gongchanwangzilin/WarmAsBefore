import { useMemo, useRef, useState, type CSSProperties, type PointerEvent } from 'react';
import { PageShell } from '../../components/PageShell';
import { SignalButton } from '../../components/SignalButton';
import { Card, PageHeader, TextBtn } from '../../components/kit';
import type { ScreenProps } from '../types';

/**
 * 选个颜色（Views/Settings/ColorPickerPage.xaml）：上预览、中「饱和度 · 明度」方块与色相条、
 * 下取消 / 用这个颜色。三块画布在源码里由 Drawable 自绘，这里按同一份 H/S/V 用渐变现画，
 * 准星、游标与回显读的都是这一个状态，所以永远不会各说各话。
 */

const PREVIEW_HINT =
  '你看到的是这块色当地面时的样子：底下那层是它，上面压的是一张半透明的卡面。玻璃透出来的颜色由它决定。';
const HINT_LINE = '左→右是饱和度，上→下是明度；方块里可以直接点，也可以按住拖。色相条上的游标同样能点能拖。';

/** 起点：源码里没有自选色、也没有地面色时兜底那支暖白。 */
const FALLBACK_RGB = [0.99, 0.97, 0.94];

const caption: CSSProperties = { fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)', lineHeight: 1.6 };
const rowTitle: CSSProperties = { margin: 0, fontSize: 'var(--w-size-body)', color: 'var(--w-ink-body)' };
const desc: CSSProperties = { margin: 0, fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)', lineHeight: 1.6 };
const hexFont: CSSProperties = {
  fontFamily: 'var(--w-font-num)',
  fontSize: 'var(--w-size-subhead)',
  color: 'var(--w-ink-display)',
};

function fromHsv(h: number, s: number, v: number): { hex: string; r: number; g: number; b: number } {
  const hh = (((h % 360) + 360) % 360) / 60;
  const c = v * Math.min(1, Math.max(0, s));
  const x = c * (1 - Math.abs((hh % 2) - 1));
  const m = v - c;
  const [r, g, b] = hh < 1 ? [c, x, 0] : hh < 2 ? [x, c, 0] : hh < 3 ? [0, c, x] : hh < 4 ? [0, x, c] : hh < 5 ? [x, 0, c] : [c, 0, x];
  const byte = (n: number) => Math.round(Math.min(1, Math.max(0, n + m)) * 255);
  const [rb, gb, bb] = [byte(r), byte(g), byte(b)];
  return {
    hex: `#${rb.toString(16).padStart(2, '0').toUpperCase()}${gb.toString(16).padStart(2, '0').toUpperCase()}${bb
      .toString(16)
      .padStart(2, '0')
      .toUpperCase()}`,
    r: rb,
    g: gb,
    b: bb,
  };
}

function toHsv(r: number, g: number, b: number): [number, number, number] {
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const d = max - min;
  let h = 0;
  if (d > 0) {
    if (max === r) h = 60 * (((g - b) / d) % 6);
    else if (max === g) h = 60 * ((b - r) / d + 2);
    else h = 60 * ((r - g) / d + 4);
  }
  if (h < 0) h += 360;
  return [h, max <= 0 ? 0 : d / max, max];
}

function startColor(query: URLSearchParams): [number, number, number] {
  const raw = (query.get('color') ?? '').trim().replace(/^#/, '');
  if (/^[0-9a-fA-F]{6}$/.test(raw)) {
    const r = parseInt(raw.slice(0, 2), 16) / 255;
    const g = parseInt(raw.slice(2, 4), 16) / 255;
    const b = parseInt(raw.slice(4, 6), 16) / 255;
    return toHsv(r, g, b);
  }
  return toHsv(FALLBACK_RGB[0], FALLBACK_RGB[1], FALLBACK_RGB[2]);
}

export function Colorpicker({ query, back }: ScreenProps) {
  const [start] = useState(() => startColor(query));
  const [hue, setHue] = useState(start[0]);
  const [sat, setSat] = useState(start[1]);
  const [val, setVal] = useState(start[2]);
  const [copyHint, setCopyHint] = useState('');

  const fieldRef = useRef<HTMLDivElement | null>(null);
  const hueRef = useRef<HTMLDivElement | null>(null);

  const current = useMemo(() => fromHsv(hue, sat, val), [hue, sat, val]);
  const pure = useMemo(() => fromHsv(hue, 1, 1), [hue]);

  const pickField = (event: PointerEvent<HTMLDivElement>) => {
    const el = fieldRef.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    setSat(Math.min(1, Math.max(0, (event.clientX - rect.left) / rect.width)));
    setVal(Math.min(1, Math.max(0, 1 - (event.clientY - rect.top) / rect.height)));
  };

  const pickHue = (event: PointerEvent<HTMLDivElement>) => {
    const el = hueRef.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    setHue(Math.min(1, Math.max(0, (event.clientX - rect.left) / rect.width)) * 360);
  };

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(current.hex);
      setCopyHint('已复制');
    } catch {
      setCopyHint('复制失败');
    }
  };

  return (
    <PageShell>
      <PageHeader
        title="选个颜色"
        subtitle="方块里挑饱和度与明度，色相条上拉色相"
        backText="< 取消"
        onBack={back}
      />

      <main className="screen">
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-step-3)', width: '100%', maxWidth: 760 }}>
          <Card>
            <p style={rowTitle}>预览</p>
            <p style={desc}>{PREVIEW_HINT}</p>
            <div
              style={{
                height: 140,
                borderRadius: 'var(--w-radius-well)',
                overflow: 'hidden',
                position: 'relative',
                background: `radial-gradient(circle at 50% 64%, ${current.hex} 0%, color-mix(in srgb, ${current.hex} 58%, transparent) 46%, transparent 78%), var(--w-bg)`,
              }}
            >
              <div
                style={{
                  position: 'absolute',
                  left: '18%',
                  right: '18%',
                  top: '26%',
                  bottom: '20%',
                  borderRadius: 10,
                  border: '1px solid var(--w-hairline)',
                  background: 'var(--w-tint-card)',
                }}
              />
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
              <button
                type="button"
                onClick={copy}
                title="点击复制这串颜色"
                style={{ background: 'none', border: 0, padding: 0, cursor: 'pointer', ...hexFont }}
              >
                {current.hex}
              </button>
              <span style={{ ...caption, flex: 1, textAlign: 'right' }}>{copyHint}</span>
            </div>
          </Card>

          <Card>
            <p style={rowTitle}>饱和度 · 明度</p>
            <div
              ref={fieldRef}
              onPointerDown={(event) => {
                event.currentTarget.setPointerCapture(event.pointerId);
                pickField(event);
              }}
              onPointerMove={(event) => {
                if (event.currentTarget.hasPointerCapture(event.pointerId)) pickField(event);
              }}
              style={{
                position: 'relative',
                height: 220,
                borderRadius: 'var(--w-radius-well)',
                cursor: 'crosshair',
                touchAction: 'none',
                background: `linear-gradient(to top, ${fromHsv(0, 0, 0).hex}, transparent), linear-gradient(to right, ${fromHsv(
                  hue,
                  0,
                  1,
                ).hex}, ${pure.hex})`,
              }}
              role="slider"
              aria-label="饱和度与明度"
              aria-valuetext={`${Math.round(sat * 100)}% · ${Math.round(val * 100)}%`}
            >
              <span
                aria-hidden="true"
                style={{
                  position: 'absolute',
                  left: `calc(${sat * 100}% - 7px)`,
                  top: `calc(${(1 - val) * 100}% - 7px)`,
                  width: 14,
                  height: 14,
                  borderRadius: '50%',
                  border: '2px solid var(--w-ink-on-signal)',
                  boxShadow: '0 0 0 1px var(--w-hairline)',
                  background: current.hex,
                }}
              />
            </div>

            <p style={{ ...rowTitle, marginTop: 4 }}>色相</p>
            <div
              ref={hueRef}
              onPointerDown={(event) => {
                event.currentTarget.setPointerCapture(event.pointerId);
                pickHue(event);
              }}
              onPointerMove={(event) => {
                if (event.currentTarget.hasPointerCapture(event.pointerId)) pickHue(event);
              }}
              style={{
                position: 'relative',
                height: 26,
                borderRadius: 'var(--w-radius-well)',
                cursor: 'pointer',
                touchAction: 'none',
                background: `linear-gradient(to right, ${[0, 60, 120, 180, 240, 300, 360]
                  .map((stop) => fromHsv(stop, 1, 1).hex)
                  .join(', ')})`,
              }}
              role="slider"
              aria-label="色相"
              aria-valuemin={0}
              aria-valuemax={360}
              aria-valuenow={Math.round(hue)}
            >
              <span
                aria-hidden="true"
                style={{
                  position: 'absolute',
                  left: `calc(${(hue / 360) * 100}% - 5px)`,
                  top: -2,
                  width: 10,
                  height: 30,
                  borderRadius: 5,
                  border: '2px solid var(--w-ink-on-signal)',
                  boxShadow: '0 0 0 1px var(--w-hairline)',
                  background: pure.hex,
                }}
              />
            </div>

            <p style={{ ...caption, margin: 0 }}>{HINT_LINE}</p>
          </Card>
        </div>
      </main>

      <div
        style={{
          position: 'fixed',
          left: 0,
          right: 0,
          bottom: 0,
          display: 'grid',
          gridTemplateColumns: 'auto 1fr auto',
          gap: 'var(--w-space-2)',
          alignItems: 'center',
          padding: 'var(--w-step-3) var(--w-pad-page) var(--w-step-4)',
          background: 'var(--w-tint-bar)',
          borderTop: '1px solid var(--w-hairline)',
        }}
      >
        <TextBtn onClick={back}>取消</TextBtn>
        <span style={{ ...hexFont, fontSize: 'var(--w-size-caption)', textAlign: 'center' }}>{current.hex}</span>
        <SignalButton onClick={back}>用这个颜色</SignalButton>
      </div>
    </PageShell>
  );
}
