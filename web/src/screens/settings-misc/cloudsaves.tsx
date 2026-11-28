import type { CSSProperties } from 'react';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import { Card, PageHeader } from '../../components/kit';
import type { ScreenProps } from '../types';
import { Inert } from './parts';

/**
 * 云存档（Views/Settings/CloudSavesPage.xaml）：私人仓库门禁的三种外观、两段存档列表
 * （当前设备 / 其他设备与历史副本）。远端的读取要真实令牌与网络，后端没有对应方法，
 * 这里停在「还没验证、还没刷新」的那一档：门禁卡与说明都在，列表两段不出现。
 */

const SYNC_LINE = '上次同步：还没有同步记录';

const muted: CSSProperties = {
  margin: 0,
  fontSize: 'var(--w-size-small)',
  color: 'var(--w-ink-muted)',
  lineHeight: 1.6,
};
const caption: CSSProperties = { fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)', lineHeight: 1.6 };
const subhead: CSSProperties = {
  margin: 0,
  fontSize: 'var(--w-size-subhead)',
  fontWeight: 600,
  color: 'var(--w-ink-display)',
};

export function Cloudsaves({ back }: ScreenProps) {
  return (
    <PageShell>
      <PageHeader
        title="云存档"
        subtitle={SYNC_LINE}
        onBack={back}
        right={
          <Inert>
            <GlassButton size="sm">刷新</GlassButton>
          </Inert>
        }
      />

      <main className="screen">
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            gap: 'var(--w-space-2)',
            width: '100%',
            maxWidth: 880,
            margin: '0 auto',
          }}
        >
          <Card>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-2)' }}>
              <span style={subhead}>云存档只写私人仓库</span>
              <Inert>
                <GlassButton size="sm">验证私人仓库</GlassButton>
              </Inert>
            </div>
            <p style={muted}>尚未验证仓库私有性</p>
            <p style={caption}>
              填了公开仓库，存档就会跟着公开。访问令牌就是登录凭据：只保存在这台设备，不会上传到任何服务器；请使用只有
              repo（私有仓库读写）权限的令牌。
            </p>
          </Card>

          <p style={muted}>
            把另一台设备上传的存档取回本机；同步覆盖前留下的旧版本，也能从这里恢复。令牌只留在本机。
          </p>

          <p style={{ ...caption, margin: '8px 0 0 4px' }}>
            历史副本保存在本机，断网时也能看、能恢复；访问令牌不会离开这台设备。
          </p>
        </div>
      </main>
    </PageShell>
  );
}
