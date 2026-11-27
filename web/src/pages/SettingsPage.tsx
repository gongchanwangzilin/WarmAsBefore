import { useMemo, useState } from 'react';
import type { Bridge } from '../bridge/client';
import type { SettingsValues } from '../bridge/protocol';
import { Glass } from '../components/Glass';
import { PageShell } from '../components/PageShell';
import { TopBar } from '../components/TopBar';
import { SkeletonRows } from '../components/states/Skeleton';
import { StateBlock } from '../components/states/StateBlock';
import { homeGroups, matchesQuery, type SettingsSectionRow } from '../lib/settingsHome';
import { useIsWide } from '../lib/useIsWide';
import { useResource } from '../lib/useResource';

type Props = {
  bridge: Bridge;
  onBack: () => void;
  onNavigate: (path: string) => void;
};

/**
 * 设置首页：大标题 → 搜索 →「常驻」「全部设置」两组 inset 列表（SettingsPage.xaml）。
 * 分组、顺序、右侧当前值全部来自 lib/settingsHome.ts，这里只排结构。
 */
export function SettingsPage({ bridge, onBack, onNavigate }: Props) {
  const { state, reload } = useResource(() => bridge.request('settings.get', {}), [bridge]);
  const [query, setQuery] = useState('');

  return (
    <PageShell>
      <TopBar title="设置" onBack={onBack} />

      <main className="home">
        <div className="home__body">
          <h2 className="home__title">设置</h2>
          <p className="home__sub">选择一处，开始调校</p>

          <Glass as="div" tone="card" radius={12} className="search">
            <input
              className="search__input"
              type="text"
              value={query}
              placeholder="搜设置（如：记忆 / 模型 / 语音 / 群聊）"
              aria-label="搜设置"
              onChange={(event) => setQuery(event.target.value)}
            />
            <button type="button" className="textbtn" onClick={() => setQuery('')}>
              清空
            </button>
          </Glass>

          {state.status === 'loading' ? (
            <div aria-busy="true">
              <h3 className="group">常驻</h3>
              <Glass as="div" tone="card" radius={12} className="list">
                <SkeletonRows count={3} />
              </Glass>
              <h3 className="group">全部设置</h3>
              <Glass as="div" tone="card" radius={12} className="list">
                <SkeletonRows count={3} />
              </Glass>
              <p className="sr-only">正在读取设置…</p>
            </div>
          ) : null}

          {state.status === 'error' ? (
            <StateBlock
              tone="error"
              title="读不到设置"
              body={<p>本地服务这次没有回话。确认后台还在运行，再试一次。</p>}
              detail={`${state.error.message}（${state.error.code}）`}
              action="重新读取"
              onAction={reload}
            />
          ) : null}

          {state.status === 'ready' && state.data.values === null ? (
            <StateBlock
              tone="empty"
              title="没有可调的设置"
              body={<p>后端还没有注册任何设置项。等模块挂上来再看。</p>}
              action="再读一次"
              onAction={reload}
            />
          ) : null}

          {state.status === 'ready' && state.data.values ? (
            <Groups values={state.data.values} query={query} onOpen={onNavigate} />
          ) : null}
        </div>
      </main>
    </PageShell>
  );
}

function Groups({
  values,
  query,
  onOpen,
}: {
  values: SettingsValues;
  query: string;
  onOpen: (path: string) => void;
}) {
  const wide = useIsWide();
  const { primary, rest } = useMemo(() => homeGroups(values), [values]);
  const visible = (rows: SettingsSectionRow[]) => rows.filter((row) => matchesQuery(row, query));
  const primaryRows = visible(primary);
  const restRows = visible(rest);
  const searching = query.trim().length > 0;
  const count = primaryRows.length + restRows.length;

  return (
    <>
      {searching ? (
        <p className="cap">匹配到 {count} 个设置项（进子页后只显示这些）</p>
      ) : null}

      {primaryRows.length > 0 ? (
        <>
          <h3 className="group">常驻</h3>
          <GroupCard rows={primaryRows} wide={wide} onOpen={onOpen} />
        </>
      ) : null}

      {restRows.length > 0 ? (
        <>
          <h3 className="group">全部设置</h3>
          <GroupCard rows={restRows} wide={wide} onOpen={onOpen} />
        </>
      ) : null}
    </>
  );
}

/**
 * 一张组卡。宽屏按源码摊成两栏、中间一道竖发丝线（BuildGroupGrid），
 * 前半进左栏、后半进右栏，两栏各自从上往下铺；首行都不带顶线。
 */
function GroupCard({
  rows,
  wide,
  onOpen,
}: {
  rows: SettingsSectionRow[];
  wide: boolean;
  onOpen: (path: string) => void;
}) {
  const split = Math.ceil(rows.length / 2);
  const left = wide ? rows.slice(0, split) : rows;
  const right = wide ? rows.slice(split) : [];

  return (
    <Glass as="div" tone="card" radius={12} className={`list${wide ? ' list--wide' : ''}`}>
      <div className="list__col">
        {left.map((row, index) => (
          <ListRow key={row.key} row={row} first={index === 0} onOpen={onOpen} />
        ))}
      </div>
      {wide ? (
        <>
          <div className="list__vsep" aria-hidden="true" />
          <div className="list__col">
            {right.map((row, index) => (
              <ListRow key={row.key} row={row} first={index === 0} onOpen={onOpen} />
            ))}
          </div>
        </>
      ) : null}
    </Glass>
  );
}

/** 一行 54 高：标题 + 右侧当前值 + ›。首行不带发丝线，其余行顶部一根通到卡边。 */
function ListRow({
  row,
  first,
  onOpen,
}: {
  row: SettingsSectionRow;
  first: boolean;
  onOpen: (path: string) => void;
}) {
  return (
    <button
      type="button"
      className={`l-row${first ? ' l-row--first' : ''}`}
      onClick={() => row.to && onOpen(row.to)}
      aria-disabled={row.to ? undefined : true}
    >
      <span className="l-row__title">{row.title}</span>
      <span className="l-row__summary">{row.summary}</span>
      <span className="l-row__chev" aria-hidden="true">
        ›
      </span>
    </button>
  );
}
