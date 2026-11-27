import { Glass } from '../../components/Glass';
import { GlassButton } from '../../components/GlassButton';
import type { ScreenProps } from '../types';

/**
 * 播放展示案：ShowcasePlayPage.xaml。
 * 背景、立绘、CG 与打字机都从 ShowcaseStore 读一步演一步；没有案可载时，
 * 这一页就是源码里那层空舞台加顶栏，对话与结束框都还不出现。
 */
export function ShowcasePlay({ back }: ScreenProps) {
  const progressText = '';

  return (
    <div style={{ position: 'relative', height: '100%', overflow: 'hidden' }}>
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
            {progressText}
          </span>
          <span className="cap" />
        </div>
      </Glass>
    </div>
  );
}
