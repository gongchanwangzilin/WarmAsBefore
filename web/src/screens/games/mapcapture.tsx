import { useState } from 'react';
import { Glass } from '../../components/Glass';
import { GlassButton } from '../../components/GlassButton';
import { GlassSlider } from '../../components/GlassSlider';
import { PageShell } from '../../components/PageShell';
import { PageHeader, Select } from '../../components/kit';
import type { ScreenProps } from '../types';
import { Cap, Section, Well } from './parts';

/**
 * 取景与导入：Views/Games/MapCapturePage.xaml（160 行）+ 同名 code-behind。
 * 手机端是取景会话（规定开始地点 → 走路随拍），电脑端是照片导入；
 * 电脑端 CaptureAvailable 为 false，所以「规定开始地点」整张卡与拍照钮不出现，换成那句「电脑端不取景」。
 */

const STREETS = [{ value: '中山路', label: '中山路' }];

export function MapCapture({ back }: ScreenProps) {
  const [street, setStreet] = useState(true);
  const [streetPick, setStreetPick] = useState('中山路');
  const [streetT, setStreetT] = useState(0.5);

  return (
    <PageShell>
      <PageHeader title="取景与导入" subtitle="还没出发" onBack={back} />

      <div className="screen" style={{ maxWidth: 720, gap: 'var(--w-space-3)' }}>
        <Glass tone="card" radius={12}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)', padding: 'var(--w-space-2)' }}>
            <Section>照片进出</Section>
            <Cap>电脑端不能取景：照片传过来在这里导入。图上会先落成「未绑」，选中一张再记绑定。</Cap>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--w-space-2)' }}>
              <GlassButton size="sm">导入照片…</GlassButton>
              <GlassButton size="sm">打开取景包…</GlassButton>
              <GlassButton size="sm">导出取景包…</GlassButton>
            </div>
          </div>
        </Glass>

        <Glass tone="card" radius={12}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)', padding: 'var(--w-space-2)' }}>
            <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 'var(--w-space-2)' }}>
              <Section>这一组绑定</Section>
              <Well
                active={street}
                onClick={() => setStreet(true)}
                style={{ padding: '4px 12px', border: '1px solid var(--w-hairline)' }}
              >
                街道
              </Well>
              <Well
                active={!street}
                onClick={() => setStreet(false)}
                style={{ padding: '4px 12px', border: '1px solid var(--w-hairline)' }}
              >
                室内
              </Well>
            </div>
            <Cap>这组绑定给谁：新拍的那张</Cap>
            <div>
              <GlassButton size="md">记上这组绑定</GlassButton>
            </div>

            {street ? (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
                <Cap>街道照落在这条街上：选街 → 拉位置。同一街道不同位置可各拍一张。</Cap>
                <Select value={streetPick} options={STREETS} onChange={setStreetPick} label="选一条街" />
                <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
                  <div style={{ flex: 1 }}>
                    <GlassSlider
                      label="位置"
                      value={streetT}
                      onChange={setStreetT}
                      width={260}
                      format={(value) => `位置 ${Math.round(value * 100)}%`}
                    />
                  </div>
                  <Cap>位置 {Math.round(streetT * 100)}%</Cap>
                </div>
              </div>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
                <Cap>室内照必须落在一层一户上：楼栋 → 楼层 → 户型（+ 门/特殊地点）。</Cap>
                <span>
                  <Select value="" options={[{ value: '', label: '选楼栋' }]} onChange={() => undefined} label="选楼栋" />
                </span>
                <span>
                  <Select value="" options={[{ value: '', label: '选楼层' }]} onChange={() => undefined} label="选楼层" />
                </span>
                <span>
                  <Select value="" options={[{ value: '', label: '选户型（场景）' }]} onChange={() => undefined} label="选户型（场景）" />
                </span>
                <span>
                  <Select
                    value=""
                    options={[{ value: '', label: '门 / 特殊地点（可不选）' }]}
                    onChange={() => undefined}
                    label="门 / 特殊地点（可不选）"
                  />
                </span>
              </div>
            )}

            <Cap>电脑端不取景：照片先「导入」，再回来这里拣街/拣户型补绑。</Cap>
          </div>
        </Glass>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-1)' }}>
          <Section>已挂上的照片</Section>
          <Cap>还没有照片。手机端拍，或上面「导入照片」。</Cap>
        </div>

        <div style={{ height: 1, background: 'var(--w-hairline)' }} />
      </div>
    </PageShell>
  );
}
