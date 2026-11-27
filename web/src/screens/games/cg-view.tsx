import type { ScreenProps } from '../types';

/**
 * CG 全屏查看：Views/Games/CgViewPage.xaml（35 行）+ 同名 code-behind。
 * 黑底整幅图（AspectFit）+ 底部一行标题 + 左上角半透明白的跳过钮。
 * 图像位按约定用空框占位；标题从查询串取（源码由 CgViewPayload 递进来）。
 */
export function CgView({ back, query }: ScreenProps) {
  const title = query.get('title') ?? '';

  return (
    <div
      style={{
        position: 'relative',
        width: '100%',
        height: '100%',
        minHeight: '100%',
        background: 'var(--w-ink-display)',
        overflow: 'hidden',
      }}
    >
      <div
        aria-label="CG"
        style={{
          position: 'absolute',
          left: '50%',
          top: '50%',
          transform: 'translate(-50%, -50%)',
          width: 'calc(100% - 96px)',
          height: 'calc(100% - 140px)',
          border: '1px dashed color-mix(in oklab, var(--w-ink-on-signal) 45%, transparent)',
          borderRadius: 10,
        }}
      />

      {title ? (
        <span
          style={{
            position: 'absolute',
            left: 16,
            right: 16,
            bottom: 30,
            textAlign: 'center',
            fontSize: 'var(--w-size-body)',
            color: 'var(--w-ink-on-signal)',
            opacity: 0.75,
            whiteSpace: 'nowrap',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
          }}
        >
          {title}
        </span>
      ) : null}

      <button
        type="button"
        aria-label="跳过"
        onClick={back}
        style={{
          position: 'absolute',
          left: 18,
          top: 54,
          padding: '10px 18px',
          border: 0,
          borderRadius: 'var(--w-radius-button)',
          background: 'color-mix(in oklab, var(--w-ink-on-signal) 22%, transparent)',
          color: 'var(--w-ink-on-signal)',
          font: 'inherit',
          fontSize: 'var(--w-size-title)',
          cursor: 'pointer',
          lineHeight: 1,
        }}
      >
        ✕
      </button>
    </div>
  );
}
