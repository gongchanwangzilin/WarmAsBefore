import { useState } from 'react';
import { PageShell } from '../../components/PageShell';
import { PageHeader, Card, Sep, Select } from '../../components/kit';
import type { ScreenProps } from '../types';
import { Cap, GhostSm, NoteRow, PickerPlaceholder, PrimarySm, Sub, Well } from './parts';

const FIELD_LABEL = {
  margin: '0 0 4px 2px',
  fontSize: 'var(--w-size-caption)',
  color: 'var(--w-ink-muted)',
} as const;

const FIELD = { fontSize: 'var(--w-size-small)' } as const;

/** 形象包里槽位与 figure.json 同一套名字。 */
const SLOTS = ['base', 'hair', 'top', 'bottom', 'shoes', 'accessory'];

/**
 * 形象包（FigurePackPage.xaml，277 行）：绑给谁 · 预览与动作 · AI 着装素材 · 导入 · 已安装。
 * 角色、包列表与预览都要后端（这里没有），按源码「还没有形象包」那一档渲染；
 * 输入框、行/列/帧率与三个下拉都是本地状态，选项取自源码。
 */
export function FigurePack({ back }: ScreenProps) {
  const [packId, setPackId] = useState('');
  const [name, setName] = useState('');
  const [prompt, setPrompt] = useState('');
  const [rows, setRows] = useState('1');
  const [columns, setColumns] = useState('4');
  const [slot, setSlot] = useState('top');
  const [motionId, setMotionId] = useState('walk');
  const [motionFps, setMotionFps] = useState('8');

  return (
    <PageShell>
      <PageHeader title="形象包" subtitle="新版着装 · 绑定后主界面与工作室一起换" onBack={back} />

      <div className="screen" style={{ maxWidth: 880, gap: 'var(--w-space-3)' }}>
        <Card>
          <Sub>绑给谁</Sub>
          <PickerPlaceholder title="选择角色" />
          <Cap>绑定只改这个角色的形象来源；旧版 SpriteMap 立绘不会被删，随时能解绑回去。</Cap>
        </Card>

        <Card>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-2)' }}>
            <Sub>预览与动作</Sub>
          </div>

          <div style={{ display: 'flex', gap: 'var(--w-space-3)', alignItems: 'flex-start' }}>
            <Well width={128} height={218} caption="点一个包开始预览" radius="12px" style={{ flexShrink: 0 }} />
            <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
              <PickerPlaceholder title="服装" />
              <PickerPlaceholder title="表情" />
              <PickerPlaceholder title="动作" />
              <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
                <PrimarySm disabled>播放</PrimarySm>
              </div>
            </div>
          </div>

          <Cap>
            动作预览只读当前包：走路动作按位移算相位，和主界面共用同一套 WalkCycle；页面离开会自动停。
          </Cap>
        </Card>

        <Card>
          <Sub>AI 着装素材</Sub>
          <Cap>
            生图模型画一张部件素材表 → 这里按网格切开并写入包 → 动画由外部按帧渲染或录成透明视频 → 回到这里导入动作。没有开启画面生成时不会假装成功。
          </Cap>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--w-space-2)' }}>
            <div>
              <div style={FIELD_LABEL}>包 id</div>
              <input
                className="k-field"
                style={FIELD}
                value={packId}
                placeholder="figure_maid"
                aria-label="包 id"
                onChange={(event) => setPackId(event.target.value)}
              />
            </div>
            <div>
              <div style={FIELD_LABEL}>显示名（可空）</div>
              <input
                className="k-field"
                style={FIELD}
                value={name}
                placeholder="新形象"
                aria-label="显示名（可空）"
                onChange={(event) => setName(event.target.value)}
              />
            </div>
          </div>

          <textarea
            className="k-field k-field--area"
            style={{ ...FIELD, minHeight: 64, height: 64 }}
            value={prompt}
            placeholder="例：同一位少女的头发、上衣、裙子、鞋子分件，透明背景"
            aria-label="生成提示词"
            onChange={(event) => setPrompt(event.target.value)}
          />

          <div style={{ display: 'flex', alignItems: 'flex-end', gap: 'var(--w-space-2)', flexWrap: 'wrap' }}>
            <div>
              <div style={FIELD_LABEL}>行</div>
              <input
                className="k-field"
                style={{ ...FIELD, width: 58 }}
                value={rows}
                inputMode="numeric"
                aria-label="行"
                onChange={(event) => setRows(event.target.value)}
              />
            </div>
            <div>
              <div style={FIELD_LABEL}>列</div>
              <input
                className="k-field"
                style={{ ...FIELD, width: 58 }}
                value={columns}
                inputMode="numeric"
                aria-label="列"
                onChange={(event) => setColumns(event.target.value)}
              />
            </div>
            <div>
              <div style={FIELD_LABEL}>槽位</div>
              <Select
                label="槽位"
                value={slot}
                options={SLOTS.map((item) => ({ value: item, label: item }))}
                onChange={setSlot}
              />
            </div>
            <div style={{ marginLeft: 'auto' }}>
              <PrimarySm disabled>生成并切图</PrimarySm>
            </div>
          </div>

          <Sep />

          <div style={{ display: 'grid', gridTemplateColumns: '1fr auto', gap: 'var(--w-space-2)' }}>
            <div>
              <div style={FIELD_LABEL}>动作 id</div>
              <input
                className="k-field"
                style={FIELD}
                value={motionId}
                placeholder="walk"
                aria-label="动作 id"
                onChange={(event) => setMotionId(event.target.value)}
              />
            </div>
            <div>
              <div style={FIELD_LABEL}>取样帧率</div>
              <input
                className="k-field"
                style={{ ...FIELD, width: 72 }}
                value={motionFps}
                inputMode="numeric"
                aria-label="取样帧率"
                onChange={(event) => setMotionFps(event.target.value)}
              />
            </div>
          </div>
          <Cap>把透明背景视频或按顺序排好的 PNG 帧录成动作。视频抽帧需要本机 ffmpeg；没有时请改用帧序列。</Cap>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
            <GhostSm disabled>导入帧序列…</GhostSm>
            <GhostSm disabled>导入透明视频…</GhostSm>
          </div>
        </Card>

        <Card>
          <Sub>导入形象包</Sub>
          <Cap>
            选一个 zip。压缩包根目录（或唯一的一层文件夹）里要有 figure.json，贴图路径按包内相对路径解析。
          </Cap>
          <div>
            <PrimarySm disabled>选择 zip 导入</PrimarySm>
          </div>
        </Card>

        <Cap style={{ color: 'var(--w-signal)' }}>
          还没有形象包。导入 zip 的按钮要等宿主开出选文件的门，现在点不动。
        </Cap>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-2)' }}>
            <Sub>已安装</Sub>
            <Cap>共 0 个</Cap>
          </div>

          <Card tight>
            <NoteRow>还没有形象包。导入一个 zip 就能开始用新版着装。</NoteRow>
          </Card>
        </div>

        <Cap>
          点任意一行选中它：上面会显示这一包的服装、表情与动作，绑定/解绑仍只作用于「绑给谁」里选中的角色。
        </Cap>
        <Cap>
          形象包由部件、服装、配色与动作组成；缺图或包损坏时会自动回退到旧版立绘，不会把角色变空白。
        </Cap>
      </div>
    </PageShell>
  );
}
