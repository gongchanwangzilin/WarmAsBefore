import { useCallback, useState } from 'react';
import type { Bridge } from '../bridge/client';
import type { SettingsValues } from '../bridge/protocol';
import type { MockFailure, MockFaults, MockLatency } from '../bridge/mock';
import { GlassSwitch } from '../components/GlassSwitch';
import { PageShell } from '../components/PageShell';
import { EntryRow, Section } from '../components/Section';
import { TopBar } from '../components/TopBar';
import { SkeletonRows } from '../components/states/Skeleton';
import { StateBlock } from '../components/states/StateBlock';
import { useResource } from '../lib/useResource';

type Props = {
  bridge: Bridge;
  onBack: () => void;
  onNavigate: (path: string) => void;
  /** 演示开关改了假后端的状态，全局光谱也得跟着重取，否则玻璃页看不到空状态 */
  onFaultsChanged: () => void;
};

export function SettingsPage({ bridge, onBack, onNavigate, onFaultsChanged }: Props) {
  const { state, reload } = useResource(() => bridge.request('settings.get', {}), [bridge]);
  const [faults, setFaults] = useState<MockFaults | null>(() => bridge.faults);

  const applyFault = useCallback(
    (patch: Partial<MockFaults>) => {
      bridge.setFaults(patch);
      setFaults(bridge.faults);
      reload();
      onFaultsChanged();
    },
    [bridge, reload, onFaultsChanged],
  );

  return (
    <PageShell>
      <TopBar title="设置" onBack={onBack} />

      <main className="content">
        {state.status === 'loading' ? (
          <div aria-busy="true">
            <Section title="常驻">
              <SkeletonRows count={3} />
            </Section>
            <Section title="全部设置">
              <SkeletonRows count={3} />
            </Section>
            <p className="sr-only">正在载入设置…</p>
          </div>
        ) : null}

        {state.status === 'error' ? (
          <StateBlock
            tone="error"
            title="设置没送上来"
            body={
              <p>
                前端一直在等本地服务，8 秒内没有拿到回话。后台可能已经退出，或者消息桥断了。
                重启应用后再试一次，外观不会丢。
              </p>
            }
            detail={`${state.error.message}（${state.error.code}）`}
            action="重新连接"
            onAction={reload}
          />
        ) : null}

        {state.status === 'ready' && state.data.values === null ? (
          <StateBlock
            tone="empty"
            title="这台设备还没注册任何设置项"
            body={
              <p>
                设置是由后端按模块注册的；现在这张表是空的，所以没有可调的东西。可以先回标题页进入故事，
                等后台把模块挂上来再回来调。
              </p>
            }
            action="再同步一次"
            onAction={reload}
          />
        ) : null}

        {state.status === 'ready' && state.data.values ? (
          <SettingsList values={state.data.values} onNavigate={onNavigate} />
        ) : null}

        {faults ? (
          <DemoCard faults={faults} mode={bridge.mode} onChange={applyFault} />
        ) : null}
      </main>
    </PageShell>
  );
}

function SettingsList({
  values,
  onNavigate,
}: {
  values: SettingsValues;
  onNavigate: (path: string) => void;
}) {
  const glass = values.glass;
  const percent = (v: number) => `${Math.round(v * 100)}%`;

  return (
    <>
      <Section title="常驻">
        <EntryRow
          label="玻璃效果"
          summary={`${percent(glass.alpha)} 透明 · ${glass.lightweight ? '轻量' : '完整'}`}
          to="/settings/glass"
          onOpen={onNavigate}
        />
        <EntryRow label="文字大小" summary={percent(values.text.scale)} badge="待接入" />
        <EntryRow label="声音" summary={values.sound.enabled ? '开' : '关'} badge="待接入" />
      </Section>

      <Section title="全部设置" note="这一批只接了外观；下面几行等后端模块挂上来再点亮。">
        <EntryRow
          label="存档与续写"
          summary={values.story.autoSave ? '自动保存' : '手动保存'}
          badge="待接入"
        />
        <EntryRow
          label="语言"
          summary={values.language.locale === 'zh-CN' ? '简体中文' : 'English'}
          badge="待接入"
        />
        <EntryRow label="关于" summary="v0.1 · Web 骨架" />
      </Section>
    </>
  );
}

function DemoCard({
  faults,
  mode,
  onChange,
}: {
  faults: MockFaults;
  mode: string;
  onChange: (patch: Partial<MockFaults>) => void;
}) {
  return (
    <Section
      title="演示 · 桥的故障开关"
      note={`当前走的是 ${mode === 'mock' ? '内存 mock' : mode}。改一项会重新拉这一页：延迟让你看见骨架屏，读/写失败让你看见错误状态，空数据让你看见空状态。`}
      className="demo"
    >
      <div className="row row--stack">
        <span className="row__label">延迟</span>
        <Chips<MockLatency>
          label="延迟"
          value={faults.latency}
          options={[
            { value: 0, label: '即时' },
            { value: 320, label: '320ms' },
            { value: 1200, label: '1.2s' },
          ]}
          onChange={(latency) => onChange({ latency })}
        />
      </div>

      <div className="row row--stack">
        <span className="row__label">失败</span>
        <Chips<MockFailure>
          label="失败方式"
          value={faults.failure}
          options={[
            { value: 'none', label: '正常' },
            { value: 'load', label: '读失败' },
            { value: 'write', label: '写失败' },
          ]}
          onChange={(failure) => onChange({ failure })}
        />
      </div>

      <div className="row">
        <span className="row__label">空数据</span>
        <GlassSwitch
          label="后端返回空数据"
          checked={faults.empty}
          onChange={(empty) => onChange({ empty })}
        />
      </div>
    </Section>
  );
}

function Chips<T extends string | number>({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: T;
  options: { value: T; label: string }[];
  onChange: (value: T) => void;
}) {
  return (
    <div className="chips" role="group" aria-label={label}>
      {options.map((option) => (
        <button
          key={String(option.value)}
          type="button"
          className="chip"
          aria-pressed={option.value === value}
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}
