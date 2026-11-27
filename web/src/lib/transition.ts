import { PAGE_MS, prefersReducedMotion, tween, type TweenHandle } from './motion';

/** 新页从这一侧进来，旧页往反方向退。返回时两者都反向。 */
export const ENTER_X = 26;
export const EXIT_X = 18;

export type LayerState = { x: number; o: number };

/** 过场时长以后端下发的 --w-dur-page 为准，拿不到才用常量。 */
function pageDuration(el: HTMLElement): number {
  const raw = getComputedStyle(el).getPropertyValue('--w-dur-page').trim();
  const value = Number.parseFloat(raw);
  if (!Number.isFinite(value)) return PAGE_MS;
  return raw.endsWith('s') && !raw.endsWith('ms') ? value * 1000 : value;
}

export type LayerMotion = {
  readonly state: LayerState;
  /** 立刻落到某个状态（新页的起点），不播动画 */
  jump(state: LayerState): void;
  /** 从当前值补到目标值；done 在这条补间落定时只响一次 */
  to(state: LayerState, done?: () => void): void;
  dispose(): void;
};

/**
 * 一层页面自己的位移 + 透明度通道。
 * 关键在于 `to` 永远从 current 出发：过场途中再来一次导航，
 * 这一层从「现在这一刻」接手，而不是回到它入场时的起点。
 */
export function bindLayerMotion(el: HTMLElement): LayerMotion {
  let current: LayerState = { x: 0, o: 0 };
  let handle: TweenHandle | null = null;

  const write = (state: LayerState) => {
    current = state;
    // 停在静止位时把 transform 撤掉：留着 translate3d(0,0,0) 会让这一层继续
    // 做 backdrop root，玻璃就糊不到身后的地面了。
    const resting = Math.abs(state.x) < 0.01 && state.o >= 0.999;
    el.style.transform = resting ? '' : `translate3d(${state.x.toFixed(2)}px, 0, 0)`;
    el.style.opacity = state.o >= 0.999 ? '' : state.o.toFixed(3);
    el.style.willChange = resting ? '' : 'transform, opacity';
  };

  return {
    get state() {
      return current;
    },
    jump(state) {
      handle?.cancel();
      handle = null;
      write(prefersReducedMotion() ? { x: 0, o: state.o } : state);
    },
    to(state, done) {
      handle?.cancel();
      const target = prefersReducedMotion() ? { x: 0, o: state.o } : state;
      handle = tween(
        { x: current.x, o: current.o },
        { x: target.x, o: target.o },
        prefersReducedMotion() ? 0 : pageDuration(el),
        (values) => write({ x: values.x, o: values.o }),
        done,
      );
    },
    dispose() {
      handle?.cancel();
    },
  };
}
