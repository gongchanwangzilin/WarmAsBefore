import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
  type ReactNode,
} from 'react';
import type { Direction, Route } from '../lib/router';
import { bindLayerMotion, type LayerMotion } from '../lib/transition';

/** 同一页换了查询串（/settingssection?key=ai → ?key=sound）也算换页。 */
const keyOf = (route: Route) => route.path + '?' + route.query.toString();

/**
 * 位移只到一行的零头：外框 12 / 退出 10，内容层再由 CSS 走 6 —— 两层错开，
 * 读起来是「页在后面推、内容在前面跟上」，而不是整块板子平移。
 */
const ENTER_X = 12;
const EXIT_X = 10;
/** 过场时长落在 120–280ms 这一档：令牌给的 320 太慢，一次点击要等三分之一秒才落定。 */
const PAGE_MS = '260ms';

type Frame = {
  id: number;
  route: Route;
  /** 1 = 从右侧进来，-1 = 从左侧（返回方向） */
  direction: 1 | -1;
  leaving: boolean;
  /** 初次挂载要摆到入场位；接手的旧层保持它此刻的位置 */
  fresh: boolean;
};

type Props = {
  route: Route;
  direction: Direction;
  render: (route: Route) => ReactNode;
};

/**
 * 页面栈。最多同时挂着两层：当前一层 + 正在退场的那一层。
 * 退场层不销毁，所以过场途中再点返回时，它还在原地，
 * 直接改成「入场」就能从当前位移/透明度接手，不需要重新从起点播一遍。
 */
export function PageDeck({ route, direction, render }: Props) {
  const [frames, setFrames] = useState<Frame[]>(() => [
    { id: 0, route, direction: 1, leaving: false, fresh: true },
  ]);
  const seq = useRef(1);

  useEffect(() => {
    setFrames((prev) => {
      const current = prev.find((frame) => !frame.leaving);
      if (current && keyOf(current.route) === keyOf(route)) return prev;

      const sign: 1 | -1 = direction === 'back' ? -1 : 1;
      const revived = prev.find((frame) => frame.leaving && keyOf(frame.route) === keyOf(route));
      const marked = prev.map((frame) => (frame.leaving ? frame : { ...frame, leaving: true }));

      if (revived) {
        return marked.map((frame) =>
          frame.id === revived.id ? { ...frame, leaving: false, direction: sign } : frame,
        );
      }

      return [...marked, { id: seq.current++, route, direction: sign, leaving: false, fresh: true }];
    });
  }, [route, direction]);

  const drop = useCallback((id: number) => {
    setFrames((prev) => prev.filter((frame) => frame.id !== id));
  }, []);

  return (
    <div className="deck">
      {frames.map((frame) => (
        <PageLayer key={frame.id} frame={frame} onGone={drop} autoFocus={frame.id !== 0}>
          {render(frame.route)}
        </PageLayer>
      ))}
    </div>
  );
}

function PageLayer({
  frame,
  onGone,
  autoFocus,
  children,
}: {
  frame: Frame;
  onGone: (id: number) => void;
  autoFocus: boolean;
  children: ReactNode;
}) {
  const ref = useRef<HTMLElement | null>(null);
  const motion = useRef<LayerMotion | null>(null);
  const booted = useRef(false);
  const focused = useRef(false);

  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;

    const layer = bindLayerMotion(el);
    motion.current = layer;

    if (!booted.current) {
      booted.current = true;
      layer.jump(
        frame.leaving
          ? { x: -frame.direction * EXIT_X, o: 0 }
          : frame.fresh
            ? { x: frame.direction * ENTER_X, o: 0 }
            : { x: 0, o: 1 },
      );
    }

    if (frame.leaving) {
      layer.to({ x: -frame.direction * EXIT_X, o: 0 }, () => onGone(frame.id));
    } else {
      layer.to({ x: 0, o: 1 });
    }

    return () => layer.dispose();
  }, [frame, onGone]);

  useEffect(() => {
    if (frame.leaving || !autoFocus || focused.current) return;
    focused.current = true;
    const title = ref.current?.querySelector<HTMLElement>('[data-page-title]');
    title?.focus({ preventScroll: true });
  }, [frame.leaving, autoFocus]);

  return (
    <section
      ref={ref}
      className="layer"
      data-leaving={frame.leaving ? '1' : undefined}
      data-enter={frame.fresh ? '1' : undefined}
      style={
        {
          // 内容层往哪一侧偏，由这一层的方向决定；过场时长也在这一层覆盖，
          // 免得改全局令牌影响别的动效。
          '--dir': frame.direction,
          '--w-dur-page': PAGE_MS,
        } as CSSProperties
      }
      inert={frame.leaving}
      aria-hidden={frame.leaving || undefined}
    >
      <div className="page">{children}</div>
    </section>
  );
}
