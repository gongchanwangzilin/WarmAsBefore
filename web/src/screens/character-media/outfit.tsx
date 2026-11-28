import { useState } from 'react';
import { PageShell } from '../../components/PageShell';
import { PageHeader, Card } from '../../components/kit';
import type { ScreenProps } from '../types';
import { Cap, NoteRow, PrimarySm, Sub, Well, Muted } from './parts';

/**
 * 换装（OutfitPage.xaml，100 行）：当前立绘 + 已有装扮 + 一句话生成新装扮。
 * 角色与装扮都来自后端（这里没有），所以按源码「还没进入角色」那一档的空态渲染；
 * 输入框与「生成并穿上」是本地状态。
 */
export function Outfit({ back }: ScreenProps) {
  const [prompt, setPrompt] = useState('');

  return (
    <PageShell>
      <PageHeader title="换装" onBack={back} />

      <div className="screen" style={{ maxWidth: 900, gap: 'var(--w-space-3)' }}>
        <Card>
          <Sub>还没有进入角色</Sub>
          <Well height={360} caption="立绘" radius="var(--w-radius-card)" />
          <Cap>当前：</Cap>
        </Card>

        <Cap style={{ color: 'var(--w-signal)' }}>先在角色选择页开始一局，或把某个角色设为主角。</Cap>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
          <Sub>已有的装扮</Sub>
          <Card tight>
            <NoteRow>这个角色还没有立绘。可以在下面用一句描述生成第一套装扮。</NoteRow>
          </Card>
        </div>

        <Card>
          <Sub>用一句话生成新装扮</Sub>
          <Cap>
            描述衣服与场景即可（例如：白色针织衫配浅色长裙，站在午后窗边）。生成的是全身立绘，会直接穿上并登记进这个角色。
          </Cap>
          <input
            className="k-field"
            value={prompt}
            placeholder="想给 TA 穿什么？"
            aria-label="想给 TA 穿什么？"
            onChange={(event) => setPrompt(event.target.value)}
            style={{ fontSize: 'var(--w-size-small)', lineHeight: 'var(--w-lh-small)' }}
          />
          <div>
            <PrimarySm disabled>生成并穿上</PrimarySm>
          </div>
          <Muted>没有开启画面生成？去设置 → AI → 画面生成填接口与密钥。</Muted>
        </Card>
      </div>
    </PageShell>
  );
}
