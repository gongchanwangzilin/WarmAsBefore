import { useEffect, useRef, useState, type CSSProperties, type MouseEvent as ReactMouseEvent, type PointerEvent } from 'react';
import type { ScreenProps } from '../types';
import { openInMain } from '../../lib/windows';
import { unreadLabel, useUnread } from '../../lib/unread';
import './main.css';

/**
 * 手机：Views/Main/PhonePage.xaml + PhoneViewModel。
 *
 * 一台真机，不是「压窄的主页面」：状态栏（真时间 / 信号 / WiFi / 电量）→ 桌面
 * （分区标题 + 方格应用 + 页码点）→ Dock → 手势条。没有读数、没有立绘舞台、
 * 没有聊天输入条 —— 那些只在主窗里。
 *
 * 手机里没有回退键：桌面就是这一页的底，窗关不关由窗标题栏决定。
 * 格子点开的都是整页（QV、角色库、地图…）：这一页在主窗里时走页内导航，
 * 在手机那扇独立窗里时交给主窗（nav.openMain），手机窗停在桌面 ——
 * 翻页与 Dock 都是本窗自己的状态。
 *
 * 每块图标的字都照抄 XAML（底下那行小字是 .xaml 里各自的 Label Text）。
 * 数据只有 PhoneViewModel 的本地档：时间、WeatherText、未读数。
 */

type Tone = 'warm' | 'sand' | 'cool' | 'deep' | 'rose' | 'jade';

type Tile = {
  key: string;
  /** 图标方块里的字（XAML 的 Label Text） */
  glyph: string;
  /** 方块底下的小字 */
  sub: string;
  tone: Tone;
  to: string;
  /** 格子尺寸：Dock 比桌面小一圈 */
  dock?: boolean;
};

const PAGE_ONE_HOME: Tile[] = [
  // 微信与 QQ 合成一个 QV：桌面上只留这一格，点进去是那一个应用。
  { key: 'qv', glyph: 'QV', sub: '消息 · 好友', tone: 'warm', to: '/qv' },
  { key: 'roster', glyph: '角色库', sub: '人设 · 改写', tone: 'sand', to: '/roster' },
  { key: 'map', glyph: '地图', sub: '去处 · 位置', tone: 'sand', to: '/map' },
  { key: 'outfit', glyph: '换装', sub: '立绘 · 服饰', tone: 'rose', to: '/outfit' },
  { key: 'gallery', glyph: '回忆录', sub: '相册 · 日记', tone: 'deep', to: '/gallery' },
];

const PAGE_ONE_SETTING: Tile[] = [
  { key: 'materials', glyph: '素材库', sub: '背景 · 音乐', tone: 'jade', to: '/materials' },
  { key: 'workshop', glyph: '工坊', sub: '场景 · 模式', tone: 'jade', to: '/scenelibrary' },
  { key: 'worldbook', glyph: '世界书', sub: '设定 · 条目', tone: 'cool', to: '/worldbook' },
  // 设置原先挂在下侧栏那颗「≡」上；下侧栏撤了，它回到桌面上当一格应用。
  { key: 'settings', glyph: '设置', sub: '外观 · 模型', tone: 'cool', to: '/settings' },
];

const PAGE_TWO_PLAY: Tile[] = [
  { key: 'forcecast', glyph: '点名', sub: '强制入戏', tone: 'rose', to: '/forcecast' },
  { key: 'shop', glyph: '商店', sub: '礼物 · 道具', tone: 'warm', to: '/shop' },
];

const PAGE_TWO_GAMES: Tile[] = [
  { key: 'gobang', glyph: '五子棋', sub: '五连', tone: 'sand', to: '/game?kind=gobang' },
  { key: 'animalchess', glyph: '斗兽棋', sub: '兽棋', tone: 'sand', to: '/game?kind=animalchess' },
  { key: 'ludo', glyph: '飞行棋', sub: '四人', tone: 'cool', to: '/game?kind=ludo' },
  { key: 'chess', glyph: '国际象棋', sub: '64 格', tone: 'deep', to: '/game?kind=chess' },
  { key: 'chinesechess', glyph: '中国象棋', sub: '河界', tone: 'rose', to: '/game?kind=chinesechess' },
  { key: 'snake', glyph: '贪吃蛇', sub: '越吃越长', tone: 'jade', to: '/game?kind=snake' },
  { key: 'go', glyph: '围棋', sub: '19 路', tone: 'deep', to: '/game?kind=go' },
];

const DOCK: Tile[] = [
  { key: 'dock-qv', glyph: 'QV', sub: '', tone: 'warm', to: '/qv', dock: true },
  { key: 'dock-roster', glyph: '角色库', sub: '', tone: 'sand', to: '/roster', dock: true },
  { key: 'dock-gallery', glyph: '回忆录', sub: '', tone: 'deep', to: '/gallery', dock: true },
  { key: 'dock-materials', glyph: '素材库', sub: '', tone: 'jade', to: '/materials', dock: true },
];

const pad = (value: number) => String(value).padStart(2, '0');

/** 桌面两页。手势阈值都摆在这里，不散在 JSX 里。 */
const PAGES = 2;
/** 拖过页宽的这个比例就算翻页（速度不够时看距离）。 */
const SWIPE_FRACTION = 0.22;
/** 甩出去的判定：px/ms。慢拖到一半松手会弹回原位，快甩才翻。 */
const FLING_SPEED = 0.45;
/** 边缘回弹：拉出边界后的位移只按这个比例跟手，最多这么远。 */
const EDGE_GIVE = 0.35;
const EDGE_MAX = 56;
/** 松手走完一整页 240ms，原地弹回 180ms。 */
const SLIDE_MS = 240;
const SETTLE_BACK_MS = 180;

type DragState = {
  id: number;
  x0: number;
  y0: number;
  lastX: number;
  lastAt: number;
  /** 平滑后的横向速度，px/ms */
  vx: number;
  /** null = 还没判方向；判成 'y' 就整条交给页内滚动，这一轮不再管 */
  axis: 'x' | 'y' | null;
  /** 一块的宽度 = 一页的位移量 */
  width: number;
  dx: number;
};

/** 边缘阻尼：第一页再往右、最后一页再往左，只给一小段回弹，像真的到头了。 */
function give(dx: number, atStart: boolean, atEnd: boolean): number {
  if (!((dx > 0 && atStart) || (dx < 0 && atEnd))) return dx;
  return Math.sign(dx) * Math.min(EDGE_MAX, Math.abs(dx) * EDGE_GIVE);
}

export function Phone({ bridge, navigate }: ScreenProps) {
  /** QV 那一格与 Dock 上的 QV 共用同一个未读数：她发来一条，桌面上就该看得见。 */
  const unread = useUnread(bridge, true);

  /** 格子交不出去时的一句话：主窗不在、或还没加载完，就在这里说明「已经退回页内导航」。 */
  const [note, setNote] = useState('');

  const [page, setPage] = useState(0);
  /** 手指的实时位移（px）。松手时归零，剩下的那段由 CSS 过渡走完。 */
  const [offset, setOffset] = useState(0);
  const [dragging, setDragging] = useState(false);
  /** 松手后走完这段用多久：翻页 240ms，原地弹回 180ms（距离不一样，时间也不该一样）。 */
  const [settle, setSettle] = useState(SLIDE_MS);
  const drag = useRef<DragState | null>(null);
  /** 这次松手别当成点击：横滑过 8px 之后，格子不该跟着开。 */
  const blockClick = useRef(false);
  const [now, setNow] = useState(() => new Date());

  useEffect(() => {
    const timer = window.setInterval(() => setNow(new Date()), 30_000);
    return () => window.clearInterval(timer);
  }, []);

  const clock = `${pad(now.getHours())}:${pad(now.getMinutes())}`;

  /**
   * 开一整页：先请主窗接手（手机窗里点时，翻的是主窗）；主窗不在就退回页内导航，
   * 并把原因留在页脚 —— 静默失败等于点了没反应。
   */
  const open = (to: string) => {
    void openInMain(bridge, to).then(({ ok, reason }) => {
      if (ok) {
        setNote('');
        return;
      }
      setNote(reason ? `已退回页内导航 · ${reason}` : '已退回页内导航');
      navigate(to);
    });
  };

  const tile = (item: Tile) => {
    const badge = item.key === 'qv' || item.key === 'dock-qv' ? unread.total : 0;
    return (
      <button
        key={item.key}
        type="button"
        className={item.dock ? 'pn__app pn__app--dock' : 'pn__app'}
        onClick={() => open(item.to)}
      >
        <span className="pn__icon">
          <span className="pn__tile" data-tone={item.tone}>
            <span className="pn__glyph" data-len={Math.min(item.glyph.length, 4)}>
              {item.glyph}
            </span>
          </span>
          {badge > 0 ? (
            <i className="pn__badge" aria-label={`${badge} 条未读`}>
              {unreadLabel(badge)}
            </i>
          ) : null}
        </span>
        {item.sub ? <span className="pn__app-label">{item.sub}</span> : null}
      </button>
    );
  };

  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    if (event.pointerType === 'mouse' && event.button !== 0) return;
    blockClick.current = false;
    drag.current = {
      id: event.pointerId,
      x0: event.clientX,
      y0: event.clientY,
      lastX: event.clientX,
      lastAt: performance.now(),
      vx: 0,
      axis: null,
      width: event.currentTarget.clientWidth || 1,
      dx: 0,
    };
  };

  const onPointerMove = (event: PointerEvent<HTMLDivElement>) => {
    const state = drag.current;
    if (!state || state.id !== event.pointerId) return;
    const dx = event.clientX - state.x0;
    const dy = event.clientY - state.y0;

    // 先判方向再接管：竖着划过 6px 就交回页内滚动，横着划才是翻页。
    if (state.axis === null) {
      if (Math.abs(dx) < 6 && Math.abs(dy) < 6) return;
      state.axis = Math.abs(dx) > Math.abs(dy) ? 'x' : 'y';
      if (state.axis === 'y') return;
      // 捕捉之后即使手指滑出这一块，move/up 也还落在这里。
      event.currentTarget.setPointerCapture(event.pointerId);
      setDragging(true);
    }
    if (state.axis !== 'x') return;

    // 速度用「最近一段位移 / 那一段用时」平滑出来：甩出去的判定靠它，不靠总位移。
    const at = performance.now();
    const dt = at - state.lastAt;
    if (dt > 0) state.vx = state.vx * 0.6 + ((event.clientX - state.lastX) / dt) * 0.4;
    state.lastX = event.clientX;
    state.lastAt = at;
    state.dx = dx;
    setOffset(give(dx, page === 0, page === PAGES - 1));
  };

  const onPointerUp = (event: PointerEvent<HTMLDivElement>) => {
    const state = drag.current;
    drag.current = null;
    if (!state || state.id !== event.pointerId) return;
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
    if (state.axis !== 'x') return;

    if (Math.abs(state.dx) > 8) {
      blockClick.current = true;
      // 拖完如果不跟一次 click（指针已经离开格子），别把下一次真正的点击也吞掉。
      window.setTimeout(() => {
        blockClick.current = false;
      }, 300);
    }

    // 过页宽 22% 就算翻；没拖够但甩得够快（>0.45px/ms，且朝拖的方向）也翻。
    const far = Math.abs(state.dx) > state.width * SWIPE_FRACTION;
    const flung = Math.abs(state.vx) > FLING_SPEED && Math.sign(state.vx) === Math.sign(state.dx);
    const next = Math.min(
      PAGES - 1,
      Math.max(0, page + (state.dx !== 0 && (far || flung) ? (state.dx < 0 ? 1 : -1) : 0)),
    );

    setSettle(next === page ? SETTLE_BACK_MS : SLIDE_MS);
    setPage(next);
    setOffset(0);
    setDragging(false);
  };

  const onPointerCancel = () => {
    if (!drag.current) return;
    drag.current = null;
    setSettle(SETTLE_BACK_MS);
    setOffset(0);
    setDragging(false);
  };

  /** 拖完那一下的点击：横滑过了阈值的这一次不算点格子。 */
  const swallowClick = (event: ReactMouseEvent<HTMLDivElement>) => {
    if (!blockClick.current) return;
    blockClick.current = false;
    event.preventDefault();
    event.stopPropagation();
  };

  return (
    <div className="pn">
      <span className="pn__shade" aria-hidden="true" />

      <div className="pn__body">
        <div className="pn__screen">
          <div className="pn__status">
            <span className="pn__time">{clock}</span>
            <span className="pn__gap" />
            <div className="pn__signals" aria-label="信号 满格，WiFi，电量">
              <span className="pn__bars" aria-hidden="true">
                <i />
                <i />
                <i />
                <i />
              </span>
              <span className="pn__wifi">WiFi</span>
              <span className="pn__battery" aria-hidden="true">
                <i className="pn__battery-shell">
                  <i />
                </i>
                <i className="pn__battery-tip" />
              </span>
            </div>
          </div>

          <div
            className="pn__pager"
            onPointerDown={onPointerDown}
            onPointerMove={onPointerMove}
            onPointerUp={onPointerUp}
            onPointerCancel={onPointerCancel}
            onClickCapture={swallowClick}
          >
            <div
              className="pn__track"
              data-page={page}
              data-drag={dragging ? '1' : undefined}
              style={
                {
                  '--pn-page': String(page),
                  '--pn-dx': `${offset}px`,
                  '--pn-dur': `${settle}ms`,
                } as CSSProperties
              }
            >
              <section className="pn__page" aria-hidden={page !== 0} aria-label="第一页">
                <h3 className="pn__sect">TA 的世界</h3>
                <div className="pn__grid">{PAGE_ONE_HOME.map(tile)}</div>

                <h3 className="pn__sect">内容与设定</h3>
                <div className="pn__grid">{PAGE_ONE_SETTING.map(tile)}</div>
              </section>

              <section className="pn__page" aria-hidden={page !== 1} aria-label="第二页">
                <h3 className="pn__sect pn__sect--top">一起玩</h3>
                <div className="pn__grid">{PAGE_TWO_PLAY.map(tile)}</div>

                <h3 className="pn__sect">小游戏</h3>
                <div className="pn__grid">{PAGE_TWO_GAMES.map(tile)}</div>
              </section>
            </div>
          </div>

          <div className="pn__dots">
            {[0, 1].map((index) => (
              <button
                key={index}
                type="button"
                className="pn__dot"
                aria-current={page === index}
                aria-label={index === 0 ? '第一页' : '第二页'}
                onClick={() => setPage(index)}
              >
                <i />
              </button>
            ))}
          </div>

          {note ? <p className="pn__note">{note}</p> : null}

          <div className="pn__dock">{DOCK.map(tile)}</div>

          <span className="pn__homebar" aria-hidden="true" />
        </div>
        <span className="pn__speaker" aria-hidden="true" />
      </div>
    </div>
  );
}
