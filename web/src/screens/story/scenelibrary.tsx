import { useState } from 'react';
import { Card, PageHeader, Row, Sep, TextBtn } from '../../components/kit';
import { PageShell } from '../../components/PageShell';
import { SignalButton } from '../../components/SignalButton';
import type { ScreenProps } from '../types';
import { Actions, Note, Stack } from './parts';

type ModeRow = { id: string; name: string; detail: string };
type LibraryEntry = { id: string; name: string; mapSceneRef: string; modes: ModeRow[] };

function newMode(name: string): ModeRow {
  return { id: `m${Date.now()}${Math.random().toString(16).slice(2, 6)}`, name, detail: '灯光 normal｜不限时间｜背景沿用条目｜不改音乐' };
}

/**
 * 场景工坊：SceneLibraryPage.xaml 的条目列表 + 选中条目操作区 + 底部生效说明。
 * SceneDirector 没有对应的前端 method，切场景（「应用」）只能停在界面状态；
 * 条目/模式的改名、增删仍在页内状态里走得通。
 */
export function Scenelibrary({ back }: ScreenProps) {
  const [entries, setEntries] = useState<LibraryEntry[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [selectedModeId, setSelectedModeId] = useState<string | null>(null);
  const [stateText] = useState('当前无生效场景。时间命中或 AI 切换后会显示在这里。');

  const selected = entries.find((e) => e.id === selectedId) ?? null;
  const selectedMode = selected?.modes.find((m) => m.id === selectedModeId) ?? null;

  const addEntry = () => {
    const entry: LibraryEntry = { id: `e${Date.now()}`, name: '未命名场景', mapSceneRef: '', modes: [] };
    setEntries((list) => [...list, entry]);
    setSelectedId(entry.id);
    setSelectedModeId(null);
  };

  const renameEntry = () => {
    if (!selected) return;
    const name = window.prompt('重命名', selected.name);
    if (!name || !name.trim()) return;
    setEntries((list) => list.map((e) => (e.id === selected.id ? { ...e, name: name.trim() } : e)));
  };

  const removeEntry = () => {
    if (!selected) return;
    if (!window.confirm(`确定删除「${selected.name}」及其全部模式吗？`)) return;
    setEntries((list) => list.filter((e) => e.id !== selected.id));
    setSelectedId(null);
    setSelectedModeId(null);
  };

  const addMode = () => {
    if (!selected) return;
    const mode = newMode('新模式');
    setEntries((list) => list.map((e) => (e.id === selected.id ? { ...e, modes: [...e.modes, mode] } : e)));
    setSelectedModeId(mode.id);
  };

  const renameMode = () => {
    if (!selected || !selectedMode) return;
    const name = window.prompt('模式命名', selectedMode.name);
    if (!name || !name.trim()) return;
    setEntries((list) =>
      list.map((e) =>
        e.id === selected.id
          ? { ...e, modes: e.modes.map((m) => (m.id === selectedMode.id ? { ...m, name: name.trim() } : m)) }
          : e,
      ),
    );
  };

  const removeMode = () => {
    if (!selected || !selectedMode) return;
    if (!window.confirm(`确定删除模式「${selectedMode.name}」吗？`)) return;
    setEntries((list) =>
      list.map((e) =>
        e.id === selected.id ? { ...e, modes: e.modes.filter((m) => m.id !== selectedMode.id) } : e,
      ),
    );
    setSelectedModeId(null);
  };

  const modeBtn = (label: string, onClick: () => void) => (
    <TextBtn onClick={onClick} disabled={!selectedMode}>{label}</TextBtn>
  );

  return (
    <PageShell>
      <PageHeader
        title="场景工坊"
        onBack={back}
        right={<SignalButton onClick={addEntry}>＋ 新建</SignalButton>}
      />

      <div className="screen">
        <Stack gap={4}>
          <Card>
            {entries.map((entry) => (
              <div key={entry.id}>
                <Row
                  title={entry.name}
                  desc={`地图引用：${entry.mapSceneRef}`}
                  right={
                    <TextBtn onClick={() => { setSelectedId(entry.id); setSelectedModeId(entry.modes[0]?.id ?? null); }}>
                      应用
                    </TextBtn>
                  }
                />
                <Sep />
              </div>
            ))}
          </Card>

          {selected ? (
            <Card>
              <Actions>
                <TextBtn onClick={renameEntry}>重命名</TextBtn>
                <TextBtn>引用地图场景</TextBtn>
                <TextBtn>变换规则</TextBtn>
              </Actions>
              <Actions>
                <TextBtn onClick={addMode}>＋ 加模式</TextBtn>
                <TextBtn onClick={removeEntry}>删除条目</TextBtn>
              </Actions>

              <div style={{ maxHeight: 132, overflowY: 'auto' }}>
                {selected.modes.map((mode) => (
                  <div key={mode.id}>
                    <Row
                      title={mode.name}
                      desc={mode.detail}
                      right={<TextBtn onClick={() => setSelectedModeId(mode.id)}>应用</TextBtn>}
                    />
                    <Sep />
                  </div>
                ))}
              </div>

              <Actions>
                {modeBtn('模式名', renameMode)}
                {modeBtn('背景', () => undefined)}
                {modeBtn('BGM', () => undefined)}
              </Actions>
              <Actions>
                {modeBtn('灯光', () => undefined)}
                {modeBtn('时间区间', () => undefined)}
                {modeBtn('删除模式', removeMode)}
              </Actions>
            </Card>
          ) : null}
        </Stack>

        <Stack gap={1} style={{ marginTop: 'var(--w-space-4)' }}>
          <span style={{ fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)' }}>{stateText}</span>
          <Note>
            点条目或模式右侧的「应用」即可立刻切换当前场景（点击式，无需拖拽）；切换结果与此刻可用性会显示在上方状态栏。时间到点自动切换，AI
            只能从「此刻可用选项」里选（无模式=仅条目）。模式写了时间区间时，只有落在区间内才能被 AI 切换。
          </Note>
        </Stack>
      </div>
    </PageShell>
  );
}
