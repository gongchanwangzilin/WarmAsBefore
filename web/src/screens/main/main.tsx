import { useEffect, useState, type MouseEvent } from 'react';
import type { CharacterRow, ChatRow, SaveRow } from '../../bridge/protocol';
import { openInWindow } from '../../lib/windows';
import { Glass } from '../../components/Glass';
import { GlassSwitch } from '../../components/GlassSwitch';
import type { ScreenProps } from '../types';
import './main.css';

/**
 * 主界面：Views/Main/MainGamePage.xaml（877 行）。
 *
 * 默认版式是「小组件模式」（UserSettings.WidgetModeEnabled 出厂为 true）：
 * 顶栏一排小组件 + 右列操作键、左栏一列信息组件、中间舞台（立绘 / 场景 / 上下场景键 /
 * 右上小地图 / 底部 Galgame 面板），切到聊天模式后右栏换成对话与快捷动作。
 * 覆盖层（菜单、组件配置、礼物箱、未标定闸门）按源码结构一并搬进本页。
 *
 * 后端目前没有读游戏状态的 method，所以这里的读数是 MainGameViewModel 的出厂值
 * （好感 300、信任 30、精力 100、余额 1000），角色名与地点取本仓库 mock 存档的占位值。
 */

type Slot = 'top' | 'left';

type WidgetRow = {
  id: string;
  title: string;
  desc: string;
  slot: Slot;
  visible: boolean;
};

/** WidgetRegistry.Widgets：顺序即默认顺序，默认可见性与默认栏位照抄。 */
const WIDGETS: WidgetRow[] = [
  { id: 'date', title: '日期', desc: '今天几号、星期几。', slot: 'top', visible: true },
  { id: 'location', title: '位置', desc: '你和 TA 现在所在的地点。', slot: 'left', visible: true },
  { id: 'affection', title: '好感', desc: '好感度与当前等级。', slot: 'left', visible: true },
  { id: 'trust', title: '信任', desc: '信任度读数。', slot: 'left', visible: true },
  { id: 'energy', title: '精力', desc: 'TA 今天还剩多少力气。', slot: 'left', visible: true },
  { id: 'balance', title: '余额', desc: '钱包里还剩多少。', slot: 'left', visible: true },
  { id: 'brain', title: '心事', desc: '大脑预判读出的一句「TA 现在在想什么」。', slot: 'left', visible: true },
  { id: 'mood', title: '情绪', desc: '此刻表情对应的情绪。', slot: 'left', visible: false },
];

/** WidgetRegistry.TopButtons：顶栏按键，空配置 = 全部。 */
const TOP_BUTTONS: { id: string; label: string }[] = [
  { id: 'save', label: '存档' },
  { id: 'menu', label: '菜单' },
  { id: 'phone', label: '手机' },
  { id: 'map', label: '地图' },
  { id: 'ai', label: 'AI' },
  { id: 'hide', label: '收起' },
];

/** 后端没有回话时的兜底（源码出厂值）；有真数据时下面那个 hook 会把它换掉。 */
const FALLBACK_NAME = 'TA';
const FALLBACK_LOCATION = '还没有进入存档';

const WEEK_CN = ['日', '一', '二', '三', '四', '五', '六'];

function dateLabel(now: Date): string {
  return `${now.getMonth() + 1}月${now.getDate()}日 周${WEEK_CN[now.getDay()]}`;
}

function timeLabel(now: Date): string {
  return `${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`;
}

type Live = { who: string; where: string; affection: number; trust: number; energy: number; mood: string; sprite: string; spriteCount: number };

const AFFECTION_LEVELS = ['初识', '熟悉', '亲近', '知心', '相守'];
const levelOf = (points: number) => AFFECTION_LEVELS[Math.min(AFFECTION_LEVELS.length - 1, Math.floor(points / 100))];

/**
 * 读真的：存档列表给「在哪儿」，角色表给「谁、好感、信任、精力、情绪」。
 * 数据变了（宿主推 data.changed）就重读 —— 这就是主窗与手机窗之间的联动。
 */
function useLive(bridge: ScreenProps['bridge']): Live {
  const [live, setLive] = useState<Live>({
    who: FALLBACK_NAME,
    where: FALLBACK_LOCATION,
    affection: 300,
    trust: 30,
    energy: 100,
    mood: '平静',
    sprite: '',
    spriteCount: 0,
  });

  useEffect(() => {
    let dropped = false;
    const read = async () => {
      const [saves, people] = await Promise.allSettled([
        bridge.request('saves.list', {}),
        bridge.request('characters.list', {}),
      ]);
      if (dropped) return;
      const slot: SaveRow | null = saves.status === 'fulfilled' ? (saves.value.slots[0] ?? null) : null;
      const profile: CharacterRow | null =
        people.status === 'fulfilled'
          ? (people.value.characters.find((c) => c.name === slot?.character) ?? people.value.characters[0] ?? null)
          : null;
      if (!slot && !profile) return;
      setLive({
        who: profile?.name || slot?.character || FALLBACK_NAME,
        where: slot?.scene || FALLBACK_LOCATION,
        affection: profile?.affection ?? 300,
        trust: profile?.trust ?? 30,
        energy: profile?.energy ?? 100,
        mood: profile?.mood || '平静',
        sprite: profile?.currentSpriteUrl ?? '',
        spriteCount: profile?.spriteCount ?? 0,
      });
    };
    void read();
    const off = bridge.onEvent('data.changed', () => void read());
    return () => {
      dropped = true;
      off();
    };
  }, [bridge]);

  return live;
}

/** WidgetItemVm.Value 的取值口径（RefreshWidgetValues）。 */
function widgetValue(id: string, day: string, clock: string, live: Live): string {
  switch (id) {
    case 'clock':
      return clock;
    case 'date':
      return day;
    case 'weather':
      return '晴朗';
    case 'location':
      return live.where;
    case 'affection':
      return `Lv.${Math.min(5, Math.floor(live.affection / 100) + 1)} · ${levelOf(live.affection)}`;
    case 'trust':
      return `${live.trust}/100`;
    case 'energy':
      return `${live.energy}%`;
    case 'balance':
      return '¤ 1000';
    case 'mood':
      return live.mood;
    default:
      return '';
  }
}

type ChatMsg = { id: string; mine: boolean; text: string };

export function Main({ bridge, navigate }: ScreenProps) {
  const live = useLive(bridge);
  const openPhone = () => void openInWindow(bridge, '/phone', '手机', { width: 420, height: 860 }, () => navigate('/phone'));
  const now = new Date();
  const day = dateLabel(now);
  const clock = timeLabel(now);

  const [widgets, setWidgets] = useState<WidgetRow[]>(WIDGETS);
  const [buttons, setButtons] = useState<Record<string, boolean>>(() =>
    Object.fromEntries(TOP_BUTTONS.map((b) => [b.id, true])),
  );
  const [chatMode, setChatMode] = useState(false);
  const [hiddenPanels, setHiddenPanels] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);
  const [widgetPanel, setWidgetPanel] = useState(false);
  const [giftOpen, setGiftOpen] = useState(false);
  const [giftMode, setGiftMode] = useState(true);
  const [minimapOpen, setMinimapOpen] = useState(true);
  const [blackout, setBlackout] = useState(false);
  // 未标定闸门与「后端模式」由标定服务 / 存档模式决定；Web 端拿不到这两份状态，
  // 默认都是源码里的默认值（不拦、不盖）。
  const [calibrationBlocked] = useState(false);
  const [backendMode] = useState(false);
  const [input, setInput] = useState('');
  // 对话列表 = chat.history 读回来的存档会话记录；送出去的一句由 chat.send 回给这里。
  const [messages, setMessages] = useState<ChatMsg[]>([]);
  const [sending, setSending] = useState(false);
  const [sendHint, setSendHint] = useState('');
  // 自定义状态条：按场景 / 楼层由流程规则长出来，这一局没有规则时整排不出现。
  const stateButtons: string[] = [];

  const modeLabel = chatMode ? '切回 Galgame' : '切到聊天模式';
  const topWidgets = widgets.filter((w) => w.slot === 'top' && w.visible);
  const leftWidgets = widgets.filter((w) => w.slot === 'left' && w.visible && w.id !== 'brain');
  const last = messages.length > 0 ? messages[messages.length - 1] : null;

  // 主界面会话：读存档里的会话记录（最近 80 条一次给完，主界面的记录本来就只留这么多）。
  useEffect(() => {
    let dropped = false;
    void bridge
      .request('chat.history', { shell: 'main', limit: 80 })
      .then(({ messages: rows }) => {
        if (dropped) return;
        setMessages(rows.map((row: ChatRow) => ({ id: row.id, mine: row.mine, text: row.text })));
      })
      .catch((cause: unknown) => {
        if (dropped) return;
        setSendHint(cause instanceof Error ? cause.message : '会话记录没有应答。');
      });
    return () => {
      dropped = true;
    };
  }, [bridge]);

  // 送真的：进聊天引擎跑一轮，回复与自己的那句一起落回列表；失败就把原因写在输入条下面，不假装发出去了。
  const send = async () => {
    const text = input.trim();
    if (!text || sending) return;
    setSending(true);
    setSendHint('');
    try {
      const { user, reply } = await bridge.request('chat.send', { shell: 'main', text });
      setMessages((prev) => [
        ...prev,
        { id: user.id, mine: true, text: user.text },
        { id: reply.id, mine: false, text: reply.text },
      ]);
      setInput('');
    } catch (cause) {
      setSendHint(cause instanceof Error ? cause.message : '这句话没有送出去。');
    } finally {
      setSending(false);
    }
  };

  const toggleButton = (id: string) => setButtons((prev) => ({ ...prev, [id]: !prev[id] }));

  /** 面板里上下移：在同一栏内与相邻一颗交换。 */
  const move = (id: string, dir: -1 | 1) => {
    setWidgets((prev) => {
      const row = prev.find((w) => w.id === id);
      if (!row) return prev;
      const same = prev.filter((w) => w.slot === row.slot);
      const at = same.indexOf(row);
      const target = same[at + dir];
      if (!target) return prev;
      const next = [...prev];
      const from = next.indexOf(row);
      const to = next.indexOf(target);
      next[from] = target;
      next[to] = row;
      return next;
    });
  };

  const flipSlot = (id: string) => {
    setWidgets((prev) =>
      prev.map((w) => (w.id === id ? { ...w, slot: w.slot === 'top' ? 'left' : 'top' } : w)),
    );
  };

  const setVisible = (id: string, on: boolean) => {
    setWidgets((prev) => prev.map((w) => (w.id === id ? { ...w, visible: on } : w)));
  };

  return (
    <div className="mg">
      {hiddenPanels ? null : (
        <Glass as="header" tone="bar" radius={0} className="mg__top">
          <div className="mg__top-widgets">
            {topWidgets.map((w) => (
              <div className="mg__w" key={w.id}>
                <span className="mg__w-title">{w.title}</span>
                <span className={`mg__w-value${w.id === 'clock' ? ' mg__w-value--clock' : ''}`}>
                  {widgetValue(w.id, day, clock, live)}
                </span>
              </div>
            ))}
          </div>
          <div className="mg__top-actions">
            {TOP_BUTTONS.filter((b) => buttons[b.id]).map((b) => {
              // 存档（QuickSave）与 AI（PickAnswerAi）在后端都没有通路：
              // 灰着比「点得动但什么都不发生」诚实。
              const dead = b.id === 'save' || b.id === 'ai';
              return (
                <button
                  type="button"
                  key={b.id}
                  className="m-btn"
                  disabled={dead}
                  onClick={() => {
                    if (b.id === 'menu') setMenuOpen(true);
                    if (b.id === 'phone') openPhone();
                    if (b.id === 'map') navigate('/map');
                    if (b.id === 'hide') setHiddenPanels(true);
                  }}
                  title={b.id === 'hide' ? '隐藏信息栏与顶部（Esc）' : b.label}
                >
                  {b.label}
                </button>
              );
            })}
            <button type="button" className="m-btn" onClick={() => navigate('/superMonitor')} title="查看当前存档的超级监测">
              监测
            </button>
            <button type="button" className="m-btn m-btn--accent" onClick={() => setWidgetPanel(true)} title="选择顶栏与左栏显示什么">
              组件
            </button>
          </div>
        </Glass>
      )}

      <div className="mg__body" data-chat={chatMode ? '1' : undefined} data-left={hiddenPanels ? '0' : '1'}>
        {hiddenPanels ? null : (
          <Glass as="aside" tone="bar" radius={0} className="mg__left">
            <span className="mg__left-title">信息</span>
            <div className="mg__left-list">
              {leftWidgets.length === 0 ? (
                <p className="mg__left-empty">
                  还没有显示的信息。打开「组件」，把想看的留在这一栏。
                </p>
              ) : (
                leftWidgets.map((w) => (
                  <div className="mg__w" key={w.id}>
                    <span className="mg__w-title">{w.title}</span>
                    <span className="mg__w-value">{widgetValue(w.id, day, clock, live)}</span>
                  </div>
                ))
              )}
            </div>
            <button type="button" className="mg__footer-btn" onClick={() => setWidgetPanel(true)}>
              组件
            </button>
          </Glass>
        )}

        <section className="mg__stage">
          <span className="mg__stage-dim" aria-hidden="true" />

          <button type="button" className="mg__nav mg__nav--prev" disabled aria-label="上一个地点">
            ◀
          </button>
          <button type="button" className="mg__nav mg__nav--next" disabled aria-label="下一个地点">
            ▶
          </button>

          <div className="mg__corner">
            {minimapOpen ? (
              <Glass as="div" tone="card" radius={16} className="mg__minimap">
                <div className="mg__mm-head">
                  <div>
                    <div className="mg__mm-title">{live.where}</div>
                    <div className="mg__mm-sub">附近</div>
                  </div>
                  <button type="button" className="m-btn m-btn--sm m-btn--ghost" onClick={() => setMinimapOpen(false)}>
                    收起
                  </button>
                </div>
                <div className="mg__mm-canvas" aria-hidden="true" />
                <div className="mg__mm-list">
                  <span className="mg__mm-empty">附近没别的地方</span>
                </div>
                <button type="button" className="m-btn m-btn--sm m-btn--block" onClick={() => navigate('/map')}>
                  打开大地图
                </button>
              </Glass>
            ) : (
              <Glass
                as="button"
                type="button"
                tone="card"
                radius={14}
                className="mg__minimap mg__mm-chip"
                style={{ width: 60, height: 60 }}
                onClick={() => setMinimapOpen(true)}
                aria-label="展开小地图"
              >
                <span className="mg__mm-dot" aria-hidden="true" />
              </Glass>
            )}
          </div>

          {live.sprite ? (
            <img className="mg__sprite" src={live.sprite} alt={live.who} draggable={false} />
          ) : (
            <div className="mg__nosprite">
              <span className="mg__nosprite-mark">{live.who.slice(0, 1) || '·'}</span>
              <span className="mg__nosprite-name">{live.who || '还没有进入存档'}</span>
              <span className="mg__nosprite-note">
                {live.who ? `这个角色还没有立绘（共 ${live.spriteCount} 张）` : '从标题页读一份存档，这里就是你们的那一幕'}
              </span>
            </div>
          )}

          {chatMode ? null : (
            <Glass as="section" tone="bar" radius={20} className="mg__gal">
              <div className="mg__gal-title">
                <span className="mg__gal-chip">{live.who}</span>
                <button type="button" className="m-btn m-btn--sm" onClick={() => setChatMode(true)}>
                  {modeLabel}
                </button>
                <span className="mg__gal-speaker">{last ? (last.mine ? '你' : live.who) : ''}</span>
              </div>
              <div className={`mg__gal-history${last ? '' : ' mg__gal-history--empty'}`}>
                {last ? last.text : '这一局还没有说过话。'}
              </div>
              {sendHint ? <p className="mg__send-hint">{sendHint}</p> : null}
              <div className="mg__input">
                <input
                  value={input}
                  placeholder="输入你想说的话..."
                  aria-label="输入你想说的话"
                  onChange={(event) => setInput(event.target.value)}
                  onKeyDown={(event) => {
                    if (event.key === 'Enter') void send();
                  }}
                />
                <button type="button" className="m-ibtn" disabled>
                  文件
                </button>
                <button type="button" className="m-ibtn" onClick={() => setGiftOpen(true)}>
                  礼物
                </button>
                <button type="button" className="m-ibtn" disabled>
                  语音
                </button>
                <button type="button" className="mg__send" onClick={() => void send()} disabled={sending}>
                  发送
                </button>
              </div>
            </Glass>
          )}

          {blackout ? (
            <div className="mg__blackout">
              <div className="mg__blackout-inner">
                <p>（黑屏）</p>
                <button type="button" className="m-btn" onClick={() => setBlackout(false)}>
                  退出黑屏
                </button>
              </div>
            </div>
          ) : null}

          {hiddenPanels ? (
            <button type="button" className="m-btn m-btn--sm mg__unhide" onClick={() => setHiddenPanels(false)} title="恢复信息栏与顶部（Esc）">
              展开界面
            </button>
          ) : null}
        </section>

        {chatMode ? (
          <Glass as="aside" tone="card" radius={0} className="mg__chat">
            <div className="mg__chat-head">
              {stateButtons.length > 0 ? (
                <div className="mg__state-row">
                  {stateButtons.map((label) => (
                    <button type="button" className="m-btn m-btn--sm" key={label}>
                      {label}
                    </button>
                  ))}
                </div>
              ) : null}
              <div className="mg__quick">
                <button type="button" className="m-btn" disabled>
                  摸摸头
                </button>
                <button type="button" className="m-btn" disabled>
                  抱抱
                </button>
                <button type="button" className="m-btn" disabled>
                  亲吻
                </button>
                <button type="button" className="m-btn m-btn--mode" onClick={() => setChatMode(false)}>
                  {chatMode ? '切回 Galgame' : '切到聊天模式'}
                </button>
              </div>
            </div>
            <div className="mg__chat-list">
              {messages.length === 0 ? (
                <p className="mg__chat-empty">还没有对话。</p>
              ) : (
                messages.map((m) => (
                  <div className={`mg__bubble mg__bubble--${m.mine ? 'mine' : 'theirs'}`} key={m.id}>
                    {m.text}
                  </div>
                ))
              )}
            </div>
            <div className="mg__input">
              <input
                value={input}
                placeholder="输入..."
                aria-label="输入消息"
                onChange={(event) => setInput(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') void send();
                }}
              />
              <button type="button" className="m-ibtn" disabled>
                文件
              </button>
              <button type="button" className="m-ibtn" disabled>
                表情
              </button>
              <button type="button" className="m-ibtn" onClick={() => setGiftOpen(true)}>
                礼物
              </button>
              <button type="button" className="m-ibtn" disabled>
                语音
              </button>
              <button type="button" className="mg__send" onClick={() => void send()} disabled={sending}>
                发送
              </button>
            </div>
            {sendHint ? <p className="mg__send-hint">{sendHint}</p> : null}
          </Glass>
        ) : null}
      </div>

      {menuOpen ? (
        <div className="mg__overlay" onClick={() => setMenuOpen(false)}>
          <Glass as="div" tone="card" radius={16} className="mg__dialog mg__dialog--menu" onClick={(event: MouseEvent) => event.stopPropagation()}>
            <h2 className="mg__menu-title">菜单</h2>
            <button type="button" className="m-btn m-btn--block" onClick={() => navigate('/settings')}>
              设置
            </button>
            <button type="button" className="m-btn m-btn--block" onClick={openPhone}>
              手机
            </button>
            <button type="button" className="m-btn m-btn--block" disabled>
              本局设定
            </button>
            <button type="button" className="m-btn m-btn--block" onClick={() => navigate('/save')}>
              存档管理
            </button>
            <button type="button" className="m-btn m-btn--block" onClick={() => navigate('/pet')}>
              桌宠模式
            </button>
            <button type="button" className="m-btn m-btn--block m-btn--danger" onClick={() => navigate('/')}>
              回标题
            </button>
            <button type="button" className="m-btn m-btn--block m-btn--ghost" onClick={() => setMenuOpen(false)}>
              关闭
            </button>
          </Glass>
        </div>
      ) : null}

      {widgetPanel ? (
        <div className="mg__overlay" onClick={() => setWidgetPanel(false)}>
          <Glass
            as="div"
            tone="card"
            radius={18}
            className="mg__dialog"
            onClick={(event: MouseEvent) => event.stopPropagation()}
          >
            <div className="mg__dlg-head">
              <div>
                <h2 className="mg__dlg-title">组件</h2>
                <p className="mg__dlg-note">想看的留在栏上，暂时不看的收起来；改动只留在这个页面，关掉窗口就回到默认。</p>
              </div>
              <button type="button" className="m-btn m-btn--sm" onClick={() => setWidgetPanel(false)}>
                关闭
              </button>
            </div>

            <span className="mg__hint">顶栏按钮</span>
            <div className="mg__chips">
              {TOP_BUTTONS.map((b) => (
                <button
                  type="button"
                  key={b.id}
                  className="m-btn m-btn--sm"
                  aria-pressed={buttons[b.id]}
                  onClick={() => toggleButton(b.id)}
                >
                  {b.label}
                </button>
              ))}
            </div>

            <div className="mg__wcols">
              {(['top', 'left'] as Slot[]).map((slot) => (
                <div className="mg__wcol" key={slot}>
                  <span className="mg__wcol-title">{slot === 'top' ? '顶栏' : '左栏'}</span>
                  <div className="mg__wscroll">
                    {widgets
                      .filter((w) => w.slot === slot)
                      .map((w) => (
                        <div className="mg__wrow" key={w.id}>
                          <label>
                            <span className="mg__wrow-title">{w.title}</span>
                            <GlassSwitch
                              label={w.title}
                              checked={w.visible}
                              onChange={(on) => setVisible(w.id, on)}
                            />
                          </label>
                          <span className="mg__wrow-desc">{w.desc}</span>
                          <div className="mg__wrow-acts">
                            <button type="button" className="m-btn m-btn--square" onClick={() => move(w.id, -1)} title="上移">
                              ↑
                            </button>
                            <button type="button" className="m-btn m-btn--square" onClick={() => move(w.id, 1)} title="下移">
                              ↓
                            </button>
                            <button type="button" className="m-btn m-btn--xs" onClick={() => flipSlot(w.id)}>
                              {w.slot === 'top' ? '移到左栏' : '移到顶栏'}
                            </button>
                          </div>
                        </div>
                      ))}
                  </div>
                </div>
              ))}
            </div>
            <span className="mg__hint">窄屏顶栏只排前三个顶栏组件，其余仍在面板里随时调。</span>
          </Glass>
        </div>
      ) : null}

      {giftOpen ? (
        <div className="mg__overlay" onClick={() => setGiftOpen(false)}>
          <Glass
            as="div"
            tone="card"
            radius={18}
            className="mg__dialog mg__dialog--gift"
            onClick={(event: MouseEvent) => event.stopPropagation()}
          >
            <h2 className="mg__dlg-title" style={{ textAlign: 'center' }}>
              礼物箱{' '}
            </h2>
            <p className="mg__dlg-note" style={{ textAlign: 'center' }}>
              点一下送出心意，TA 会收下并回应
            </p>
            <div className="mg__gift-tabs">
              <button
                type="button"
                className={`m-btn${giftMode ? ' m-btn--accent' : ''}`}
                onClick={() => setGiftMode(true)}
              >
                {' '}
                送礼
              </button>
              <button
                type="button"
                className={`m-btn${giftMode ? '' : ' m-btn--accent'}`}
                onClick={() => setGiftMode(false)}
              >
                使用
              </button>
            </div>
            <div className="mg__gift-body">
              <span>还没有已购商品，先去商店逛逛吧～</span>
            </div>
            <button type="button" className="m-btn m-btn--block m-btn--ghost" onClick={() => setGiftOpen(false)}>
              关闭
            </button>
          </Glass>
        </div>
      ) : null}

      {calibrationBlocked ? (
        <div className="mg__overlay">
          <Glass as="div" tone="card" radius={18} className="mg__dialog mg__dialog--menu" style={{ width: 'min(520px, 100%)' }}>
            <h2 className="mg__calib-title">这张背景还没有标定</h2>
            <p className="mg__calib-body">
              标定确定两件事：角色能站在哪里（落脚点），以及近处远处该怎么缩放（缩放标定）。
            </p>
            <button type="button" className="m-btn m-btn--block m-btn--accent">
              现在去标定
            </button>
            <button type="button" className="m-btn m-btn--block">
              移除这张背景（改用纯色）
            </button>
            <button type="button" className="m-btn m-btn--block m-btn--ghost">
              暂时用居中显示继续
            </button>
          </Glass>
        </div>
      ) : null}

      {backendMode ? (
        <div className="mg__backend">
          <div className="mg__backend-inner">
            <h2>我的世界主战场</h2>
            <p>
              Galgame 界面已关闭。WAB 现在只作为我的世界的 AI 与通讯后端运行；你在 WAB 或假微信里说的话、下的命令，游戏里的女仆依然会响应。
            </p>
            <button type="button" className="m-btn" onClick={openPhone}>
              打开手机
            </button>
          </div>
        </div>
      ) : null}
    </div>
  );
}

