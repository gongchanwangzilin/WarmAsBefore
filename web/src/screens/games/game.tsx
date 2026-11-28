import { useState } from 'react';
import { Glass } from '../../components/Glass';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import { PageHeader } from '../../components/kit';
import type { ScreenProps } from '../types';
import { Inert, InlineField } from './parts';

/**
 * 小游戏：Views/Games/GamePage.xaml（191 行）+ 同名 code-behind。
 * 版式照源码：顶栏（标题 + 对手名）· 左边棋盘、右边聊天气泡 · 底栏控制条。
 * 对手是当前存档里的那个人 —— 名字取存档里的角色，没有就写「TA」。
 * 默认开的是五子棋（GameViewModel.GameKindParam 认不出参数时就是 Gobang），
 * 棋盘 15×15 交点、格距 30、四周边距 16 —— 与 GameBoardDrawable 同一组数。
 * 棋盘按容器宽度缩（宽窗等于源码的 436，窄窗等比缩），不会把聊天区挤出屏幕。
 */

const SIZE = 15;
const CELL = 30;
const PAD = 16;
const SPAN = (SIZE - 1) * CELL;
const BOARD = SPAN + PAD * 2;
const STARS: [number, number][] = [
  [3, 3],
  [3, 11],
  [11, 3],
  [11, 11],
  [7, 7],
];
const WIN_TEXT_MINE = '你赢了！五子连珠';
const WIN_TEXT_HERS = 'TA 赢了…五子连珠';
const WIN_TEXT_DRAW = '平局，棋盘已满';
const START_TEXT = 'TA: 五子棋开始！点击空位落子，连成五子即胜';

type Stone = { r: number; c: number; black: boolean };

/** 五子连珠判定：从这一手往四个方向数同色。 */
function hasFive(stones: Stone[], last: Stone): boolean {
  const at = (r: number, c: number) =>
    stones.some((s) => s.r === r && s.c === c && s.black === last.black);
  return (
    [
      [1, 0],
      [0, 1],
      [1, 1],
      [1, -1],
    ] as const
  ).some(([dr, dc]) => {
    let count = 1;
    for (const sign of [1, -1]) {
      let r = last.r + dr * sign;
      let c = last.c + dc * sign;
      while (at(r, c)) {
        count += 1;
        r += dr * sign;
        c += dc * sign;
      }
    }
    return count >= 5;
  });
}

/** 棋盘坐标换成百分比，缩放到任何宽度都落在交点上。 */
const pct = (value: number) => `${(value / BOARD) * 100}%`;

export function Game({ back }: ScreenProps) {
  const [stones, setStones] = useState<Stone[]>([]);
  const [black, setBlack] = useState(true);
  const [display, setDisplay] = useState('轮到你走');
  const [input, setInput] = useState('');
  const [messages, setMessages] = useState<string[]>([START_TEXT]);

  const place = (r: number, c: number) => {
    if (stones.length >= SIZE * SIZE) return;
    if (stones.some((s) => s.r === r && s.c === c)) return;
    const next: Stone = { r, c, black };
    const list = [...stones, next];
    setStones(list);
    if (hasFive(list, next)) {
      setDisplay(black ? WIN_TEXT_MINE : WIN_TEXT_HERS);
      return;
    }
    if (list.length === SIZE * SIZE) {
      setDisplay(WIN_TEXT_DRAW);
      return;
    }
    setBlack(!black);
  };

  const send = () => {
    const text = input.trim();
    if (!text) return;
    setMessages([...messages, `你: ${text}`]);
    setInput('');
  };

  return (
    <PageShell>
      <div style={{ display: 'flex', flexDirection: 'column', minHeight: '100%' }}>
        <PageHeader
          title="五子棋"
          onBack={back}
          right={<span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)' }}>TA</span>}
        />

        {/* 中间一行：棋盘（Auto） + 聊天（*），窄窗按源码的顺序折行 */}
        <div
          style={{
            display: 'flex',
            flexWrap: 'wrap',
            alignItems: 'stretch',
            gap: 'var(--w-space-3)',
            padding: 'var(--w-space-3) var(--w-pad-page)',
            flex: '1 1 auto',
            minHeight: 0,
            boxSizing: 'border-box',
          }}
        >
          {/* 棋盘：状态一行在棋盘正上方 */}
          <div
            style={{
              display: 'flex',
              flexDirection: 'column',
              alignItems: 'center',
              gap: 6,
              flex: '0 0 auto',
              width: `min(${BOARD}px, 100%)`,
              minWidth: 0,
            }}
          >
            <span
              style={{
                fontSize: 'var(--w-size-caption)',
                color: 'var(--w-ink-muted)',
                maxWidth: '100%',
                whiteSpace: 'nowrap',
                overflow: 'hidden',
                textOverflow: 'ellipsis',
              }}
            >
              {display}
            </span>
            <div
              style={{
                position: 'relative',
                width: '100%',
                aspectRatio: '1 / 1',
                borderRadius: 10,
                border: '1px solid var(--w-edge)',
                background: 'var(--w-surface-tint)',
              }}
            >
              <svg
                viewBox={`0 0 ${BOARD} ${BOARD}`}
                width="100%"
                height="100%"
                aria-hidden="true"
                style={{ position: 'absolute', inset: 0, display: 'block' }}
              >
                {Array.from({ length: SIZE }, (_, i) => (
                  <line
                    key={`h${i}`}
                    x1={PAD}
                    y1={PAD + i * CELL}
                    x2={PAD + SPAN}
                    y2={PAD + i * CELL}
                    stroke="var(--w-edge)"
                    strokeWidth={1}
                  />
                ))}
                {Array.from({ length: SIZE }, (_, i) => (
                  <line
                    key={`v${i}`}
                    x1={PAD + i * CELL}
                    y1={PAD}
                    x2={PAD + i * CELL}
                    y2={PAD + SPAN}
                    stroke="var(--w-edge)"
                    strokeWidth={1}
                  />
                ))}
                {STARS.map(([r, c]) => (
                  <circle key={`s${r}-${c}`} cx={PAD + c * CELL} cy={PAD + r * CELL} r={3} fill="var(--w-ink-muted)" />
                ))}
                {stones.map((stone) => (
                  <circle
                    key={`o${stone.r}-${stone.c}`}
                    cx={PAD + stone.c * CELL}
                    cy={PAD + stone.r * CELL}
                    r={CELL * 0.41}
                    fill={stone.black ? 'var(--w-ink-display)' : 'var(--w-bg)'}
                    stroke={stone.black ? 'none' : 'var(--w-edge)'}
                    strokeWidth={stone.black ? 0 : 1}
                  />
                ))}
              </svg>

              {/* 交点上的点击区：26 的逻辑尺寸换成百分比，跟棋盘一起缩 */}
              {Array.from({ length: SIZE }, (_, r) =>
                Array.from({ length: SIZE }, (_, c) => {
                  const taken = stones.some((s) => s.r === r && s.c === c);
                  return (
                    <button
                      key={`p${r}-${c}`}
                      type="button"
                      aria-label={`第 ${r + 1} 行第 ${c + 1} 列`}
                      onClick={() => place(r, c)}
                      style={{
                        position: 'absolute',
                        left: pct(PAD + c * CELL - 13),
                        top: pct(PAD + r * CELL - 13),
                        width: pct(26),
                        height: pct(26),
                        padding: 0,
                        border: 0,
                        background: 'none',
                        cursor: taken ? 'default' : 'pointer',
                      }}
                    />
                  );
                }),
              )}
            </div>
          </div>

          {/* 聊天区：一摞气泡 + 底下输入行（CardFrost） */}
          <Glass tone="card" radius={12} style={{ flex: '1 1 320px', minWidth: 0 }}>
            <div
              style={{
                display: 'flex',
                flexDirection: 'column',
                gap: 'var(--w-space-2)',
                padding: 8,
                minHeight: 0,
              }}
            >
              <div
                style={{
                  display: 'flex',
                  flexDirection: 'column',
                  gap: 'var(--w-space-2)',
                  flex: 1,
                  minHeight: 140,
                  maxHeight: 'clamp(180px, 46vh, 460px)',
                  overflowY: 'auto',
                }}
              >
                {messages.map((text, index) => {
                  const mine = text.startsWith('你: ');
                  return (
                    <div key={index} style={{ display: 'flex', justifyContent: mine ? 'flex-end' : 'flex-start' }}>
                      <div
                        style={{
                          maxWidth: 'min(260px, 86%)',
                          padding: '8px 10px',
                          borderRadius: 'var(--w-radius-well)',
                          background: mine ? 'var(--w-signal)' : 'var(--w-surface-tint)',
                          color: mine ? 'var(--w-ink-on-signal)' : 'var(--w-ink-body)',
                          border: mine ? '0' : '1px solid var(--w-hairline)',
                          fontSize: 'var(--w-size-small)',
                          lineHeight: 1.55,
                          wordBreak: 'break-word',
                        }}
                      >
                        {text}
                      </div>
                    </div>
                  );
                })}
              </div>

              <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
                <InlineField
                  value={input}
                  onChange={setInput}
                  placeholder="和 TA 说点什么…"
                  onEnter={send}
                  style={{ flex: 1, minWidth: 0 }}
                />
                <GlassButton size="sm" onClick={send}>
                  发送
                </GlassButton>
              </div>
            </div>
          </Glass>
        </div>

        {/* 底栏控制条：四个动作都走 GameViewModel 的 RelayCommand（要 AI 与语音），Web 端没有桥方法，画成禁用态 */}
        <div
          style={{
            position: 'sticky',
            bottom: 0,
            padding: '6px 8px 10px',
            background: 'var(--w-tint-bar)',
            borderTop: '1px solid var(--w-hairline)',
            display: 'flex',
            flexDirection: 'column',
            gap: 'var(--w-space-2)',
          }}
        >
          <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'center', gap: 'var(--w-space-3)' }}>
            <Inert>
              <GlassButton size="sm">嘲讽</GlassButton>
            </Inert>
            <Inert>
              <button
                type="button"
                className="k-textbtn"
                style={{ color: 'var(--w-signal)', height: 34, padding: '0 14px' }}
              >
                投降
              </button>
            </Inert>
            <Inert>
              <GlassButton size="sm">朗读</GlassButton>
            </Inert>
            <Inert>
              <button
                type="button"
                className="k-textbtn"
                style={{
                  height: 34,
                  width: 44,
                  padding: 0,
                  background: 'var(--w-surface-tint)',
                  borderRadius: 'var(--w-radius-button)',
                  color: 'var(--w-ink-body)',
                }}
              >
                语音
              </button>
            </Inert>
          </div>
        </div>
      </div>
    </PageShell>
  );
}
