import { StrictMode, useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App';
import { Bridge } from './bridge/client';
import { MOCK_TOKENS } from './bridge/tokens';
import type { TokenMap } from './bridge/protocol';
import { prefersReducedMotion } from './lib/motion';
import { applyTokens, dampingTokens } from './lib/tokens';
import './styles/glass.css';
import './styles/app.css';
import './styles/kit.css';

/**
 * 令牌是颜色与字阶的补色，不是开屏的前提。
 *
 * 原来这里先 await 一次 `ui.tokens` 才 render：后端慢一秒，白屏就多一秒，超时 8 秒时
 * 整窗连错误页都看不见。现在第一帧就用 mock 那份令牌上色（它和真机同一套阶），
 * 真令牌到了再覆盖 —— 首屏不再等任何一个请求。
 */
const bridge = new Bridge();

applyTokens(dampingTokens(MOCK_TOKENS, prefersReducedMotion()));

/** StrictMode 会把 effect 跑两遍，请求只发一次。 */
let tokensOnce: Promise<TokenMap | null> | null = null;

function bootTokens(): Promise<TokenMap | null> {
  tokensOnce ??= bridge.request('ui.tokens', {}).then(
    ({ tokens }) => tokens,
    () => null,
  );
  return tokensOnce;
}

function Root() {
  const [tokens, setTokens] = useState<TokenMap>(MOCK_TOKENS);

  useEffect(() => {
    let live = true;
    void bootTokens().then((real) => {
      if (!live || !real) return;
      applyTokens(dampingTokens(real, prefersReducedMotion()));
      setTokens(real);
    });
    return () => {
      live = false;
    };
  }, []);

  return <App bridge={bridge} tokens={tokens} />;
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <Root />
  </StrictMode>,
);

// 渲染两帧之后再报「通道挂好了」：这时订阅事件的 useEffect 已经跑过一轮，
// 宿主补发的启动提示才有人接（早一拍就落在地上，原生那条又已经让位了）。
requestAnimationFrame(() => {
  window.setTimeout(() => {
    void bridge.request('ui.ready', {}).catch(() => undefined);
  }, 60);
});
