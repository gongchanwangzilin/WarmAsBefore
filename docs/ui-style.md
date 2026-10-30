# 温暖如初 · UI 风格规约（ui-style v2）

> 本规约 = 「温暖编辑风」的成文版。来源：设置台重设计 v1（2026-10-30）拍板。
> 一句话：**角色库那一页就是标准**——把它写成规则，此后所有页面照办。

## 0. 唯一样式源

- 运行时颜色/字号/圆角/内距一律 `{DynamicResource}`（StyleWorld 每档下发），禁止手写色值。
- `DesignSystem/Tokens/DesignTokens.cs` 静态常量只供 C# 侧使用；XAML 不引它。
- 眉标/行/卡这套结构键在 `DesignSystem/Styles/`：`Eyebrow`（TextTypography）、`CardSolid/HSepBleed`（CardShapes）、`BtnPrimary*/BtnGhost*/BtnText*`（ButtonShapes）、隐式 `Switch/Slider/Picker` 暖皮（BaseStyles）。
- 页内 Resources 只允许放「BindableLayout 模板内引用」的那一部分（VirtualView 解析层级限制），其余一律进系统字典。

## 1. 页面骨架

1. 顶栏：`WBarBrush` 井 + 发丝线（`HSep`）分界，不放描边盒；左返回、中 `// EYEBROW + 标题`、右主行动钮。
2. 内容列上限：单列阅读流 `WCardWidth`；双栏编辑（预设页等）`WCardWidthWide`。横竖 Center 收口，消灭"卡片顶满全宽"。
3. ≥1280 宽才允许双栏；手机单列。
4. 页面留白用 `WPageMargin / WPadCard / WPadRow / WPadWell`，不准手写 `0,13` 这类裸数。

## 2. 设置行

1. 一行只做一件事：标题（`TxtRowTitle`，加粗正文号）+ 说明（`TxtRowDesc`）左，控件右。
2. 控件右列定宽 280；滑杆 150–260 + 右侧读数 64；说明 ≤2 行（`LineBreakMode="WordWrap"` + `MaxLines="2"`），超长说明移入折叠或悬停。
3. 分隔线 `HSepBleed`（行内）/ `WSep`（卡底），行内距 `WPadRow`。
4. 选项 2–4 个的场合用 SegBar 分段，不用 Picker。

## 3. 控件

- Switch/Slider/Picker 允许用原生件（已全局暖皮），但**必须**右列定宽摆齐。
- 所有可点元素挂 `motion:PressFeel/HoverFeel`。
- 图块（tile）：`CardSolid` + Eyebrow 码 + 标题 + 一句摘要 + `›`；hover 抬起；两列网格。

## 4. 横幅（进度通知）

- 统一走 `BannerService`（成就风：图标框金描边 + 眉标 + 标题 + 一句话）。
- 方向：上浮现 / 下浮现 / 四角滑出；**必须感知按键栏**：有顶栏时从顶栏下方出，页面有底输入栏时抬高避让，禁止盖住任何可点条。

## 5. 收尾自检（每页提交前）

1. 深色档 + 柔和极简世界各跑一遍，截图留档。
2. 全宽卡、裸 padding、手写色、默认灰控件 → 一项都不许剩。
3. 窗口拉到 1024 与缩到 320 内容不挤不散。
