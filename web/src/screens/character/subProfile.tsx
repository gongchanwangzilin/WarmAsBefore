import { useState } from 'react';
import { Glass } from '../../components/Glass';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import { Grid2, PageHeader } from '../../components/kit';
import type { ScreenProps } from '../types';
import { Caption, DangerBtn, Hair, Note, Stack } from './parts';

/**
 * 主设定 × 副设定：Views/Character/SubProfilePage.xaml（176 行）+ SubProfileViewModel.cs。
 * 左边主设定（内核，运行时不改），右边副设定（随经历漂移的当下之我），下面心事与漂移留影。
 */

/**
 * SubProfileViewModel.Compare 读的是角色库里的那一份对比结果，Web 端没有对应 method：
 * 读不到角色时源码只写一句「角色不存在」（SubProfileViewModel.cs:57），
 * 其余各栏停在字段级空标记上（同上 61–65）。
 */
const TITLE = '角色不存在';
const MAIN_PERSONALITY = '（空）';
const MAIN_DESCRIPTION = '（空）';
const SUB_PERSONALITY = '（未建立）';
const SUB_DESCRIPTION = '（未建立）';
const SUB_TONE = '（未建立）';

export function SubProfile({ back }: ScreenProps) {
  const [status, setStatus] = useState('');

  const reset = () => {
    const yes = window.confirm('把副设定退回主设定的副本？会清空累积的心境、心事与留影，主设定不受影响。');
    if (!yes) return;
    setStatus('已回正到主设定。');
  };

  return (
    <PageShell>
      <PageHeader title="主设定 × 副设定" onBack={back} />

      <div className="screen" style={{ maxWidth: 860 }}>
        <Stack gap={4}>
          <Stack gap={1}>
            <h2
              style={{
                margin: 0,
                fontSize: 'var(--w-size-display)',
                fontWeight: 600,
                color: 'var(--w-ink-display)',
                lineHeight: 1.35,
              }}
            >
              {TITLE}
            </h2>
            <Caption>左边是主设定（角色库规定，运行时不改）；右边是副设定（随经历漂移的当下之我）。</Caption>
            <span style={{ fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)' }}>
              还没有漂移过——TA 就是主设定里的样子。
            </span>
          </Stack>

          <Grid2>
            <Glass as="section" tone="card" radius={12} style={{ padding: 0 }}>
              <Row title="主设定（内核）" />
              <Hair />
              <Row title="性格" body={MAIN_PERSONALITY} />
              <Hair />
              <Row title="背景" body={MAIN_DESCRIPTION} />
            </Glass>

            <Glass as="section" tone="card" radius={12} style={{ padding: 0 }}>
              <Row title="副设定（当下之我）" />
              <Hair />
              <Row title="性格" body={SUB_PERSONALITY} note="与主设定一致" />
              <Hair />
              <Row title="背景" body={SUB_DESCRIPTION} note="与主设定一致" />
              <Hair />
              <Row title="此刻心境" value={SUB_TONE} />
            </Glass>
          </Grid2>

          <Glass as="section" tone="card" radius={12} style={{ padding: 0 }}>
            <Row title="近期心事（TA 自己放在心上的事）" />
            <Hair />
            <div style={{ padding: '10px var(--w-pad-card)' }}>
              <Stack gap={1}>
                <span style={{ fontSize: 'var(--w-size-body)', color: 'var(--w-ink-body)' }}>暂无心事</span>
                <Caption>心事由每次折叠出的记忆要点累积而来，最多保留 8 条、留影 12 份。</Caption>
              </Stack>
            </div>
          </Glass>

          <div style={{ display: 'flex', justifyContent: 'center', gap: 'var(--w-space-3)', alignItems: 'center' }}>
            <GlassButton size="sm" className="is-off">
              刷新
            </GlassButton>
            <DangerBtn onClick={reset}>副设定回正</DangerBtn>
          </div>

          {status ? <Note>{status}</Note> : null}

          <Note>
            在设置里打开「让模型参与副设定改写」后，TA 会每 12 轮对话重新表述一次「自己现在的样子」（默认关闭，因为要花钱）。
          </Note>
        </Stack>
      </div>
    </PageShell>
  );
}

/** 对比卡里的一行：标题（+ 正文）在左，值 / 差异标记在右。 */
function Row({ title, body, note, value }: { title: string; body?: string; note?: string; value?: string }) {
  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 'var(--w-space-3)',
        padding: '10px var(--w-pad-card)',
      }}
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: 2, flex: 1, minWidth: 0 }}>
        <span style={{ fontSize: 'var(--w-size-body)', color: 'var(--w-ink-body)' }}>{title}</span>
        {body ? <Caption>{body}</Caption> : null}
      </div>
      {note ? (
        <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-signal)', textAlign: 'right' }}>{note}</span>
      ) : null}
      {value ? (
        <span style={{ fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)', textAlign: 'right', maxWidth: '55%' }}>
          {value}
        </span>
      ) : null}
    </div>
  );
}
