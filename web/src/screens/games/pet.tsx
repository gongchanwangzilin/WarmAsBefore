import type { ScreenProps } from '../types';

/**
 * 桌宠：Views/Games/PetPage.xaml（88 行）+ 同名 code-behind。
 * 这一页在两个平台上是两种东西：Windows 上只剩头顶那条对话小窗，角色本体由分层窗口贴在桌面上；
 * 其余平台是全屏问答页（立绘 + 名字角标 + 左下返回）。这里摆的是全屏那一面。
 * 立绘是图像资源，按约定用空框占位；名字角标（PetPage.NameLabel）由 PetService 解析角色后填，
 * 这一页读不到角色，落回源码对 TA 的通用称呼「TA」。
 */
export function Pet({ back }: ScreenProps) {
  return (
    <div
      style={{
        position: 'relative',
        width: '100%',
        height: '100%',
        minHeight: '100%',
        overflow: 'hidden',
        background: 'color-mix(in oklab, var(--w-ink-display) 82%, var(--w-bg))',
        color: 'var(--w-ink-on-signal)',
      }}
    >
      {/* 立绘区域：点击角色是「嘀咕一句」，双击回主窗口；这里单击 = 返回 */}
      <button
        type="button"
        aria-label="TA"
        onClick={back}
        style={{
          position: 'absolute',
          top: 58,
          bottom: 50,
          left: 0,
          right: 0,
          border: 0,
          background: 'none',
          padding: 0,
          cursor: 'pointer',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        <span
          aria-hidden="true"
          style={{
            width: 220,
            height: 300,
            border: '1px dashed color-mix(in oklab, var(--w-ink-on-signal) 45%, transparent)',
            borderRadius: 14,
          }}
        />
      </button>

      {/* 名字角标 */}
      <span
        style={{
          position: 'absolute',
          left: '50%',
          transform: 'translateX(-50%)',
          bottom: 6,
          padding: '5px 14px',
          borderRadius: 12,
          background: 'color-mix(in oklab, var(--w-ink-display) 70%, transparent)',
          fontSize: 'var(--w-size-caption)',
          color: 'var(--w-ink-on-signal)',
        }}
      >
        TA
      </span>

      {/* 返回：挪到左下角 */}
      <button
        type="button"
        onClick={back}
        style={{
          position: 'absolute',
          left: 8,
          bottom: 8,
          height: 28,
          padding: '0 10px',
          borderRadius: 14,
          border: 0,
          background: 'color-mix(in oklab, var(--w-ink-display) 70%, transparent)',
          color: 'color-mix(in oklab, var(--w-ink-on-signal) 78%, transparent)',
          font: 'inherit',
          fontSize: 'var(--w-size-caption)',
          cursor: 'pointer',
        }}
      >
        ← 返回
      </button>
    </div>
  );
}
