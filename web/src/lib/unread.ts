import { useEffect, useState } from 'react';
import type { Bridge } from '../bridge/client';

/**
 * 未读：同一份联系人表算出来的两个数 —— 总数（顶栏「手机」、手机桌面与 Dock 上的 QV 角标）
 * 与每人几条（QV 会话行的数字角标）。真相在宿主的 WeChatService（UnreadCount），
 * 这里只负责「什么时候去问」：进页面问一次，她发来一条（chat.arrived）与
 * 有人标读了（chat.unread.changed）之后重问。
 *
 * 到达事件先在本地 +1，角标立刻出现；再等一小会儿问宿主对齐数字 ——
 * 宿主是先落消息、紧接着才把未读 +1 写盘，立刻回问可能读到还没加上的那一份。
 * 那格会话正开着时不算未读：打开会话就算读过（与 MarkReadAsync 同一条语义）。
 */

export type Unread = {
  total: number;
  /** 每个角色几条；没有未读的人不在表里 */
  byContact: Record<string, number>;
};

const NONE: Unread = { total: 0, byContact: {} };

/** 变换成宿主那张表：只认 id 与计数，别的一概不关心。 */
function toUnread(rows: { id: string; count: number }[], total: number): Unread {
  const byContact: Record<string, number> = {};
  for (const row of rows) byContact[row.id] = row.count;
  return { total, byContact };
}

/** 角标上的字：超过 99 收成「99+」。0 条由调用方判断要不要画，这里不替它决定。 */
export function unreadLabel(count: number): string {
  return count > 99 ? '99+' : String(count);
}

let openConversation: string | null = null;

/** 正在屏幕上的那一格会话（QV 换了人、离开了页面都要改它）。提示条据此让开：会话开着就不弹。 */
export function setOpenConversation(id: string | null): void {
  openConversation = id;
}

export function openConversationId(): string | null {
  return openConversation;
}

export function useUnread(bridge: Bridge, watching: boolean): Unread {
  const [summary, setSummary] = useState<Unread>(NONE);

  useEffect(() => {
    if (!watching) {
      setSummary(NONE);
      return;
    }
    let alive = true;
    let reconcile = 0;

    const read = () => {
      bridge.request('chat.unread.summary', {}).then(
        (result) => {
          if (alive) setSummary(toUnread(result.byContact, result.total));
        },
        // 问不到就不动现有数字：一次读失败不该把角标抹掉，下一次事件再对齐。
        () => undefined,
      );
    };

    read();
    const arrived = bridge.onEvent('chat.arrived', (payload) => {
      if (payload.characterId === openConversationId()) return;
      setSummary((prev) => ({
        total: prev.total + 1,
        byContact: { ...prev.byContact, [payload.characterId]: (prev.byContact[payload.characterId] ?? 0) + 1 },
      }));
      window.clearTimeout(reconcile);
      reconcile = window.setTimeout(read, 400);
    });
    const changed = bridge.onEvent('chat.unread.changed', read);

    return () => {
      alive = false;
      window.clearTimeout(reconcile);
      arrived();
      changed();
    };
  }, [bridge, watching]);

  return summary;
}
