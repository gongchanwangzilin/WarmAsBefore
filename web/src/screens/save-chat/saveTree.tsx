import { useState } from 'react';
import { Card, PageHeader, Select } from '../../components/kit';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import type { ScreenProps } from '../types';
import './savechat.css';

/**
 * 分支树：Views/Save/SaveTreePage.xaml（62 行）+ Drawables/SaveTreeDrawable.cs。
 *
 * 页头右侧是「这是哪一局的树」的根档下拉与「去这个存档」；画布是 VS Code 式时间 × 泳道图：
 * 每份存档一列（固定 104px），每条分支一泳道（54px），外层双向可滚。源码用 GraphicsView，
 * 这里手画 SVG：节点矩形 + 折线连线 + 点击后的详情面板（角色/场景/世界/时长/保存/父档/分支）。
 * 存档血缘来自 SaveManager.List()，前端没有接口，一棵树都读不到时就照源码说「还没有存档分支」。
 */

type Node = {
  id: string;
  label: string;
  /** 这一档的角色名 */
  character: string;
  parentId: string;
  column: number;
  lane: number;
  date: string;
  savedAtText: string;
  scene: string;
  world: string;
  duration: string;
  branch: string;
  current?: boolean;
};

const NODES: Node[] = [];

const CARD_W = 92;
const CARD_H = 40;
const LANE_H = 54;
const COLUMN_W = 104;
const LEFT_PAD = 12;
const RIGHT_PAD = 12;
const TOP_PAD = 8;
const AXIS_H = 30;
const PANEL_W = 216;
const PANEL_GAP = 10;

function nodeX(node: Node): number {
  return LEFT_PAD + node.column * COLUMN_W;
}

function nodeY(node: Node): number {
  return TOP_PAD + AXIS_H + node.lane * LANE_H;
}

export function SaveTree({ navigate, back }: ScreenProps) {
  // 根档 = 没有合法父档的存档（SaveTreeViewModel 的 Roots）。
  const roots = NODES.filter((node) => !node.parentId || !NODES.some((candidate) => candidate.id === node.parentId));
  const [rootId, setRootId] = useState(roots[0]?.id ?? '');
  const [selectedId, setSelectedId] = useState('');

  const lanes = NODES.length === 0 ? 0 : Math.max(...NODES.map((node) => node.lane)) + 1;
  const width = LEFT_PAD + NODES.length * COLUMN_W + RIGHT_PAD;
  const height = TOP_PAD + AXIS_H + lanes * LANE_H + 8;

  const selected = NODES.find((node) => node.id === selectedId) ?? null;
  const parent = selected?.parentId ? NODES.find((node) => node.id === selected.parentId) : undefined;

  return (
    <PageShell>
      <PageHeader
        title="分支树"
        onBack={back}
        right={
          <>
            <Select
              label="选择起始存档"
              value={rootId}
              options={roots.map((node) => ({ value: node.id, label: `${node.label} · ${node.savedAtText}` }))}
              onChange={setRootId}
            />
            <span style={selectedId ? undefined : { opacity: 0.5, pointerEvents: 'none' }} aria-disabled={!selectedId}>
              <GlassButton size="sm" className="sc-load" onClick={() => navigate('/save')}>
                去这个存档
              </GlassButton>
            </span>
          </>
        }
      />

      <div className="screen">
        <Card>
          <div className="sc-tree">
            <svg width={width} height={height} role="img" aria-label="存档分支树">
              {NODES.filter((node) => node.parentId).map((node) => {
                const parent = NODES.find((candidate) => candidate.id === node.parentId);
                if (!parent) return null;
                const x1 = nodeX(parent) + CARD_W;
                const y1 = nodeY(parent) + CARD_H / 2;
                const x2 = nodeX(node);
                const y2 = nodeY(node) + CARD_H / 2;
                const mid = x1 + (x2 - x1) / 2;
                return (
                  <path
                    key={`${parent.id}-${node.id}`}
                    d={`M ${x1} ${y1} H ${mid} V ${y2} H ${x2}`}
                    fill="none"
                    stroke="var(--w-hairline)"
                    strokeWidth={1.5}
                  />
                );
              })}

              {NODES.map((node) => {
                const active = node.id === selectedId;
                const current = Boolean(node.current);
                return (
                  <g
                    key={node.id}
                    className="sc-tree__node"
                    onClick={() => setSelectedId(active ? '' : node.id)}
                    role="button"
                    aria-label={node.label}
                  >
                    <text
                      x={nodeX(node) + CARD_W / 2}
                      y={TOP_PAD + 12}
                      textAnchor="middle"
                      fontSize={11}
                      fill="var(--w-ink-faint)"
                    >
                      {node.date}
                    </text>
                    <rect
                      x={nodeX(node)}
                      y={nodeY(node)}
                      width={CARD_W}
                      height={CARD_H}
                      rx={8}
                      fill="var(--w-tint-card)"
                      stroke={active || current ? 'var(--w-signal)' : 'var(--w-hairline)'}
                      strokeWidth={active || current ? 1.5 : 1}
                    />
                    <text
                      x={nodeX(node) + CARD_W / 2}
                      y={nodeY(node) + CARD_H / 2 + 4}
                      textAnchor="middle"
                      fontSize={11}
                      fill="var(--w-ink-body)"
                    >
                      {node.label.length > 7 ? `${node.label.slice(0, 7)}…` : node.label}
                    </text>
                    {current ? (
                      <circle
                        cx={nodeX(node) + CARD_W - 8}
                        cy={nodeY(node) + 8}
                        r={3}
                        fill="var(--w-signal)"
                      />
                    ) : null}
                  </g>
                );
              })}
            </svg>

            {selected ? (
              <div
                className="sc-panel"
                style={{
                  left: Math.min(nodeX(selected) + CARD_W + PANEL_GAP, width - PANEL_W),
                  top: Math.max(nodeY(selected) - 6, 0),
                }}
              >
                <span className="sc-panel__title">{selected.label}</span>
                {[
                  ['角色', selected.character],
                  ['场景', selected.scene],
                  ['世界', selected.world],
                  ['时长', selected.duration],
                  ['保存', selected.savedAtText],
                  ['父档', parent ? parent.label : '无（起始存档）'],
                  ['分支', selected.branch || '无分支'],
                ].map(([key, value]) => (
                  <span key={key} className="sc-panel__row">
                    {key}
                    <b>{value}</b>
                  </span>
                ))}
              </div>
            ) : null}
          </div>
          <p className="cap">
            {NODES.length === 0
              ? '还没有存档分支'
              : `共 ${NODES.length} 个存档 · ${roots.length} 条起始存档 · 当前树 ${NODES.length} 个`}
          </p>
        </Card>
      </div>
    </PageShell>
  );
}
