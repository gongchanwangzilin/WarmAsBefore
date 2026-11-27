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
      const textual = [...(layer?.querySelectorAll('button') ?? [])].some((b) => {
        const text = (b.textContent ?? '').trim();
        return text.startsWith('‹') || text.startsWith('◀') || text.startsWith('<') || text === '返回';
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
