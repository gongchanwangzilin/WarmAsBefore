import { Fragment, useId, type CSSProperties, type KeyboardEvent, type ReactNode } from 'react';

/**
 * 药丸分段开关：一条胶囊、一个滑动的滑块、真 radio 驱动。
 *
 * 选中态完全由 CSS 的 `input:checked + label` 兄弟选择器画出；React 只把当前值换算成
 * 滑块的格子号（`--k-seg-i`），不维护任何选中状态机。于是键盘、读屏、点击都走原生 radio。
 *
 *   size: sm（28 高，行内用） / md（34 高，页头、整排页签）
 *   tone: solid（滑块 = 墨色，选中字 = 反色） / ghost（透明壳，滑块 = 淡墨）
 */

type Size = 'sm' | 'md';
type Tone = 'solid' | 'ghost';

export type SegmentedOption<T extends string> = { value: T; label: ReactNode };

export type SegmentedNestedOption<O extends string, S extends string> = {
  value: O;
  label: ReactNode;
  /** 这一格内部再嵌一条更小的胶囊；选中子项时外层一并选中。 */
  sub?: {
    label: string;
    value: S;
    options: readonly SegmentedOption<S>[];
    onChange: (next: S) => void;
  };
};

type SegmentedProps<T extends string> = {
  value: T;
  options: readonly SegmentedOption<T>[];
  onChange: (next: T) => void;
  /** 整组的可读名字（读屏念出来的那一句）。 */
  label: string;
  size?: Size;
  tone?: Tone;
  /** 窄窗换行：滑块收起，选中的那一格自己填墨。 */
  wrap?: boolean;
  className?: string;
};

type NestedProps<O extends string, S extends string> = {
  value: O;
  options: readonly SegmentedNestedOption<O, S>[];
  onChange: (next: O) => void;
  label: string;
  size?: Size;
  tone?: Tone;
  className?: string;
};

const cx = (...parts: (string | false | undefined)[]) => parts.filter(Boolean).join(' ');

/** ← ↑ 往前一格，→ ↓ 往后一格，Home / End 到两头；到头绕回。 */
function step(
  event: KeyboardEvent<HTMLDivElement>,
  current: number,
  count: number,
  pick: (index: number) => void,
  focus: (index: number) => void,
) {
  const to =
    event.key === 'ArrowLeft' || event.key === 'ArrowUp'
      ? current - 1
      : event.key === 'ArrowRight' || event.key === 'ArrowDown'
        ? current + 1
        : event.key === 'Home'
          ? 0
          : event.key === 'End'
            ? count - 1
            : null;
  if (to === null) return;
  event.preventDefault();
  const next = (to + count) % count;
  pick(next);
  focus(next);
}

/** 隐形的「最宽标签」量尺：每格塞同一份，格子于是等宽，滑块按格平移。 */
function Fit({ labels }: { labels: ReactNode[] }) {
  return (
    <span className="k-seg__fit" aria-hidden="true">
      {labels.map((item, i) => (
        <span key={i}>{item}</span>
      ))}
    </span>
  );
}

/** 一条分段轨：外壳 + 滑块 + n 组「input + label」。 */
function Track<T extends string>({
  value,
  options,
  onChange,
  label,
  size,
  tone,
  wrap,
  className,
  children,
}: SegmentedProps<T> & {
  /** 格子由调用方给：一级是 input+label，二级有一格是自带小胶囊的宿主。 */
  children: ReactNode;
}) {
  const uid = useId().replace(/:/g, '');
  const group = `seg-${uid}`;
  const index = Math.max(0, options.findIndex((option) => option.value === value));
  const idOf = (i: number) => `${group}-${i}`;
  const focusAt = (i: number) => document.getElementById(idOf(i))?.focus();

  return (
    <div
      className={cx('k-seg', className)}
      role="radiogroup"
      aria-label={label}
      data-size={size}
      data-tone={tone}
      data-wrap={wrap ? '1' : undefined}
      style={
        {
          '--k-seg-n': options.length,
          '--k-seg-i': index,
        } as CSSProperties
      }
      onKeyDown={(event) =>
        step(
          event,
          index,
          options.length,
          (i) => {
            const option = options[i];
            if (option && option.value !== value) onChange(option.value);
          },
          focusAt,
        )
      }
    >
      {children}
    </div>
  );
}

function SegmentedBase<T extends string>(props: SegmentedProps<T>) {
  const { value, options, onChange, size = 'sm', tone = 'solid' } = props;
  const uid = useId().replace(/:/g, '');
  const group = `seg-${uid}`;
  const fit = options.map((option) => option.label);

  return (
    <Track {...props} size={size} tone={tone} options={options} value={value} onChange={onChange}>
      <i className="k-seg__thumb" aria-hidden="true" />
      {options.map((option, i) => (
        <Fragment key={option.value}>
          <input
            className="k-seg__in"
            type="radio"
            id={`${group}-${i}`}
            name={group}
            value={option.value}
            checked={option.value === value}
            onChange={() => onChange(option.value)}
          />
          <label className="k-seg__seg" htmlFor={`${group}-${i}`}>
            <Fit labels={fit} />
            <span className="k-seg__txt">{option.label}</span>
          </label>
        </Fragment>
      ))}
    </Track>
  );
}

/**
 * 二级：外层胶囊里再嵌一条小胶囊（「大类」里套「子项」）。
 * 小胶囊是它自己的 radio 组；点/选子项时外层一并选中。
 */
function Nested<O extends string, S extends string>({
  value,
  options,
  onChange,
  label,
  size = 'md',
  tone = 'solid',
  className,
}: NestedProps<O, S>) {
  const uid = useId().replace(/:/g, '');
  const group = `seg-${uid}`;
  const idOf = (i: number) => `${group}-${i}`;

  /** 量尺要把小胶囊整条也算进来：子项标签并成一整行，外层格子才容得下内层胶囊。 */
  const fit: ReactNode[] = [];
  options.forEach((option) => {
    fit.push(option.label);
    if (option.sub) {
      fit.push(
        <span key={`fit-${option.value}`}>
          {option.sub.options.map((sub, i) => (
            <span key={sub.value}>
              {i > 0 ? ' ' : ''}
              {sub.label}
            </span>
          ))}
        </span>,
      );
    }
  });

  return (
    <Track
      value={value}
      options={options}
      onChange={onChange}
      label={label}
      size={size}
      tone={tone}
      className={cx('k-seg--nested', className)}
    >
      <>
        <i className="k-seg__thumb" aria-hidden="true" />
        {options.map((option, i) =>
          option.sub ? (
            <Fragment key={option.value}>
              <input
                className="k-seg__in"
                type="radio"
                id={idOf(i)}
                name={group}
                value={option.value}
                checked={option.value === value}
                onChange={() => onChange(option.value)}
              />
              <div className="k-seg__host">
                <label className="k-seg__seg k-seg__seg--host" htmlFor={idOf(i)}>
                  <Fit labels={fit} />
                  <span className="k-seg__txt">{option.label}</span>
                </label>
                <SubTrack
                  sub={option.sub}
                  onEnterHost={() => {
                    if (option.value !== value) onChange(option.value);
                  }}
                />
              </div>
            </Fragment>
          ) : (
            <Fragment key={option.value}>
              <input
                className="k-seg__in"
                type="radio"
                id={idOf(i)}
                name={group}
                value={option.value}
                checked={option.value === value}
                onChange={() => onChange(option.value)}
              />
              <label className="k-seg__seg" htmlFor={idOf(i)}>
                <Fit labels={fit} />
                <span className="k-seg__txt">{option.label}</span>
              </label>
            </Fragment>
          ),
        )}
      </>
    </Track>
  );
}

/** 内层小胶囊：自己的 radio 组、自己的滑块；按键不往外套传。 */
function SubTrack<O extends string, S extends string>({
  sub,
  onEnterHost,
}: {
  sub: NonNullable<SegmentedNestedOption<O, S>['sub']>;
  onEnterHost: () => void;
}) {
  const uid = useId().replace(/:/g, '');
  const group = `sub-${uid}`;
  const index = Math.max(0, sub.options.findIndex((option) => option.value === sub.value));
  const idOf = (i: number) => `${group}-${i}`;
  const focusAt = (i: number) => document.getElementById(idOf(i))?.focus();

  return (
    <div
      className="k-seg k-seg--sub"
      role="radiogroup"
      aria-label={sub.label}
      style={
        {
          '--k-seg-n': sub.options.length,
          '--k-seg-i': index,
        } as CSSProperties
      }
      onKeyDown={(event) => {
        event.stopPropagation();
        step(
          event,
          index,
          sub.options.length,
          (i) => {
            const option = sub.options[i];
            if (!option) return;
            onEnterHost();
            if (option.value !== sub.value) sub.onChange(option.value);
          },
          focusAt,
        );
      }}
    >
      <i className="k-seg__thumb" aria-hidden="true" />
      {sub.options.map((option, i) => (
        <Fragment key={option.value}>
          <input
            className="k-seg__in"
            type="radio"
            id={idOf(i)}
            name={group}
            value={option.value}
            checked={option.value === sub.value}
            onChange={() => {
              onEnterHost();
              sub.onChange(option.value);
            }}
          />
          <label className="k-seg__seg" htmlFor={idOf(i)}>
            <Fit labels={sub.options.map((item) => item.label)} />
            <span className="k-seg__txt">{option.label}</span>
          </label>
        </Fragment>
      ))}
    </div>
  );
}

export const Segmented = Object.assign(SegmentedBase, { Nested });
