import { useState } from 'react';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import { Card, Chip, PageHeader, Sep, TextBtn } from '../../components/kit';
import type { ScreenProps } from '../types';

/**
 * 插件权限台：Views/Settings/PluginPermissionsPage.xaml（169 行）+ ViewModels/Settings/PluginPermissionsViewModel.cs。
 * 能力目录（PluginCapabilities.All）× 这个插件的声明 × 用户给过的策略，再加它撞过哪些闸。
 */

type Grant = 'allow' | 'ask' | 'deny';
type Tier = 'own' | 'pipeline' | 'interact' | 'external';

type Capability = { name: string; title: string; detail: string; tier: Tier; defaultGrant: Grant; argHint: string };

const GRANT_LABEL: Record<Grant, string> = { allow: '允许', ask: '问一次', deny: '禁止' };

const CAPABILITIES: Capability[] = [
  {
    name: 'settings',
    title: '自己的设置',
    detail: '读与写 plugins/<插件>/settings.json 里属于它自己的那几项。看不到游戏设置，也看不到别的插件的设置。',
    tier: 'own',
    defaultGrant: 'allow',
    argHint: '',
  },
  {
    name: 'storage',
    title: '自己的文件区',
    detail: '在 plugins/<插件>/files/ 下面写小文件（单个 1MB、合计 8MB）。出了这个目录一律拒绝，读写不到存档、图片与设置。',
    tier: 'own',
    defaultGrant: 'allow',
    argHint: '',
  },
  {
    name: 'log',
    title: '写应用日志',
    detail: '往应用日志追加一行（自动带上插件名前缀）。日志会随数据包导出，别把用户的私密内容写进去。',
    tier: 'own',
    defaultGrant: 'allow',
    argHint: '',
  },
  {
    name: 'prompt.write',
    title: '注入人设与上下文',
    detail: 'persona / context 钩子往提示词里加文字。设为禁止后这两个钩子直接不执行——它唯一的作用就是加字。',
    tier: 'pipeline',
    defaultGrant: 'allow',
    argHint: '',
  },
  {
    name: 'pipeline.transform',
    title: '改写对话文本',
    detail: 'beforeSend / afterReply 的返回值替换原文。这是「改写这一句」，不是「接管管线」。设为禁止后钩子不执行，文本原样流过。',
    tier: 'pipeline',
    defaultGrant: 'allow',
    argHint: '',
  },
  {
    name: 'pipeline.override',
    title: '整段接管管线',
    detail: '声明了 overridePersona 的插件会清空别的人设段，只留自己那份。等于替主程序决定「TA 是谁」。',
    tier: 'pipeline',
    defaultGrant: 'ask',
    argHint: '',
  },
  {
    name: 'tools',
    title: '调用工具',
    detail: 'callTool 让宿主执行一个工具（内置、外部、MCP 都算）。工具能做的事比 JS 多得多，所以要逐个工具名放行。',
    tier: 'interact',
    defaultGrant: 'ask',
    argHint: '工具名，如 tools:send_to_user',
  },
  {
    name: 'plugins',
    title: '调用其它插件',
    detail: 'callPlugin 进入另一个插件的钩子。被调用方仍然受自己那份授权约束，不会借着别人的手越权。',
    tier: 'interact',
    defaultGrant: 'ask',
    argHint: '插件 id，如 plugins:mood_echo',
  },
  {
    name: 'broadcast',
    title: '广播与收听',
    detail: 'emit(topic, payload) 发给其它插件的 on:<topic> 钩子，话题名要逐条声明。禁止后 emit 直接被拦。',
    tier: 'interact',
    defaultGrant: 'ask',
    argHint: '话题名，如 broadcast:turn.done',
  },
  {
    name: 'notify',
    title: '发用户通知',
    detail: '在应用里弹一条用户可见的提示。打扰归打扰，但它不改数据，所以默认问一次。',
    tier: 'interact',
    defaultGrant: 'ask',
    argHint: '',
  },
  {
    name: 'stats',
    title: '调整角色数值',
    detail: 'adjustStat 直接改动某个角色的好感 / 信任 / 精力（走与主流程同一套钳制与落盘）。这是改数据，不是弹提示，所以逐个角色放行。',
    tier: 'interact',
    defaultGrant: 'ask',
    argHint: '角色 id，如 stats:mom',
  },
  {
    name: 'page',
    title: '打开自带页面',
    detail: 'manifest.pages 声明的页面，用系统浏览器打开。点了才开，插件自己弹不出来。',
    tier: 'interact',
    defaultGrant: 'allow',
    argHint: '',
  },
  {
    name: 'network',
    title: '访问外部网络',
    detail: 'fetch 出网。必须逐主机在 plugin.json 里声明（network:api.example.com），不跟跳转，只给 http/https。',
    tier: 'external',
    defaultGrant: 'deny',
    argHint: '主机名，如 network:api.example.com',
  },
  {
    name: 'localnet',
    title: '访问本机与内网',
    detail: '允许 fetch 打到 127.0.0.1、局域网与 169.254 这类地址——本机服务往往没鉴权，这一条比外网更危险。',
    tier: 'external',
    defaultGrant: 'deny',
    argHint: '',
  },
];

const UNO: Capability[] = CAPABILITIES.filter((capability) => capability.tier === 'own');
const PIPELINE: Capability[] = CAPABILITIES.filter((capability) => capability.tier === 'pipeline');
const INTERACT: Capability[] = CAPABILITIES.filter((capability) => capability.tier === 'interact');
const EXTERNAL: Capability[] = CAPABILITIES.filter((capability) => capability.tier === 'external');

const CAPTION = { fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)' } as const;

/** 一组能力的抬头：左边一根色条 + 标题 + 右侧说明，下面一条通到卡边的发丝线。 */
function TierHead({ bar, title, note }: { bar: string; title: string; note?: string }) {
  return (
    <>
      <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-3)' }}>
        <span style={{ width: '4px', height: '18px', borderRadius: '1px', background: bar }} aria-hidden="true" />
        <span style={{ flex: 1, fontSize: 'var(--w-size-subhead)', fontWeight: 700, color: 'var(--w-ink-display)' }}>
          {title}
        </span>
        {note ? <span style={CAPTION}>{note}</span> : null}
      </div>
      <Sep />
    </>
  );
}

export function PluginPerms({ query, back }: ScreenProps) {
  const pluginName = query.get('name') ?? '示例插件';
  const pluginVersion = query.get('version') ?? '1.0';
  const [policies, setPolicies] = useState<Record<string, Grant>>(() =>
    Object.fromEntries(CAPABILITIES.map((capability) => [capability.name, capability.defaultGrant])),
  );
  const [status, setStatus] = useState('');
  const [auditVisible, setAuditVisible] = useState(false);

  const custom = CAPABILITIES.filter((capability) => policies[capability.name] !== capability.defaultGrant).length;
  const summary =
    `${CAPABILITIES.length} 项能力里，这个插件声明了 0 项，现在能用 0 项` +
    (custom === 0 ? '，你还没改过任何一条（全按默认）。' : `，你改过 ${custom} 条。`);

  const setGrant = (capability: Capability, grant: Grant) => {
    setPolicies((prev) => ({ ...prev, [capability.name]: grant }));
    setStatus(`「${capability.title}」已设为${GRANT_LABEL[grant]}。（这个插件没声明它，声明了才用得上。）`);
  };

  const rowsOf = (list: Capability[]) =>
    list.map((capability) => {
      const policy = policies[capability.name];
      return (
        <Card key={capability.name}>
          <div style={{ display: 'flex', alignItems: 'flex-start', gap: 'var(--w-space-3)' }}>
            <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: '2px' }}>
              <span style={{ fontSize: 'var(--w-size-body)', color: 'var(--w-ink-display)' }}>{capability.title}</span>
              <span style={{ ...CAPTION, lineHeight: 1.6 }}>{capability.detail}</span>
            </div>
            <Chip>{'用不上'}</Chip>
          </div>

          <p style={{ margin: 'var(--w-space-2) 0 0', ...CAPTION, lineHeight: 1.6 }}>
            插件没声明这一项：允许了也不会给它——声明才是可审计的。
          </p>

          <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 'var(--w-space-2)', marginTop: 'var(--w-space-2)' }}>
            <TextBtn tone={policy === 'deny' ? 'signal' : 'plain'} onClick={() => setGrant(capability, 'deny')}>
              禁止
            </TextBtn>
            <TextBtn tone={policy === 'ask' ? 'signal' : 'plain'} onClick={() => setGrant(capability, 'ask')}>
              问一次
            </TextBtn>
            <TextBtn tone={policy === 'allow' ? 'signal' : 'plain'} onClick={() => setGrant(capability, 'allow')}>
              允许
            </TextBtn>
            <TextBtn onClick={() => setStatus(`「${capability.title}」下次用到时会重新问。`)}>重新问</TextBtn>
          </div>
        </Card>
      );
    });

  return (
    <PageShell>
      <PageHeader
        title="插件权限"
        subtitle={`${pluginName} v${pluginVersion}`}
        onBack={back}
        backText="< 返回"
        right={
          <GlassButton
            size="sm"
            onClick={() => {
              if (!window.confirm(`恢复默认\n撤销你对「${pluginName}」做过的全部授权改动？问过之后的答案也一起清掉。`)) return;
              setPolicies(
                Object.fromEntries(CAPABILITIES.map((capability) => [capability.name, capability.defaultGrant])),
              );
              setStatus('已恢复默认策略。');
            }}
          >
            恢复默认
          </GlassButton>
        }
      />

      <div className="screen" style={{ maxWidth: '920px' }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
          <span style={{ fontSize: 'var(--w-size-body)', color: 'var(--w-ink-body)' }}>{summary}</span>
          <span style={{ ...CAPTION, lineHeight: 1.6 }}>
            一条能力要用，得同时满足两件事：这个插件被允许，而且它确实要过——越出自己家门的用法（工具、别的插件、广播、联网）必须在
            plugin.json 里写明，没写明的即使你点「允许」也不会给它。「只在自己家里」那一组不要求声明：它碰的只有这个插件自己的设置项和文件区，圈是程序划的，作者重复声明一遍买不到任何保证。
          </span>
          <span style={{ ...CAPTION, lineHeight: 1.6 }}>
            这份清单里没有 permissions 数组：它写于权限目录之前，所以除「出本机」外的能力按默认策略对待（该问的照问、该禁的照禁）。只要它补上任何一条声明，就只认声明过的那几条。
          </span>
          {status ? (
            <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-signal)' }}>{status}</span>
          ) : null}
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
          <TierHead bar="var(--w-ink-faint)" title="只在自己家里 · 默认允许" note="这一组不要求声明，禁止仍然生效" />
          {rowsOf(UNO)}
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
          <TierHead bar="var(--w-signal)" title="动主程序的管线 · 默认允许，接管要问" />
          {rowsOf(PIPELINE)}
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
          <TierHead bar="var(--w-signal)" title="动用别的东西 · 默认要问" note="工具、别的插件、广播、通知" />
          {rowsOf(INTERACT)}
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
          <TierHead bar="var(--w-ink-display)" title="出本机 · 默认禁止" note="逐主机声明，逐主机放行" />
          {rowsOf(EXTERNAL)}
        </div>

        <Card title="判定留痕" right={<TextBtn onClick={() => setAuditVisible((prev) => !prev)}>{auditVisible ? '收起' : '展开'}</TextBtn>}>
          <span style={{ ...CAPTION, lineHeight: 1.6 }}>
            每一次拦截、每一次问过之后放行都记在这里（同一条反复撞上只计次）。
          </span>
          <p style={{ margin: 'var(--w-space-2) 0 0', ...CAPTION, lineHeight: 1.6 }}>
            还没有判定记录：这个插件至今没撞上任何一道闸。
          </p>
        </Card>
      </div>
    </PageShell>
  );
}
