# 《温暖如初》设计系统

本文是这个项目的设计系统说明书。它回答一个问题：**下一个控件、下一个页面该长什么样，参数从哪儿取。**
所有数值都从代码里查证，并标注出处；代码里没有的，写「待定（当前无令牌）」，不编造。

**与其它文档的分工**

| 文档 | 管什么 |
| --- | --- |
| 本文 `DESIGN_SYSTEM.md` | 令牌族、推导公式、材质物理量、组件尺寸与状态机。回答「这个值是多少、为什么是这个值」 |
| `docs/UI-STANDARD.md` | 配额与选型（一屏一个响、深度三档、R1–R9 配方、黑名单）。回答「该用哪一种、用几个」 |
| `docs/COMPONENTS.md` | 页面级组件清单与用法 |
| `docs/ui-style.md` | v2 技术规约（BannerService 避让、页内 Resources 限制） |

三者冲突时：**排版配额服从 UI-STANDARD，数值与推导服从本文，本文与代码冲突以代码为准并记入第 11 章。**

**读法约定**

- 「当前值」= 视口 1280×800、`Scale = 1.0` 时按源码常量算出的结果。窗口尺寸变化后字号/内距/行高按尺度乘子等比缩放，圆角与发丝线不缩放（`DesignSystem/Worlds/StyleWorld.cs:247-259`）。
- 键名一律写代码里的资源键（`{DynamicResource XXX}`），不写自然语言别名。
- 出处一律写成 `文件:行号`。行号对应**当前工作区**（含尚未提交的改动），不是某个历史提交；`git diff` 变了就要重核引用。

---

## 0. 系统骨架

三层，从上到下：

```
        页面 / 控件（Views/**/*.xaml，Controls/*.cs）
                     ↓ 只认键名，不认颜色
    StyleWorldTokens.Publish   ← 语义色 + 材质几何 + 字号台阶（每次换档重发）
    GlassTokens.Publish        ← 玻璃材质参数（面/环/影/色散，读世界给的颜色）
                     ↓ 写进 Application.Current.Resources 的顶层字典
        色板 ColorPalette*.xaml（只提供色相与彩度种子）
```

三条硬约束，违反任何一条都会在换主题/换明暗时暴露：

1. **颜色只有一个作者**：色板给种子，`StyleWorldTokens` 算语义色，`GlassTokens` 只算「哪一档多透、边多亮、影多远」。玻璃里任何写死的奶油值都会在深色档或冷灰地里变成一块不属于当前世界的颜色（`DesignSystem/Theme/GlassTokens.cs:176-181`）。
2. **令牌写进顶层字典**。只替换 `MergedDictionaries` 里的字典实例不会通知已注册的 `{DynamicResource}`，表现是「换了主题但颜色不变」（`DesignSystem/Theme/ThemeManager.cs:335-343`）。
3. **换档必须走 `StyleWorldTokens.Publish` → `GlassTokens.Publish` → `StyleWorldTokens.ApplyFace()` 这个顺序**。顺序颠倒时玻璃读到的是上一轮的世界色（`ThemeManager.cs:449-455`）。

---

## 1. 材质

### 1.1 玻璃状态机：`GlassKind`

枚举里只有四档，不是「Button/Slider/Card/Plate」那一套——滑杆与开关不是 `GlassKind`，它们是 `GlassKind` 的**消费者**（`DesignSystem/Theme/GlassTokens.cs:22-28`）：

| `GlassKind` | 语义 | 墙厚倍率 `Wall` | 用在哪里 |
| --- | --- | --- | --- |
| `Bar` | 薄。页底座、上下栏，只给底色一点奶白 | 0.78 | `GlassBarBg/Stroke/Shadow`、`WBarBrush` |
| `Card` | 中。一块板，窄倒角 + 一圈贴地的接触影 | 0.95 | `GlassCardBg/Stroke/Shadow`、`.CardFrost`、`.CardNeumorph` |
| `Button` | 厚。胶质，顶面高光带、底面侧壁暗带、最宽的倒角 | 0.88 | `GlassButtonBg/Hover/Pressed`、`GlassPlate` 按钮面 |
| `Dialog` | 最厚。最实、最暗的侧壁、最大的接触影，因为它是模态的 | 1.15 | `GlassDialogBg/Stroke/Shadow`、`.CardDialog` |

`Wall` 的定义在 `GlassTokens.cs:140-146`；它是**唯一**的档位阶梯——描边宽度、侧壁暗带、接触阴影、摩擦音全部乘它。

### 1.2 一个厚度，六种物理量

玻璃有两条实现路径，共用同一套「厚度 → 物理量」的语言：

- **自绘路径** `Controls/GlassFace.cs`（Skia 合成一张位图）：真折射、真镜面弧、真内影。用于 `GlassPlate`（按钮/滑杆/开关）。
- **画刷路径** `DesignSystem/Theme/GlassTokens.cs`（MAUI 渐变画刷 + Shadow）：只有渐变台阶与外发光，没有逐像素折射。用于 `Border`/`Button` 的样式。

#### 厚度是怎么算的

```
Depth(h)       = 0.35 + 0.65 × h          // h = GlassHeight(0..1)，0 也留 0.35 板厚
ThicknessOf(k) = clamp01( Depth(h) × Wall(k) / 1.15 )    // 弹窗、最高 = 1.0
```

出处 `GlassTokens.cs:133`、`GlassTokens.cs:153`。**高度 0 不是「平」**：0.35 的底厚是为了让「最薄」仍然是薄，而不是没有边（`GlassTokens.cs:129-132`）。
`ThicknessOf` 是画面与声音共用的那个数——摩擦音的音高、衰减也从它出来，两边不许各抄一份（`GlassTokens.cs:148-152`）。

默认 `GlassHeight = 0.5`，世界偏向 `(0.85, 0.16, 0.80)`（透明度/磨砂/高度，`StyleWorld.cs:269`），实际下发时 `h = 0.5 × 0.80 = 0.40`。

按此计算，`ThicknessOf` 落在（`h` 取 0 / 0.40 / 1）：

| 档 | h=0 | 默认 h=0.40 | h=1 |
| --- | --- | --- | --- |
| `Bar` | 0.237 | 0.414 | 0.678 |
| `Button` | 0.268 | 0.467 | 0.765 |
| `Card` | 0.289 | 0.504 | 0.826 |
| `Dialog` | 0.350 | 0.610 | 1.000 |

#### 物理量表：同一个厚度，八个量

| 物理量 | 公式 / 常量 | 默认 `Card` 档 | 默认 `Button` 档 | 默认 `Dialog` 档 | 出处 |
| --- | --- | --- | --- | --- | --- |
| 采样模糊半径 | 糊底本身先在 320px 采样图上高斯 blur σ=12（原图约 48px，1280 宽窗）。玻璃只**采样**这张已糊的图，不再二次模糊 | σ=12@320px | 同左 | 同左 | `Services/Core/FrostImageService.cs:29-31` |
| 折射位移 | 环带宽 `min(短边×0.17, 7dp)`，下限 3.2dp，上限短边 34%；绕中心放大 1.08 倍；切 4 档；权重 `(1-t)^1.4 × 0.62`；按下 ×1.15、悬停 ×1.08 | 环带 ≤7dp；位移 = 0.08 × 距中心距离（边缘最大，约 0.08 × 半宽） | 同左 | 同左 | `Controls/GlassFace.cs:58-72`、`:229-254` |
| 边缘壁厚（画刷路径） | `RingOf = round(Stroke × (0.85 + 0.55×Depth(h)), 2)`，`Stroke`：Bar 1.0 / Card 2.0 / Dialog 2.0 | 2.37px | 固定 3.0px（不走 RingOf） | 2.37px | `GlassTokens.cs:159-160`、`:443-450` |
| 边缘壁厚（自绘路径） | 外环 1.2dp；空心环（滑钮）2.4dp | 1.2dp | 1.2dp | 1.2dp | `GlassFace.cs:425-447` |
| 内影深度 | 三遍描边 9 / 17 / 26 dp，alpha = `0.18 × 状态倍率 × clamp(Thickness,0,1.4)`，渐变在 45% / 80% 收掉；按下 ×1.35、悬停 ×1.1 | 随 `GlassPlate.Thickness` | 0.18 × 0.75 ≈ 0.14 | 画刷路径无内影 | `GlassFace.cs:315-350` |
| 镜面弧强度 | 峰值 alpha 亮底 0.80 / 暗底 0.98（`GSpecular`）；弧宽 2.0dp（按下 1.4dp）；内缩 0.7dp；外层辉光 4.5× 宽、13% | 同左 | 同左 | 同左 | `GlassFace.cs:368-423`、`StyleWorld.cs:879-900` |
| 色调浓度 | alpha = `TintAmount(控件相对倍率) × GTintAmount ÷ 0.07 × (悬停 1.1)`；信号钮 ×2.4、上限 0.34 | 0.07 × 世界值 | `GlassButton` 0.07，信号钮 0.11 | 0.07 | `GlassFace.cs:292-307`、`:516-518` |
| 接触阴影（画刷路径） | `reach = (1.2 + 4.0T) × Weight`，`radius = (9 + 20T) × Weight × (1+0.5z)`，`opacity = (0.15 + 0.24T)(1 − 0.30z)` | reach 3.22 / r 22.4 / op 0.24 | reach 3.99 / r 28.0 / op 0.24 | reach 6.55 / r 44.8 / op 0.27 | `GlassTokens.cs:336-361` |
| 面不透明度 | 有糊底时 Slab 面夹在 `FaceCeiling = 0.62`；无糊底自动补到 `0.80 + a×0.22`，上限 0.96。按钮走 `ClearFace`，不吃这条兜底 | 上限 0.62 | 白 7%~15%（不兜底） | 上限 0.62 | `GlassTokens.cs:171-174`、`:239-249`、`:387-412` |

`Weight` 是每档的阴影倍率：Bar 0.55 / Card 1.00 / Button 1.30 / Dialog 1.80（`GlassTokens.cs:200-210`）。`z = LightZ`（固定 0.35）。
弹窗的阴影几何被钉死在样式里（`Offset 0,16 / Radius 38`），只换颜色，因为换整个 `Shadow` 对象等于重建合成层（`DesignSystem/Styles/CardShapes.xaml:60-65`）。

#### 控件档（`GlassPlate.Thickness`，0..1.4）

`GlassPlate` 不走 `GlassKind`，它直接吃一个 0..1.4 的厚度。当前的取值：

| 控件 | `Thickness` | `TintAmount` | `CornerRadius` | 出处 |
| --- | --- | --- | --- | --- |
| `GlassButton` | 0.75 | 0.07（信号 0.11） | 8（默认） | `Controls/GlassButton.cs:136-143`、`:170-180` |
| `GlassSwitch` 轨道 | 0.55 | 0.05 | 13.5 = 27/2 | `Controls/GlassSwitch.cs:82-87` |
| `GlassSwitch` 圆钮 | 1.0 | 0.12 | 10.5 = 21/2 | `GlassSwitch.cs:113-123` |
| `ViewSlider` 轨道 | 0.55 | 0.05 | 6 = 12/2 | `Controls/ViewSlider.cs:176-184` |
| `ViewSlider` 滑钮（空心） | 0.9 | 默认 0.07 | 14 = 28/2 | `ViewSlider.cs:213-222` |

规则：`Thickness` 越大，内影越紧、镜面越窄、侧壁越暗；`TintAmount` 只表达「这块玻璃是主体还是容器」——容器恒低于主体（轨道 0.05 < 按钮 0.07 < 圆钮 0.12）。

### 1.3 糊底与窗口透视：两条来源，不能互换

| | 糊底（应用内） | 窗口透视（系统） |
| --- | --- | --- |
| 实现 | `FrostImageService` 生产 PNG → `FrostField` 解码共享 → `GlassFace` 按页面坐标裁剪取样 | `SystemBackdropService` 给 WinUI 窗口挂 `DesktopAcrylicBackdrop` 或 `MicaBackdrop` |
| 处理 | 降采样 320px → 高斯 σ=12 → 去饱和 18% → 亮度归一化到 0.72（浅）/0.24（深）→ 对比 ×1.10 → 混 10% 地面色（照片）→ 颗粒 ±6/255 → 输出 960 宽 PNG | 系统自己模糊窗口**背后**的桌面 |
| 看得到什么 | 应用自己的背景（壁纸、合成地面、纯色地面） | 只有窗口之外的桌面和其它窗口 |
| 适用 | **默认档**。图片背景与纯色背景都走它 | 只有「看穿到桌面」这一档 |
| 失效时 | 玻璃退回同一套令牌的斜渐变（镜面、内影、描边照画），不是白块 | 静默无效，必须回落 |

关键区别：**系统 backdrop 看不见应用内的背景图**，壁纸画在窗口内容层里，Acrylic 只处理窗口背后（`Services/Core/SystemBackdropService.cs:5-8`）。所以「有壁纸」和「看穿桌面」是互斥的两种背景档，不能同时假设。

没有糊底时（Android、首帧、解码失败）玻璃必须**变实**：`FrostedBackdrop=false` 时面 alpha 补到 0.80~0.96（`GlassTokens.cs:163-168`、`:245-247`）。宁可丑，也不能让正文压在照片上读不出来。
唯一的例外是按钮：按钮永远站在卡片或面板上，背后永远有东西可透，所以它不允许被补实（`GlassTokens.cs:387-395`）。

性能约定：一块玻璃只在「几何 / 状态 / 主题 / 糊底版本」变化时合成一次，位图按缓存键复用（最多 64 张）；重画节流到约 30fps（`GlassFace.cs:74-75`、`:125-131`；`Controls/GlassPlate.cs:152-168`）。

---

## 2. 颜色

### 2.1 世界令牌

五套色板，`ThemeName` 这条轴已被**锁定为 `endfield`**（`ThemeManager.LockedPalette`，`ThemeManager.cs:226-235`）。旧存档里的 classic/sakura/bamboo/mist 读得进来，但一律按终末地渲染。色板只提供种子：`GroundH/GroundC/AccentH/AccentC/Green/Red/Blue`（`StyleWorld.cs:343-345`、`:824-838`）。

终末地种子（`DesignSystem/Theme/ColorPaletteEndfield.xaml`）：

| 种子 | 值 | 用途 |
| --- | --- | --- |
| `AccentWarm` | `#FAD41F`（黄） | 强调、液柱、信号块（终末地档信号色直接用它的色相） |
| `SurfaceBg`（地面种子） | `#1C1E20` | 提供地面色相与彩度 |
| `GreenSoft` / `RedSoft` / `BlueSoft` | `#8CE03C` / `#E5383B` / `#22A6F2` | 成功 / 危险 / 信息的色相 |

### 2.2 三档地：页面 / 卡面 / 井

明度不直接取色板值，而是由 `Depth` 与明暗算出来（`StyleWorld.cs:375-384`）。`d = Depth − 0.55 = 0.07`：

| 档 | 浅档 | 深档 |
| --- | --- | --- |
| 页面 `PageBg` | L = 0.968 − 0.032d ≈ **0.966** | L = 0.115 − 0.030d ≈ **0.113** |
| 卡面 `SurfaceBg` | L = **0.995**，彩度 ×0.55 | L = 0.175 + 0.020d ≈ **0.176**，彩度 ×0.55 |
| 井 `WellBg` | L = 0.930 − 0.028d ≈ **0.928**，彩度 ×1.3 | L = 0.082 − 0.015d ≈ **0.081**，彩度 ×1.3 |

浅档的页面钉在 0.966 是有原因的：地面沉到 0.90，白卡就变成「灰纸上贴一块糖」（`StyleWorld.cs:376-378`）。
深档染色更重（`tint × 1.7`），因为同量彩度压在暗底上几乎读不出来（`StyleWorld.cs:372-373`）。

### 2.3 墨阶（文字色）

五档，每档都带一个**目标对比比**，由 `Lock()` 强制达标（`StyleWorld.cs:386-391`）：

| 令牌 | 语义 | 目标对比（浅/深） | 实际用途 |
| --- | --- | --- | --- |
| `WarmDark` | 显示 / 标题 / 行标题 | 11.0 / 9.0 | 最深那档墨 |
| `SurfaceFg`、`PrimaryFg` | 正文 | 8.4 / 7.6 | 默认正文 |
| `WarmBody` | 标签 | 7.0 / 6.4 | 小组标题 |
| `MutedFg`、`WarmMuted` | 次要 | 5.4 / 5.0 | 说明、脚注、caption |
| `WarmFaint` | 装饰 | 3.4 / 3.1 | 眉标、图形、不可用作正文 |

### 2.4 边、强调、信号、状态

| 令牌 | 推导 | 出处 |
| --- | --- | --- |
| `BorderLine` | `surface` 沉/提 0.055 | `StyleWorld.cs:397` |
| `WControlEdge` | `surface` 沉/提 0.11（控件边：取「缝」那一档，实测参考控件边 ≈ `#E3E3E3` 压在 `#FFFFFF`） | `StyleWorld.cs:398`、`:623-629` |
| `EdgeStrong` | `surface` 沉/提 0.21 / 0.18 | `StyleWorld.cs:399` |
| `ShadowDim` | = `seam`（**不是阴影色**，见第 11 章） | `StyleWorld.cs:465` |
| `AccentWarm` | 色板 `AccentH`，L 浅 0.80 / 深 0.37 | `StyleWorld.cs:404` |
| `AccentWarmFg` | 压强调色锁 5.0:1 | `StyleWorld.cs:407` |
| `Signal` | 终末地档取 `AccentH`，S 0.95，L 浅 0.40 / 深 0.62，锁 4.8 / 5.0 | `StyleWorld.cs:412-415` |
| `SignalFg` | 同色相，锁 4.8 / 5.0 | `StyleWorld.cs:481-482` |
| `SignalInk` | 压信号面上：浅档白、深档黑，锁 4.6 | `StyleWorld.cs:486` |
| `SignalTint` | 极浅色偏（悬停底），L 浅 0.958 / 深 0.215 | `StyleWorld.cs:487` |
| `SignalHover` / `SignalPressed` | `SinkOrLift(signal, 0.16 / 0.30)` | `StyleWorld.cs:490-491` |
| `DangerInk` | 锁 4.8 / 5.2 | `StyleWorld.cs:493` |
| `GreenSoft` / `RedSoft` / `BlueSoft` | `Status()`：L 浅 0.42 / 深 0.58，兜底锁 3.2 | `StyleWorld.cs:861-865` |
| `*Tint`（`DangerTint` 等） | L 浅 0.945 / 深 0.235，**只当填充** | `StyleWorld.cs:553-557` |

信号墨的方向是手写的，因为 `Lock` 只兜底补对比、不判断方向；方向写错就是一团脏灰（`StyleWorld.cs:483-486`）。

### 2.5 文字对比度规则

| 类别 | 下限 | 系统的构造性保证 |
| --- | --- | --- |
| 正文 | **≥ 4.5:1** | `SurfaceFg` 锁 8.4 / 7.6 |
| 次要文字（说明、脚注、caption） | **≥ 4.5:1** | `MutedFg` 锁 5.4 / 5.0 |
| 装饰性文字、图标、状态色 | **≥ 3:1** | `WarmFaint` 3.4 / 3.1；状态色兜 3.2 |

低于 3:1 等于没画（`UI-STANDARD` 第 1 章第 4 条、第 5.3 节）。填充色（`*Tint`、`BeigeAccent*`、`WellBg`）**永远只当填充**，上面必须压墨阶，没有例外。

#### 「文字色由背景亮度自动反向」怎么算

规范的算法在 `StyleWorld.cs:917-966`，四步：

**第一步：相对亮度（sRGB → 线性）**

```
f(v)     = v ≤ 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055)^2.4
L(color) = 0.2126·f(R) + 0.7152·f(G) + 0.0722·f(B)
```

**第二步：对比度**

```
CR(a, b) = (max(L_a, L_b) + 0.05) / (min(L_a, L_b) + 0.05)
```

**第三步：先按亮度选方向，再往该方向推**

```
Step(face, k) = L(face) > 0.5
              ? Mix(face, Black, k)          // 亮面往黑沉
              : Mix(face, White, clamp(k × 1.2))  // 暗面往白提（多给 20%）
```

阈值是**相对亮度 0.5**，不是 HSL 的 L，也不是感知亮度。这正是朴素做法出错的地方：终末地的强调黄 `#FAD41F` 在 HSL 里明度 0.55，看着「不算亮」，相对亮度却有 0.675，白字压上去只有 1.45:1（黑字 14.5:1）。按 0.5 的阈值它会正确地选黑；按 HSL 明度随手判断就很容易选成白。同理 `#8CE03C`（L 0.592）白字只有 1.64:1——「淡彩色上摆白字」这一类案子都是同一个错误。

**第四步：用与背景的混色把对比度钉到目标值**

```
Lock(fg, ground, target):
    if CR(fg, ground) ≥ target: return fg
    toward = L(ground) > 0.5 ? Black : White
    二分 k ∈ [0,1]，取最小的 k 使 CR(Mix(fg, toward, k), ground) ≥ target（24 次迭代）
    return Mix(fg, toward, 那个 k)
```

方向先由背景定，再由二分把对比**恰好**推到目标值——不是推到底。所以墨阶之间永远保持 11 / 8.4 / 7 / 5.4 / 3.4 的层级，不会因为某次调色变成两档一样重。
这是**构造性保证**：任何人以后调世界台阶，都不可能再调出 1.05:1 的说明行（`StyleWorld.cs:950-953`）。

**落到控件上的简化版**（悬停/按下的那一档、透明底那一层）：

```
Step(face, k)        // 见上，k = HoverFeel.HoverStep = 0.16
Wash(k) = Dark ? White@(k × 1.1) : Black@k    // 透明底上的状态层：压一层色偏，不换实色
```

`Wash` 之所以用半透明黑/白，是因为同一个控件可能站在页底上，也可能站在卡面或淡彩徽章上；固定实色必然在其中一处变成「鼠标移上去没变化」（`StyleWorld.cs:911-928`）。

---

## 3. 文字

### 3.1 字号阶梯

六个正式档，全部由世界下发并乘尺度乘子（`StyleWorld.cs:238`、`:678-683`）：

| 键 | 当前值 | 用例（样式键） | 字重 |
| --- | --- | --- | --- |
| `WSizeDisplay` | **30** | `TxtDisplay`；标题页主标语 | Bold |
| `WSizeTitle` | **20** | `TxtTitle`；页头标题、卡内大标题（`CardTitle` 用 `WSizeSubhead`） | Bold |
| `WSizeSubhead` | **15.5** | `TxtSubhead`、`CardTitle`、`Monogram` | Bold |
| `WSizeBody` | **14.5** | `TxtBody`、`TxtRowTitle`、按钮字号 | Regular / Bold |
| `WSizeSmall` | **13** | `TxtBodySm`、`TxtMuted`、`TxtSection`、`TxtSignal` | Regular / Bold |
| `WSizeCaption` | **11.5** | `TxtCaption`、`CardNote`、`CardFooter`、`Eyebrow`、`StatLabel`、`BtnPrimarySm` | Regular |

另有 `WSizeBrand = round(WSizeDisplay × 2.15) = 64`（64.5 走银行家舍入），只给启动卡那四个字（`StyleWorld.cs:686`）。

字号与样式的对应关系写在 `DesignSystem/Styles/TextTypography.xaml` 与 `CardShapes.xaml`。**挂了样式键的元素不许再内联 `FontSize`**（`UI-STANDARD` §2.3 执法政策）。

### 3.2 行高

只有一个行高令牌：

```
WRowHeight = round(PadCard × 0.62 × 2 + SizeBody × 1.45) = round(19.84 + 21.03) = 41
```

出处 `StyleWorld.cs:651-655`、`:692`。它定义「一行设置项 = 上下内距 + 一行正文」。纵向内距固定取横向的 **0.62** 档，收到 0.72 时一行会被推到 51~52，而标签只有 15~17 号——那正是「一行字浮在一条带子中间」的成因。

多行文本的行高没有独立令牌，`CardNote` / `TxtRowDesc` 用 `LineHeight = 1.25`（`CardShapes.xaml:191`、`TextTypography.xaml:73`）。
标题类（Display/Title/Subhead）的显式行高：**待定（当前无令牌）**，走平台默认。

### 3.3 字距

| 场景 | `CharacterSpacing` | 出处 |
| --- | --- | --- |
| 拉丁眉标（`Eyebrow`，Bahnschrift） | **3** | `TextTypography.xaml:78-83` |
| HUD 读数标签 | **2.0**（源里先写 1.4，`Compose()` 覆写为 2.0） | `Controls/HudReadout.cs:32`、`:143` |
| 读数小标题（`StatLabel`） | **0.6** | `CardShapes.xaml:214-218` |
| 正文 / 标题 / 中文 | 0（不设） | — |

中文不加字距。全大写只出现在拉丁眉标（`// LAST SAVE` 一类），靠 Bahnschrift + 3 的字距构成「终端标签」的观感。

### 3.4 数字与中英混排

- 数字永远不折行：`HudReadout` 的标签与数值都是 `LineBreakMode.NoWrap`，因为在横向 `ScrollView` 的无界测量下，`TailTruncation` 会把 `21:30` / `0 / 100` 的尾字符裁掉（`HudReadout.cs:28-42`）。
- 数字优先用等宽字体（`Consolas`）出现在需要对齐的表格读数里：`Views/Memory/MemoryGraphPage.xaml:333`、`Views/Monitor/SuperMonitorPage.xaml:126`、`Views/Settings/SettingsSectionPage.xaml:749-758`。
- 无空格长串由 `SoftWrap` 转换器软换行，RTL 段落方向由 `BidiFlow` / `BidiAlign` 转换器给（`App.xaml:31-34`、`Helpers/BidiText.cs`）。数据本身一个字不改。
- 中文与拉丁之间的空格规则：**待定（当前无令牌、无规约）**。现状是各页面自己写空格与分隔符（如 `"  ·  "`，`Views/Main/TitlePage.xaml:210`）。

### 3.5 可用字体来源

`WarmAsBefore.csproj:43` 声明了 `MauiFont Include="Resources\Fonts\*"`，但 **`Resources/Fonts` 目录不存在**，`MauiProgram.cs` 里也没有 `ConfigureFonts`。所以项目**不打包任何字体**，全部是系统字体：

| 字体 | 出处 | 平台可用性 |
| --- | --- | --- |
| `Bahnschrift`（宽体技术字形） | 14 处，`Eyebrow` 样式定死 | Windows 10 1709+ 自带；**Android 无此字体，回退到系统默认** |
| `Consolas`（等宽） | 6 处，读数/JSON 表格 | Windows 自带；Android 无 |
| `Segoe MDL2 Assets`（图标字形） | 22 处 | Windows 自带；Android 无 |

结论：字体栈目前是 **Windows 专有**。Android 上这三个 `FontFamily` 都会静默回退。跨平台字体方案：**待定（当前无令牌、无字体包）**。

---

## 4. 间距与栅格

### 4.1 尺度乘子

```
Scale   = clamp(min(宽/1280, 高/800), 0.86, 1.16)，量化到 0.02 一档
SafeScale = Scale（非法值时 1.0）
```

出处 `StyleWorld.cs:64-76`、`:103-110`。取宽高两轴的**较小值**：只按宽度算的话，把窗口竖着拉（分屏）字号会跟着涨，而那个方向上真正发生的是内容被压扁。

被乘的：字号、内距（`PadCard` / `PadWell`）、行高。
不被乘的：圆角（4/8/12 是阶梯，缩放成 3.4/9.3/11.6 就不再是阶梯）、描边宽度（发丝线在哪个尺寸下都还是一根发丝，乘到 1.16 只会开始发灰）。

### 4.2 间距四档

间距只有四档，每一档都是 `PadCard` 的分数（`StyleWorld.cs:699-710`）：

| 键 | 系数 | 当前值 | 语义 |
| --- | --- | --- | --- |
| `WSpaceTight` | 0.25 | **4** | 同一个读数的两行字之间（标题与紧跟着它的说明） |
| `WSpaceRegular` | 0.50 | **8** | 列表项之间、按钮与按钮之间 |
| `WSpaceLoose` | 0.85 | **14** | 卡内分块之间、卡与卡之间 |
| `WSpaceSection` | 1.15 | **18** | 主题与主题之间（整页只该出现几次） |

此外 `WGapItem = round(PadCard × 0.62) = 10`、`WGapSection = PadCard + 8 = 24`。

**诚实说明**：这套阶梯以 8 为基点（`WSpaceRegular`），但 `Loose = 14`、`Section = 18` 不是 4 的倍数。它由「卡内距的分数」推出来，所以窗口换档时能一起缩放。如果要严格的 4/8 基点，`Loose` 应取 16、`Section` 取 16 或 20。这一处要在令牌层决定，不在页面层各写各的（见第 11 章第 11 条）。

### 4.3 容器内距

| 键 | 当前值 | 语义 | 出处 |
| --- | --- | --- | --- |
| `WPadCard` | `16` | 卡片内距 | `StyleWorld.cs:237`、`:649` |
| `WPadWell` | `10, 5.5, 10, 5.5` | 井面内距（纵向 0.55 档） | `:650` |
| `WPadRow` | `16, 9.92, 16, 9.92` | 行内距（纵向 0.62 档） | `:655` |
| `WPadRowInset` | `16, 0, 0, 0` | 缩进发丝线 | `:656` |
| `WPadCardBleed` | `−16, 0, −16, 0` | 出血发丝线（抵消卡内距） | `:660` |
| `WPadHero` | `23.2, 24.8, 23.2, 27.2` | 主行动带（1.45 / 1.55 / 1.7 档） | `:724` |
| `WPillPad` | `8, 1.6` | 胶囊内距 | `:721` |
| `WHeaderPad` | `14, 8, 14, 8` | 页头内距 | `:716` |
| `WPageMargin` | `24` | 页面留白（卡片浮在地面上四周那一圈） | `:727` |
| `WHeaderGap` | `12` | 返回 / 标题 / 动作三段之间的横距 | `:719` |

### 4.4 栅格与内容列

内容列宽度跟窗走、封顶（`StyleWorld.cs:130-178`）：

```
WCardWidth     = clamp(round(窗宽 × 0.62), floor, 900)      // 单列正文
WCardWidthWide = clamp(round(窗宽 × 0.90), floor, 1600)     // 双栏页（设置首页、桌宠）
WRailWidth     = WCardWidth < 480 ? 0 : round(262 × Scale)  // 左导航条；放不下一列时塌成一行
floor          = min(320, max(0, 窗宽 − 48))
```

1280 窗宽下：`WCardWidth = 794`、`WCardWidthWide = 1152`、`WRailWidth = 262`。
这三个令牌单独重发（`RepublishWidth`），因为窗口拖动时尺度可能没跨档，但内容列必须逐像素跟手。

断点：**没有全站令牌**，各页自带常量——标题页 `CompactBreakpoint = 820`（`Views/Main/TitlePage.xaml.cs:22`），聊天页 `620`（`Views/Chat/WeChatPage.xaml.cs:11`）。设计档 = 1280×800 逻辑像素（`App.xaml.cs:402-403`）。

---

## 5. 形状

### 5.1 圆角阶梯

只有三档，加上胶囊（`DesignSystem/Theme/GlassTokens.cs:105-109`）：

| 令牌 | 值 | 用在 |
| --- | --- | --- |
| `RadiusSmall` → `WRadiusWell` | **4** | 井面、内嵌槽、chip（`TagChip` 用 `WRadiusWell`） |
| `RadiusMedium` → `WRadiusCard`、`WRadiusButton` | **8** | 卡片、按钮 |
| `RadiusLarge` → `WRadiusRaised` | **12** | 浮档卡（`CardNeumorph`、`CardLiquidGlass`） |
| `RadiusDialog` → `WRadiusDialog` | **12** | 弹窗 |
| `WRadiusPill`（`RoundRectangle`） | **9999** | 胶囊：chip、井面圆片、圆形图标钮（`BtnLiquidGlassIcon`，56×56 → 真圆） |
| `WPillRadius`（数值版） | **21** | `Button.CornerRadius` 的数值形态 |

**为什么是阶梯而不是连续量**：圆角是「离读者多近」的线索也是对齐的线索。差 1px 的圆角相邻两块并排放会读成「没对齐」，所以 `SnapRadius()` 把任何入参吸附到 4/8/12 里最近的一档（`StyleWorld.cs:789-797`）。旧配方里的 6、14 这类中间值不再存在。

`WRadiusPill` 的 9999 交给平台夹到短边一半，所以「方正的一块」在 56 高的方框上自动成为真圆。

### 5.2 按钮必须是圆角矩形，不是胶囊

**这是产品决定，不是风格偏好。** 本项目的按钮一律 `WRadiusButton = 8`：

- `BtnGlass` / `BtnPrimary` / `BtnSignal` / `BtnNeumorphic` / `BtnDanger` 全部 `CornerRadius = {DynamicResource WRadiusButton}`（`DesignSystem/Styles/ButtonShapes.xaml:125`、`:164`、`:85`；`NeumorphicStyles.xaml:50`）。
- 胶囊状态（`WRadiusPill` / `WPillRadius`）只属于三类东西：**选择块（chip）**、**井面圆片**、**圆形图标钮**。它们是「容器」或「物件」，不是「动作」。

禁止场景，逐条：

1. **动作按钮不许是胶囊。** 页面级 CTA、行内动作、危险操作，一律圆角矩形 8。一颗胶囊按钮混在一排圆角矩形按钮里，读起来是「这一颗是别人家的」。
2. **卡片、弹窗、井面不许是胶囊。** 它们承载文字，胶囊会把两端的字压进弧里。
3. **按钮高度不决定圆角。** 34 / 40 / 46 / 56 四档高度全部用 8，不许因为「矮钮看着圆更顺眼」改成 `Height/2`。

现存违规：`Views/Main/TitlePage.xaml:225` 给 `EnterButton` 内联写了 `CornerRadius="9999"`。另外两处注释仍声称「四档高度都成了真正的胶囊」（`ButtonShapes.xaml:37`）、「四档高度的按钮都成了真正的圆头」（`StyleWorld.cs:645-647`）——这两句是过期描述，代码里没有任何样式把 `WRadiusPillPx` 用在按钮上。见第 11 章第 2、3 条。

### 5.3 发丝线

`GlassTokens.Hairline = 1.0`，是定数：栏与分隔线永远是 1px，粗一分就变成盒子（`GlassTokens.cs:94-95`）。
画面上的分隔线走 `WSep`（颜色）+ 宽 0.8（深）/ 1.0（浅）（`StyleWorld.cs:504-506`），样式是 `HSep` / `HSepInset` / `HSepBleed` / `VSep`（`CardShapes.xaml:106-133`）。
发丝线**不属于玻璃**：玻璃的边是倒角（2.37~3px 渐变），分隔线才是那 1px。

---

## 6. 动效

### 6.1 曲线

五条曲线，全站只有这一组（`DesignSystem/Theme/MotionTokens.cs:55-198`）：

| 名字 | 定义 | 语义 | 用在哪 |
| --- | --- | --- | --- |
| `Spring` | `cubic-bezier(0.34, 1.30, 0.64, 1.00)`，末段过冲约 **5%** 再落回 1 | 软材料回弹 | 悬停、按下、松手、整页过场 |
| `Settle` | `1 − e^(−6x)·cos(6.2x)`，过冲约 **5%** | 阻尼余韵 | 松手回位、开关滑行、液柱灌满 |
| `Snap` | `1 − (1−t)^6` | 机械定位（前 1/3 走完九成） | 元素入场 |
| `Mech` | `cubic-bezier(0.7,0,0.2,1)` 近似 | 机器动作 | 擦入、灌满、所有退出 |
| `OutCubic` | `1 − (1−t)³` | 快出 | 状态浮起的兜底 |

`Spring` 是真的解 cubic-bezier 的 `x(t)`（牛顿法 6 次 + 二分 24 次），不是多项式近似——近似曲线的手臂会慢半拍，压下去那一帧就得不到「立刻有回应」（`MotionTokens.cs:127-165`）。

### 6.2 时长

| 令牌 | 值 | 有效值（世界倍率 0.9） | 用途 |
| --- | --- | --- | --- |
| `TapMs` | 70 | **63ms** | 按下 |
| `SettleMs` | 170 | **153ms** | 抬起、回位 |
| `CardEnterMs` | 130 | **117ms** | 设置子页逐张建卡 |
| `EnterMs` | 220 | **198ms** | 元素进场（上浮 6px） |
| `SheetMs` | 320 | **288ms** | 弹层展开（盖住整页的一层） |
| `PageSlideMs` | 260 | **234ms** | 页面切换退路（位移 32px） |
| `PageSweepMs` | 440 | **396ms** | 整页过场（Windows 合成层，位移 56px + 上浮 6px） |
| `PageBackMs` | 340 | **306ms** | 返回（位移 44px） |
| `StaggerMs` | 45 | 45 | 多个元素依次进场的间隔 |
| `FrameMs` | 16 | 16 | 帧驱动节奏（约 60fps） |

世界给的节奏倍率是 `MotionOf(world) = (0.9, 2.0)`——`Scale = 0.9`，`Press = 2.0`（`StyleWorld.cs:334`）。
按下比抬起短是刻意的：手指碰到屏幕时人要的是「确认按到了」，80ms 内必须有变化；抬起后的回弹是材料的余韵，慢一点才显得软。反过来（进慢出快）就会像隔着棉絮点按钮（`MotionTokens.cs:7-8`）。

### 6.3 各状态的实际动作

| 动作 | 时长 | 曲线 | 位移 / 缩放 | 出处 |
| --- | --- | --- | --- | --- |
| 按下 | `TapMs` 63 | `Spring` | 下沉 `Press = 2.0px`（常量 `SinkPx` 默认 2.4，世界把它压到 2.0）+ 缩到 `PressScale = 0.96` + 换面 + 环提亮 | `MotionTokens.cs:67-78`、`Helpers/PressFeel.cs` |
| 抬起 | `SettleMs` 153 | `Spring` | 回 0 / 1.0 | 同上 |
| 悬停 | 由 HoverFeel 逐帧插值 | 面阶 `Step(face, 0.16)`；渐变面沿光照方向滑 **6%**；可点行用强调底 `0.20` | `Helpers/HoverFeel.cs:73`、`:82`、`:818` |
| 元素进场 | `EnterMs` 198 | `Snap` | 从下方 6px 浮起，**不淡入** | `Helpers/Animation.cs:31-40` |
| 次第进场 | `EnterMs` + `StaggerMs` 45×n | `Snap` | 每张差 45ms | `Animation.cs` |
| 弹层上浮 | `SheetMs` 288 | `Snap` | 上浮 18px（`EnterRisePx × 3`）+ 一声 Dialog 档摩擦音 | `Animation.cs:70-84` |
| 整页过场 | `PageSweepMs` 396 | `Spring` | 右侧进 56px + 上浮 6px | `MotionTokens.cs:40-51` |
| 返回 | `PageBackMs` 306 | `Spring` | 退出 44px（约为进入的 2/3） | `MotionTokens.cs:46-47` |
| 开关滑行 | 189ms（圆钮）/ 171ms（液柱） | `Settle` | 圆钮位移；液柱宽度同步灌满；圆钮按下瞬间涨到 **1.07** 再落回 | `Controls/GlassSwitch.cs:42-43`、`:190-201` |
| HUD 读数变化 | 130 | `Mech` | `ScaleY 0.6 → 1` 的「咔哒」 | `Helpers/HudMotion.cs:126-135` |
| 拒绝 / 失败 | `SettleMs` | `CubicOut` | 横向抖 ±6px 两轮 | `Animation.cs:52-64` |

**过冲上限**：`Spring` 与 `Settle` 各约 5%；圆钮那一下是 7%（再多就成了橡皮球，`GlassSwitch.cs:197`）。任何新动效的过冲不得超过 5%，唯一例外是圆钮。

### 6.4 三条禁令

1. **按压反馈只碰位移与缩放，不碰不透明度。** 透明度同时是进场动画、禁用态、卡片显隐的输出量，驱动再乘一层就会跟它们抢；抢输的那次赋值能把按钮留在半透明上，看起来像「莫名其妙消失」（`MotionTokens.cs:14-17`）。
2. **不把缩放写进 VisualState。** 带导航的按钮「按下即切页」时收不到抬起，视觉状态会永久停在 0.97（`MotionTokens.cs:17`、`ButtonShapes.xaml:28-34`）。颜色归 `HoverFeel`，位移归 `PressFeel`，视觉状态里的 `PointerOver` / `Pressed` 是空壳。
3. **进场不淡入，只位移。** 淡入从 `Opacity = 0` 起步，动画回调一旦丢失（页面被顶掉、ticker 不再推进）元素永久停在看不见那一头；位移的起点是 6px，最坏只是低了一点（`Animation.cs:20-28`）。

### 6.5 性能模式与减少动画

两个来源分开存，**用「或」合并**（`MotionTokens.cs:83-106`）：

| 来源 | 字段 | 默认 |
| --- | --- | --- |
| 系统「减少动画」 | `SystemDisabled`（Windows 读 `SPI_GETANIMATION` 的 `iMinAnimate`；环境变量 `WARM_NO_MOTION=1` 强制） | false |
| 用户设置的性能模式 | `PerformanceMode`（设置 → 实验卡片） | false |
| 实际生效 | `Disabled = SystemDisabled \|\| PerformanceMode` | — |

拆开的原因：系统偏好是「这台机器/这个人不能看动效」，性能模式是「我先省点性能」。合在一个布尔里，用户关掉性能模式就会把系统偏好一起清掉，前庭敏感的用户得每次重启后重新忍受动效（`MotionTokens.cs:83-87`）。

降级规则：

- `MotionTokens.Ms(x)` 一律返回 **0**，调用处直接落到终值（`MotionTokens.cs:117-118`）。**状态照样变**（按下仍会沉、仍会压暗），只是不再补中间帧——用户要的是「别动」，不是「没有反馈」。
- 阴影由 `StyleWorldTokens.FlatShadows` 改平（颜色发到全透明，`Shadow` 对象不动，避免重建合成层）。诊断开关 `WARM_NO_SHADOW=1` 的方向与性能模式相反：一旦置位就一直压着，关掉性能模式不会把排障开关一起关掉（`StyleWorld.cs:806-815`）。
- HUD 动效在关闭时直接返回（`HudMotion.IsHud` 恒 false）。

---

## 7. 声音

### 7.1 五个语义音

`UiSoundKind`（`Services/Media/UiSoundSynth.cs:6-21`），全部 44.1kHz / 单声道 / 16bit 合成，每个不到 20KB：

| 音 | 时长 | 合成 | 语义 | 谁在用 |
| --- | --- | --- | --- | --- |
| `Tap` | **46ms** | 2400Hz + 3100Hz(×0.55) + 1850Hz(×0.18)，起音 1.2ms，衰减率 120 | 玻璃轻击：手指落到可点的东西上 | 所有按钮按下（`PressFeel`、`GlassButton`） |
| `Confirm` | **150ms** | 784Hz（62ms）→ 1175Hz + 2350Hz(×0.06) | 上行两音：事情办成了 | 开关打开、导入/新开局成功 |
| `Back` | **110ms** | 660 → 420Hz 连续下滑 | 下行一音：返回 / 取消 | 开关关闭、撤销、警告与错误 Toast |
| `Notice` | **190ms** | 1568 + 3136(×0.34) + 4704(×0.12)，起音 12ms | 柔和的一声「叮」 | Toast 出现、提示 |
| `Friction` | **22–38ms** | 带通噪声 + 低频撞击，参数由厚度算（见下） | 两块玻璃板擦过去 | 滑杆拖动、开关切换、卡片入场、弹层上浮 |

### 7.2 摩擦音：一个厚度，两个投影

摩擦音不是固定采样，它**按厚度生成**（`UiSoundSynth.cs:87-134`）。四条只读 `GlassTokens.ThicknessOf(kind)` 这一个数：

```
带通中心 = 3100 − 1500 × T   Hz     （薄 3100 → 厚 1600）
时长     = 22 + 16 × T       ms     （薄 22 → 厚 38）
衰减率   = 150 − 70 × T             （薄一下收，厚留尾巴）
撞击     = 118 − 26 × T      Hz     （厚板更沉）
```

默认档：`Card` T=0.504 → 中心 2344Hz、30ms；`Button` T=0.467 → 2400Hz、29.5ms；`Dialog` T=0.610 → 2185Hz、32ms。
响度归一化到同一峰值（`0.30/peak`）：厚薄是音色差别，不该变成音量差别。
种子固定（`20261127 + (kind+1)×977 + 厚度百分位`），所以同一块板永远是同一声摩擦，同一个厚度永远合成出同一段噪声。

### 7.3 节流、音量、总开关

| 项 | 值 | 出处 |
| --- | --- | --- |
| 任意音最小间隔 | **8ms**（只约束非语义音） | `Services/Media/UiSoundService.cs:96-100` |
| 同种音最小间隔 | **30ms** | 同上 `:100` |
| 队列上限 | **6** 声 | `:24`、`:101` |
| 默认音量 | **0.5**（设置项 `UiSoundLevel`，0~1） | `:23`、`:51` |
| 总开关 | `UiSoundEnabled`，默认 **开**；关掉后一个采样都不合成、不碰播放器 | `:44`、`:88` |
| 失败策略 | 一次异常即 `_broken = true` 彻底闭麦，不反复刷日志、不抛给调用方、不弹窗 | `:121` |

语义音（`Confirm` / `Back` / `Notice`）**只受 30ms 同族闸**，不受 8ms 闸——它们跟着一次明确操作来，被闸吃掉就等于这两个精心挑的声音永远听不见（`UiSoundService.cs:96-99`）。

### 7.4 什么交互配什么音

| 交互 | 声音 | 出处 |
| --- | --- | --- |
| 任何按钮按下 | `Tap`（Windows 上在 `PointerPressed` 响，与下沉同时发生） | `PressFeel.cs`、`GlassButton.cs:274-281` |
| 开关：摩擦 + 确认 / 返回 | `Friction(Button)` + `Confirm`（开）/ `Back`（关）；**只在用户按的那一下响**，绑定初值与 ViewModel 回写不出声 | `GlassSwitch.cs:158-168` |
| 滑杆拖动 | `Friction(Card)`，每滑满 **24dp** 响一次，**方向反转也响一次**（里程表从那里重新起算） | `ViewSlider.cs:56-60`、`:314-329` |
| 弹层上浮 / 整排卡片入场 | `Friction(Dialog)` / `Friction(Card)` | `Animation.cs:75`、`:122` |
| Toast 出现 | `Info` / `Success` → `Notice`；`Warning` / `Error` → `Back`（下行音，听见就知道不是好消息） | `Controls/Toast.cs:55-57` |

`Friction` 不准挂在悬停或滚动 tick 上（`UiSoundService.cs:291-294`）。

---

## 8. 状态

### 8.1 七种状态

| 状态 | 视觉 | 动效 | 出处 |
| --- | --- | --- | --- |
| **默认** | 玻璃面：白 7%~15%，环 3px（紧凑档 2.5px），外发光 | 无 | `ButtonShapes.xaml`、`GlassTokens.cs:443-454` |
| **悬停** | 面阶 `Step(face, 0.16)`；渐变面沿光照方向滑 6%；可点行换成强调底（alpha 0.20）；信号钮色调 ×1.1 | HoverFeel 逐帧插值，离场按原路收回 | `HoverFeel.cs:73`、`:82`、`:818` |
| **按下** | 下沉 2.0px + 缩 0.96 + 换 `GlassButtonBgPressed`（高光带收窄、侧壁压紧）+ 环换 `GlassButtonRingHot`；自绘玻璃：折射 ×1.15、镜面更亮更窄 1.4dp、内影 ×1.35 | `TapMs` 63ms / `Spring` | `PressFeel.cs`、`GlassFace.cs:238`、`:318`、`:371` |
| **焦点** | 系统焦点环（WinUI 在控件外沿一条）。我们**不自绘第二圈** | — | `ButtonShapes.xaml:106`、`:35` |
| **禁用** | 整体 `Opacity`：`GlassButton` **0.4**；`GlassSwitch` / `ViewSlider` **0.45**；样式里的 `Disabled` 视觉状态统一 **0.4** | 立即（无过渡），并把按着的那一笔落回原位 | `GlassButton.cs:182-186`、`GlassSwitch.cs:139-142`、`ViewSlider.cs:288-291` |
| **加载** | `LoadingVeil`：4~6 条骨架，底色 `WWellBrush`，静态 `Opacity 0.55`；顶部一块 72px 的封面占位。**只用 `IsVisible` 切换，永不写 `Opacity` 做动画** | 无 | `Controls/LoadingVeil.cs:1-60` |
| **空** | `EmptyState`：标题（这是什么空）+ 一句「为什么空」+ 一句「怎么才不空」；后两句缺一可以不出现，标题必须有。出路句用 `SignalFg`（整块里唯一带方向感的话）。不画插图、不画大图标 | 无 | `Controls/EmptyState.xaml:1-46` |
| **错误** | `Toast.Error`：方形徽章 + 眉标 `ERROR` + 标题 + 一句话，从屏幕边缘滑入、停留几秒滑回；阻断性确认仍用原生对话框 | 逐出 / 逐入 | `Controls/Toast.cs:12-20`、`:35-41` |

`LoadingVeil` 默认 1800ms 兜底收起：空库时信号永远不「就绪」，没有这道保险就把骨架永久盖在内容上（`LoadingVeil.cs:21`、`:129`）。

### 8.2 焦点环画法（当前缺口）

现状是**没有一条我们画得出来的焦点环**：

- 令牌存在：`WFocusRingWidth = 2.0`、`WDecorRingWidth = 4.0`（`StyleWorld.cs:630-633`，`BaseStyles.xaml:14-16` 给了静态兜底）。
- 但 `WFocusRingWidth` 全仓只有一个消费者：`Views/Main/TitlePage.xaml:262` 的键盘光标（那是方向键操作模式的光标，不是 Tab 焦点环）。
- 所有按钮样式把 `Focused` 留空，依赖 WinUI 自画——理由是不自绘第二圈会让首屏主按钮自动获得焦点时出现「双边框」（`NeumorphicStyles.xaml:75-78`）。
- 自绘控件（`GlassButton`、`GlassSwitch`、`ViewSlider` 都是 `ContentView`）**在代码里没有任何 `Focusable` / `IsTabStop` 设置**，全仓 grep 为 0。它们默认不进 Tab 序列，所以也拿不到系统焦点环。

规范的画法（修好之后应该长的样子）：

```
外环：StrokeShape 同控件圆角，StrokeThickness = {DynamicResource WFocusRingWidth} (2)
颜色：{DynamicResource EdgeStrongBrush}（深色档上它比卡面亮一档，浅色档上沉一档，两头都看得见）
内缩：环画在控件外侧 2px，不盖内容；不与环（3px 玻璃边）重叠
动效：无。焦点是位置信息，不是材料动作。
```

---

## 9. 无障碍

| 项 | 当前状态 | 依据 |
| --- | --- | --- |
| **对比度** | 墨阶由 `Lock()` 构造性保证：正文 8.4/7.6、次要 5.4/5.0、装饰 3.4/3.1、状态色兜 3.2 | `StyleWorld.cs:386-391`、`:864` |
| **减弱透明** | `ReducedTransparency` → `GlassParams.Reduced`，玻璃面与描边 alpha 直接置 1.0（实心） | `ThemeManager.cs:122-133`、`GlassTokens.cs:241`、`:295` |
| **减少动画** | 跟随系统 `SPI_GETANIMATION`；`WARM_NO_MOTION=1` 强制 | `DesignSystem/Theme/MotionPrefs.cs` |
| **键盘 Tab 路径** | **缺口**：`GlassButton` / `GlassSwitch` / `ViewSlider` 未设 `Focusable`/`IsTabStop`；原生 `Button`/`ImageButton` 走平台默认 | 全仓 grep `Focusable\|IsTabStop` = 0 |
| **Enter / Space 激活** | `GlassSwitch` 有（Windows 上挂原生 `KeyDown`，Space/Enter 都切换，并 `e.Handled = true` 防双触发）；`GlassButton` / `ViewSlider` **没有** | `GlassSwitch.cs:248-253` |
| **读物名称** | **缺口**：`AutomationProperties.Name` 在 Views 里 0 处；`SemanticProperties.Description` 只有 3 处。`GlassSwitch` 会在名字后补「已开启/已关闭」，但名字本身没人给 | grep；`GlassSwitch.cs:210-223` |
| **状态播报** | `GlassSwitch` 把 `"名字，已开启"` 同时写进 `SemanticProperties.Description` 与 WinUI 的 UIA `Name`，并设 `HelpText = "空格或回车切换"` | `GlassSwitch.cs:216-222` |
| **触控最小尺寸** | 44（`MinimumHeightRequest` 字面量）；`DesignTokens.Sizing.TouchMin = 44` 存在但**没有消费者** | `ButtonShapes.xaml:87`；`DesignSystem/Tokens/DesignTokens.cs` |

补 Tab 路径的最小改动方向：给三个自绘控件加 `Focusable = true` + `TabIndex`，在 `Focused` 状态下画 8.2 的环；`GlassButton` 在 `KeyDown` 里接 Space/Enter；页面侧给每个可点控件补 `AutomationProperties.Name`。

---

## 10. 组件规格

### 10.1 Button

五个高度档（34 / 40 / 42 / 46 / 56）+ 命中区下限 44。**高度是字面量，世界里没有按钮高度令牌**（`ButtonShapes.xaml:39-41`）：

| 样式键 | 高度 | 水平内距 | 字号 | 环宽 | 用途 |
| --- | --- | --- | --- | --- | --- |
| `BtnPrimarySm` / `BtnGhostSm` / `BtnDangerSm` | **34** | 12 | `WSizeCaption` 11.5 | `GlassButtonRingWidthSm` 2.5 | 卡片内小步确认、行内动作 |
| `BtnGhost` / `BtnDanger` | **40** | 18 | `WSizeSmall` 13 | 3.0 | 中档次要动作 |
| `BtnGlass` / `BtnPrimary` / `BtnNeumorphic` | **46** | 20 | `WSizeBody` 14.5 | 3.0 | 标准按钮 |
| `BtnNeumorphLg` | **56** | 24 | `WSizeBody` | 3.0 | 首屏大钮 |
| `BtnSignal` | `WPillHeight` = **42** | 30 | `WSizeBody` | 3.0 | 页面级 CTA（整页唯一响） |
| `BtnIcon`（ImageButton） | 44×44 | padding 10 | — | 2.5 | 导航图标钮（圆靠尺寸成立） |
| `BtnText` / `BtnTextSm` | 自适应 | 12,6 / 8,4 | `WSizeSmall` / `WSizeCaption` | 无 | 纯文字钮 |

层次：`BtnText`（无面、无环、无光）→ `BtnGhost`（玻璃 + 环）→ `BtnGlass`（标准）→ `BtnPrimary`（`SignalFg` 字）→ `BtnSignal`（`SignalFg` 字 + `GlassButtonGlowAccent` 外发光）。
`BtnText` 刻意没有环：它常出现在卡片里的小动作区，一旦有环就会被读成实心按钮，一行三四个动作立刻变成一排钮（`ButtonShapes.xaml:259-261`）。

可调参数：`Text`、`Command`、`CommandParameter`、`Signal`（bool）、`FontSize`、`FontAttributes`、`TextColor`、`Padding`、`CornerRadius`（默认 8）。**页面不该改后四项**——它们是样式档的定义域。

禁用：`Opacity 0.4`，`CanExecute == false` 也按禁用处理但不回写 `IsEnabled`（避免与页面显式绑定互顶，`GlassButton.cs:16-20`）。

### 10.2 GlassSwitch

```
轨道  46 × 27，圆角 13.5（= H/2），Thickness 0.55，Tint 0.05，关态是空的玻璃腔
液柱  高 24（= 27 − 2×1.5），圆角 12，内缩 1.5，宽 0 → 43；渐变 WSliderFillBrush + WSliderBulgeBrush
圆钮  ⌀21，圆角 10.5，Thickness 1.0，Tint 0.12，位移 3 → 22；外发光 0,1 / r9
```

出处 `Controls/GlassSwitch.cs:35-43`、`:80-127`。
动作：圆钮 `TranslateTo` 189ms（`Settle`）；液柱宽度 171ms（`Settle`）；按下瞬间圆钮胀到 **1.07** 再落回（`GlassSwitch.cs:170-202`）。
状态：`Signal = on`（开态轨道染成世界强调色）；禁用 `Opacity 0.45`。
可调参数：`IsToggled`（TwoWay）、`Toggled` 事件、`IsEnabled`、`AutomationProperties.Name`。
等价原生 `Switch`：同名同义，页面改一个标签名就能换。

### 10.3 ViewSlider

```
行高   32
轨道   高 12，圆角 6（= H/2），Thickness 0.55，Tint 0.05（玻璃管）
液柱   高 12，圆角 6，宽 = 滑钮中心；渐变 WSliderFillBrush
鼓包   32 × 16 径向亮斑，比滑钮宽一圈，两端夹回轨道内
张力   2px 白线（WSliderTension），贴在滑钮中心左侧
滑钮   ⌀28 空心环（Hollow），Thickness 0.9，环宽 2.4dp，外发光 0,0 / r12
```

出处 `Controls/ViewSlider.cs:32-37`、`:176-225`。
拖动按**差量**算：`Started` 记下起点比例，`Running` 加 `TotalX / (宽 − 28)`。整档滑杆（`Maximum − Minimum ≥ 5`）自动取整，否则读数会一直抖一位小数（`ViewSlider.cs:340-347`）。
兼容口：显式给 `MaximumTrackColor` 或 `ThumbColor` 的页面会自动退回实色轨道 / 实色环（`AvatarCropPage`、`MapCreatePage` 要的是「这一颗钮是红的」，不是玻璃）。
可调参数：`Value`（TwoWay）、`Minimum`、`Maximum`、`ValueChanged`、`MinimumTrackColor`、`MaximumTrackColor`、`ThumbColor`。

### 10.4 Card

五个键，对应五个层次（`CardShapes.xaml`、`NeumorphicStyles.xaml`）：

| 样式键 | 面 | 圆角 | 影 | 层次 |
| --- | --- | --- | --- | --- |
| `CardNeumorphInset` | `WWellBrush` | 4 | 无（凹的地方不能有影） | 井 |
| `CardSolid` / `CardGroup` | `WSurfaceBrush`（glass 档被接管为 `GlassCardBg`） | 8 | 无 | 平 |
| `CardFrost` | `GlassCardBg` | 8 | 无 | 平（压在画布/图片上） |
| `CardNeumorph` / `CardLiquidGlass` | `GlassCardBg` | **12** | `0,10 / r30`，`WShadowCardBrush` | 浮（一屏 ≤ 1） |
| `CardDialog` | `GlassDialogBg` | 12 | `0,16 / r38`，`WShadowDialogBrush` | 弹窗 |

`CardSolid` 在 glass 档下的边宽不是 1px：`ApplyFace()` 把 `WEdgeWidth` 换成了 `GlassCardStrokeThickness`（`RingOf` ≈ 2.37），所以卡片的边是**倒角**，不是发丝线（`StyleWorld.cs:316-328`）。
内距 `WPadCard` 16；`CardGroup` 内距归 0（内距归每一行），行间压 `HSep`。
卡内五段固定：`CardHead`（行框）→ `CardTitle`（`WSizeSubhead` Bold）→ `CardNote`（`WSizeCaption` + `MutedFg` + LineHeight 1.25）→ `SettingRow`（`WRowHeight` 41）→ `CardFooter`（同档换灰 + 自带 `WPadRow`）。

弹窗内距是字面量 **24**（`CardShapes.xaml:58`）——**待定（当前无令牌）**，建议改为 `WPadCard × 1.5`。

### 10.5 GlassPlate

自绘玻璃基元，属性即参数（`Controls/GlassPlate.cs:33-96`）：

| 属性 | 默认 | 作用 |
| --- | --- | --- |
| `CornerRadius` | 8 | 圆角（像素） |
| `Thickness` | 0.7 | 板厚 0..1.4，只影响内阴影的浓度与宽度 |
| `TintAmount` | 0.07 | 世界色调的相对倍率（绝对浓淡归世界） |
| `Signal` | false | 换成强调色调并 ×2.4（上限 0.34），一屏只该有一颗 |
| `Hollow` | false | 只画环带（折射 + 环 + 镜面 + 内影），中间留透 |
| `Interactive` | false | 打开后自己吃点击（纯装饰用默认值） |
| `Pressed` / `Hover` | false | 状态位，写一次触发重画 |
| `Slot` | null | 叠在玻璃上的内容（文字、图标），玻璃本身输入透传 |

规则：`GlassPlate` 只在「看得见 + 尺寸有效 + 参数变了」时重画，滚动与尺寸变化节流到约 30fps，缓存上限 64 张。

### 10.6 页面标题区（`PageHeader`）

```
结构   返回 ·（眉标 + 世界印记）/ 标题 / 副标题 ·（压测徽标 + 右侧动作）
高度   WHeaderHeight = round(PadWell × 1.6 + WSizeTitle × 1.45) = 45（有副标题时自然长高）
内距   WHeaderPad = 14, 8, 14, 8
横距   WHeaderGap = 12；正文列内 `WSpaceTight` 4
底     WBarBrush + 一条 HSep（组件自带，页面不再各写一条）
印记   12 × 12，圆角 2，色 WMarkColor（= Signal）
文字   Eyebrow（Bahnschrift / 字距 3 / WSizeCaption / WarmFaint）+ TxtTitle + TxtCaption（副标题单行截断，全量走 ToolTip）
```

出处 `Controls/PageHeader.xaml`、`StyleWorld.cs:712-719`、`:777-780`。
两个形态：`Bar`（页面级顶栏，有底有发丝线）与 `Ground`（落在地上的一页头，无底无线，给「页头本身就是内容首屏」的信息页用）。

### 10.7 HUD 读数角（`HudReadout`）

```
标签   10 号（横向 8.5）+ 字距 2.0 + MutedFg（NoWrap，单行）
数值   21 号 Bold + SurfaceFg（横向 15）
进度   3px 高，填充 Signal / 轨道 Signal@0.25
边框   1px EdgeStrong（Framed 时）+ 底色 WellBg，内距 9,5
动效   数值变化时 ScaleY 0.6 → 1（130ms，Mech）
```

出处 `Controls/HudReadout.cs:32-79`、`:137-165`。
形态由宿主页面决定：横向（标签与数值并排，顶栏用）/ 竖向（标签在上、数值在下、进度线在底）。
当前 `HudSkin.IsActive` 恒为 `false`（切角面板这一套已收掉，只剩圆角阶梯），所以 `HudReadout` 默认整块隐藏；页面里原来的读数行原样保留（`Controls/HudSkin.cs:20-26`、`HudReadout.cs:78`）。

---

## 11. 规整清单

通读时查到的真实不一致。只列查证的，每条给出文件和行号，加一句处理建议。按严重程度排。

**1. `DesignTokens.cs` 是一整套并行的死令牌。**
`DesignSystem/Tokens/DesignTokens.cs:1-62` 定义了第二套阶梯：`Spacing`（4/8/12/16/24/32/48）、`Radius`（4/8/12/16/20/9999）、`Typography`（12/14/16/18/20/24/30/36）、`Timing`（100/200/300/500）、`Sizing.TouchMin 44`。全仓引用数 **0**（grep `DesignTokens.` / `Spacing.` / `Sizing.` / `TouchTarget` 均无命中）。它与 `WSize*`（11.5/13/14.5/15.5/20/30）、`WRadius*`（4/8/12）、`MotionTokens`（70/170/220/320…）三套真值互相矛盾——抄它的人会拿到一套不存在的系统。建议：删除该文件，或把它改成从 `StyleWorldTokens` / `MotionTokens` 取值的薄封装。

**2. 主 CTA 被内联写成胶囊，与「按钮是圆角矩形」冲突。**
`Views/Main/TitlePage.xaml:225`：`<controls:GlassButton ... CornerRadius="9999" .../>`。同页其余动作与全站样式都走 `WRadiusButton = 8`。建议：删掉该内联值，让 `GlassButton.CornerRadius` 默认 8 生效。

**3. 两处注释仍声称「按钮全是胶囊」，与代码相反。**
`DesignSystem/Styles/ButtonShapes.xaml:37`（"四档高度都成了真正的胶囊"）、`DesignSystem/Worlds/StyleWorld.cs:645-647`（"四档高度的按钮都成了真正的圆头"）。令牌 `WRadiusPillPx` 定义在 `StyleWorld.cs:647`，全仓**无消费者**。建议：删掉这两句与 `WRadiusPillPx`，避免下一个人照注释把胶囊写回来。

**4. 页面把「缝」的颜色当成阴影画刷用。**
`Views/Chat/WeChatPage.xaml:469`、`Views/Main/MainGamePhoneView.xaml:199`、`:223` 都是 `<Shadow Brush="{DynamicResource ShadowDim}" .../>`。而 `ShadowDim` 在 `StyleWorld.cs:465` 被赋值为 `seam`（`surface` 沉 0.10~0.11 的近地色），是**分隔线**颜色，不是阴影墨。结果是这三处阴影几乎不可见，且换明暗时不会跟着走。建议：换成 `WShadowCardBrush`，几何用 `WShadowChipOffset/Radius`。

**5. 同一族奶油色在手机视图里还是写死的，与世界派生的令牌并存。**
`Views/Main/MainGamePhoneView.xaml:12`、`:45`（`#E8F5F0E8`）、`:218`（`#F0FDF8F0`）、`:234`、`:241`、`:328`、`:380`（`#CCFDF8F0`）。`StyleWorld.cs:438-445` 明确记录了这族 `#B8FDF8F0 / #D9FDF8F0` 已改为由 `page/surface` 派生的 `TopBarChipBg / PanelGlass / PanelGlassSoft / PanelGlassStrong`，深色档下写死值会亮在半屏黑里。建议：一律换成那四个令牌。

**6. 禁用态的透明度有两档。**
`Controls/GlassButton.cs:184` 用 `0.4`；`Controls/GlassSwitch.cs:141` 与 `Controls/ViewSlider.cs:290` 用 `0.45`；样式里的 `Disabled` 状态统一 `0.4`（`ButtonShapes.xaml:71` 等 8 处）。同一屏上「灰掉」有两种深浅。建议：收敛到单一常量（建议 0.4），从令牌层下发。

**7. 字号内联 618 处，令牌 94 处。**
`grep 'FontSize="[0-9.]*"' Views` = 618 处（13 号 226 次、12 号 120 次、11 号 83 次、9.5 号 20 次…），而 `{DynamicResource WSize*}` 只有 94 处（`WSizeCaption` 35 / `WSizeSmall` 27 / `WSizeBody` 13 / `WSizeDisplay` 8 / `WSizeSubhead` 7 / `WSizeTitle` 4）。世界换档时字阶整体缩放，这 618 处纹丝不动——这正是「窗口放大后卡片变大、字没变」的来源。建议：先处理第 8 条那类「样式键 + 内联覆写」，再按域把裸字号换成 `WSize*`（先做 `Views/Main` 230 处、`Views/Settings` 215 处、`Views/Chat` 99 处——三个域占 618 处里的 544 处）。

**8. 已挂样式键的按钮又被内联改高度。**
`Views/Games/FlowEditorPage.xaml:46`、`:50`、`:54`、`:58`：`Style="{StaticResource BtnGhostSm}"`（34）同时写 `HeightRequest="28"`。这制造了第五个按钮高度档，且 28 低于 44 的命中区下限。建议：新增一个「行内极小」样式键，或在页面改用 34 档。

**9. 自造尺寸的按钮遍布聊天页。**
`Views/Chat/WeChatPage.xaml:478`、`:488`（`FontSize="13" HeightRequest="36" CornerRadius="10"`）、`:526`、`:531`（`FontSize="11" HeightRequest="28" CornerRadius="10"`）。高度 36/28、圆角 10 都不在阶梯上。建议：用 `BtnGhost`（40）与 `BtnGhostSm`（34），删掉内联尺寸。

**10. 圆角阶梯被大面积绕过。**
`Views/**/*.xaml` 里 `StrokeShape` 上的圆角值统计：**17 出现 18 次**（`Views/Main/PhonePage.xaml:152-337` 整屏 10 处）、`10` 17 次、`14` 10 次、`18` 11 次、`16` 3 次、`20` 3 次、`9`/`11`/`32`/`90`/`52`/`44` 各 1~3 次。阶梯只有 4/8/12，`SnapRadius()` 会把它们吸附到最近一档（`StyleWorld.cs:789-797`），所以这些值在样式路径上是**无效**的；但在 `StrokeShape` 直接内联 `RoundRectangle` 的地方（如 `Views/Chat/QqPage.xaml:300`、`Views/Chat/WeChatPage.xaml:429`、`Views/Games/PetPage.xaml:22`）它们真的被画出来了。建议：把「聊天气泡 / 手机壳 / 头像」这三类按域正式登记进阶梯（例如气泡 = 12 与 4 的组合），其余一律吸附到 4/8/12。

**11. 间距令牌 14 / 18 不是 4 的倍数。**
`WSpaceLoose = round(PadCard × 0.85) = 14`、`WSpaceSection = round(PadCard × 1.15) = 18`（`StyleWorld.cs:709-710`）。四档令牌本身是好的（能跟窗口一起缩放），但与「4/8 基点」的说法不合；同时 `UI-STANDARD.md:385` 的自检清单又要求间距落在 `0/1/2/3/4/6/8/10/12/14/16/20` 里。建议：二选一，正式决定 14/18 是不是阶的一部分；若保留 4/8 基点则改成 16/16 或 16/20。

**12. `WMarkSize` 在同一次下发里被赋值两次。**
`StyleWorld.cs:693` 算出 `round(WSizeTitle × 1.85) = 37`，`:780` 又覆写为 `12.0`（世界签名层的印记尺寸）。后者生效，前者是死代码。建议：删掉 693 行那一句，或把两处合成一条并有注释的分支。

**13. 页面自己画阴影、不走令牌。**
`Views/Main/PhonePage.xaml:30`（`Brush="#4D000000" Offset="0,14" Radius="24"`）、`:662`（`#33000000 0,6 r14`）、`Views/Main/MainGamePhoneView.xaml:199`、`:223`。这些几何（0,14/r24、0,6/r14）与令牌化的三档（chip 0,3/r10、float 0,6/r18、panel 0,14/r34）都对不上。建议：手机壳这类「实物模型」如果确实需要自己的一套阴影，就用 `WShadowPanel*` 三个令牌，不再写字面量。

**14. 同一个「主按钮」有两个高度。**
`BtnSignal` 高 `WPillHeight = 42`（`ButtonShapes.xaml:165`），`BtnPrimary` 高 46（`:126`）。两者都是一屏唯一响的那颗，只差 4px，并排时看得出来。建议：`BtnSignal` 也用 46，或让 `WPillHeight` 从 46 派生。

**15. 裸 hex 仍在 137 处。**
`grep '#[0-9A-Fa-f]\{6,8\}' Views --include=*.xaml` = 137（注释里提到色值的不算）。除了第 5 条那族奶油色，还有 `Views/Main/MainGamePage.xaml:854`（`AppThemeBinding Light=#F7F1E8, Dark=#1E1B18`）、`Views/Main/PhonePage.xaml:24-26`（机身渐变 `#2B2622/#12100F/#241F1B`）、`Views/Main/MainGamePhoneView.xaml:95`、`:188`、`:466-472`。建议：AppThemeBinding 一律换成语义令牌（它绕过了 `StyleWorld`，是深色档里唯一不受控的东西）；机身/黑屏这类「装置本身」的颜色按 `OverlaySkin.xaml` 的先例集中成一个静态字典。

**16. `IsActive` 恒假，但形状代码仍在按它分支。**
`Controls/HudSkin.cs:26` 恒返回 `false`，所以 `Path_Shape` 永远走 `RoundRectangle`。页面里 `x:Static` 绑着这批形状与角标可见性，删不掉。建议：文档化保留原因（已做），并把 `HudPanelShape` / `CornerBracketDrawable` / `HudHatchDrawable` 标为「历史装饰类，不得用于新页面」。

**17. 响应式断点各页各写一个常数。**
`Views/Main/TitlePage.xaml.cs:22` = **820**，`Views/Chat/WeChatPage.xaml.cs:11` = **620**，其余页面用各自的 `Width < N`。没有断点令牌，所以同一窗口宽度下不同页面的「紧凑态」不同时发生。建议：把 820（顶栏塌成单列）与 620（聊天双栏变单栏）登记成两个宽度令牌，从 `StyleWorldTokens` 下发，页面只读数。

---

## 附录 A · 令牌速查

| 族 | 键前缀 | 定义位置 | 消费者 |
| --- | --- | --- | --- |
| 语义色 | `PageBg` `SurfaceBg` `WellBg` `InputBg` `SecondaryBg` `OverlayDim` | `StyleWorld.cs:431-436` | 页面、样式 |
| 墨阶 | `WarmDark` `SurfaceFg` `WarmBody` `MutedFg` `WarmFaint` `PrimaryFg` | `:454-460` | 文字 |
| 边界 | `BorderLine` `WControlEdge` `EdgeStrong` `WSep` `WRule` | `:463-467`、`:627`、`:635-637` | 描边、分隔线 |
| 强调 / 信号 / 状态 | `AccentWarm*` `Signal*` `Danger*` `GreenSoft` `RedSoft` `BlueSoft` `*Tint` | `:469-496`、`:553-557` | 按钮、徽章、Toast |
| 玻璃面 | `GlassBar*` `GlassCard*` `GlassButton*` `GlassDialog*` `GlassHairline` | `GlassTokens.cs:59-90` | `CardFrost`、按钮样式 |
| 玻璃色标 | `GTintTop/Mid/Low` `GTintAmount` `GSpecular` `GEdgeHi/Mid/Lo` | `StyleWorld.cs:750-760` | `GlassTokens`、`GlassFace` |
| 世界面 | `WSurfaceBrush` `WSurfaceRaised` `WWellBrush` `WBarBrush` `WEdgeBrush` `WEdgeWidth` | `:612-629` | 卡片样式 |
| 滑杆 / 液柱 | `WSliderTrackBrush` `WSliderFillBrush` `WSliderBulgeBrush` `WSliderRingBrush` `WSliderTension` `WSliderGlowBrush` `WSliderRingWidth` | `:508-549` | `ViewSlider`、`GlassSwitch` |
| 阴影 | `WShadowCardBrush` `WShadowDialogBrush` `WShadowChip*` `WShadowFloat*` `WShadowPanel*` | `:666-676` | 浮档、弹窗 |
| 圆角 | `WRadiusCard` `WRadiusRaised` `WRadiusWell` `WRadiusDialog` `WRadiusButton` `WRadiusPill` `WRadiusPillPx` `WPillRadius` | `:639-648`、`:695` | 形状 |
| 字号 | `WSizeDisplay/Title/Subhead/Body/Small/Caption/Brand` | `:678-686` | 文字样式 |
| 间距 | `WPadCard/Well/Row/Hero` `WSpaceTight/Regular/Loose/Section` `WGapItem/Section` `WPageMargin` `WHeaderPad/Gap/Height` | `:649-727` | 布局 |
| 尺寸 | `WRowHeight` `WMarkSize` `WMarkRadius` `WPillHeight` `WCardWidth(Wide)` `WRailWidth` | `:692-695`、`:779-780`、`:153-178` | 布局 |
| 宽度 / 焦点 | `WFocusRingWidth` `WDecorRingWidth` `WControlEdgeWidth` `WHairline` | `:630-633`、`:629` | 焦点、装饰 |
| 大气 | `WAtmoGrainOpacity` `WAtmoSpotOpacity` `WAtmoVignetteOpacity` `WMarkColor` | `:773-775`、`:591` | `WorldAtmosphere` |
| 动效 | `MotionTokens.*`（时长与四个曲线函数）+ `Spring/Settle/Snap/Mech/OutCubic` | `MotionTokens.cs` | 全部动画 |
| 声音 | `UiSoundKind` 五档 + `Friction(GlassKind)` | `UiSoundSynth.cs` | 交互反馈 |

## 附录 B · 新增控件的检查单

1. 面用 `GlassCardBg` / `GlassButtonBg` / `GlassPlate`，不写裸色。
2. 圆角只取 4 / 8 / 12（按钮取 8）。
3. 字号只取 `WSize*`；挂了样式键就不要再写 `FontSize`。
4. 内距只取 `WPad*` / `WSpace*`。
5. 时长与曲线只取 `MotionTokens`，过冲 ≤ 5%。
6. 按压只碰位移与缩放；不写 `Opacity` 做按压，不把 `Scale` 放进 VisualState。
7. 禁用态一个值（0.4），别自造第二档。
8. 文字色用墨阶令牌；需要自己算就用 `Step` + `Lock` 的公式（第 2.5 节），不要手挑颜色。
9. 可点元素补 `AutomationProperties.Name`；能 Tab 到的补 `Focusable` 与焦点环。
10. 声音只从五个语义音里选，`Friction` 不挂悬停与滚动。
