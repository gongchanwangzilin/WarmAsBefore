import { PageHeader } from './kit';
import { PageShell } from './PageShell';
import { StateBlock } from './states/StateBlock';
import { NEED_SAVE } from '../lib/run';

/**
 * 系在存档上的页在没存档时的统一停法：页头还在（能返回），中间一块说清为什么空、
 * 以及往哪走（存档管理）。这些页自己画不出来 —— 它们画的全是「这一局」的状态。
 */
export function SaveGate({
  title,
  eyebrow,
  onBack,
  onGoSave,
}: {
  title: string;
  eyebrow?: string;
  onBack: () => void;
  onGoSave: () => void;
}) {
  return (
    <PageShell>
      <PageHeader title={title} eyebrow={eyebrow} backText="←" onBack={onBack} />
      <div className="screen">
        <StateBlock
          tone="empty"
          title="这一页要先有一份存档"
          body={
            <>
              {title}画的是这一局里 TA 的状态。{NEED_SAVE}：没有存档，就没有角色、没有读数、没有要说的话。
            </>
          }
          action="去存档管理"
          onAction={onGoSave}
        />
      </div>
    </PageShell>
  );
}
