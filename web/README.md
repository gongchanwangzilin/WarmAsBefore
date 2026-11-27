# web/ — Web 前端骨架

原型 `spike/web-glass/` 是设计基准，这里把它升级成能跑应用的骨架：路由、消息桥、三页、四种状态。
只依赖 React + Vite，样式是原生 CSS，路由是自写的 hash 路由。

```bash
cd web
npm install
npm run dev      # http://localhost:5188/
npm run build    # tsc --noEmit && vite build
```

## 目录

```
src/
  bridge/        协议与桥
    protocol.ts  请求/应答/事件三种形状，方法表即类型
    client.ts    三条传输路径 + 超时 + 事件分发
    mock.ts      内存假后端（延迟/读失败/写失败/空数据四个开关）
    tokens.ts    mock 下发的令牌（真机由 ui.tokens 从 C# 侧送来）
  lib/           路由、动效时钟、墨阶、地面、玻璃光、资源装载
  components/    Glass / 开关 / 滑杆 / 主按钮 / 顶栏 / 页面栈
  pages/         标题页、设置首页、玻璃效果页
  styles/        glass.css（六层材质）、app.css（版式与状态）
```

## 桥

三条回落路径，按顺序探测：

1. `window.chrome.webview.postMessage` → WebView2；应答走 `chrome.webview` 的 `message` 事件。
2. `window.WabBridge.postMessage` → Android 注入的 JavascriptInterface；宿主用
   `evaluateJavascript("window.__wabBridgeDispatch(<json>)")` 回灌。
3. 都没有 → 内存 mock，浏览器里独立跑。

消息都是 JSON 字符串：请求 `{ id, method, params }`，应答 `{ id, ok, result | error }`，
后端主动事件 `{ event, payload }`（`nav.request` / `palette.changed` / `settings.changed` / `theme.tokens`）。
请求 8 秒无应答按 `E_TIMEOUT` 失败。

设置页底部有一张「演示 · 桥的故障开关」，只在 mock 下出现：延迟、读失败、写失败、空数据。
改一项会重新拉这一页，用来演示骨架屏、错误状态与空状态。

## 约定

- 颜色、字号、间距、圆角、动效时长全部来自 `ui.tokens` 下发的 `--w-*`；页面 CSS 里没有裸色值。
- 过场：位移 + 透明度 320ms（时长读 `--w-dur-page`），前进从左进右出、返回反向；
  过场途中再导航，两层都从当前值接手。
- `prefers-reduced-motion`：令牌里的时长归零、位移归零，状态照常落定。
