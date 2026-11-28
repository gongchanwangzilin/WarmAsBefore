import { useState } from 'react';
import type { ScreenProps } from '../types';
import { Cap, Fab, Section, ToolRow, Well } from './parts';

/**
 * 地图：Views/Games/MapPage.xaml（439 行）+ 同名 code-behind。
 * 高德式全屏地图：画布铺满整页，控件浮在图上，没有顶栏。
 * 左上搜索胶囊与状态条、右上图层钮、右侧缩放 / 定位 / 指南针、左下比例尺、
 * 底部抽屉；图层面板从右侧滑出（336 宽）。
 * 画布本身由 MapCanvasDrawable 现画（纸面网格 + 卡片 + 连线），这里画的是同一张「纸面」。
 */

const CHIP = { margin: '0 8px 8px 0' } as const;

const TILE_SOURCES = ['手工绘制', '高德路网', '高德卫星', 'OSM', '本地缓存', '离线包'];

const TILE_NOTES: Record<string, string> = {
  手工绘制: '只画你自己的地点与路网，不联网。',
  高德路网: '高德瓦片（GCJ-02，落图时按 WGS-84 偏回来），中文路名。',
  高德卫星: '高德卫星影像（GCJ-02），没有路名，适合看地块与屋顶。',
  OSM: '开放街道图（WGS-84）。志愿者服务器，并发压到 2，别当自家图床用。',
  本地缓存: '只用本地缓存里的瓦片，不发任何请求。切到这个来源前先联网逛一遍要用的区域。',
  离线包: '读取你选的 .mbtiles 文件：整个地区都在本地，没网也能看。',
};

type Mode = 'view' | 'edit' | 'link' | 'doodle';

const MODE_LABEL: Record<Mode, string> = {
  view: '浏览',
  edit: '编辑',
  link: '连线',
  doodle: '涂鸦',
};

export function Map({ navigate, back }: ScreenProps) {
  const [panelOpen, setPanelOpen] = useState(false);
  const [searchOpen, setSearchOpen] = useState(false);
  const [searchText, setSearchText] = useState('');
  const [mode, setMode] = useState<Mode>('view');
  const [regionEdit, setRegionEdit] = useState(false);
  const [weather, setWeather] = useState('跟随用户天气');
  const [tileSource, setTileSource] = useState('手工绘制');
  const [onlineTiles, setOnlineTiles] = useState(true);
  const [curve, setCurve] = useState(false);

  const modeChip = (value: Mode) => (
    <Well key={value} active={mode === value} onClick={() => setMode(value)} style={CHIP}>
      {mode === value ? `● ${MODE_LABEL[value]}` : MODE_LABEL[value]}
    </Well>
  );

  return (
    <div style={{ position: 'relative', height: '100%', minHeight: '100%', overflow: 'hidden' }}>
      {/* 画布：占满窗口 */}
      <div
        aria-label="地图画布"
        style={{
          position: 'absolute',
          inset: 0,
          backgroundImage:
            'linear-gradient(var(--w-hairline) 1px, transparent 1px), linear-gradient(90deg, var(--w-hairline) 1px, transparent 1px)',
          backgroundSize: '24px 24px',
        }}
      />

      {/* 左上：搜索 + 状态条 */}
      <div
        style={{
          position: 'absolute',
          left: 16,
          top: 14,
          width: 'min(360px, calc(100vw - 32px))',
          display: 'flex',
          flexDirection: 'column',
          gap: 'var(--w-space-2)',
          zIndex: 4,
        }}
      >
        <div
          onClick={() => setSearchOpen(true)}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 8,
            height: 44,
            cursor: 'text',
            padding: '0 12px',
            borderRadius: 99,
            border: '1px solid var(--w-edge)',
            background: 'var(--w-surface-tint)',
          }}
        >
          <span aria-hidden="true" style={{ fontSize: 'var(--w-size-small)' }}>
            🔍
          </span>
          <input
            type="text"
            value={searchText}
            placeholder="搜索地点、区域…"
            aria-label="搜索地点、区域…"
            onFocus={() => setSearchOpen(true)}
            onChange={(event) => setSearchText(event.target.value)}
            style={{
              flex: 1,
              minWidth: 0,
              border: 0,
              outline: 'none',
              background: 'transparent',
              color: 'var(--w-ink-body)',
              font: 'inherit',
              fontSize: 'var(--w-size-body)',
            }}
          />
          {searchOpen ? (
            <button
              type="button"
              aria-label="收起搜索"
              onClick={() => setSearchOpen(false)}
              style={{
                border: 0,
                background: 'none',
                cursor: 'pointer',
                font: 'inherit',
                fontSize: 'var(--w-size-small)',
                color: 'var(--w-ink-muted)',
              }}
            >
              ✕
            </button>
          ) : null}
        </div>

        {searchOpen ? (
          <div
            style={{
              padding: 6,
              borderRadius: 'var(--w-radius-button)',
              border: '1px solid var(--w-edge)',
              background: 'var(--w-surface-tint)',
            }}
          >
            {searchText.trim().length === 0 ? (
              <Cap style={{ padding: '6px 8px' }}>搜索地点、区域，或说句话让她带你去</Cap>
            ) : null}
          </div>
        ) : null}
      </div>

      {/* 右上：图层按钮 */}
      <div style={{ position: 'absolute', right: 16, top: 14, zIndex: 7 }}>
        <Fab label="☰" size={46} fontSize="var(--w-size-subhead)" title="图层" onClick={() => setPanelOpen(!panelOpen)} />
      </div>

      {/* 图层面板 + 遮罩 */}
      {panelOpen ? (
        <button
          type="button"
          aria-label="关闭图层面板"
          onClick={() => setPanelOpen(false)}
          style={{
            position: 'absolute',
            inset: 0,
            border: 0,
            padding: 0,
            background: 'var(--w-ink-display)',
            opacity: 0.35,
            cursor: 'pointer',
            zIndex: 5,
          }}
        />
      ) : null}

      {panelOpen ? (
        <div
          style={{
            position: 'absolute',
            right: 10,
            top: 10,
            bottom: 10,
            width: 'min(336px, calc(100vw - 20px))',
            display: 'flex',
            flexDirection: 'column',
            gap: 10,
            padding: '14px 16px 16px',
            borderRadius: 'var(--w-radius-button)',
            border: '1px solid var(--w-edge)',
            background: 'var(--w-surface-tint)',
            overflowY: 'auto',
            zIndex: 6,
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <Section>图层</Section>
            <button
              type="button"
              aria-label="关闭"
              onClick={() => setPanelOpen(false)}
              className="k-textbtn"
              style={{ fontSize: 'var(--w-size-body)' }}
            >
              ✕
            </button>
          </div>

          <Section muted>图层</Section>
          <ToolRow>
            <Well
              active={regionEdit}
              onClick={() => setRegionEdit(!regionEdit)}
              style={CHIP}
            >
              {regionEdit ? '● 区域划分' : '区域划分'}
            </Well>
            <button
              type="button"
              className="k-textbtn"
              style={{
                margin: '0 8px 8px 0',
                padding: '7px 10px',
                borderRadius: 'var(--w-radius-well)',
                background: 'var(--w-surface-tint)',
                color: 'var(--w-ink-body)',
                fontSize: 'var(--w-size-small)',
              }}
              onClick={() =>
                setWeather((current) =>
                  current === '跟随用户天气'
                    ? '地图当地天气'
                    : current === '地图当地天气'
                      ? '自定义地点天气'
                      : '跟随用户天气',
                )
              }
            >
              {weather}
            </button>
            <button
              type="button"
              className="k-textbtn"
              style={{
                margin: '0 8px 8px 0',
                padding: '7px 10px',
                borderRadius: 'var(--w-radius-well)',
                background: 'var(--w-surface-tint)',
                color: 'var(--w-ink-body)',
                fontSize: 'var(--w-size-small)',
              }}
              onClick={() => navigate('/minimal3d')}
            >
              3D 灰度
            </button>
          </ToolRow>

          <Section muted>地图来源</Section>
          <div style={{ display: 'flex', flexWrap: 'wrap' }}>
            {TILE_SOURCES.map((name) => (
              <Well
                key={name}
                active={tileSource === name}
                onClick={() => setTileSource(name)}
                style={{ margin: '0 8px 8px 0' }}
              >
                {name}
              </Well>
            ))}
          </div>
          <Cap>{TILE_NOTES[tileSource]}</Cap>
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: 10,
              padding: '2px 0',
            }}
          >
            <div style={{ display: 'flex', flexDirection: 'column', gap: 1, minWidth: 0 }}>
              <span style={{ fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)' }}>允许联网取瓦片</span>
              <Cap>锚点：上海（默认落点） —— 这张图没有经纬度，底图只当背景铺，不对应真实位置。</Cap>
            </div>
            <button
              type="button"
              role="switch"
              aria-checked={onlineTiles}
              aria-label="允许联网取瓦片"
              onClick={() => setOnlineTiles(!onlineTiles)}
              style={{
                flex: '0 0 auto',
                width: 46,
                height: 27,
                borderRadius: 99,
                border: '1px solid var(--w-signal-edge)',
                background: onlineTiles ? 'var(--w-signal)' : 'var(--w-surface-tint)',
                cursor: 'pointer',
                position: 'relative',
                padding: 0,
              }}
            >
              <span
                style={{
                  position: 'absolute',
                  top: 3,
                  left: onlineTiles ? 22 : 3,
                  width: 19,
                  height: 19,
                  borderRadius: 99,
                  background: 'var(--w-bg)',
                }}
              />
            </button>
          </div>
          <Cap>{tileSource === '手工绘制' ? '底图关着：编辑器的纸面画布。' : `${tileSource} · 联网${onlineTiles ? '已开' : '已关'}`}</Cap>

          <Section muted>编辑</Section>
          <div style={{ display: 'flex', flexWrap: 'wrap' }}>
            {modeChip('view')}
            {modeChip('edit')}
            {modeChip('link')}
            {modeChip('doodle')}
            {mode === 'link' ? (
              <Well onClick={() => setCurve(!curve)} style={CHIP}>
                {curve ? '曲线' : '直线'}
              </Well>
            ) : null}
            {[
              '↻ 重置布局',
              '＋ 新地点',
              '＋ 新场景',
              '背景（素材库）',
              'AI 画背景',
            ].map((label) => (
              <Well key={label} style={{ margin: '0 8px 8px 0' }}>
                {label}
              </Well>
            ))}
          </div>
          <Cap>编辑：拖卡片=移动 · 空白拖动=平移 · 按住空白 0.6 秒再拖=框选 · 右键=菜单</Cap>

          <Section muted>导入导出</Section>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
            <span
              style={{
                fontSize: 'var(--w-size-caption)',
                color: 'var(--w-ink-body)',
                whiteSpace: 'nowrap',
                overflow: 'hidden',
                textOverflow: 'ellipsis',
              }}
            >
              这张图来自：手工绘制
            </span>
            <button
              type="button"
              className="k-textbtn"
              style={{
                flex: '0 0 auto',
                height: 30,
                padding: '0 10px',
                borderRadius: 'var(--w-radius-button)',
                border: '1px solid var(--w-hairline)',
                fontSize: 'var(--w-size-caption)',
                color: 'var(--w-ink-body)',
              }}
              onClick={() => navigate('/mapcreate')}
            >
              换来源…
            </button>
          </div>
          <div style={{ display: 'flex', flexWrap: 'wrap' }}>
            <Well style={CHIP}>导入地图</Well>
            <Well onClick={() => navigate('/mapcapture')} style={CHIP}>
              取景（拍照钉在地图上）
            </Well>
            <Well onClick={() => navigate('/mapcreate')} style={CHIP}>
              创建地图…（高德 / 开放街道图）
            </Well>
            <Well style={CHIP}>导出</Well>
            <Well onClick={() => navigate('/flows')} style={CHIP}>
              流程图
            </Well>
          </div>

          <Section muted>其他</Section>
          <ToolRow>
            <Well data-back="1"
        onClick={back} style={CHIP}>
              返回
            </Well>
            <Well style={CHIP}>ℹ 帮助</Well>
          </ToolRow>
        </div>
      ) : null}

      {/* 右下：缩放 / 定位 / 指南针 */}
      <div
        style={{
          position: 'absolute',
          right: 16,
          bottom: 118,
          display: 'flex',
          flexDirection: 'column',
          gap: 10,
          zIndex: 4,
        }}
      >
        <Fab label="＋" fontSize="var(--w-size-title)" title="放大" />
        <Fab label="－" fontSize="var(--w-size-title)" title="缩小" />
        <Fab label="⌖" title="回到她此刻所在的位置" />
        <Fab label="N" fontSize="var(--w-size-small)" title="回正：正北朝上 + 全图取景" />
      </div>

      {/* 左下：比例尺 */}
      <div
        style={{
          position: 'absolute',
          left: 18,
          bottom: 26,
          display: 'flex',
          alignItems: 'flex-end',
          gap: 6,
          zIndex: 4,
        }}
      >
        <span style={{ width: 60, height: 3, borderRadius: 1, background: 'var(--w-ink-body)' }} />
        <Cap>100 米</Cap>
      </div>
    </div>
  );
}
