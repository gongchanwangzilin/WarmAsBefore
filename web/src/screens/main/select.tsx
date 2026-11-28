import { useEffect, useState } from 'react';
import { useEscape } from '../../lib/keys';
import type { SaveRow } from '../../bridge/protocol';
import type { ScreenProps } from '../types';
import './main.css';

/**
 * 选角色：Views/Main/CharacterSelectPage.xaml（107 行）+ CharacterSelectViewModel。
 *
 * 微信壳的联系人列表：顶栏（‹ / 微信 / ＋）、白行 + 发丝线、头像左置、行尾存档读数，
 * 底部两颗「新建角色 / 导入角色包」。源码的 Cards 来自角色库；这里按 saves.list 的
 * 存档表把「谁有档」摆成一行（一份档一行），读不到就落回源码的空态。
 */

type Card = { id: string; name: string; subtitle: string; savesLabel: string };

function pad(value: number): string {
  return String(value).padStart(2, '0');
}

/** 「已有 N 个存档 · 最近 11-26 22:41」（CharacterSelectViewModel.Refresh 的 label）。 */
function savesLabel(count: number, savedAt: string): string {
  const at = new Date(savedAt);
  if (Number.isNaN(at.getTime())) return `已有 ${count} 个存档`;
  return `已有 ${count} 个存档 · 最近 ${pad(at.getMonth() + 1)}-${pad(at.getDate())} ${pad(at.getHours())}:${pad(at.getMinutes())}`;
}

export function Select({ bridge, navigate, back }: ScreenProps) {
  const [cards, setCards] = useState<Card[]>([]);
  const [addOpen, setAddOpen] = useState(false);
  useEscape(addOpen, () => setAddOpen(false));

  useEffect(() => {
    let alive = true;
    // 一份存档一行：同一角色有几份档就写几个数，最新那份的时间上屏。
    void bridge
      .request('saves.list', {})
      .then(({ slots }: { slots: SaveRow[] }) => {
        if (!alive) return;
        const byName = new Map<string, { count: number; latest: string }>();
        for (const row of slots) {
          const name = row.character || row.label;
          if (!name) continue;
          const seen = byName.get(name);
          if (!seen) byName.set(name, { count: 1, latest: row.savedAt });
          else seen.count += 1;
        }
        setCards(
          [...byName.entries()].map(([name, info]) => ({
            id: name,
            name,
            subtitle: '点击开始聊天',
            savesLabel: savesLabel(info.count, info.latest),
          })),
        );
      })
      .catch(() => undefined);
    return () => {
      alive = false;
    };
  }, [bridge]);

  return (
    <div className="mc">
      <div className="mc__head">
        <button type="button" className="mc__back" onClick={back} aria-label="返回">
          ‹
        </button>
        <h1 className="mc__title" data-page-title tabIndex={-1}>
          微信
        </h1>
        <button
          type="button"
          className="mc__plus"
          onClick={() => setAddOpen((open) => !open)}
          aria-label="新建"
        >
          ＋
        </button>
        {addOpen ? (
          <div className="mc__addmenu">
            <button type="button" className="m-btn m-btn--block" onClick={() => navigate('/studio?new=1&start=1')}>
              手动新建
            </button>
            <button type="button" className="m-btn m-btn--block" onClick={() => navigate('/studio?new=1&start=1')}>
              AI 造角色
            </button>
          </div>
        ) : null}
      </div>
      <div className="mc__sep" />

      <div className="mc__list">
        {cards.length === 0 ? (
          <div className="mc__empty">
            <span className="mc__empty-mark">微信</span>
            <span className="mc__empty-title">还没有微信好友</span>
            <span className="mc__empty-hint">
              让 AI 用「add_wechat」把剧情里的角色加为好友，或下方新建/导入
            </span>
          </div>
        ) : (
          cards.map((card) => (
            <div key={card.id}>
              <button
                type="button"
                className="mc__row"
                onClick={() => navigate(`/newrun?character=${encodeURIComponent(card.id)}`)}
              >
                <span className="mc__avatar">{card.name.slice(0, 1)}</span>
                <span className="mc__row-text">
                  <span className="mc__row-name">{card.name}</span>
                  <span className="mc__row-sub">{card.subtitle}</span>
                </span>
                <span className="mc__row-saves">{card.savesLabel}</span>
              </button>
              <div className="mc__row-sep" />
            </div>
          ))
        )}
      </div>

      <div className="mc__foot">
        <button type="button" className="m-btn m-btn--accent" onClick={() => navigate('/studio?new=1&start=1')}>
          ＋ 新建角色
        </button>
        <button type="button" className="m-btn m-btn--soft" disabled>
          导入角色包
        </button>
      </div>
    </div>
  );
}
