import { useEffect, useRef, useState } from 'react';
import type { Bridge } from '../bridge/client';
import { openConversationId } from '../lib/unread';

/**
 * 宿主提示条。
 *
 * 原生的 Toast 是 MAUI 画在 WebView 上面的一层，缩放、圆角、字号都跟着原生走，
 * 和前端这套玻璃不是一回事。所以内容仍由后端产生（导入成功、写盘失败、警告），
 * 画法收到这里：同样的墨阶、同样的玻璃、同样的缩放规则。
 *
 * 行为照原版：右下/右上浮现、停几秒自己走、可以点掉。
 */

type Kind = 'info' | 'success' | 'warn' | 'error';

type Item = {
  id: number;
  title: string;
  body: string;
  code: string;
  kind: Kind;
  glyph: string;
};

const LIFETIME: Record<Kind, number> = {
  info: 4200,
  success: 3600,
  warn: 5600,
  error: 7000,
};

let seq = 0;

/**
 * 这一扇窗现在停在 QV 上吗。
 *
 * QV 是一个「打开就该安静」的应用：她那边的消息由 QV 自己的名册与未读数说话，
 * 窗口不需要再在右上角叠一张条 —— 「一进 QV 就弹一条」正是用户说的那条通知。
 * 提示条本身不按路由重建（它挂在 App 上、整窗只挂一次），所以这里读实时 hash。
 */
function onQvPage(): boolean {
  return window.location.hash.replace(/^#/, '').startsWith('/qv');
}

/**
 * 这一扇窗是主窗还是独立窗（手机这类）。
 *
 * 启动自检与她发来一条都是「整台机器只该说一次」的话：独立窗一开就再弹一遍，
 * 用户看到的是同一条消息越弹越多。问一次 app.info 就记住；
 * 老宿主没有 `window` 字段、或者问不到，一律当主窗 —— 宁可多弹，也不让主窗悄悄少一条。
 */
let roleProbe: Promise<'main' | 'sub'> | null = null;

function windowRole(bridge: Bridge): Promise<'main' | 'sub'> {
  roleProbe ??= bridge
    .request('app.info', {})
    .then((info) => (info.window?.role === 'sub' ? 'sub' : 'main'), () => 'main' as const);
  return roleProbe;
}

export function ToastHost({ bridge }: { bridge: Bridge }) {
  const [items, setItems] = useState<Item[]>([]);
  const recent = useRef<Record<string, number>>({});

  const push = (payload: Omit<Item, 'id'>) => {
    // 同一条提示可能从两条路各来一次（宿主推一遍、前端自检一遍）：
    // 内容一样且 9 秒内已经出现过，就不再叠第二张。
    const stamp = `${payload.code}|${payload.title}|${payload.body}`;
    const now = performance.now();
    if (recent.current[stamp] && now - recent.current[stamp] < 9000) return;
    recent.current[stamp] = now;

    const id = ++seq;
    const kind = (payload.kind || 'info') as Kind;
    setItems((prev) => [...prev.slice(-2), { ...payload, kind, id }]);
    window.setTimeout(() => setItems((prev) => prev.filter((item) => item.id !== id)), LIFETIME[kind] ?? 4200);
  };

  useEffect(() => {
    const off = bridge.onEvent('ui.toast', (payload) => {
      // 宿主自己也会推提示（启动时那条「还没配置任何模型」就是它推的）。
      // 站在 QV 上时只留 error：那是「事情没做成」，静默掉等于点了没反应；
      // 其余（info / warn / success）都是别处的事，QV 打开时一条都不弹。
      if (payload.kind !== 'error' && onQvPage()) return;
      push(payload);
    });
    return off;
  }, [bridge]);

  // 她发来一条手机消息：那一格会话没开着才弹（开着的话她已经在你眼前，条是多余的）。
  // 站在 QV 上时整条让位：名册行上的角标与未读数已经把这件事说了一遍。
  // 独立窗（手机那扇）同样不弹：它和主窗看的是同一份未读，条只在一处出现；
  // 那边的角标由 useUnread 自己 +1，与这里无关。
  // 标题就是她的名字、正文就是消息本身（宿主给的 preview），条上的字一个都不新编。
  useEffect(() => {
    const off = bridge.onEvent('chat.arrived', (payload) => {
      if (onQvPage()) return;
      if (payload.characterId === openConversationId()) return;
      void windowRole(bridge).then((role) => {
        if (role === 'sub') return;
        push({
          title: payload.name || payload.characterId,
          body: payload.preview,
          code: 'QV',
          kind: 'info',
          glyph: '✦',
        });
      });
    });
    return off;
  }, [bridge]);

  // 启动自检：一个模型都没配时给一次提醒 —— 条件与文案都照 App.xaml.cs 里那一处原文，
  // 只是这条以前是原生画的，现在由前端自己判断、自己画。
  // 只由主窗说：原生那条挂在 AfterWindowStartedAsync 上，独立窗根本不跑；前端这条也得守同一条线，
  // 否则手机窗一开又弹一次「还没配置任何模型」。QV 那一扇窗同样不弹（那里是去看消息的）。
  useEffect(() => {
    let alive = true;
    void (async () => {
      if ((await windowRole(bridge)) !== 'main') return;
      const payload = await bridge.request('ai.providers', {}).catch(() => null);
      if (!alive || !payload) return;
      if (onQvPage()) return;
      if ((payload.providers ?? []).length > 0) return;
      push({
        title: '还没配置任何模型：去「AI 模型管理 → 模型部署向导」下载并安装（名字、来源、位置都由你定）。',
        body: '',
        code: 'WARN',
        kind: 'warn',
        glyph: '✦',
      });
    })();
    return () => {
      alive = false;
    };
  }, [bridge]);

  if (items.length === 0) return null;

  return (
    <div className="toasts" role="status" aria-live="polite">
      {items.map((item) => (
        <div
          key={item.id}
          className="toast"
          data-kind={item.kind}
          onClick={() => setItems((prev) => prev.filter((entry) => entry.id !== item.id))}
        >
          <span className="toast__glyph">{item.glyph || '✦'}</span>
          <span className="toast__text">
            <span className="toast__code">{item.code || 'NOTICE'}</span>
            {item.title ? <span className="toast__title">{item.title}</span> : null}
            {item.body ? <span className="toast__body">{item.body}</span> : null}
          </span>
        </div>
      ))}
    </div>
  );
}
