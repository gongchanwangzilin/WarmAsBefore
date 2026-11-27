import { useState } from 'react';
import type { ScreenProps } from '../types';
import { closeWindow } from '../../lib/windows';
import './main.css';

/**
 * 手机：Views/Main/PhonePage.xaml（808 行）+ PhoneViewModel。
 *
 * 一台摆在地面上的手机：状态栏（退出口袋 / 时间 / 信号 / WiFi / 电池）、两页应用网格、
 * 页码点、Dock、下侧栏三键与「转到主窗口」。窄屏（真手机/小窗）撤掉机身，屏幕直接铺满。
 *
 * 图标下的字照抄源码；图标本身在源码里是各家的品牌色渐变，Web 端没有对应令牌，
 * 只能按「暖 / 沙 / 深 / 冷」四档从令牌混出来。
 */

type Tile = {
  key: string;
  glyph: string;
  label: string;
  sub: string;
  tone: 'warm' | 'sand' | 'deep' | 'cool';
  to: string;
  small?: boolean;
};

const PAGE_ONE_APPS: Tile[] = [
  { key: 'wechat', glyph: '微信', label: '微信', sub: '微信 · 本地', tone: 'warm', to: '/chat' },
  { key: 'roster', glyph: '角色库', label: '角色库', sub: '人设 · 改写', tone: 'sand', to: '/roster' },
  { key: 'map', glyph: '地图', label: '地图', sub: '去处 · 位置', tone: 'cool', to: '/map' },
  { key: 'outfit', glyph: '换装', label: '换装', sub: '立绘 · 服饰', tone: 'warm', to: '/outfit' },
  { key: 'qq', glyph: 'QQ', label: 'QQ', sub: 'QQ · 论坛', tone: 'cool', to: '/qqchat' },
  { key: 'gallery', glyph: '回忆录', label: '回忆录', sub: '相册 · 日记', tone: 'cool', to: '/gallery' },
  { key: 'materials', glyph: '素材库', label: '素材库', sub: '背景 · 音乐', tone: 'sand', to: '/materials' },
  { key: 'workshop', glyph: '工坊', label: '工坊', sub: '场景 · 模式', tone: 'sand', to: '/scenelibrary' },
  { key: 'worldbook', glyph: '世界书', label: '世界书', sub: '设定 · 条目', tone: 'deep', to: '/worldbook' },
];

const PAGE_TWO_APPS: Tile[] = [
  { key: 'forcecast', glyph: '点名', label: '点名', sub: '强制入戏', tone: 'warm', to: '/forcecast' },
  { key: 'shop', glyph: '商店', label: '商店', sub: '礼物 · 道具', tone: 'warm', to: '/shop' },
  { key: 'gobang', glyph: '五子棋', label: '五子棋', sub: '五连', tone: 'sand', to: '/game?kind=gobang', small: true },
  { key: 'animalchess', glyph: '斗兽棋', label: '斗兽棋', sub: '兽棋', tone: 'sand', to: '/game?kind=animalchess', small: true },
  { key: 'ludo', glyph: '飞行棋', label: '飞行棋', sub: '四人', tone: 'cool', to: '/game?kind=ludo', small: true },
  { key: 'chess', glyph: '国际象棋', label: '国际象棋', sub: '64 格', tone: 'cool', to: '/game?kind=chess', small: true },
  { key: 'chinesechess', glyph: '中国象棋', label: '中国象棋', sub: '河界', tone: 'warm', to: '/game?kind=chinesechess', small: true },
  { key: 'snake', glyph: '贪吃蛇', label: '贪吃蛇', sub: '越吃越长', tone: 'sand', to: '/game?kind=snake', small: true },
  { key: 'go', glyph: '围棋', label: '围棋', sub: '19 路', tone: 'deep', to: '/game?kind=go', small: true },
];

const DOCK_APPS: Tile[] = [
  { key: 'dock-wechat', glyph: '微信', label: '微信', sub: '', tone: 'warm', to: '/chat', small: true },
  { key: 'dock-roster', glyph: '角色库', label: '角色库', sub: '', tone: 'sand', to: '/roster', small: true },
  { key: 'dock-gallery', glyph: '回忆录', label: '回忆录', sub: '', tone: 'cool', to: '/gallery', small: true },
  { key: 'dock-materials', glyph: '素材库', label: '素材库', sub: '', tone: 'sand', to: '/materials', small: true },
];

function periodLabel(hour: number): string {
  if (hour >= 5 && hour < 11) return '上午';
  if (hour >= 11 && hour < 13) return '中午';
  if (hour >= 13 && hour < 17) return '下午';
  if (hour >= 17 && hour < 19) return '傍晚';
  return '晚上';
}

function pad(value: number): string {
  return String(value).padStart(2, '0');
}

export function Phone({ bridge, navigate, back }: ScreenProps) {
  // 这一页在桌面上是自己那扇窗：窗里的「‹」关窗，窗外的「‹」照旧返回上一页。
  const leave = () => void closeWindow(bridge, back);
  const [page, setPage] = useState(0);
  const now = new Date();
  const clock = `${pad(now.getHours())}:${pad(now.getMinutes())}`;
  const statusTime = `${pad(now.getMonth() + 1)}-${pad(now.getDate())} ${periodLabel(now.getHours())}`;

  const appButton = (tile: Tile, className = '') => (
    <button
      type="button"
      className={`mp__app ${className}`.trim()}
      key={tile.key}
      onClick={() => navigate(tile.to)}
    >
      <span className={`mp__tile${tile.small ? ' mp__tile--sm' : ''}`} data-tone={tile.tone}>
        <span>{tile.glyph}</span>
      </span>
      {tile.sub ? <span className="mp__app-label">{tile.sub}</span> : null}
    </button>
  );

  return (
    <div className="mp">
      <div className="mp__body">
        <div className="mp__screen">
          <span className="mp__glow mp__glow--a" aria-hidden="true" />
          <span className="mp__glow mp__glow--b" aria-hidden="true" />

          <div className="mp__status">
            <button type="button" className="mp__back-pill" onClick={leave}>
              <span>‹</span>
              <span>游戏</span>
            </button>
            <span className="mp__time">{clock}</span>
            <div className="mp__signals" aria-label="信号 满格">
              <span className="mp__bars" aria-hidden="true">
                <i style={{ height: 3.5 }} />
                <i style={{ height: 5.5 }} />
                <i style={{ height: 7.5 }} />
                <i style={{ height: 9.5 }} />
              </span>
              <span className="mp__wifi">WiFi</span>
              <span className="mp__battery" aria-hidden="true">
                <span className="mp__battery-frame">
                  <i />
                </span>
                <span className="mp__battery-tip" />
              </span>
            </div>
          </div>

          <div className="mp__pages">
            {page === 0 ? (
              <div className="mp__page">
                <div className="mp__clock">
                  <div>
                    <h2>{clock}</h2>
                    <div className="mp__clock-sub">{statusTime}</div>
                    <div className="mp__clock-line">温暖如初 · 手机</div>
                  </div>
                  <span className="mp__weather">晴天</span>
                </div>

                <span className="mp__sect">TA 的世界</span>
                <div className="mp__grid">{PAGE_ONE_APPS.slice(0, 6).map((tile) => appButton(tile))}</div>

                <span className="mp__sect">内容与设定</span>
                <div className="mp__grid">{PAGE_ONE_APPS.slice(6).map((tile) => appButton(tile))}</div>
              </div>
            ) : (
              <div className="mp__page">
                <span className="mp__sect">一起玩</span>
                <div className="mp__grid">{PAGE_TWO_APPS.slice(0, 2).map((tile) => appButton(tile))}</div>

                <span className="mp__sect">小游戏</span>
                <div className="mp__grid">{PAGE_TWO_APPS.slice(2).map((tile) => appButton(tile))}</div>
              </div>
            )}
          </div>

          <div className="mp__dots">
            <button type="button" className="mp__dot" aria-current={page === 0} aria-label="第一页" onClick={() => setPage(0)}>
              <i />
            </button>
            <button type="button" className="mp__dot" aria-current={page === 1} aria-label="第二页" onClick={() => setPage(1)}>
              <i />
            </button>
          </div>

          <div className="mp__dock">{DOCK_APPS.map((tile) => appButton(tile))}</div>

          <div className="mp__nav">
            <button type="button" data-k="back" onClick={leave} aria-label="回退">
              ◁
            </button>
            <button type="button" data-k="home" onClick={() => setPage(0)} aria-label="回到桌面">
              □
            </button>
            <button type="button" data-k="settings" onClick={() => navigate('/settings')} aria-label="设置">
              ≡
            </button>
            <button type="button" className="mp__nav-tail" onClick={() => navigate('/main')}>
              转到主窗口
            </button>
          </div>

          <div className="mp__homebar" aria-hidden="true" />
          <span className="mp__speaker" aria-hidden="true" />
        </div>
      </div>
    </div>
  );
}
