import { useEffect, useState } from 'react';
import { Glass } from './Glass';

/**
 * 回退键：只在页面自己没画回退时出现，而且**占位不压字**。
 *
 * 原生 MAUI 的返回是 Shell 画在标题栏里的；换成 WebView 之后那一颗没了，得自己画。
 * 但页面大多已经有自己的回退（页头的 `.ph__back`、顶栏的 `‹`、行尾的「返回」），
 * 所以这一颗是补位，不是重复：只要同一层里已经有任何回退控件，它就不出现；
 * 出现时会给页面头留出同样的左侧空间，绝不像之前那样压住标题。
 */
export function BackChip({
  onBack,
  routeKey,
  force = false,
}: {
  onBack: () => void;
  routeKey: string;
  /** 根级页（主界面、手机）永远要有回退：它们没有页头，也没地方画返回 */
  force?: boolean;
}) {
  const [needed, setNeeded] = useState(force);

  useEffect(() => {
    if (force) {
      setNeeded(true);
      return;
    }
    let cancelled = false;
    setNeeded(false);
    const timer = window.setTimeout(() => {
      if (cancelled) return;
      const layer = document.querySelector('.layer:not([data-leaving])');
      const own =
        !!layer?.querySelector('[data-back], .ph__back, .bar__back, [class*="back" i]') ||
        [...(layer?.querySelectorAll('button, [role="button"]') ?? [])].some((el) => {
          const text = (el.textContent ?? '').trim();
          const name = `${el.getAttribute('aria-label') ?? ''} ${el.getAttribute('title') ?? ''}`;
          return text === '返回' || name.includes('返回') || (/^[‹<◀]/.test(text) && text.length <= 8);
        });
      setNeeded(!own);
    }, 80);
    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [routeKey, force]);

  useEffect(() => {
    if (needed) document.body.dataset.backChip = '1';
    else delete document.body.dataset.backChip;
    return () => {
      delete document.body.dataset.backChip;
    };
  }, [needed]);

  if (!needed) return null;

  return (
    <div className="backchip">
      <Glass
        as="button"
        type="button"
        tone="button"
        radius={9}
        className="backchip__btn"
        data-back="1"
        aria-label="返回上一页"
        onClick={onBack}
      >
        <span aria-hidden="true">‹</span>
        <span className="backchip__label">返回</span>
      </Glass>
    </div>
  );
}
