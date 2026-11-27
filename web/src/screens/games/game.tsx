import { useState } from 'react';
import { Glass } from '../../components/Glass';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import { PageHeader } from '../../components/kit';
import type { ScreenProps } from '../types';
import { InlineField } from './parts';

/**
 * 小游戏：Views/Games/GamePage.xaml（191 行）+ 同名 code-behind。
 * 版式照源码：顶栏（标题 + 对手名）· 左边棋盘、右边聊天气泡 · 底栏控制条。
 * 对手是当前存档里的那个人，名字由后端给；这一页没有接口，落回源码对 TA 的通用称呼「TA」。
 * 默认开的是五子棋（GameViewModel.GameKindParam 认不出参数时就是 Gobang），
 * 棋盘 15×15 交点、格距 30、四周边距 16 —— 与 GameBoardDrawable 同一组数。
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

export function Game({ back }: ScreenProps) {
  const [stones, setStones] = useState<Stone[]>([]);
  const [black, setBlack] = useState(true);
  const [display, setDisplay] = useState('轮到你走');
  const [input, setInput] = useState('');
  const [messages, setMessages] = useState<string[]>(['TA: 五子棋开始！点击空位落子，连成五子即胜']);

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
      <PageHeader
        title="五子棋"
        onBack={back}
        right={<span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)' }}>TA</span>}
      />

      <div
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          alignItems: 'stretch',
          gap: 'var(--w-space-3)',
          padding: 'var(--w-space-3) var(--w-pad-page)',
          minHeight: 'calc(100% - 57px)',
          boxSizing: 'border-box',
        }}
      >
        {/* 棋盘：图上那一句状态在棋盘正上方 */}
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            gap: 6,
            padding: '8px 0 0 8px',
          }}
        >
          <span
            style={{
              fontSize: 'var(--w-size-caption)',
              color: 'var(--w-ink-muted)',
              maxWidth: BOARD,
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
              width: BOARD,
              height: BOARD,
              boxSizing: 'border-box',
              borderRadius: 10,
              border: '1px solid var(--w-edge)',
              background: 'var(--w-surface-tint)',
            }}
          >
            {Array.from({ length: SIZE }, (_, i) => (
              <span
                key={`h${i}`}
                aria-hidden="true"
                style={{
                  position: 'absolute',
                  left: PAD,
                  top: PAD + i * CELL,
                  width: SPAN,
                  height: 1,
                  background: 'var(--w-edge)',
                }}
              />
            ))}
            {Array.from({ length: SIZE }, (_, i) => (
              <span
                key={`v${i}`}
                aria-hidden="true"
                style={{
                  position: 'absolute',
                  top: PAD,
                  left: PAD + i * CELL,
                  height: SPAN,
                  width: 1,
                  background: 'var(--w-edge)',
                }}
              />
            ))}
            {STARS.map(([r, c]) => (
              <span
                key={`s${r}-${c}`}
                aria-hidden="true"
                style={{
                  position: 'absolute',
                  left: PAD + c * CELL - 3,
                  top: PAD + r * CELL - 3,
                  width: 6,
                  height: 6,
                  borderRadius: 99,
                  background: 'var(--w-ink-muted)',
                }}
              />
            ))}
            {Array.from({ length: SIZE }, (_, r) =>
              Array.from({ length: SIZE }, (_, c) => {
                const stone = stones.find((s) => s.r === r && s.c === c);
                return (
                  <button
                    key={`p${r}-${c}`}
                    type="button"
                    aria-label={`第 ${r + 1} 行第 ${c + 1} 列`}
                    onClick={() => place(r, c)}
                    style={{
                      position: 'absolute',
                      left: PAD + c * CELL - 13,
                      top: PAD + r * CELL - 13,
                      width: 26,
                      height: 26,
                      padding: 0,
                      border: 0,
                      background: 'none',
                      cursor: 'pointer',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                    }}
                  >
                    {stone ? (
                      <span
                        style={{
                          width: CELL * 0.82,
                          height: CELL * 0.82,
                          borderRadius: 99,
                          background: stone.black ? 'var(--w-ink-display)' : 'var(--w-bg)',
                          border: stone.black ? '0' : '1px solid var(--w-edge)',
                        }}
                      />
                    ) : null}
                  </button>
                );
              }),
            )}
          </div>
        </div>

        {/* 聊天区：一摞气泡 + 底下输入行 */}
        <Glass
          tone="card"
          radius={12}
          style={{ flex: '1 1 320px', minWidth: 296, margin: '8px 8px 8px 0', order: 1 }}
        >
          <div
            style={{
              display: 'flex',
              flexDirection: 'column',
              gap: 'var(--w-space-2)',
              padding: 8,
              minHeight: 420,
              height: '100%',
              boxSizing: 'border-box',
            }}
          >
            <div
              style={{
                display: 'flex',
                flexDirection: 'column',
                gap: 'var(--w-space-2)',
                flex: 1,
                overflowY: 'auto',
              }}
            >
              {messages.map((text, index) => {
                const mine = text.startsWith('你: ');
                return (
                  <div key={index} style={{ display: 'flex', justifyContent: mine ? 'flex-end' : 'flex-start' }}>
                    <div
                      style={{
                        maxWidth: 260,
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
                style={{ flex: 1 }}
              />
              <GlassButton size="sm" onClick={send}>
                发送
              </GlassButton>
            </div>
          </div>
        </Glass>
      </div>

      {/* 底栏控制条 */}
      <div
        style={{
          padding: '6px 8px 10px',
          background: 'var(--w-tint-bar)',
          borderTop: '1px solid var(--w-hairline)',
          display: 'flex',
          flexDirection: 'column',
          gap: 'var(--w-space-2)',
        }}
      >
        <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'center', gap: 'var(--w-space-3)' }}>
          <GlassButton size="sm">
            嘲讽
          </GlassButton>
          <button
            type="button"
            className="k-textbtn"
            style={{ color: 'var(--w-signal)', height: 34, padding: '0 14px' }}
          >
            投降
          </button>
          <GlassButton size="sm">
            朗读
          </GlassButton>
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
        </div>
      </div>
    </PageShell>
  );
}
