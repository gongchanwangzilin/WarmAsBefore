import { useCallback, useEffect, useState } from 'react';
import type { ChatContactRow, ChatRow } from '../../bridge/protocol';
import { PageShell } from '../../components/PageShell';
import type { ScreenProps } from '../types';
import { ChatShell, type ChatContact, type ChatMessage } from './parts';
import './savechat.css';

/**
 * 微信壳：Views/Chat/WeChatPage.xaml（573 行）+ WeChatViewModel。
 *
 * 左栏竖排入口（消息/好友/收藏/表情/设置）· 会话列表（搜索 + ＋ + 每行头像/名字/最后一条/时间/未读点）
 * · 会话区（顶栏头像 + 名字 + 「本地微信 · TA 还在」+ 全屏 + 微信徽标；气泡我右她左；
 * 输入条：语音 / 礼物 / 输入 / 表情 / 文件 / 发送）。送礼面板与表情空态照抄源码。
 *
 * 会话名册来自 chat.contacts（WeChatService 好友表 ∪ 这一档的角色），
 * 历史与发送走 chat.history / chat.send —— 与微信页同一个 PhoneMessageService 落盘。
 * 名册为空时照源码说「这一档还没有角色」，一条聊天内容都不编。
 */

const toContact = (row: ChatContactRow): ChatContact => ({
  id: row.id,
  name: row.name,
  last: row.last,
  time: row.time,
  unread: row.unread,
});

const toMessage = (row: ChatRow): ChatMessage => ({ id: row.id, text: row.text, mine: row.mine });

export function Chat({ bridge, back }: ScreenProps) {
  const [contacts, setContacts] = useState<ChatContact[]>([]);
  const [modeLabel, setModeLabel] = useState('本地微信 · TA 还在');
  const [activeId, setActiveId] = useState('');
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [nextBefore, setNextBefore] = useState<string | null>(null);
  const [loadHint, setLoadHint] = useState('');

  // 名册：好友在前、这一档其余角色补在后面（宿主那一段的组合规则）。
  const readContacts = useCallback(async () => {
    const { contacts: rows, modeLabel: label } = await bridge.request('chat.contacts', { shell: 'wechat' });
    setContacts(rows.map(toContact));
    setModeLabel(label);
    setActiveId((prev) => (rows.some((row) => row.id === prev) ? prev : rows[0]?.id ?? ''));
    return rows;
  }, [bridge]);

  useEffect(() => {
    let dropped = false;
    void readContacts().catch((cause: unknown) => {
      if (!dropped) setLoadHint(cause instanceof Error ? cause.message : '会话名册没有应答。');
    });
    const off = bridge.onEvent('data.changed', () => void readContacts().catch(() => undefined));
    return () => {
      dropped = true;
      off();
    };
  }, [bridge, readContacts]);

  // 切人就真的去读那一段历史（最近 60 条；更早的用 before 翻页）。
  useEffect(() => {
    let dropped = false;
    setMessages([]);
    if (!activeId) return;
    setLoadHint('');
    void bridge
      .request('chat.history', { shell: 'wechat', characterId: activeId, limit: 60 })
      .then(({ messages: rows, hasMore, nextBefore: cursor }) => {
        if (dropped) return;
        setMessages(rows.map(toMessage));
        setNextBefore(hasMore ? cursor : null);
      })
      .catch((cause: unknown) => {
        if (!dropped) setLoadHint(cause instanceof Error ? cause.message : '会话记录没有应答。');
      });
    return () => {
      dropped = true;
    };
  }, [bridge, activeId]);

  /** 往前翻一页：before 给上一页最早那一条的时间（chat.history 的倒序分页）。 */
  const loadEarlier = async () => {
    if (!activeId || !nextBefore) return;
    const { messages: rows, hasMore, nextBefore: cursor } = await bridge.request('chat.history', {
      shell: 'wechat',
      characterId: activeId,
      before: nextBefore,
      limit: 60,
    });
    setMessages((prev) => [...rows.map(toMessage), ...prev]);
    setNextBefore(hasMore ? cursor : null);
  };

  const send = async (text: string) => {
    if (!activeId) throw new Error('还没有选中任何一个会话。');
    const { user, reply } = await bridge.request('chat.send', { shell: 'wechat', characterId: activeId, text });
    setMessages((prev) => [...prev, toMessage(user), toMessage(reply)]);
    // 「最后一条」是盘上的数据：发完重读一次名册，列表右侧那句预览跟着换。
    void readContacts().catch(() => undefined);
  };

  const partner = contacts.find((contact) => contact.id === activeId)?.name ?? 'TA';

  return (
    <PageShell>
      <ChatShell
        skin="wechat"
        contacts={contacts}
        activeId={activeId}
        onSelectContact={setActiveId}
        partner={partner}
        modeLabel={modeLabel}
        messages={messages}
        onBack={back}
        onSend={send}
        loadHint={activeId ? loadHint : ''}
        hasMore={Boolean(nextBefore)}
        onLoadEarlier={loadEarlier}
      />
    </PageShell>
  );
}
