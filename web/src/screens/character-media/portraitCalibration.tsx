import { useState } from 'react';
import { PageShell } from '../../components/PageShell';
import { PageHeader, Card, Sep } from '../../components/kit';
import type { ScreenProps } from '../types';
import { Cap, GhostSm, PrimarySm, RowLine } from './parts';

/** 预览框：与逻辑画布同比例（9:16）的手机舞台尺寸，源码里写死 270 宽。 */
const FRAME_W = 270;
const FRAME_H = 480;
/** 没有素材时 VM 用 400×640 当有效尺寸、16:9 当背景，落脚点在画面 90% 高度处。 */
const SRC_W = 400;
const SRC_H = 640;
const DEFAULT_ANCHOR_Y = 0.9;

const round = (value: number) => Math.round(value * 100) / 100;

/**
 * 立绘标定（PortraitCalibrationPage.xaml，185 行）：预览框 + 三条读数 + 方向 / 大小 / 位置三张卡 + 规范化。
 * 换算照 PortraitCalibrationViewModel.Recalc 的那一套（同宽高、同落脚点），所以按钮按下时预览真的会动；
 * 素材与场景标定要后端（这里没有），素材尺寸显示「读取中…」、脚底线走默认落脚点。
 */
export function PortraitCalibration({ back }: ScreenProps) {
  const [flipH, setFlipH] = useState(false);
  const [flipV, setFlipV] = useState(false);
  const [rotation, setRotation] = useState(0);
  const [scale, setScale] = useState(1);
  const [offsetX, setOffsetX] = useState(0);
  const [offsetY, setOffsetY] = useState(0);
  const [footLine, setFootLine] = useState(1);

  const swap = rotation % 180 !== 0;
  const effW = swap ? SRC_H : SRC_W;
  const effH = swap ? SRC_W : SRC_H;
  const fit = Math.min(FRAME_W / effW, FRAME_H / effH);
  const baseW = round(effW * fit);
  const baseH = round(effH * fit);

  const finalScale = Math.min(8, Math.max(0.02, scale));
  const anchorX = FRAME_W / 2;
  const anchorY = DEFAULT_ANCHOR_Y * FRAME_H;
  const previewX = round(anchorX - FRAME_W / 2 + offsetX * FRAME_W);
  const previewY = round(
    anchorY - FRAME_H / 2 - (baseH * finalScale) / 2 + baseH * finalScale * (1 - footLine) + offsetY * FRAME_H,
  );
  const groundLineY = round(anchorY);

  const status = `有效尺寸 ${effW}×${effH} · 显示 ${Math.round(baseW * finalScale)}×${Math.round(
    baseH * finalScale,
  )} · 缩放 ${finalScale.toFixed(2)}`;

  const reset = () => {
    setFlipH(false);
    setFlipV(false);
    setRotation(0);
    setScale(1);
    setOffsetX(0);
    setOffsetY(0);
    setFootLine(1);
  };

  return (
    <PageShell>
      <PageHeader title="立绘标定" onBack={back} />

      <div className="screen" style={{ maxWidth: 760, gap: 'var(--w-space-3)' }}>
        <Cap>
          立绘按 1080×1920 的逻辑画布对齐。允许角色溢出画布，但脚底必须落在这块画布的下沿线上，大小与朝向也要正确。
        </Cap>

        <div
          style={{
            position: 'relative',
            width: FRAME_W,
            height: FRAME_H,
            margin: '0 auto',
            overflow: 'hidden',
            borderRadius: 'var(--w-radius-well)',
            border: '1px solid var(--w-hairline)',
            background: 'var(--w-bg)',
          }}
        >
          <div
            style={{
              position: 'absolute',
              left: '50%',
              top: '50%',
              width: baseW,
              height: baseH,
              marginLeft: -baseW / 2,
              marginTop: -baseH / 2,
              transform: `translate(${previewX}px, ${previewY}px) rotate(${rotation}deg) scale(${
                (flipH ? -1 : 1) * finalScale
              }, ${(flipV ? -1 : 1) * finalScale})`,
              border: '1px solid var(--w-hairline)',
              boxSizing: 'border-box',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)' }}>立绘</span>
          </div>

          <div
            style={{
              position: 'absolute',
              left: 0,
              right: 0,
              top: groundLineY,
              height: 1.5,
              background: 'var(--w-signal)',
              opacity: 0.9,
            }}
          />
          <span
            style={{
              position: 'absolute',
              right: 4,
              top: groundLineY,
              transform: 'translateY(-12px)',
              padding: '2px 6px',
              borderRadius: 'var(--w-radius-well)',
              background: 'var(--w-surface-tint)',
              fontSize: 'var(--w-size-caption)',
              color: 'var(--w-ink-muted)',
            }}
          >
            脚底基准线
          </span>
        </div>

        <Card tight>
          <div style={{ padding: '4px 0', fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)' }}>
            素材尺寸：读取中…
          </div>
          <Sep bleed />
          <div style={{ padding: '4px 0', fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)' }}>{status}</div>
          <Sep bleed />
          <Cap>
            当前场景没有可用标定，按游戏默认居中 + 落脚点 0.9（画面 90% 高度处）
          </Cap>
        </Card>

        <Card tight>
          <RowLine
            title="方向"
            right={
              <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'flex-end', gap: 'var(--w-space-2)' }}>
                <GhostSm onClick={() => setFlipH((v) => !v)}>水平翻转</GhostSm>
                <GhostSm onClick={() => setFlipV((v) => !v)}>垂直翻转</GhostSm>
                <GhostSm onClick={() => setRotation((v) => (v + 90) % 360)}>旋转 90°</GhostSm>
              </div>
            }
          />
        </Card>

        <Card tight>
          <RowLine
            title="大小（>100% 即溢出画布）"
            right={
              <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'flex-end', alignItems: 'center', gap: 'var(--w-space-2)' }}>
                <GhostSm width={80} onClick={() => setScale((v) => Math.max(0.1, Math.round((v - 0.05) * 100) / 100))}>
                  缩小
                </GhostSm>
                <span
                  style={{
                    width: 56,
                    textAlign: 'center',
                    fontSize: 'var(--w-size-small)',
                    color: 'var(--w-ink-body)',
                    fontVariantNumeric: 'tabular-nums',
                  }}
                >
                  {Math.round(scale * 100)}%
                </span>
                <GhostSm width={80} onClick={() => setScale((v) => Math.min(4, Math.round((v + 0.05) * 100) / 100))}>
                  放大
                </GhostSm>
              </div>
            }
          />
        </Card>

        <Card tight>
          <RowLine
            title="位置与脚底线"
            right={
              <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'flex-end', gap: 'var(--w-space-2)' }}>
                <GhostSm width={52} onClick={() => setOffsetX((v) => round(v - 0.01))}>
                  ←
                </GhostSm>
                <GhostSm width={52} onClick={() => setOffsetX((v) => round(v + 0.01))}>
                  →
                </GhostSm>
                <GhostSm width={52} onClick={() => setOffsetY((v) => round(v - 0.01))}>
                  ↑
                </GhostSm>
                <GhostSm width={52} onClick={() => setOffsetY((v) => round(v + 0.01))}>
                  ↓
                </GhostSm>
              </div>
            }
          />
          <Sep />
          <RowLine
            title="脚底线"
            right={
              <div style={{ display: 'flex', flexWrap: 'wrap', justifyContent: 'flex-end', gap: 'var(--w-space-2)' }}>
                <GhostSm width={96} onClick={() => setFootLine((v) => round(Math.max(0.5, v - 0.01)))}>
                  抬脚线上移
                </GhostSm>
                <GhostSm width={96} onClick={() => setFootLine((v) => round(Math.min(1, v + 0.01)))}>
                  回到底边
                </GhostSm>
              </div>
            }
          />
        </Card>

        <div style={{ display: 'flex', gap: 'var(--w-space-2)', justifyContent: 'center' }}>
          <GhostSm onClick={reset}>重置</GhostSm>
          <GhostSm disabled style={{ color: 'var(--w-signal)' }}>
            清除标定
          </GhostSm>
          <PrimarySm disabled>保存</PrimarySm>
        </div>

        <Card tight>
          <RowLine
            title={
              <span style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-1)' }}>
                <span>规范化（可选）</span>
                <span className="k-row__desc">
                  标定只记「怎么映射」，不改文件；规范化会按当前的翻转与旋转，真的生成一张 1080×1920
                  的 PNG（等比居中、余量透明），并把角色的立绘指向它。
                </span>
                <span className="k-row__desc">
                  余量留透明会让角色的显示占比变小——素材比例越接近 9:16，透明边越少、越贴合。
                </span>
              </span>
            }
            right={<GhostSm disabled>生成 1080×1920 标准画布文件</GhostSm>}
          />
          <Sep />
        </Card>
      </div>
    </PageShell>
  );
}
