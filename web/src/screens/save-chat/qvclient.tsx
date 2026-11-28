import { useEffect, useState } from 'react';
import { toErrorBody, type Bridge } from '../../bridge/client';
import type { CharacterRow, ChatContactRow, ChatRow } from '../../bridge/protocol';
import { useIsWide } from '../../lib/useIsWide';
import { useVoice } from '../../lib/voice';
import { Icon, type IconName } from './qqicons';
import { QqSettings } from './qqsettings';
import './savechat.css';

/**
 * QV：QQ 与假微信合成的那一个应用。壳是 QqPage.xaml 的四栏（真机客户端三张参照截图），
 * 会话名册把两个壳的名单并成一条。
 *
 * 左窄条工具栏 / 会话列表 / 会话区 / 右侧信息栏。内容只认仓库里已有的数据与源码原话：
 *   · 会话名册与模式：chat.contacts（shell: wechat 与 qq 各读一次）—— 老宿主没有这个方法时，
 *     退回角色库真表 characters.list，那时预览停在源码那句「还没有聊过」；
 *   · 会话内容与发送：chat.history / chat.send，shell 跟着这条会话的来源走；
 *   · 等级：Helpers/AffectionScale（每 100 点一级，0 点也算第 1 级，短标「Lv.N」），来自 characters.list；
 *   · 空态：「还没有好友」「让 AI 用「add_wechat」把剧情里的角色加为好友」（QqPage.xaml），
 *     预览「还没有聊过」（WeChatViewModel.LoadContactsAsync），输入框占位「输入…」。
 *
 * 两个来源：宿主 WebBridge.ChatContactsAsync 对 wechat / qq 给的是同一份名册（好友表 ∪ 这一档角色，
 * 只有 modeLabel 不同），所以合并按角色 id 去重，一行记下它在哪几个壳的名单里，标记就照这两个
 * 源码里的词写「微信」「QQ」；打开会话时用它的第一个来源去读历史、去发送。
 * 名册里没有跨壳的时间戳，合并顺序照宿主给的名单顺序（显式好友在前），不臆造按时间重排。
 *
 * 未读数、群公告、群主/管理员角色这几样桥里没有：各自停在空态，结构照真机留着。
 */

type Person = {
  id: string;
  name: string;
  avatarUrl: string;
  /** 角色存档里的口头禅（CharacterRow.catchphrase），没有就是空串 */
  signature: string;
  mood: string;
  /** Helpers/AffectionScale.LevelOf —— 1..10 */
  level: number;
};

type Source = 'wechat' | 'qq';

type Contact = {
  id: string;
  name: string;
  avatarUrl: string;
  /** 最近一条的摘要；空 = 源码的「还没有聊过」 */
  last: string;
  /** 右上角相对时间；没有最后一条就是空串 */
  time: string;
  unread: boolean;
  /** 这条会话出现在哪几个壳的名册里，第一个就是打开它时用的壳 */
  shells: Source[];
};

/** 两个来源，词都取自源码（ChatShellOf 的 wechat / qq，路由名「微信」「QQ」）。 */
const SOURCES: { shell: Source; word: string }[] = [
  { shell: 'wechat', word: '微信' },
  { shell: 'qq', word: 'QQ' },
];

const wordOf = (shell: Source) => SOURCES.find((item) => item.shell === shell)?.word ?? shell;

/** 两个壳的名单并成一条：同一个人只留一行，来源按读到的顺序记下来。 */
function mergeContacts(groups: { shell: Source; rows: ChatContactRow[] }[]): Contact[] {
  const byId = new Map<string, Contact>();
  for (const group of groups) {
    for (const row of group.rows) {
      const known = byId.get(row.id);
      if (known) {
        if (!known.shells.includes(group.shell)) known.shells.push(group.shell);
        continue;
      }
      byId.set(row.id, {
        id: row.id,
        name: row.name,
        avatarUrl: row.avatarUrl,
        last: row.last,
        time: row.time,
        unread: row.unread,
        shells: [group.shell],
      });
    }
  }
  return [...byId.values()];
}

const PER_LEVEL = 100;
const levelOf = (points: number) => (points <= 0 ? 1 : Math.min(10, Math.floor((points - 1) / PER_LEVEL) + 1));

const toPerson = (row: CharacterRow): Person => ({
  id: row.id,
  name: row.nickname || row.name,
  avatarUrl: row.avatarUrl || '',
  signature: row.catchphrase || '',
  mood: row.mood || '',
  level: levelOf(row.affection),
});

/** 会话行里那种带色的小标签：真机是 [有新文件] / [@全体成员]。 */
const PREVIEW_TAG = /(\[有新文件\]|\[@全体成员\])/g;

function previewParts(text: string) {
  return text.split(PREVIEW_TAG).filter(Boolean).map((part, index) => {
    if (part === '[有新文件]') {
      return (
        <em key={index} className="qw-tag" data-kind="file">
          {part}
        </em>
      );
    }
    if (part === '[@全体成员]') {
      return (
        <em key={index} className="qw-tag" data-kind="at">
          {part}
        </em>
      );
    }
    return <span key={index}>{part}</span>;
  });
}

/** 消息流里的一行：日期分隔 / 系统提示 / 一条聊天。 */
type ChatLine =
  | { id: string; kind: 'day'; text: string }
  | { id: string; kind: 'system'; text: string }
  | {
      id: string;
      kind: 'text';
      mine: boolean;
      name: string;
      level: number;
      time: string;
      text: string;
      avatarUrl: string;
    };

const MENTION = /(@[^\s@]+)/g;

function spoken(text: string) {
  return text.split(MENTION).map((part, index) =>
    part.startsWith('@') ? (
      <em key={index} className="qw-at">
        {part}
      </em>
    ) : (
      <span key={index}>{part}</span>
    ),
  );
}

const pad = (value: number) => String(value).padStart(2, '0');

/** 消息时间：今天给 HH:mm，其余按 WeChatViewModel.RelativeTime 的词汇给「昨天」「M月d日」。 */
function dayWord(at: string): string {
  const date = new Date(at);
  if (Number.isNaN(date.getTime())) return '';
  const today = new Date();
  const day = new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();
  const base = new Date(today.getFullYear(), today.getMonth(), today.getDate()).getTime();
  const diff = Math.round((base - day) / 86_400_000);
  if (diff === 0) return '今天';
  if (diff === 1) return '昨天';
  return `${date.getMonth() + 1}月${date.getDate()}日`;
}

function clockOf(at: string): string {
  const date = new Date(at);
  return Number.isNaN(date.getTime()) ? '' : `${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

function toLines(rows: ChatRow[], roster: Person[]): ChatLine[] {
  const lines: ChatLine[] = [];
  let seenDay = '';
  for (const row of rows) {
    const word = dayWord(row.at);
    if (word && word !== seenDay) {
      lines.push({ id: `day-${row.id}`, kind: 'day', text: `${word} ${clockOf(row.at)}` });
      seenDay = word;
    }
    const person = roster.find((item) => item.name === row.sender || item.id === row.sender);
    lines.push({
      id: row.id,
      kind: 'text',
      mine: row.mine,
      name: row.sender,
      level: person?.level ?? 0,
      time: clockOf(row.at),
      text: row.text,
      avatarUrl: person?.avatarUrl ?? '',
    });
  }
  return lines;
}

const CHAT_TOOLS: { icon: IconName; label: string; reason: string }[] = [
  { icon: 'phone', label: '语音通话', reason: '通话要接真机通道' },
  { icon: 'video', label: '视频通话', reason: '通话要接真机通道' },
  { icon: 'screen', label: '屏幕共享', reason: '共享要接真机通道' },
  { icon: 'apps', label: '群应用', reason: '群应用面板还没有桥' },
  { icon: 'gear', label: '会话设置', reason: '会话设置还没有桥' },
  { icon: 'more', label: '更多', reason: '更多菜单还没有桥' },
];

const INPUT_TOOLS: { icon: IconName; label: string; reason: string }[] = [
  { icon: 'smile', label: '表情', reason: '表情面板要读表情库' },
  { icon: 'scissors', label: '截图', reason: '截图要接真机的截屏通道' },
  { icon: 'folder', label: '文件', reason: '发文件要接存储桥' },
  { icon: 'image', label: '图片', reason: 'chat.history 的行里只有文字，没有图片字段' },
  { icon: 'gift', label: '礼物', reason: 'QQ 壳没有礼物面板（QqPage.xaml 注释）' },
  { icon: 'clock', label: '定时消息', reason: '定时消息还没有桥' },
];

const MENU_ITEMS: { icon: IconName; label: string; ready?: boolean }[] = [
  { icon: 'chat', label: '聊天记录管理' },
  { icon: 'gear', label: '检查更新' },
  { icon: 'info', label: '帮助' },
  { icon: 'shield', label: '锁定' },
  { icon: 'gear', label: '设置', ready: true },
  { icon: 'power', label: '退出账号' },
];

function Avatar({
  person,
  className,
}: {
  person: { name: string; avatarUrl: string };
  className?: string;
}) {
  return (
    <span className={className ?? 'qw-av'}>
      {person.avatarUrl ? <img src={person.avatarUrl} alt="" /> : <span>{person.name.slice(0, 1) || '?'}</span>}
    </span>
  );
}

export function QvClient({ bridge, navigate, back }: { bridge: Bridge; navigate: (path: string) => void; back: () => void }) {
  const [roster, setRoster] = useState<Person[]>([]);
  const [contacts, setContacts] = useState<Contact[] | null>(null);
  const [modeLabels, setModeLabels] = useState<Partial<Record<Source, string>>>({});
  const [live, setLive] = useState(false);
  const [lines, setLines] = useState<ChatLine[]>([]);
  const [historyState, setHistoryState] = useState<'idle' | 'loading' | 'ready'>('idle');
  // 读会话失败和「还没聊过」是两回事：失败要说出来，并给一次重试。
  const [historyError, setHistoryError] = useState('');
  const [historyTicket, setHistoryTicket] = useState(0);
  const [sendError, setSendError] = useState('');
  const [sending, setSending] = useState(false);

  const [tab, setTab] = useState<'chat' | 'people'>('chat');
  const [activeId, setActiveId] = useState('');
  const [search, setSearch] = useState('');
  const [draft, setDraft] = useState('');
  const [menu, setMenu] = useState(false);
  const [settings, setSettings] = useState(false);
  const [drawer, setDrawer] = useState(false);
  const [noticeOpen, setNoticeOpen] = useState(false);
  const [memberQuery, setMemberQuery] = useState('');
  const [memberSearch, setMemberSearch] = useState(false);

  const showInfo = useIsWide(1180);
  const showList = useIsWide(880);

  useEffect(() => {
    let dropped = false;
    const read = async () => {
      let people: Person[] = [];
      try {
        const { characters } = await bridge.request('characters.list', {});
        people = characters.map(toPerson);
      } catch {
        people = [];
      }
      if (dropped) return;
      setRoster(people);

      // 两个壳各读一次名册（WebBridge 的 chat.contacts 照 WeChatViewModel.LoadContactsAsync 组表：
      // 显式好友在前，这一档其余角色补在后面）。一个壳失败不拖累另一个。
      const answered = await Promise.allSettled(
        SOURCES.map(async (source) => ({
          shell: source.shell,
          result: await bridge.request('chat.contacts', { shell: source.shell }),
        })),
      );
      if (dropped) return;

      const groups: { shell: Source; rows: ChatContactRow[] }[] = [];
      const labels: Partial<Record<Source, string>> = {};
      for (const outcome of answered) {
        if (outcome.status !== 'fulfilled') continue;
        groups.push({ shell: outcome.value.shell, rows: outcome.value.result.contacts });
        if (outcome.value.result.modeLabel) labels[outcome.value.shell] = outcome.value.result.modeLabel;
      }
      setModeLabels(labels);

      if (groups.length === 0) {
        // 老宿主没有 chat.contacts：拿角色库当名单，预览停在源码空态。
        setContacts(
          people.map((person) => ({
            id: person.id,
            name: person.name,
            avatarUrl: person.avatarUrl,
            last: '',
            time: '',
            unread: false,
            shells: [],
          })),
        );
        setLive(false);
        setActiveId((prev) => (people.some((person) => person.id === prev) ? prev : people[0]?.id ?? ''));
        return;
      }

      const merged = mergeContacts(groups);
      setContacts(merged);
      setLive(true);
      setActiveId((prev) => (merged.some((row) => row.id === prev) ? prev : merged[0]?.id ?? ''));
    };
    void read();
    const off = bridge.onEvent('data.changed', () => void read());
    return () => {
      dropped = true;
      off();
    };
  }, [bridge]);

  const people = contacts ?? [];
  const active = people.find((item) => item.id === activeId) ?? people[0] ?? null;
  const activePerson = roster.find((item) => item.id === active?.id) ?? null;
  /** 打开这条会话用的壳：它名册里的第一个来源（名册退回角色库时没有来源，按宿主默认的 wechat）。 */
  const activeShell: Source = active?.shells[0] ?? 'wechat';
  // 麦克风走宿主的 voice.listen（同一句听写链路），先把文字填进输入框，不替用户发送。
  const voice = useVoice(bridge, activeShell);
  const toggleMic = async () => {
    if (voice.listening) {
      await voice.stop();
      return;
    }
    const heard = await voice.listen(active?.id);
    if (!heard) return;
    setDraft((prev) => (prev ? `${prev}${heard}` : heard));
  };
  const mode = modeLabels[activeShell] ?? '本地微信 · TA 还在';

  useEffect(() => {
    if (!active?.id || !live) {
      setLines([]);
      setHistoryState('idle');
      return;
    }
    let dropped = false;
    setHistoryState('loading');
    setHistoryError('');
    bridge
      .request('chat.history', { shell: activeShell, characterId: active.id, limit: 50 })
      .then((result) => {
        if (dropped) return;
        setLines(toLines(result.messages, roster));
        setHistoryState('ready');
      })
      .catch((cause: unknown) => {
        if (dropped) return;
        setLines([]);
        setHistoryError(cause instanceof Error ? cause.message : '会话记录没有应答。');
        setHistoryState('ready');
      });
    return () => {
      dropped = true;
    };
  }, [bridge, live, active?.id, activeShell, roster, historyTicket]);

  const query = search.trim();
  const rows = query
    ? people.filter((item) => item.name.includes(query) || item.last.includes(query))
    : people;
  const members = memberQuery.trim() ? people.filter((item) => item.name.includes(memberQuery.trim())) : people;
  const newCount = people.filter((item) => item.unread).length;

  const pick = (id: string) => {
    setActiveId(id);
    setDrawer(false);
    setTab('chat');
  };

  const send = async () => {
    const text = draft.trim();
    if (!text || !active || !live || sending) return;
    setSending(true);
    setSendError('');
    try {
      const result = await bridge.request('chat.send', { shell: activeShell, characterId: active.id, text });
      setDraft('');
      setLines((prev) => [...prev, ...toLines([result.user, result.reply], roster)]);
      setHistoryState('ready');
    } catch (error) {
      setSendError(toErrorBody(error).message);
    } finally {
      setSending(false);
    }
  };

  const railTabs: { id: string; label: string; icon: IconName; ready: boolean }[] = [
    { id: 'chat', label: '消息', icon: 'chat', ready: true },
    { id: 'people', label: '联系人', icon: 'people', ready: true },
    { id: 'star', label: '收藏', icon: 'star', ready: false },
    { id: 'files', label: '文件', icon: 'folder', ready: false },
  ];

  const listColumn = (
    <div className="qw-list">
      <div className="qw-list__head">
        <span className="qw-search">
          <Icon name="search" className="qw-search__icon" />
          <input
            value={search}
            placeholder="搜索"
            aria-label="搜索"
            onChange={(event) => setSearch(event.target.value)}
          />
        </span>
        <button type="button" className="qw-list__plus" aria-label="加好友" title="加好友走 AI 的 add_wechat，Web 端没有入口" disabled>
          <Icon name="plus" />
        </button>
      </div>

      <div className="qw-list__rows">
        {contacts === null ? (
          <div className="qw-skel" aria-hidden="true">
            <i />
            <i />
            <i />
            <i />
            <i />
          </div>
        ) : rows.length === 0 ? (
          <div className="qw-empty">
            <Icon name="people" className="qw-empty__icon" />
            <b>{query ? `没有匹配「${query}」的人` : '还没有好友'}</b>
            <span>让 AI 用「add_wechat」把剧情里的角色加为好友</span>
          </div>
        ) : (
          rows.map((contact) => {
            const person = roster.find((item) => item.id === contact.id) ?? null;
            const last = tab === 'people' && person?.signature ? person.signature : contact.last || '还没有聊过';
            return (
              <button
                key={contact.id}
                type="button"
                className="qw-row"
                aria-pressed={contact.id === active?.id}
                onClick={() => pick(contact.id)}
              >
                <Avatar person={contact} className="qw-row__av" />
                <span className="qw-row__mid">
                  <span className="qw-row__name">
                    <span className="qw-row__who">{contact.name}</span>
                    {contact.shells.map((shell) => (
                      <em key={shell} className="qw-src" title={`这条会话在${wordOf(shell)}的名册里`}>
                        {wordOf(shell)}
                      </em>
                    ))}
                  </span>
                  <span className="qw-row__last">{previewParts(last)}</span>
                </span>
                <span className="qw-row__side">
                  {contact.time ? <span className="qw-row__time">{contact.time}</span> : null}
                  {contact.unread ? <i className="qw-row__dot" aria-label="未读" /> : null}
                </span>
              </button>
            );
          })
        )}
      </div>
    </div>
  );

  const chatColumn = (
    <section className="qw-chat">
      <header className="qw-chat__head">
        {showList ? null : (
          <button type="button" className="qw-ibtn" aria-label="会话列表" onClick={() => setDrawer(true)}>
            <Icon name="menu" />
          </button>
        )}
        <div className="qw-chat__title">
          <h2>{active ? active.name : 'QV'}</h2>
          <small>{mode}</small>
        </div>
        <span className="qw-chip">QV</span>
        <div className="qw-chat__tools">
          {CHAT_TOOLS.map((tool) => (
            <button key={tool.label} type="button" className="qw-ibtn" aria-label={tool.label} title={tool.reason} disabled>
              <Icon name={tool.icon} />
            </button>
          ))}
        </div>
      </header>

      <div className="qw-msgs">
        {newCount > 0 ? (
          <button type="button" className="qw-newpill">
            <Icon name="chevron" className="qw-newpill__up" />
            {newCount > 999 ? '999+' : newCount} 条新消息
          </button>
        ) : null}

        {lines.length === 0 ? (
          historyState === 'loading' ? (
            <div className="qw-skel qw-skel--msgs" aria-hidden="true">
              <i />
              <i />
              <i />
            </div>
          ) : historyError ? (
            <div className="qw-hello">
              <span className="qw-hello__line">这段会话读不出来</span>
              <small className="qw-hello__hint">{historyError}</small>
              <button type="button" className="k-textbtn" onClick={() => setHistoryTicket((n) => n + 1)}>
                重试
              </button>
            </div>
          ) : active ? (
            <div className="qw-hello">
              <Avatar person={active} className="qw-hello__av" />
              <b className="qw-hello__name">{active.name}</b>
              {activePerson?.signature ? <span className="qw-hello__sign">{activePerson.signature}</span> : null}
              <span className="qw-hello__line">还没有聊过</span>
              <small className="qw-hello__hint">说点什么，TA 会在下面回你</small>
            </div>
          ) : (
            <div className="qw-hello">
              <span className="qw-hello__line">还没有好友</span>
              <small className="qw-hello__hint">先在角色库建一个人，TA 就会出现在联系人里</small>
            </div>
          )
        ) : (
          lines.map((line) => {
            if (line.kind === 'day') {
              return (
                <div key={line.id} className="qw-day">
                  <span>{line.text}</span>
                </div>
              );
            }
            if (line.kind === 'system') {
              return (
                <div key={line.id} className="qw-system">
                  {line.text}
                </div>
              );
            }
            return (
              <div key={line.id} className={line.mine ? 'qw-msg qw-msg--mine' : 'qw-msg'}>
                {line.mine ? null : <Avatar person={{ name: line.name, avatarUrl: line.avatarUrl }} className="qw-msg__av" />}
                <div className="qw-msg__body">
                  <div className="qw-msg__meta">
                    {line.mine ? null : (
                      <>
                        {line.level > 0 ? <em className="qw-lv">Lv.{line.level}</em> : null}
                        <span className="qw-msg__name">{line.name}</span>
                      </>
                    )}
                    <span className="qw-msg__time">{line.time}</span>
                  </div>
                  <div className="qw-bubble">
                    <span className="qw-bubble__text">{spoken(line.text)}</span>
                  </div>
                </div>
              </div>
            );
          })
        )}
      </div>

      <div className="qw-input">
        <div className="qw-input__tools">
          {INPUT_TOOLS.map((tool) => (
            <button key={tool.label} type="button" className="qw-ibtn" aria-label={tool.label} title={tool.reason} disabled>
              <Icon name={tool.icon} />
            </button>
          ))}
        </div>
        <div className="qw-input__box">
          <button
            type="button"
            className="qw-ibtn"
            aria-label={voice.listening ? '停止听' : '语音'}
            aria-pressed={voice.listening}
            title={voice.canListen ? (voice.listening ? '停止听' : '语音输入') : voice.statusReason}
            disabled={!voice.canListen || !active}
            onClick={() => void toggleMic()}
          >
            <Icon name="mic" />
          </button>
          <textarea
            rows={1}
            value={draft}
            placeholder="输入…"
            aria-label="输入…"
            disabled={!active}
            onChange={(event) => setDraft(event.target.value)}
            onKeyDown={(event) => {
              if (event.key !== 'Enter' || event.shiftKey) return;
              event.preventDefault();
              void send();
            }}
          />
          <button
            type="button"
            className="qw-send"
            disabled={!live || !active || sending || !draft.trim()}
            title={live ? '发送（Enter）' : 'chat.send 桥方法还没有上，送不出去'}
            onClick={() => void send()}
          >
            {sending ? '发送中' : '发送'}
            <Icon name="chevron" className="qw-send__caret" />
          </button>
        </div>
        {sendError ? <span className="qw-input__error">{sendError}</span> : null}
        {voice.canListen || !voice.statusReason ? null : <span className="qw-input__error">{voice.statusReason}</span>}
        {voice.note ? <span className="qw-input__error">{voice.note}</span> : null}
      </div>
    </section>
  );

  const infoColumn = (
    <aside className="qw-info">
      <section className="qw-notice">
        <button
          type="button"
          className="qw-notice__head"
          aria-expanded={noticeOpen}
          title="群公告还没有桥方法，这里停在空态"
          onClick={() => setNoticeOpen((value) => !value)}
        >
          <b>群公告</b>
          <Icon name="chevron" className="qw-notice__caret" />
        </button>
        <p className={noticeOpen ? 'qw-notice__text' : 'qw-notice__text qw-notice__text--clamp'}>
          还没有群公告。群公告归 qq.group 桥方法，接上以后这里显示原文，可展开。
        </p>
      </section>

      <section className="qw-members">
        <header className="qw-members__head">
          <b>
            群聊成员 <em>{people.length}</em>
          </b>
          <button
            type="button"
            className="qw-ibtn"
            aria-label="搜索成员"
            aria-pressed={memberSearch}
            onClick={() => setMemberSearch((value) => !value)}
          >
            <Icon name="search" />
          </button>
        </header>
        {memberSearch ? (
          <input
            className="qw-members__search"
            value={memberQuery}
            placeholder="搜索成员"
            aria-label="搜索成员"
            onChange={(event) => setMemberQuery(event.target.value)}
          />
        ) : null}
        <ul className="qw-members__list">
          {members.length === 0 ? (
            <li className="qw-members__empty">还没有好友</li>
          ) : (
            members.map((contact) => {
              const person = roster.find((item) => item.id === contact.id) ?? null;
              return (
                <li key={contact.id}>
                  <button type="button" className="qw-member" onClick={() => pick(contact.id)}>
                    <Avatar person={contact} className="qw-member__av" />
                    <span className="qw-member__name">{contact.name}</span>
                    {/* 群主 / 管理员 这类角色标签归 qq.members；桥里没有，就不给人派角色。 */}
                    {person ? <em className="qw-lv">Lv.{person.level}</em> : null}
                  </button>
                </li>
              );
            })
          )}
        </ul>
      </section>
    </aside>
  );

  return (
    <div className="qw">
      <header className="qw-top">
        <span className="qw-top__brand" aria-hidden="true">
          QV
        </span>
        <span className="qw-top__account">
          <span className="qw-top__av">初</span>
          <span className="qw-top__id">
            <b>温暖如初</b>
            <small>编辑个性签名</small>
          </span>
        </span>
        <span className="qw-top__gap" />
        <button
          type="button"
          className="qw-top__act"
          onClick={() => navigate('/settings')}
          title="视频号链接在设置 → 外部接入里"
        >
          视频号
        </button>
        <button type="button" className="qw-top__act" onClick={() => navigate('/qqforum')}>
          论坛
        </button>
        <button type="button" className="qw-top__act qw-top__act--back" onClick={back} aria-label="返回">
          ‹ 返回
        </button>
      </header>

      <div className="qw__main" data-info={showInfo ? 'on' : 'off'} data-list={showList ? 'on' : 'off'}>
        <nav className="qw-rail" aria-label="QV 工具栏">
          <button type="button" className="qw-rail__me" aria-label="账号" title="本地账号">
            初
            {newCount > 0 ? <i className="qw-rail__badge">{newCount > 99 ? '99+' : newCount}</i> : null}
          </button>

          <div className="qw-rail__stack">
            {railTabs.map((item) => (
              <button
                key={item.id}
                type="button"
                className="qw-rail__tab"
                aria-current={tab === item.id}
                aria-label={item.label}
                title={item.ready ? item.label : `${item.label}这一栏还没有接上数据`}
                disabled={!item.ready}
                onClick={() => {
                  setTab(item.id as 'chat' | 'people');
                  if (!showList) setDrawer(true);
                }}
              >
                <Icon name={item.icon} />
              </button>
            ))}
          </div>

          <div className="qw-rail__foot">
            <button
              type="button"
              className="qw-rail__tab"
              aria-label="菜单"
              aria-expanded={menu}
              onClick={() => setMenu((value) => !value)}
            >
              <Icon name="menu" />
            </button>
            <button type="button" className="qw-rail__tab" aria-label="设置" onClick={() => setSettings(true)}>
              <Icon name="gear" />
            </button>
            <button type="button" className="qw-rail__tab" aria-label="退出" title="退出账号要接登录桥" disabled>
              <Icon name="power" />
            </button>
          </div>

          {menu ? (
            <div className="qw-menu" role="menu" aria-label="QV 菜单">
              <div className="qw-menu__quick">
                {(
                  [
                    { icon: 'star', label: '收藏' },
                    { icon: 'folder', label: '文件' },
                    { icon: 'palette', label: '调色盘' },
                  ] as const
                ).map((item) => (
                  <button key={item.label} type="button" disabled title="这一栏还没有接上数据">
                    <Icon name={item.icon} />
                    <span>{item.label}</span>
                  </button>
                ))}
              </div>
              <div className="qw-menu__list">
                {MENU_ITEMS.map((item) => (
                  <button
                    key={item.label}
                    type="button"
                    role="menuitem"
                    disabled={!item.ready}
                    title={item.ready ? undefined : '这一项还没有桥方法'}
                    onClick={() => {
                      if (!item.ready) return;
                      setSettings(true);
                      setMenu(false);
                    }}
                  >
                    <Icon name={item.icon} />
                    <span>{item.label}</span>
                    {item.label === '帮助' ? <Icon name="chevron" className="qw-menu__more" /> : null}
                  </button>
                ))}
              </div>
            </div>
          ) : null}
        </nav>

        {showList ? listColumn : null}
        {chatColumn}
        {showInfo ? infoColumn : null}
      </div>

      {!showList && drawer ? (
        <>
          <button type="button" className="qw-scrim" aria-label="关闭会话列表" onClick={() => setDrawer(false)} />
          <div className="qw-drawer">{listColumn}</div>
        </>
      ) : null}

      {settings ? <QqSettings onClose={() => setSettings(false)} onTheme={() => navigate('/settings')} /> : null}
    </div>
  );
}
