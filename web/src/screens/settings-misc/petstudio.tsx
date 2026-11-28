import type { CSSProperties } from 'react';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import { SignalButton } from '../../components/SignalButton';
import { Card, PageHeader, Sep } from '../../components/kit';
import type { ScreenProps } from '../types';
import { Inert } from './parts';

/**
 * AI 制作桌宠（Views/Games/PetStudioPage.xaml）：① 挑角色 → ② 一键生成 → ③ 逐状态出图与预览。
 * 出图要真实的画面生成接口，后端只有本地设置那几个方法，所以这里停在「还没配好、还没生成」那一档：
 * 三段结构、每个状态的帧数与状态行都在，按钮与进度条按源码的初始可见性摆。
 */

/** PetStudioService.DefaultStates：11 个状态的帧数（走动按步频算 6 帧，跳跃固定 4 帧）。 */
const STATES: { id: string; name: string; frames: number }[] = [
  { id: 'normal', name: '普通', frames: 1 },
  { id: 'happy', name: '开心', frames: 1 },
  { id: 'sit', name: '坐下', frames: 1 },
  { id: 'stand', name: '站起', frames: 1 },
  { id: 'walk', name: '走动', frames: 6 },
  { id: 'jump', name: '跳跃', frames: 4 },
  { id: 'love', name: '喜爱', frames: 1 },
  { id: 'angry', name: '生气', frames: 1 },
  { id: 'cry', name: '流泪', frames: 1 },
  { id: 'surprised', name: '惊讶', frames: 1 },
  { id: 'sleep', name: '睡着', frames: 1 },
];

const clamp = (v: number, min: number, max: number) => Math.min(max, Math.max(min, v));

/** PetStudioService.PlanWalkCadence：基准画布 320、体型 1、速度 120 DIP/s → 6 帧 · 6 fps。 */
function cadenceText(): string {
  const bodyHeight = clamp(320, 96, 720) * 1;
  const cycleSeconds = Math.max(1, bodyHeight) / 120;
  const fps = clamp(6 / cycleSeconds, 6, 10);
  return `走动循环：6 帧 · ${Number(fps.toFixed(1))} fps · ${Math.round(1000 / fps)} ms/帧（按步频算，非写死）`;
}

const IMAGE_HINT = '画面生成还没开启（设置 → AI → 画面生成 里填好接口与密钥），现在没法出图。';

const subhead: CSSProperties = {
  margin: 0,
  fontSize: 'var(--w-size-subhead)',
  fontWeight: 600,
  color: 'var(--w-ink-display)',
};
const rowTitle: CSSProperties = { margin: 0, fontSize: 'var(--w-size-body)', color: 'var(--w-ink-body)' };
const caption: CSSProperties = { margin: 0, fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)', lineHeight: 1.6 };

export function Petstudio({ back }: ScreenProps) {
  return (
    <PageShell>
      <PageHeader
        title="AI 制作桌宠"
        onBack={back}
        right={
          <span
            style={{
              maxWidth: 280,
              padding: '5px 10px',
              borderRadius: 99,
              background: 'var(--w-surface-tint)',
              color: 'var(--w-ink-label)',
              fontSize: 'var(--w-size-small)',
              whiteSpace: 'nowrap',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
            }}
          >
            {cadenceText()}
          </span>
        }
      />

      <main className="screen">
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)', width: '100%', maxWidth: 1000, margin: '0 auto' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
            <p style={subhead}>① 挑一位角色</p>
            <Card>
              <p style={{ ...caption, padding: '10px 0' }}>角色库里还没有角色，先去人物工作室建一位。</p>
            </Card>
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
            <p style={subhead}>② 一键生成整套状态动画</p>
            <Card>
              <p style={caption}>
                代码先按走动速度算好走动循环的帧数与步频，再逐状态串行出图；每出一帧就点亮下面对应的一行。进度是真的，不会用假动画糊弄你。
              </p>
              <p className="s-warn" style={{ marginTop: 0 }}>
                {IMAGE_HINT}
              </p>
              <div style={{ display: 'flex', gap: 'var(--w-space-2)' }}>
                <Inert>
                  <SignalButton>一键生成桌宠动画</SignalButton>
                </Inert>
                <Inert>
                  <GlassButton size="md">取消生成</GlassButton>
                </Inert>
              </div>
            </Card>
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
            <p style={subhead}>③ 状态动画与预览</p>
            <Card>
              <div style={{ display: 'flex', flexDirection: 'column' }}>
                {STATES.map((state, i) => (
                  <div key={state.id}>
                    <div
                      style={{
                        display: 'grid',
                        gridTemplateColumns: '64px minmax(0,1fr) auto',
                        gap: 'var(--w-space-2)',
                        alignItems: 'center',
                        padding: '10px 0',
                      }}
                    >
                      <div
                        aria-hidden="true"
                        style={{ height: 64, borderRadius: 'var(--w-radius-well)', background: 'var(--w-surface-tint)' }}
                      />
                      <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                        <span style={{ ...rowTitle, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{state.name}</span>
                        <span style={{ ...caption, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>待生成</span>
                      </div>
                      <span
                        style={{
                          padding: '5px 10px',
                          borderRadius: 99,
                          background: 'var(--w-surface-tint)',
                          color: 'var(--w-ink-label)',
                          fontSize: 'var(--w-size-caption)',
                          whiteSpace: 'nowrap',
                        }}
                      >
                        0/{state.frames} 帧
                      </span>
                    </div>
                    {i < STATES.length - 1 ? <Sep /> : null}
                  </div>
                ))}
              </div>
            </Card>
            <p style={caption}>
              走动、跳跃是多帧循环，其余状态多为单帧；生成完成后可在桌宠设置里把这套素材包接上播放。
            </p>
          </div>
        </div>
      </main>
    </PageShell>
  );
}
