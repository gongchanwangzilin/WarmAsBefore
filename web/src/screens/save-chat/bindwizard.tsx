import { useState } from 'react';
import { Card, PageHeader } from '../../components/kit';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import type { ScreenProps } from '../types';
import './savechat.css';

/**
 * 绑定向导：Views/BindWizardPage.xaml（63 行）+ BindWizardPage.xaml.cs。
 *
 * 首次连进一个 Minecraft 世界时的四张卡：世界（世界 / 主人 / 女仆）、存档（选旧档或「新建一份存档」）、
 * 角色（只在新建档时出现）、女仆（选一个接进这个世界）。页脚是一句提示 + 开始绑定。
 * 世界与女仆由游戏桥推送（Configure），存档与角色来自 SaveManager.List() / CharacterLibrary.ListAsync()，
 * 前端一条都读不到，这里按「还没看到女仆」这一档摆；一条存档都没有时，源码默认落回「新建一份存档」，
 * 角色库是空的就默认「用女仆的名字新建角色」。
 */

type Pick = { id: string; title: string; desc: string; enabled: boolean };

const SLOTS: Pick[] = [];

const CHARACTERS: Pick[] = [];

const WORLD = 'Minecraft 世界';
const OWNER = '未知';
const MAIDS: string[] = [];

export function Bindwizard({ back }: ScreenProps) {
  const [saveId, setSaveId] = useState('');
  const [newSave, setNewSave] = useState(true);
  const [charId, setCharId] = useState('');
  const [fromMaid, setFromMaid] = useState(true);

  const hasMaid = MAIDS.length > 0;
  const hasChar = newSave ? fromMaid || charId.length > 0 : SLOTS.some((slot) => slot.id === saveId);

  const hint = !hasMaid
    ? '游戏里还没有可绑定的女仆'
    : !hasChar
      ? '先选一份存档，或为这份新档选一个角色'
      : '';

  const pick = (item: Pick, selected: boolean, onTap: () => void) => (
    <button
      key={item.id}
      type="button"
      className="sc-pick"
      data-selected={selected}
      data-enabled={item.enabled}
      onClick={item.enabled ? onTap : undefined}
    >
      <span className="sc-pick__bar" style={{ visibility: selected ? 'visible' : 'hidden' }} />
      <span className="sc-pick__text">
        <span className="sc-pick__title">{item.title}</span>
        {item.desc ? <span className="sc-pick__desc">{item.desc}</span> : null}
      </span>
    </button>
  );

  return (
    <PageShell>
      <PageHeader
        title="接入 Minecraft 世界"
        subtitle={`${WORLD} · 主人 ${OWNER} · ${MAIDS.length} 只女仆`}
        onBack={back}
      />

      <div className="screen sc-wiz">
        <Card>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-1)' }}>
            <span className="sc-line">
              世界
              <b className="sc-line__strong">{WORLD}</b>
            </span>
            <span className="sc-line">
              主人
              <b>{OWNER}</b>
            </span>
            <span className="sc-line">
              女仆
              <b>{MAIDS.length === 0 ? '还没看到女仆' : MAIDS.join('、')}</b>
            </span>
          </div>
        </Card>

        <Card
          title="存档"
          note="选一份已有存档接着用，或者为这个世界新建一份。选「新建」并点右下角按钮，会打开 WAB 自己的新建存档向导（世界 → 人设 → 细则 → 开始），走完自动回来完成绑定。"
        >
          <div className="sc-picks">
            {SLOTS.map((slot) =>
              pick(slot, !newSave && saveId === slot.id, () => {
                setNewSave(false);
                setSaveId(slot.id);
              }),
            )}
            {pick(
              { id: 'new', title: '新建一份存档', desc: `为「${WORLD}」开一份新档，并绑定下面选的角色`, enabled: true },
              newSave,
              () => {
                setNewSave(true);
                setSaveId('');
              },
            )}
          </div>
        </Card>

        {newSave ? (
          <Card
            title="角色"
            note="新建的存档要绑定一个角色：她在 WAB 里由这个角色说话、记事。先在这里定好人，再交给新建存档向导。"
          >
            <div className="sc-picks">
              {CHARACTERS.map((character) =>
                pick(character, !fromMaid && charId === character.id, () => {
                  setFromMaid(false);
                  setCharId(character.id);
                }),
              )}
              {pick(
                {
                  id: 'from-maid',
                  title: '用「女仆」的名字新建角色',
                  desc: '按女仆名建一个默认角色（温柔可爱），之后可以在角色库里慢慢改',
                  enabled: true,
                },
                fromMaid,
                () => setFromMaid(true),
              )}
            </div>
          </Card>
        ) : null}

        <Card title="女仆" note="选择一个要接进这个世界的女仆；其余女仆之后可以在插件里逐个绑定。">
          {MAIDS.length === 0 ? (
            <p className="cap">游戏里还没有女仆：先训服一只，再回来连接。</p>
          ) : (
            <div className="sc-picks">{MAIDS.map((maid) => pick({ id: maid, title: maid, desc: '', enabled: true }, false, () => undefined))}</div>
          )}
        </Card>
      </div>

      <div className="sc-wiz__footer">
        <span className="cap">{hint || '绑定要等游戏那边推来世界与女仆：这一页现在只摆文档骨架'}</span>
        {/* 绑定这条动作还没有桥方法：灰着，别让它演成一颗点了没反应的钮。 */}
        <span style={{ opacity: 0.45, pointerEvents: 'none' }} aria-disabled="true">
          <GlassButton size="sm" disabled title="宿主还没有绑定的方法">
            {newSave ? '新建存档并绑定' : '开始绑定'}
          </GlassButton>
        </span>
      </div>
    </PageShell>
  );
}
