import { useState } from 'react';
import { Glass } from '../../components/Glass';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import type { ScreenProps } from '../types';
import { Cap } from './parts';

/**
 * 流程图：Views/Games/FlowEditorPage.xaml（70 行）+ 同名 code-behind。
 * 一条规则用人话读三行：什么时候看（触发）· 当什么成立（条件）· 那么做什么（动作）；
 * 一条一段，靠发丝线分，不再一摞同款卡片。
 */

type Rule = {
  id: string;
  name: string;
  enabled: boolean;
  trigger: string;
  when: string;
  then: string;
};

/**
 * 静态一条示例规则：名字、触发档、条件与动作的写法全部取自源码里的示例文案
 * （FlowViewModels 的新建提示「例：天黑了开门口的灯」与 FlowCondition/FlowAction.Describe 的句式）。
 */
const SEED: Rule[] = [
  {
    id: 'seed',
    name: '天黑了开门口的灯',
    enabled: true,
    trigger: '太阳换档（黑天）',
    when: '当 现在是黑天',
    then: '那么 把「门口的灯」改成「开」',
  },
];

export function Flows({ back }: ScreenProps) {
  const [rows, setRows] = useState<Rule[]>(SEED);
  const [status, setStatus] = useState(`共 ${SEED.length} 条规则，改完立刻生效（不用重启）。`);

  const refresh = (next: Rule[], message: string) => {
    setRows(next);
    setStatus(next.length === 0 ? '还没有规则。点「＋ 新规则」写一条；多层与/或的条件可以让 AI 帮你写。' : message);
  };

  const add = () => {
    const name = window.prompt('新规则\n给这条规则起个名字（例：天黑了开门口的灯）');
    if (name === null) return;
    const what = window.prompt('提示什么？\n一句话（会以 Toast 弹出来）');
    if (what === null) return;
    const rule: Rule = {
      id: `r${rows.length + 1}`,
      name: name.trim(),
      enabled: true,
      trigger: '手动',
      when: '当 只要触发',
      then: `那么 说一句：${what.trim()}`,
    };
    refresh([...rows, rule], `共 ${rows.length + 1} 条规则，改完立刻生效（不用重启）。`);
  };

  const edit = (row: Rule) => {
    const name = window.prompt('改规则\n这条规则叫什么？', row.name);
    if (name === null) return;
    refresh(
      rows.map((item) => (item.id === row.id ? { ...item, name: name.trim() } : item)),
      '规则已改好。',
    );
  };

  const toggle = (row: Rule) => {
    const enabled = !row.enabled;
    refresh(
      rows.map((item) => (item.id === row.id ? { ...item, enabled } : item)),
      enabled ? '已启用。' : '已停用：这条规则不再自动触发，手动跑仍可用。',
    );
  };

  const remove = (row: Rule) => {
    if (!window.confirm(`删掉这条规则？\n${row.name}`)) return;
    refresh(rows.filter((item) => item.id !== row.id), '已删掉。');
  };

  return (
    <PageShell>
      <div className="screen" style={{ gap: 'var(--w-space-3)' }}>
        <Glass tone="card" radius={12}>
          <div
            style={{
              display: 'flex',
              flexWrap: 'wrap',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: 'var(--w-space-2)',
              padding: 'var(--w-space-2)',
            }}
          >
            <div style={{ display: 'flex', flexDirection: 'column', gap: 3, minWidth: 0 }}>
              <span style={{ fontSize: 'var(--w-size-title)', fontWeight: 600, color: 'var(--w-ink-display)' }}>
                流程图
              </span>
              <Cap>{status}</Cap>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
              <GlassButton size="sm" onClick={add}>
                ＋ 新规则
              </GlassButton>
              <GlassButton size="sm" onClick={back}>
                返回
              </GlassButton>
            </div>
          </div>
        </Glass>

        <div style={{ display: 'flex', flexDirection: 'column' }}>
          {rows.map((row) => (
            <div key={row.id} style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-1)', padding: '10px 4px' }}>
              <div
                style={{
                  display: 'grid',
                  gridTemplateColumns: 'minmax(0, 1fr) auto',
                  columnGap: 'var(--w-space-3)',
                  rowGap: 'var(--w-space-1)',
                  alignItems: 'center',
                }}
              >
                <span
                  style={{
                    fontSize: 'var(--w-size-body)',
                    color: 'var(--w-ink-body)',
                    whiteSpace: 'nowrap',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                  }}
                >
                  {row.name}
                </span>
                <Cap style={{ whiteSpace: 'nowrap' }}>{row.enabled ? '● 启用' : '○ 停用'}</Cap>

                <Cap style={{ gridColumn: '1 / -1', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                  {row.trigger}
                </Cap>
                <span style={{ gridColumn: '1 / -1', fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)', lineHeight: 1.55 }}>
                  {row.when}
                </span>
                <span style={{ fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)', lineHeight: 1.55 }}>
                  {row.then}
                </span>
                <div style={{ display: 'flex', alignItems: 'flex-end', gap: 'var(--w-space-1)' }}>
                  <GlassButton size="sm" onClick={() => setStatus(`「${row.name}」跑完了。`)}>
                    试跑
                  </GlassButton>
                  <GlassButton size="sm" onClick={() => edit(row)}>
                    改
                  </GlassButton>
                  <GlassButton size="sm" onClick={() => toggle(row)}>
                    启停
                  </GlassButton>
                  <GlassButton size="sm" onClick={() => remove(row)}>
                    删
                  </GlassButton>
                </div>
              </div>
              <div className="k-sep" style={{ margin: '8px 0 0' }} role="presentation" />
            </div>
          ))}
        </div>
      </div>
    </PageShell>
  );
}
