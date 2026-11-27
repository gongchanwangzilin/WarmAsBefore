import { useState } from 'react';
import { GlassSwitch } from '../../components/GlassSwitch';
import { PageShell } from '../../components/PageShell';
import { SignalButton } from '../../components/SignalButton';
import { Field, PageHeader, Select } from '../../components/kit';
import type { ScreenProps } from '../types';

/**
 * 插件设置：Views/Settings/PluginSettingsPage.xaml（36 行）+ 同名 code-behind。
 * 表单由 plugin.json 的 settings.fields 现场生成；这里用一份示例清单演示四种控件。
 */

type PluginField = {
  key: string;
  label: string;
  type: 'text' | 'switch' | 'number' | 'select' | 'secret';
  default: string;
  help: string;
  options: string[];
};

const EXAMPLE_FIELDS: PluginField[] = [
  {
    key: 'greet',
    label: '问候语',
    type: 'text',
    default: '今天也要好好休息。',
    help: '回复开头加的那一句，留空就不加。',
    options: [],
  },
  {
    key: 'enabled',
    label: '启用钩子',
    type: 'switch',
    default: 'true',
    help: '关掉后钩子不执行，插件仍然留在列表里。',
    options: [],
  },
  {
    key: 'everyTurns',
    label: '轮次间隔',
    type: 'number',
    default: '8',
    help: '每多少轮做一次，太小会显得啰嗦。',
    options: [],
  },
  { key: 'tone', label: '语气', type: 'select', default: '温柔', help: '改写文本时的口吻。', options: ['温柔', '克制', '活泼'] },
];

const CAPTION = { fontSize: 'var(--w-size-caption)', color: 'var(--w-ink-muted)' } as const;
const FIELD_LABEL = { fontSize: 'var(--w-size-small)', color: 'var(--w-ink-body)' } as const;

export function PluginSettings({ query, back }: ScreenProps) {
  const pluginId = query.get('id') ?? '';
  const title = `${pluginId || '示例插件'} · 设置`;
  const description = query.get('desc') ?? '一份示例清单：文本、开关、数字、下拉各一，实际表单由 plugin.json 的 fields 现场生成。';

  const [values, setValues] = useState<Record<string, string>>(() =>
    Object.fromEntries(EXAMPLE_FIELDS.map((field) => [field.key, field.default])),
  );
  const [initial, setInitial] = useState<Record<string, string>>(values);
  const [status, setStatus] = useState('');

  const setValue = (key: string, next: string) => setValues((prev) => ({ ...prev, [key]: next }));

  const save = () => {
    const dirty = EXAMPLE_FIELDS.filter((field) => (values[field.key] ?? '') !== (initial[field.key] ?? ''));
    if (dirty.length === 0) {
      setStatus('没有改动，什么都不用写。');
      return;
    }
    setInitial({ ...values });
    setStatus('已保存，下一次用到钩子时生效。');
  };

  return (
    <PageShell>
      <PageHeader title={title} onBack={back} backText="< 返回" />

      <div className="screen screen--narrow">
        <span style={{ ...CAPTION, lineHeight: 1.6 }}>{description}</span>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-3)' }}>
          {EXAMPLE_FIELDS.map((field) => (
            <div key={field.key} style={{ display: 'flex', flexDirection: 'column', gap: 'var(--w-space-1)' }}>
              <span style={FIELD_LABEL}>{field.label || field.key}</span>
              {field.help ? <span style={CAPTION}>{field.help}</span> : null}
              {field.type === 'switch' ? (
                <GlassSwitch
                  label={field.label || field.key}
                  checked={values[field.key] === 'true' || values[field.key] === '1'}
                  onChange={(next) => setValue(field.key, next ? 'true' : 'false')}
                />
              ) : field.type === 'select' && field.options.length > 0 ? (
                <span>
                  <Select
                    label={field.label || field.key}
                    value={field.options.includes(values[field.key] ?? '') ? values[field.key] : field.options[0]}
                    options={field.options.map((option) => ({ value: option, label: option }))}
                    onChange={(next) => setValue(field.key, next)}
                  />
                </span>
              ) : (
                <Field
                  label={field.label || field.key}
                  value={values[field.key] ?? ''}
                  onChange={(next) => setValue(field.key, next)}
                  type={field.type === 'number' ? 'number' : 'text'}
                />
              )}
            </div>
          ))}
        </div>

        {status ? <span style={{ fontSize: 'var(--w-size-caption)', color: 'var(--w-signal)' }}>{status}</span> : null}

        <div>
          <SignalButton onClick={save}>保存设置</SignalButton>
        </div>
      </div>
    </PageShell>
  );
}
