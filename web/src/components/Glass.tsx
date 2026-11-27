import {
  createElement,
  useEffect,
  useRef,
  type CSSProperties,
  type ElementType,
  type ReactNode,
} from 'react';
import { registerGlass } from '../lib/light';

export type GlassTone = 'card' | 'bar' | 'button' | 'signal' | 'track' | 'knob' | 'thumb';

export type GlassProps = {
  as?: ElementType;
  tone?: GlassTone;
  radius?: number;
  className?: string;
  children?: ReactNode;
  style?: CSSProperties;
  elementRef?: React.RefObject<HTMLElement | null>;
};

/**
 * 一块玻璃。六层按固定顺序叠，指针光是第七层、独立于材质。
 * 一个元素承载六层，所以每块玻璃同步的只有 CSS 变量，不碰布局。
 */
export function Glass({
  as = 'div',
  tone = 'card',
  radius = 8,
  className = '',
  children,
  style,
  elementRef,
  ...rest
}: GlassProps & Record<string, unknown>) {
  const inner = useRef<HTMLDivElement | null>(null);
  const ref = elementRef ?? inner;

  useEffect(() => {
    const el = ref.current;
    if (!el) return;
    return registerGlass(el, tone === 'card' || tone === 'bar' || tone === 'signal');
  }, [ref, tone]);

  return createElement(
    as,
    {
      ref,
      className: `glass glass--${tone} ${className}`.trim(),
      style: { ...style, '--r': `${radius}px` } as CSSProperties,
      ...rest,
    },
    <div className="glass__layers" aria-hidden="true">
      <div className="glass__refract">
        <i />
      </div>
      <div className="glass__wall" />
      <div className="glass__cyl glass__cyl--top">
        <i />
      </div>
      <div className="glass__cyl glass__cyl--bottom">
        <i />
      </div>
      <div className="glass__face" />
      <div className="glass__soft" />
      <div className="glass__sheen" />
    </div>,
    <div className="glass__content">{children}</div>,
  );
}
