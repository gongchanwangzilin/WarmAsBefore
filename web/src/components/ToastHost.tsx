import { useEffect, useState } from 'react';
import type { Bridge } from '../bridge/client';

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

export function ToastHost({ bridge }: { bridge: Bridge }) {
  const [items, setItems] = useState<Item[]>([]);

  const push = (payload: Omit<Item, 'id'>) => {
    const id = ++seq;
    const kind = (payload.kind || 'info') as Kind;
    setItems((prev) => [...prev.slice(-2), { ...payload, kind, id }]);
    window.setTimeout(() => setItems((prev) => prev.filter((item) => item.id !== id)), LIFETIME[kind] ?? 4200);
  };

  useEffect(() => {
    const off = bridge.onEvent('ui.toast', (payload) => push(payload));
    return off;
  }, [bridge]);

  // 启动自检：一个模型都没配时给一次提醒 —— 条件与文案都照 App.xaml.cs 里那一处原文，
  // 只是这条以前是原生画的，现在由前端自己判断、自己画。
  useEffect(() => {
    let alive = true;
    void bridge
      .request('ai.providers', {})
      .then((payload) => {
        if (!alive) return;
        if ((payload.providers ?? []).length > 0) return;
        push({
          title: '还没配置任何模型：去「AI 模型管理 → 模型部署向导」下载并安装（名字、来源、位置都由你定）。',
          body: '',
          code: 'WARN',
          kind: 'warn',
          glyph: '✦',
        });
      })
      .catch(() => undefined);
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
