# 温暖如初 · UI 标准书（Design Standard v1）

> 本书的地位：**成文的设计决策**。颜色、字号、圆角的"值"由 StyleWorld 按世界下放，本书负责把它们**用在哪里、用几档、配额多少**写死。
> 做页面的流程从此只有三条路：
> 1. 新页面 → 直接套第 4 章的配方（R1–R9），不许从零发明结构；
> 2. 元素拿不准 → 查第 5 章选型表，表里没有的组合禁止出现；
> 3. 页面觉得丑 → 查第 8 章黑名单，先对号入座再动手改。
>
> 与 [`ui-style.md`](ui-style.md) 的关系：v2 成文规约的技术细节（BannerService 避让、页内 Resources 限制等）继续有效；**设计判断冲突时，以本书为准**。修改标准必须先改书、再改代码——代码里的"已经是这样写的"不构成对标准的修改。

---

## 第 1 章 · 设计宪法（七条原则）

每条都写"违反时的症状"，因为丑是有表型的，对症能找到违反的那一条。

### 1. 一屏只有一个响

饱和色（`Signal` 令牌算出的唯一强调色）是**稀缺资源**，每屏配额恰好 1。暖金 `BtnPrimary`（`AccentWarm`）是品牌色，只用于**卡片内部的小步确认**，不与 Signal 同屏竞争。
**症状**：一屏里有两颗东西在喊，你不知道先点哪个；或者喊声是三行碎碎念里的一个红字。

### 2. 深度只有三档

凹（`WellBg`/`CardNeumorphInset`）— 平（`CardSolid`）— 浮（`CardNeumorph`/`CardDialog`）。任何界面元素必须能归入其中一档；归不进去的元素先问是不是该删。
**配额**：一屏浮阶 ≤ 1（此刻的焦点卡），对话框例外（它浮在一切之上）。
**症状**：画面"满"但没有焦点——每一块都在浮，读者眼睛没地方落。

### 3. 层次靠台阶，不靠装饰

分出先后的合法手段只有五个：**字重、墨阶、底色台阶、留白、位置**。影子只属于浮阶与对话框；描边只属于缝。
**症状**：加粗没用就换成彩色，彩色没用就加阴影，阴影没用就加渐变——三层装饰上去之后还是不显眼，因为台阶本身没搭。

### 4. 对比是算出来的

填充色（`*Tint`、`BeigeAccent*`、`WellBg`）**永远只当填充**，上面压的永远是墨 (`WarmDark`/`SurfaceFg`/`MutedFg`/语义 Ink)。没有例外。
**症状**：红字看着发灰、徽章上的字像没印上——那是把淡彩当文字用了（RedSoft 上摆白字 2.1:1 的老案子）。

### 5. 一卡一事，一行一事

一张卡只有一个主张；一行只说一件事情（标题+说明+右控件是一件事的三个部分）。
**症状**：大卡里塞三个按钮几十行字，读起来是"一摞牌子"；或者说明行换行四行，把行撑散。

### 6. 留白先于元素

退一档比加一档优先：降级顺序 = 整块删除 → 降为 `MutedFg`+`TxtCaption` → 收进折叠/悬停。**减法永远先于装饰。**
**症状**：页面改不动了就"再放一个卡片框住它"。

### 7. 唯一世界成立（档案室）

**只有一个世界**：档案室 = 终末地 HUD 的排版骨架（近直角、细描边、收紧字阶、干脆动效）+ 玻璃透材质 + 拟态软阴影 + 砂纸/纸本的大气层。其余四套旧世界（轻质感 / 柔和极简 / 柔和渐变 / 液态玻璃）已**收编删除**，不再作为选项存在。

每个设计必须在**档案室的明/暗两档 × 五套色板**下都成立：
- 明档：暖纸地 + 玻璃卡面 + 黄铜信号；
- 暗档：深灰台面 + 透光卡面 + 同一颗黄铜信号。

**症状**：只在某个色板下好看、换个色板就散——那是把色相写死进了形状；只在明档好看、暗档发灰——那是把纸的白当成了唯一底色。
**禁令**：禁止再写「换世界」逻辑（世界枚举仅为旧存档兼容保留，材质配方只有一份）；新设计不许依赖任何一档旧世界的性格。

---

## 第 2 章 · 度量衡

所有数值由 StyleWorld 按世界下放，这里记的是**档位与用途**。禁止在 XAML 里写任何裸色值、裸字号、裸圆角。

### 2.1 字阶（六档一台，没有第七档）

| 样式键 | 用途 | 颜色来源 | 用法禁止 |
|---|---|---|---|
| `TxtDisplay` | 页面最大的一行（欢迎语、大标题、数字强调） | `WarmDark` | 一屏最多 1 处 |
| `TxtTitle` | 页面标题、空态大字 | `WarmDark` | — |
| `TxtSubhead` | 卡内小节标题 | `SurfaceFg` | 必须加粗（已成文），且其下必有正文 |
| `TxtBody` / `TxtRowTitle` | 正文 / 行主语 | `SurfaceFg` / `WarmDark` | `TxtRowTitle` 用于一切"这一行是 principal"的行 |
| `TxtMuted` / `TxtCaption` / `TxtRowDesc` | 说明、注解 | `MutedFg` | 严禁用 `WarmFaint`——它只当**淡档面**，不是文字色 |
| `Eyebrow` | 宽体大写拉丁分组符 | `WarmFaint` | 只放 ASCII+数字；一屏 1–3 排，每一排说的是"一件事"的名字 |
| `TxtSignal` | 可点但不是按钮的信号文字（当前项、链接） | `SignalFg` | **全站唯一饱和文字色**，其他地方一概禁用饱和色文字 |

**铁律**：没有 `TxtBodySm2` 这种发明。要小一档 + 重一档 = `TxtCaption`+Bold 的组合只存在于 `TxtSection`（组标题）这一档，别处禁止复刻。
**字号内联覆写禁止**：尺寸不够就换尺寸档按钮（`BtnPrimarySm` 先例），不许对现有键 `Setter` 覆写 FontSize。

### 2.2 墨阶与面阶

墨色从重到轻五档山：`WarmDark` → `WarmBody` → `SurfaceFg` → `MutedFg` → `WarmFaint`（仅面/眉标）。
面阶从地到天五档国：`PageBg`（地）→ `CardSolid` 用的 `WSurfaceBrush`（卡）→ `WSurfaceRaised`（浮）→ `WBarBrush`（顶栏井）→ `WWellBrush`（凹井）。

**台阶规矩**：相邻两级可视容器必须差**至少一档墨或一档面**，否则"塌"成一整块。
**信号色配额**：`Signal` / `SignalInk` / `SignalHover` — 每屏 1 个饱和面（通常就是 CTA）。违者见宪法 1。

### 2.3 间距

页面内一律走令牌：`WPageMargin`（页边）、`WPadCard`（卡内距）、`WPadRow`（行内距）、`WPadWell`（井内距）、`WPadHero`（英雄区）。
`Spacing` 裸数成文阶 = **0 / 1 / 2 / 3 / 4 / 6 / 8 / 10 / 12 / 14 / 16 / 20**（2026-11-06 按全仓真实节奏修订：6 是紧凑半档、14 是分组半档，都是这套系统的既有韵律；0 = 无距、1 = 发丝微距，均为合法档）。阶外的间距需求，改到最近的一档；禁止发明阶外的新值（`5→4`、`7→6`、`18→16`、`22→20`）。`CharacterSpacing` 不在此列，它是字距不是间距。

### 2.4 圆角与高度

圆角四档：卡片 `WRadiusCard`、浮阶再大 4 的 `WRadiusRaised`、井 `WRadiusWell`、对话框 `WRadiusDialog`、胶囊 `WPillRadius`。按钮角一律 `WRadiusButton`（世界下发成对）。
控件高度梯度（成文，世界不下发）：**30 / 34 / 40 / 46 / 56**。命中区下限 44 (`MinimumHeightRequest`)。**同一视觉行内不许出现两种高度档的按钮**——行内的就全 34。
圆形元素的例外照旧成立：圆形的角 = 边长的一半，跟着元素尺寸走，直接写边长/2（如 22、24、32）是合法的。

### 2.5 影子

归属：`CardNeumorph`（浮）、`CardDialog`（对话）、玻璃卡面；档案室的浮卡带真实软阴影（`WShadowCardBrush`），凹件不发影。
**禁区**：VisualState 里换 Shadow（合成层重建→"点过的按钮返回后不绘制"，已踩实）；影子不是凹的表达——凹的东西有影子就是 BUG。

### 2.6 emoji 与图形符号（2026-11-06 成文）

**硬禁令**：控件文本不得含彩色 emoji——按钮、标题、占位符、提示语、右键菜单项、Toast 正文、设置目录图标位，全部禁。范围 = U+1F000–U+1FAFF 全部、emoji 变体链（`️`-U+FE0F 结尾的呈现符号，✅ ❌ ⬇️ ☕ ❤️ 之类）。图标语义要么交给**文字本身**（「导入表情」不需要 📥），要么交给**图形资产**（2.6-3）。理由：emoji 靠系统字体渲染，跨色板、跨平台、跨缩放都不合调——宪法 7 破门。

1. **单色文本符号白名单**（不算 emoji，属排版符号体系，合法）：`✓ ✕ ✎ ✦ △ · ○ ● ♡`。代码库既有先例 = `PluginDoctor` 的 `✕/△/·` 体检符号、`Toast` 的 `✦`、围棋的 `○●`、点赞的 `♡`。`✅ ❌` 一律改用 `✓ ✕`（同为记号，但前者是 emoji 呈现）。
2. **空态/占位装饰位**不用字符：用图形资产 `Resources/Images/ic_*.png`（暖墨单色剪影、透明底，由 `.local/gen_icons.py` 脚本生成——尺寸/描边/墨色都出自脚本参数，改样式改脚本重跑，不许手绘替换）。现役：`ic_chat / ic_gift / ic_heart / ic_masks / ic_frame / ic_music / ic_paw`。
3. **内容豁免**（照 R10 原则：内容 ≠ 镶边，豁免的是被仿真的世界本身）：
   - 仿真聊天壳（`QqPage` / `WeChatPage`）工具条的**纯图标按钮**（🙂 📎 🎁）；
   - 游戏内模拟手机/画布顶栏的**纯图标按钮**（`MainGamePage` / `MainGamePhoneView` 的 🎁 🎤）；
   - 商品/物品**数据字段**的 emoji（`ShopService.Add(..., "🧋")` 及 `{item.Emoji}` 的引用——是数据内容，不是版式装饰）；
   - 棋盘/小游戏的棋子与物件字符（`♔ ♞ 🍎 ● ○`）与战斗 HUD 头像 emoji 数据。
   豁免收紧为「纯图标 + 数据内容」两条；凡是「emoji + 空格 + 文字」的混合按钮，即便在豁免域内也按硬禁令处理（删 emoji 留文字）。

### 2.7 世界签名层（唯一世界 · 档案室，2026-11-06 成文并收编四世界）

度量衡管**这个界面不丑**（秩序），签名层管**凭什么记住它**（性格）。两层并存，宪法一条不动。

**一份处方**（强度只在令牌，不在资产）：

1. **大气层** `WorldAtmosphere` 控件：砂纸颗粒 + 顶部暖光 + 四角暗角，纹理资产中性（`Resources/Images/atmo_*.png`，`.local/gen_atmo.py` 生成），透明度由世界下发 `WAtmoGrainOpacity / WAtmoSpotOpacity / WAtmoVignetteOpacity`。规矩：**整层 `InputTransparent`**、放页面根容器**最后一个子级**（内容之上、点击之下）、**每页至多一层**。当前处方：颗粒 .07 / 暖光 .24 / 暗角 .16 —— 上一版 0.04–0.10 实测「看不出区别」，这一档保证肉眼可辨且不脏字。
2. **卡面受光**：`Face()` 给每一块面一道极浅顶光渐变（顶提 0.045），纸从「一整块死白」变成「对着窗光的纸」。玻璃档由 `GlassTokens` 接管卡面（tier=glass）。
3. **页首印记** `WMarkColor / WMarkRadius / WMarkSize`：Eyebrow 前一枚 12px 近直角小块（r2），颜色 = `Signal`（黄铜/琥珀，钉死色相 38°）。它是这间屋子的角标 notch，不跟色板换色相。

| 令牌 | 档案室处方 |
|---|---|
| 颗粒 / 暖光 / 暗角 | .07 / .24 / .16 |
| 印记 | 12px · r2 · Signal（黄铜） |
| 材质 | 终末地字阶（30/20/15.5/14.5/13/11.5）+ 圆角 8/6/8/6 + 描边 1.2 + 玻璃面 + 拟态软阴影 |

**禁令**：签名层不参与悬停/按压/选中状态（签名不演戏——状态色仍归 HoverFeel/PressFeel）；印记不用 emoji；大气层不许写死透明度（必须走令牌）；**不许再按世界分支写处方**（世界只有一间房）。

---

## 第 3 章 · 构图

### 3.1 页面骨架定理

每页从这只 Grid 开始，不许偏离：

```
Grid  RowDefinitions="Auto,Auto,*"
  Row 0  顶栏 Border（WBarBrush 井 + WPadWell，StrokeThickness=0）
  Row 1  HSep 发丝线
  Row 2  内容（WPadCard 内距）
```

顶栏三段式：左（`BtnText` 返回 / 面包）、中（`Eyebrow` 一行 + `TxtTitle` 一行，左对齐居中列）、右（唯一主行动 + 可选井面注解徽章）。**顶栏从不放描边盒**——它和内容是同一块地，靠发丝线分界。
带着右列图标的顶栏，桌面端徽章在窄窗收起，让位给主行动。

### 3.2 内容列

- 单列阅读流：`MaximumWidthRequest={DynamicResource WCardWidth}`，横竖居中。**消灭整页顶满全宽的卡**。
- 双栏编辑：`WCardWidthWide`，≥1280 才允许，手机退化为单列。
- 行长 > 900 读不回来——这是 `CardWidth` 封顶的物理原因，不是审美选择。

### 3.3 重量配额表（每屏量一次）

| 资源 | 每屏配额 |
|---|---|
| Signal 饱和面（CTA） | 1 |
| `TxtDisplay` | 1 |
| 浮阶卡 | 1 |
| `Eyebrow` 排 | ≤ 3 |
| 图片/大图块 | ≤ 2 |
| 说明文字总行数（`TxtMuted`/`TxtCaption`） | 与正文行数 ≤ 1:1 |

超出配额不是"设计更丰富"，是违反本书。

### 3.4 成组规则

分组用 `CardGroup`（一张白卡装多条行）+ `HSepBleed` 分线，**不是一行一张卡**。组标题 `TxtSection`（上 26 下 12 的留白分家）。
左导航条用 `NavRailItem`：静止透明、选中只沉一档 + 左缘细胶囊，**禁止整行饱和色**（视线会被钉死）。

---

## 第 4 章 · 页面配方

九个配方覆盖你现有的全部页面形态。配方给出结构与检查点，**改一页之前先找出它是哪个配方，对照差距**。

### R1 · 列表 + 预览双栏页（标准源：角色库）

两栏 FlexLayout（桌面 Row / 手机 Column）：左名册 `CardSolid`（宽 372，手机高 300），右预览。
名册头部：`Eyebrow` + `TxtSubhead` + 右侧计数 `TxtCaption`，下压 `HSep`；行 = `TappableRow`（头像井片 + `TxtRowTitle` + 副文）。选中态由当前档的 nav/accent 表达，不描边不加影。
**检查点**：名册只负责"选"，预览负责"看"，改设定不在这里。

### R2 · 设置分组页（标准源：设置各子页）

```
CardGroup（Padding=0）
  ├─ SettingRow × N（左 TxtRowTitle+TxtRowDesc，右控件右列定宽 280）
  ├─ HSepBleed 每行之间
```
说明 ≤2 行（`MaxLines=2` + WordWrap），超长进折叠或悬停。2–4 个选项用分段（SegBar），不用 Picker。**一行一卡禁止**（间距比行距还大，分组感即刻消失）。

### R3 · 图块网格页（入口/功能块）

两列网格，tile = `CardSolid` + `Eyebrow` 码 + `TxtSubhead` 标题 + 一句摘要 + `›`。悬停 = `TappableRow` 式 Accent 提亮（`HoverFeel`），**hover 物理上必须是"压色"而不是"换脸"**。
tile 摘要一行封顶。两列 tile 里严禁出现第二颗主按钮。

### R4 · 单列阅读/表单页

内容列 `WCardWidth` 居中；页头 = `TxtDisplay` 欢迎/标题 + `TxtMuted` 一句话；表单行距用 `WPadRow`，字段间距 12。提交区永远是一颗 `BtnSignal` 胶囊（整页唯一响），居左跟随阅读流，不铺满全宽。

### R5 · 工作台编辑页（向导/工作室类）

双栏 `WCardWidthWide`：左列（编辑，卡内滚动）、右列固定 440 预览 `CardFrost`（玻璃面）。步进器/步骤条用 `Eyebrow` 码 + 当步 `TxtSignal`。
主行动锁在左列底部一颗 `BtnSignal`；保存/生成这类主行动不许出现在预览列。

### R6 · 对话/聊天壳（QQ、微信）——豁免区

叙事道具化：壳里的品牌蓝/绿用 `Qq*`/`WeChat*` 静态色板，**不随色板与明暗切换**（style ⊥ palette）。气泡对：我方 `BubbleMine`+`AccentWarmFg`，对方 `BubbleTheirs`+`SurfaceFg`。壳的其余骨架仍然走本书（发丝线、行高、按压力学）。

### R7 · 空态

标准件（来源：角色库）：井面圆片（72×72，`WWellBrush`+`WRadiusPill`）内放**墨档**图形（`MutedFg`）→ `TxtTitle` 一句 → `TxtMuted` 一句 → （可选）一颗行动钮。**空态里禁止放淡彩图形**（`WarmFaint` 压井面 1.x:1 = 没画）。

### R8 · 弹窗/确认框

`CardDialog` + 顶部 `TxtTitle` + `TxtMuted` 说明 + 底部按钮行**右对齐**：
- 普通确认：`BtnText` 取消 + `BtnPrimary` 确认；
- 破坏性：`BtnDanger`（非紧凑）放**最左**，取消、确认在右——离确认越远的危险动作越难误触，这一条是物理位置规则。
对话框在档案室也带影（`WShadowDialogBrush`），它是唯一允许盖住全页的东西。

### R9 · 横幅/状态通知

一律走 `BannerService` 成就风（图标框金描边 + Eyebrow + 标题 + 一句话）。方向按文档：上浮/下浮/四角；**必须避让顶栏与底输入栏**，禁止盖住任何可点条。桌宠模式 `PetService.NotifyBubble`，不抢窗口。

### R10 · 领域豁免区（成文清单，2026-11-06 修订）

以下页面/元素**不适用**系统字阶、间距阶与裸色禁令——它们的成立依赖画布与叙事，套系统令牌反而会坏：

| 域 | 页面/元素 | 豁免范围 | 原因 |
|---|---|---|---|
| 聊天壳道具 | `Qq*` / `WeChat*` 页面 | 品牌色、气泡角（14,14,4,14）、微字阶全部 | R6 的叙事道具豁免 |
| 手机道具 | `PhonePage`、`MainGamePhoneView` | 图块渐变品牌色、白色遮罩、微字阶 | 手机壳是叙事道具，内部是模拟器画面 |
| 桌宠 | `PetPage`、`PetStudioPage` | 桌面壁纸上的遮罩色 | 压在任意壁纸上，语义阶反而不成立 |
| 图像工具遮罩 | `AvatarCropPage`、`PortraitCalibrationPage`（`#FF5252` 系注释里已说明转 RedSoft） | 裁切白罩、标定彩线 | 图像编辑遮罩必须独立于世界 |
| 游戏画布 HUD | `MainGamePage`、`MinimapHostView`、`TitlePage` 的 HUD 角 | 微字阶（9–11.5）、画布角 | HUD 密仪表有自己的一档，压到 WSizeCaption 就挤爆 |
| 领域调色板 | `MemoryGraphPage` 的节点/图例色 | 节点散色（每类一个色相） | 图谱分类配色是数据语义，不是装饰 |

**豁免≠无序**：豁免域内"挂了样式键就不再内联覆写"这条**仍然生效**；豁免的只是「必须用 WSize*/间距阶/语义色」这三条。

### 字号执法政策（度量衡 2.1 的执行细则）

1. **挂了样式键的元素禁止再内联 FontSize**——样式键给出的是档位（如 `BtnGhostSm` = 胶囊字），内联一个裸数会把世界的档位按死成固定值。执法动作 = 删掉内联 FontSize，让样式档生效。
2. 未挂样式的独立 Label/Entry/Picker 允许直接写字号，但值必须落在当前域的阶里：系统页用 WSize 阶（12/13/14/15/16…），豁免域用各自的微字阶。
3. 需要第三种尺寸时先造样式键（`BtnPrimarySm` 先例），不许对现有键逐处覆写。

### 状态适配

每一页都要给出三种状态的标准实现：**空态（R7）→ 加载（`ActivityIndicator` 叶子，PrimaryAction 色）→ 正常**。错误走横幅（R9）不弹窗打断，除非需要选择。

---

## 第 5 章 · 组件选型表

### 5.1 按钮（按意图选，不按好看选）

| 你要说的话 | 样式键 | 层级 |
|---|---|---|
| 本屏唯一的大行动 | `BtnSignal`（胶囊+Signal 帧） | ★★★ |
| 卡内/普通确认 | `BtnPrimary`（暖金） | ★★ |
| 一般动作（顶栏页面按钮） | `BtnNeumorph` | ★★ |
| 次要动作、跟随项 | `BtnGhost` / `BtnGhostSm` | ★ |
| 行内小动作（识别/移动/上移） | `BtnText` / `BtnTextSm` |  |
| 删除/破坏性 | `BtnDanger` / `BtnDangerSm`（face=卡面，字 DangerInk，RedSoft 缝） | 特 |
| 图标钮（返回/顶栏） | `BtnIcon` 44×44 / `BtnLiquidGlassIcon` 56×56 | — |

**配额与落位**：
- 每屏 `BtnSignal` ≤ 1；已有它的卡里禁止再放 `BtnPrimary`。
- 页面级 CTA 用胶囊角（与一排方角的"形状差"是色盲可读的那一唯一标识）。
- 尺寸档跟容器：行内全 34；卡内 34/40；页面级 46；标题页英雄区 56。
- 主游戏互动钮用 `BtnLiquidGlassIcon`，只有它是圆形。

### 5.2 卡片

| 内容需要 | 样式键 |
|---|---|
| 结构说明、静态分组 | `CardSolid`（平） |
| 此刻要动它（选中预览、焦点卡） | `CardNeumorph`（浮，一屏 1） |
| 细节的井（输入区、代码块、缩略容器） | `CardNeumorphInset`（凹） |
| 压在图/画布上 | `CardFrost` |
| 装多条设置行 | `CardGroup`（平，Padding=0） |
| 可点击功能块 | `PressableCard` + `HoverFeel` |

**选型次序**：先问"浮不浮"→"平"→"凹"。默认平，浮是稀缺的，凹是功能性的。

### 5.3 分隔线与分组

- 容器内容：`HSep`（1px `WSep`）；
- 行首有头像/图标：`HSepInset`（从文字起，不跨图标——线跨图标=变成上一行的底）；
- 行不带内距、卡带内距的老结构：`HSepBleed`（顶回卡边）；
- 双栏之间：`VSep`（1px 中柱）。

### 5.4 徽章/胶囊三型，只许这三型

1. **注解徽章**：井面（`WWellBrush`）+ `TxtCaption` 淡墨字，垂直居中，窄屏收起；
2. **状态徽章**：`*Tint` 填充 + 对应墨字（不是 Tint 字）；可操作选 `TagChip`；
3. **胶囊 chip**：选项 2–N 的选择块，用 `Chip`/`TagChip` 范本（页内 Resources 允许的少数例外），`PressFeel`+`HoverFeel` 必挂。

### 5.5 行

| 行的性质 | 样式键 | 有无悬停 |
|---|---|---|
| 说明行（左字右开关） | `SettingRow` | 无 |
| 可点进详情 | `TappableRow` | Accent 提亮 |
| 左导航 | `NavRailItem` | 按条自定 |

`SettingRow` 与 `TappableRow` 是**故意分成两个**的：说明行不可点，加深底等于把整行谎报成按钮。

### 5.6 输入

`Entry`/`Editor` 只允许语义令牌：`InputBg` 底、`BorderLine` 静止缝、焦 `EdgeStrong` 缝、`SurfaceFg` 字。输入内容永远放在**凹档井**或平档输入框里，命令行风格的输入井用 `CardNeumorphInset`。

---

## 第 6 章 · 文字与文案

| 部位 | 规则 |
|---|---|
| 页面标题 | ≤ 8 汉字 + 必配一行 `Eyebrow` |
| 按钮文字 | 动词开头，≤ 4 字，确认类必带宾语倾向（"保存设定"） |
| 说明文字 | ≤ 2 行；放不下 → 折叠/悬停/刪行，**不缩字号** |
| 数字强调 | `TxtDisplay`，不用颜色 |
| Eyebrow 文案 | 大写拉丁，写**这一段的类别**（`// ROSTER`），不写动作 |

文案顺序 = 越可能被打断越靠前：标题 → 一句主张 → 长说明。

---

## 第 7 章 · 动效

全部可点元素必挂 `motion:PressFeel`/`HoverFeel`。这是成文的**分工**，写死：

| 通道 | 归属 | 内容 |
|---|---|---|
| 悬停色 | `HoverFeel`（按面色现算） | 全站唯一悬停色作者 |
| 按下位移 | `PressFeel` 2.4px（Tap 70 / Settle 170） | 位移=按、色=悬 |
| 系统焦点 | WinUI 自画一条 | 不自绘第二圈 |
| 禁用 | Opacity 0.4 | 唯一允许的透明度状态 |

**禁区**（每一条都是修过 BUG 的成文禁令）：
1. VisualState 里换 `Shadow` / 写 `Scale` —— 合成层重建、重新布局；
2. VisualState 里写 PointerOver/Pressed 色 —— 导航切页后收不到抬起事件、颜色长一脸；
3. 状态里写 `TranslationY` —— 与 PressFeel 抢；
4. 画刷令牌只进 `Background/Stroke`，颜色令牌只进 `*Color` 属性，同一控件两条属性不混写。

---

## 第 8 章 · 黑名单（丑的十二表）

每条：症状 → 病根（违反哪条宪法）→ 纠法。

1. **整页顶满全宽的卡** — 骨架定理裂开（卡片没被 `WCardWidth` 夹住）。
2. **手写 hex / `Cream*` 内联当语义色** — 换色板/换明暗劈成两半；一律换语义键。
3. **一行一张卡** — 宪法 5 裂开；换 `CardGroup`+`HSepBleed`。
4. **凹的地方有影子** — 深度两档反了凹浮（宪法 2）；凹靠沉底色+压线。
5. **`*Tint`/`BeigeAccent*` 当文字色** — 宪法 4；填上配得上它的墨字。
6. **同屏两颗亮钮 / 两个饱和面** — 宪法 1；降一颗为 `BtnGhost`。
7. **行标题不加粗、说明文与正文同重** — 层次没台阶；`TxtRowTitle`+`MutedFg`。
8. **顶栏包描边盒** — 骨架定理；顶栏是井+发丝线。
9. **`Spacing="7"` 这类无阶裸距** — 度量衡 2.3 破门；改到最近的阶档（或 R10 豁免域合法微阶）。
10. **对样式键内联覆写字号/高度** — 制作"第三种尺寸"；换尺寸档键。
11. **空态放一个灰字完事** — R7 配方裂开；缺井面圆片或第二行话说。
12. **同一行 40 高与 46 高按钮混排** — 度量衡 2.4 裂开；行内统一 34。
13. **按钮/菜单文案里缀 emoji**（「📥 导入」「✨ 使用」） — 度量衡 2.6 硬禁令；删 emoji 只留文字，或换 ic_* 图形资产。
14. **用 ✅ ❌ ⬇️ 这类 emoji 呈现符号当状态/图标** — 2.6 白名单之类；换单色文本符号 `✓ ✕`。

---

## 第 9 章 · 提交自检（每页硬门）

**A · 硬规则，一项不过不发**
- [ ] 零手写 hex（R10 豁免域除外）、零内联字号覆写（挂了样式键的一概不许）、零阶外间距（阶 = 0/1/2/3/4/6/8/10/12/14/16/20）
- [ ] 零彩色 emoji（度量衡 2.6：控件文本扫描 `U+1F000–U+1FAFF`/`U+FE0F` 为零；白名单单色符号除外）
- [ ] 页面骨架是定理骨架（顶栏井 + 发丝线 + 内容列有宽夹）
- [ ] 一屏 Signal 面 / `TxtDisplay` / 浮阶卡 各 ≤ 1
- [ ] 可点件全部挂 `PressFeel`/`HoverFeel`；按钮高度档在每一视觉行内统一
- [ ] 填充色上没有非墨字；`WarmFaint` 没当文字用
- [ ] 空态/加载/错误三态都有成文实现

**B · 唯一世界截图对照（审美验收）**
- [ ] 档案室 × 明 / 档案室 × 暗 两档各截一图
- [ ] 至少两套色板（暖色板 + 终末地冷色板）各截一图，确认形状不依赖色相
- [ ] 1024 宽不挤、320 宽不散（内容列、双栏退化）
- [ ] 截图与首页/角色库并排看 3 秒：**认得出是同一家人**（配色、密度、顶栏、字阶全部一致）

**C · 留痕**
- [ ] 改的是配方内改动直接提交；需要发明新组件/新配额 → 先在本书加一版条款再提交
- [ ] 手机上没检查过的页面不标"完成"

---

## 附录 A · 样式键索引

| 文件 | 提供键 |
|---|---|
| `DesignSystem/Styles/TextTypography.xaml` | `TxtDisplay/Subhead/Body/Small/Caption/Muted/Section/RowTitle/RowDesc`、`Eyebrow`、`TxtSignal` |
| `DesignSystem/Styles/ButtonShapes.xaml` | `BtnSignal/Primary/PrimarySm/Neumorph/Neumorphic/NeumorphSm/Lg/Accent/Glass/Ghost/GhostSm/Text/TextSm/Danger/DangerSm/Icon/LiquidGlassIcon` |
| `DesignSystem/Styles/CardShapes.xaml` | `CardSolid/Frost/Dialog/Group`、`SettingRow/TappableRow`、`HSep/HSepInset/HSepBleed/VSep`、`NavRailItem` |
| `DesignSystem/Styles/NeumorphicStyles.xaml` | `CardNeumorph(+Inset)`、`CardLiquidGlass`、`PressableCard`、`TagChip` |
| `DesignSystem/Styles/BaseStyles.xaml` | `Switch/Slider/Picker` 暖皮 |
| `DesignSystem/Theme/ColorPalette*.xaml` | 语义色 + 种子色（详见页内注释）
| `DesignSystem/Worlds/StyleWorld.cs` | 全部 `W*`/`Glass*` 令牌为运行时下发，公式与取舍见该文件注释 |

## 附录 B · 本书修改流程

类型甲：错别字、键名更新 → 直接改、提交流里说明。
类型乙：新增配方/组件/配额 → 在对应章节加条款，注明来源页与日期；同时在 `DEVELOPMENT.md` 的 UI 速查表中镜像一条。
类型丙：推翻现存条款（如"按钮不挂影子"这类性能成文禁令）→ 必须先在 issue/愿望单里论证、附实测证据，再改书、再改代码。**代码先行=不算变更，会被回滚。**
