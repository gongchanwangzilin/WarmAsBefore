import { useCallback, useEffect, useState } from 'react';
import { GlassButton } from '../../components/GlassButton';
import { PageShell } from '../../components/PageShell';
import { SignalButton } from '../../components/SignalButton';
import { Card, Chip, Field, PageHeader, Sep } from '../../components/kit';
import type { ScreenProps } from '../types';

/**
 * 插件中心：Views/Settings/PluginCenterPage.xaml（314 行）+ ViewModels/Settings/PluginCenterViewModel.cs。
 * 已装插件列表来自 plugins.list（WebBridge.PluginsListAsync）：清单、启停、权限声明与缺依赖。
 */

/** PluginHost.PluginsDir 在 Windows 上是应用数据目录下的 plugins；Web 端拿不到这条路径，留成占位。 */
const PLUGINS_DIR_LABEL = '<应用数据目录>\\plugins';

const CAPTION = { fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)' } as const;
const SIGNAL = { ...CAPTION, color: 'var(--w-signal)' } as const;

type PluginRow = {
  id: string;
  name: string;
  version: string;
  description: string;
  author: string;
  enabled: boolean;
  permissions: string[];
  settingsIssue: string;
  missing: string[];
};

/** PluginRow.StateLabel：有设置问题就「不能运行」，否则按启停。 */
const stateOf = (plugin: PluginRow) =>
  plugin.settingsIssue.length > 0 ? '不能运行' : plugin.enabled ? '生效中' : '已停用';

/** PluginRow.PermissionLabel：声明了几项、都是哪几项。 */
const permissionTextOf = (plugin: PluginRow) =>
  `权限：声明 ${plugin.permissions.length} 项` +
  (plugin.permissions.length > 0 ? ` · ${plugin.permissions.join('、')}` : '');

export function PluginCenter({ bridge, navigate, back }: ScreenProps) {
  const [plugins, setPlugins] = useState<PluginRow[]>([]);
  const [workshopPrompt, setWorkshopPrompt] = useState('');
  const [mcpPrompt, setMcpPrompt] = useState('');
  const [status, setStatus] = useState('');

  const load = useCallback(async () => {
    try {
      const { plugins: rows } = await bridge.request('plugins.list', {});
      setPlugins(rows);
      const running = rows.filter((row) => row.enabled && row.settingsIssue.length === 0).length;
      setStatus(rows.length === 0 ? '还没有插件。' : `共 ${rows.length} 个插件，${running} 个正在生效。`);
    } catch {
      /* 老宿主没有这个方法：列表留空，走源码的空态 */
    }
  }, [bridge]);

  useEffect(() => {
    void load();
  }, [load]);

  const notWired = () => setStatus('安装与工坊还没有接通写入：这一页只显示已经装好的插件。');

  return (
    <PageShell>
      <PageHeader
        title="插件中心"
        onBack={back}
        backText="< 返回"
        right={
          <GlassButton size="sm" onClick={() => void load()}>
            刷新
          </GlassButton>
        }
      />

      <div className="screen" style={{ maxWidth: '1000px' }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
          {status ? <span style={SIGNAL}>{status}</span> : null}
          <span style={{ ...CAPTION, lineHeight: 1.6 }}>
            插件可以改人设、改写发送前/回复后的文本、读取上下文、按轮次记账，也能带自己的设置页与页面。钩子运行在受限 JS
            沙箱里（语句数与内存有硬上限，等授权的时间不计入预算）；设置页三选一是硬性要求，没声明就不能运行。每项能力都要两边点头才生效：插件在
            plugin.json 里声明过，你在「权限」里放行——没声明的允许了也不给，声明了但被禁止的一律拦下。
          </span>
        </div>

        <Card title="安装插件">
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
            <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 'var(--w-space-3)' }}>
              <SignalButton onClick={notWired}>从 zip 安装</SignalButton>
              <GlassButton size="md" onClick={notWired}>
                从文件夹安装
              </GlassButton>
              <GlassButton size="md" onClick={notWired}>
                打开插件目录
              </GlassButton>
            </div>
            <span style={{ ...CAPTION, lineHeight: 1.6 }}>
              zip 的根目录、或根目录里那层同名文件夹里，都要有一个
              plugin.json（写 name、settings 三选一、hooks 指向包内的 JS）。同名插件会被直接覆盖，不会另开一份。
            </span>
            <span style={{ ...CAPTION, lineHeight: 1.6 }}>
              插件装在 {PLUGINS_DIR_LABEL} —— 直接拷进这个目录再点右上角「刷新」也一样装得上。
            </span>
          </div>
        </Card>

        {plugins.length === 0 ? (
          <span style={{ ...CAPTION, lineHeight: 1.6 }}>
            还没有插件。可以用下面的工坊生成一个，或用上面那两颗按钮装现成的。
          </span>
        ) : null}

        {plugins.map((plugin) => (
          <Card key={plugin.id}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-2)' }}>
              <div style={{ display: 'flex', alignItems: 'flex-start', gap: 'var(--w-space-3)' }}>
                <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: '2px' }}>
                  <span
                    style={{
                      fontSize: 'var(--w-size-subhead)',
                      color: 'var(--w-ink-display)',
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap',
                    }}
                  >
                    {plugin.name}
                  </span>
                  <span style={{ ...CAPTION, lineHeight: 1.6 }}>{plugin.description}</span>
                </div>
                <Chip>{stateOf(plugin)}</Chip>
              </div>

              <span style={CAPTION}>
                v{plugin.version}
                {plugin.author ? ` · ${plugin.author}` : ''}
              </span>

              {plugin.missing.length > 0 ? (
                <span style={SIGNAL}>缺少依赖：{plugin.missing.join('、')}</span>
              ) : null}

              {plugin.settingsIssue.length > 0 ? <span style={SIGNAL}>{plugin.settingsIssue}</span> : null}

              <span style={CAPTION}>{permissionTextOf(plugin)}</span>

              <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 'var(--w-space-2)' }}>
                <GlassButton
                  size="sm"
                  onClick={() =>
                    navigate(
                      `/pluginSettings?id=${encodeURIComponent(plugin.id)}&desc=${encodeURIComponent(plugin.description)}`,
                    )
                  }
                >
                  设置
                </GlassButton>
                <GlassButton
                  size="sm"
                  onClick={() =>
                    navigate(
                      `/pluginPerms?name=${encodeURIComponent(plugin.name)}&version=${encodeURIComponent(plugin.version)}`,
                    )
                  }
                >
                  权限
                </GlassButton>
              </div>
            </div>
          </Card>
        ))}

        <Card title="插件工坊">
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
            <span style={{ ...CAPTION, lineHeight: 1.6 }}>
              用一句话说你要什么，AI 会写出清单、设置项和 JS 钩子，并直接安装。装完可以点「试跑」；坏了就到「日志」看原因，不影响主程序。
            </span>
            <Field
              value={workshopPrompt}
              onChange={setWorkshopPrompt}
              multiline
              label="插件工坊"
              placeholder="例：让 TA 在回复末尾偶尔加一句极简的环境描写；给一个「克制程度」滑杆"
            />
            <div>
              <SignalButton onClick={notWired}>生成并安装</SignalButton>
            </div>
            <span style={{ ...CAPTION, lineHeight: 1.6, opacity: 0.8 }}>
              插件能做什么：README 里的
              plugin.json 清单（hooks 指向 JS 文件）+ 沙箱运行时。它们可以互相依赖；缺依赖且给了安装地址时，上面的「安装地址」会直接打开。深度改写仍受权限与沙箱约束——这是有意为之：插件不该能悄悄替换主程序。
            </span>
          </div>
        </Card>

        <Card title="MCP 工坊">
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
            <span style={{ ...CAPTION, lineHeight: 1.6 }}>
              用一句话描述你要的外部能力，AI 会生成一个 MCP 数据包（服务器 + 若干工具），直接注册并试跑。本地工具跑在受限 JS
              沙箱里；只有给了远程地址的工具才会走 HTTP。装好后 AI 用 mcp_list / mcp_call 调用。
            </span>
            <Field
              value={mcpPrompt}
              onChange={setMcpPrompt}
              multiline
              label="MCP 工坊"
              placeholder="例：做一个「情绪色彩」工具，把一句话映射成一种颜色和一句环境描写；再做「随机小剧场」给三个开场"
            />
            <div>
              <SignalButton onClick={notWired}>生成并注册</SignalButton>
            </div>

            <Sep bleed />

            <span style={{ fontSize: 'var(--w-size-subhead)', color: 'var(--w-ink-display)' }}>已装 MCP 数据包</span>
            <span style={{ ...CAPTION, lineHeight: 1.6 }}>
              数据包＝自己写的
              tools.json（本地 JS 工具或远程 HTTP 端点），是往机器里装东西，所以和插件放在一起。要接的是外部 MCP
              服务（本机启动进程或连一个地址），去设置 → 外部与接入 → MCP。
            </span>
            <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 'var(--w-space-3)' }}>
              <GlassButton size="sm" onClick={notWired}>
                导入数据包 zip
              </GlassButton>
              <GlassButton size="sm" onClick={notWired}>
                从文件夹导入
              </GlassButton>
              <GlassButton size="sm" onClick={notWired}>
                从 GitHub 导入
              </GlassButton>
            </div>
            <span style={{ ...CAPTION, lineHeight: 1.6 }}>
              还没有 MCP 数据包。可以在上面用一句话生成，或用这三颗按钮导入现成的。
            </span>
          </div>
        </Card>
      </div>
    </PageShell>
  );
}
