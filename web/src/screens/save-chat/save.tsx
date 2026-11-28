import { useEffect, useState } from 'react';
import type { SaveRow } from '../../bridge/protocol';
import { Card, PageHeader, Sep, TextBtn } from '../../components/kit';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
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
  const [slots, setSlots] = useState<Slot[]>([]);
  // 存档图标：按角色名对上花名册里的头像；没有头像就退回首字方块。
  const [avatars, setAvatars] = useState<Record<string, string>>({});

  // 真的存档表：SaveManager.List() 那一份。写盘后宿主推 data.changed，这里跟着重读。
  useEffect(() => {
    let dropped = false;
    const read = async () => {
      try {
        const { slots: rows } = await bridge.request('saves.list', {});
        if (dropped) return;
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
      } catch {
        /* 老宿主没有这个方法：列表留空，走源码的空态 */
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
  }, [bridge]);

  return (
    <PageShell>
      <PageHeader
        title="存档管理"
        onBack={back}
        right={
          <>
            <TextBtn onClick={() => navigate('/superMonitor')}>监测</TextBtn>
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
          {slots.length === 0 ? (
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
                      <TextBtn>重命名</TextBtn>
                      <TextBtn>分支</TextBtn>
                      <TextBtn>导出</TextBtn>
                      <TextBtn>权限</TextBtn>
                      <TextBtn>删除</TextBtn>
                    </div>
                  </div>
                  <GlassButton size="sm" className="sc-load" onClick={() => navigate('/main')}>
                    读取
                  </GlassButton>
                </div>
                {index < slots.length - 1 ? <Sep /> : null}
              </div>
            ))
          )}
        </Card>

        <div className="sc-bottom">
          <GlassButton size="lg" className="sc-wide" onClick={() => navigate('/select')}>
            新建存档
          </GlassButton>
          <div className="sc-bottom__pair">
            <GlassButton size="md">导入存档</GlassButton>
            <GlassButton size="md">备份全部</GlassButton>
          </div>
        </div>
      </div>
    </PageShell>
  );
}
