import { StrictMode } from 'react';
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
 * 令牌先到、页面后画：颜色与字阶都在 :root 上，避免第一帧用默认黑字闪一下。
 * 拿不到就拿 mock 的那一份兜底 —— 连错误页都得有颜色和字号，否则报错都看不见。
 */
async function bootTokens(bridge: Bridge): Promise<TokenMap> {
  try {
    const { tokens } = await bridge.request('ui.tokens', {});
    return tokens;
  } catch {
    return MOCK_TOKENS;
  }
}

const bridge = new Bridge();
const tokens = await bootTokens(bridge);
applyTokens(dampingTokens(tokens, prefersReducedMotion()));

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App bridge={bridge} tokens={tokens} />
  </StrictMode>,
);
