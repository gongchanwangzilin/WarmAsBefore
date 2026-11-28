import type {
  ErrorBody,
  ErrorCode,
  EventEnvelope,
  EventName,
  Incoming,
  Method,
  Params,
  Result,
} from './protocol';
import { mockHandle } from './mock';

export type BridgeMode = 'webview2' | 'wab' | 'mock';

const REQUEST_TIMEOUT_MS = 8000;

type Transport = {
  mode: BridgeMode;
  send(raw: string): void;
  listen(handle: (raw: unknown) => void): () => void;
};

type WebView2Host = {
  postMessage(raw: string): void;
  addEventListener(type: 'message', handler: (event: { data: unknown }) => void): void;
  removeEventListener(type: 'message', handler: (event: { data: unknown }) => void): void;
};

type WabHost = {
  /** 由 Android 用 addJavascriptInterface 注入 */
  postMessage(raw: string): void;
};

export class BridgeFailure extends Error {
  readonly code: ErrorCode;
  readonly detail?: string;

  constructor(code: ErrorCode, message: string, detail?: string) {
    super(message);
    this.name = 'BridgeFailure';
    this.code = code;
    this.detail = detail;
  }
}

export function toErrorBody(error: unknown): ErrorBody {
  if (error instanceof BridgeFailure) {
    return { code: error.code, message: error.message, detail: error.detail };
  }
  if (error instanceof Error) return { code: 'E_BACKEND', message: error.message };
  return { code: 'E_BACKEND', message: '未知错误。' };
}

/* ── 三条回落路径 ─────────────────────────────────────────────
   1. WebView2：window.chrome.webview.postMessage 发出，宿主用
      postMessage 回灌，前端挂在 chrome.webview 的 message 事件上。
   2. Android：window.WabBridge.postMessage 发出（JavascriptInterface，
      同步字符串），宿主用 evaluateJavascript 调
      window.__wabBridgeDispatch(json) 回灌。
   3. 都没有：内存 mock，浏览器里能独立跑。                     */

function webview2Transport(view: WebView2Host): Transport {
  return {
    mode: 'webview2',
    send: (raw) => view.postMessage(raw),
    listen(handle) {
      const onMessage = (event: { data: unknown }) => handle(event.data);
      view.addEventListener('message', onMessage);
      return () => view.removeEventListener('message', onMessage);
    },
  };
}

function wabTransport(host: WabHost): Transport {
  return {
    mode: 'wab',
    send: (raw) => host.postMessage(raw),
    listen(handle) {
      const scope = window as unknown as {
        __wabBridgeDispatch?: (raw: unknown) => void;
      };
      const previous = scope.__wabBridgeDispatch;
      scope.__wabBridgeDispatch = (raw: unknown) => handle(raw);
      return () => {
        if (scope.__wabBridgeDispatch && scope.__wabBridgeDispatch !== previous) {
          if (previous) scope.__wabBridgeDispatch = previous;
          else delete scope.__wabBridgeDispatch;
        }
      };
    },
  };
}

function mockTransport(): Transport {
  let emit: (raw: unknown) => void = () => undefined;
  return {
    mode: 'mock',
    send(raw) {
      void mockHandle(raw).then((reply) => emit(reply));
    },
    listen(handle) {
      emit = handle;
      return () => {
        emit = () => undefined;
      };
    },
  };
}

function detectTransport(): Transport {
  const scope = window as unknown as {
    chrome?: { webview?: WebView2Host };
    WabBridge?: WabHost;
  };
  const view = scope.chrome?.webview;
  if (view && typeof view.postMessage === 'function' && typeof view.addEventListener === 'function') {
    return webview2Transport(view);
  }
  const wab = scope.WabBridge;
  if (wab && typeof wab.postMessage === 'function') return wabTransport(wab);
  return mockTransport();
}

type Pending = {
  resolve(value: unknown): void;
  reject(reason: unknown): void;
  timer: number;
};

export class Bridge {
  readonly mode: BridgeMode;

  private readonly transport: Transport;
  private seq = 1;
  private readonly pending = new Map<number, Pending>();
  private readonly listeners = new Set<(event: EventEnvelope) => void>();

  constructor(transport: Transport = detectTransport()) {
    this.transport = transport;
    this.mode = transport.mode;
    this.transport.listen((raw) => this.receive(raw));
  }

  /**
   * 发一条请求。`timeoutMs` 只给「宿主要等一段真实时间」的方法用（voice.listen 开麦等一句话，
   * 上限 20 秒，8 秒默认值会先把它判死）；其余请求走同一个默认值。
   */
  request<M extends Method>(method: M, params: Params<M>, timeoutMs: number = REQUEST_TIMEOUT_MS): Promise<Result<M>> {
    const id = this.seq++;
    const raw = JSON.stringify({ id, method, params });

    return new Promise<Result<M>>((resolve, reject) => {
      const timer = window.setTimeout(() => {
        this.pending.delete(id);
        reject(new BridgeFailure('E_TIMEOUT', `后端 ${Math.round(timeoutMs / 1000)} 秒没有回应。`, method));
      }, timeoutMs);

      this.pending.set(id, {
        resolve: resolve as (value: unknown) => void,
        reject,
        timer,
      });

      try {
        this.transport.send(raw);
      } catch (error) {
        window.clearTimeout(timer);
        this.pending.delete(id);
        reject(
          new BridgeFailure(
            'E_TRANSPORT',
            '消息没有送出去，宿主那一侧可能已经断开。',
            error instanceof Error ? error.message : String(error),
          ),
        );
      }
    });
  }

  onEvent<E extends EventName>(name: E, handler: (payload: EventEnvelope<E>['payload']) => void) {
    const wrapped = (event: EventEnvelope) => {
      if (event.event === name) handler(event.payload as EventEnvelope<E>['payload']);
    };
    this.listeners.add(wrapped);
    return () => {
      this.listeners.delete(wrapped);
    };
  }

  private receive(raw: unknown): void {
    let message: Incoming;
    try {
      message = typeof raw === 'string' ? (JSON.parse(raw) as Incoming) : (raw as Incoming);
    } catch {
      return;
    }
    if (!message || typeof message !== 'object') return;

    if ('event' in message) {
      for (const listener of [...this.listeners]) listener(message);
      return;
    }

    if (typeof message.id !== 'number') return;
    const pending = this.pending.get(message.id);
    if (!pending) return;

    this.pending.delete(message.id);
    window.clearTimeout(pending.timer);

    if (message.ok) pending.resolve(message.result);
    else pending.reject(new BridgeFailure(message.error.code, message.error.message, message.error.detail));
  }
}
