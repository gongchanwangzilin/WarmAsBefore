import { kick, onFrame } from './motion';

/**
 * SPEC §5：光在窗口里只有一个位置，每块玻璃自己算方向与衰减。
 * 衰减 = (1 - d / R) ^ 1.35，R = 520px；块外时把光投影到最近的边（d 取到矩形外沿的距离）。
 */
export const LIGHT_R = 520;
export const LIGHT_EXP = 1.35;

type Rect = { left: number; top: number; width: number; height: number };
type Sample = { el: HTMLElement; rect: Rect; observer: ResizeObserver };

const samples = new Set<Sample>();
const pointer = { x: 0, y: 0, tx: 0, ty: 0, known: false };

let ground: HTMLElement | null = null;
let started = false;

function measure(sample: Sample) {
  const r = sample.el.getBoundingClientRect();
  sample.rect = { left: r.left, top: r.top, width: r.width, height: r.height };
}

export function registerGlass(el: HTMLElement) {
  const sample: Sample = {
    el,
    rect: { left: 0, top: 0, width: 0, height: 0 },
    observer: new ResizeObserver(() => {
      measure(sample);
      paint(pointer.x, pointer.y);
    }),
  };
  measure(sample);
  sample.observer.observe(el);
  samples.add(sample);
  if (pointer.known) paint(pointer.x, pointer.y);
  return () => {
    sample.observer.disconnect();
    samples.delete(sample);
  };
}

export function registerGround(el: HTMLElement | null) {
  ground = el;
}

export function lightAt(px: number, py: number, rect: Rect) {
  const cx = Math.min(Math.max(px, rect.left), rect.left + rect.width);
  const cy = Math.min(Math.max(py, rect.top), rect.top + rect.height);
  const d = Math.hypot(px - cx, py - cy);
  const intensity = Math.pow(Math.max(0, 1 - d / LIGHT_R), LIGHT_EXP);
  return { x: cx - rect.left, y: cy - rect.top, d, intensity };
}

function paint(px: number, py: number) {
  for (const sample of samples) {
    const { x, y, intensity } = lightAt(px, py, sample.rect);
    const style = sample.el.style;
    style.setProperty('--lx', `${x.toFixed(1)}px`);
    style.setProperty('--ly', `${y.toFixed(1)}px`);
    style.setProperty('--li', intensity.toFixed(3));
  }
}

let lastFrame = 0;

function easeBackground(now: number) {
  const dt = lastFrame ? Math.min(64, now - lastFrame) : 16;
  lastFrame = now;

  const k = 1 - Math.exp(-dt / 110);
  pointer.x += (pointer.tx - pointer.x) * k;
  pointer.y += (pointer.ty - pointer.y) * k;

  if (ground) {
    const cx = window.innerWidth / 2;
    const cy = window.innerHeight / 2;
    ground.style.setProperty('--gx', `${((pointer.x - cx) * 0.18).toFixed(1)}px`);
    ground.style.setProperty('--gy', `${((pointer.y - cy) * 0.18).toFixed(1)}px`);
    const d = Math.hypot(pointer.x - cx, pointer.y - cy);
    ground.style.setProperty('--gi', (0.38 + 0.62 * Math.max(0, 1 - d / 980)).toFixed(3));
  }

  const settled = Math.abs(pointer.tx - pointer.x) < 0.4 && Math.abs(pointer.ty - pointer.y) < 0.4;
  if (settled) lastFrame = 0;
  return !settled;
}

export function startLighting() {
  if (started) return;
  started = true;

  const onMove = (event: PointerEvent) => {
    pointer.tx = event.clientX;
    pointer.ty = event.clientY;
    if (!pointer.known) {
      pointer.x = event.clientX;
      pointer.y = event.clientY;
      pointer.known = true;
    }
    paint(event.clientX, event.clientY);
    kick();
  };

  const remeasure = () => {
    for (const sample of samples) measure(sample);
    paint(pointer.x, pointer.y);
  };

  window.addEventListener('pointermove', onMove, { passive: true });
  window.addEventListener('resize', remeasure);
  window.addEventListener('scroll', remeasure, true);

  pointer.x = pointer.tx = window.innerWidth * 0.3;
  pointer.y = pointer.ty = window.innerHeight * 0.22;
  pointer.known = true;
  onFrame(easeBackground);
  remeasure();
}
