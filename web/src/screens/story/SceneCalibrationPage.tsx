import { useState } from 'react';
import { Card, PageHeader, TextBtn } from '../../components/kit';
import { Segmented, type SegmentedOption } from '../../components/Segmented';
import { Glass } from '../../components/Glass';
import { PageShell } from '../../components/PageShell';
import { SignalButton } from '../../components/SignalButton';
import type { ScreenProps } from '../types';
import { Actions, Inert, Stack } from './parts';

type Landing = 'point' | 'full' | 'linear';
type Scale = 'dualLine' | 'spheres';

const LANDING_NAME: Record<Landing, string> = { point: '点选', full: '整图可站', linear: '线性' };
const SCALE_NAME: Record<Scale, string> = { dualLine: '双线', spheres: '圆球' };

const LANDING_OPTIONS: SegmentedOption<Landing>[] = [
  { value: 'point', label: LANDING_NAME.point },
  { value: 'full', label: LANDING_NAME.full },
  { value: 'linear', label: LANDING_NAME.linear },
];

const SCALE_OPTIONS: SegmentedOption<Scale>[] = [
  { value: 'dualLine', label: SCALE_NAME.dualLine },
  { value: 'spheres', label: SCALE_NAME.spheres },
];

/**
 * 背景标定：SceneCalibrationPage.xaml。
 * 画布与命中/撤销/缩放都在 GraphicsView + Drawable 里，没有前端 method，
 * 这页只复刻工具条、三行强制输出与两项缺口校验（几何全为空时的原话）。
 */
export function SceneCalibrationPage({ back }: ScreenProps) {
  const [landing, setLanding] = useState<Landing>('point');
  const [scale, setScale] = useState<Scale>('spheres');
  const [status, setStatus] = useState('拖空白处开始标定');

  const summary =
    `① 落脚点类型：${LANDING_NAME[landing]}` +
    (landing === 'point' ? '（0 个点）' : landing === 'full' ? '（整张图均可站立，无落脚几何）' : '（0 条线，总长 0）') +
    '\n② 缩放标定方式：' +
    SCALE_NAME[scale] +
    '\n③ 标定数据：' +
    (scale === 'dualLine' ? '双线数据不可用' : '0 个球，半径 []，基准半径 0，整体系数 1，参考身高占比 0.5');

  const missing: string[] = [];
  if (landing === 'point') missing.push('落脚点未标定：点选模式至少要指定 1 个落脚点');
  if (landing === 'linear') missing.push('落脚点未标定：线性模式至少要画 1 条可站立的线');
  if (scale === 'dualLine') missing.push('缩放标定未完成：双线方式需要恰好 2 条线，当前 0 条');
  if (scale === 'spheres') missing.push('缩放标定未完成：圆球方式至少需要 2 个圆球（近处一个、远处一个），当前 0 个');

  return (
    <PageShell>
      <PageHeader
        onBack={back}
        title="标定背景：未命名场景"
        subtitle="原图尺寸未知（视频背景可能如此）"
        right={
          <>
            <span className="cap">{status}</span>
            <TextBtn onClick={back}>取消</TextBtn>
          </>
        }
      />

      <Glass as="div" tone="bar" radius={0}>
        <Stack gap={2} style={{ padding: 'var(--w-space-2) var(--w-pad-page)' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-2)', flexWrap: 'wrap' }}>
            <span className="cap" style={{ width: 60 }}>落脚点</span>
            <Segmented
              label="落脚点类型"
              tone="ghost"
              value={landing}
              options={LANDING_OPTIONS}
              onChange={(next) => {
                setLanding(next);
                setStatus(`落脚点类型：${LANDING_NAME[next]}`);
              }}
            />
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-2)', flexWrap: 'wrap' }}>
            <span className="cap" style={{ width: 60 }}>缩放</span>
            <Segmented
              label="缩放标定方式"
              tone="ghost"
              value={scale}
              options={SCALE_OPTIONS}
              onChange={(next) => {
                setScale(next);
                setStatus(`缩放标定方式：${SCALE_NAME[next]}`);
              }}
            />
            <TextBtn onClick={() => setStatus('需要恰好两条缩放线才能翻转')}>⇄ 翻转第二条线</TextBtn>
            <Inert>
              <TextBtn>↻ 适应窗口</TextBtn>
            </Inert>
          </div>

          <span className="cap">
            拖空白=新建 · 拖顶点/球心=移动 · 拖球缘=改半径 · 双击=删除（线身双击=插折点） · 右键=撤销 · 滚轮/双指=缩放 · 选「平移」后可拖动画面
          </span>
        </Stack>
      </Glass>

      <div
        style={{
          height: 'clamp(220px, 44vh, 560px)',
          margin: '0 var(--w-pad-page)',
          borderRadius: 'var(--w-radius-well)',
          background: 'var(--w-ink-display)',
        }}
        aria-hidden="true"
      />

      <div className="screen" style={{ paddingTop: 0 }}>
        <Card>
          <p style={{ margin: 0, fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)', whiteSpace: 'pre-wrap', lineHeight: 1.6 }}>
            {summary}
          </p>
          {missing.length > 0 ? (
            <p style={{ margin: 0, fontSize: 'var(--w-size-small)', color: 'var(--w-signal)', whiteSpace: 'pre-wrap', lineHeight: 1.6 }}>
              {`还差：\n${missing.map((m, i) => `${i + 1}. ${m}`).join('\n')}`}
            </p>
          ) : null}
          <Actions end>
            <TextBtn onClick={() => setStatus('画布归宿主画，这里没有可清的几何：标定要等场景标定接上')}>清空重标</TextBtn>
            <SignalButton disabled={missing.length > 0}>完成标定</SignalButton>
          </Actions>
        </Card>
      </div>
    </PageShell>
  );
}
