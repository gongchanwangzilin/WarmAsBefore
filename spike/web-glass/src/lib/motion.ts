/**
 * 一个时钟（SPEC §4）：按压通道与背景响应都由同一个 rAF 推进，
 * 中途再输入从当前值接手，不回到起点。
 */
export const PRESS_MS = 120;
export const STATE_MS = 180;
export const PAGE_MS = 320;

const reduced = () =>
  typeof window !== 'undefined' &&
  window.matchMedia('(prefers-reduced-motion: reduce)').matches;

type Channel = {
  el: HTMLElement;
  name: string;
  value: number;
  from: number;
  to: number;
  start: number;
  dur: number;
  active: boolean;
};

type FrameListener = (now: number) => boolean;

const channels = new Set<Channel>();
const frames = new Set<FrameListener>();
let raf = 0;

function tick(now: number) {
  raf = 0;
  let busy = false;

  for (const c of channels) {
    if (!c.active) continue;
    const k = c.dur <= 0 ? 1 : Math.min(1, (now - c.start) / c.dur);
    const eased = 1 - Math.pow(1 - k, 3);
    c.value = c.from + (c.to - c.from) * eased;
    if (k >= 1) {
      c.value = c.to;
      c.active = false;
    }
    c.el.style.setProperty(c.name, c.value.toFixed(3));
    if (c.active) busy = true;
  }

  for (const fn of frames) {
    if (fn(now)) busy = true;
  }

  if (busy) raf = requestAnimationFrame(tick);
}

export function kick() {
  if (!raf) raf = requestAnimationFrame(tick);
}

export function onFrame(fn: FrameListener) {
  frames.add(fn);
  kick();
  return () => {
    frames.delete(fn);
  };
}

export type PressControl = {
  set(v: number): void;
  dispose(): void;
};

/** 元素上的 --press 0..1：位移与缩放都从这里取，不碰布局。 */
export function bindPress(el: HTMLElement, name = '--press'): PressControl {
  const channel: Channel = {
    el,
    name,
    value: 0,
    from: 0,
    to: 0,
    start: 0,
    dur: PRESS_MS,
    active: false,
  };
  channels.add(channel);
  el.style.setProperty(name, '0');

  return {
    set(v: number) {
      const next = Math.min(1, Math.max(0, v));
      if (next === channel.to && !channel.active) return;
      channel.from = channel.value;
      channel.to = next;
      channel.start = performance.now();
      channel.dur = reduced() ? 0 : PRESS_MS;
      channel.active = true;
      kick();
    },
    dispose() {
      channels.delete(channel);
    },
  };
}
