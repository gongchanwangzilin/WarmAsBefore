# 踩坑台账（Web 迁移 + 宿主）

这份文件只记账：每一条都对应历史里一次真实的「修 / 回退 / 假 / 坏 / 挂」，并注明现在还在不在。
状态含义：**已修**（确认修好且未退化）、**又退回去了**（修过又被后面某次重写抹掉）、**仍在**（从来没修完）、
**潜伏**（现在没爆，但触发条件已经写在代码里）、**未验证**（没证据，不敢下结论）。

「证据」一列是可复现的检查，或者当前代码里的那一行。

| # | 坑 | 出处 | 当初的修法 | 现状 | 证据 |
| --- | --- | --- | --- | --- | --- |
| 1 | 原生 `<select>` 弹出层是系统窗口，不受页面布局约束，116 条选项会甩到应用窗口外 | 2d2b967 | `kit.Select` 改自绘 `button` + `role=listbox`，下方放不下就上翻 | **又退回去了 → 本轮已修** | 修前 `grep -rn "<select" src --include=*.tsx` 命中 4 处（`pages/AppearancePage.tsx:220`、`screens/section/settingssection.tsx:601`、`screens/save-chat/qqsettings.tsx:126/153`）；修后 0 处 |
| 2 | 类名写了但 CSS 里没有规则 → 元素按原始尺寸渲染，1500px 立绘撑爆界面 | 38a1422 · 44f97d1 · 5585759 | `.mg__sprite` 用 `position:absolute; inset:0; width/height:100%; object-fit:contain`，禁用百分比高度 | 已修 | `screens/main/main.css:1539` 起；`main.tsx:438` 使用该类。脚本比对 634 个 className / 685 个 CSS 选择器 |
| 3 | 同类坑再犯：**选角色整页**的 `.mc__*` 样式被一次 main.css 重写整段删掉 | 7de2100 | —（当时没发现） | **又退回去了 → 本轮已修** | `git show 7de2100 -- web/src/screens/main/main.css \| grep -c "^-.*\.mc__"` = 27，`+` = 0；修前 `grep -n "\.mc" main.css` = 0，而 `select.tsx` 仍在用 25 个 `mc__*` 类。本轮新建 `screens/main/select.css` 并在页面里 import |
| 4 | 用了**从未存在**的按钮修饰类，主行动按钮于是没有主色 | 5d6c3dc（同类：WBorderBrush/WWelBrush 从未存在） | 改成真存在的令牌/类 | **仍在 → 本轮已修** | `m-btn--primary`（`main/setup.tsx` ×3）、`m-btn--soft`（`main/select.tsx` ×2）在 main.css 无定义 → 改为 `--accent` / 去掉修饰 |
| 5 | 用了**未定义**的间距令牌，整条 padding 静默变 0 | 5d6c3dc 同类 | — | **仍在 → 本轮已修** | `character/viewer.css:59` 写 `var(--w-space-5)`，全仓只有 `--w-step-5`（`styles/kit.css:35`）。已换 |
| 6 | 回退键两个（页头一颗 + 补位一颗）或一个都没有 | e7e70e4 · 5585759 · b70f3dc · bef8443 | `BackChip` 占位不压字；`MutationObserver` 全程重判（页面懒加载）；自绘返回加 `data-back` | 已修 | `components/BackChip.tsx:35-64`；`games/pet.tsx:27,78`、`games/map.tsx:391` 有 `data-back`；`kit.tsx:51` 的 `hd__back` 也带 |
| 7 | 全站假成功：没调用后端就说「已保存」 | 7de2100「全站假成功清理」 | 成功文案只在 `await` 真返回后写；失败把宿主那句上屏 | 已修 | 「已保存」全仓 4 处，都在 `bridge.request(...)` 之后；`settings-ai/saveRules.tsx:131-137` 有 catch 分支 |
| 8 | 宿主回裸对象、前端按 `{ appearance }` 取 → 永远判成「没下发」走空态 | e7e70e4 | C# 侧包一层 `new { appearance = ... }` | 已修 | `Services/Web/WebBridge.cs` `AppearanceGet`；`lib/useAppearance.ts:63`；`bridge/protocol.ts:182` |
| 9 | 前端事件通道还没挂好时，宿主发的事件直接丢（启动提示就是这么丢的） | 2d2b967 | 未就绪入 `Pending`，`ui.ready` 时 `FlushPending` | 已修 | `WebBridge.cs:341-347`、`:859`；前端 `main.tsx:104` 发 `ui.ready` |
| 10 | 构建产物入库（`binVerifyBuild 2/`，497 个文件） | 7de2100 | — | **仍在** | `git ls-files \| grep -c "^binVerifyBuild"` = 497；`git check-ignore "binVerifyBuild 2/Acornima.dll"` 退出码 1（未被忽略）。**待办**：`.gitignore` 加 `binVerifyBuild*/` |
| 11 | 复刻期的临时脚本/冒烟产物入库 | e7e70e4 加 `.audit.js/.audit2.js`；9cbbfad 清 `web/tmp` | 从树里删掉 | 基本已修，剩一个空文件 | `git ls-files web \| grep -v src` 只剩 `web/.gitignore` 和 `web/routes_tmp.txt`（0 字节）。**待办**：删 `routes_tmp.txt` |
| 12 | 胶囊按钮回潮（圆角 22px / 999px 胶囊 vs 全站切角合同） | 4a1cad2「恢复原圆角（胶囊收掉）」+ DESIGN-ENTRY §5 | 按钮一律 `border-radius:0` + 右上 7px 切角 | 潜伏 | 历史 `.mc__foot .m-btn` 是 `border-radius:22px`。本轮恢复该页样式时按切角重写，没有复刻胶囊；`main.css:5-25` 现为 `clip-path` 切角 |
| 13 | 硬编码颜色（`#hex` / `rgb()`）绕过令牌 | CONVENTIONS 硬规则 1 | 只写 `var(--w-*)` 与墨阶 | 已守 | `grep -rnE "'#[0-9a-fA-F]{3,8}'" src --include=*.tsx` = 0；页面 css 里 0 处 `rgba(`，唯一一处在 `select.css` 的 `var(--w-shadow-float, …)` 兜底值里（与 `kit.css:768` 同一写法） |
| 14 | 六档字号必须与行高成对，不许出现字面行高 | kit.css 文件头合同（2c801d3 立） | 每条 `font-size` 旁边配 `--w-lh-*` | **仍在** | 字面行高 31 处，集中在 `main.css`、`savechat.css`。**待办**：逐条换令牌 |
| 15 | 实现改了、注释和旧选择器没跟着改 | 2d2b967 | — | **仍在** | `components/kit.tsx:350` 注释仍写「下拉（Picker）：原生 select」；`styles/kit.css:532` 还留着 `.k-select select`。**待办**：kit.tsx / kit.css |
| 16 | `.picker` 在本轮之后成为死 CSS | 本轮（改掉最后两个调用方） | — | **仍在**（死代码） | `grep -rn 'className="picker"' src` = 0；`styles/app.css:568,582` 仍有规则。**待办**：app.css |
| 17 | `BackChip` 用 `[class*="back" i]` 判「页面自己画了回退」→ 任何含 `back` 的类名都会让它永远消失，整页没有退出口 | e7e70e4 | — | 潜伏 | `BackChip.tsx:47`。当前命中 `mg__backend`/`mc__back`/`qw-top__act--back`，都真是回退；只要有人加 `.xxx__background` 就静默失效。**待办**：BackChip（收窄成白名单） |
| 18 | QV 跨档串会话、读档时顺手写盘 | 2c801d3 | 只读当前档 | 未验证 | `screens/save-chat/qvclient.tsx` 正在被别人改，不动 |
| 19 | 相机预览起停竞态、窗口隐身不停 | 38a03e9 | 显式开关 + 隐身即停 | 未验证 | 属于 C# 相机服务，本轮未读 |
| 20 | 3D 层建链原生崩溃（DXGI_ERROR_INVALID_CALL / stowed exception） | 8d4445e · 059f20d · 31f731e · 4f3cfcf | 不在尺寸事件里重入建链，改「一次机会制」；后来又停用整层 | 未验证 | 同上。要确认得跑起应用看监测页 |
| 21 | 元素级 `DragOver/Drop` 收不到资源管理器拖入，必须走 HWND 级消息 | 7ca72ec · 7df9352 | 换 `WM_DROPFILES` | 已修 | `grep -rl WM_DROPFILES --include=*.cs` → `App.xaml.cs` |
| 22 | XAML 里写了从未存在的画笔令牌（WBorderBrush / WWelBrush） | 5d6c3dc | 换成真令牌 | 已修 | 全仓 `grep WBorderBrush\|WWelBrush` = 0 命中 |
| 23 | 宣传片/媒体文件被提交进仓库 | 340d011 · eaedd2f · e5a3766 · ee0c0d1 | 移出仓库 + `.gitignore` | 已修 | `git ls-files \| grep -iE "\.(mp4\|mov\|webm\|wav\|mp3)$"` = 0；`.gitignore:409` 有 `*.mp4` |
| 24 | `web/dist` 被打进仓库 | aa08bc0 前后 | `.gitignore` 排除 | 已修 | `git ls-files web/dist` = 0；`.gitignore:370,400` |
| 25 | 空 `catch` 把异常吞掉、`console.log` 残留 | c09ad38（263 处空 catch 归类，13 处补日志，插件钩子改 fail-closed） | 分类 + 补日志 + fail-closed | 已修 | `grep -rn "catch {}\|catch (..) {}"` = 0；`console.(log\|debug)` = 0 |

## 统计

- 共 **25** 条。已修/已守 **15** 条，其中本轮修掉 **4** 条（#1 #3 #4 #5）。
- **仍存在 5** 条（#10 #11 #14 #15 #16），**潜伏 2** 条（#12 #17），**未验证 3** 条（#18 #19 #20）。
- 可复现的机械检查（推荐放进 CI）：
  `grep -rn "<select" web/src --include=*.tsx`、`grep -rnE "#[0-9a-fA-F]{3,8}(')" web/src --include=*.tsx`、
  `git ls-files | grep -c "^binVerifyBuild"`、以及「className 有、CSS 无」的集合比对（#2/#3 的成因）。

## 最危险的三条

1. **#2/#3 类丢样式。** 它无声：页面照样渲染、tsc 照样 0 错误，只是元素全裸。7de2100 一次重写就删掉了整页 25 个类而没人发现，说明「重写 css」是这类坑的高发动作。守门办法只有集合比对，不是人眼。
2. **#1 原生控件。** 只要有人新写一行 `<select>`，默认体验就退到系统弹出层；心理状况那 116 条已经证明它在 WebView2 里会跑到窗口外。本轮清到 0，但没有机制阻止下一次。
3. **#10 构建产物入库。** 497 个文件已经在树里，此后每次 `git status` 都被淹，且 `.gitignore` 现在挡不住它。它还会持续污染 diff 与仓库体积，越晚越好清理。
