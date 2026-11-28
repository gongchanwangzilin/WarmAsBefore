import { kick, onFrame } from './motion';

/**
 * SPEC §5：光在窗口里只有一个位置，每块玻璃自己算方向与衰减。
 * 衰减 = (1 - d / R) ^ 1.35，R = 520px；块外时把光投影到最近的边（d 取到矩形外沿的距离）。
 *
 * 光只有一盏，而且是「湿」的：指针走得快，光斑就摊得开一点（--ls），站住时收回去。
 * 半径变化同样走 transform，不重画渐变。
 */
export const LIGHT_R = 520;
export const LIGHT_EXP = 1.35;

type Rect = { left: number; top: number; width: number; height: number };
type Sample = {
  el: HTMLElement;
  rect: Rect;
  observer: ResizeObserver;
  /** 上一次写进样式的值：没变到看得出来的程度就不再写，省掉整场重绘 */
  lit: { x: number; y: number; i: number; s: number };
};

const samples = new Set<Sample>();
const pointer = { x: 0, y: 0, tx: 0, ty: 0, known: false };
/** 指针速度（px/ms，已平滑）：光斑半径跟着它走，停下后自己衰减回 1。 */
let spread = 1;
let speed = 0;

let started = false;

function measure(sample: Sample) {
  const r = sample.el.getBoundingClientRect();
  sample.rect = { left: r.left, top: r.top, width: r.width, height: r.height };
}

/* ── 一帧一次：量、画都并到同一个 rAF 里 ────────────────────
   注册一块玻璃时量一次是必须的；但二十块玻璃同时挂载时，如果每块量完就写样式，
   后面的量就全变成强制重排。滚动同理：一次手势是几十个 scroll 事件，
   每个事件里量一遍全部矩形，滚动就只剩读布局了。 */

let paintQueued = 0;
let reflowQueued = 0;
let pendingAt: { x: number; y: number } | null = null;

function queuePaint(px?: number, py?: number) {
  if (px !== undefined && py !== undefined) pendingAt = { x: px, y: py };
  if (paintQueued) return;
  paintQueued = requestAnimationFrame(() => {
    paintQueued = 0;
    const at = pendingAt;
    pendingAt = null;
    if (at) paint(at.x, at.y);
    else if (pointer.known) paint(pointer.x, pointer.y);
  });
}

/** 位置变了（挂载 / 滚动 / 改尺寸）才需要重新量：合并到一帧里，量完整片再画。 */
function queueRemeasure() {
  if (reflowQueued) return;
  reflowQueued = requestAnimationFrame(() => {
    reflowQueued = 0;
    for (const sample of samples) measure(sample);
    if (pointer.known) paint(pointer.x, pointer.y, true);
  });
}

export function registerGlass(el: HTMLElement, lit = true) {
  // 控件（开关、滑杆、小按钮）不挂指针光：一块玻璃一次 reflow×3 个变量，几百个控件每动一下鼠标就是几百次样式写入。
  // 光要落在「面」上才看得出材质，落在每一颗开关上是纯开销。
  if (!lit) return () => undefined;
  const sample: Sample = {
    el,
    rect: { left: 0, top: 0, width: 0, height: 0 },
    lit: { x: 0, y: 0, i: -1, s: -1 },
    observer: new ResizeObserver(() => {
      measure(sample);
      queuePaint();
    }),
  };
  measure(sample);
  sample.observer.observe(el);
  samples.add(sample);
  queuePaint();
  return () => {
    sample.observer.disconnect();
    samples.delete(sample);
  };
}

export function lightAt(px: number, py: number, rect: Rect) {
  const cx = Math.min(Math.max(px, rect.left), rect.left + rect.width);
  const cy = Math.min(Math.max(py, rect.top), rect.top + rect.height);
  const d = Math.hypot(px - cx, py - cy);
  const intensity = Math.pow(Math.max(0, 1 - d / LIGHT_R), LIGHT_EXP);
  return { x: cx - rect.left, y: cy - rect.top, d, intensity };
}

function paint(px: number, py: number, force = false) {
  for (const sample of samples) {
    const { x, y, intensity } = lightAt(px, py, sample.rect);
    const lit = sample.lit;
    if (
      !force &&
      Math.abs(lit.x - x) < 0.4 &&
      Math.abs(lit.y - y) < 0.4 &&
      Math.abs(lit.i - intensity) < 0.004 &&
      Math.abs(lit.s - spread) < 0.004
    ) {
      continue;
    }
    lit.x = x;
    lit.y = y;
    lit.i = intensity;
    lit.s = spread;
    const style = sample.el.style;
    style.setProperty('--lx', `${x.toFixed(1)}px`);
    style.setProperty('--ly', `${y.toFixed(1)}px`);
    style.setProperty('--li', intensity.toFixed(3));
    style.setProperty('--ls', spread.toFixed(3));
  }
}

let lastFrame = 0;
let lastAt = 0;

/** 速度 → 光斑摊开的倍数：站住是 1，快速划过最多 1.18（再大就成了拖影）。 */
const spreadOf = (v: number) => 1 + Math.min(0.18, v * 0.009);

function easeBackground(now: number) {
  const dt = lastFrame ? Math.min(64, now - lastFrame) : 16;
  lastFrame = now;

  const k = 1 - Math.exp(-dt / 110);
  pointer.x += (pointer.tx - pointer.x) * k;
  pointer.y += (pointer.ty - pointer.y) * k;

  // 速度自己衰减：指针一停，摊开的光斑在 ~180ms 内收回原大小。
  speed *= Math.exp(-dt / 90);
  const next = spreadOf(speed);
  const s = next + (spread - next) * (1 - Math.exp(-dt / 70));
  const spreadMoved = Math.abs(s - spread) > 0.0015;
  spread = s;
  if (spreadMoved && pointer.known) paint(pointer.x, pointer.y);

  const settled =
    Math.abs(pointer.tx - pointer.x) < 0.4 &&
    Math.abs(pointer.ty - pointer.y) < 0.4 &&
    speed < 0.02 &&
    Math.abs(spread - 1) < 0.004;
  if (settled) {
    lastFrame = 0;
    speed = 0;
    if (spread !== 1) {
      spread = 1;
      if (pointer.known) paint(pointer.x, pointer.y);
    }
  }
  return !settled;
}

export function startLighting() {
  if (started) return;
  started = true;

  // 指针移动只记坐标，真正写样式合并到下一帧：一次 pointermove 里画几百块玻璃，
  // 输入事件就被样式写入淹了 —— 这就是「卡」的主要来源之一。
  const onMove = (event: PointerEvent) => {
    const now = event.timeStamp || performance.now();
    if (pointer.known && lastAt) {
      const dt = now - lastAt;
      if (dt > 0 && dt < 120) {
        const v = Math.hypot(event.clientX - pointer.tx, event.clientY - pointer.ty) / dt;
        speed += (v - speed) * 0.35;
      }
    }
    lastAt = now;
    pointer.tx = event.clientX;
    pointer.ty = event.clientY;
    if (!pointer.known) {
      pointer.x = event.clientX;
      pointer.y = event.clientY;
      pointer.known = true;
    }
    queuePaint(event.clientX, event.clientY);
    kick();
  };

  window.addEventListener('pointermove', onMove, { passive: true });
  window.addEventListener('resize', queueRemeasure);
  // 玻璃的矩形是相对视口的：里面任何一层滚动都要重排，捕获阶段一次就够，
  // 但每个事件只记一个「待量」标记，真正量发生在下一帧。
  window.addEventListener('scroll', queueRemeasure, true);

  pointer.x = pointer.tx = window.innerWidth * 0.3;
  pointer.y = pointer.ty = window.innerHeight * 0.22;
  pointer.known = true;
  onFrame(easeBackground);
  queueRemeasure();
}
