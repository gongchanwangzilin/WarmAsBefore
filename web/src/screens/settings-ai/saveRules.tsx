import { useCallback, useEffect, useState, type CSSProperties, type ReactNode } from 'react';
import type { SaveRules as SaveRulesPayload, SaveRulesEdit } from '../../bridge/protocol';
import { GlassSwitch } from '../../components/GlassSwitch';
import { PageShell } from '../../components/PageShell';
import { SignalButton } from '../../components/SignalButton';
import { Card, PageHeader, Row, Sep } from '../../components/kit';
import type { ScreenProps } from '../types';

const CAPTION: CSSProperties = { fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)', lineHeight: 1.6 };
const SECTION_TITLE: CSSProperties = {
  margin: '0 0 0 0',
  fontSize: 'var(--w-size-subhead)',
  fontWeight: 700,
  color: 'var(--w-ink-display)',
};

type RuleItem = { id: string; name: string; hint?: string; allowed: boolean };
type Character = { id: string; name: string; isMain: boolean };

/** 一组白名单的抬头：一行小标题，下面才是 CardGroup。 */
function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
      <h2 style={SECTION_TITLE}>{title}</h2>
      {children}
    </section>
  );
}

/**
 * 存档高级设置（Views/Settings/SaveRulesPage.xaml，315 行）+ SaveRulesViewModel.cs。
 * 这一份存档能用哪些插件 / 工具 / 角色、主角是谁、用哪套世界观、性欲系统点名了谁。
 *
 * 白名单由 SaveManager.LoadRulesAsync / SaveRulesAsync 读写（save.rules.get / save.rules.set），
 * 与原生那一页同一份落盘；换档就换一套。世界观与「这一档的主角」还没接桥，只读出当前值，不给点了没动静的钮。
 */
export function SaveRules({ bridge, query, back }: ScreenProps) {
  const saveId = query.get('id') ?? '';

  const [data, setData] = useState<SaveRulesPayload | null>(null);
  const [loadError, setLoadError] = useState('');
  const [ticket, setTicket] = useState(0);
  const [allowAllPlugins, setAllowAllPlugins] = useState(true);
  const [allowAllTools, setAllowAllTools] = useState(true);
  const [allowAiInstallPlugins, setAllowAiInstallPlugins] = useState(false);
  const [allowAllCharacters, setAllowAllCharacters] = useState(true);
  const [worldviewCrossSave] = useState(true);
  const [plugins, setPlugins] = useState<RuleItem[]>([]);
  const [tools, setTools] = useState<RuleItem[]>([]);
  const [characters, setCharacters] = useState<Character[]>([]);
  const [characterAllowed, setCharacterAllowed] = useState<Record<string, boolean>>({});
  const [status, setStatus] = useState('');
  const [saving, setSaving] = useState(false);

  const read = useCallback(
    async (id: string) => {
      try {
        const payload = await bridge.request('save.rules.get', id ? { id } : {});
        const rules = payload.rules;
        setData(payload);
        setLoadError('');
        setAllowAllPlugins(rules.allowAllPlugins);
        setAllowAllTools(rules.allowAllTools);
        setAllowAiInstallPlugins(rules.allowAiInstallPlugins);
        setAllowAllCharacters(rules.allowAllCharacters);
        setPlugins(
          payload.plugins.map((p) => ({
            id: p.id,
            name: p.name,
            hint: `v${p.version} · ${p.enabled ? '已启用' : '已停用'}`,
            allowed: rules.allowAllPlugins || (rules.allowedPlugins ?? []).some((x) => x.toLowerCase() === p.id.toLowerCase()),
          })),
        );
        setTools(
          payload.tools.map((t) => ({
            id: t.name,
            name: t.name,
            hint: t.description,
            allowed: rules.allowAllTools || (rules.allowedTools ?? []).some((x) => x.toLowerCase() === t.name.toLowerCase()),
          })),
        );
        setCharacters(payload.characters.map((c) => ({ id: c.id, name: c.name, isMain: c.main })));
        setCharacterAllowed(
          Object.fromEntries(
            payload.characters.map((c) => [
              c.id,
              rules.allowAllCharacters || rules.allowedCharacters.some((x) => x.toLowerCase() === c.id.toLowerCase()),
            ]),
          ),
        );
        setStatus(
          `插件 ${payload.plugins.length} 个 · 工具 ${payload.tools.length} 个 · 角色 ${payload.characters.length} 个。`,
        );
      } catch (cause) {
        setLoadError(cause instanceof Error ? cause.message : '这份存档的规则没有应答。');
        setData(null);
      }
    },
    [bridge],
  );

  useEffect(() => {
    let dropped = false;
    void read(saveId).then(() => {
      if (dropped) return;
    });
    return () => {
      dropped = true;
    };
  }, [read, saveId, ticket]);

  const togglePlugin = (id: string) =>
    setPlugins((prev) => prev.map((item) => (item.id === id ? { ...item, allowed: !item.allowed } : item)));
  const toggleTool = (id: string) =>
    setTools((prev) => prev.map((item) => (item.id === id ? { ...item, allowed: !item.allowed } : item)));
  const toggleCharacter = (id: string) =>
    setCharacterAllowed((prev) => ({ ...prev, [id]: !prev[id] }));

  /** 保存：整份白名单写回这一档；宿主回 false 就不会走到这里（应答直接失败）。 */
  const save = async () => {
    if (saving) return;
    setSaving(true);
    const allowedCharacters = allowAllCharacters ? [] : characters.filter((c) => characterAllowed[c.id]).map((c) => c.id);
    const rules: SaveRulesEdit = {
      allowAllPlugins,
      allowAllTools,
      allowAiInstallPlugins,
      allowAllCharacters,
      allowedCharacters,
      allowedPlugins: plugins.filter((p) => p.allowed).map((p) => p.id),
      allowedTools: tools.filter((t) => t.allowed).map((t) => t.id),
    };
    try {
      await bridge.request('save.rules.set', saveId ? { id: saveId, rules } : { rules });
      setStatus('已保存。下次进入这个存档时生效；AI 安装插件的开关同样跟着存档走。');
    } catch (cause) {
      setStatus('保存失败：' + (cause instanceof Error ? cause.message : '宿主没有应答。'));
    } finally {
      setSaving(false);
    }
  };

  const worldviews = 0;
  const mainName = characters.find((c) => c.isMain)?.name ?? '';

  return (
    <PageShell>
      <PageHeader title="存档高级设置" onBack={back} backText="< 返回" />

      <div className="screen" style={{ maxWidth: 900 }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-1)' }}>
          <p style={{ ...CAPTION, margin: 0 }}>
            这些白名单只属于这一份存档：换档就换一套。插件与工具被关掉时不会执行；角色白名单决定 AI 能在这份存档里让谁出场。
          </p>
          {data?.label ? <p style={{ ...CAPTION, margin: 0 }}>{data.label}</p> : null}
        </div>

        {loadError ? (
          <Card>
            <div className="sc-empty">
              <span>存档规则读不出来</span>
              <small>{loadError}</small>
              <button
                type="button"
                className="k-textbtn"
                style={{ color: 'var(--w-signal)' }}
                onClick={() => setTicket((n) => n + 1)}
              >
                重试
              </button>
            </div>
          </Card>
        ) : null}

        <Card>
          <Row
            title="允许全部插件"
            desc="关掉后，只有下面勾选的插件能在这份存档里跑"
            right={<GlassSwitch label="允许全部插件" checked={allowAllPlugins} onChange={setAllowAllPlugins} />}
          />
          <Sep />
          <Row
            title="允许全部工具"
            desc="工具包括内置工具与外部 Java/Python 工具"
            right={<GlassSwitch label="允许全部工具" checked={allowAllTools} onChange={setAllowAllTools} />}
          />
          <Sep />
          <Row
            title="AI 可以自行安装插件"
            desc="默认关闭：装代码应该由你做决定"
            right={
              <GlassSwitch
                label="AI 可以自行安装插件"
                checked={allowAiInstallPlugins}
                onChange={setAllowAiInstallPlugins}
              />
            }
          />
        </Card>

        <Section title="可以运行的插件">
          <Card>
            {plugins.length === 0 ? (
              <p style={{ ...CAPTION, margin: 0 }}>还没有装插件。插件中心可以从 zip / 文件夹安装，也可以让工坊写一个。</p>
            ) : (
              plugins.map((plugin, index) => (
                <div key={plugin.id}>
                  {index > 0 ? <Sep /> : null}
                  <Row
                    title={plugin.name}
                    desc={plugin.hint}
                    right={
                      <GlassSwitch
                        label={plugin.name}
                        checked={plugin.allowed}
                        onChange={() => togglePlugin(plugin.id)}
                        disabled={allowAllPlugins}
                      />
                    }
                  />
                </div>
              ))
            )}
          </Card>
        </Section>

        <Section title="可以调用的工具">
          <Card>
            {tools.map((tool, index) => (
              <div key={tool.id}>
                {index > 0 ? <Sep /> : null}
                <Row
                  title={tool.name}
                  desc={tool.hint}
                  right={
                    <GlassSwitch
                      label={tool.name}
                      checked={tool.allowed}
                      onChange={() => toggleTool(tool.id)}
                      disabled={allowAllTools}
                    />
                  }
                />
              </div>
            ))}
          </Card>
        </Section>

        <Section title="这一档的主角">
          <Card>
            <p style={{ ...CAPTION, margin: 0 }}>
              主角只属于这份存档：换个档可以从另一个人开始，角色库本身不记谁是主角。
            </p>
            {mainName ? (
              <>
                <Sep />
                <Row title={mainName} desc="这一档的主角" />
              </>
            ) : null}
          </Card>
        </Section>

        <Section title="可以出场的角色">
          <Card>
            <Row
              title="允许全部角色"
              desc="关掉后只让勾选的角色进入这份存档的剧情"
              right={
                <GlassSwitch label="允许全部角色" checked={allowAllCharacters} onChange={setAllowAllCharacters} />
              }
            />
            {characters.map((character) => (
              <div key={character.id}>
                <Sep />
                <Row
                  title={character.name}
                  right={
                    <GlassSwitch
                      label={character.name}
                      checked={Boolean(characterAllowed[character.id])}
                      onChange={() => toggleCharacter(character.id)}
                      disabled={allowAllCharacters}
                    />
                  }
                />
              </div>
            ))}
            {characters.length === 0 ? <p style={{ ...CAPTION, margin: 0 }}>角色库里还没有别的角色。</p> : null}
          </Card>
        </Section>

        <Section title="这一档的角色世界观">
          <Card>
            <Row
              title="世界观改动跨存档生效"
              desc={
                worldviewCrossSave
                  ? '开：在这里换世界观会写回角色，其他存档一起换。'
                  : '关：只在当前这一档换，角色与其他存档不受影响。'
              }
              right={
                worldviews > 0 ? (
                  <GlassSwitch label="世界观改动跨存档生效" checked={worldviewCrossSave} onChange={() => undefined} />
                ) : undefined
              }
            />
            <Sep />
            <p style={{ ...CAPTION, margin: 0 }}>这份存档的主角还没进角色库。</p>
          </Card>
        </Section>

        <Section title="性欲系统参与角色">
          <Card>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-1)', padding: '4px 0' }}>
              <span style={{ fontSize: 'var(--w-size-body)', fontWeight: 700, color: 'var(--w-ink-display)' }}>
                只对这里点名的人生效
              </span>
              <span style={CAPTION}>
                没点名的人完全不参与。数值由设定（写死的值）与女性周期共同决定，设定优先——写死时周期不再影响它。
              </span>
              <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-signal)', lineHeight: 1.6 }}>
                总开关在「设置 → 体验」里，目前是关的；下面勾了也不会生效。
              </span>
            </div>
            {characters.map((character) => (
              <div key={character.id}>
                <Sep />
                <Row
                  title={character.name}
                  right={<GlassSwitch label={character.name} checked={false} onChange={() => undefined} disabled />}
                />
              </div>
            ))}
            {characters.length === 0 ? <p style={{ ...CAPTION, margin: 0 }}>角色库里还没有别的角色。</p> : null}
          </Card>
        </Section>

        <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-signal)', lineHeight: 1.6 }}>{status}</span>
        <div>
          <SignalButton onClick={() => void save()}>保存这份存档的规则</SignalButton>
        </div>
      </div>
    </PageShell>
  );
}
