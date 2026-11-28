import { useState } from 'react';
import { Glass } from '../../components/Glass';
import { GlassButton } from '../../components/GlassButton';
import { GlassSlider } from '../../components/GlassSlider';
import { PageShell } from '../../components/PageShell';
import { PageHeader, Select } from '../../components/kit';
import type { ScreenProps } from '../types';
import { Cap, FormRow, Inert, InlineField, Section, ToolRow, Well } from './parts';

/**
 * 创建地图：Views/Games/MapCreatePage.xaml（343 行）+ 同名 code-behind。
 * ① 找地方 → ② 取多大一片 → ③ 预览与导入 → ④ 背景 → ⑤ 地图天气 → ⑥ 自己的文件 / 高德 Key / 判灯。
 * 默认值取自 MapCreateViewModel：半径 500 米、跟随用户天气、判灯关着、AI 出图未开启。
 */

const PRESETS: { label: string; radius: number }[] = [
  { label: '街区', radius: 200 },
  { label: '一公里', radius: 500 },
  { label: '五公里', radius: 2500 },
];

const BATCHES = [
  { value: '3 张', label: '3 张' },
  { value: '6 张', label: '6 张' },
  { value: '12 张', label: '12 张' },
];

const WEATHER_CHOICES = [
  { value: 'user', label: '跟随用户天气' },
  { value: 'map', label: '地图当地天气' },
  { value: 'custom', label: '自定义地点天气' },
];

function radiusLabel(radius: number) {
  const span = radius * 2;
  const text =
    span >= 1000
      ? `${(span / 1000).toFixed(2).replace(/\.?0+$/, '')} 公里宽的一整片`
      : `${Math.round(radius)} 米为半径的一小片`;
  return `取中心点周围 ${text}`;
}

export function MapCreate({ back }: ScreenProps) {
  const [query, setQuery] = useState('');
  const [radius, setRadius] = useState(500);
  const [weather, setWeather] = useState('user');
  const [batch, setBatch] = useState('3 张');
  const [amapKey, setAmapKey] = useState('');
  const [googleKey, setGoogleKey] = useState('');
  const [city, setCity] = useState('');
  const [signalAmap, setSignalAmap] = useState(false);

  return (
    <PageShell>
      <PageHeader title="创建地图" subtitle="输入城市、区县、街道，或者直接填经纬度（例：120.15,30.27）。" onBack={back} />

      <div className="screen" style={{ maxWidth: 920, gap: 'var(--w-space-4)' }}>
        {/* ① 找地方 */}
        <Section>① 找地方</Section>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
          <div style={{ display: 'flex', gap: 'var(--w-space-2)', alignItems: 'center' }}>
            <InlineField
              value={query}
              onChange={setQuery}
              placeholder="城市 / 区县 / 街道，或直接写经纬度：120.15,30.27"
              fontSize="var(--w-size-small)"
              style={{ flex: 1 }}
            />
            <Inert>
              <GlassButton size="md">检索</GlassButton>
            </Inert>
          </div>
          <Cap>还没填高德 Key，检索退回开放街道图 —— 国内的中文店名与小路会少一些。填上 Key 自动改用高德。</Cap>
        </div>

        {/* ② 取多大一片 */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
          <Section>② 取多大一片</Section>
          <ToolRow>
            {PRESETS.map((preset) => (
              <Well
                key={preset.label}
                active={radius === preset.radius}
                onClick={() => setRadius(preset.radius)}
                style={{ padding: '6px 12px', borderRadius: 99 }}
              >
                {preset.label}
              </Well>
            ))}
          </ToolRow>
          <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
            <Cap>半径</Cap>
            <div style={{ flex: 1 }}>
              <GlassSlider
                label="半径"
                value={(radius - 100) / 2900}
                onChange={(next) => setRadius(Math.round((100 + next * 2900) / 10) * 10)}
                width={220}
                format={(value) => `${Math.round(100 + value * 2900)} 米`}
              />
            </div>
            <Cap style={{ minWidth: 56, textAlign: 'right' }}>{Math.round(radius)} 米</Cap>
          </div>
          <Cap>{radiusLabel(radius)}</Cap>
          <Cap>一次最多 6×6 公里，道路 1200 段 / 建筑 600 栋 / 带名字的店铺 300 家封顶：塞得太满，安卓端的画布与寻路会先撑不住，超了会砍并说明。</Cap>
        </div>

        {/* ③ 预览与导入 */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
          <Section>③ 预览与导入</Section>
          <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 'var(--w-space-2)' }}>
            <Inert>
              <GlassButton size="md">预览：先数一数</GlassButton>
            </Inert>
            <Cap>预览不写进存档，只算一次给你看拿到多少东西</Cap>
            <span style={{ flex: 1 }} />
            <Inert>
              <GlassButton size="md">导入到游戏</GlassButton>
            </Inert>
          </div>
        </div>

        {/* ④ 背景 */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
          <Section>④ 背景：形状之外的那一层</Section>
          <Cap tone="body">手上还没有地图：先检索一片真实城市导进来，背景才有着落。</Cap>
          <Cap>
            真实城市导进来只有建筑轮廓、道路命名与店名，画面是空的：自己拍的照片回到地图页右键场景 →「背景图（电脑导入）」挂上，这里只负责让 AI 补。
          </Cap>
          <ToolRow>
            <Select value={batch} options={BATCHES} onChange={setBatch} label="一次几张" />
            <Inert>
              <GlassButton size="sm">让 AI 补背景</GlassButton>
            </Inert>
            <span style={{ flex: 1 }} />
            <Inert>
              <GlassButton size="sm">生成大地图底图</GlassButton>
            </Inert>
          </ToolRow>
          <Cap>
            出图走「设置 → AI → 画面生成」那一套；这里只放 AI 现生成的图与你自己导入的文件，不用任何游戏自带或受版权保护的素材。
          </Cap>

          <Glass tone="card" radius={12}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)', padding: 'var(--w-space-2)' }}>
              <span style={{ fontSize: 'var(--w-size-body)', color: 'var(--w-ink-body)' }}>谷歌地图地点照片</span>
              <Cap>
                还没填 Key：高德没有地点照片接口，谷歌 Places 有 —— 填一个启用了 Places API 的 Google Maps API Key 才能取照片（想让没照片的地点退到街景，还要启用 Street View Static API）。
              </Cap>
              <div style={{ display: 'flex', gap: 'var(--w-space-2)' }}>
                <InlineField
                  value={googleKey}
                  onChange={setGoogleKey}
                  placeholder="Google Maps API Key（需启用 Places API）"
                  style={{ flex: 1 }}
                />
                <Inert>
                  <GlassButton size="sm">存 Key</GlassButton>
                </Inert>
              </div>
              <ToolRow>
                <Select value={batch} options={BATCHES} onChange={setBatch} label="一次几张" />
                <Inert>
                  <GlassButton size="sm">从谷歌地图取照片</GlassButton>
                </Inert>
              </ToolRow>
              <Cap>
                高德没有地点照片接口，谷歌 Places 有：按店名 / 门牌搜到地点后取它的照片，没有照片的地点回退街景（街景还要另启用 Street View Static API）。下载的照片只落在本机地图目录。
              </Cap>
            </div>
          </Glass>
        </div>

        {/* ⑤ 地图天气 */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
          <Section>⑤ 地图天气：这张图按哪儿算</Section>
          <Cap>导入的真实城市地图默认按抓取位置的当地天气算；也可以改成跟随用户设置里的城市，或者自己填一个地点。</Cap>
          <Glass tone="card" radius={12}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)', padding: 'var(--w-space-2)' }}>
              <FormRow
                title="天气按哪儿算"
                right={<Select value={weather} options={WEATHER_CHOICES} onChange={setWeather} label="天气按哪儿算" />}
              />
              <Cap>这张图：手工绘制 / 自己导入 · 没有城市名 · 没有坐标</Cap>
              <Cap>当前：跟随用户天气（设置 → 天气与生活 里选的城市）。</Cap>
            </div>
          </Glass>
          {weather === 'custom' ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
              <Cap>自定义地点</Cap>
              <div style={{ display: 'flex', gap: 'var(--w-space-2)' }}>
                <InlineField value={city} onChange={setCity} placeholder="城市名，例如：杭州市" style={{ flex: 1 }} />
                <Inert>
                  <GlassButton size="sm">保存地点</GlassButton>
                </Inert>
              </div>
              <Cap>填城市名就够：第一次查天气时会自动地理编码出坐标。</Cap>
            </div>
          ) : null}
          <Cap>主界面顶部的天气、场景的天气门控读的都是这一份；地图一换（重新导入），这里会跟着新图重读。</Cap>
        </div>

        {/* ⑥ 自己的文件 / 高德 Key / 判灯 */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
          <Section>⑥ 自己的文件 / 高德 Key / 判灯</Section>
          <FormRow
            title="导入我自己准备好的地图"
            desc="一份地图 JSON：只有形状与路名，背景同样要实地取材或让 AI 补。"
            right={<GlassButton size="sm">选文件</GlassButton>}
          />
          <div className="k-sep" role="presentation" />
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
            <span style={{ fontSize: 'var(--w-size-body)', color: 'var(--w-ink-body)' }}>高德 Key（选填）</span>
            <Cap>没填 Key 也能用：检索退回开放街道图，只是国内的中文店名与小马路会少一些。</Cap>
            <div style={{ display: 'flex', gap: 'var(--w-space-2)' }}>
              <InlineField value={amapKey} onChange={setAmapKey} placeholder="高德 Web 服务的 key" style={{ flex: 1 }} />
              <Inert>
                <GlassButton size="sm">存 Key</GlassButton>
              </Inert>
            </div>
          </div>
          <div className="k-sep" role="presentation" />
          <FormRow
            title="红绿灯看高德路况"
            desc="关着（默认）：灯色只按每盏灯自己的周期走，不联网。流程图里判「绿灯能不能走」用的就是它。"
            right={
              <button
                type="button"
                role="switch"
                aria-checked={signalAmap}
                aria-label="红绿灯看高德路况"
                onClick={() => setSignalAmap(!signalAmap)}
                style={{
                  width: 46,
                  height: 27,
                  borderRadius: 99,
                  border: '1px solid var(--w-signal-edge)',
                  background: signalAmap ? 'var(--w-signal)' : 'var(--w-surface-tint)',
                  cursor: 'pointer',
                  position: 'relative',
                  padding: 0,
                }}
              >
                <span
                  style={{
                    position: 'absolute',
                    top: 3,
                    left: signalAmap ? 22 : 3,
                    width: 19,
                    height: 19,
                    borderRadius: 99,
                    background: 'var(--w-bg)',
                  }}
                />
              </button>
            }
          />
        </div>

        <Cap>导入的地图会整张换掉手上这一张；想回去接着改，回地图页即可。</Cap>
      </div>
    </PageShell>
  );
}
