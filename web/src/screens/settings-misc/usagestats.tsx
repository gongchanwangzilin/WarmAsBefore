import { useState, type CSSProperties, type ReactNode } from 'react';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import { Card, Field, PageHeader, Sep, TextBtn } from '../../components/kit';
import { Segmented, type SegmentedOption } from '../../components/Segmented';
import { useIsWide } from '../../lib/useIsWide';
import type { ScreenProps } from '../types';
import { Inert } from './parts';

/**
 * 用量统计（Views/Settings/UsageStatsPage.xaml）：总览 / 成本构成 / 时间序列 / 分组明细
 * 在左列，Token 活跃度与模型价格设定在右列。账本在本地盘上，后端没有对应的读取方法，
 * 所以这里按「账本为空」的那一档渲染 —— 三档读数、热力网格的空格、时间序列的空槽都照源码摆。
 */

const section: CSSProperties = {
  margin: 0,
  fontSize: 'var(--w-size-subhead)',
  fontWeight: 600,
  color: 'var(--w-ink-display)',
};
const caption: CSSProperties = { fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)' };
const muted: CSSProperties = {
  margin: 0,
  fontSize: 'var(--w-size-small)',
  color: 'var(--w-ink-muted)',
  lineHeight: 1.6,
};
const bodySm: CSSProperties = {
  margin: 0,
  fontSize: 'var(--w-size-small)',
  color: 'var(--w-ink-body)',
  lineHeight: 1.5,
};
const display: CSSProperties = {
  fontSize: 'var(--w-size-display)',
  fontWeight: 600,
  color: 'var(--w-ink-display)',
  fontVariantNumeric: 'tabular-nums',
  whiteSpace: 'nowrap',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
};

const HEAT_DAYS = ['周一', '周二', '周三', '周四', '周五', '周六', '周日'];

/** 热力格：Ramp(0) 再压到 0.1 透明度 —— 空格也占位，网格才数得出 24×7。 */
const HEAT_EMPTY = 'color-mix(in srgb, var(--w-signal) 10%, transparent)';

const FMT_OVERVIEW_UNIT = '未录单价，只统计 token';
const FMT_EMPTY_HINT = '还没有用量记录——端点返回 usage 后会自动记账（部分服务不返回 usage，则无法统计）。';
const FMT_COST_NOTE = '未录单价，只统计 token——在下方「模型价格设定」录入后这里自动变成成本构成。';
const FMT_BREAKDOWN = '累计输入 0（其中缓存命中 0） · 累计输出 0（外加思考/缓存 0）';

/** YYYY-MM-DD 往前数 n 天，末位是今天（与 CostEstimatorService.DayUsageRowsAsync 同序）。 */
function lastDays(n: number): Date[] {
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  return Array.from({ length: n }, (_, i) => new Date(today.getTime() - (n - 1 - i) * 86_400_000));
}

const mmdd = (d: Date) => `${`${d.getMonth() + 1}`.padStart(2, '0')}-${`${d.getDate()}`.padStart(2, '0')}`;

const TREND_OPTIONS: SegmentedOption<'7' | '14' | '30'>[] = [
  { value: '7', label: '7 天' },
  { value: '14', label: '14 天' },
  { value: '30', label: '30 天' },
];

const GROUP_OPTIONS: SegmentedOption<'model' | 'day' | 'character'>[] = [
  { value: 'model', label: '按模型' },
  { value: 'day', label: '按天' },
  { value: 'character', label: '按角色' },
];

export function Usagestats({ back }: ScreenProps) {
  const wide = useIsWide(820);
  const [trendDays, setTrendDays] = useState(14);
  const [detailGroup, setDetailGroup] = useState<'model' | 'day' | 'character'>('model');
  const [pricesOpen, setPricesOpen] = useState(false);
  const [priceNote, setPriceNote] = useState('');
  const [newPrice, setNewPrice] = useState({ model: '', input: '', output: '', cachedInput: '', cachedOutput: '' });

  const days = lastDays(trendDays);
  const slot = trendDays >= 24 ? 11 : trendDays >= 12 ? 20 : 40;
  const bar = trendDays >= 24 ? 8 : trendDays >= 12 ? 16 : 34;

  const left = (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)', minWidth: 0 }}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
        <p style={section}>{FMT_OVERVIEW_UNIT}</p>
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1px 1fr 1px 1fr', gap: 'var(--w-space-3)' }}>
          <HeadBlock label="今日" head="0" meta="0 次调用 · 0 tok" />
          <span style={{ background: 'var(--w-hairline)' }} />
          <HeadBlock label="近 7 天" head="0" meta="0 次调用 · 0 tok" />
          <span style={{ background: 'var(--w-hairline)' }} />
          <HeadBlock label="累计" head="0" meta="0 次调用 · 0 tok" sub="录入单价后这里显示均价" />
        </div>
        <p style={{ ...muted, margin: 0 }}>{FMT_BREAKDOWN}</p>
        <p style={{ ...caption, margin: 0, lineHeight: 1.6 }}>{FMT_EMPTY_HINT}</p>
      </div>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
        <p style={section}>成本构成</p>
        <p style={muted}>{FMT_COST_NOTE}</p>
      </div>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
        <p style={section}>时间序列（按天）</p>
        <Card>
          <Segmented
            label="时间序列范围"
            tone="ghost"
            value={String(trendDays) as '7' | '14' | '30'}
            options={TREND_OPTIONS}
            onChange={(next) => setTrendDays(Number(next))}
          />
          <div
            style={{
              height: 116,
              display: 'flex',
              justifyContent: 'center',
              alignItems: 'flex-end',
              overflow: 'hidden',
            }}
            aria-label={`最近 ${trendDays} 天用量`}
          >
            {days.map((day) => (
              <div key={day.toISOString()} style={{ width: slot, display: 'flex', flexDirection: 'column', justifyContent: 'flex-end' }}>
                <div style={{ display: 'flex', justifyContent: 'center' }} aria-hidden="true">
                  <div style={{ width: bar }} title={`${mmdd(day)} · 0 次 · 输入 0（缓存 0） · 输出 0（思考/缓存 0）`} />
                </div>
              </div>
            ))}
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', gap: 'var(--w-space-2)' }}>
            <span style={{ ...caption, overflow: 'hidden', whiteSpace: 'nowrap', textOverflow: 'ellipsis' }}>
              {mmdd(days[0])}
            </span>
            <span style={{ ...caption, overflow: 'hidden', whiteSpace: 'nowrap', textOverflow: 'ellipsis' }}>
              {mmdd(days[days.length - 1])}
            </span>
          </div>
          <p style={bodySm}>
            最近 {trendDays} 天：0 次 · 0 tok · 缓存命中 0 tok
          </p>
          <p style={{ ...caption, margin: 0, lineHeight: 1.6 }}>
            深色段 = 当日走缓存价的输入 token。当日明细要等账本接上，现在柱子只是占位。
          </p>
        </Card>
      </div>

      <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
        <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: 'var(--w-space-2)' }}>
          <p style={{ ...section, marginLeft: 4 }}>分组明细</p>
          <Segmented
            label="分组明细维度"
            tone="ghost"
            value={detailGroup}
            options={GROUP_OPTIONS}
            onChange={setDetailGroup}
          />
        </div>
        <div style={{ overflowX: 'auto', scrollbarWidth: 'none' }}>
          <Card>
            <div style={{ minWidth: 560, display: 'flex', flexDirection: 'column' }}>
              <div style={TABLE_ROW}>
                {TABLE_HEADS.map((head, i) => (
                  <span key={head} style={{ ...caption, textAlign: i === 0 ? 'left' : 'right' }}>
                    {head}
                  </span>
                ))}
              </div>
              <Sep />
              <p style={{ ...caption, margin: 0, padding: '10px 12px' }}>还没有可分组的记录。</p>
            </div>
          </Card>
        </div>
      </div>
    </div>
  );

  const right = (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)', minWidth: 0 }}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
        <p style={section}>Token 活跃度（星期 × 小时）</p>
        <Card>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
            <div style={{ display: 'grid', gridTemplateColumns: '32px 1fr', gap: 'var(--w-space-1)', alignItems: 'center' }}>
              <span style={caption}>时</span>
              <div style={{ display: 'flex', gap: 2 }}>
                {Array.from({ length: 24 }, (_, h) => (
                  <span key={h} style={{ width: 12, fontSize: 'var(--w-size-caption)', lineHeight: 'var(--w-lh-caption)', textAlign: 'center', color: 'var(--w-ramp-4)' }}>
                    {h % 6 === 0 ? h : ''}
                  </span>
                ))}
              </div>
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
              {HEAT_DAYS.map((day) => (
                <div key={day} style={{ display: 'grid', gridTemplateColumns: '32px 1fr', gap: 'var(--w-space-1)', alignItems: 'center' }}>
                  <span style={{ ...caption, overflow: 'hidden', whiteSpace: 'nowrap', textOverflow: 'ellipsis' }}>{day}</span>
                  <div style={{ display: 'flex', gap: 2 }}>
                    {Array.from({ length: 24 }, (_, h) => (
                      <span
                        key={h}
                        title={`${day} ${h}:00–${(h + 1) % 24}:00 · 0 次 · 0 tok`}
                        style={{ width: 11, height: 15, borderRadius: 2, background: HEAT_EMPTY }}
                      />
                    ))}
                  </div>
                </div>
              ))}
            </div>
            <p style={{ ...caption, margin: '6px 0 0', lineHeight: 1.6 }}>点格子看该小时明细；颜色越深越活跃。</p>
          </div>
        </Card>
      </div>

      <Card>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-2)' }}>
          <p style={section}>模型价格设定</p>
          <TextBtn onClick={() => setPricesOpen((open) => !open)}>{pricesOpen ? '收起' : '展开'}</TextBtn>
        </div>
        {pricesOpen ? (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
            <p style={muted}>
              算钱口径：(输入 − 缓存输入)×输入价 ＋ 缓存输入×缓存输入价 ＋ 输出×输出价 ＋
              缓存输出×缓存输出价。缓存输入本就算在输入里，所以是减法改价、不是加一笔。
            </p>
            <Card>
              <p style={{ ...bodySm, fontWeight: 600 }}>＋ 给没出现过的模型录价</p>
              <Field
                value={newPrice.model}
                onChange={(model) => setNewPrice({ ...newPrice, model })}
                placeholder="模型名（与请求里一致）"
                label="模型名"
              />
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--w-space-1)' }}>
                <Field
                  value={newPrice.input}
                  onChange={(input) => setNewPrice({ ...newPrice, input })}
                  placeholder="输入价/百万"
                  label="输入价/百万"
                  type="number"
                />
                <Field
                  value={newPrice.output}
                  onChange={(output) => setNewPrice({ ...newPrice, output })}
                  placeholder="输出价/百万"
                  label="输出价/百万"
                  type="number"
                />
                <Field
                  value={newPrice.cachedInput}
                  onChange={(cachedInput) => setNewPrice({ ...newPrice, cachedInput })}
                  placeholder="缓存输入价/百万（可留空）"
                  label="缓存输入价/百万（可留空）"
                  type="number"
                />
                <Field
                  value={newPrice.cachedOutput}
                  onChange={(cachedOutput) => setNewPrice({ ...newPrice, cachedOutput })}
                  placeholder="缓存输出价/百万（可留空）"
                  label="缓存输出价/百万（可留空）"
                  type="number"
                />
              </div>
              <div>
                <GlassButton
                  size="sm"
                  onClick={() =>
                    setPriceNote(
                      newPrice.model.trim().length === 0 ? '先填模型名（要和请求里用的模型名一致）。' : '',
                    )
                  }
                >
                  保存单价
                </GlassButton>
              </div>
              {priceNote ? <p style={caption}>{priceNote}</p> : null}
            </Card>
          </div>
        ) : null}
      </Card>
    </div>
  );

  return (
    <PageShell>
      <PageHeader
        onBack={back}
        variant="ground"
        eyebrow="// USAGE"
        title="用量统计"
        subtitle="每次请求的真实 token 记在本地账上：缓存命中的输入按缓存价重算，思考/缓存输出另计一笔。"
        right={
          <>
            <Inert>
              <GlassButton size="sm">刷新</GlassButton>
            </Inert>
            <Inert>
              <GlassButton size="sm">导出 CSV</GlassButton>
            </Inert>
          </>
        }
      />
      <main className="screen">
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: wide ? '1.7fr 1fr' : 'minmax(0, 1fr)',
            gap: 'var(--w-space-3)',
            alignItems: 'start',
          }}
        >
          {left}
          {right}
        </div>
      </main>
    </PageShell>
  );
}

const TABLE_HEADS = ['名称', '调用', '输入', '输出', '缓存输入', '缓存输出', '成本'];

const TABLE_ROW: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: '1.6fr 0.6fr 0.8fr 0.8fr 0.9fr 0.9fr 0.8fr',
  gap: 'var(--w-space-1)',
  padding: '8px 12px',
};

function HeadBlock({ label, head, meta, sub }: { label: string; head: string; meta: string; sub?: string }): ReactNode {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 2, minWidth: 0 }}>
      <span style={caption}>{label}</span>
      <span style={display}>{head}</span>
      <span style={{ ...bodySm, overflow: 'hidden', whiteSpace: 'nowrap', textOverflow: 'ellipsis' }}>{meta}</span>
      {sub ? (
        <span style={{ ...caption, overflow: 'hidden', whiteSpace: 'nowrap', textOverflow: 'ellipsis' }}>{sub}</span>
      ) : null}
    </div>
  );
}
