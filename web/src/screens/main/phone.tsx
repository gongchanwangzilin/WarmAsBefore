import { useEffect, useRef, useState, type PointerEvent } from 'react';
import type { ScreenProps } from '../types';
import { closeWindow } from '../../lib/windows';
import './main.css';

/**
 * 手机：Views/Main/PhonePage.xaml（808 行）+ PhoneViewModel。
 *
 * 摆在地面上的一台真机：接地影、机身、圆角屏幕、壁纸渐变与两团光斑；
 * 状态栏（退出口袋 ‹ 游戏 / 时间 / 信号 / WiFi / 电池）、两页桌面、页码点、
 * Dock、下侧栏三键与「转到主窗口」。窄窗（真手机、小窗）撤掉机身，屏幕直接铺满 ——
 * 源码的 IsCompactedScreen 就是这个判断。
 *
 * 每块图标的字都照抄 XAML（底下那行小字是 .xaml 里各自的 Label Text）；
 * 机身内是暖色渐变壁纸，源码用的是 Cream/Beige 那几只刷子，Web 端按令牌混出来。
 * 数据只有 PhoneViewModel 的本地档：时间、StatusTime、StatusLine、WeatherText、WeChatLabel。
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
  { key: 'wechat', glyph: '微信', sub: '微信 · 本地', tone: 'warm', to: '/chat' },
  { key: 'roster', glyph: '角色库', sub: '人设 · 改写', tone: 'sand', to: '/roster' },
  { key: 'map', glyph: '地图', sub: '去处 · 位置', tone: 'sand', to: '/map' },
  { key: 'outfit', glyph: '换装', sub: '立绘 · 服饰', tone: 'rose', to: '/outfit' },
  { key: 'qq', glyph: 'QQ', sub: 'QQ · 论坛', tone: 'cool', to: '/qqchat' },
  { key: 'gallery', glyph: '回忆录', sub: '相册 · 日记', tone: 'deep', to: '/gallery' },
];

const PAGE_ONE_SETTING: Tile[] = [
  { key: 'materials', glyph: '素材库', sub: '背景 · 音乐', tone: 'jade', to: '/materials' },
  { key: 'workshop', glyph: '工坊', sub: '场景 · 模式', tone: 'jade', to: '/scenelibrary' },
  { key: 'worldbook', glyph: '世界书', sub: '设定 · 条目', tone: 'cool', to: '/worldbook' },
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
  { key: 'dock-wechat', glyph: '微信', sub: '', tone: 'warm', to: '/chat', dock: true },
  { key: 'dock-roster', glyph: '角色库', sub: '', tone: 'sand', to: '/roster', dock: true },
  { key: 'dock-gallery', glyph: '回忆录', sub: '', tone: 'deep', to: '/gallery', dock: true },
  { key: 'dock-materials', glyph: '素材库', sub: '', tone: 'jade', to: '/materials', dock: true },
];

/** PhoneViewModel.OnAppearing 的 Period：morning / noon / afternoon / evening / night。 */
function periodLabel(hour: number): string {
  if (hour >= 5 && hour < 11) return '上午';
  if (hour >= 11 && hour < 13) return '中午';
  if (hour >= 13 && hour < 17) return '下午';
  if (hour >= 17 && hour < 19) return '傍晚';
  return '晚上';
}

const pad = (value: number) => String(value).padStart(2, '0');

export function Phone({ bridge, navigate, back }: ScreenProps) {
  // 这一页在桌面上是自己那扇窗：窗里的「‹」关窗，窗外的「‹」照旧返回上一页。
  const leave = () => void closeWindow(bridge, back);

  const [page, setPage] = useState(0);
  const [now, setNow] = useState(() => new Date());
  const drag = useRef<{ x: number; tracking: boolean }>({ x: 0, tracking: false });

  useEffect(() => {
    const timer = window.setInterval(() => setNow(new Date()), 30_000);
    return () => window.clearInterval(timer);
  }, []);

  const clock = `${pad(now.getHours())}:${pad(now.getMinutes())}`;
  const statusTime = `${pad(now.getMonth() + 1)}-${pad(now.getDate())} ${periodLabel(now.getHours())}`;

  const tile = (item: Tile) => (
    <button
      key={item.key}
      type="button"
      className={item.dock ? 'pn__app pn__app--dock' : 'pn__app'}
      onClick={() => navigate(item.to)}
    >
      <span className="pn__tile" data-tone={item.tone}>
        <i className="pn__gloss" aria-hidden="true" />
        <span className="pn__glyph" data-len={Math.min(item.glyph.length, 4)}>
          {item.glyph}
        </span>
      </span>
      {item.sub ? <span className="pn__app-label">{item.sub}</span> : null}
    </button>
  );

  const onPointerDown = (event: PointerEvent<HTMLDivElement>) => {
    drag.current = { x: event.clientX, tracking: true };
  };

  const onPointerUp = (event: PointerEvent<HTMLDivElement>) => {
    if (!drag.current.tracking) return;
    const dx = event.clientX - drag.current.x;
    drag.current.tracking = false;
    if (Math.abs(dx) < 40) return;
    setPage(dx < 0 ? 1 : 0);
  };

  return (
    <div className="pn">
      <span className="pn__shade" aria-hidden="true" />

      <div className="pn__body">
        <div className="pn__screen">
          <span className="pn__glow pn__glow--a" aria-hidden="true" />
          <span className="pn__glow pn__glow--b" aria-hidden="true" />

          <div className="pn__status">
            <button type="button" className="pn__back" onClick={leave}>
              <span>‹</span>
              <span>游戏</span>
            </button>
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
            onPointerUp={onPointerUp}
            onPointerCancel={() => {
              drag.current.tracking = false;
            }}
          >
            <div className="pn__track" data-page={page}>
              <section className="pn__page" aria-hidden={page !== 0}>
                <div className="pn__clock">
                  <div className="pn__clock-main">
                    <h2>{clock}</h2>
                    <span className="pn__clock-sub">{statusTime}</span>
                    <span className="pn__clock-line">温暖如初 · 手机</span>
                  </div>
                  <span className="pn__weather">晴天</span>
                </div>

                <h3 className="pn__sect">TA 的世界</h3>
                <div className="pn__grid">{PAGE_ONE_HOME.map(tile)}</div>

                <h3 className="pn__sect">内容与设定</h3>
                <div className="pn__grid">{PAGE_ONE_SETTING.map(tile)}</div>
              </section>

              <section className="pn__page" aria-hidden={page !== 1}>
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

          <div className="pn__dock">{DOCK.map(tile)}</div>

          <div className="pn__nav">
            <button type="button" onClick={leave} aria-label="回退">
              ◁
            </button>
            <button type="button" onClick={() => setPage(0)} aria-label="回到桌面">
              □
            </button>
            <button type="button" onClick={() => navigate('/settings')} aria-label="设置">
              ≡
            </button>
            <button type="button" className="pn__nav-tail" onClick={() => navigate('/main')}>
              转到主窗口
            </button>
          </div>

          <span className="pn__homebar" aria-hidden="true" />
        </div>
        <span className="pn__speaker" aria-hidden="true" />
      </div>
    </div>
  );
}
