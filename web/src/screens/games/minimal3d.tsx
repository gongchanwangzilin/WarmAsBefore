import { useState, type CSSProperties } from 'react';
import { Glass } from '../../components/Glass';
import { GlassButton } from '../../components/GlassButton';
import { GlassSlider } from '../../components/GlassSlider';
import { PageHeader } from '../../components/kit';
import type { ScreenProps } from '../types';
import { Cap } from './parts';

/**
 * 3D 灰度视图：Views/Games/Minimal3DPage.xaml（56 行）+ 同名 code-behind。
 * 灰度把注意力留给空间感：街道平铺地面、建筑按层高挤出。
 * 这里用 CSS 透视画一条灰盒走廊示意（不用 WebGL），转向 / 走动 / 全景旋钮都真的动。
 */

const GRAY = {
  face: 'color-mix(in oklab, var(--w-ink-display) 22%, var(--w-surface-tint))',
  side: 'color-mix(in oklab, var(--w-ink-display) 34%, var(--w-surface-tint))',
  floor: 'color-mix(in oklab, var(--w-ink-display) 8%, var(--w-surface-tint))',
  sky: 'color-mix(in oklab, var(--w-ink-display) 2%, var(--w-surface-tint))',
  line: 'color-mix(in oklab, var(--w-ink-display) 16%, transparent)',
} as const;

const BUILDINGS: { x: number; z: number; w: number; h: number }[] = [
  { x: -290, z: -180, w: 150, h: 170 },
  { x: 300, z: -120, w: 170, h: 210 },
  { x: -330, z: -420, w: 190, h: 260 },
  { x: 340, z: -380, w: 160, h: 150 },
  { x: -320, z: -680, w: 210, h: 320 },
  { x: 330, z: -640, w: 200, h: 240 },
];

function headingOf(deg: number) {
  if (deg < 22.5 || deg >= 337.5) return '北';
  if (deg < 67.5) return '东北';
  if (deg < 112.5) return '东';
  if (deg < 157.5) return '东南';
  if (deg < 202.5) return '南';
  if (deg < 247.5) return '西南';
  if (deg < 292.5) return '西';
  return '西北';
}

export function Minimal3D({ back }: ScreenProps) {
  const [yaw, setYaw] = useState(0);
  const [walk, setWalk] = useState(0);
  const heading = headingOf(((yaw % 360) + 360) % 360);

  const stage: CSSProperties = {
    position: 'absolute',
    left: '50%',
    top: '50%',
    width: 0,
    height: 0,
    transformStyle: 'preserve-3d',
    transform: `translateZ(${walk}px) rotateX(-6deg) rotateY(${-yaw}deg)`,
  };

  const plane = (style: CSSProperties): CSSProperties => ({
    position: 'absolute',
    transformStyle: 'preserve-3d',
    ...style,
  });

  return (
    <div className="page" style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
      <PageHeader
        title="3D 灰度视图"
        backText="返回"
        onBack={back}
        right={<GlassButton size="sm">回鸟瞰</GlassButton>}
      />

      <div
        style={{
          position: 'relative',
          flex: 1,
          minHeight: 320,
          overflow: 'hidden',
          background: GRAY.sky,
        }}
        aria-label="3D 灰度视图"
      >
        <div style={{ position: 'absolute', inset: 0, perspective: '640px', perspectiveOrigin: '50% 48%' }}>
          <div style={stage}>
            {/* 地面：网格铺开 */}
            <div
              style={plane({
                left: -700,
                top: -700,
                width: 1400,
                height: 1400,
                transform: 'translateY(190px) rotateX(90deg)',
                background: `linear-gradient(${GRAY.line} 1px, transparent 1px), linear-gradient(90deg, ${GRAY.line} 1px, transparent 1px), ${GRAY.floor}`,
                backgroundSize: '60px 60px, 60px 60px, auto',
              })}
            />
            {/* 两侧街墙 */}
            <div
              style={plane({
                left: -700,
                top: -220,
                width: 1400,
                height: 410,
                transform: 'translateX(-360px) rotateY(90deg)',
                background: `linear-gradient(${GRAY.line} 1px, transparent 1px), ${GRAY.face}`,
                backgroundSize: '100% 52px',
              })}
            />
            <div
              style={plane({
                left: -700,
                top: -220,
                width: 1400,
                height: 410,
                transform: 'translateX(360px) rotateY(90deg)',
                background: `linear-gradient(${GRAY.line} 1px, transparent 1px), ${GRAY.face}`,
                backgroundSize: '100% 52px',
              })}
            />
            {/* 建筑：一块块灰盒 */}
            {BUILDINGS.map((box) => (
              <div key={`${box.x}-${box.z}`} style={plane({ left: 0, top: 0, transformStyle: 'flat' })}>
                <div
                  style={{
                    position: 'absolute',
                    left: box.x - box.w / 2,
                    top: 190 - box.h,
                    width: box.w,
                    height: box.h,
                    transform: `translateZ(${box.z}px)`,
                    background: GRAY.face,
                    border: `1px solid ${GRAY.side}`,
                  }}
                />
              </div>
            ))}
          </div>
        </div>
        {/* 地平线：远处雾起来 */}
        <div
          aria-hidden="true"
          style={{
            position: 'absolute',
            inset: 0,
            background: `radial-gradient(ellipse at 50% 48%, transparent 40%, ${GRAY.sky} 78%)`,
            pointerEvents: 'none',
          }}
        />
      </div>

      <Glass tone="card" radius={12} style={{ margin: '0 12px 12px' }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-1)', padding: 'var(--w-space-2)' }}>
          <Cap>拖动画面转向 · 电脑可用方向键走动 · 手机可选陀螺仪环视</Cap>
          <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
            <Cap>全景</Cap>
            <div style={{ flex: 1 }}>
              <GlassSlider
                label="全景"
                value={((yaw % 360) + 360) % 360 / 360}
                onChange={(next) => setYaw(next * 360)}
                width={280}
                format={(value) => `${Math.round(value * 360)}°`}
              />
            </div>
            <Cap style={{ width: 34, textAlign: 'center' }}>{heading}</Cap>
          </div>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--w-space-2)' }}>
            <GlassButton size="sm" onClick={() => setYaw(yaw - 12.857)}>
              ← 左转
            </GlassButton>
            <GlassButton size="sm" onClick={() => setWalk(walk + 16)}>
              ↑ 前进
            </GlassButton>
            <GlassButton size="sm" onClick={() => setWalk(walk - 10)}>
              ↓ 后退
            </GlassButton>
            <GlassButton size="sm" onClick={() => setYaw(yaw + 12.857)}>
              右转 →
            </GlassButton>
          </div>
        </div>
      </Glass>
    </div>
  );
}
