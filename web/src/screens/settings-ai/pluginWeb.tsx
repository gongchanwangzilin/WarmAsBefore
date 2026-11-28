import { useState } from 'react';
import { PageShell } from '../../components/PageShell';
import { Empty, PageHeader, TextBtn } from '../../components/kit';
import type { ScreenProps } from '../types';

/**
 * 插件页面：Views/Settings/PluginWebPage.xaml（30 行）。
 * 原生版是一整块 WebView，页面由本机回环服务器提供；Web 端只画外框，联网加载交给宿主。
 */
export function PluginWeb({ query, back }: ScreenProps) {
  const title = query.get('title') || '插件页面';
  const url = query.get('url') ?? '';
  const [hint, setHint] = useState('');

  return (
    <PageShell>
      <PageHeader
        title={title}
        onBack={back}
        backText="< 返回"
        right={
          <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--w-space-2)' }}>
            <TextBtn onClick={() => setHint('外框这一层没有宿主 WebView，刷新不了页面；地址在下面。')}>刷新</TextBtn>
            <TextBtn onClick={() => setHint(url ? `宿主没有开浏览器的门，地址留给你手动打开：${url}` : '这条入口没带页面地址。')}>
              用浏览器打开
            </TextBtn>
          </div>
        }
      />

      <div className="screen screen--full" style={{ minHeight: 'min(60vh, 520px)' }}>
        <div
          style={{
            flex: 1,
            minHeight: 'clamp(180px, 34vh, 420px)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            padding: 'var(--w-pad-card)',
            borderRadius: 'var(--w-radius-card)',
            border: '1px solid var(--w-hairline)',
            background: 'var(--w-tint-card)',
          }}
        >
          <Empty
            title="插件页面由宿主 WebView 打开"
            reason="这是插件自带的本地网页，由 Windows 端的内嵌浏览器加载，地址指向本机回环服务。"
            hint="在桌面端会直接显示；浏览器这一层没有那套宿主，所以这里只有外框。"
          />
        </div>
        {hint ? (
          <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)', wordBreak: 'break-all' }}>
            {hint}
          </span>
        ) : null}
      </div>
    </PageShell>
  );
}
