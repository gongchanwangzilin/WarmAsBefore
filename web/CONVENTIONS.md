# Web 端复刻约定

这份文件写给「把一页 XAML 搬成 React」的人。目标只有一句话：**结构、文案、行序、尺寸逐条对齐源码，不发明任何东西。**

已经搬完、可以直接当样板的 1:1 案例：`src/pages/AppearancePage.tsx`（对应 `Views/Settings/SettingsSectionPage.xaml` 的 appearance 段）与 `src/pages/SettingsPage.tsx`。

## 一、文件与注册

- 一个路由一页，一页一个文件：`src/screens/<组>/<路由名>.tsx`，导出同名大驼峰组件，例如 `src/screens/save-chat/save.tsx` 导出 `Save`。
- 组目录下有 `index.tsx`，在里面加一行即可（键必须是路由名）：

```tsx
import { Save } from './save';

export const screens: Record<string, Screen> = {
  save: Save,
};
```

- 只改自己组里的文件。`routes.ts`、`App.tsx`、`components/kit.tsx`、`styles/kit.css` 与别的组都别碰。
- 不要 commit、不要 push、不要 `npm run build`、不要起 dev server（会踩到别人的端口）。

## 二、页面入参

```tsx
import type { ScreenProps } from '../types';

export function Save({ bridge, query, navigate, back }: ScreenProps) { ... }
```

- `bridge.request(method, params)` 只在后端已有该 method 时用；没有就用本地 `useState` 装源码里的**默认值**，并在页面上按源码的空态写。不要发明接口，也不要等后端。
- `navigate('/path')` 跳页，`back()` 返回。查询串从 `query.get('key')` 取。

## 三、通用件（`src/components/kit.tsx`）

| 件 | 对应源码 | 用途 |
| --- | --- | --- |
| `PageHeader` | `Controls/PageHeader.xaml` | 页头：返回 · 眉标/标题/副标题 · 右侧动作。`variant="bar"` 玻璃条，`variant="ground"` 无底 |
| `Card` | `Border Style=CardSolid` | 小标题 + 说明 + 内容的一张卡 |
| `Row` | 列表一行 | 标题 / 说明 / 右侧值 / `›`，`onClick` 整行可点 |
| `Sep` | `HSep` / `HSepBleed` | 发丝线，`bleed` 通到卡边 |
| `GroupLabel` | 分组小标题 | 「常驻」「全部设置」那一档 |
| `TextBtn` | `BtnText` | 只有文字的钮 |
| `Chip` | 小标签 | 「已启用」「终末地」 |
| `Stat` | 读数 | 上面一个数、下面一行名 |
| `Bar` | 占比条 | 0..1 |
| `Empty` | `Controls/EmptyState.xaml` | 两句空态：为什么空 + 怎么才不空 |
| `Grid2` | 双栏 | 宽屏两列、窄屏一列 |
| `Field` | `Entry` | 输入框；`multiline` 变多行 |
| `Select` | `Picker` | 原生下拉 |

材质与控件：`Glass`（六层玻璃）、`GlassSwitch`、`GlassSlider`、`SignalButton`（一屏一次的实心钮）、`PageShell`（滚动容器）。

## 四、硬规则

1. **不写死颜色。** 只用 `var(--w-*)` 令牌与墨阶 `var(--w-ink-display|body|label|muted|faint|on-signal)`。没有令牌可取时用 `var(--w-hairline)`、`var(--w-signal)`，不要出现 `#hex`、`rgb()`、`hsl()`。
2. **不用图标、不用 emoji、不装依赖。** 源码没有图标的地方不要加图标；有 `›`、`<`、`✦` 这类字符的就照抄字符。
3. **文案逐字照抄**，包括标点、空格、「·」分隔符与括号。切词、改写、补解释都算错。
4. **行序与层级照抄**：卡片顺序、组内行序、哪两行之间有线，源码怎么写就怎么写。
5. **空态两句**：原因 + 出路。源码里有原话就用原话。
6. **静态数据可以**，但只能用源码里的占位值（如「沈砚」「第三章 · 灯下补书」），不要编造看起来真实的假数据。
7. 数字与尺寸优先用令牌（`--w-row-h`、`--w-pad-card`、`--w-size-*`、`--w-radius-*`）；源码里明确写死的像素值（如 262px 侧栏、34px 色块、120px 滑杆）可以直接写。

## 五、版式适配（允许自己判断）

- 内容列：`<div className="screen">`（最宽 1120）或 `screen screen--narrow`（720）；铺满整窗用 `screen screen--full`。
- 源码里 `Width > 900` 的双栏、`Width <= 720` 的单列，直接用 `Grid2` 与 CSS 断点表达，不必复刻测量代码。
- 手机端（窄屏）可以重排成单列、按钮全宽，但**不得删内容**。

## 六、自检

```bash
cd web && npx tsc --noEmit
```

必须 0 错误。页面写完后在 `index.tsx` 里注册，缺注册等于没写。
