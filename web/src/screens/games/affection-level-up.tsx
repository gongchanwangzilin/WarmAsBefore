import { Glass } from '../../components/Glass';
import type { ScreenProps } from '../types';

/**
 * 好感提升过场：Views/Games/AffectionLevelUpPage.xaml（102 行）+ 同名 code-behind。
 * 背景（当前场景）→ 粉色纱幕 → 顶部丝带（好感等级提升！+「称号」· 名字）
 * → 中央爱心徽章（Lv.N + 名字 + 点数）→ 立绘 → 底部「轻触任意处返回」。
 * 徽章里的爱心与立绘都是图像资源，这里按约定用空框占位。
 */

const TITLES = ['初识', '相识', '熟悉', '亲近', '心动', '温柔', '依恋', '深情', '挚爱', '永恒'];

export function AffectionLevelUp({ back, query }: ScreenProps) {
  const level = Number(query.get('level') ?? '1');
  const safeLevel = Number.isFinite(level) ? Math.min(10, Math.max(1, Math.floor(level))) : 1;
  const points = Number(query.get('points') ?? '0');
  // 名字由升过级的那个人经 AffectionLevelUpService 带进来；这一页读不到角色时用源码对 TA 的通用称呼。
  const name = query.get('name') ?? 'TA';
  const title = TITLES[safeLevel - 1];

  return (
    <div
      role="button"
      tabIndex={0}
      onClick={back}
      onKeyDown={(event) => {
        if (event.key === 'Enter' || event.key === ' ') back();
      }}
      aria-label="轻触任意处返回"
      style={{
        position: 'relative',
        display: 'block',
        width: '100%',
        height: '100%',
        minHeight: '100%',
        border: 0,
        padding: 0,
        overflow: 'hidden',
        cursor: 'pointer',
        background: 'color-mix(in oklab, var(--w-signal) 42%, var(--w-bg))',
        color: 'var(--w-ink-on-signal)',
        font: 'inherit',
        textAlign: 'center',
      }}
    >
      {/* 半透明粉色纱幕 */}
      <span
        aria-hidden="true"
        style={{
          position: 'absolute',
          inset: 0,
          background: 'var(--w-signal)',
          opacity: 0.55,
        }}
      />

      {/* 顶部丝带 */}
      <div style={{ position: 'absolute', top: 70, left: 0, right: 0 }}>
        <div style={{ height: 74, background: 'var(--w-signal)', opacity: 0.96 }} />
        <div
          style={{
            position: 'absolute',
            top: 2,
            left: 0,
            right: 0,
            height: 30,
            background: 'color-mix(in oklab, var(--w-signal) 55%, var(--w-bg))',
            opacity: 0.9,
          }}
        />
        <div
          style={{
            position: 'absolute',
            top: 14,
            left: 0,
            right: 0,
            display: 'flex',
            flexDirection: 'column',
            gap: 2,
          }}
        >
          <span style={{ fontSize: 30, fontWeight: 700 }}>好感等级提升！</span>
          <span
            style={{
              fontSize: 17,
              color: 'color-mix(in oklab, var(--w-ink-on-signal) 82%, var(--w-signal))',
              whiteSpace: 'nowrap',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              padding: '0 16px',
            }}
          >
            「{title}」 · {name}
          </span>
        </div>
      </div>

      {/* 中央爱心徽章 */}
      <div
        style={{
          position: 'absolute',
          left: '50%',
          top: '50%',
          transform: 'translate(-50%, -50%)',
          marginTop: -60,
        }}
      >
        <Glass tone="card" radius={32} style={{ padding: '24px 30px', border: '2px solid var(--w-ink-on-signal)' }}>
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 'var(--w-space-1)' }}>
            <div
              aria-hidden="true"
              style={{
                width: 52,
                height: 52,
                borderRadius: 8,
                border: '1px dashed var(--w-ink-on-signal)',
                opacity: 0.7,
              }}
            />
            <span style={{ fontSize: 44, fontWeight: 700, lineHeight: 1.1 }}>
              Lv.{safeLevel}
            </span>
            <span style={{ fontSize: 16, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
              {name}
            </span>
            <span style={{ fontSize: 12, opacity: 0.85 }}>
              {Number.isFinite(points) ? Math.min(1000, Math.max(0, Math.floor(points))) : 0}/1000 · 跨过了 {safeLevel * 100} 点
            </span>
          </div>
        </Glass>
      </div>

      {/* 立绘：图像资源，空框占位；按舞台高度定高（源码 AspectFit + MaximumWidthRequest=460） */}
      <div
        aria-hidden="true"
        style={{
          position: 'absolute',
          bottom: 0,
          left: '50%',
          transform: 'translateX(-50%)',
          height: '42%',
          aspectRatio: '3 / 4',
          maxWidth: '80%',
          border: '1px dashed color-mix(in oklab, var(--w-ink-on-signal) 55%, transparent)',
          borderRadius: 12,
          opacity: 0.5,
        }}
      />

      <span
        style={{
          position: 'absolute',
          left: 0,
          right: 0,
          bottom: 22,
          fontSize: 13,
          color: 'var(--w-ink-on-signal)',
          opacity: 0.75,
        }}
      >
        轻触任意处返回
      </span>
    </div>
  );
}
