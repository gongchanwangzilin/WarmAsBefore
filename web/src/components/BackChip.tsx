import { useEffect, useState } from 'react';
import { Glass } from './Glass';

/**
 * 回退键：只在页面自己没画回退时出现，而且**占位不压字**。
 *
 * 原生 MAUI 的返回是 Shell 画在标题栏里的；换成 WebView 之后那一颗没了，得自己画。
 * 但页面大多已经有自己的回退（页头的 `.ph__back`、顶栏的 `‹`、行尾的「返回」），
 * 所以这一颗是补位，不是重复：同一层里只要出现任何回退控件，它就让位。
 *
 * 这里不停在「第一次判断」上：页面是懒加载的，页头往往比正文晚挂上，
 * 判一次就定死会出现「页头画了返回、补位的也还在」两条。所以整个路由存续期间
 * 都盯着 DOM（rAF 合并，一次只查一个选择器），页头一出现就自己收起来。
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
    let frame = 0;
    let observer: MutationObserver | null = null;
    const startedAt = performance.now();

    const decide = () => {
      frame = 0;
      if (cancelled) return;
      const layer = document.querySelector('.layer:not([data-leaving])');
      if (!layer) {
        setNeeded(false);
        return;
      }
      const own = !!layer.querySelector(
        '[data-back], .ph__back, .bar__back, .backchip__btn:not(.backchip__btn--self), [class*="back" i]',
      ) || [...layer.querySelectorAll('button, [role="button"]')].some((el) => {
        const text = (el.textContent ?? '').trim();
        const name = `${el.getAttribute('aria-label') ?? ''} ${el.getAttribute('title') ?? ''}`;
        return text === '返回' || name.includes('返回') || (/^[‹<◀]/.test(text) && text.length <= 8);
      });
      if (own) {
        setNeeded(false);
        return;
      }
      // 页头还没来：先别摆键，等这一层真的有内容（250ms 之后仍空就直接摆，免得整页无从退出）
      const settledIn = performance.now() - startedAt > 250;
      const hasContent = (layer.textContent ?? '').trim().length > 30;
      setNeeded(settledIn && hasContent);
    };

    const ping = () => {
      if (frame) return;
      frame = requestAnimationFrame(decide);
    };

    decide();
    observer = new MutationObserver(ping);
    observer.observe(document.body, { childList: true, subtree: true, characterData: true });
    const settle = window.setTimeout(ping, 260);

    return () => {
      cancelled = true;
      observer?.disconnect();
      window.clearTimeout(settle);
      if (frame) cancelAnimationFrame(frame);
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
        className="backchip__btn backchip__btn--self"
        aria-label="返回上一页"
        onClick={onBack}
      >
        <span aria-hidden="true">‹</span>
        <span className="backchip__label">返回</span>
      </Glass>
    </div>
  );
}
