# 插件系统

插件是放在应用数据目录 `plugins/<id>/` 下的本地 JavaScript 包。宿主扫描 `plugin.json`，
在 Jint 沙箱里执行钩子，并通过一组白名单宿主函数把「能做什么」交给权限系统裁决。

插件不是 MCP，也不是外部工具：

| | 插件 | MCP | 工具 | 技能 |
|---|---|---|---|---|
| 形态 | 本地 JS 包 | 外部 stdio / HTTP 服务 | 内置 C# 或外部 Python/Java | `SKILL.md` + 可选 `tool.json` |
| 入口 | 插件中心 | 插件中心的 MCP 列表 | 工具管理 / 聊天工具调用 | 技能库 |
| 扩展点 | 钩子、设置页、自带页面 | 工具清单 | JSON-RPC 工具 | 渐进加载的说明书 |
| 运行位置 | 应用进程内 Jint 沙箱 | 独立进程或远端服务 | 进程内 / 子进程 | 本地目录 |
| 能改对话 | 可以（persona / beforeSend / afterReply） | 只能提供工具 | 只能提供工具 | 只能提供工具 |

> 插件的能力边界是**宿主函数 + 权限**，不是进程隔离。插件 JS 与应用同进程运行，
> 只应安装可信来源的插件。

---

## 1. 目录与数据

```text
{数据根}/                          # App.RootDirectory，Windows 通常在 %LOCALAPPDATA% 下
├── plugins/
│   ├── <id>/
│   │   ├── plugin.json           # 清单（必需）
│   │   ├── hooks/                # 钩子 JS（路径可自定义）
│   │   ├── pages/                # 自带页面（可选）
│   │   ├── files/                # 插件文件区（运行时创建）
│   │   └── settings.json         # 插件设置（宿主写入）
│   ├── state.json                # 启停状态
│   └── permissions.json          # 授权策略、询问答案、留痕
├── plugin_logs/
│   └── <id>.log                  # 崩溃、权限拦截、JS 异常；超过 256KB 截到最近 128KB
└── ...
```

- 插件目录名就是它的安装 id；`plugin.json` 里的 `id` 在安装时会被清洗成
  字母、数字、`-`、`_`，清洗后为空则生成 `plugin_xxxxxx`。
- 装载时以目录名为准；清单里的 `id` 与目录名不一致时，体检会给警告。
- `settings.json` 由 `StorageProvider` 写入，值统一是**字符串**。
- 插件文件区只能通过 `writeFile` / `readFile` / `listFiles` / `deleteFile` 访问，
  不是任意文件系统入口。
- 插件日志受设置项「插件崩溃日志」控制；关闭后 `LogCrash` 不再写文件。

---

## 2. 安装、启停与卸载

### 安装

| 方式 | 行为 |
|---|---|
| ZIP | 解压到临时目录，递归找第一个 `plugin.json`；允许压缩包里多套一层目录 |
| 文件夹（Windows） | 直接读取选中的目录；Android 不提供文件夹选择 |
| 手动放置 | 把插件目录放进 `plugins/<id>/`，在插件中心刷新 |
| AI 工坊 | 生成到临时目录，再走同一条 `InstallFromFolderAsync` 安装路径 |

安装流程：

1. 读取 `plugin.json`，要求至少有 `name`。
2. 计算 id：优先 `manifest.id`，否则用 zip 文件名/目录名；只保留字母、数字、`-`、`_`。
3. 目标已存在且未允许覆盖 → 失败；允许覆盖则整目录替换。
4. 复制到 `plugins/<id>/`，把清洗后的 id 写回 `plugin.json`。
5. 重新扫描，执行 `onInstall` 钩子。
6. 刷新列表，并提示是否立即体检。

> 解压/复制阶段不会执行插件 JS。`onInstall` 是安装完成后才执行的第一个钩子。

### 启停

- 默认启用；停用状态写入 `plugins/state.json`。
- 停用前执行 `onUnload`，启用后执行 `onLoad`。
- 停用会清掉该插件的「仅本次」授权答案；下次启用重新询问。
- 全局设置里的「插件钩子」总开关关闭时，`persona` / `context` / `beforeSend` /
  `afterReply` 与广播钩子不执行；`onLoad` / `onInstall` / `onUnload` 仍会执行。

### 卸载

删除插件目录，清空该插件的授权记录，停止它的 HTML 回调服务器。
插件自己的 `settings.json` 与 `files/` 随目录一起删除；不会保留「重装后继承旧授权」的路径。

### 依赖与运行时

- `dependencies` 只做检查与提示，不自动安装；依赖可以是插件、工具、技能或 MCP 包目录。
- `runtime` 声明外部运行时（如 `node`、`python`、`java`、`browser`、`git`、`dotnet`、
  `uv`、`deno`、`bun`）。宿主只探测、给安装地址与替代方案，不拦启用。
- 只要清单里有 `pages` 或 `settings.kind = "html"`，体检会自动把 `browser` 算进依赖，
  因为本应用没有内嵌 WebView，所有网页都交给系统浏览器。

---

## 3. `plugin.json`

### 最小清单

```json
{
  "id": "hello_plugin",
  "name": "你好插件",
  "version": "1.0.0",
  "description": "在每轮对话的人设里加一句话。",
  "settings": { "kind": "none" },
  "hooks": { "persona": "hooks/persona.js" }
}
```

`settings.kind` 是硬性字段，必须是 `html`、`native`、`none` 三者之一，且大小写敏感。
缺失或写错时插件仍会出现在列表里，但不会执行任何钩子，体检会报 Error。

### 字段表

| 字段 | 类型 | 必需 | 说明 |
|---|---|---|---|
| `id` | string | 建议 | 安装 id；安装时清洗，装载时以目录名为准 |
| `name` | string | 是 | 显示名；为空不会装载 |
| `version` | string | 否 | 建议三段式 `1.0.0`；不以数字开头会警告 |
| `description` | string | 否 | 插件中心说明；为空会提示 |
| `author` | string | 否 | 作者 |
| `permissions` | string[] | 否 | 能力声明，见第 7 节 |
| `dependencies` | object[] | 否 | `{ "id": "...", "url": "...", "optional": true }` |
| `hooks` | object | 否 | 钩子名 → 插件目录内的 `.js` 相对路径 |
| `settings` | object | 是 | 设置页声明，见第 4 节 |
| `pages` | object[] | 否 | 自带页面，见第 5 节 |
| `overrides` | string[] | 否 | 声明接管的环节；当前只兑现 `persona` |
| `runtime` | string[] 或 string | 否 | 外部运行时声明；单个字符串也接受 |

`plugin.json` 解析允许注释与尾逗号（`ReadCommentHandling.Skip`、`AllowTrailingCommas`）。
`runtime` 用 `OneOrManyConverter`，写成 `"runtime": "node"` 也等价于 `["node"]`。

### `overrides`

- `persona`：当 `persona` 钩子返回非空且用户授予 `pipeline.override` 时，
  清空前面插件已追加的人设段，只保留这一份。
- `reply` / `beforeSend` / `afterReply` / `scene`：当前版本不会兑现。
  文本改写本身就是钩子返回值覆盖，不需要写进 `overrides`；体检会对无效声明给警告。

---

## 4. 设置页

`settings.kind` 三选一：

### `none`

明确表示不需要设置：

```json
"settings": { "kind": "none" }
```

### `native`

由应用内标准表单渲染，字段来自 `settings.fields`：

```json
"settings": {
  "kind": "native",
  "fields": [
    {
      "key": "style",
      "label": "人设风格",
      "type": "select",
      "default": "温柔",
      "help": "会追加到每轮人设里。",
      "options": ["温柔", "克制", "活泼"]
    },
    {
      "key": "enabled",
      "label": "启用",
      "type": "switch",
      "default": "true"
    },
    {
      "key": "limit",
      "label": "每轮上限",
      "type": "number",
      "default": "3"
    },
    {
      "key": "token",
      "label": "接口令牌",
      "type": "secret",
      "default": ""
    }
  ]
}
```

| 字段 | 说明 |
|---|---|
| `key` | 存进 `settings.json` 的键；只能使用字母、数字、`_`、`-`、`.`，不能重复 |
| `label` | 表单标题；为空会警告 |
| `type` | `text` / `number` / `switch` / `select` / `secret`；不认识的类型按 `text` 渲染 |
| `default` | 默认值；统一写成字符串（`"true"`、`"3"`、`""`） |
| `help` | 字段下方说明 |
| `options` | `select` 的选项；为空会报 Error |

`native` 表单保存后立即写 `plugins/<id>/settings.json`，但**正在执行的钩子**读的是进入钩子时的快照；
下一次钩子执行才能看到新值。`secret` 只影响输入框显示（密码掩码），落盘仍是明文，
不要把真正的长期密钥交给不可信插件。

### `html`

插件自带一个网页作为设置页；应用用系统浏览器打开它，并附带回调地址：

```json
"settings": {
  "kind": "html",
  "page": "pages/settings.html",
  "callback": true
}
```

- `page` 可以是插件目录内的相对路径，也可以是 `http(s)://` 地址。
- `callback` 默认 `true`；为 `false` 时页面只读展示，不附带回调地址。
- 页面 URL 会附加：
  - `callback=http://127.0.0.1:<port>/save?token=<token>`
  - `plugin=<id>`
- 回调服务器只监听 `127.0.0.1`，token 在本次进程内有效；token 不符返回 403。
- 页面保存设置走 `POST <callback>`；读取设置走 `GET /settings`；
  读取实时状态走 `GET /runtime`。详见第 9 节。

> `settings.page` 为本地文件时，应用用 `file://` URI 打开；浏览器可能按自身策略限制
> `file://` 页面的跨域请求。若页面需要稳定回调，优先使用本地 HTTP 服务或远程页面。

---

## 5. 自带页面

`pages` 是插件提供给用户打开的页面，可以是说明页、面板或带表单的页面：

```json
"pages": [
  {
    "file": "pages/about.html",
    "title": "说明"
  },
  {
    "route": "https://example.com/panel",
    "title": "在线面板"
  }
]
```

- 本地页面写 `file`，远程页面写 `route`；两者都为空会报 Error。
- 打开时优先用 `file`；`file` 为空才用 `route`。
- 页面用系统浏览器打开，**始终**附带 `callback` 与 `plugin` 参数（不受 `settings.callback` 影响）。
- 打开前要求授予 `page` 能力；插件不能自己弹页面，必须用户点击。
- 本应用不内嵌 WebView；页面不是应用 UI 的一部分，也不能替换主界面。

---

## 6. 钩子

宿主认识的钩子名（大小写不敏感）：

| 钩子 | 执行时机 | `input` 字段 | 返回值 |
|---|---|---|---|
| `persona` | 每轮组装人设时 | `charId`、`character` | 追加到人设；`overrides` 含 `persona` 时可替换前面段落 |
| `context` | 每轮组装动态上下文时 | `charId`、`text`、`reply` | 追加到上下文 |
| `beforeSend` | 用户文本发给模型前 | `charId`、`text`、`reply` | 非空则替换用户文本 |
| `afterReply` | 模型回复写入会话前 | `charId`、`text`、`reply` | 非空则替换回复 |
| `afterTurn` | 一轮结束、记忆/关系网/标签更新后 | `charId`、`note` | 忽略 |
| `onSceneChange` | 场景切换生效后 | `charId`、`scene`、`sceneName` | 忽略 |
| `onToolResult` | 任意工具返回后 | `tool`、`result`（最多 8000 字符） | 忽略 |
| `onLoad` | 插件启用后 | `plugin` | 忽略 |
| `onInstall` | 安装完成后 | `plugin` | 忽略 |
| `onUnload` | 停用或卸载前 | `plugin` | 忽略 |
| `on:<topic>` | 收到 `emit(topic, payload)` 时 | `from`、`topic`、`payload`、`data` | 忽略 |

补充：

- `persona` / `context` 属于 `prompt.write`；`beforeSend` / `afterReply` 属于
  `pipeline.transform`。没有声明也会由钩子本身隐含，但仍受用户授权策略约束。
- `afterTurn` / `onSceneChange` / `onToolResult` 是副作用钩子，不改文本，不额外要求能力。
- `on:<topic>` 是广播监听钩子，发送方与接收方都要有 `broadcast:<topic>`；
  发送方不会收到自己的广播；嵌套广播最多 2 层。
- **已知不一致**：`PluginDoctor.HookNames` 白名单没有包含 `on:` 前缀，
  因此体检会把自定义 `on:<topic>` 报成「钩子名宿主不认识」，但运行时 `emit` 仍能投递。
  看到这条 Error 时，以运行时行为为准；后续版本会统一白名单。
- 所有钩子按插件显示名排序依次执行；`beforeSend` / `afterReply` 的当前文本会传给下一个插件。
- 某个插件抛异常/超时只写插件日志并返回 `null`，不阻断其他插件，也不会带崩主程序。

### 钩子文件

- 必须是插件目录内的相对路径；绝对路径、`..`、目录外路径会被体检拒绝。
- 扩展名建议 `.js`；沙箱只执行 JavaScript。
- 文件不存在、为空、语法错误时，体检会报 Error/Warn；运行时该钩子直接跳过并写日志。
- 不支持 `await`、`require`、`import`、`export`；异步能力要拆成同步宿主调用。

---

## 7. JS API

### 基本约定

每个钩子文件是一段同步脚本，宿主注入两个变量：

| 变量 | 类型 | 说明 |
|---|---|---|
| `input` | string | 上下文 JSON 字符串；必须 `JSON.parse(input)` |
| `result` | string | 初始为空；赋值后作为钩子返回值 |

```js
var ctx = JSON.parse(input);
var style = getSetting("style");
result = style ? "【风格：" + style + "】" : "";
```

返回值：

- `result` 为空、`null`、`undefined` → 钩子返回 `null`，调用方保留原文/不追加。
- 对象/数组建议先 `JSON.stringify`；直接赋对象会走 JS 的 `ToString`，通常不是想要的结果。
- `persona` / `context` / `beforeSend` / `afterReply` 会使用返回值；其余钩子忽略返回值。

### 宿主函数

| 函数 | 签名 | 返回 | 所需能力 |
|---|---|---|---|
| `getSetting` | `getSetting(key)` | string；不存在返回 `""` | `settings` |
| `setSetting` | `setSetting(key, value)` | 无 | `settings` |
| `log` | `log(message)` | 无；写应用日志并带插件前缀 | `log` |
| `now` | `now()` | `yyyy-MM-dd HH:mm` 本地时间 | 无 |
| `callTool` | `callTool(name, argsJson)` | 工具结果字符串；失败返回 `{"error":"..."}` | `tools:<name>` |
| `callPlugin` | `callPlugin(id, hook, argsJson)` | 被调钩子的返回值 | `plugins:<id>` |
| `fetch` | `fetch(url, optionsJson)` | JSON 字符串，见下 | `network:<host>` 或 `localnet` |
| `emit` | `emit(topic, payload)` | `{"ok":true,"delivered":n,"error":""}` | `broadcast:<topic>` |
| `notify` | `notify(title, text)` | `{"ok":true,"error":""}` | `notify` |
| `writeFile` | `writeFile(name, text)` | `{"ok":true,"bytes":n,"error":""}` | `storage` |
| `readFile` | `readFile(name)` | `{"ok":true,"content":"...","error":""}` | `storage` |
| `listFiles` | `listFiles()` | `[{"name","bytes","at"}]` 或错误 JSON | `storage` |
| `deleteFile` | `deleteFile(name)` | `{"ok":true,"error":""}` | `storage` |
| `adjustStat` | `adjustStat(charId, stat, delta)` | `{"ok":true,"error":""}` | `stats:<charId>` |

调用被权限拦截时，函数**不抛异常**，而是返回空值或 `{"error":"..."}`；原因同时写入插件日志与权限留痕。
同一次钩子执行内，同一条「能力:对象」只判定一次，避免每轮重复弹窗。

### `getSetting` / `setSetting`

- 值统一是字符串；布尔用 `"true"` / `"false"`，数字自行 `parseInt` / `parseFloat`。
- `setSetting` 先落盘再更新本次钩子的本地副本；同一钩子里紧接着 `getSetting` 能读到新值。
- `setSetting` 只影响当前插件自己的 `settings.json`，看不到游戏设置或其他插件的设置。

### `log` / `now`

```js
log("本轮开始：" + now());
```

`log` 写入应用日志（`App.WriteLog`），不是 `plugin_logs`；后者只收集崩溃/权限拦截。
不要把用户隐私写进日志，数据包导出会带上应用日志。

### `callTool`

```js
var raw = callTool("send_to_user", JSON.stringify({ text: "晚安" }));
var data = JSON.parse(raw);
if (data.error) log("工具失败：" + data.error);
```

- `argsJson` 必须是 JSON 字符串；具体参数结构由工具定义决定。
- 工具名需要逐条声明：`"permissions": ["tools:send_to_user"]`。
- 工具执行仍受沙箱策略、信任名单与工具自身校验约束。
- 工具返回后会广播 `onToolResult`；插件在 `onToolResult` 里再调工具会被深度保护拦掉。

### `callPlugin`

```js
var reply = callPlugin("mood_echo", "persona", JSON.stringify({ charId: ctx.charId }));
```

- 被调用插件仍按自己的清单与授权执行，不能借调用方越权。
- 参数不是合法 JSON 时按原始字符串传入 `input`。
- 嵌套调用最多 4 层；超过返回错误 JSON。

### `fetch`

```js
var res = fetch("https://api.example.com/v1/quote", JSON.stringify({
  method: "POST",
  headers: { "Content-Type": "application/json", "Authorization": "Bearer " + getSetting("token") },
  body: JSON.stringify({ mood: "calm" })
}));
var data = JSON.parse(res);
if (!data.ok) log("HTTP " + data.status + "：" + data.error);
else log(data.body);
```

返回：

```json
{
  "ok": true,
  "status": 200,
  "body": "响应文本",
  "truncated": false,
  "location": "",
  "error": ""
}
```

限制：

- 只允许 `http` / `https`；URL 最长 2000 字符。
- 方法：`GET` / `POST` / `PUT` / `PATCH` / `DELETE` / `HEAD`。
- 不自动跟随重定向；`3xx` 的 `location` 会放在返回值里，需要自己决定是否再请求。
- 请求体上限 64KB；响应最多读取 512KB，超出部分丢弃并置 `truncated=true`。
- HTTP 超时 10 秒；连接超时 6 秒。
- `Host`、`Content-Length`、`Transfer-Encoding`、`Connection`、`Upgrade`、`Keep-Alive`、
  `TE`、`Trailer` 与 `access-control-*` 头会被忽略。
- 访问 `localhost`、`.local`、`.internal`、回环、私网、链路本地、组播地址需要 `localnet`；
  域名解析后落到私网也算本机访问。
- 访问外网需要 `network:<host>`，逐主机声明；`network` 声明为空或 `*` 表示任意主机，
  但仍需要用户在权限页放行。

### `emit`

```js
var r = emit("turn.done", JSON.stringify({ charId: ctx.charId, note: ctx.note }));
log("投递 " + r.delivered + " 个插件");
```

- 话题名 1–64 字符；载荷上限 64KB。
- 只投递给启用了 `on:<topic>` 钩子的其他插件；接收方也要有 `broadcast:<topic>`。
- 发送方不会收到自己的广播；嵌套最多 2 层。
- 返回 `delivered` 是成功投递的插件数。

### `notify`

```js
notify("心情便签", "今天已经记录了 3 次。");
```

用户可见提示，需要 `notify` 能力。标题/文本会 trim；主程序没有接上通知通道时返回错误 JSON。

### 文件区

```js
writeFile("notes.json", JSON.stringify({ at: now(), text: "..." }));
var all = listFiles();
var note = readFile("notes.json");
deleteFile("old.json");
```

- 只接受纯文件名：不能带路径分隔符、盘符、`..`；长度不超过 120。
- 单文件最多 1MB；整个 `files/` 最多 8MB；只写 UTF-8 文本。
- `readFile` 文件不存在返回 `{"ok":false,"content":"","error":""}`。
- `listFiles` 返回数组（或权限错误 JSON），每项含 `name`、`bytes`、`at`。
- 路径越出 `plugins/<id>/files/` 一律拒绝。

### `adjustStat`

```js
var r = adjustStat(ctx.charId, "affection", 3);
if (r.error) log(r.error);
```

- `stat` 接受 `affection` / `好感` / `好感度`、`trust` / `信任` / `信任度`、
  `energy` / `精力` / `精力值`。
- 好感/信任夹在 `0..100`；精力修正夹在 `-60..40`。
- 走与主流程同一套落盘与角色状态更新；未知角色或写入失败返回错误 JSON。
- 按角色 id 逐项授权：`"permissions": ["stats:mom"]`。

---

## 8. 权限模型

权限按「声明 ∩ 授予」判定：

1. **声明**：`plugin.json` 的 `permissions` 必须写清楚要用什么；没声明就没有。
2. **授予**：用户在权限页设置 `允许` / `问一次` / `禁止`；`问一次` 会真的弹窗。
3. **对象**：工具名、插件 id、主机名、话题名、角色 id 逐项授权，不是一句「准许用工具」。

### 风险档

| 档位 | 含义 | 默认策略 |
|---|---|---|
| `Own` | 只动自己的设置、文件、日志 | 允许，可撤销 |
| `Pipeline` | 动主程序的提示词/回复管线 | 多数允许，整段接管默认询问 |
| `Interact` | 调工具、别的插件、广播、通知、改数值、开页面 | 多数询问，页面默认允许 |
| `External` | 出本机或访问本机/内网 | 默认禁止，必须逐条声明并放行 |

### 14 项能力

| 能力 | 标题 | 档位 | 默认 | 对象 | 说明 |
|---|---|---|---|---|---|
| `settings` | 自己的设置 | Own | 允许 | — | 读写自己的 `settings.json` |
| `storage` | 自己的文件区 | Own | 允许 | — | `plugins/<id>/files/`，单文件 1MB / 合计 8MB |
| `log` | 写应用日志 | Own | 允许 | — | 带插件前缀 |
| `prompt.write` | 注入人设与上下文 | Pipeline | 允许 | — | `persona` / `context` |
| `pipeline.transform` | 改写对话文本 | Pipeline | 允许 | — | `beforeSend` / `afterReply` |
| `pipeline.override` | 整段接管管线 | Pipeline | 询问 | — | 当前只兑现 `persona` |
| `tools` | 调用工具 | Interact | 询问 | 工具名 | `callTool` |
| `plugins` | 调用其它插件 | Interact | 询问 | 插件 id | `callPlugin` |
| `broadcast` | 广播与收听 | Interact | 询问 | 话题名 | `emit` / `on:<topic>` |
| `notify` | 发用户通知 | Interact | 询问 | — | `notify` |
| `stats` | 调整角色数值 | Interact | 询问 | 角色 id | `adjustStat` |
| `page` | 打开自带页面 | Interact | 允许 | — | `pages` |
| `network` | 访问外部网络 | External | 禁止 | 主机名 | `fetch` 外网 |
| `localnet` | 访问本机与内网 | External | 禁止 | — | `fetch` 回环/私网 |

### 声明写法

```json
"permissions": [
  "notify",
  "tools:send_to_user",
  "plugins:mood_echo",
  "broadcast:turn.done",
  "stats:mom",
  "network:api.example.com",
  "localnet",
  "pipeline.override"
]
```

规则：

- `能力:对象` 用冒号分隔；对象为 `*` 或省略表示任意对象。
- `Own` 档（`settings` / `storage` / `log`）不要求声明，默认放行，但仍可在权限页禁止。
- `prompt.write` / `pipeline.transform` 可由 `persona` / `context` / `beforeSend` / `afterReply`
  钩子隐含，不必重复写。
- `pipeline.override` 由 `overrides` 含 `persona` 隐含。
- `page` 由 `pages` 非空隐含。
- **旧插件兼容**：`permissions` 为空时，除 `External` 外都视为已声明，按默认策略继续；
  `network` / `localnet` 仍必须显式声明。
- 未知能力名按最高风险处理，默认拒绝。

### 授权弹窗

「问一次」第一次撞上时弹：

- **总是允许**：写入 `permissions.json`，以后同能力同对象直接放行。
- **仅这一次**：只活到本次进程结束，不落盘。
- **这次先不给**：本次进程内同能力同对象不再询问，也不落盘。

权限页提供逐项 `禁止` / `问一次` / `允许`。刻意没有「一键全部允许」：
那等于把整套机制退回一刀切；真想全部关掉，停用插件就是那个开关。

### 存档白名单

每份存档的 `state.permissions` 另有一层开关：

- `AllowAllPlugins`（默认 `true`）：这份存档是否放行所有插件。
- `AllowedPlugins`：关闭全量放行后，只允许列出的插件 id。
- 对应的工具白名单为 `AllowAllTools` / `AllowedTools`。

插件在钩子执行前检查存档白名单；不在名单内时跳过该钩子并写插件日志。
白名单在「存档规则」页修改，换档就换一套。

---

## 9. HTML 设置回调

打开 `settings.kind = "html"` 的设置页时，应用会为这个插件启动一个只监听
`127.0.0.1` 的 `HttpListener`，并把回调地址注入页面 URL。

### 页面拿到的参数

```text
file:///.../pages/settings.html
  ?callback=http%3A%2F%2F127.0.0.1%3A51234%2Fsave%3Ftoken%3Dabc123
  &plugin=hello_plugin
```

`callback` 已经带上 token；页面直接把它当作保存地址即可。

### 端点

| 方法 | 路径 | 作用 |
|---|---|---|
| `POST` | `/save?token=...` | 写入设置；body 可以是 `{"key":"value"}` 或 `{"settings":{"key":"value"}}` |
| `GET` | `/settings?token=...` | 读取全部设置（字符串字典） |
| `GET` | `/runtime?token=...` | 读取实时状态；没有状态时返回 `{"ok":false,"host":"...","error":"..."}` |
| `OPTIONS` | 任意 | CORS 预检，返回 204 |

响应：

- token 缺失/不匹配：403。
- `POST /save` 时 `settings` 能力被禁止：403。
- 保存成功：`{"ok":true,"saved":n}`。
- `GET /settings`：`{"key":"value", ...}`。
- `GET /runtime`：宿主把 `RuntimeProvider` 返回的 JSON 对象加上 `host` 字段；
  无法解析或没有实时状态时返回 `{"ok":false,"host":"...","error":"..."}`。

### 页面示例

```html
<!doctype html>
<meta charset="utf-8">
<label>风格 <input id="style"></label>
<button id="save">保存</button>
<pre id="status"></pre>
<script>
  const params = new URLSearchParams(location.search);
  const callback = params.get("callback");
  const settingsUrl = callback.replace("/save", "/settings");
  const runtimeUrl = callback.replace("/save", "/runtime");

  async function load() {
    const res = await fetch(settingsUrl);
    const data = await res.json();
    document.getElementById("style").value = data.style || "";
  }

  async function save() {
    const style = document.getElementById("style").value;
    const res = await fetch(callback, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ settings: { style } })
    });
    document.getElementById("status").textContent = JSON.stringify(await res.json());
  }

  document.getElementById("save").addEventListener("click", save);
  load();
</script>
```

注意事项：

- token 等同于这个插件的设置写权限，不要写进日志、截图或第三方请求。
- 回调服务器只监听回环地址；其他本机进程如果拿到 token 也能写，因此 token 不要外泄。
- 应用重启后旧页面里的 token 失效，需要从插件中心重新打开。
- `GET /runtime` 只读、不缓存、不落盘；实时值由主程序注入，页面无法伪造来源。

---

## 10. 沙箱与限制

### JS 执行

| 项目 | 限制 |
|---|---|
| 引擎 | Jint，进程内同步执行 |
| 语句数 | 20,000 |
| 内存 | 16MB |
| 默认时间预算 | 300ms |
| 声明 `network` / `localnet` | 12s |
| 声明 `tools` / `plugins` / `broadcast` | 5s |
| 等待用户授权 | 不计入时间预算 |
| 看门狗 | 每 25ms 检查一次 |

- 不支持 `await`、`require`、`import`、`export`、模块系统、DOM、Node API。
- 没有文件系统、反射、进程、环境变量入口；所有外部能力必须走宿主函数。
- `eval` / `new Function` 能运行，但体检会警告：字符串代码对用户不可见，权限也判不准。
- 超时/超语句/超内存会中断钩子，返回 `null`，并写插件日志。
- 钩子在线程池执行，`callTool` / `callPlugin` / `fetch` 同步等待；不要把长任务放 `persona`
  这种每轮都跑的钩子里。

### 网络

- 不跟随重定向；只允许 `http` / `https`。
- 请求体 64KB、响应 512KB、HTTP 超时 10s、连接超时 6s。
- 外网逐主机声明；本机/内网需要 `localnet`。
- 域名解析到私网也按本机访问处理，防止用 `network` 绕过 `localnet`。
- 不提供 cookie 容器、代理配置或客户端证书。

### 文件

- 只能访问 `plugins/<id>/files/`。
- 纯文件名、UTF-8 文本、单文件 1MB、合计 8MB。
- 不能读存档、图片、设置、其他插件目录。

### 设置

- 只能读写自己的 `settings.json`。
- 值是字符串；没有类型 schema 强校验（`native` 的字段定义只用于生成表单）。
- HTML 设置页通过回调写入同样受 `settings` 能力约束。

### 页面

- 系统浏览器打开，不是内嵌 WebView。
- 页面可以访问网络，但那是浏览器自己的能力，不受插件 `network` 权限约束；
  插件权限只约束 JS 沙箱里的 `fetch`。
- 页面拿到的 token 只对当前插件的设置回调有效。

---

## 11. 体检、试跑与日志

### 体检

插件中心对每个插件提供「体检」，只读文件、只解析 JS，不执行代码。检查项：

| 分类 | 规则 |
|---|---|
| 身份 | 目录名与 `id` 不一致、缺 `name`、缺 `description`、版本号不以数字开头 |
| 设置 | `kind` 非三选一；`html` 缺 `page` 或文件不存在；`native` 无字段；字段 key 重复/非法；`type` 不认识；`select` 无 `options` |
| 页面 | `file` / `route` 都为空；本地文件不存在；`route` 不是 http(s)；缺 `title` |
| 依赖 | 依赖缺 `id`、重复、依赖自己、缺必选依赖、可选依赖未装 |
| overrides | 声明了当前不会兑现的接管目标 |
| runtime | 未安装、未知运行时；给出安装地址/替代路线 |
| 钩子 | 名称不认识、文件为空/不存在/越界/非 `.js`；JS 语法错误；`require`/`import`；`eval` 警告；值钩子没给 `result`；用了未声明能力；`getSetting` 键不在 `native` 字段里；每轮钩子里塞询问型能力 |

体检级别：

- **Error**：按当前清单插件不能正常工作，必须修。
- **Warn**：能跑，但行为可能不符合预期。
- **Info**：提示性信息，例如页面由系统浏览器打开、运行时已安装。

### 试跑

插件中心的「试跑」会取该插件的第一个钩子，用测试上下文执行一次：

```json
{
  "charId": "test",
  "character": "test",
  "text": "这是一次测试。",
  "reply": "这是一次测试。"
}
```

返回非空时显示返回值；为空时说明「只执行了钩子，没有返回文本」。
试跑不替代真实对话，但能快速确认脚本语法、设置读取与权限路径。

### 日志

- `plugin_logs/<id>.log`：钩子异常、权限拦截、JS 超时、广播投递失败。
- 应用日志（`App.WriteLog`）：`log()` 输出、安装/加载错误、回调服务器错误。
- 插件中心可以展开/清空单个插件的日志。
- 日志开关关闭后不再写 `plugin_logs`；已经写下的内容不会自动删除。

### 常见问题

| 现象 | 排查 |
|---|---|
| 插件不在列表里 | 目录下没有 `plugin.json`；`name` 为空；JSON 解析失败（看应用日志） |
| 插件在列表但不能跑 | `settings.kind` 缺失/写错；体检 Error 未修 |
| 钩子不执行 | 插件停用；全局插件钩子关闭；存档白名单未放行；钩子名不在上表；文件不存在；权限被禁止 |
| 设置页没有保存 | `html` 页面没拿到 `callback`；token 失效；`settings` 被禁止；`native` 字段 key 非法 |
| `fetch` 总是失败 | 没声明 `network:<host>` / `localnet`；URL 不是 http(s)；超时；响应被截断 |
| JS 超时 | 死循环；在 `persona` 里做网络/工具调用；没有声明对应能力导致预算仍按 300ms |
| 页面打不开 | 没授予 `page`；本地文件不存在；`route` 不是 http(s)；系统没有默认浏览器 |
| 体检说 `on:topic` 不认识 | 体检白名单未包含自定义广播钩子；运行时仍可通过 `emit` 投递 |
| 依赖一直报缺 | 依赖不会自动安装；把被依赖的插件/工具/技能/MCP 包放到对应目录 |

---

## 12. 完整示例

目录：

```text
plugins/mood_note/
├── plugin.json
├── hooks/
│   ├── persona.js
│   └── afterTurn.js
└── pages/
    └── about.html
```

`plugin.json`：

```json
{
  "id": "mood_note",
  "name": "心情便签",
  "version": "1.0.0",
  "description": "在每轮人设里加一句当前风格，并在十轮后提醒一次。",
  "author": "example",
  "permissions": ["notify"],
  "dependencies": [],
  "hooks": {
    "persona": "hooks/persona.js",
    "afterTurn": "hooks/afterTurn.js"
  },
  "settings": {
    "kind": "native",
    "fields": [
      {
        "key": "style",
        "label": "人设风格",
        "type": "select",
        "default": "温柔",
        "options": ["温柔", "克制", "活泼"]
      },
      {
        "key": "remindEvery",
        "label": "每多少轮提醒",
        "type": "number",
        "default": "10"
      }
    ]
  },
  "pages": [
    { "file": "pages/about.html", "title": "说明" }
  ],
  "overrides": [],
  "runtime": ["browser"]
}
```

`hooks/persona.js`：

```js
var style = getSetting("style");
if (style) {
  result = "【表达风格】" + style + "；不要解释这条标记。";
}
```

`hooks/afterTurn.js`：

```js
var ctx = JSON.parse(input);
var every = parseInt(getSetting("remindEvery") || "10", 10);
if (every < 1) every = 10;

var count = parseInt(getSetting("turns") || "0", 10) + 1;
setSetting("turns", String(count));

if (count % every === 0) {
  notify("心情便签", "已经记录了 " + count + " 轮。");
}
```

`pages/about.html`：

```html
<!doctype html>
<meta charset="utf-8">
<h1>心情便签</h1>
<p>人设钩子会追加一条表达风格；回合钩子每十轮提醒一次。</p>
<p>这个页面由系统浏览器打开，不嵌在应用里。</p>
```

说明：

- `persona` 返回值会进入人设；没有声明 `prompt.write`，由钩子隐含。
- `afterTurn` 只做副作用；`notify` 需要 `notify` 能力，第一次会弹授权询问。
- `turns` 不需要在 `settings.fields` 里声明，`setSetting` 仍可写入；
  但体检会对 `getSetting("turns")` 报「字段不存在」的警告。要消除警告，
  可以把 `turns` 加成一个只读/隐藏字段，或接受这条提示。
- `pages` 会自动把 `browser` 算进运行时检查；没有默认浏览器时页面按钮会失败。

---

## 13. 发布前检查清单

- [ ] `plugin.json` 能被 JSON 解析，`name` 非空，`settings.kind` 是 `html` / `native` / `none`。
- [ ] 所有钩子文件在插件目录内，扩展名 `.js`，没有 `require` / `import` / `await`。
- [ ] 值钩子（`persona` / `context` / `beforeSend` / `afterReply`）给 `result` 赋值。
- [ ] 用到的 `callTool` / `callPlugin` / `emit` / `fetch` / `notify` / `adjustStat`
      都在 `permissions` 里逐项声明。
- [ ] `native` 字段的 key 与 JS 里的 `getSetting` 一致；`select` 有 `options`。
- [ ] `html` 设置页能处理 `callback` 参数；token 不写日志。
- [ ] `pages` 的本地文件存在，或 `route` 是 http(s)。
- [ ] 依赖与运行时写清楚；没有把「需要浏览器」当成「应用内会显示网页」。
- [ ] 插件体检没有 Error；试跑能看到预期返回值。
- [ ] 卸载后不依赖残留文件；重装不会继承旧授权。
