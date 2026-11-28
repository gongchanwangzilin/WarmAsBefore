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

/**
 * 窗口是应用，不是浏览器：把浏览器留给用户的那几样能力在这里收一次。
 *
 * 右键菜单、把图片拖出一个跟手的幽灵、按住划过一段文字、点一下闪一块蓝 ——
 * 每一样都在告诉用户「这是个网页」。放在模块顶层、第一次渲染之前挂，
 * 所有页面、所有弹层都逃不掉这一份，不用逐页抄。
 *
 * 放行名单只留真正要读写的文字：输入框、多行框、可编辑区、下拉列表、
 * 以及显式标了 `data-selectable` 的正文（比如要让人复制的一段日志）。
 * 与 styles/app.css 里那份 user-select 白名单同一条线。
 */
const TEXT_SURFACE =
  'input, textarea, [contenteditable], .k-select__list, [data-selectable]';

function allowsText(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest(TEXT_SURFACE) !== null;
}

function lockBrowserSurface(): void {
  // 右键：菜单沿用不着（Windows 侧宿主已关，这里给 Android 与 dev server 兜）。
  document.addEventListener('contextmenu', (event) => event.preventDefault());

  // 拖：图与链接一律拖不出幽灵。拖动在这台机器上是应用自己的手势。
  document.addEventListener('dragstart', (event) => event.preventDefault());

  // 选中：默认不许，白名单放行。拦在 selectstart 是因为它早于浏览器真正开始选中，
  // 按住划过去时连那一下蓝色的闪都不会有。
  document.addEventListener('selectstart', (event) => {
    if (!allowsText(event.target)) event.preventDefault();
  });
}

lockBrowserSurface();

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
    // 宿主换了配色（设置里点预设换那一套种子，或原生侧改了外观）：令牌整份重发一条。
    // 换上去之后 App 里的地面与六级墨都从这份 tokens 现算，所以整屏跟着一起变。
    const off = bridge.onEvent('theme.tokens', ({ tokens: next }) => {
      applyTokens(dampingTokens(next, prefersReducedMotion()));
      setTokens(next);
    });
    return () => {
      live = false;
      off();
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
