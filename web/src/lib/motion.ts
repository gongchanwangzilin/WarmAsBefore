/**
 * 一个时钟：按压、页面过场、背景跟随都由同一个 rAF 推进。
 * 补间从「当前值」出发（不是从起点），所以中途再输入是接手，不是重放。
 */
export const PRESS_MS = 120;
export const STATE_MS = 180;
export const PAGE_MS = 320;

export type Numbers = Record<string, number>;

export const prefersReducedMotion = () =>
  typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

/** 减少动态时时长归零：位移与时长都没有，状态变化仍然落定。 */
export const canonical = (ms: number) => (prefersReducedMotion() ? 0 : ms);

type FrameListener = (now: number) => boolean;

const frames = new Set<FrameListener>();
let raf = 0;

function tick(now: number) {
  raf = 0;
  let busy = false;
  for (const frame of frames) {
    if (frame(now)) busy = true;
  }
  if (busy) raf = requestAnimationFrame(tick);
}

export function kick() {
  if (!raf) raf = requestAnimationFrame(tick);
}

/** 每帧回调；返回 true 表示还没做完，继续要帧。 */
export function onFrame(frame: FrameListener) {
  frames.add(frame);
  kick();
  return () => {
    frames.delete(frame);
  };
}

const easeOutCubic = (k: number) => 1 - Math.pow(1 - k, 3);

export type TweenHandle = { cancel(): void };

export function tween(
  from: Numbers,
  to: Numbers,
  ms: number,
  apply: (values: Numbers) => void,
  done?: () => void,
): TweenHandle {
  const keys = Object.keys(to);
  const values: Numbers = { ...from };

  if (ms <= 0) {
    apply(to);
    done?.();
    return { cancel: () => undefined };
  }

  const started = performance.now();
  let alive = true;
  let stop: (() => void) | null = null;

  stop = onFrame((now) => {
    if (!alive) return false;
    // rAF 的时间戳取的是这一帧的开始时刻，可能早于 tween 建立时的 performance.now()，
    // 不夹住就会算出负进度：第一帧透明度 -0.1、位移越过起点。
    const k = Math.min(1, Math.max(0, (now - started) / ms));
    const eased = easeOutCubic(k);
    for (const key of keys) {
      const a = from[key] ?? 0;
      const b = to[key] ?? 0;
      values[key] = a + (b - a) * eased;
    }
    apply(values);
    if (k >= 1) {
      alive = false;
      stop?.();
      done?.();
      return false;
    }
    return true;
  });

  return {
    cancel() {
      alive = false;
      stop?.();
    },
  };
}
