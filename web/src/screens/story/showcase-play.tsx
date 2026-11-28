import { useEffect, useState } from 'react';
import { Glass } from '../../components/Glass';
import { GlassButton } from '../../components/GlassButton';
import type { ScreenProps } from '../types';

type ShowcaseRow = {
  id: string;
  name: string;
  steps: number;
  backgroundUrl: string;
};

/**
 * 播放展示案：ShowcasePlayPage.xaml。
 * 从 ?id= 取一案，读 showcases.list 找它；背景与顶栏（退出 · 进度 · 名称）按源码首帧摆好，
 * 对话、立绘、CG 与结束框要一步一步演，这里不越到那几层。
 */
export function ShowcasePlay({ bridge, query, back }: ScreenProps) {
  const [script, setScript] = useState<ShowcaseRow | null>(null);
  const id = query.get('id');

  useEffect(() => {
    let alive = true;
    bridge
      .request('showcases.list', {})
      .then((result) => {
        if (!alive) return;
        const hit = result.showcases.find((showcase) => showcase.id === id);
        setScript(
          hit
            ? { id: hit.id, name: hit.name, steps: hit.steps, backgroundUrl: hit.backgroundUrl }
            : null,
        );
      })
      .catch(() => undefined);
    return () => {
      alive = false;
    };
  }, [bridge, id]);

  return (
    <div style={{ position: 'relative', height: '100%', overflow: 'hidden' }}>
      {script?.backgroundUrl ? (
        <img
          src={script.backgroundUrl}
          alt=""
          style={{ position: 'absolute', inset: 0, width: '100%', height: '100%', objectFit: 'cover' }}
        />
      ) : null}

      <Glass
        as="div"
        tone="card"
        radius={12}
        style={{ position: 'absolute', top: 'var(--w-pad-card)', left: 'var(--w-pad-card)', right: 'var(--w-pad-card)' }}
      >
        <div style={{ display: 'grid', gridTemplateColumns: 'auto 1fr auto', alignItems: 'center', gap: 'var(--w-space-4)' }}>
          <div>
            <GlassButton size="sm" onClick={back}>◀ 退出</GlassButton>
          </div>
          <span style={{ textAlign: 'center', fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)' }}>
            {script ? `0/${script.steps}` : ''}
          </span>
          <span
            className="cap"
            title={script?.name}
            style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
          >
            {script?.name ?? ''}
          </span>
        </div>
      </Glass>
    </div>
  );
}
