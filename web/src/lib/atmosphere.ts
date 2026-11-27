import { onFrame } from './motion';

/**
 * 世界气氛层：cloud / dust。
 *
 * 原先只有一张静态渐变底，玻璃背后是「死」的。这里用一块 canvas 画两层慢动作，
 * 让玻璃后面真的有东西在动 —— 一层是被光推着走的雾（视差，越近越大越亮），
 * 一层是几乎看不见的浮尘（靠近指针的那几粒更亮）。
 *
 * 预算写死在这里，不给用户调：
 *   · 雾 7 团 + 尘 ≤ 96 粒（按窗口面积缩放，最大 96）；
 *   · DPR 上限 1.25（再多看不出差别，像素填充翻倍）；
 *   · 页面不可见时停帧；轻量模式 / 减少动效时整层不画；
 *   · 参数只用令牌（--w-signal-glow / --w-ground-warm），不引入新配色。
 */
type Blob = { x: number; y: number; r: number; depth: number; vx: number; vy: number; hue: 'warm' | 'cool' };
type Mote = { x: number; y: number; z: number; vx: number; vy: number; r: number };

export type Atmosphere = { dispose(): void; setEnabled(on: boolean): void };

const BLOBS = 7;
const MAX_MOTES = 96;
const DPR_CAP = 1.25;

export function startAtmosphere(canvas: HTMLCanvasElement, enabled: boolean, reduceMotion: boolean): Atmosphere {
  const ctx = canvas.getContext('2d', { alpha: true });
  if (!ctx) return { dispose() {}, setEnabled() {} };

  let on = enabled && !reduceMotion;
  let raf = 0;
  let w = 0;
  let h = 0;
  let dpr = 1;
  let warm = 'rgba(255, 214, 120, 0.05)';
  let cool = 'rgba(58, 92, 112, 0.06)';

  const blobs: Blob[] = [];
  const motes: Mote[] = [];
  const pointer = { x: 0, y: 0, sx: 0, sy: 0 };

  function readTokens() {
    const style = getComputedStyle(document.documentElement);
    warm = style.getPropertyValue('--w-ground-warm').trim() || warm;
    cool = style.getPropertyValue('--w-ground-cool').trim() || cool;
  }

  function resize() {
    dpr = Math.min(window.devicePixelRatio || 1, DPR_CAP);
    w = canvas.clientWidth;
    h = canvas.clientHeight;
    canvas.width = Math.max(1, Math.round(w * dpr));
    canvas.height = Math.max(1, Math.round(h * dpr));
    ctx!.setTransform(dpr, 0, 0, dpr, 0, 0);

    blobs.length = 0;
    for (let i = 0; i < BLOBS; i++) {
      const depth = 0.35 + (i / BLOBS) * 0.65;
      blobs.push({
        x: Math.random() * w,
        y: Math.random() * h,
        r: (180 + Math.random() * 260) * depth,
        depth,
        vx: (Math.random() - 0.5) * 0.06 * depth,
        vy: (Math.random() - 0.5) * 0.05 * depth,
        hue: i % 3 === 0 ? 'cool' : 'warm',
      });
    }

    const wanted = Math.min(MAX_MOTES, Math.round((w * h) / 22000));
    motes.length = 0;
    for (let i = 0; i < wanted; i++) {
      motes.push({
        x: Math.random() * w,
        y: Math.random() * h,
        z: 0.3 + Math.random() * 0.7,
        vx: (Math.random() - 0.5) * 0.12,
        vy: -0.04 - Math.random() * 0.12,
        r: 0.5 + Math.random() * 1.4,
      });
    }
  }

  function sprite(color: string) {
    const size = 128;
    const off = document.createElement('canvas');
    off.width = size;
    off.height = size;
    const c = off.getContext('2d')!;
    const g = c.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
    g.addColorStop(0, color);
    g.addColorStop(1, 'rgba(0, 0, 0, 0)');
    c.fillStyle = g;
    c.fillRect(0, 0, size, size);
    return off;
  }

  let warmSprite: HTMLCanvasElement;
  let coolSprite: HTMLCanvasElement;

  function frame() {
    raf = 0;
    if (!on) return;
    const rect = canvas.getBoundingClientRect();
    const cx = rect.width / 2;
    const cy = rect.height / 2;

    pointer.sx += (pointer.x - pointer.sx) * 0.06;
    pointer.sy += (pointer.y - pointer.sy) * 0.06;
    const px = (pointer.sx - cx) / Math.max(1, cx);
    const py = (pointer.sy - cy) / Math.max(1, cy);

    ctx!.clearRect(0, 0, w, h);
    ctx!.globalCompositeOperation = 'lighter';

    for (const b of blobs) {
      b.x += b.vx;
      b.y += b.vy;
      if (b.x < -b.r) b.x = w + b.r;
      if (b.x > w + b.r) b.x = -b.r;
      if (b.y < -b.r) b.y = h + b.r;
      if (b.y > h + b.r) b.y = -b.r;

      const ox = -px * 26 * b.depth;
      const oy = -py * 20 * b.depth;
      ctx!.globalAlpha = 0.5 + b.depth * 0.4;
      ctx!.drawImage(b.hue === 'warm' ? warmSprite : coolSprite, b.x + ox - b.r, b.y + oy - b.r, b.r * 2, b.r * 2);
    }

    for (const m of motes) {
      m.x += m.vx;
      m.y += m.vy;
      if (m.y < -4) {
        m.y = h + 4;
        m.x = Math.random() * w;
      }
      if (m.x < -4) m.x = w + 4;
      if (m.x > w + 4) m.x = -4;

      const dx = m.x - pointer.sx;
      const dy = m.y - pointer.sy;
      const d = Math.hypot(dx, dy);
      const near = Math.max(0, 1 - d / 420);
      ctx!.globalAlpha = (0.06 + near * 0.5) * m.z;
      ctx!.fillStyle = warm;
      ctx!.beginPath();
      ctx!.arc(m.x, m.y, m.r * m.z + near * 0.9, 0, Math.PI * 2);
      ctx!.fill();
    }

    ctx!.globalAlpha = 1;
    ctx!.globalCompositeOperation = 'source-over';

    if (on && !document.hidden) raf = requestAnimationFrame(frame);
  }

  function kick() {
    if (on && !raf && !document.hidden) raf = requestAnimationFrame(frame);
  }

  readTokens();
  resize();
  warmSprite = sprite(warm);
  coolSprite = sprite(cool);

  const onPointer = (event: PointerEvent) => {
    pointer.x = event.clientX;
    pointer.y = event.clientY;
    kick();
  };
  const onResize = () => {
    resize();
    kick();
  };
  const onVisibility = () => {
    if (document.hidden && raf) {
      cancelAnimationFrame(raf);
      raf = 0;
    } else kick();
  };

  window.addEventListener('pointermove', onPointer, { passive: true });
  window.addEventListener('resize', onResize);
  document.addEventListener('visibilitychange', onVisibility);
  const offFrame = onFrame(() => {
    if (on && !raf && !document.hidden) raf = requestAnimationFrame(frame);
    return false;
  });
  kick();

  return {
    dispose() {
      on = false;
      if (raf) cancelAnimationFrame(raf);
      raf = 0;
      window.removeEventListener('pointermove', onPointer);
      window.removeEventListener('resize', onResize);
      document.removeEventListener('visibilitychange', onVisibility);
      offFrame();
    },
    setEnabled(next: boolean) {
      on = next && !reduceMotion;
      if (!on) {
        if (raf) cancelAnimationFrame(raf);
        raf = 0;
        ctx!.clearRect(0, 0, w, h);
      } else {
        readTokens();
        kick();
      }
    },
  };
}
