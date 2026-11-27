import { useMemo, useRef, useState, type PointerEvent, type ReactNode } from 'react';
import { PageShell } from '../../components/PageShell';
import { PageHeader } from '../../components/kit';
import type { ScreenProps } from '../types';
import { Cap, GhostSm, PickerPlaceholder, PrimarySm, TextSm } from './parts';

type Kind = 'person' | 'memory' | 'tag' | 'place' | 'thing' | 'emotion' | 'word' | 'text';

type GraphNode = {
  id: string;
  label: string;
  kind: Kind;
  net: string;
  x: number;
  y: number;
  depth: number;
  source: 'human' | 'auto';
};

type GraphEdge = { a: string; b: string; kind: string; weight: number };

const KIND_TEXT: Record<Kind, string> = {
  person: '人',
  memory: '记得的事',
  tag: '标签',
  place: '地点',
  thing: '东西',
  emotion: '情绪',
  word: '词',
  text: '一句话',
};

const DETAIL_TEXT: Record<Kind, string> = {
  person: 'TA 记得的一个人。',
  memory: '',
  tag: '一个标签：句子上的 # 号落到网里就是一个点。',
  place: 'TA 记得的一个地点。',
  thing: 'TA 记得的一个东西。',
  emotion: '一种情绪：连着它的那枝是感受，不是事实。',
  word: '一个词：聊天里反复出现的说法，自己长成的点。',
  text: '一句原文。',
};

/**
 * 这张网由 HumanMemoryService 按记忆长出来，Web 端没有取网的 method：
 * 网里没有点时照源码说「这张网还是空的：先和 TA 聊几句，枝会自己长出来。」
 * （MemoryGraphViewModel.cs:287），画布、过滤带与各个面板都按 0 个概念摆。
 */
const NODES: GraphNode[] = [];

const EDGES: GraphEdge[] = [];

const CANVAS_W = 900;
const CANVAS_H = 600;

const byId = (id: string) => NODES.find((node) => node.id === id);
const degree = (id: string) => EDGES.filter((edge) => edge.a === id || edge.b === id).length;
const labelOf = (id: string) => byId(id)?.label ?? '';
const truncate = (text: string, max: number) => (text.length <= max ? text : `${text.slice(0, max)}…`);

type Params = {
  k: number;
  fan: number;
  hop: number;
  halfLife: number;
  permanent: number;
  floor: number;
  depth: number;
};

const DEFAULT_PARAMS: Params = { k: 8, fan: 1, hop: 0.55, halfLife: 60, permanent: 0.3, floor: 0.02, depth: 3 };

/**
 * 关系网（MemoryGraphPage.xaml，412 行）：顶栏九个动作 · 过滤带 · 画布 · 选中/参数/量化/思考/JSON · 图例。
 * 画布是手画的 SVG（圆形节点 + 连线 + 标签），不装图形库；拖动挪画面、滚轮/按钮缩放、点节点选中。
 * 网本身由后端按记忆长出来，读不到网就是一张空画布。
 */
export function MemoryGraph({ navigate, back }: ScreenProps) {
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [zoom, setZoom] = useState(1);
  const [selected, setSelected] = useState<string | null>(null);
  const [net, setNet] = useState<string | null>(null);
  const [panel, setPanel] = useState<'params' | 'quant' | 'think' | 'json' | null>(null);
  const [params, setParams] = useState<Params>(DEFAULT_PARAMS);
  const drag = useRef<{ x: number; y: number } | null>(null);
  const moved = useRef(false);
  const hostRef = useRef<HTMLDivElement | null>(null);

  const nets = useMemo(() => {
    const counts = new Map<string, number>();
    for (const node of NODES) counts.set(node.net, (counts.get(node.net) ?? 0) + 1);
    return [...counts.entries()].map(([name, count]) => ({ name, count }));
  }, []);

  const selectedNode = selected ? byId(selected) : undefined;

  const neighbors = useMemo(() => {
    if (!selected) return [];
    return EDGES.filter((edge) => edge.a === selected || edge.b === selected)
      .sort((left, right) => right.weight - left.weight)
      .slice(0, 14)
      .map((edge) => {
        const other = edge.a === selected ? edge.b : edge.a;
        return { id: other, label: labelOf(other), meta: `${edge.kind} · ${edge.weight.toFixed(1)}` };
      });
  }, [selected]);

  const recall = useMemo(() => {
    if (!selected) return '';
    const fan = Math.pow(Math.max(1, degree(selected)), params.fan);
    const forward = (from: string, hops: number): [string, number][] =>
      EDGES.filter((edge) => (hops === 1 ? edge.a === from : edge.b === from))
        .map((edge) => [hops === 1 ? edge.b : edge.a, edge.weight] as [string, number]);
    const seen = new Map<string, number>();
    const add = (id: string, score: number) => {
      if (id === selected) return;
      seen.set(id, Math.max(seen.get(id) ?? 0, score));
    };
    for (const [id, weight] of forward(selected, 1)) add(id, weight / (weight + params.k) / fan);
    if (params.depth > 1) {
      for (const [id, weight] of forward(selected, 1)) {
        const hopFan = Math.pow(Math.max(1, degree(id)), params.fan);
        const base = weight / (weight + params.k) / hopFan;
        for (const [next, nextWeight] of forward(id, 1)) {
          add(next, base * (nextWeight / (nextWeight + params.k)) / hopFan * params.hop);
        }
      }
    }
    const hits = [...seen.entries()]
      .filter(([, score]) => score >= params.floor)
      .sort((left, right) => right[1] - left[1])
      .slice(0, 10);
    return hits.length === 0
      ? '这样调，什么都想不起来了（阈值太高或联系都断了）。'
      : hits.map(([id, score]) => `${labelOf(id)} ${score.toFixed(2)}`).join('　');
  }, [selected, params]);

  const summary = useMemo(() => {
    const halfLife = params.halfLife <= 0 ? '不遗忘' : `${params.halfLife.toFixed(1)} 天`;
    return `饱和 K=${params.k.toFixed(1)}｜扇形^${params.fan.toFixed(2)}｜二跳×${params.hop.toFixed(
      2,
    )}｜半衰期 ${halfLife}｜永久 ${Math.round(params.permanent * 100)}%｜阈值 ${params.floor.toFixed(3)}`;
  }, [params]);

  const json = useMemo(
    () =>
      JSON.stringify(
        {
          nodes: NODES.map((node) => ({ id: node.id, label: node.label, kind: KIND_TEXT[node.kind], source: node.source })),
          edges: EDGES,
        },
        null,
        2,
      ),
    [],
  );

  const onPointerDown = (event: PointerEvent<SVGSVGElement>) => {
    drag.current = { x: event.clientX, y: event.clientY };
    moved.current = false;
    event.currentTarget.setPointerCapture(event.pointerId);
  };

  const onPointerMove = (event: PointerEvent<SVGSVGElement>) => {
    if (!drag.current) return;
    const rect = hostRef.current?.getBoundingClientRect();
    const factor = rect && rect.width > 0 ? CANVAS_W / rect.width / zoom : 1;
    const dx = (event.clientX - drag.current.x) * factor;
    const dy = (event.clientY - drag.current.y) * (rect && rect.height > 0 ? CANVAS_H / rect.height / zoom : factor);
    if (Math.abs(dx) + Math.abs(dy) > 1) moved.current = true;
    drag.current = { x: event.clientX, y: event.clientY };
    setPan((now) => ({ x: now.x + dx, y: now.y + dy }));
  };

  const onPointerUp = () => {
    drag.current = null;
  };

  const togglePanel = (which: 'params' | 'quant' | 'think' | 'json') =>
    setPanel((now) => (now === which ? null : which));

  const selectedMeta = selectedNode
    ? `${degree(selectedNode.id)} 条联系 · ${selectedNode.source === 'human' ? '人工改过' : '自动积累'} · 「${
        selectedNode.net
      }」这一片` + (selectedNode.depth > 0 ? ` · 离片心 ${selectedNode.depth} 跳` : ' · 这片的地标')
    : '';

  return (
    <PageShell>
      <PageHeader
        eyebrow="// NETWORK"
        title="关系网"
        subtitle="只在这份存档里"
        backText="←"
        onBack={back}
        right={
          <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'flex-end', gap: 6, maxWidth: 520 }}>
            <GhostSm onClick={() => { setPan({ x: 0, y: 0 }); setZoom(1); }}>重播生长</GhostSm>
            <GhostSm onClick={() => { setPan({ x: 0, y: 0 }); setZoom(1); }}>铺满屏幕</GhostSm>
            <GhostSm>跳到一个说法</GhostSm>
            <GhostSm onClick={() => navigate('/memorySearch')}>搜事件</GhostSm>
            <GhostSm onClick={() => togglePanel('params')}>参数模型</GhostSm>
            <GhostSm onClick={() => togglePanel('quant')}>量化模型</GhostSm>
            <GhostSm onClick={() => togglePanel('think')}>思考模型</GhostSm>
            <GhostSm onClick={() => togglePanel('json')}>JSON</GhostSm>
            <TextSm>重新摊网</TextSm>
          </div>
        }
      />

      <div style={{ display: 'flex', flexDirection: 'column' }}>
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 'var(--w-space-2)',
            padding: 'var(--w-space-2) var(--w-pad-card)',
            background: 'var(--w-surface-tint)',
            overflow: 'hidden',
          }}
        >
          <div style={{ display: 'flex', gap: 'var(--w-space-1)', overflowX: 'auto', flex: 1, paddingBottom: 2 }}>
            <GhostSm
              onClick={() => setNet(null)}
              style={{ background: net === null ? 'var(--w-surface-tint)' : undefined, fontWeight: net === null ? 700 : 400 }}
            >
              整张都看
            </GhostSm>
            {nets.map((item) => {
              const active = net === item.name;
              return (
                <GhostSm
                  key={item.name}
                  onClick={() => setNet(active ? null : item.name)}
                  style={{
                    background: active ? 'var(--w-signal)' : undefined,
                    color: active ? 'var(--w-ink-on-signal)' : undefined,
                    fontWeight: active ? 700 : undefined,
                    whiteSpace: 'nowrap',
                  }}
                >
                  {truncate(item.name, 10)} {item.count}
                </GhostSm>
              );
            })}
          </div>
          {NODES.length === 0 ? null : (
            <Cap style={{ whiteSpace: 'nowrap' }}>长好了 · {NODES.length} 个概念</Cap>
          )}
          <Cap style={{ whiteSpace: 'nowrap' }}>
            {NODES.length} 个概念 · {EDGES.length} 条联系 · {nets.length} 片
          </Cap>
        </div>
        <div style={{ height: 1, background: 'var(--w-hairline)' }} />
      </div>

      <div
        ref={hostRef}
        style={{ position: 'relative', flex: 1, minHeight: 560, overflow: 'hidden', background: 'var(--w-bg)' }}
      >
        <svg
          viewBox={`0 0 ${CANVAS_W} ${CANVAS_H}`}
          preserveAspectRatio="xMidYMid meet"
          style={{ position: 'absolute', inset: 0, width: '100%', height: '100%', cursor: 'grab', touchAction: 'none' }}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onPointerCancel={onPointerUp}
          onWheel={(event) => setZoom((value) => Math.min(2.5, Math.max(0.4, value - event.deltaY * 0.001)))}
          role="img"
          aria-label="关系网"
        >
          <g transform={`translate(${pan.x} ${pan.y}) scale(${zoom})`}>
            {EDGES.map((edge) => {
              const from = byId(edge.a);
              const to = byId(edge.b);
              if (!from || !to) return null;
              const dim = net !== null && from.net !== net && to.net !== net;
              return (
                <line
                  key={`${edge.a}-${edge.b}-${edge.kind}`}
                  x1={from.x}
                  y1={from.y}
                  x2={to.x}
                  y2={to.y}
                  style={{
                    stroke: 'var(--w-ink-body)',
                    strokeWidth: 1 + Math.min(3, edge.weight * 0.5),
                    strokeOpacity: dim ? 0.06 : 0.18 + Math.min(0.5, edge.weight * 0.12),
                  }}
                />
              );
            })}
            {NODES.map((node) => {
              const dim = net !== null && node.net !== net;
              const active = selected === node.id;
              const radius = node.kind === 'person' ? 13 : node.kind === 'memory' ? 9 : 7;
              return (
                <g
                  key={node.id}
                  transform={`translate(${node.x} ${node.y})`}
                  opacity={dim ? 0.28 : 1}
                  style={{ cursor: 'pointer' }}
                  onClick={(event) => {
                    event.stopPropagation();
                    if (!moved.current) setSelected(node.id);
                  }}
                >
                  <title>{node.label}</title>
                  {active ? (
                    <circle r={radius + 5} style={{ fill: 'none', stroke: 'var(--w-signal)', strokeWidth: 2 }} />
                  ) : null}
                  <circle
                    r={radius}
                    style={{
                      fill: node.kind === 'person' ? 'var(--w-signal)' : 'var(--w-surface-tint)',
                      stroke: node.kind === 'person' ? 'var(--w-signal)' : 'var(--w-ink-muted)',
                      strokeWidth: 1.2,
                    }}
                  />
                  {node.source === 'auto' ? (
                    <circle cx={radius - 1} cy={-radius + 1} r={2.5} style={{ fill: 'var(--w-ink-muted)' }} />
                  ) : null}
                  <text
                    y={radius + 13}
                    textAnchor="middle"
                    style={{ fontSize: 11, fill: 'var(--w-ink-body)', fontFamily: 'var(--w-font-ui)' }}
                  >
                    {truncate(node.label, 12)}
                  </text>
                </g>
              );
            })}
          </g>
        </svg>

        {NODES.length === 0 ? (
          <div
            style={{
              position: 'absolute',
              inset: 0,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              pointerEvents: 'none',
            }}
          >
            <Cap>这张网还是空的：先和 TA 聊几句，枝会自己长出来。</Cap>
          </div>
        ) : null}

        <div
          style={{
            position: 'absolute',
            left: 10,
            bottom: 10,
            width: 246,
            padding: '10px 12px',
            borderRadius: 'var(--w-radius-card)',
            border: '1px solid var(--w-hairline)',
            background: 'var(--w-surface-tint)',
            display: 'flex',
            flexDirection: 'column',
            gap: 'var(--w-space-1)',
          }}
        >
          {net ? <Cap>只看这一片：{net}</Cap> : null}
          <div style={{ display: 'flex', gap: 'var(--w-space-1)', margin: '2px 0' }}>
            <GhostSm width={34} style={{ padding: 0 }} onClick={() => setZoom((v) => Math.max(0.4, v - 0.15))}>
              －
            </GhostSm>
            <GhostSm width={34} style={{ padding: 0 }} onClick={() => setZoom((v) => Math.min(2.5, v + 0.15))}>
              ＋
            </GhostSm>
            <GhostSm onClick={() => { setPan({ x: 0, y: 0 }); setZoom(1); }}>铺满</GhostSm>
          </div>
          <Cap>拖动=挪画面 · 滚轮=缩放 · 点一下=选中 · 再点一下=钉住它</Cap>
        </div>

        <div
          style={{
            position: 'absolute',
            right: 12,
            top: 10,
            bottom: 10,
            width: 352,
            maxWidth: 'calc(100% - 24px)',
            overflowY: 'auto',
            display: 'flex',
            flexDirection: 'column',
            gap: 'var(--w-space-2)',
          }}
        >
          {selectedNode ? (
            <div
              style={{
                padding: '10px 12px',
                borderRadius: 'var(--w-radius-card)',
                border: '1px solid var(--w-hairline)',
                background: 'var(--w-surface-tint)',
                display: 'flex',
                flexDirection: 'column',
                gap: 'var(--w-space-1)',
              }}
            >
              <span style={{ fontSize: 'var(--w-size-small)', fontWeight: 700, color: 'var(--w-ink-display)' }}>
                {selectedNode.label}
              </span>
              <Cap>{KIND_TEXT[selectedNode.kind]}</Cap>
              <Cap>{selectedMeta}</Cap>
              <span style={{ fontSize: 'var(--w-size-body)', color: 'var(--w-ink-body)', lineHeight: 1.6 }}>
                {selectedNode.kind === 'memory' ? selectedNode.label : DETAIL_TEXT[selectedNode.kind]}
              </span>
              <div style={{ display: 'flex', gap: 'var(--w-space-1)', marginTop: 2, flexWrap: 'wrap' }}>
                <GhostSm>钉住它</GhostSm>
                <GhostSm>和它连…</GhostSm>
                <GhostSm style={{ color: 'var(--w-signal)' }}>忘掉</GhostSm>
              </div>
              <div style={{ height: 1, background: 'var(--w-hairline)', margin: '4px 0' }} />
              <Cap>身上的联系（点一条过去看）</Cap>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                {neighbors.map((row) => (
                  <button
                    key={row.id}
                    type="button"
                    onClick={() => setSelected(row.id)}
                    style={{
                      display: 'flex',
                      gap: 'var(--w-space-1)',
                      padding: '5px 6px',
                      border: 0,
                      borderRadius: 'var(--w-radius-well)',
                      background: 'transparent',
                      font: 'inherit',
                      textAlign: 'left',
                      cursor: 'pointer',
                    }}
                  >
                    <span style={{ fontSize: 12, color: 'var(--w-ink-display)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                      {truncate(row.label, 16)}
                    </span>
                    <span style={{ fontSize: 11, color: 'var(--w-ink-muted)', whiteSpace: 'nowrap' }}>{row.meta}</span>
                  </button>
                ))}
              </div>
              <Cap>点枝（不是点疙瘩）能选中一条联系，然后调弱或断开。</Cap>
              <div style={{ display: 'flex', gap: 'var(--w-space-1)' }}>
                <GhostSm>调弱这条</GhostSm>
                <GhostSm style={{ color: 'var(--w-signal)' }}>断开这条</GhostSm>
              </div>
            </div>
          ) : null}

          {panel === 'params' ? (
            <Panel title="参数模型：这张网怎么算">
              <Cap>同样的网，参数不同就是不同的脑子。改一个数字，下面立刻按新参数重算「现在能想起什么」。</Cap>
              <ParamSlider label="联想饱和 K：要多强的联系才算数" min={0.5} max={40} step={0.5} value={params.k} onChange={(k) => setParams((now) => ({ ...now, k }))} />
              <ParamSlider label="扇形抑制：连得越杂，单条越容易被挤掉（0=不抑制）" min={0} max={2} step={0.05} value={params.fan} onChange={(fan) => setParams((now) => ({ ...now, fan }))} />
              <ParamSlider label="二跳衰减：想到第二步时还剩多少" min={0.05} max={1} step={0.05} value={params.hop} onChange={(hop) => setParams((now) => ({ ...now, hop }))} />
              <ParamSlider label="联系半衰期（天，0=不忘）" min={0} max={365} step={1} value={params.halfLife} onChange={(halfLife) => setParams((now) => ({ ...now, halfLife }))} />
              <ParamSlider label="永久成分：久到极限还剩几分" min={0} max={0.9} step={0.05} value={params.permanent} onChange={(permanent) => setParams((now) => ({ ...now, permanent }))} />
              <ParamSlider label="想不起来的线（低于它就当没想到）" min={0.005} max={0.2} step={0.005} value={params.floor} onChange={(floor) => setParams((now) => ({ ...now, floor }))} />
              <ParamSlider label="往外看几圈" min={1} max={4} step={1} value={params.depth} onChange={(depth) => setParams((now) => ({ ...now, depth }))} />
              <Cap>{summary}</Cap>
              <div style={{ height: 1, background: 'var(--w-hairline)' }} />
              <Cap>现在顺着钉住的那个说法能想起：</Cap>
              <span style={{ fontSize: 'var(--w-size-body)', color: 'var(--w-ink-body)', lineHeight: 1.6 }}>
                {recall || '先在网里点一个点，这里就按当前参数算给它看。'}
              </span>
              <div style={{ display: 'flex', gap: 'var(--w-space-1)' }}>
                <PrimarySm>存成 TA 的联想方式</PrimarySm>
                <GhostSm onClick={() => setParams(DEFAULT_PARAMS)}>回到默认</GhostSm>
              </div>
              <Cap>存了之后，聊天时检索记忆也按这套参数走 —— 不只是画面变了。</Cap>
            </Panel>
          ) : null}

          {panel === 'quant' ? (
            <Panel title="量化模型：把说法换算成数值">
              <Cap>
                知识库管理员干的是这件事：词进去、一组数值出来，活得像同一类东西的说法数值就挨得近。它看得见从没同框出现过的近义 —— 共现统计和全文检索都到不了这一层。
              </Cap>
              {selectedNode ? <Cap>正在问：「{truncate(selectedNode.label, 20)}」</Cap> : null}
              <Cap>本地统计凑的</Cap>
              <Cap>
                {selectedNode
                  ? '数值上没找到像的：这个说法还没攒够语境。多聊几轮再来看。'
                  : '先在网里点一个点（或一段记得的事），这里就列出数值上最接近它的一批说法。'}
              </Cap>
              <PrimarySm>整张网按数值再织一遍</PrimarySm>
              <Cap>
                「牵上」= 把这一对按数值落一条线；「再织一遍」= 让模型把所有挨得近又没连上的对子都补一遍（贵，按一次就好）。
              </Cap>
            </Panel>
          ) : null}

          {panel === 'think' ? (
            <Panel title="思考模型：让模型读一遍网">
              <Cap>
                下面列出的是所有配好的对话模型，任何一款都能当思考模型来用。名字带「会想」的会先想一步再答，其余的直接出建议 —— 这不是一道门槛。建议都要你点头才落地。
              </Cap>
              <PickerPlaceholder title="选一个模型" />
              <div style={{ display: 'flex', gap: 'var(--w-space-1)', flexWrap: 'wrap' }}>
                <PrimarySm>读网出建议</PrimarySm>
              </div>
              <Cap>先在设置里填一个对话模型，这里才有得选。</Cap>
            </Panel>
          ) : null}

          {panel === 'json' ? (
            <Panel title="这张网的 JSON（和磁盘上的文件同一份）">
              <Cap>节点、边、权重、来历都在里面，改它等于改 TA 的脑子。</Cap>
              <pre
                style={{
                  margin: 0,
                  padding: '8px 10px',
                  borderRadius: 'var(--w-radius-well)',
                  border: '1px solid var(--w-hairline)',
                  background: 'var(--w-bg)',
                  color: 'var(--w-ink-body)',
                  fontSize: 11,
                  lineHeight: 1.5,
                  maxHeight: 360,
                  overflow: 'auto',
                  whiteSpace: 'pre-wrap',
                }}
              >
                {json}
              </pre>
            </Panel>
          ) : null}
        </div>
      </div>

      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 'var(--w-space-3)',
          padding: '7px 12px',
          overflowX: 'auto',
          borderTop: '1px solid var(--w-hairline)',
          background: 'var(--w-surface-tint)',
        }}
      >
        <Legend kind="person" label="人" />
        <Legend kind="memory" label="记得的事" />
        <Legend kind="tag" label="标签" />
        <Legend kind="place" label="地点" />
        <Legend kind="thing" label="东西" />
        <Legend kind="emotion" label="情绪" />
        <Legend kind="word" label="词" />
        <span style={{ width: 1, height: 16, background: 'var(--w-hairline)' }} />
        <span style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          <span style={{ display: 'block', height: 2, width: 24, background: 'var(--w-ink-display)', opacity: 0.72 }} />
          <span style={{ display: 'block', height: 2, width: 24, background: 'var(--w-ink-display)', opacity: 0.72 }} />
        </span>
        <Cap>等价（就是它）</Cap>
        <span style={{ height: 4, width: 24, background: 'var(--w-ink-display)', opacity: 0.55, borderRadius: 2 }} />
        <Cap>片里的枝</Cap>
        <span style={{ height: 1, width: 24, background: 'var(--w-ink-display)', opacity: 0.4 }} />
        <Cap>跨片的细藤</Cap>
        <span style={{ height: 3, width: 24, background: 'var(--w-ink-display)', opacity: 0.16, borderRadius: 2 }} />
        <Cap>淡=快要想不起来了</Cap>
        <span style={{ width: 8, height: 8, borderRadius: 4, background: 'var(--w-ink-muted)' }} />
        <Cap>这个点是 AI 整理出来的</Cap>
      </div>
    </PageShell>
  );
}

function Panel({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div
      style={{
        padding: '10px 12px',
        borderRadius: 'var(--w-radius-card)',
        border: '1px solid var(--w-hairline)',
        background: 'var(--w-surface-tint)',
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--w-space-2)',
      }}
    >
      <span style={{ fontSize: 'var(--w-size-small)', fontWeight: 700, color: 'var(--w-ink-display)' }}>{title}</span>
      {children}
    </div>
  );
}

function ParamSlider({
  label,
  min,
  max,
  step,
  value,
  onChange,
}: {
  label: string;
  min: number;
  max: number;
  step: number;
  value: number;
  onChange: (next: number) => void;
}) {
  return (
    <label style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
      <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)' }}>{label}</span>
      <input
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        onChange={(event) => onChange(Number(event.target.value))}
        style={{ width: '100%', accentColor: 'var(--w-signal)' }}
      />
    </label>
  );
}

function Legend({ kind, label }: { kind: Kind; label: string }) {
  const square = kind === 'memory';
  return (
    <span style={{ display: 'inline-flex', alignItems: 'center', gap: 'var(--w-space-1)', whiteSpace: 'nowrap' }}>
      <span
        style={{
          display: 'block',
          width: square ? 13 : 11,
          height: square ? 9 : 11,
          borderRadius: square ? 2 : 6,
          background: kind === 'person' ? 'var(--w-signal)' : 'var(--w-surface-tint)',
          border: square || kind !== 'person' ? '1px solid var(--w-hairline)' : 0,
        }}
      />
      <Cap>{label}</Cap>
    </span>
  );
}
