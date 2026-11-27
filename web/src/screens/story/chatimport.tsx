import { useState } from 'react';
import { Card, Field, PageHeader, TextBtn } from '../../components/kit';
import { PageShell } from '../../components/PageShell';
import { SignalButton } from '../../components/SignalButton';
import type { ScreenProps } from '../types';
import { Actions, FootBar, Lead, Note, Stack } from './parts';

type SourceRow = { name: string; detail: string };

/**
 * 导入聊天记录：ChatImportPage.xaml 的 01 · PAST 与 02 · READ。
 * 读文件、嗅探格式、算关系都在 ChatLogImportService 里，前端没有对应 method，
 * 所以这里只保留两块输入与源码里的空态错误；结果区在拿到结果前不出现。
 */
export function Chatimport({ navigate, back }: ScreenProps) {
  const [pasted, setPasted] = useState('');
  const [sources] = useState<SourceRow[]>([]);
  const [status, setStatus] = useState('');
  const [error, setError] = useState('');
  const [hasResult] = useState(false);

  const characterDisplay = '这一局';
  const pasteCount = pasted.trim() ? `${pasted.trim().length} 字` : '';

  const parsePaste = () => {
    if (pasted.trim().length === 0) {
      setStatus('');
      setError('先粘贴一段聊天记录，再点解析。');
      return;
    }
    setError('');
  };

  return (
    <PageShell>
      <PageHeader title="导入聊天记录" onBack={back} right={<span className="cap">{characterDisplay}</span>} />

      <div className="screen" style={{ maxWidth: 880 }}>
        <Stack gap={4}>
          <Lead
            eyebrow="01 · PAST"
            title="把你们的以前带进来"
            sub="可一次选多个文件，也可以直接粘一大段。格式靠内容认，不靠扩展名：微信/QQ 导出的 txt/mht、通用 txt、md、json、csv 都行。"
          />

          <Card>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-2)' }}>
              <Stack gap={1}>
                <span style={{ fontSize: 'var(--w-size-subhead)', color: 'var(--w-ink-display)' }}>从文件导入</span>
                <span className="cap">.txt / .mht / .md / .json / .csv，可多选；读不出的文件会单独说原因，不影响别的文件。</span>
              </Stack>
              <TextBtn>选择文件…</TextBtn>
            </div>
            {sources.map((row) => (
              <div key={row.name} style={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
                <span style={{ fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)' }}>{row.name}</span>
                <span className="cap">{row.detail}</span>
              </div>
            ))}
          </Card>

          <Card>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 'var(--w-space-2)' }}>
              <Stack gap={1}>
                <span style={{ fontSize: 'var(--w-size-subhead)', color: 'var(--w-ink-display)' }}>或者直接粘贴</span>
                <span className="cap">一整段聊天记录贴进来就行，几十万字也能读。</span>
              </Stack>
              <span className="cap">{pasteCount}</span>
            </div>
            <Field
              multiline
              label="粘贴的聊天记录"
              value={pasted}
              onChange={setPasted}
              placeholder={'2024-05-01 20:14:00 晚晚\n今天下雨了，你带伞了吗'}
            />
            <Actions>
              <SignalButton onClick={parsePaste}>解析粘贴的内容</SignalButton>
              <TextBtn onClick={() => { setPasted(''); setStatus(''); }}>清空</TextBtn>
            </Actions>
          </Card>

          {error ? <Note warn>{error}</Note> : null}
          {hasResult && status ? <Note>{status}</Note> : null}

          {hasResult ? (
            <Lead eyebrow="02 · READ" title="读出来的样子" />
          ) : null}
        </Stack>
      </div>

      {hasResult ? (
        <FootBar>
          <span className="cap">{status}</span>
          <SignalButton onClick={() => navigate('/newrun')}>带回这一局</SignalButton>
        </FootBar>
      ) : null}
    </PageShell>
  );
}
