import { useEffect, useState } from 'react';
import { GlassButton } from '../../components/GlassButton';
import { Empty, PageHeader, Row, Sep } from '../../components/kit';
import { PageShell } from '../../components/PageShell';
import type { ScreenProps } from '../types';
import { Actions, FootBar, Inert, Note } from './parts';

/**
 * 点名 · 进入剧情：ForceCastPage.xaml。
 * 名册读 characters.list（空不空按真的说）；「点名」本身要宿主给进入剧情的方法，
 * 现在还接不上，所以那颗钮画成禁用态，而不是点了没反应。
 */
export function Forcecast({ bridge, navigate, back }: ScreenProps) {
  const [names, setNames] = useState<string[]>([]);
  const [loaded, setLoaded] = useState(false);
  const [loadError, setLoadError] = useState('');

  useEffect(() => {
    let alive = true;
    void bridge
      .request('characters.list', {})
      .then(({ characters }) => {
        if (!alive) return;
        setNames(characters.map((row) => row.nickname || row.name));
      })
      .catch((cause: unknown) => {
        if (!alive) return;
        setLoadError(cause instanceof Error ? cause.message : '角色库没有应答。');
      })
      .finally(() => {
        if (alive) setLoaded(true);
      });
    return () => {
      alive = false;
    };
  }, [bridge]);

  return (
    <PageShell>
      <PageHeader
        title="点名 · 进入剧情"
        onBack={back}
        right={<span className="cap">{names.length > 0 ? `${names.length} 个人可选` : '角色库还是空的'}</span>}
      />

      <div className="screen" style={{ minHeight: 'clamp(200px, 44vh, 420px)' }}>
        {loadError ? (
          <Empty title="角色库读不出来" reason={loadError} hint="确认后台还在跑，再重新进这一页。" />
        ) : !loaded ? null : names.length === 0 ? (
          <>
            <Empty title="角色库还是空的" reason="先创建几个角色，再来点名吧" />
            <span style={{ alignSelf: 'center' }}>
              <GlassButton size="sm" onClick={() => navigate('/newcharacter')}>
                ＋ 新建角色
              </GlassButton>
            </span>
          </>
        ) : (
          <>
            {names.map((name) => (
              <div key={name}>
                <Row title={name} desc="勾选要点名的角色，并为 TA 指定初始位置" />
                <Sep />
              </div>
            ))}
            <Note>
              勾选与「进入剧情」还要宿主的支持：这一页目前只把名册摆出来，点名点不下去。
            </Note>
          </>
        )}
      </div>

      <FootBar>
        <div style={{ width: '100%' }}>
          <Actions>
            <Inert>
              <GlassButton size="md">确定点名 · 进入剧情</GlassButton>
            </Inert>
          </Actions>
        </div>
      </FootBar>
    </PageShell>
  );
}
