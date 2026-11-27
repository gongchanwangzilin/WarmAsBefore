import { useEffect, useState } from 'react';
import { Glass } from './Glass';

/**
 * 回退键：长在窗口左上角。
 *
 * 原生 MAUI 的返回是 Shell 画在标题栏里的，换成 WebView 之后那一颗整个没了 ——
 * 所以这一颗必须由我们自己画，而且**每一页都得有**。
 *
 * 两条来源：页面自己的页头已经画了返回（PageHeader 的 `.ph__back`、或自己那枚
 * 「‹ / ◀ / 返回」字钮）时，这里让位不画第二颗；没画时这一颗补上。
 * 位置固定在内容左上、压在页头之上，不占版面，也不改各页自己的排版。
 */
export function BackChip({ onBack, routeKey }: { onBack: () => void; routeKey: string }) {
  const [needed, setNeeded] = useState(true);

  useEffect(() => {
    let cancelled = false;
    // 等这一层渲染完再判断：页面自己的返回是不是已经在了
    const timer = window.setTimeout(() => {
      if (cancelled) return;
      const layer = document.querySelector('.layer:not([data-leaving])');
      const own = layer?.querySelector('[data-back], .ph__back');
      // 「自己已经有回退」只认两种：明确写着返回的字钮，或带 aria-label/title 说明的。
      // 舞台上的 ▶ / ◀ 是换场景的箭头，不是返回 —— 之前把它当成回退，主界面就没补那颗键。
      const textual = [...(layer?.querySelectorAll('button') ?? [])].some((b) => {
        const text = (b.textContent ?? '').trim();
        const name = `${b.getAttribute('aria-label') ?? ''} ${b.getAttribute('title') ?? ''}`;
        return text === '返回' || name.includes('返回') || (/^[‹<]/.test(text) && text.length <= 6);
      });
      setNeeded(!own && !textual);
    }, 60);
    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [routeKey]);

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
