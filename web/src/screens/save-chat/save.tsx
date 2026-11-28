import { useEffect, useState } from 'react';
import type { SaveRow } from '../../bridge/protocol';
import { Card, PageHeader, Sep, TextBtn } from '../../components/kit';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import { NEED_SAVE, useRun } from '../../lib/run';
import type { ScreenProps } from '../types';
import './savechat.css';

/**
 * 存档管理：Views/Save/SavePage.xaml（333 行）+ SavePage.xaml.cs。
 *
 * 页头（监测 / 分支树）→ 云存档入口行 → 存档列表（一行一档，头像 + 名字 + 角色·场景·时间
 * + 历史注记，右侧「读取」，说明下方一排文字级次要动作）→ 底部新建 / 导入 / 备份。
 * 源码是一列到底、动作压在底部；列表数据来自 SaveManager.List()，读不到时走源码的空态。
 */

/** ISO 时间 → 源码里的「11/26 22:41」。 */
function stamp(iso: string): string {
  const at = new Date(iso);
  if (Number.isNaN(at.getTime())) return '';
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${at.getMonth() + 1}/${at.getDate()} ${pad(at.getHours())}:${pad(at.getMinutes())}`;
}

type Slot = {
  id: string;
  label: string;
  character: string;
  scene: string;
  savedAt: string;
  historyNote: string;
};

export function Save({ bridge, navigate, back }: ScreenProps) {
  const run = useRun(bridge);
  /** 监测画的是「这一局」的状态：没进存档就灰着，进了再亮。 */
  const outsideSave = run.known && !run.active;
  const [slots, setSlots] = useState<Slot[]>([]);
  const [loadError, setLoadError] = useState('');
  const [ticket, setTicket] = useState(0);
  // 读取/改名/删除的实时回话：成功一句、失败一句，都来自宿主，不编。
  const [busyId, setBusyId] = useState('');
  const [note, setNote] = useState('');
  // 存档图标：按角色名对上花名册里的头像；没有头像就退回首字方块。
  const [avatars, setAvatars] = useState<Record<string, string>>({});

  // 真的存档表：SaveManager.List() 那一份。写盘后宿主推 data.changed，这里跟着重读。
  useEffect(() => {
    let dropped = false;
    const read = async () => {
      try {
        const { slots: rows } = await bridge.request('saves.list', {});
        if (dropped) return;
        setLoadError('');
        setSlots(
          rows.map((row: SaveRow) => ({
            id: row.id,
            label: row.label || row.character || '没有名字的一档',
            character: row.character,
            scene: row.scene,
            savedAt: stamp(row.savedAt),
            historyNote: row.historyNote,
          })),
        );
      } catch (cause) {
        // 读失败不等于「没有存档」：分开说，并给一次重试。
        if (dropped) return;
        setLoadError(cause instanceof Error ? cause.message : '存档表没有应答。');
        setSlots([]);
      }
    };
    void read();
    void bridge
      .request('characters.list', {})
      .then(({ characters }) => {
        if (dropped) return;
        setAvatars(
          Object.fromEntries(characters.filter((c: { avatarUrl: string }) => c.avatarUrl).map((c: { name: string; avatarUrl: string }) => [c.name, c.avatarUrl])),
        );
      })
      .catch(() => undefined);
    const off = bridge.onEvent('data.changed', () => void read());
    return () => {
      dropped = true;
      off();
    };
  }, [bridge, ticket]);

  /** 读取：真的载入当前局（SaveManager.Load），载进来了才跳主界面。 */
  const load = async (id: string) => {
    if (busyId) return;
    setBusyId(id);
    setNote('');
    try {
      const result = await bridge.request('save.load', { id });
      if (result.loaded) {
        navigate('/main');
        return;
      }
      // 能打开但核心状态整段重建过：停在原地把宿主那句话说完，不替用户决定。
      setNote(result.note || '这份存档的核心状态是重建出来的，先没有进入。');
    } catch (cause) {
      setNote(cause instanceof Error ? cause.message : '这份存档没有读出来。');
    } finally {
      setBusyId('');
    }
  };

  const rename = async (slot: Slot) => {
    const label = window.prompt('重命名存档', slot.label);
    if (label === null || !label.trim()) return;
    try {
      await bridge.request('save.rename', { id: slot.id, label: label.trim() });
      setNote(`已改名：${label.trim()}`);
      setTicket((n) => n + 1);
    } catch (cause) {
      setNote(cause instanceof Error ? cause.message : '改名没有成功。');
    }
  };

  const remove = async (slot: Slot) => {
    if (!window.confirm(`删除「${slot.label}」？删除后不可恢复。`)) return;
    try {
      await bridge.request('save.delete', { id: slot.id });
      setNote(`已删除：${slot.label}`);
      setTicket((n) => n + 1);
    } catch (cause) {
      setNote(cause instanceof Error ? cause.message : '这一档没有删掉。');
    }
  };

  return (
    <PageShell>
      <PageHeader
        title="存档管理"
        onBack={back}
        right={
          <>
            <span title={outsideSave ? NEED_SAVE : undefined}>
              <TextBtn onClick={() => navigate('/superMonitor')} disabled={outsideSave}>
                监测
              </TextBtn>
            </span>
            <TextBtn onClick={() => navigate('/saveTree')}>分支树</TextBtn>
          </>
        }
      />

      <div className="screen">
        <Card tight>
          <button type="button" className="sc-cloud" onClick={() => navigate('/cloudsaves')}>
            <span className="sc-well">云</span>
            <span className="sc-slot__text">
              <span className="sc-slot__title">云存档</span>
              <span className="sc-slot__desc">取回其他设备的存档，或恢复同步覆盖前的历史版本</span>
            </span>
            <span style={{ color: 'var(--w-ink-muted)', fontSize: 'var(--w-size-body)' }}>›</span>
          </button>
        </Card>

        <Card tight>
          {loadError ? (
            <div className="sc-empty">
              <span>存档表读不出来</span>
              <small>{loadError}</small>
              <button type="button" className="k-textbtn" style={{ color: 'var(--w-signal)' }} onClick={() => setTicket((n) => n + 1)}>
                重试
              </button>
            </div>
          ) : slots.length === 0 ? (
            <div className="sc-empty">
              <span>还没有存档</span>
              <small>这台机器上还没有任何一局</small>
              <small style={{ color: 'var(--w-signal)' }}>点下方「新建存档」开始新的一局</small>
            </div>
          ) : (
            slots.map((slot, index) => (
              <div key={slot.id}>
                <div className="sc-slot">
                  <span className="sc-slot__avatar">
                    {avatars[slot.character] ? (
                      <img src={avatars[slot.character]} alt="" />
                    ) : (
                      slot.character.slice(0, 1) || '档'
                    )}
                  </span>
                  <div className="sc-slot__text">
                    <span className="sc-slot__title">{slot.label}</span>
                    <span className="sc-slot__desc">
                      <b style={{ fontWeight: 600, color: 'var(--w-ink-body)' }}>{slot.character}</b>
                      {' · '}
                      {slot.scene}
                      {' · '}
                      {slot.savedAt}
                    </span>
                    {slot.historyNote ? <span className="sc-slot__note">{slot.historyNote}</span> : null}
                    <div className="sc-slot__acts">
                      <TextBtn onClick={() => void rename(slot)}>重命名</TextBtn>
                      <TextBtn disabled>分支</TextBtn>
                      <TextBtn disabled>导出</TextBtn>
                      <TextBtn onClick={() => navigate(`/saveRules?id=${encodeURIComponent(slot.id)}`)}>权限</TextBtn>
                      <TextBtn onClick={() => void remove(slot)}>删除</TextBtn>
                    </div>
                  </div>
                  {/* 读取 = 真的把这一档载进当前局；载进来了才跳主界面。 */}
                  <GlassButton
                    size="sm"
                    className="sc-load"
                    onClick={() => void load(slot.id)}
                  >
                    {busyId === slot.id ? '读取中…' : '读取'}
                  </GlassButton>
                </div>
                {index < slots.length - 1 ? (
                  <div className="sc-slotsep">
                    <Sep />
                  </div>
                ) : null}
              </div>
            ))
          )}
        </Card>

        {note ? (
          <p style={{ margin: 0, fontSize: 'var(--w-size-caption)', color: 'var(--w-signal)', lineHeight: 1.5 }}>{note}</p>
        ) : null}

        <div className="sc-bottom">
          <GlassButton size="lg" className="sc-wide" onClick={() => navigate('/select')}>
            新建存档
          </GlassButton>
          {/* 导入 / 备份要落盘，桥里没有这两个 method：停在禁用态，不做假成功。 */}
          <div className="sc-bottom__pair">
            <span className="sc-off" aria-disabled="true">
              <GlassButton size="md">导入存档</GlassButton>
            </span>
            <span className="sc-off" aria-disabled="true">
              <GlassButton size="md">备份全部</GlassButton>
            </span>
          </div>
        </div>
      </div>
    </PageShell>
  );
}
