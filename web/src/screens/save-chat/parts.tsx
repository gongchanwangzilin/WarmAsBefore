import { useEffect, useState } from 'react';
import { useEscape } from '../../lib/keys';
import { useIsWide } from '../../lib/useIsWide';
import './savechat.css';

/**
 * 微信壳 / QQ 壳共用的会话骨架（WeChatPage.xaml 573 行、QqPage.xaml 335 行同构）。
 *
 * 宽屏：左栏（仅微信有 64px 竖栏）+ 300px 会话列表 + 会话区；
 * 窄屏：列表收成抽屉（源码里窄屏就是抽屉 + 压暗层），会话区铺满。
 * 顶栏、气泡、输入条、表情面板、送礼面板的结构与文案都照抄源码，只换皮。
 */

export type ChatContact = {
  id: string;
  name: string;
  last: string;
  time: string;
  unread: boolean;
};

export type ChatMessage = {
  id: string;
  text: string;
  mine: boolean;
};

type Props = {
  skin: 'wechat' | 'qq';
  contacts: ChatContact[];
  /** 当前会话对象（由页面持有：切人时要真的去读那一段历史） */
  activeId: string;
  onSelectContact: (id: string) => void;
  partner: string;
  modeLabel: string;
  messages: ChatMessage[];
  onBack: () => void;
  onForum?: () => void;
  /** 真的把这句话送进聊天引擎；不给就是不能发（按钮停在禁用态） */
  onSend?: (text: string) => Promise<void>;
  /** 会话记录读失败时宿主给的那一句原话 */
  loadHint?: string;
  /** 还有更早的消息（chat.history 的 hasMore） */
  hasMore?: boolean;
  onLoadEarlier?: () => Promise<void>;
};

const TIERS = ['关闭', '档1', '档2', '档3'];

export function ChatShell({
  skin,
  contacts,
  activeId,
  onSelectContact,
  partner,
  modeLabel,
  messages,
  onBack,
  onForum,
  onSend,
  loadHint,
  hasMore,
  onLoadEarlier,
}: Props) {
  const wide = useIsWide(900);
  const isWeChat = skin === 'wechat';
  const [full, setFull] = useState(false);
  const [listOpen, setListOpen] = useState(!isWeChat && !wide);
  const [search, setSearch] = useState('');
  const [input, setInput] = useState('');
  const [stickersOpen, setStickersOpen] = useState(false);
  const [giftOpen, setGiftOpen] = useState(false);
  // Esc 先关礼物，再关表情。
  useEscape(stickersOpen || giftOpen, () => {
    if (giftOpen) setGiftOpen(false);
    else setStickersOpen(false);
  });
  const [giftMode, setGiftMode] = useState<'gift' | 'use'>('gift');
  const [thinkOn, setThinkOn] = useState(false);
  const [tier, setTier] = useState(0);
  const [sending, setSending] = useState(false);
  const [sendHint, setSendHint] = useState('');

  /** 发送：走页面给的 onSend；失败把宿主给的原因写在输入条上面，不往气泡里塞「已发送」。 */
  const submit = async () => {
    const text = input.trim();
    if (!text || !onSend || sending) return;
    setSending(true);
    setSendHint('');
    try {
      await onSend(text);
      setInput('');
    } catch (cause) {
      setSendHint(cause instanceof Error ? cause.message : '这句话没有送出去。');
    } finally {
      setSending(false);
    }
  };

  useEffect(() => {
    if (wide) setListOpen(false);
  }, [wide]);

  const unreadCount = contacts.filter((contact) => contact.unread).length;
  const railLabel = unreadCount > 0 ? String(unreadCount) : '消息';

  const partnerName = contacts.find((contact) => contact.id === activeId)?.name ?? partner;

  const query = search.trim();
  const rows = query
    ? contacts.filter((contact) => contact.name.includes(query) || contact.last.includes(query))
    : contacts;

  const showListColumn = wide && !full;

  const openContact = (id: string) => {
    onSelectContact(id);
    setListOpen(false);
  };

  const listBody = (
    <>
      <div className="sc-listhead">
        <span className="sc-search">
          <input
            value={search}
            placeholder="搜索"
            aria-label="搜索"
            onChange={(event) => setSearch(event.target.value)}
          />
        </span>
        <button type="button" className="sc-plus" aria-label="刷新联系人" disabled>
          ＋
        </button>
      </div>
      <div className="sc-list">
        {rows.length === 0 ? (
          isWeChat ? (
            <div className="sc-empty">
              <span>{query ? `没有会话匹配「${query}」` : '这一档还没有角色'}</span>
              <small>存档里的角色都会出现在这里，随时能给你发消息</small>
            </div>
          ) : (
            <div className="sc-empty">
              <span>还没有好友</span>
              <small>让 AI 用「add_wechat」把剧情里的角色加为好友</small>
            </div>
          )
        ) : (
          rows.map((contact) => (
            <button
              key={contact.id}
              type="button"
              className="sc-contact"
              aria-pressed={contact.id === activeId}
              onClick={() => openContact(contact.id)}
            >
              <span className="sc-avatar">{contact.name.slice(0, 1)}</span>
              <span className="sc-contact__text">
                <span className="sc-contact__name">{contact.name}</span>
                <span className="sc-contact__last">{contact.last}</span>
              </span>
              <span className="sc-contact__side">
                <span className="sc-contact__time">{contact.time}</span>
                {contact.unread ? (
                  isWeChat ? (
                    <i className="sc-dot" aria-label="未读" />
                  ) : (
                    <i className="sc-badge" aria-label="未读" />
                  )
                ) : null}
              </span>
            </button>
          ))
        )}
      </div>
    </>
  );

  const chatCol = showListColumn ? (isWeChat ? 3 : 2) : 1;

  const header = (
    <div className="sc-chead" style={{ gridColumn: chatCol }}>
      {!wide ? (
        <button
          type="button"
          className="sc-iconbtn"
          aria-label={isWeChat ? '会话列表' : '好友'}
          onClick={() => setListOpen(true)}
        >
          {isWeChat ? '列表' : '好友'}
        </button>
      ) : null}
      <button type="button" className="sc-iconbtn" aria-label="返回" onClick={onBack}>
        {'<'}
      </button>
      <span className="sc-avatar">{partnerName.slice(0, 1)}</span>
      <span className="sc-chead__name">
        <span className="sc-chead__title">{partnerName}</span>
        <span className="sc-chead__sub">{modeLabel}</span>
      </span>
      <span className="sc-chead__acts">
        {isWeChat ? (
          <>
            <button type="button" className="sc-pill" onClick={() => setFull((value) => !value)}>
              {full ? '返回' : '全屏'}
            </button>
            {full ? null : <span className="sc-pill">微信</span>}
          </>
        ) : (
          <>
            <span className="sc-pill">QQ</span>
            <button type="button" className="sc-pill" onClick={onForum}>
              论坛
            </button>
          </>
        )}
      </span>
    </div>
  );

  const body = (
    <div className="sc-body" style={{ gridColumn: chatCol }}>
      <div className="sc-msgs">
        {hasMore && onLoadEarlier ? (
          <button type="button" className="sc-pill" style={{ alignSelf: 'center' }} onClick={() => void onLoadEarlier()}>
            更早的消息
          </button>
        ) : null}
        {messages.length === 0 ? (
          isWeChat ? (
            <div className="sc-empty" style={{ margin: 'auto' }}>
              <span>{loadHint ? '会话记录读不出来' : '还没有聊过'}</span>
              <small>{loadHint || '说点什么，她会在下面回你'}</small>
            </div>
          ) : null
        ) : (
          messages.map((message) => (
            <div key={message.id} className={message.mine ? 'sc-msg sc-msg--mine' : 'sc-msg'}>
              <span className={message.mine ? 'sc-bubble sc-bubble--mine' : 'sc-bubble sc-bubble--theirs'}>
                {message.text}
              </span>
            </div>
          ))
        )}
      </div>

      {stickersOpen ? (
        <div className="sc-stickers">
          <div className="sc-stickers__head">
            <span>我的表情包</span>
            <span className="sc-pill">导入表情</span>
          </div>
          <p className="sc-stickers__empty">还没有表情，点「导入表情」添加（图片/动图/视频/任意文件均可）</p>
        </div>
      ) : null}

      {sendHint ? (
        <p
          style={{
            margin: 0,
            padding: '4px 2px 6px',
            fontSize: 'var(--w-size-caption)',
            color: 'var(--w-signal)',
            lineHeight: 1.5,
          }}
        >
          {sendHint}
        </p>
      ) : null}

      <div className="sc-input">
        <div className="sc-think">
          <button
            type="button"
            className="sc-think__toggle"
            onClick={() => {
              setThinkOn((value) => !value);
              setTier((value) => (value > 0 ? value : 1));
            }}
          >
            {thinkOn ? '思考：开' : '思考：关'}
          </button>
          {thinkOn ? (
            <div className="sc-tiers">
              {TIERS.map((label, index) => (
                <button
                  key={label}
                  type="button"
                  className="sc-tier"
                  aria-pressed={tier === index}
                  onClick={() => setTier(index)}
                >
                  {label}
                </button>
              ))}
            </div>
          ) : null}
        </div>

        <div className={isWeChat ? 'sc-inputrow' : 'sc-inputrow sc-inputrow--qq'}>
          <button type="button" className="sc-ibtn" disabled>
            语音
          </button>
          {isWeChat ? (
            <button type="button" className="sc-ibtn" onClick={() => setGiftOpen(true)}>
              礼物
            </button>
          ) : null}
          <textarea
            className="sc-editor"
            rows={1}
            value={input}
            placeholder="输入…"
            aria-label="输入…"
            onChange={(event) => setInput(event.target.value)}
          />
          <button type="button" className="sc-ibtn" onClick={() => setStickersOpen((value) => !value)}>
            表情
          </button>
          <button type="button" className="sc-ibtn" disabled>
            文件
          </button>
          <button
            type="button"
            className="sc-send"
            onClick={() => void submit()}
            disabled={!onSend || sending}
            aria-busy={sending || undefined}
          >
            {sending ? '发送中…' : '发送'}
          </button>
        </div>
      </div>
    </div>
  );

  const columns = showListColumn ? (isWeChat ? '64px 300px minmax(0, 1fr)' : '300px minmax(0, 1fr)') : 'minmax(0, 1fr)';

  return (
    <div className="sc-shell">
      <div className="sc-chat" style={{ gridTemplateColumns: columns }}>
        {isWeChat && showListColumn ? (
          <div className="sc-rail">
            <span className="sc-rail__logo">初</span>
            {/* 竖栏四档（好友/收藏/表情/设置）在源码里各是一页列表，Web 端没有数据源，
                只留「消息」这一档为当前项，其余灰着，不做点了没动静的钮。 */}
            <span className="sc-rail__item sc-rail__item--on">
              <span style={{ fontSize: 'var(--w-size-body)' }}>{railLabel}</span>
            </span>
            <button type="button" className="sc-rail__item" disabled>
              <span>好友</span>
            </button>
            <button type="button" className="sc-rail__item" disabled>
              <span>收藏</span>
            </button>
            <button type="button" className="sc-rail__item" disabled>
              <span>表情</span>
            </button>
            <button type="button" className="sc-rail__item" disabled>
              <span>设置</span>
            </button>
          </div>
        ) : null}

        {showListColumn ? (
          <div className="sc-listwrap" style={{ gridColumn: isWeChat ? 2 : 1, gridRow: '1 / span 2' }}>
            {isWeChat ? null : (
              <div className="sc-listwrap__head">
                <button type="button" className="sc-iconbtn" aria-label="返回" onClick={onBack}>
                  {'<'}
                </button>
                <span className="sc-forum__title">QQ</span>
                <span className="sc-pill">视频号</span>
              </div>
            )}
            {listBody}
          </div>
        ) : null}

        {header}
        {body}
      </div>

      {listOpen ? (
        <>
          <button type="button" className="sc-scrim" aria-label="关闭列表" onClick={() => setListOpen(false)} />
          <div className="sc-drawer">
            {isWeChat ? null : (
              <div className="sc-listwrap__head">
                <button type="button" className="sc-iconbtn" aria-label="返回" onClick={() => setListOpen(false)}>
                  {'<'}
                </button>
                <span className="sc-forum__title">QQ</span>
                <span className="sc-pill">视频号</span>
              </div>
            )}
            {listBody}
          </div>
        </>
      ) : null}

      {isWeChat && giftOpen ? (
        <div className="sc-gift">
          <div className="sc-gift__card">
            <span className="sc-gift__title">礼物箱 </span>
            <span className="sc-gift__sub">点一下送出心意，对方会收下并回应</span>
            <div className="sc-gift__modes">
              <button
                type="button"
                className="sc-gift__mode"
                aria-pressed={giftMode === 'gift'}
                onClick={() => setGiftMode('gift')}
              >
                {' '}
                送礼
              </button>
              <button
                type="button"
                className="sc-gift__mode"
                aria-pressed={giftMode === 'use'}
                onClick={() => setGiftMode('use')}
              >
                使用
              </button>
            </div>
            <div className="sc-gift__empty">
              <span>还没有已购商品，先去商店逛逛吧～</span>
            </div>
            <button type="button" className="sc-gift__close" onClick={() => setGiftOpen(false)}>
              关闭
            </button>
          </div>
        </div>
      ) : null}
    </div>
  );
}
