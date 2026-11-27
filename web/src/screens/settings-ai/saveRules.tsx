import { useState, type CSSProperties, type ReactNode } from 'react';
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

/** 内置工具（ToolManager.RegisterBuiltin 的名字与说明），顺序即注册顺序里最常用的那几条。 */
const TOOLS: RuleItem[] = [
  { id: 'get_state', name: 'get_state', hint: '获取当前游戏状态（角色、好感、精力、所在地点）', allowed: true },
  { id: 'send_message', name: 'send_message', hint: '向游戏里的角色发送一条消息', allowed: true },
  { id: 'set_sprite', name: 'set_sprite', hint: '设置角色立绘：参数为 "位置[,x,y]" 或 "opacity:0.5" 形式', allowed: true },
  { id: 'animate', name: 'animate', hint: '播放立绘动画：sink(下沉) / jump(跳跃) / shake(颤抖)', allowed: true },
  { id: 'scene_list', name: 'scene_list', hint: '列出当前可用场景选项（带编号）', allowed: true },
  { id: 'scene_set', name: 'scene_set', hint: '按编号应用一个场景选项', allowed: true },
  {
    id: 'memory_chain',
    name: 'memory_chain',
    hint: '从一条线索沿字词、时间、共同经历扩散，找回一串相关的记忆（默认 5 条，新的/更相似的在前）。',
    allowed: true,
  },
  {
    id: 'send_to_user',
    name: 'send_to_user',
    hint: '给用户真的发一条消息到他的手机上（QQ 私聊），不是游戏里的对话。',
    allowed: true,
  },
];

type Character = { id: string; name: string; libidoHint: string };

/** 这一档里的角色来自 CharacterLibrary.ListAsync()，Web 端读不到，就一个也不列。 */
const CHARACTERS: Character[] = [];

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
 * 存档规则由 SaveManager 读写，Web 端没有对应 method，勾选与开关停在本地状态；
 * 存档标签（SaveLabel）与角色清单也读不到，角色栏就照源码说「角色库里还没有别的角色。」。
 */
export function SaveRules({ back }: ScreenProps) {
  const [allowAllPlugins, setAllowAllPlugins] = useState(true);
  const [allowAllTools, setAllowAllTools] = useState(true);
  const [allowAiInstallPlugins, setAllowAiInstallPlugins] = useState(false);
  const [allowAllCharacters, setAllowAllCharacters] = useState(true);
  const [worldviewCrossSave, setWorldviewCrossSave] = useState(true);
  const [tools, setTools] = useState(TOOLS);
  const [characters, setCharacters] = useState(CHARACTERS.map((item) => ({ ...item, allowed: true, participates: false })));
  const [status, setStatus] = useState(`插件 0 个 · 工具 ${TOOLS.length} 个 · 角色 ${CHARACTERS.length} 个。`);

  const toggleTool = (id: string) =>
    setTools((prev) => prev.map((item) => (item.id === id ? { ...item, allowed: !item.allowed } : item)));
  const toggleCharacter = (id: string) =>
    setCharacters((prev) => prev.map((item) => (item.id === id ? { ...item, allowed: !item.allowed } : item)));
  const toggleLibido = (id: string) =>
    setCharacters((prev) => prev.map((item) => (item.id === id ? { ...item, participates: !item.participates } : item)));

  const worldviews = 0;

  return (
    <PageShell>
      <PageHeader title="存档高级设置" onBack={back} backText="< 返回" />

      <div className="screen" style={{ maxWidth: 900 }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-1)' }}>
          <p style={{ ...CAPTION, margin: 0 }}>
            这些白名单只属于这一份存档：换档就换一套。插件与工具被关掉时不会执行；角色白名单决定 AI 能在这份存档里让谁出场。
          </p>
        </div>

        <Card>
          <Row
            title="允许全部插件"
            desc="关掉后，只有下面勾选的插件能在这份存档里跑"
            right={
              <GlassSwitch label="允许全部插件" checked={allowAllPlugins} onChange={setAllowAllPlugins} />
            }
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
            <p style={{ ...CAPTION, margin: 0 }}>还没有装插件。插件中心可以从 zip / 文件夹安装，也可以让工坊写一个。</p>
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
            {characters.map((character) => (
              <div key={character.id}>
                <Sep />
                <Row
                  title={character.name}
                  onClick={() => setStatus(`${character.name} 成了这一档的主角。`)}
                />
              </div>
            ))}
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
                      checked={character.allowed}
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
                  <GlassSwitch label="世界观改动跨存档生效" checked={worldviewCrossSave} onChange={setWorldviewCrossSave} />
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
                  desc={character.libidoHint}
                  right={
                    <GlassSwitch
                      label={character.name}
                      checked={character.participates}
                      onChange={() => toggleLibido(character.id)}
                      disabled
                    />
                  }
                />
              </div>
            ))}
            {characters.length === 0 ? <p style={{ ...CAPTION, margin: 0 }}>角色库里还没有别的角色。</p> : null}
          </Card>
        </Section>

        <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-signal)', lineHeight: 1.6 }}>{status}</span>
        <div>
          <SignalButton
            onClick={() =>
              setStatus('已保存。下次进入这个存档时生效；AI 安装插件的开关同样跟着存档走。')
            }
          >
            保存这份存档的规则
          </SignalButton>
        </div>
      </div>
    </PageShell>
  );
}
