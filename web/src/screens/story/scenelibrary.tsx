import { useState } from 'react';
import { Card, PageHeader, Row, Sep, TextBtn } from '../../components/kit';
import { PageShell } from '../../components/PageShell';
import { SignalButton } from '../../components/SignalButton';
import type { ScreenProps } from '../types';
import { Actions, Inert, Note, Stack } from './parts';

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

  /** 没有通路的那几颗（背景 / BGM / 灯光 / 时间区间）一律灰着：点了不改任何东西，就不装成能改。 */
  const modeBtn = (label: string, onClick?: () => void) => (
    <TextBtn onClick={onClick} disabled={!onClick || !selectedMode}>{label}</TextBtn>
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
                      选中
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
                <Inert>
                  <TextBtn>引用地图场景</TextBtn>
                </Inert>
                <Inert>
                  <TextBtn>变换规则</TextBtn>
                </Inert>
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
                      right={<TextBtn onClick={() => setSelectedModeId(mode.id)}>选中</TextBtn>}
                    />
                    <Sep />
                  </div>
                ))}
              </div>

              <Actions>
                {modeBtn('模式名', renameMode)}
                {modeBtn('背景')}
                {modeBtn('BGM')}
              </Actions>
              <Actions>
                {modeBtn('灯光')}
                {modeBtn('时间区间')}
                {modeBtn('删除模式', removeMode)}
              </Actions>
            </Card>
          ) : null}
        </Stack>

        <Stack gap={1} style={{ marginTop: 'var(--w-space-4)' }}>
          <span style={{ fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)' }}>{stateText}</span>
          <Note>
            源码里点条目或模式右侧的「应用」就会立刻切换当前场景；Web 端还没有切换场景的方法（scenes.apply），所以右侧那颗叫「选中」——
            它只把这一条挑出来，不会换掉正在演的场景。条目与模式的增删改名同样只留在这个页面。
          </Note>
        </Stack>
      </div>
    </PageShell>
  );
}
